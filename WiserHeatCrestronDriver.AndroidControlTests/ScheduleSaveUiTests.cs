// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Net.Http;
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
		public ScheduleSaveRoomBinding[] ScheduleSaveRooms { get; init; } = [];
		}
	private sealed record ScheduleSaveRoomBinding (int DeviceId, string HubRoomName, bool UseExistingExclusiveSchedule = false)
		{
		public string? ManagedAlias { get; init; }
		}
	partial void ResolveScheduleSaveBindings (AndroidRunContext context)
		{
		_settings = _settings! with
			{
			ScheduleSaveRooms = (_settings.ScheduleSaveRooms ?? throw new InvalidDataException ("Schedule-save room settings are missing.")).Select (room =>
				room with { DeviceId = ResolveManagedRoomId (context, room.DeviceId, room.ManagedAlias) }).ToArray ()
			};
		}

	[Test, Category ("LiveControl"), Category ("LiveScheduleSave")]
	public async Task ScheduleSaveDayAndAllRestoreOriginalSchedules ()
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		var rooms = _settings!.ScheduleSaveRooms;
		if (rooms.Length == 0)
			Assert.Ignore ("Set ScheduleSaveRooms explicitly to operate an isolated schedule through the app.");
		if (rooms.Any (r => r.DeviceId <= 0 || string.IsNullOrWhiteSpace (r.HubRoomName)) ||
			rooms.Select (r => r.DeviceId).Distinct ().Count () != rooms.Length || rooms.Select (r => r.HubRoomName).Distinct ().Count () != rooms.Length ||
			string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("Explicit unique schedule-save rooms and private hub settings are required.");
		var hub = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing private hub settings.");
		if (string.IsNullOrWhiteSpace (hub.HubHost) || string.IsNullOrWhiteSpace (hub.Secret))
			throw new InvalidDataException ("Incomplete private hub settings.");
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", hub.Secret);
		foreach (var control in rooms)
			{
			var binding = _settings.Rooms.Single (r => r.DeviceId == control.DeviceId);
			using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (15));
			await _navigation!.RestoreHomeAsync (timeout.Token);
			var original = await ReadRoomAsync (binding, timeout.Token);
			string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture) + ".schedule-save";
			var session = new ScheduleSaveSession (this, hub, http, new ControlRoomBinding (control.DeviceId, control.HubRoomName), binding, original, check);
			var snapshot = await session.ReadAsync (timeout.Token);
			var physical = snapshot.Domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("Name").GetString () == control.HubRoomName);
			int roomId = physical.GetProperty ("id").GetInt32 ();
			int scheduleId = physical.GetProperty ("ScheduleId").GetInt32 ();
			if (original.PropertyValues["controlDeviceId"].GetString () != hub.HubHost.Trim ().ToLowerInvariant () + "/room/" + roomId.ToString (CultureInfo.InvariantCulture) ||
				original.PropertyValues["selectedScheduleId"].GetString () != scheduleId.ToString (CultureInfo.InvariantCulture))
				throw new InvalidDataException ("The independently observed hub room and driver assignment differ.");
			ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (original.PropertyValues), snapshot.Schedules, scheduleId);
			var result = control.UseExistingExclusiveSchedule
				? await ScheduleSaveIsolation.RunExistingAsync (session, roomId, TimeSpan.FromSeconds (45), timeout.Token)
				: await ScheduleSaveIsolation.RunAsync (session, roomId, Guid.NewGuid (), TimeSpan.FromSeconds (45), timeout.Token);
			bool homeRestored = _navigation?.HomeRestored == true;
			_roomStatePreserved = result.RestorationConfirmed && homeRestored;
			await session.RecordAsync ("result", new { Result = result, HomeRestored = homeRestored });
			Assert.That (result.Passed && _roomStatePreserved, Is.True, result.Detail);
			}
		}

	private sealed partial class ScheduleSaveSession (GatewayUiTests fixture, HubSettings hub, HttpClient http,
		ControlRoomBinding control, RoomBinding binding, DeviceInfo original, string check) : IScheduleConflictSession, IScheduleLayoutSession
		{
		private readonly ScheduleActivity _initial = Activity (original);
		private readonly JsonElement _originalEditor = ScheduleEditorObservation.Editor (original.PropertyValues);
		private int _saveAttempts;
		private int _editorRestorations;
		private int _httpCommands;
		private AndroidWorkflowSession Session => fixture._session!;
		private string[] Titles => [binding.PageTitle, "Schedule", "Edit Schedule"];
		private static ScheduleActivity Activity (DeviceInfo device) => JsonSerializer.Deserialize<ScheduleActivity> (device.PropertyValues["controlStatus"].GetString ()!)!;
		private static string Id (int id) => id.ToString (CultureInfo.InvariantCulture);

		private async Task<JsonElement> ReadHubAsync (string endpoint, CancellationToken token)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			using var response = await http.GetAsync ("http://" + hub.HubHost + "/data/v2/" + endpoint + "/", token);
			response.EnsureSuccessStatusCode ();
			using var document = JsonDocument.Parse (await response.Content.ReadAsStringAsync (token));
			return document.RootElement.Clone ();
			}
		public async Task<ScheduleHubSnapshot> ReadAsync (CancellationToken token)
			{
			var domain = await ReadHubAsync ("domain", token);
			var physical = domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("Name").GetString () == control.HubRoomName);
			if (original.PropertyValues["controlDeviceId"].GetString () != hub.HubHost.Trim ().ToLowerInvariant () + "/room/" + Id (physical.GetProperty ("id").GetInt32 ()))
				throw new InvalidDataException ("The selected physical room identity changed.");
			return new (await ReadHubAsync ("schedules", token), domain);
			}
		public async Task RecordAsync (string phase, object value)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			string directory = Path.Combine (Session.Context.EvidenceDirectory, check + ".records");
			Directory.CreateDirectory (directory);
			await using var file = new FileStream (Path.Combine (directory, phase + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
			await JsonSerializer.SerializeAsync (file, new { Session.Context.RunId, Session.Context.PackageSha256, Phase = phase, ObservedUtc = DateTimeOffset.UtcNow, Value = value });
			file.Flush (true);
			if (phase is "create-intent" or "existing-schedule-intent")
				fixture._roomStatePreserved = false;
			}
		private void RequireIdentity (DeviceInfo device)
			{
			if (_initial.Pending != 0 || !Guid.TryParseExact (_initial.Epoch, "N", out _) || _initial.Completed < 0 || _initial.Completed > long.MaxValue - 2 ||
				device.Id != original.Id || device.Name != original.Name || device.LocationId != original.LocationId || device.ParentDeviceId != original.ParentDeviceId ||
				device.PropertyValues["controlDeviceId"].GetString () != original.PropertyValues["controlDeviceId"].GetString ())
				throw new InvalidDataException ("The expected idle room identity is not available.");
			var activity = Activity (device);
			if (activity.Epoch != _initial.Epoch || activity.Pending < 0 || activity.Completed < _initial.Completed || activity.Completed > _initial.Completed + _saveAttempts)
				throw new InvalidDataException ("Driver restart or unrelated commands prevent attribution.");
			}
		private async Task<DeviceInfo> ObserveDriverAsync (int? scheduleId, Func<DeviceInfo, bool>? ready, CancellationToken token)
			{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			deadline.CancelAfter (TimeSpan.FromSeconds (75));
			await using var client = await fixture.OpenEditorObservationConnectionAsync (deadline.Token);
			while (true)
				{
				var device = await fixture.ReadRoomAsync (client, binding, deadline.Token);
				RequireIdentity (device);
				var activity = Activity (device);
				if (activity.Pending == 0 && activity.Completed == _initial.Completed + _saveAttempts &&
					(!scheduleId.HasValue || device.PropertyValues["selectedScheduleId"].GetString () == Id (scheduleId.Value)) && (ready == null || ready (device)))
					return device;
				await Task.Delay (500, deadline.Token);
				}
			}
		public async Task SendAsync (string method, string relativePath, object body, CancellationToken token)
			{
			await ObserveDriverAsync (null, null, token);
			AndroidWorkflowSession.VerifyContext (Session.Context);
			using var request = new HttpRequestMessage (new HttpMethod (method), "http://" + hub.HubHost + "/data/v2/schedules/" + relativePath)
				{
				Content = new StringContent (JsonSerializer.Serialize (body), Encoding.UTF8, "application/json")
				};
			using var response = await http.SendAsync (request, token);
			await RecordAsync ("http-" + (++_httpCommands).ToString (CultureInfo.InvariantCulture) + "-response",
				new { Method = method, Path = relativePath, Status = (int)response.StatusCode, Body = await response.Content.ReadAsStringAsync (token) });
			response.EnsureSuccessStatusCode ();
			}
		public async Task ExerciseAsync (ScheduleSaveCase operation, CancellationToken token)
			{
			string phase = operation.AllDays ? "save-all" : "save-day";
			var before = await ObserveDriverAsync (operation.ScheduleId, null, token);
			await fixture._navigation!.InspectRoomExtensionPagesAsync (check + "." + phase, binding.RoomName, original.Name!, binding.PageTitle, async (pages, cancellation) =>
				{
				await pages.OpenPageAsync (Text ("Open"), "Schedule", CrestronHomePages.Resource ("customdevices_toolbarClose"), cancellation);
				await pages.OpenPageAsync (Text ("Edit"), "Edit Schedule", Text ("Cancel"), cancellation);
				string day = before.PropertyValues["editSelectedDay"].GetString ()!;
				if (day != operation.Day)
					await fixture.ChooseEditorOptionAsync (check + "." + phase + ".day", Titles, "DAY", _dayLabels,
						_dayLabels.Single (d => d.Trim () == day), _dayLabels.Single (d => d.Trim () == operation.Day),
						chosen => RecordAsync (phase + "-day-input", new { Chosen = chosen }), cancellation);
				var editor = await ObserveDriverAsync (operation.ScheduleId, d => d.PropertyValues["editSelectedDay"].GetString () == operation.Day, cancellation);
				ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (editor.PropertyValues), (await ReadAsync (cancellation)).Schedules, operation.ScheduleId);
				var selector = CrestronHomePages.Resource (operation.Temperature > operation.OriginalTemperature ? "customdeviceraiselowerwithtext_plus" : "customdeviceraiselowerwithtext_minus")
					with { SiblingText = "SETPOINT 1" };
				var valueSelector = CrestronHomePages.Resource ("customdeviceraiselowerwithtext_value") with { SiblingText = "SETPOINT 1" };
				void GuardTemperature (AndroidHierarchy hierarchy)
					{
					AndroidWorkflowSession.VerifyContext (Session.Context);
					var front = CrestronHomeExtensionPages.RequirePage (hierarchy, Titles);
					if (front.RequireUnique (selector) != hierarchy.RequireUnique (selector) ||
						front.RequireUnique (valueSelector).Text != (operation.OriginalTemperature / 10m).ToString ("0.0", CultureInfo.InvariantCulture) + "\u00B0")
						throw new InvalidOperationException ("The selected temperature row does not show the expected original target.");
					}
				await Session.CaptureAsync (check + "." + phase + ".before-adjust", GuardTemperature, cancellation);
				await RecordAsync (phase + "-temperature-input", new { Before = operation.OriginalTemperature, After = operation.Temperature });
				await Session.Device.TapAsync (selector, GuardTemperature, cancellation);
				await ObserveDriverAsync (operation.ScheduleId, d => d.PropertyValues["editSlot1Temperature"].GetDecimal () == operation.Temperature / 10m, cancellation);
				void GuardSave (AndroidHierarchy hierarchy)
					{
					AndroidWorkflowSession.VerifyContext (Session.Context);
					var front = CrestronHomeExtensionPages.RequirePage (hierarchy, Titles);
					if (front.RequireUnique (valueSelector).Text != (operation.Temperature / 10m).ToString ("0.0", CultureInfo.InvariantCulture) + "\u00B0" ||
						front.RequireUnique (Text (operation.AllDays ? "Save All" : "Save Day")) != hierarchy.RequireUnique (Text (operation.AllDays ? "Save All" : "Save Day")))
						throw new InvalidOperationException ("The edited temperature or intended save control is not available.");
					}
				await Session.CaptureAsync (check + "." + phase + ".before-save", GuardSave, cancellation);
				await RecordAsync (phase + "-button-input", new { Expected = operation.After });
				_saveAttempts++;
				await Session.Device.TapAsync (Text (operation.AllDays ? "Save All" : "Save Day"), GuardSave, cancellation);
				var saved = await ObserveDriverAsync (operation.ScheduleId, null, cancellation);
				if (!string.IsNullOrEmpty (saved.PropertyValues["editScheduleError"].GetString ()))
					throw new InvalidDataException ("The driver reported a schedule save error.");
				ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (saved.PropertyValues), (await ReadAsync (cancellation)).Schedules, operation.ScheduleId);
				await Session.CaptureAsync (check + "." + phase + ".saved", GuardSave, cancellation);
				await pages.ClosePageAsync (cancellation);
				await pages.OpenPageAsync (Text ("Edit"), "Edit Schedule", Text ("Cancel"), cancellation);
				var reopened = await ObserveDriverAsync (operation.ScheduleId, null, cancellation);
				ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (reopened.PropertyValues), (await ReadAsync (cancellation)).Schedules, operation.ScheduleId);
				await Session.CaptureAsync (check + "." + phase + ".reopened", GuardSave, cancellation);
				await pages.ClosePageAsync (cancellation);
				}, token);
			}
		public async Task RestoreEditorAsync (CancellationToken token)
			{
			await fixture._navigation!.RestoreHomeAsync (token);
			int id = (await ReadAsync (token)).Domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("Name").GetString () == control.HubRoomName).GetProperty ("ScheduleId").GetInt32 ();
			var observed = await ObserveDriverAsync (id, null, token);
			string day = _originalEditor.GetProperty ("editSelectedDay").GetString ()!;
			string phase = "editor-recovery-" + (++_editorRestorations).ToString (CultureInfo.InvariantCulture);
			await using var client = await fixture.OpenEditorObservationConnectionAsync (token);
			if (observed.PropertyValues["editSelectedDay"].GetString () != day)
				{
				await RecordAsync (phase + "-day", new { Day = day });
				await client.ExecuteDeviceCommandAsync (binding.DeviceId, "extension:setPropertyValue", new { property = "editSelectedDay", value = day }, token);
				await ObserveDriverAsync (id, d => d.PropertyValues["editSelectedDay"].GetString () == day, token);
				}
			await RecordAsync (phase + "-cancel", new { ScheduleId = id });
			await client.ExecuteDeviceCommandAsync (binding.DeviceId, "extension:doCommand", new { commandName = "cancelEditSchedule", args = Array.Empty<string> () }, token);
			bool originalAssignment = Id (id) == original.PropertyValues["selectedScheduleId"].GetString ();
			var restored = await ObserveDriverAsync (id, d => d.PropertyValues["editSelectedDay"].GetString () == day && string.IsNullOrEmpty (d.PropertyValues["editScheduleError"].GetString ()) &&
				(!originalAssignment || JsonElement.DeepEquals (_originalEditor, ScheduleEditorObservation.Editor (d.PropertyValues))), token);
			ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (restored.PropertyValues), (await ReadAsync (token)).Schedules, id);
			if (restored.PropertyValues["selectedScheduleId"].GetString () == original.PropertyValues["selectedScheduleId"].GetString () &&
				!JsonElement.DeepEquals (_originalEditor, ScheduleEditorObservation.Editor (restored.PropertyValues)))
				throw new InvalidDataException ("The original editor state is not restored.");
			if (!fixture._navigation!.HomeRestored)
				throw new InvalidDataException ("Home restoration is unconfirmed.");
			}
		}
	}