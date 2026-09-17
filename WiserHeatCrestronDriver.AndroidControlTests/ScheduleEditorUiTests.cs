// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	[Test, Category ("LiveControl")]
	public async Task ScheduleEditorSelectionsCancelWithoutChangingHub ()
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		if (_settings!.ControlRooms.Length == 0 || _settings.ControlRooms.Select (room => room.DeviceId).Distinct ().Count () != _settings.ControlRooms.Length ||
			string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("Explicit control rooms and private hub settings are required.");
		var hub = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing hub settings.");
		if (string.IsNullOrWhiteSpace (hub.HubHost) || string.IsNullOrWhiteSpace (hub.Secret))
			throw new InvalidDataException ("Incomplete hub settings.");
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", hub.Secret);
		async Task<JsonElement> ReadHub (string endpoint, CancellationToken token)
			{
			using var response = await http.GetAsync ("http://" + hub.HubHost + "/data/v2/" + endpoint + "/", token);
			response.EnsureSuccessStatusCode ();
			using var document = JsonDocument.Parse (await response.Content.ReadAsStringAsync (token));
			return document.RootElement.Clone ();
			}
		foreach (var control in _settings.ControlRooms)
			{
			var binding = _settings.Rooms.Single (room => room.DeviceId == control.DeviceId);
			using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (18));
			var before = await ReadRoomAsync (binding, timeout.Token);
			var domain = await ReadHub ("domain", timeout.Token);
			var physical = domain.GetProperty ("Room").EnumerateArray ().Single (room => room.GetProperty ("Name").GetString () == control.HubRoomName);
			int scheduleId = physical.GetProperty ("ScheduleId").GetInt32 ();
			if (before.PropertyValues["controlDeviceId"].GetString () != hub.HubHost.Trim ().ToLowerInvariant () + "/room/" + physical.GetProperty ("id").GetInt32 ().ToString (CultureInfo.InvariantCulture) ||
				before.PropertyValues["selectedScheduleId"].GetString () != scheduleId.ToString (CultureInfo.InvariantCulture))
				throw new InvalidDataException ("The Home room is not bound to the independent hub schedule.");
			var activity = JsonSerializer.Deserialize<ScheduleActivity> (before.PropertyValues["controlStatus"].GetString ()!)!;
			if (activity.Pending != 0)
				throw new InvalidDataException ("The room is busy.");
			var schedules = await ReadHub ("schedules", timeout.Token);
			var originalEditor = ScheduleEditorObservation.Editor (before.PropertyValues);
			ScheduleEditorObservation.RequireMatchesHub (originalEditor, schedules, scheduleId);
			string originalDay = originalEditor.GetProperty ("editSelectedDay").GetString ()!;
			string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture) + ".editor-cancel";
			string evidence = Path.Combine (_session!.Context.EvidenceDirectory, check + ".records");
			Directory.CreateDirectory (evidence);
			async Task Record (string phase, object value)
				{
				AndroidWorkflowSession.VerifyContext (_session.Context);
				await using var file = new FileStream (Path.Combine (evidence, phase + ".json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
				await JsonSerializer.SerializeAsync (file, new { _session.Context.RunId, _session.Context.PackageSha256, Phase = phase, ObservedUtc = DateTimeOffset.UtcNow, Value = value });
				file.Flush (true);
				}
			await Record ("original", new { Editor = originalEditor, Schedules = schedules, Rooms = ScheduleEditorObservation.RoomAssignments (domain), Activity = activity });
			_roomStatePreserved = false;
			Exception? failure = null;
			string? alternateDay = null;
			try
				{
				await _navigation!.InspectRoomExtensionPagesAsync (check, binding.RoomName, before.Name!, binding.PageTitle, async (pages, token) =>
					{
					await pages.OpenPageAsync (Text ("Open"), "Schedule", CrestronHomePages.Resource ("customdevices_toolbarClose"), token);
					await pages.OpenPageAsync (Text ("Edit"), "Edit Schedule", Text ("Cancel"), token);
					string[] titles = [binding.PageTitle, "Schedule", "Edit Schedule"];
					await CaptureEditorValuesAsync (check, "controls-initial", titles, binding, Record, token);
					string dayLabel = _dayLabels.Single (label => label.Trim () == originalDay);
					string changedDay = await ChooseEditorOptionAsync (check + ".day", titles, "DAY", _dayLabels, dayLabel, null,
						value => { alternateDay = value.Trim (); return Record ("day-intent", new { Original = originalDay, Chosen = value }); }, token);
					var changed = await WaitForEditorAsync (binding, "editSelectedDay", changedDay.Trim (), token);
					ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (changed.PropertyValues), schedules, scheduleId);
					await Record ("changed-day", ScheduleEditorObservation.Editor (changed.PropertyValues));
					await ChooseEditorOptionAsync (check + ".day-restore", titles, "DAY", _dayLabels, changedDay, dayLabel,
						value => Record ("day-restore-intent", new { Chosen = value }), token);
					await WaitForEditorAsync (binding, "editSelectedDay", originalDay, token);
					string originalTime = originalEditor.GetProperty ("editSlot1Time").GetString ()!;
					string[] times = Enumerable.Range (0, 48).Select (index => (index / 2).ToString ("00", CultureInfo.InvariantCulture) + ":" + (index % 2 * 30).ToString ("00", CultureInfo.InvariantCulture)).ToArray ();
					string changedTime = await ChooseEditorOptionAsync (check + ".time", titles, "TIME 1", times, originalTime, null,
						value => Record ("time-intent", new { Original = originalTime, Chosen = value }), token);
					await WaitForEditorAsync (binding, "editSlot1Time", changedTime, token);
					await Record ("time-observed", ScheduleEditorObservation.Editor ((await ReadRoomAsync (binding, token)).PropertyValues));
					await CaptureEditorValuesAsync (check, "controls-edited", titles, binding, Record, token);
					await AdjustVisibleEditorSetpointsAsync (check, titles, binding, Record, token);
					await Record ("cancel-intent", new { Expected = originalEditor });
					await pages.ClosePageAsync (token);
					var cancelled = await WaitForEditorAsync (binding, "editSlot1Time", originalTime, token);
					Assert.That (JsonElement.DeepEquals (originalEditor, ScheduleEditorObservation.Editor (cancelled.PropertyValues)), Is.True, "Cancel must discard the pending editor values.");
					await pages.OpenPageAsync (Text ("Edit"), "Edit Schedule", Text ("Cancel"), token);
					var reopened = await ReadRoomAsync (binding, token);
					ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (reopened.PropertyValues), await ReadHub ("schedules", token), scheduleId);
					await Record ("reopened", ScheduleEditorObservation.Editor (reopened.PropertyValues));
					await pages.ClosePageAsync (token);
					}, timeout.Token);
				}
			catch (Exception error) { failure = error; throw; }
			finally
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
				try
					{
					var after = await ReadRoomAsync (binding, cleanup.Token);
					if (JsonSerializer.Deserialize<ScheduleActivity> (after.PropertyValues["controlStatus"].GetString ()!) != activity ||
						after.Name != before.Name || after.LocationId != before.LocationId || after.ParentDeviceId != before.ParentDeviceId)
						throw new InvalidDataException ("Concurrent activity or changed identity prevents editor restoration.");
					if (!JsonElement.DeepEquals (originalEditor, ScheduleEditorObservation.Editor (after.PropertyValues)))
						{
						string currentDay = after.PropertyValues["editSelectedDay"].GetString ()!;
						if (currentDay != originalDay && currentDay != alternateDay)
							throw new InvalidDataException ("An unrelated editor selection prevents restoration.");
						await using var client = await ConfigurationClient.ConnectAsync (new () { Host = _settings.Host, CertificateSha256 = _settings.CertificateSha256 }, new NetworkCredential (_settings.UserName, _settings.Password), cleanup.Token);
						if (currentDay != originalDay)
							{
							await Record ("recovery-day-intent", new { Original = originalDay });
							await client.ExecuteDeviceCommandAsync (binding.DeviceId, "setEditSelectedDay", new { value = originalDay }, cleanup.Token);
							}
						await Record ("recovery-cancel-intent", new { Original = originalEditor });
						await client.ExecuteDeviceCommandAsync (binding.DeviceId, "cancelEditSchedule", cancellationToken: cleanup.Token);
						after = await ReadRoomAsync (binding, cleanup.Token);
						}
					var finalSchedules = await ReadHub ("schedules", cleanup.Token);
					var finalRooms = ScheduleEditorObservation.RoomAssignments (await ReadHub ("domain", cleanup.Token));
					_roomStatePreserved = _navigation!.HomeRestored && JsonElement.DeepEquals (originalEditor, ScheduleEditorObservation.Editor (after.PropertyValues)) &&
						JsonElement.DeepEquals (ScheduleEditorObservation.PersistentSchedules (schedules), ScheduleEditorObservation.PersistentSchedules (finalSchedules)) &&
						JsonElement.DeepEquals (ScheduleEditorObservation.RoomAssignments (domain), finalRooms);
					await Record ("verification", new { Restored = _roomStatePreserved, HomeRestored = _navigation.HomeRestored, Editor = ScheduleEditorObservation.Editor (after.PropertyValues), Schedules = finalSchedules, Rooms = finalRooms, Passed = failure == null && _roomStatePreserved });
					Assert.That (_roomStatePreserved, Is.True, "Editor, independent hub schedules, assignments and Home must all be restored.");
					}
				catch (Exception recoveryError) when (failure != null)
					{
					throw new AggregateException ("Editor validation and restoration both failed.", failure, recoveryError);
					}
				}
			}
		}

	private async Task AdjustVisibleEditorSetpointsAsync (string check, string[] titles, RoomBinding binding,
		Func<string, object, Task> record, CancellationToken token)
		{
		var initial = ScheduleEditorObservation.Editor ((await ReadRoomAsync (binding, token)).PropertyValues);
		int[] slots = [];
		await _session!.CaptureAsync (check + ".setpoints-before", hierarchy =>
			{
			AndroidWorkflowSession.VerifyContext (_session.Context);
			var front = CrestronHomeExtensionPages.RequirePage (hierarchy, titles);
			slots = ScheduleEditorRendering.RequireValues (front.MaskedXml, initial, requireComplete: false)
				.Where (value => value.Kind == "Temperature").Select (value => value.Slot).Order ().ToArray ();
			Assert.That (slots, Is.Not.Empty, "At least one completely visible setpoint row is required.");
			}, token);
		foreach (int slot in slots)
			{
			string phase = "setpoint-" + slot.ToString (CultureInfo.InvariantCulture);
			string property = "editSlot" + slot.ToString (CultureInfo.InvariantCulture) + "Temperature";
			var before = ScheduleEditorObservation.Editor ((await ReadRoomAsync (binding, token)).PropertyValues);
			decimal original = before.GetProperty (property).GetDecimal ();
			// These are the editor's published range and step, not the room's manual target.
			if (original < 5m || original > 35m || original % 0.5m != 0m)
				throw new InvalidDataException ("The pending setpoint is outside the editor's supported range or step.");
			bool increase = original == 5m || original < 35m && slot % 2 == 1;
			decimal expectedTemperature = original + (increase ? 0.5m : -0.5m);
			var expectedValues = JsonSerializer.Deserialize<Dictionary<string, JsonElement>> (before.GetRawText ())!;
			expectedValues[property] = JsonSerializer.SerializeToElement (expectedTemperature);
			var expected = JsonSerializer.SerializeToElement (expectedValues);
			var selector = CrestronHomePages.Resource (increase ? "customdeviceraiselowerwithtext_plus" : "customdeviceraiselowerwithtext_minus")
				with { SiblingText = "SETPOINT " + slot.ToString (CultureInfo.InvariantCulture) };
			void Guard (AndroidHierarchy hierarchy)
				{
				AndroidWorkflowSession.VerifyContext (_session.Context);
				var front = CrestronHomeExtensionPages.RequirePage (hierarchy, titles);
				var values = ScheduleEditorRendering.RequireValues (front.MaskedXml, before, requireComplete: false);
				if (!values.Any (value => value.Slot == slot && value.Kind == "Temperature") || front.RequireUnique (selector) != hierarchy.RequireUnique (selector))
					throw new InvalidDataException ("The chosen setpoint action is not wholly visible and unique on the front editor page.");
				}
			await record (phase + "-intent", new { Slot = slot, Original = original, Expected = expectedTemperature, Action = increase ? "Plus" : "Minus", Before = before });
			// Exactly one input. Failed or uncertain actions are never automatically repeated.
			await _session.Device.TapAsync (selector, Guard, token);
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			deadline.CancelAfter (TimeSpan.FromSeconds (60));
			await using var client = await OpenEditorObservationConnectionAsync (deadline.Token);
			JsonElement observed;
			while (true)
				{
				observed = ScheduleEditorObservation.Editor ((await ReadRoomAsync (client, binding, deadline.Token)).PropertyValues);
				if (observed.GetProperty (property).GetDecimal () == expectedTemperature) break;
				await Task.Delay (250, deadline.Token);
				}
			Assert.That (JsonElement.DeepEquals (observed, expected), Is.True, "A setpoint button must change only its own pending slot.");
			await record (phase + "-observed", observed);
			await CaptureEditorValuesAsync (check, phase + "-rendered", titles, binding, record, token);
			}
		await record ("setpoints-coverage", new { Slots = slots, Scope = "Only initially complete visible setpoint rows; other rows require separate coverage. Pending changes are discarded by Cancel." });
		}

	private async Task CaptureEditorValuesAsync (string check, string phase, string[] titles, RoomBinding binding,
		Func<string, object, Task> record, CancellationToken token)
		{
		var editor = ScheduleEditorObservation.Editor ((await ReadRoomAsync (binding, token)).PropertyValues);
		IReadOnlyList<ScheduleEditorRenderedValue>? controls = null;
		await _session!.CaptureAsync (check + "." + phase, hierarchy =>
			{
			AndroidWorkflowSession.VerifyContext (_session.Context);
			var front = CrestronHomeExtensionPages.RequirePage (hierarchy, titles);
			controls = ScheduleEditorRendering.RequireValues (front.MaskedXml, editor, requireComplete: false);
			Assert.That (controls, Is.Not.Empty, "The editor must expose at least one complete control row.");
			}, token);
		int expected = Enumerable.Range (1, 10).Count (slot => editor.GetProperty ("editSlot" + slot.ToString (CultureInfo.InvariantCulture) + "Visible").GetBoolean ()) * 2;
		await record (phase, new { Editor = editor, Controls = controls, AllCurrentControlsObserved = controls!.Count == expected,
			Scope = "Fully visible rows on this editor page; clipped or off-screen controls do not establish coverage." });
		}

	private async Task<DeviceInfo> WaitForEditorAsync (RoomBinding binding, string property, string expected, CancellationToken token)
		{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (TimeSpan.FromSeconds (60));
		// Reuse one short-lived session while observing this selection. Reopening
		// an authenticated websocket on every property poll adds load and latency.
		await using var client = await OpenEditorObservationConnectionAsync (deadline.Token);
		while (true)
			{
			var room = await ReadRoomAsync (client, binding, deadline.Token);
			if (room.PropertyValues[property].GetString () == expected)
				return room;
			await Task.Delay (250, deadline.Token);
			}
		}

	private async Task<ConfigurationClient> OpenEditorObservationConnectionAsync (CancellationToken token)
		{
		for (int attempt = 1; ; attempt++)
			{
			AndroidWorkflowSession.VerifyContext (_session!.Context);
			try
				{
				var client = await ConfigurationClient.ConnectAsync (new ()
					{
					Host = _settings!.Host,
					CertificateSha256 = _settings.CertificateSha256,
					RequestTimeout = TimeSpan.FromSeconds (15)
					}, new NetworkCredential (_settings.UserName, _settings.Password), token);
				TestContext.Progress.WriteLine ($"Editor observation connection established on attempt {attempt}.");
				return client;
				}
			catch (OperationCanceledException) when (attempt == 1 && !token.IsCancellationRequested)
				{
				// Only the initial login timed out; no device command was sent through
				// this connection. Authentication rejection and other errors propagate.
				TestContext.Progress.WriteLine ("Editor observation login timed out; allowing one fresh read-only connection attempt.");
				await Task.Delay (1000, token);
				}
			}
		}

	private async Task<string> ChooseEditorOptionAsync (string check, string[] titles, string label, string[] allowed, string original, string? desired,
		Func<string, Task> recordIntent, CancellationToken token)
		{
		var session = _session!;
		void Page (AndroidHierarchy h)
			{
			AndroidWorkflowSession.VerifyContext (session.Context);
			var front = CrestronHomeExtensionPages.RequirePage (h, titles);
			if (front.RequireUnique (Text (label)) != h.RequireUnique (Text (label)))
				throw new InvalidOperationException ("The editor control is not unique on the front page.");
			}
		void Selection (AndroidHierarchy h)
			{
			AndroidWorkflowSession.VerifyContext (session.Context);
			var options = CrestronHomeExtensionPages.ReadSelectionOptions (CrestronHomeExtensionPages.RequireSelection (h, titles));
			if (options.Any (option => !allowed.Contains (option.Label, StringComparer.Ordinal) || option.Selected != (option.Label == original)))
				throw new InvalidDataException ("The editor selection differs from the expected state.");
			}
		async Task WaitPage (Action<AndroidHierarchy> guard, CancellationToken cancellation)
			{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellation);
			deadline.CancelAfter (TimeSpan.FromSeconds (25));
			while (true)
				{
				var hierarchy = await session.Device.CaptureAsync (deadline.Token);
				try { guard (hierarchy); return; }
				catch (InvalidOperationException) { await Task.Delay (250, deadline.Token); }
				}
			}
		Exception? failure = null;
		try
			{
			await session.Device.TapAsync (Text (label), Page, token);
			await WaitPage (Selection, token);
			string? choice = null;
			await session.CaptureAsync (check + ".options", hierarchy =>
				{
				Selection (hierarchy);
				var options = CrestronHomeExtensionPages.ReadSelectionOptions (CrestronHomeExtensionPages.RequireSelection (hierarchy, titles));
				choice = desired == null ? options.First (option => option.Label != original).Label : options.Single (option => option.Label == desired).Label;
				}, token);
			await recordIntent (choice!);
			var selector = Text (choice!) with { AncestorResourceId = CrestronHomePages.ResourcePrefix + "customdevice_selectionRecyclerView" };
			await session.Device.TapAsync (selector, Selection, token);
			await WaitPage (Page, token);
			await session.CaptureAsync (check + ".selected", Page, token);
			return choice!;
			}
		catch (Exception error) { failure = error; throw; }
		finally
			{
			using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (1));
			try
				{
				var current = await session.Device.CaptureAsync (cleanup.Token);
				bool selectionOpen;
				try { CrestronHomeExtensionPages.RequireSelection (current, titles); selectionOpen = true; }
				catch (InvalidOperationException) { Page (current); selectionOpen = false; }
				if (selectionOpen)
					{
					await session.Device.BackAsync (hierarchy => CrestronHomeExtensionPages.RequireSelection (hierarchy, titles), cleanup.Token);
					await WaitPage (Page, cleanup.Token);
					}
				}
			catch (Exception cleanupError) when (failure != null)
				{
				throw new AggregateException ("Editor selection and page restoration both failed.", failure, cleanupError);
				}
			}
		}
	}