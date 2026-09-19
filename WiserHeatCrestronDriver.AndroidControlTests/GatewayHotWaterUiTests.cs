// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
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
		public bool AllowGatewayHotWaterControl { get; init; }
		}

	[Test, Category ("LiveControl")]
	public Task GatewayHotWaterChangesBothStatesAndRestoresOriginalPolicy () => RunGatewayHotWaterAsync (requirePeer: false);
	[Test, Category ("LiveControl"), Category ("MultipleInstance")]
	public Task GatewayHotWaterUpdatesBothInstancesAndRestoresOriginalPolicy () => RunGatewayHotWaterAsync (requirePeer: true);
	private async Task RunGatewayHotWaterAsync (bool requirePeer)
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		if (!_settings!.AllowGatewayHotWaterControl) Assert.Ignore ("Enable AllowGatewayHotWaterControl to temporarily operate whole-house hot water.");
		if (requirePeer && !_settings.ObservePeerDuringGatewayControls) Assert.Ignore ("Enable ObservePeerDuringGatewayControls for this required two-instance case.");
		if (_settings.Rooms.Length != 1 || string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("One bound child and absolute private hub settings are required to verify the gateway's physical hub.");
		var hub = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing private hub settings.");
		if (string.IsNullOrWhiteSpace (hub.HubHost) || string.IsNullOrWhiteSpace (hub.Secret)) throw new InvalidDataException ("Incomplete private hub settings.");
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", hub.Secret);
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (6));
		var gateway = await ReadGatewayAsync (timeout.Token);
		string check = requirePeer ? "wiser.gateway-hot-water-two-instances" : "wiser.gateway-hot-water";
		var session = new GatewayHotWaterSession (this, hub, http, _settings.Rooms[0], gateway, check);
		await using var peer = await GatewayPeerControlObserver.OpenAsync (this, hub, check, timeout.Token);
		bool controlStarted = false;
		HotWaterControlResult? result = null;
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
			controlStarted = true;
			result = await HotWaterControlCycle.RunAsync (session, TimeSpan.FromSeconds (90), timeout.Token, peer);
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
					await _session.Device.TapAsync (CrestronHomePages.Resource ("customdevices_toolbarClose"), GatewayHotWaterSession.Page, cleanup.Token);
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
			finally
				{
				if (peer != null) await peer.CompleteAsync (!controlStarted || result?.RestorationConfirmed == true,
					failure != null || result?.Passed != true || !_roomStatePreserved);
				}
			}
		Assert.That (result?.Passed, Is.True, result?.Detail);
		}

	private sealed class GatewayHotWaterSession (GatewayUiTests fixture, HubSettings hub, HttpClient http, RoomBinding binding,
		DeviceInfo gateway, string check) : IHotWaterControlSession
		{
		private AndroidWorkflowSession Session => fixture._session!;
		private int _taps;
		private HotWaterRestorePlan? _restorePlan;
		private HotWaterControlSnapshot? _original;
		private readonly HashSet<int> _restoreAttempts = [];
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
			if (phase.StartsWith ("ui-", StringComparison.Ordinal) && phase.EndsWith ("-observed", StringComparison.Ordinal))
				{
				using var captureTimeout = new CancellationTokenSource (TimeSpan.FromSeconds (25));
				bool enabled = JsonSerializer.SerializeToElement (value).GetProperty ("Snapshot").GetProperty ("HomeOn").GetBoolean ();
				await Session.CaptureAsync (check + "." + phase, hierarchy =>
					{
					Page (hierarchy);
					var row = CrestronHomePages.ReadStatusAndButton (hierarchy, "Hot Water");
					if (!row.Enabled || row.Action != (enabled ? "Turn Off" : "Turn On") ||
						!string.Equals (row.Status, enabled ? "On" : "Off", StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException ("The hot-water UI changed before its evidence capture.");
					}, captureTimeout.Token);
				}
			string directory = Path.Combine (Session.Context.EvidenceDirectory, check + ".records");
			Directory.CreateDirectory (directory);
			await using var file = new FileStream (Path.Combine (directory, phase + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			await JsonSerializer.SerializeAsync (file, new { Session.Context.RunId, Session.Context.PackageSha256, Phase = phase, ObservedUtc = DateTimeOffset.UtcNow, Value = value });
			file.Flush (true);
			if (phase == "ui-1-intent") fixture._roomStatePreserved = false;
			}
		private async Task<JsonElement> ReadHubAsync (string group, CancellationToken token)
			{
			using var response = await http.GetAsync ("http://" + hub.HubHost + "/data/v2/" + group + "/", token);
			response.EnsureSuccessStatusCode ();
			using var document = JsonDocument.Parse (await response.Content.ReadAsStringAsync (token));
			return document.RootElement.Clone ();
			}
		public async Task<HotWaterControlSnapshot> ReadAsync (CancellationToken token)
			{
			var snapshot = await ReadForRecoveryAsync (token);
			var hierarchy = await Session.Device.CaptureAsync (token);
			Page (hierarchy);
			var row = CrestronHomePages.ReadStatusAndButton (hierarchy, "Hot Water");
			bool agrees = string.Equals (row.Status, snapshot.HomeOn ? "On" : "Off", StringComparison.OrdinalIgnoreCase) &&
				row.Action == (snapshot.HomeOn ? "Turn Off" : "Turn On");
			return snapshot with { ActionEnabled = snapshot.ActionEnabled && agrees && row.Enabled };
			}
		public async Task VerifyRestoredUiAsync (HotWaterControlSnapshot snapshot, CancellationToken token)
			{
			await Session.CaptureAsync (check + ".restored", hierarchy =>
				{
				Page (hierarchy);
				var row = CrestronHomePages.ReadStatusAndButton (hierarchy, "Hot Water");
				if (!row.Enabled || row.Action != (snapshot.HomeOn ? "Turn Off" : "Turn On") ||
					!string.Equals (row.Status, snapshot.HomeOn ? "On" : "Off", StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException ("Hot-water policy was restored, but the UI does not agree.");
				}, token);
			await RecordAsync ("restored-ui-observed", new { Snapshot = snapshot });
			}
		public async Task<HotWaterControlSnapshot> ReadForRecoveryAsync (CancellationToken token)
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
				!after.PropertyValues["hotWaterVisible"].GetBoolean ()) throw new InvalidDataException ("Gateway identity or configured hot-water capability changed.");
			bool enabled = after.PropertyValues["hotWaterIsOn"].GetBoolean ();
			var snapshot = new HotWaterControlSnapshot (physical, after.PropertyValues["driverLifetimeId"].GetString ()!,
				SuccessfulRefreshSequence.ParseTimestamp (after.PropertyValues["lastHubRefreshUtc"].GetString ()!),
				new (schedules, domain), enabled, after.PropertyValues["hotWaterActionEnabled"].GetBoolean ());
			_restorePlan ??= HotWaterRestoration.Capture (domain);
			_original ??= snapshot;
			return snapshot;
			}
		public async Task RestoreAsync (int index, int controllerId, JsonElement request, HotWaterControlSnapshot expected, CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			if (_restorePlan == null || _original == null || controllerId != _restorePlan.Id || index < 0 || index >= _restorePlan.Requests.Length ||
				!JsonElement.DeepEquals (request, _restorePlan.Requests[index]) || _restoreAttempts.Contains (index))
				throw new InvalidDataException ("Compensation must match the captured plan and may not be repeated.");
			var current = await ReadForRecoveryAsync (token);
			HotWaterControlCycle.RequireGuarded (_original, current);
			HotWaterControlCycle.RequireRestorationUnchanged (expected, current);
			await RecordAsync ("restore-http-" + index + "-intent", new { ControllerId = controllerId, Request = request, Expected = expected, Snapshot = current });
			token.ThrowIfCancellationRequested ();
			_restoreAttempts.Add (index);
			using var message = new HttpRequestMessage (HttpMethod.Patch, "http://" + hub.HubHost + "/data/v2/domain/HotWater/" + controllerId.ToString (CultureInfo.InvariantCulture))
				{ Content = new StringContent (request.GetRawText (), Encoding.UTF8, "application/json") };
			// A missing response is reconciled by fresh reads in the cycle, never by repeating this request.
			using var response = await http.SendAsync (message, token);
			await RecordAsync ("restore-http-" + index + "-response", new { Status = (int)response.StatusCode });
			response.EnsureSuccessStatusCode ();
			}
		public async Task SetHotWaterAsync (bool enabled, CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			if (_taps >= 2) throw new InvalidOperationException ("Gateway control inputs cannot be replayed.");
			string action = enabled ? "Turn On" : "Turn Off";
			var selector = new AndroidSelector (AndroidSelectorKind.Text, action);
			void Guard (AndroidHierarchy hierarchy)
				{
				Page (hierarchy);
				var row = CrestronHomePages.ReadStatusAndButton (hierarchy, "Hot Water");
				if (!row.Enabled || row.Action != action || !string.Equals (row.Status, enabled ? "Off" : "On", StringComparison.OrdinalIgnoreCase) ||
					hierarchy.RequireUnique (selector).ResourceId != CrestronHomePages.ResourcePrefix + "customdevice_statusAndButtonAction")
					throw new InvalidDataException ("The labelled hot-water action no longer matches the intended input.");
				}
			await Session.CaptureAsync (check + ".input-" + (_taps + 1), Guard, token);
			await RecordAsync ("tap-" + (++_taps) + "-intent", new { Enabled = enabled });
			await Session.Device.TapAsync (selector, Guard, token);
			}
		}
	}