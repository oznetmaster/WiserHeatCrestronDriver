// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	[TestCase (false), TestCase (true), Category ("LiveControl"), Category ("LiveScheduleSave")]
	public async Task ScheduleConflictRefusesStaleSaveAndRestoresOriginal (bool allDays)
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		var rooms = _settings!.ScheduleSaveRooms;
		if (rooms.Length == 0) Assert.Ignore ("Set ScheduleSaveRooms explicitly for the schedule conflict test.");
		if (rooms.Length != 1 || !rooms[0].UseExistingExclusiveSchedule || rooms[0].DeviceId <= 0 || string.IsNullOrWhiteSpace (rooms[0].HubRoomName) ||
			string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("One explicitly selected existing exclusive schedule and private hub settings are required.");
		var hub = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing private hub settings.");
		if (string.IsNullOrWhiteSpace (hub.HubHost) || string.IsNullOrWhiteSpace (hub.Secret))
			throw new InvalidDataException ("Incomplete private hub settings.");
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", hub.Secret);
		var control = rooms[0];
		var binding = _settings.Rooms.Single (r => r.DeviceId == control.DeviceId);
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (15));
		await _navigation!.RestoreHomeAsync (timeout.Token);
		var original = await ReadRoomAsync (binding, timeout.Token);
		string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture) + (allDays ? ".schedule-conflict-all" : ".schedule-conflict-day");
		var session = new ScheduleSaveSession (this, hub, http, new ControlRoomBinding (control.DeviceId, control.HubRoomName), binding, original, check);
		var snapshot = await session.ReadAsync (timeout.Token);
		var physical = snapshot.Domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("Name").GetString () == control.HubRoomName);
		int roomId = physical.GetProperty ("id").GetInt32 ();
		int scheduleId = physical.GetProperty ("ScheduleId").GetInt32 ();
		if (original.PropertyValues["controlDeviceId"].GetString () != hub.HubHost.Trim ().ToLowerInvariant () + "/room/" + roomId.ToString (CultureInfo.InvariantCulture) ||
			original.PropertyValues["selectedScheduleId"].GetString () != scheduleId.ToString (CultureInfo.InvariantCulture))
			throw new InvalidDataException ("The independently observed room and driver assignment differ.");
		ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (original.PropertyValues), snapshot.Schedules, scheduleId);
		var result = await ScheduleSaveIsolation.RunConflictAsync (session, roomId, allDays, TimeSpan.FromSeconds (45), timeout.Token);
		_roomStatePreserved = result.RestorationConfirmed && _navigation.HomeRestored;
		await session.RecordAsync ("result", new { Result = result, HomeRestored = _navigation.HomeRestored });
		Assert.That (result.Passed && _roomStatePreserved, Is.True, result.Detail);
		}

	private sealed partial class ScheduleSaveSession
		{
		public async Task ExerciseConflictAsync (ScheduleConflictCase operation, Func<CancellationToken, Task> changeHub, CancellationToken token)
			{
			const string error = "Schedule changed. Cancel and reopen.";
			var before = await ObserveDriverAsync (operation.ScheduleId, null, token);
			await fixture._navigation!.InspectRoomExtensionPagesAsync (check, binding.RoomName, original.Name!, binding.PageTitle, async (pages, cancellation) =>
				{
				await pages.OpenPageAsync (Text ("Open"), "Schedule", CrestronHomePages.Resource ("customdevices_toolbarClose"), cancellation);
				await pages.OpenPageAsync (Text ("Edit"), "Edit Schedule", Text ("Cancel"), cancellation);
				string day = before.PropertyValues["editSelectedDay"].GetString ()!;
				if (day != operation.Day)
					await fixture.ChooseEditorOptionAsync (check + ".day", Titles, "DAY", _dayLabels,
						_dayLabels.Single (d => d.Trim () == day), _dayLabels.Single (d => d.Trim () == operation.Day),
						chosen => RecordAsync ("day-input", new { Chosen = chosen }), cancellation);
				var editor = await ObserveDriverAsync (operation.ScheduleId, d => d.PropertyValues["editSelectedDay"].GetString () == operation.Day, cancellation);
				ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (editor.PropertyValues), (await ReadAsync (cancellation)).Schedules, operation.ScheduleId);
				var valueSelector = CrestronHomePages.Resource ("customdeviceraiselowerwithtext_value") with { SiblingText = "SETPOINT 1" };
				var adjustment = CrestronHomePages.Resource (operation.PendingTemperature > operation.OriginalTemperature ? "customdeviceraiselowerwithtext_plus" : "customdeviceraiselowerwithtext_minus")
					with { SiblingText = "SETPOINT 1" };
				string Temperature (int value) => (value / 10m).ToString ("0.0", CultureInfo.InvariantCulture) + "\u00B0";
				void GuardValue (AndroidHierarchy hierarchy, int expected)
					{
					AndroidWorkflowSession.VerifyContext (Session.Context);
					var front = CrestronHomeExtensionPages.RequirePage (hierarchy, Titles);
					if (front.RequireUnique (valueSelector) != hierarchy.RequireUnique (valueSelector) || front.RequireUnique (valueSelector).Text != Temperature (expected))
						throw new InvalidDataException ("The labelled pending setpoint differs from the expected editor value.");
					}
				void GuardAdjust (AndroidHierarchy hierarchy)
					{
					GuardValue (hierarchy, operation.OriginalTemperature);
					var front = CrestronHomeExtensionPages.RequirePage (hierarchy, Titles);
					if (front.RequireUnique (adjustment) != hierarchy.RequireUnique (adjustment) || !front.RequireUnique (adjustment).Enabled)
						throw new InvalidDataException ("The intended pending adjustment is unavailable.");
					}
				await Session.CaptureAsync (check + ".before-adjust", GuardAdjust, cancellation);
				await RecordAsync ("pending-temperature-intent", new { operation.OriginalTemperature, operation.PendingTemperature });
				await Session.Device.TapAsync (adjustment, GuardAdjust, cancellation);
				var pending = await ObserveDriverAsync (operation.ScheduleId, d => d.PropertyValues["editSlot1Temperature"].GetDecimal () == operation.PendingTemperature / 10m, cancellation);
				await RecordAsync ("pending-observed", ScheduleEditorObservation.Editor (pending.PropertyValues));
				var expectedConflict = JsonNode.Parse (ScheduleEditorObservation.Editor (pending.PropertyValues).GetRawText ())!;
				expectedConflict["editScheduleError"] = error;
				var expectedPending = JsonSerializer.SerializeToElement (expectedConflict);
				await changeHub (cancellation);
				var conflict = await ObserveDriverAsync (operation.ScheduleId, d => d.PropertyValues["editScheduleError"].GetString () == error, cancellation);
				Assert.That (JsonElement.DeepEquals (expectedPending, ScheduleEditorObservation.Editor (conflict.PropertyValues)), Is.True, "The conflict must preserve every pending editor value and visibility flag.");
				await RecordAsync ("conflict-observed", ScheduleEditorObservation.Editor (conflict.PropertyValues));
				void GuardConflict (AndroidHierarchy hierarchy)
					{
					GuardValue (hierarchy, operation.PendingTemperature);
					var front = CrestronHomeExtensionPages.RequirePage (hierarchy, Titles);
					var displayed = XDocument.Parse (front.MaskedXml).Descendants ("node").Where (n =>
						string.Equals ((string?)n.Attribute ("text"), error, StringComparison.OrdinalIgnoreCase)).ToArray ();
					if (displayed.Length != 1 || front.RequireUnique (Text ((string)displayed[0].Attribute ("text")!)) != hierarchy.RequireUnique (Text ((string)displayed[0].Attribute ("text")!)))
						throw new InvalidDataException ("The actual editor does not display its conflict message uniquely.");
					var button = Text (operation.AllDays ? "Save All" : "Save Day");
					if (front.RequireUnique (button) != hierarchy.RequireUnique (button) || !front.RequireUnique (button).Enabled)
						throw new InvalidDataException ("The intended stale-save action is unavailable.");
					}
				await Session.CaptureAsync (check + ".conflict-before-save", GuardConflict, cancellation);
				await RecordAsync ("stale-save-input", new { operation.AllDays, ExpectedHubSchedule = operation.External });
				_saveAttempts++;
				await Session.Device.TapAsync (Text (operation.AllDays ? "Save All" : "Save Day"), GuardConflict, cancellation);
				var refused = await ObserveDriverAsync (operation.ScheduleId, d => d.PropertyValues["editScheduleError"].GetString () == error, cancellation);
				Assert.That (JsonElement.DeepEquals (expectedPending, ScheduleEditorObservation.Editor (refused.PropertyValues)), Is.True, "The conflict must preserve every pending editor value and visibility flag.");
				await RecordAsync ("refused-driver", new { Activity = Activity (refused), Editor = ScheduleEditorObservation.Editor (refused.PropertyValues), Hub = await ReadAsync (cancellation) });
				await Session.CaptureAsync (check + ".conflict-after-save", GuardConflict, cancellation);
				await RecordAsync ("cancel-input", new { ExpectedHubSchedule = operation.External });
				await pages.ClosePageAsync (cancellation);
				await pages.OpenPageAsync (Text ("Edit"), "Edit Schedule", Text ("Cancel"), cancellation);
				var reopened = await ObserveDriverAsync (operation.ScheduleId, d => string.IsNullOrEmpty (d.PropertyValues["editScheduleError"].GetString ()), cancellation);
				ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (reopened.PropertyValues), (await ReadAsync (cancellation)).Schedules, operation.ScheduleId);
				await RecordAsync ("reopened-current-hub", ScheduleEditorObservation.Editor (reopened.PropertyValues));
				await Session.CaptureAsync (check + ".reopened", hierarchy => GuardValue (hierarchy, operation.External.GetProperty (operation.Day).GetProperty ("DegreesC")[0].GetInt32 ()), cancellation);
				await pages.ClosePageAsync (cancellation);
				}, token);
			}
		}
	}