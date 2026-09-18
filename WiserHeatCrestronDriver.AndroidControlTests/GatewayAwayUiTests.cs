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
		public bool AllowGatewayAwayControl { get; init; }
		public int GatewayAwayCycles { get; init; } = 1;
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
		if (_settings.GatewayAwayCycles is < 1 or > 3) throw new InvalidDataException ("GatewayAwayCycles must be between one and three.");
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (3 * _settings.GatewayAwayCycles + 3));
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
			result = await GatewayAwayRepetition.RunAsync (cycle =>
				new GatewayAwaySession (this, hub, http, _settings.Rooms[0], gateway, check + ".cycle-" + (cycle + 1)),
				_settings.GatewayAwayCycles, TimeSpan.FromSeconds (55), timeout.Token);
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
		private GatewayAwaySnapshot? _original;
		private bool _compensationAttempted;
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
			if (phase == "changed")
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
			var snapshot = await ReadForRecoveryAsync (token);
			var hierarchy = await Session.Device.CaptureAsync (token);
			Page (hierarchy);
			var row = CrestronHomePages.ReadStatusAndButton (hierarchy, "Away Mode");
			bool agrees = string.Equals (row.Status, snapshot.HomeAway ? "Enabled" : "Disabled", StringComparison.OrdinalIgnoreCase) &&
				row.Action == (snapshot.HomeAway ? "Disable Away" : "Enable Away");
			return snapshot with { ActionEnabled = snapshot.ActionEnabled && agrees && row.Enabled };
			}
		public async Task VerifyRestoredUiAsync (GatewayAwaySnapshot snapshot, CancellationToken token)
			{
			await Session.CaptureAsync (check + ".restored", hierarchy =>
				{
				Page (hierarchy);
				var row = CrestronHomePages.ReadStatusAndButton (hierarchy, "Away Mode");
				if (!row.Enabled || row.Action != (snapshot.HomeAway ? "Disable Away" : "Enable Away") ||
					!string.Equals (row.Status, snapshot.HomeAway ? "Enabled" : "Disabled", StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException ("Away state was restored, but the UI does not agree.");
				}, token);
			await RecordAsync ("restored-ui-observed", new { Snapshot = snapshot });
			}
		public async Task<GatewayAwaySnapshot> ReadForRecoveryAsync (CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			// This short cycle already keeps the gateway connection active; reuse it for room identity reads.
			var room = await fixture.ReadRoomAsync (fixture._processor!, binding, token);
			string physical = hub.HubHost.Trim ().ToLowerInvariant ();
			if (room.ParentDeviceId != gateway.Id || !room.PropertyValues["controlDeviceId"].GetString ()!.StartsWith (physical + "/room/", StringComparison.Ordinal))
				throw new InvalidDataException ("The bound child does not verify the selected gateway's physical hub.");
			var before = await fixture.ReadGatewayAsync (token);
			var domain = await ReadHubAsync ("domain", token);
			var schedules = await ReadHubAsync ("schedules", token);
			var after = await fixture.ReadGatewayAsync (token);
			if (after.Id != gateway.Id || after.Name != gateway.Name || before.PropertyValues["driverLifetimeId"].GetString () != after.PropertyValues["driverLifetimeId"].GetString () ||
				!after.PropertyValues["awayModeVisible"].GetBoolean ()) throw new InvalidDataException ("Gateway identity or configured Away capability changed.");
			bool enabled = after.PropertyValues["awayModeIsEnabled"].GetBoolean ();
			var snapshot = new GatewayAwaySnapshot (physical, after.PropertyValues["driverLifetimeId"].GetString ()!,
				SuccessfulRefreshSequence.ParseTimestamp (after.PropertyValues["lastHubRefreshUtc"].GetString ()!),
				new (schedules, domain), enabled, after.PropertyValues["awayModeActionEnabled"].GetBoolean ());
			_original ??= snapshot;
			return snapshot;
			}
		public async Task SetAwayAsync (bool enabled, bool recovery, CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			if (recovery)
				{
				if (_original == null || _taps != 1 || _compensationAttempted || enabled != GatewayAwayCycle.IsAway (_original))
					throw new InvalidDataException ("Away compensation must restore the original state once, after the first UI input only.");
				var current = await ReadForRecoveryAsync (token);
				GatewayAwayCycle.RequirePreserved (_original, current);
				if (!current.ActionEnabled || current.HomeAway == enabled || GatewayAwayCycle.IsAway (current) == enabled || current.RefreshUtc <= _original.RefreshUtc)
					throw new InvalidDataException ("The owned Away transition must be independently observed before compensation.");
				var request = new { RequestOverride = new { Type = enabled ? 2 : 0 } };
				await RecordAsync ("restore-http-intent", new { Enabled = enabled, Request = request, Snapshot = current });
				token.ThrowIfCancellationRequested ();
				_compensationAttempted = true;
				using var message = new HttpRequestMessage (HttpMethod.Patch, "http://" + hub.HubHost + "/data/v2/domain/System")
					{ Content = new StringContent (JsonSerializer.Serialize (request), Encoding.UTF8, "application/json") };
				using var response = await http.SendAsync (message, token);
				await RecordAsync ("restore-http-response", new { Status = (int)response.StatusCode });
				response.EnsureSuccessStatusCode ();
				return;
				}
			if (_compensationAttempted) throw new InvalidOperationException ("UI control cannot resume after compensation.");
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
			try { await Session.CaptureAsync (check + ".input-" + (_taps + 1), Guard, token); }
			catch (Exception failure) when (_taps == 1 && _original != null && enabled == GatewayAwayCycle.IsAway (_original))
				{
				// No returning tap has been issued: this is still the observation-only preflight.
				// Never use this fallback after entering TapAsync, whose outcome may be uncertain.
				await RecordAsync ("restore-ui-preflight-error", new { Exception = failure.ToString () });
				await SetAwayAsync (enabled, true, token);
				throw new InvalidOperationException ("The return UI input was not issued; independent compensation was attempted instead.", failure);
				}
			await RecordAsync ("tap-" + (++_taps) + "-intent", new { Enabled = enabled, Recovery = recovery });
			await Session.Device.TapAsync (selector, Guard, token);
			}
		}
	}