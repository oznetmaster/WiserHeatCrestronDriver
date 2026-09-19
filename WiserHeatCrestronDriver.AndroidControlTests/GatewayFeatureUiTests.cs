// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using CrestronHomeDevTools;
using CrestronHomeNUnit.Android;
using NUnit.Framework;
using WiserHeatCrestronDriver.ConfigurationProbe;
using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed partial record Settings
		{
		public bool AllowGatewayFeatureConfiguration { get; init; }
		}

	[Test, Category ("LiveConfiguration")]
	public async Task GatewayFeatureConfigurationsShowExpectedControlsAndRestoreOriginal ()
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		if (!_settings!.AllowGatewayFeatureConfiguration)
			Assert.Ignore ("Enable AllowGatewayFeatureConfiguration to cycle gateway feature settings and restore them.");
		if (string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("Absolute private hub settings are required for independent policy checks.");
		var hub = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing hub settings.");
		if (string.IsNullOrWhiteSpace (hub.HubHost) || string.IsNullOrWhiteSpace (hub.Secret)) throw new InvalidDataException ("Incomplete hub settings.");
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", hub.Secret);
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (15));
		_ = await ReadGatewayAsync (timeout.Token);
		var session = new GatewayFeatureSession (this, hub, http);
		var result = await GatewayFeatureCycle.RunAsync (session, TimeSpan.FromMinutes (6), timeout.Token);
		_roomStatePreserved = result.ConfigurationRestored;
		await session.RecordAsync ("features-result", result);
		Assert.That (result.ConfigurationRestored, Is.True, "Reconcile retained configuration evidence before releasing reservations.");
		Assert.That (result.Passed, Is.True, "Inspect the retained feature-cycle failure and restoration records.");
		Assert.That (result.VariantsObserved, Is.EqualTo (4));
		}

	private sealed class GatewayFeatureSession (GatewayUiTests fixture, HubSettings hub, HttpClient http) : IGatewayFeatureSession
		{
		private const string COMMAND = "cp.driverConfiguration:applyConfiguration";
		private GatewayFeatureObservation? _last;
		private GatewayAwaySnapshot? _physicalOriginal;
		private AndroidWorkflowSession Session => fixture._session!;
		private static string Hash (object value) => Convert.ToHexString (SHA256.HashData (Encoding.UTF8.GetBytes (JsonSerializer.Serialize (value))));

		public async Task<GatewayFeatureObservation> ReadAsync (CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			var device = await fixture._processor!.GetDeviceAsync (Session.Context.InstalledDriverId, token) ?? throw new InvalidDataException ("Gateway disappeared.");
			var config = await DriverConfigurationInspection.GetAsync (fixture._processor, device.Id, token);
			_ = ConfigurationCompatibility.Identity (config);
			var values = config.Items.ToDictionary (i => i.Id, i => i.CurrentValue!.Value.GetString ()!);
			if (config.IsReconfigurable != true || device.ParentDeviceId != -6 || config.DeviceId != device.Id ||
				config.Name != device.Name || config.Model != device.Model || config.Version != Session.Context.DriverVersion ||
				device.PropertyValues["cp.driverInformation:version"].GetString () != Session.Context.DriverVersion ||
				HashPeerBinding (values["_Host_"], values["HubSecret"]) != HashPeerBinding (hub.HubHost, hub.Secret) ||
				!device.Commands.Contains (COMMAND) || config.Items.Where (i => i.Id is "AllowAwayMode" or "EnableWholeHouseHotWater").Any (i => i.ReadOnly != false))
				throw new InvalidDataException ("Gateway identity, writable features or physical hub binding differs from the reserved candidate.");
			var flags = new GatewayFeatures (bool.Parse (values["EnableWholeHouseHotWater"]), bool.Parse (values["AllowAwayMode"]));
			var normalized = config with { Items = config.Items.Select (i => i.Id is "AllowAwayMode" or "EnableWholeHouseHotWater"
				? i with { CurrentValue = JsonSerializer.SerializeToElement ("false") } : i).ToArray () };
			bool ready = device.PropertyValues["readyIndicator:isReady"].GetBoolean () && device.PropertyValues["onlineIndicator:isOnline"].GetBoolean () &&
				device.PropertyValues["cp.driverConfiguration:driverLoadingStatus"].GetString () == "Loaded" &&
				device.PropertyValues["hotWaterVisible"].GetBoolean () == flags.HotWater && device.PropertyValues["awayModeVisible"].GetBoolean () == flags.Away &&
				(!flags.HotWater || device.PropertyValues["hotWaterActionEnabled"].GetBoolean ()) &&
				(!flags.Away || device.PropertyValues["awayModeActionEnabled"].GetBoolean ());
			return _last = new (flags, ConfigurationCompatibility.Identity (normalized), Hash (new { device.Id, device.Name, device.Model, device.LocationId,
				Version = config.Version, Lifetime = device.PropertyValues["driverLifetimeId"].GetString () }), ready);
			}

		public async Task ApplyAsync (GatewayFeatures features, CancellationToken token)
			{
			var previous = _last ?? throw new InvalidDataException ("Observe configuration before any input.");
			var current = await ReadAsync (token);
			if (current != previous || !current.Ready) throw new InvalidDataException ("Configuration changed before the requested input.");
			fixture._roomStatePreserved = false;
			var reply = await fixture._processor!.ExecuteDeviceCommandAsync (Session.Context.InstalledDriverId, COMMAND, new
				{
				configurationItemValues = new Dictionary<string, string>
					{
					["EnableWholeHouseHotWater"] = features.HotWater ? "true" : "false",
					["AllowAwayMode"] = features.Away ? "true" : "false"
					}, isoCulture = "en-GB"
				}, token);
			if (reply is { ValueKind: not JsonValueKind.Null } && (reply.Value.ValueKind != JsonValueKind.Array || reply.Value.GetArrayLength () != 0))
				throw new InvalidDataException ("Configuration was not accepted; the command will not be repeated.");
			}

		private async Task CheckPhysicalPolicyAsync (string phase, CancellationToken token)
			{
			async Task<JsonElement> Get (string group)
				{
				using var response = await http.GetAsync ("http://" + hub.HubHost + "/data/v2/" + group + "/", token);
				response.EnsureSuccessStatusCode ();
				using var document = JsonDocument.Parse (await response.Content.ReadAsStringAsync (token));
				return document.RootElement.Clone ();
				}
			var domain = await Get ("domain");
			var schedules = await Get ("schedules");
			var device = await fixture.ReadGatewayAsync (token);
			var observed = new GatewayAwaySnapshot (HashPeerBinding (hub.HubHost, hub.Secret), device.PropertyValues["driverLifetimeId"].GetString ()!,
				SuccessfulRefreshSequence.ParseTimestamp (device.PropertyValues["lastHubRefreshUtc"].GetString ()!), new (schedules, domain),
				device.PropertyValues["awayModeIsEnabled"].GetBoolean (), device.PropertyValues["awayModeActionEnabled"].GetBoolean ());
			_physicalOriginal ??= observed;
			GatewayAwayCycle.RequirePreserved (_physicalOriginal, observed);
			if (GatewayAwayCycle.IsAway (_physicalOriginal) != GatewayAwayCycle.IsAway (observed))
				throw new InvalidDataException ("Physical Away policy changed during display-feature configuration.");
			await RecordAsync (phase, observed);
			}

		public async Task InspectAsync (string phase, GatewayFeatures features, CancellationToken token)
			{
			await CheckPhysicalPolicyAsync (phase + "-physical-before", token);
			var before = await fixture.ReadGatewayAsync (token);
			await fixture._navigation!.InspectHomeExtensionAsync ("wiser." + phase, before.Name!, "Wiser Heat Options", hierarchy =>
				{
				foreach (var (label, prefix, visible) in new[] { ("Hot Water", "hotWater", features.HotWater), ("Away Mode", "awayMode", features.Away) })
					{
					if (!visible) { hierarchy.RequireAbsent (new (AndroidSelectorKind.Text, label)); continue; }
					var row = CrestronHomePages.ReadStatusAndButton (hierarchy, label);
					Assert.That (row.Status, Is.EqualTo (Label (before.PropertyValues[prefix + "StateLabel"])).IgnoreCase);
					Assert.That (row.Action, Is.EqualTo (Label (before.PropertyValues[prefix + "ActionLabel"])));
					Assert.That (row.Enabled, Is.EqualTo (before.PropertyValues[prefix + "ActionEnabled"].GetBoolean ()));
					}
				}, token);
			var after = await fixture.ReadGatewayAsync (token);
			foreach (string key in StateKeys) Assert.That (JsonElement.DeepEquals (before.PropertyValues[key], after.PropertyValues[key]), Is.True, key);
			await CheckPhysicalPolicyAsync (phase + "-physical-after", token);
			await RecordAsync (phase + "-ui", new
				{
				Features = features, before.Id,
				Before = StateKeys.ToDictionary (key => key, key => before.PropertyValues[key]),
				After = StateKeys.ToDictionary (key => key, key => after.PropertyValues[key]),
				fixture._navigation.HomeRestored, PhysicalPolicyPreserved = true
				});
			}

		public async Task RecordAsync (string phase, object value)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			string directory = Path.Combine (Session.Context.EvidenceDirectory, "wiser.gateway-features.records");
			Directory.CreateDirectory (directory);
			await using var file = new FileStream (Path.Combine (directory, phase + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			await JsonSerializer.SerializeAsync (file, new { Session.Context.RunId, Session.Context.PackageSha256, Phase = phase, ObservedUtc = DateTimeOffset.UtcNow, Value = value });
			file.Flush (true);
			}
		}
	}