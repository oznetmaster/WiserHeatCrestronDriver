// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed partial record Settings
		{
		public bool AllowGatewayAwayControl { get; init; }
		}

	[Test, Category ("LiveControl")]
	public async Task GatewayAwayChangesHubStateAndRestoresOriginal ()
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		if (!_settings!.AllowGatewayAwayControl) Assert.Ignore ("Enable AllowGatewayAwayControl to temporarily change whole-house Away mode.");
		if (_settings.Rooms.Length != 1 || string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("One bound child and absolute private hub settings are required to verify the gateway's physical hub.");
		var hub = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing private hub settings.");
		if (string.IsNullOrWhiteSpace (hub.HubHost) || string.IsNullOrWhiteSpace (hub.Secret)) throw new InvalidDataException ("Incomplete private hub settings.");
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", hub.Secret);
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (6));
		var gateway = await ReadGatewayAsync (timeout.Token);
		string check = "wiser.gateway-away";
		var session = new GatewayAwaySession (this, hub, http, _settings.Rooms[0], gateway, check);
		GatewayAwayResult? result = null;
		Exception? failure = null;
		try
			{
			await _navigation!.RestoreHomeAsync (timeout.Token);
			void Home (AndroidHierarchy hierarchy) => CrestronHomePages.RequireHome (hierarchy, _session!.Context.Profile.ExpectedHomeText);
			var tile = new AndroidSelector (AndroidSelectorKind.Text, gateway.Name!) { AncestorResourceId = CrestronHomePages.ResourcePrefix + "fragmentHomeContainer" };
			await session.RecordAsync ("open-intent", new { gateway.Id, gateway.Name });
			await _session!.Device.TapAsync (tile, hierarchy =>
				{
				Home (hierarchy);
				if (hierarchy.RequireUnique (tile).ResourceId != CrestronHomePages.ResourcePrefix + "titleSubtitle_title")
					throw new InvalidDataException ("The selected text is not a unique gateway tile.");
				}, timeout.Token);
			await session.WaitForPageAsync (timeout.Token);
			result = await GatewayAwayCycle.RunAsync (session, TimeSpan.FromSeconds (55), timeout.Token);
			_roomStatePreserved = result.RestorationConfirmed;
			}
		catch (Exception error) { failure = error; throw; }
		finally
			{
			using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
			try
				{
				var hierarchy = await _session!.Device.CaptureAsync (cleanup.Token);
				bool home = false;
				try { CrestronHomePages.RequireHome (hierarchy, _session.Context.Profile.ExpectedHomeText); home = true; }
				catch (InvalidOperationException) { }
				if (!home)
					{
					await session.RecordAsync ("close-intent", new { Recovery = failure != null || result?.Passed != true });
					await _session.Device.TapAsync (CrestronHomePages.Resource ("customdevices_toolbarClose"), GatewayAwaySession.Page, cleanup.Token);
					}
				await _navigation!.RestoreHomeAsync (cleanup.Token);
				await _session.CaptureAsync (check + ".home-restored", h => CrestronHomePages.RequireHome (h, _session.Context.Profile.ExpectedHomeText), cleanup.Token);
				await session.RecordAsync ("result", new { Result = result, HomeRestored = _navigation.HomeRestored });
				}
			catch (Exception cleanupFailure)
				{
				_roomStatePreserved = false;
				try { await session.RecordAsync ("navigation-recovery-required", new { Exception = cleanupFailure.ToString () }); } catch { }
				if (failure == null) throw;
				}
			}
		Assert.That (result?.Passed, Is.True, result?.Detail);
		}

	private sealed class GatewayAwaySession (GatewayUiTests fixture, HubSettings hub, HttpClient http, RoomBinding binding,
		DeviceInfo gateway, string check) : IGatewayAwaySession
		{
		private AndroidWorkflowSession Session => fixture._session!;
		private int _taps;
		public static void Page (AndroidHierarchy hierarchy) => CrestronHomePages.RequireExtensionPage (hierarchy, "Wiser Heat Options");
		public async Task WaitForPageAsync (CancellationToken token)
			{
			while (true)
				{
				var hierarchy = await Session.Device.CaptureAsync (token);
				try { Page (hierarchy); return; }
				catch (InvalidOperationException) { await Task.Delay (250, token); }
				}
			}
		public async Task RecordAsync (string phase, object value)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			if (phase is "changed" or "restored")
				{
				using var captureTimeout = new CancellationTokenSource (TimeSpan.FromSeconds (25));
				bool enabled = JsonSerializer.SerializeToElement (value).GetProperty ("Snapshot").GetProperty ("HomeAway").GetBoolean ();
				await Session.CaptureAsync (check + "." + phase, hierarchy =>
					{
					Page (hierarchy);
					var row = CrestronHomePages.ReadStatusAndButton (hierarchy, "Away Mode");
					if (!row.Enabled || row.Action != (enabled ? "Disable Away" : "Enable Away") ||
						!string.Equals (row.Status, enabled ? "Enabled" : "Disabled", StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException ("The gateway UI changed before its evidence capture.");
					}, captureTimeout.Token);
				}
			string directory = Path.Combine (Session.Context.EvidenceDirectory, check + ".records");
			Directory.CreateDirectory (directory);
			await using var file = new FileStream (Path.Combine (directory, phase + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			await JsonSerializer.SerializeAsync (file, new { Session.Context.RunId, Session.Context.PackageSha256, Phase = phase, ObservedUtc = DateTimeOffset.UtcNow, Value = value });
			file.Flush (true);
			if (phase == "change-intent") fixture._roomStatePreserved = false;
			}
		private async Task<JsonElement> ReadHubAsync (string group, CancellationToken token)
			{
			using var response = await http.GetAsync ("http://" + hub.HubHost + "/data/v2/" + group + "/", token);
			response.EnsureSuccessStatusCode ();
			using var document = JsonDocument.Parse (await response.Content.ReadAsStringAsync (token));
			return document.RootElement.Clone ();
			}
		public async Task<GatewayAwaySnapshot> ReadAsync (CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			var room = await fixture.ReadRoomAsync (binding, token);
			string physical = hub.HubHost.Trim ().ToLowerInvariant ();
			if (room.ParentDeviceId != gateway.Id || !room.PropertyValues["controlDeviceId"].GetString ()!.StartsWith (physical + "/room/", StringComparison.Ordinal))
				throw new InvalidDataException ("The bound child does not verify the selected gateway's physical hub.");
			var before = await fixture.ReadGatewayAsync (token);
			var domain = await ReadHubAsync ("domain", token);
			var schedules = await ReadHubAsync ("schedules", token);
			var after = await fixture.ReadGatewayAsync (token);
			if (after.Id != gateway.Id || after.Name != gateway.Name || before.PropertyValues["driverLifetimeId"].GetString () != after.PropertyValues["driverLifetimeId"].GetString () ||
				!after.PropertyValues["awayModeVisible"].GetBoolean ()) throw new InvalidDataException ("Gateway identity or configured Away capability changed.");
			var hierarchy = await Session.Device.CaptureAsync (token);
			Page (hierarchy);
			var row = CrestronHomePages.ReadStatusAndButton (hierarchy, "Away Mode");
			bool enabled = after.PropertyValues["awayModeIsEnabled"].GetBoolean ();
			bool agrees = string.Equals (row.Status, enabled ? "Enabled" : "Disabled", StringComparison.OrdinalIgnoreCase) &&
				row.Action == (enabled ? "Disable Away" : "Enable Away");
			return new (physical, after.PropertyValues["driverLifetimeId"].GetString ()!,
				DateTimeOffset.Parse (after.PropertyValues["lastHubRefreshUtc"].GetString ()!, CultureInfo.InvariantCulture),
				new (schedules, domain), enabled, agrees && row.Enabled && after.PropertyValues["awayModeActionEnabled"].GetBoolean ());
			}
		public async Task SetAwayAsync (bool enabled, bool recovery, CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			if (_taps >= 2) throw new InvalidOperationException ("Gateway control inputs cannot be replayed.");
			string action = enabled ? "Enable Away" : "Disable Away";
			var selector = new AndroidSelector (AndroidSelectorKind.Text, action);
			void Guard (AndroidHierarchy hierarchy)
				{
				Page (hierarchy);
				var row = CrestronHomePages.ReadStatusAndButton (hierarchy, "Away Mode");
				if (!row.Enabled || row.Action != action || !string.Equals (row.Status, enabled ? "Disabled" : "Enabled", StringComparison.OrdinalIgnoreCase) ||
					hierarchy.RequireUnique (selector).ResourceId != CrestronHomePages.ResourcePrefix + "customdevice_statusAndButtonAction")
					throw new InvalidDataException ("The labelled Away action no longer matches the intended input.");
				}
			await Session.CaptureAsync (check + ".input-" + (_taps + 1), Guard, token);
			await RecordAsync ("tap-" + (++_taps) + "-intent", new { Enabled = enabled, Recovery = recovery });
			await Session.Device.TapAsync (selector, Guard, token);
			}
		}
	}