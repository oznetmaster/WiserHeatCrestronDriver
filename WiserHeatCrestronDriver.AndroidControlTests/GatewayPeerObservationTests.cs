// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Net;
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
		public string? PeerObservationSettingsPath { get; init; }
		}
	private sealed record PeerObservationSettings (string Host, string UserName, string Password, string CertificateSha256,
		string SshFingerprint, int DeviceId, int LocationId, string Name, string CatalogueId, string PackagePath)
		{
		public PeerRoomBinding[] Rooms { get; init; } = [];
		}
	private sealed record PeerRoomBinding (int DeviceId, int LocationId, string Name, int HubRoomId);
	private PeerObservationSettings LoadPeerSettings (HubSettings hub)
		{
		if (string.IsNullOrWhiteSpace (_settings!.PeerObservationSettingsPath) || !Path.IsPathFullyQualified (_settings.PeerObservationSettingsPath))
			throw new InvalidDataException ("An absolute private peer settings path is required.");
		var peer = JsonSerializer.Deserialize<PeerObservationSettings> (File.ReadAllText (_settings.PeerObservationSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing peer settings.");
		var context = _session!.Context;
		AndroidWorkflowSession.VerifyContext (context);
		if (!IPAddress.TryParse (context.ProcessorAddress, out var primaryAddress) || !IPAddress.TryParse (peer.Host, out var peerAddress) ||
			primaryAddress.Equals (peerAddress) || string.IsNullOrWhiteSpace (peer.UserName) || string.IsNullOrWhiteSpace (peer.Password) ||
			peer.CertificateSha256?.Length != 64 || !peer.CertificateSha256.All (char.IsAsciiHexDigit) ||
			peer.CertificateSha256.Equals (_settings.CertificateSha256, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace (peer.SshFingerprint) ||
			peer.DeviceId <= 0 || peer.LocationId <= 0 || string.IsNullOrWhiteSpace (peer.Name) || string.IsNullOrWhiteSpace (peer.CatalogueId) ||
			!Path.IsPathFullyQualified (peer.PackagePath) || string.IsNullOrWhiteSpace (hub.HubHost) || string.IsNullOrWhiteSpace (hub.Secret))
			throw new InvalidDataException ("Two distinct pinned processors, an exact peer instance, a candidate package and a shared hub are required.");
		return peer;
		}

	[Test, Category ("LiveReadOnly")]
	public async Task TwoGatewayInstancesRefreshSharedHubWithoutChangingLocalConfiguration ()
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		if (string.IsNullOrWhiteSpace (_settings!.PeerObservationSettingsPath))
			Assert.Ignore ("Select a private PeerObservationSettingsPath to inspect a second reserved processor.");
		if (!Path.IsPathFullyQualified (_settings.PeerObservationSettingsPath!) ||
			string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("Absolute private peer and hub settings paths are required.");
		var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
		var hub = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), json)
			?? throw new InvalidDataException ("Missing hub settings.");
		var peer = LoadPeerSettings (hub);
		var context = _session!.Context;
		using var packageLock = new FileStream (peer.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (!Convert.ToHexString (SHA256.HashData (packageLock)).Equals (context.PackageSha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException ("Peer package differs from the active workflow candidate.");
		var package = DriverDeployment.Inspect (peer.PackagePath);
		if (package.DriverId != context.DriverGuid || package.Version != context.DriverVersion || package.Model != ConfigurationCompatibility.Model)
			throw new InvalidDataException ("Peer package identity differs from the active Wiser candidate.");
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (5));
		var credential = new NetworkCredential (peer.UserName, peer.Password);
		string owner = Guid.NewGuid ().ToString ("N");
		await RecordPeerAsync ("reservation-intent", new { context.RunId, Owner = owner, peer.Host, peer.DeviceId });
		using var lease = await ProcessorOperationLease.AcquireAsync (peer.Host, credential, peer.SshFingerprint, owner, timeout.Token);
		Exception? failure = null;
		try
			{
			await using var client = await ConfigurationClient.ConnectAsync (new () { Host = peer.Host, CertificateSha256 = peer.CertificateSha256 }, credential, timeout.Token);
			var catalogue = await client.GetDriverAsync (peer.CatalogueId, timeout.Token);
			if (catalogue == null || catalogue.Id != peer.CatalogueId || catalogue.Model != package.Model || catalogue.Manufacturer != package.Manufacturer ||
				catalogue.Developer != "Neil Colvin" || !Version.TryParse (catalogue.Version, out var catalogueVersion) || catalogueVersion != Version.Parse (context.DriverVersion))
				throw new InvalidDataException ("Peer catalogue does not identify the selected Wiser candidate.");
			var payload = await DriverPayloadInspection.CompareAsync (peer.Host, credential, peer.SshFingerprint, peer.PackagePath,
				context.PackageSha256, peer.CatalogueId, TimeSpan.FromMinutes (2), timeout.Token);
			await RecordPeerAsync ("payload-before", payload);
			string sharedBinding = HashPeerBinding (hub.HubHost, hub.Secret);
			var first = await ReadInstanceAsync (_processor!, context.InstalledDriverId, _settings.CertificateSha256, sharedBinding, timeout.Token);
			var second = await ReadInstanceAsync (client, peer.DeviceId, peer.CertificateSha256, sharedBinding, timeout.Token);
			if (second.Name != peer.Name || second.LocationId != peer.LocationId)
				throw new InvalidDataException ("Peer instance name or room does not match the explicit binding.");
			GatewayPairObservation.RequirePair (first, second);
			await RecordPeerAsync ("original", new { First = first, Second = second });
			using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
			http.DefaultRequestHeaders.Add ("SECRET", hub.Secret);
			async Task<(bool Away, bool HotWater)> ReadHub (string evidence, CancellationToken token)
				{
				using var response = await http.GetAsync ("http://" + hub.HubHost + "/data/v2/domain/", token);
				response.EnsureSuccessStatusCode ();
				using var document = JsonDocument.Parse (await response.Content.ReadAsStringAsync (token));
				await RecordPeerAsync (evidence, new { ObservedUtc = DateTimeOffset.UtcNow, Domain = document.RootElement });
				bool away = document.RootElement.GetProperty ("System").GetProperty ("OverrideType").GetString () switch
					{ "None" => false, "Away" => true, _ => throw new InvalidDataException ("Unsupported hub override.") };
				bool water = document.RootElement.GetProperty ("HotWater").EnumerateArray ().Single ().GetProperty ("WaterHeatingState").GetString () switch
					{ "On" => true, "Off" => false, _ => throw new InvalidDataException ("Unknown hot-water feedback.") };
				return (away, water);
				}
			using var convergence = CancellationTokenSource.CreateLinkedTokenSource (timeout.Token);
			convergence.CancelAfter (TimeSpan.FromSeconds (90));
			var lastFirst = first;
			var lastSecond = second;
			for (int sample = 1; ; sample++)
				{
				AndroidWorkflowSession.VerifyContext (context);
				var before = await ReadHub ($"sample-{sample}.hub-before", convergence.Token);
				var currentFirst = await ReadInstanceAsync (_processor!, first.DeviceId, _settings.CertificateSha256, sharedBinding, convergence.Token);
				var currentSecond = await ReadInstanceAsync (client, second.DeviceId, peer.CertificateSha256, sharedBinding, convergence.Token);
				var after = await ReadHub ($"sample-{sample}.hub-after", convergence.Token);
				GatewayPairObservation.RequirePreserved (lastFirst, currentFirst);
				GatewayPairObservation.RequirePreserved (lastSecond, currentSecond);
				lastFirst = currentFirst;
				lastSecond = currentSecond;
				bool matches = GatewayPairObservation.HasFreshSharedState (first, second, currentFirst, currentSecond, after.Away, after.HotWater);
				await RecordPeerAsync ($"sample-{sample}", new { First = currentFirst, Second = currentSecond, HubStable = before == after, Matches = matches });
				if (before == after && matches) break;
				await Task.Delay (TimeSpan.FromSeconds (2), convergence.Token);
				}
			await lease.VerifyAfterReconnectAsync (peer.Host, timeout.Token);
			payload = await DriverPayloadInspection.CompareAsync (peer.Host, credential, peer.SshFingerprint, peer.PackagePath,
				context.PackageSha256, peer.CatalogueId, TimeSpan.FromMinutes (2), timeout.Token);
			await RecordPeerAsync ("payload-after", payload);
			var finalFirst = await ReadInstanceAsync (_processor!, first.DeviceId, _settings.CertificateSha256, sharedBinding, timeout.Token);
			var finalSecond = await ReadInstanceAsync (client, second.DeviceId, peer.CertificateSha256, sharedBinding, timeout.Token);
			GatewayPairObservation.RequirePreserved (lastFirst, finalFirst);
			GatewayPairObservation.RequirePreserved (lastSecond, finalSecond);
			await RecordPeerAsync ("result", new { ObservationsPassed = true, PeerReservationReleaseRequired = true, First = finalFirst, Second = finalSecond,
				Scope = "Read-only shared-hub convergence and local-configuration preservation; no physical control-isolation or second-UI claim." });
			}
		catch (Exception error)
			{
			failure = error;
			try { await RecordPeerAsync ("failure", new { Type = error.GetType ().FullName }); } catch { }
			throw;
			}
		finally
			{
			// This fixture performs no peer device/configuration writes. Release only its own reservation.
			try
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (30));
				await lease.ReleaseAsync (cleanup.Token);
				await RecordPeerAsync ("reservation-released", new { Owner = owner, peer.Host });
				}
			catch (Exception cleanupFailure)
				{
				try { await RecordPeerAsync ("reservation-cleanup-failed", new { Type = cleanupFailure.GetType ().FullName, Owner = owner }); } catch { }
				if (failure == null) throw;
				}
			}
		}

	private static string HashPeerBinding (string host, string secret) => Convert.ToHexString (SHA256.HashData (
		Encoding.UTF8.GetBytes (JsonSerializer.Serialize (new[] { host.Trim ().ToLowerInvariant (), secret }))));

	private async Task<GatewayInstanceObservation> ReadInstanceAsync (ConfigurationClient client, int id, string processorIdentity,
		string sharedBinding, CancellationToken token, bool requireGatewayControls = true)
		{
		AndroidWorkflowSession.VerifyContext (_session!.Context);
		var before = await client.GetDeviceAsync (id, token) ?? throw new InvalidDataException ("Instance disappeared.");
		var config = await DriverConfigurationInspection.GetAsync (client, id, token);
		string configuration = ConfigurationCompatibility.Identity (config);
		var values = config.Items.ToDictionary (item => item.Id, item => item.CurrentValue!.Value.GetString ()!);
		if (HashPeerBinding (values["_Host_"], values["HubSecret"]) != sharedBinding)
			throw new InvalidDataException ("The instance configuration does not bind the independently observed hub.");
		var device = await client.GetDeviceAsync (id, token) ?? throw new InvalidDataException ("Instance disappeared.");
		string Text (string name) => device.PropertyValues[name].GetString ()!;
		if (device.Id != id || device.ParentDeviceId != -6 || device.Model != ConfigurationCompatibility.Model || device.Name != before.Name ||
			device.LocationId != before.LocationId || Text ("driverLifetimeId") != before.PropertyValues["driverLifetimeId"].GetString () ||
			Text ("cp.driverInformation:version") != _session.Context.DriverVersion || config.Version != _session.Context.DriverVersion ||
			Text ("cp.driverInformation:developer") != "Neil Colvin" || Text ("cp.driverInformation:controlType") != "tcpClient" ||
			Text ("cp.driverConfiguration:driverLoadingStatus") != "Loaded" || !device.PropertyValues["onlineIndicator:isOnline"].GetBoolean () ||
			requireGatewayControls && (!device.PropertyValues["awayModeVisible"].GetBoolean () || !device.PropertyValues["hotWaterVisible"].GetBoolean () ||
			!device.PropertyValues["awayModeActionEnabled"].GetBoolean () || !device.PropertyValues["hotWaterActionEnabled"].GetBoolean ()))
			throw new InvalidDataException ("Both gateways must remain loaded, online and idle with the observed capabilities enabled.");
		return new (processorIdentity.ToUpperInvariant (), id, device.LocationId ?? 0, device.Name ?? "", _session.Context.DriverVersion,
			_session.Context.PackageSha256.ToUpperInvariant (), configuration, sharedBinding, Guid.Parse (Text ("driverLifetimeId")),
			SuccessfulRefreshSequence.ParseTimestamp (Text ("lastHubRefreshUtc")), device.PropertyValues["awayModeIsEnabled"].GetBoolean (), device.PropertyValues["hotWaterIsOn"].GetBoolean ());
		}
	private async Task RecordPeerAsync (string phase, object value)
		{
		AndroidWorkflowSession.VerifyContext (_session!.Context);
		await using var output = new FileStream (Path.Combine (_session.Context.EvidenceDirectory, "wiser.peer." + phase + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
		await JsonSerializer.SerializeAsync (output, value);
		await output.FlushAsync ();
		output.Flush (flushToDisk: true);
		}
	}