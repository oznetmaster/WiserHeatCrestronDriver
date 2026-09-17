// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

using WiserHeatApiV2;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed record ControlRoomBinding (int DeviceId, string HubRoomName, bool AllowManualTargetInitialization = false)
		{
		public string? ManagedAlias { get; init; }
		}
	partial void ResolveControlBindings (AndroidRunContext context)
		{
		_settings = _settings! with
			{
			ControlRooms = (_settings.ControlRooms ?? throw new InvalidDataException ("Control room settings are missing.")).Select (room =>
				room with { DeviceId = ResolveManagedRoomId (context, room.DeviceId, room.ManagedAlias) }).ToArray ()
			};
		}
	private sealed partial record Settings
		{
		public string? ControlHubSettingsPath
			{
			get; init;
			}
		public ControlRoomBinding[] ControlRooms { get; init; } = [];
		}
	private sealed record HubSettings (string HubHost, string Secret);

	[Test, Category ("LiveControl")]
	public async Task RoomScheduleControlChangesHubModeAndRestoresSchedule ()
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		var controls = _settings!.ControlRooms;
		if (controls == null || controls.Length == 0 || controls.Any (c => c == null || c.DeviceId <= 0 || string.IsNullOrWhiteSpace (c.HubRoomName)) ||
			 controls.Select (c => c.DeviceId).Distinct ().Count () != controls.Length ||
			 controls.Select (c => c.HubRoomName).Distinct (StringComparer.Ordinal).Count () != controls.Length ||
			 string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("The control project needs explicit ControlRooms and an absolute private ControlHubSettingsPath.");
		var settings = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath),
			 new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException ("Missing private hub settings.");
		if (string.IsNullOrWhiteSpace (settings.HubHost) || string.IsNullOrWhiteSpace (settings.Secret))
			throw new InvalidDataException ("Private hub settings are incomplete.");
		string host = settings.HubHost.Trim ().ToLowerInvariant ();
		using var hub = new WiserRestController (new WiserConnection (host, settings.Secret));
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", settings.Secret);
		foreach (var control in controls)
			{
			var binding = _settings.Rooms.Single (room => room.DeviceId == control.DeviceId);
			using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (5));
			var original = await ReadRoomAsync (binding, timeout.Token);
			string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture) + ".control";
			await _navigation!.InspectRoomExtensionPagesAsync (check, binding.RoomName, original.Name!, binding.PageTitle, async (pages, token) =>
			{
				await pages.OpenPageAsync (Text ("Open"), "Schedule", CrestronHomePages.Resource ("customdevices_toolbarClose"), token);
				var cycle = new RoomControlSession (this, hub, http, host, binding, control, original, check);
				var result = await ScheduleControlCycle.RunAsync (cycle, TimeSpan.FromSeconds (45), token, control.AllowManualTargetInitialization);
				_roomStatePreserved = result.RestorationConfirmed;
				await cycle.SaveResultAsync (result);
				Assert.That (result.Passed, Is.True, result.Detail);
			}, timeout.Token);
			Assert.That (_navigation.HomeRestored, Is.True);
			}
		}

	private sealed class RoomControlSession (GatewayUiTests fixture, WiserRestController hub, HttpClient http,
		 string host, RoomBinding binding, ControlRoomBinding control, DeviceInfo original, string check) : IScheduleControlSession
		{
		private AndroidWorkflowSession Session => fixture._session ?? throw new InvalidOperationException ("Android workflow is not active.");
		private string DirectoryPath => Path.Combine (Session.Context.EvidenceDirectory, check + ".records");
		private string[] Titles => [binding.PageTitle, "Schedule"];
		private ScheduleControlSnapshot? _captured;

		private static ScheduleActivity Activity (DeviceInfo device) =>
			 JsonSerializer.Deserialize<ScheduleActivity> (device.PropertyValues["controlStatus"].GetString ()!)
				  ?? throw new InvalidDataException ("The room did not report command activity.");

		public async Task<ScheduleControlSnapshot> ReadAsync (CancellationToken token)
			{
			while (true)
				{
				AndroidWorkflowSession.VerifyContext (Session.Context);
				var before = await fixture.ReadRoomAsync (binding, token);
				var domain = await hub.GetHubDataAsync ("http://" + host + "/data/v2/domain/", cancellationToken: token);
				var rooms = (List<Dictionary<string, object>>)domain["Room"];
				var selected = rooms.Single (room => room.TryGetValue ("Name", out var name) && name?.ToString () == control.HubRoomName);
				JsonElement room = JsonSerializer.SerializeToElement (selected);
				string identity = host + "/room/" + room.GetProperty ("id").GetInt32 ().ToString (CultureInfo.InvariantCulture);
				var after = await fixture.ReadRoomAsync (binding, token);
				foreach (var device in new[] { before, after })
					if (device.Id != original.Id || device.Name != original.Name || device.LocationId != original.LocationId ||
						 device.ParentDeviceId != original.ParentDeviceId || device.PropertyValues["controlDeviceId"].GetString () != identity ||
						 !device.Commands.Contains ("enableSchedule") || !device.Commands.Contains ("disableSchedule"))
						throw new InvalidDataException ("The Home room and independently observed hub room no longer match.");
				if (Activity (before) == Activity (after))
					return new (identity, Activity (after), room, after.PropertyValues["scheduleEnabled"].GetBoolean ());
				await Task.Delay (250, token);
				}
			}

		public async Task RecordAsync (string phase, ScheduleControlSnapshot snapshot)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			if (phase == "disable-intent")
				fixture._roomStatePreserved = false;
			Directory.CreateDirectory (DirectoryPath);
			await using var file = new FileStream (Path.Combine (DirectoryPath, phase + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			await JsonSerializer.SerializeAsync (file, new
				{
				Session.Context.RunId,
				Session.Context.PackageSha256,
				Session.Context.DriverVersion,
				binding.DeviceId,
				Phase = phase,
				RecordedUtc = DateTimeOffset.UtcNow,
				Snapshot = snapshot
				});
			file.Flush (flushToDisk: true);
			if (phase == "original")
				_captured = snapshot;
			}

		public async Task SaveResultAsync (ScheduleControlResult result)
			{
			Directory.CreateDirectory (DirectoryPath);
			await using var file = new FileStream (Path.Combine (DirectoryPath, "result.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			await JsonSerializer.SerializeAsync (file, result);
			file.Flush (flushToDisk: true);
			}

		public async Task SetAsync (bool enabled, bool recovery, CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			if (recovery)
				{
				if (!enabled)
					throw new InvalidOperationException ("Recovery may only restore the scheduled starting state.");
				// This is a single distinct compensation after the first command has been observed complete,
				// not a replay of an uncertain tap. Original inputs and this recovery remain in the journal.
				await fixture.ReadRoomAsync (binding, token);
				await using var client = await ConfigurationClient.ConnectAsync (new ()
					{
					Host = fixture._settings!.Host,
					CertificateSha256 = fixture._settings.CertificateSha256
					}, new NetworkCredential (fixture._settings.UserName, fixture._settings.Password), token);
				await client.ExecuteDeviceCommandAsync (binding.DeviceId, "extension:doCommand", new { commandName = "enableSchedule", args = Array.Empty<string> () }, token);
				return;
				}
			string action = enabled ? "Enable" : "Disable";
			var selector = Text (action);
			void Guard (AndroidHierarchy hierarchy)
				{
				AndroidWorkflowSession.VerifyContext (Session.Context);
				var front = CrestronHomeExtensionPages.RequirePage (hierarchy, Titles);
				var row = CrestronHomePages.ReadStatusAndButton (front, "Schedule Control");
				if (row.Action != action || !row.Enabled || !string.Equals (row.Status, enabled ? "Disabled" : "Enabled", StringComparison.OrdinalIgnoreCase) ||
					 hierarchy.RequireUnique (selector) != front.RequireUnique (selector))
					throw new InvalidOperationException ("The intended Schedule control is not uniquely available on the front page.");
				}
			await Session.CaptureAsync (check + (enabled ? ".enable-before" : ".disable-before"), Guard, token);
			await Session.Device.TapAsync (selector, Guard, token);
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			deadline.CancelAfter (TimeSpan.FromSeconds (25));
			void Verify (AndroidHierarchy hierarchy)
				{
				var row = CrestronHomePages.ReadStatusAndButton (CrestronHomeExtensionPages.RequirePage (hierarchy, Titles), "Schedule Control");
				if (row.Action != (enabled ? "Disable" : "Enable") || !row.Enabled ||
					 !string.Equals (row.Status, enabled ? "Enabled" : "Disabled", StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException ("The UI has not observed the requested schedule mode.");
				}
			while (true)
				{
				AndroidWorkflowSession.VerifyContext (Session.Context);
				var hierarchy = await Session.Device.CaptureAsync (deadline.Token);
				try
					{
					Verify (hierarchy);
					break;
					}
				catch (InvalidOperationException) { await Task.Delay (250, deadline.Token); }
				}
			await Session.CaptureAsync (check + (enabled ? ".enabled" : ".disabled"), Verify, token);
			}

		public async Task SetManualTargetAsync (int target, CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			var state = await ReadAsync (token);
			if (_captured == null || state.PhysicalIdentity != _captured.PhysicalIdentity ||
				 state.Activity.Epoch != _captured.Activity.Epoch || state.Activity.Completed != _captured.Activity.Completed + 1 ||
				 ScheduleObservation.ReadTransition (_captured.Room, state.Room) || state.HomeEnabled ||
				 state.Activity.Pending != 0 || target != _captured.Room.GetProperty ("ManualSetPoint").GetInt32 ())
				throw new InvalidDataException ("Restoring the stored target requires an idle room in Manual mode.");
			// ManualSetPoint is read-only on the hub. Its supported temperature command
			// restores the saved target while still Manual, before the UI returns to Auto.
			using var request = new HttpRequestMessage (HttpMethod.Patch, "http://" + host + "/data/v2/domain/Room/" + state.Room.GetProperty ("id").GetInt32 ().ToString (CultureInfo.InvariantCulture))
				{
				Content = new StringContent (JsonSerializer.Serialize (new { RequestOverride = new { Type = "Manual", SetPoint = target } }), Encoding.UTF8, "application/json")
				};
			// No library HTTP retries: an uncertain mutation must be observed, never replayed.
			using var response = await http.SendAsync (request, token);
			response.EnsureSuccessStatusCode ();
			}
		}
	}