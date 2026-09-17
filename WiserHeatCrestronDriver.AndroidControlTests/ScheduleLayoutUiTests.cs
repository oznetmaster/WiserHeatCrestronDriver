// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed partial record Settings
		{
		public int[] ScheduleLayoutCounts { get; init; } = [];
		public string ScheduleLayoutDay { get; init; } = "Monday";
		}

	[Test, Category ("LiveControl"), Category ("LiveScheduleLayout")]
	public async Task ScheduleDefinitionLayoutsAreRenderedAndOriginalRestored ()
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		if (_settings!.ScheduleLayoutCounts.Length == 0)
			Assert.Ignore ("Set ScheduleLayoutCounts explicitly to change a hub schedule and inspect its layouts.");
		var rooms = _settings.ScheduleSaveRooms;
		if (rooms.Length != 1 || !rooms[0].UseExistingExclusiveSchedule || rooms[0].DeviceId <= 0 || string.IsNullOrWhiteSpace (rooms[0].HubRoomName) ||
			string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("One explicitly bound exclusive schedule and private hub settings are required.");
		var hub = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing private hub settings.");
		if (string.IsNullOrWhiteSpace (hub.HubHost) || string.IsNullOrWhiteSpace (hub.Secret))
			throw new InvalidDataException ("Incomplete private hub settings.");
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", hub.Secret);
		var control = rooms[0];
		var binding = _settings.Rooms.Single (r => r.DeviceId == control.DeviceId);
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (45));
		await _navigation!.RestoreHomeAsync (timeout.Token);
		var original = await ReadRoomAsync (binding, timeout.Token);
		string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture) + ".schedule-layout";
		var session = new ScheduleSaveSession (this, hub, http, new ControlRoomBinding (control.DeviceId, control.HubRoomName), binding, original, check);
		var snapshot = await session.ReadAsync (timeout.Token);
		var physical = snapshot.Domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("Name").GetString () == control.HubRoomName);
		int roomId = physical.GetProperty ("id").GetInt32 ();
		int scheduleId = physical.GetProperty ("ScheduleId").GetInt32 ();
		if (original.PropertyValues["controlDeviceId"].GetString () != hub.HubHost.Trim ().ToLowerInvariant () + "/room/" + roomId.ToString (CultureInfo.InvariantCulture) ||
			original.PropertyValues["selectedScheduleId"].GetString () != scheduleId.ToString (CultureInfo.InvariantCulture))
			throw new InvalidDataException ("The independently observed room and driver assignment differ.");
		ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (original.PropertyValues), snapshot.Schedules, scheduleId);
		var result = await ScheduleSaveIsolation.RunLayoutsAsync (session, roomId, _settings.ScheduleLayoutDay,
			_settings.ScheduleLayoutCounts, TimeSpan.FromSeconds (45), timeout.Token);
		_roomStatePreserved = result.RestorationConfirmed && _navigation.HomeRestored;
		await session.RecordAsync ("result", new { Result = result, HomeRestored = _navigation.HomeRestored });
		Assert.That (result.Passed && _roomStatePreserved, Is.True, result.Detail);
		}

	private sealed partial class ScheduleSaveSession
		{
		public async Task ObserveLayoutAsync (ScheduleLayoutCase operation, CancellationToken token)
			{
			string prefix = "layout-" + operation.Index;
			string capture = check + "." + prefix;
			await ObserveDriverAsync (operation.ScheduleId, null, token);
			await fixture._navigation!.InspectRoomExtensionPagesAsync (capture, binding.RoomName, original.Name!, binding.PageTitle, async (pages, cancellation) =>
				{
				await RevealEditorNavigationAsync (pages, capture + ".open-schedule", "Open", cancellation);
				await pages.OpenPageAsync (Text ("Open"), "Schedule", CrestronHomePages.Resource ("customdevices_toolbarClose"), cancellation);
				await RevealEditorNavigationAsync (pages, capture + ".open-editor", "Edit", cancellation);
				await pages.OpenPageAsync (Text ("Edit"), "Edit Schedule", Text ("Cancel"), cancellation);
				bool uncertainScroll = false;
				Exception? inspectionFailure = null;
				try
					{
					var state = await ObserveDriverAsync (operation.ScheduleId, null, cancellation);
					string currentDay = state.PropertyValues["editSelectedDay"].GetString ()!;
					if (currentDay != operation.Day)
						await fixture.ChooseEditorOptionAsync (capture + ".day", Titles, "DAY", _dayLabels,
							_dayLabels.Single (d => d.Trim () == currentDay), _dayLabels.Single (d => d.Trim () == operation.Day),
							chosen => RecordAsync (prefix + "-day-input", new { Chosen = chosen }), cancellation);
					var expectedHub = JsonSerializer.SerializeToElement (new { Heating = new[] { operation.Schedule } });
					var ready = await ObserveDriverAsync (operation.ScheduleId, d =>
						{
						if (d.PropertyValues["editSelectedDay"].GetString () != operation.Day || !string.IsNullOrEmpty (d.PropertyValues["editScheduleError"].GetString ()))
							return false;
						try
							{
							ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (d.PropertyValues), expectedHub, operation.ScheduleId);
							return true;
							}
						catch (InvalidDataException) { return false; }
						}, cancellation);
					var editor = ScheduleEditorObservation.Editor (ready.PropertyValues);
					var expected = Enumerable.Range (1, operation.Count).SelectMany (slot => new[] { (slot, "Time"), (slot, "Temperature") }).ToHashSet ();
					var observed = new HashSet<(int Slot, string Kind)> ();
					string? previous = null;
					bool complete = false;
					int gestures = 0;
					for (int viewport = 0; viewport < 24; viewport++)
						{
						bool cancelVisible = false;
						string current = "";
						IReadOnlyList<ScheduleEditorRenderedValue> controls = [];
						await pages.InspectAsync (capture + ".viewport-" + viewport, front =>
							{
							controls = ScheduleEditorRendering.RequireValues (front.MaskedXml, editor, requireComplete: false);
							current = front.MaskedXml;
							var container = front.RequireUnique (CrestronHomePages.Resource ("customdevices_componentRecyclerView"));
							try
								{
								var cancel = front.RequireUnique (Text ("Cancel"));
								cancelVisible = cancel.Enabled && cancel.Left >= container.Left && cancel.Right <= container.Right && cancel.Top > container.Top && cancel.Bottom < container.Bottom;
								}
							catch (InvalidOperationException) { }
							}, cancellation);
						foreach (var value in controls)
							observed.Add ((value.Slot, value.Kind));
						await RecordAsync (prefix + "-viewport-" + viewport, new { Editor = editor, Controls = controls, CancelVisible = cancelVisible, Gestures = gestures });
						if (observed.SetEquals (expected) && cancelVisible)
							{
							complete = true;
							break;
							}
						if (previous == current)
							throw new InvalidDataException ("The editor stopped moving before every declared layout control and Cancel were observed.");
						previous = current;
						uncertainScroll = true;
						await pages.ScrollDownAsync (front => ScheduleEditorRendering.RequireValues (front.MaskedXml, editor, requireComplete: false), cancellation);
						uncertainScroll = false;
						gestures++;
						}
					Assert.That (complete, Is.True, "Every declared layout control must be completely observed.");
					await RecordAsync (prefix + "-coverage", new { Complete = complete, Count = operation.Count, Gestures = gestures, Editor = editor,
						Observed = observed.Select (value => new { value.Slot, value.Kind }), Activity = Activity (ready) });
					}
				catch (Exception error)
					{
					inspectionFailure = error;
					throw;
					}
				finally
					{
					using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
					try
						{
						await RevealEditorNavigationAsync (pages, capture + ".close-editor", "Cancel", cleanup.Token, allowScroll: !uncertainScroll);
						await pages.ClosePageAsync (cleanup.Token);
						}
					catch (Exception recoveryError) when (inspectionFailure != null)
						{
						throw new AggregateException ("Layout inspection and editor cancellation both failed.", inspectionFailure, recoveryError);
						}
					}
				}, token);
			await ObserveDriverAsync (operation.ScheduleId, null, token);
			}
		}
	}