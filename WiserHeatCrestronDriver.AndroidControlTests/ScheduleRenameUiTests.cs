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
		public bool AllowScheduleRename { get; init; }
		}

	[TestCase (false), TestCase (true), Category ("LiveControl"), Category ("LiveScheduleChoices")]
	public async Task ScheduleRenameUpdatesOpenPageOrDialogAndRestoresOriginal (bool dialogOpen)
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		if (!_settings!.AllowScheduleRename)
			Assert.Ignore ("Enable AllowScheduleRename explicitly to test independently changed schedule labels.");
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
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (12));
		await _navigation!.RestoreHomeAsync (timeout.Token);
		var original = await ReadRoomAsync (binding, timeout.Token);
		string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture) + ".schedule-rename-" + (dialogOpen ? "dialog" : "page");
		var session = new ScheduleSaveSession (this, hub, http, new ControlRoomBinding (control.DeviceId, control.HubRoomName), binding, original, check) { RenameDialogOpen = dialogOpen };
		var snapshot = await session.ReadAsync (timeout.Token);
		var physical = snapshot.Domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("Name").GetString () == control.HubRoomName);
		int id = physical.GetProperty ("id").GetInt32 ();
		int scheduleId = physical.GetProperty ("ScheduleId").GetInt32 ();
		if (original.PropertyValues["selectedScheduleId"].GetString () != scheduleId.ToString (CultureInfo.InvariantCulture))
			throw new InvalidDataException ("Driver and independently observed assignment differ.");
		ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (original.PropertyValues), snapshot.Schedules, scheduleId);
		var result = await ScheduleSaveIsolation.RunRenameAsync (session, id, Guid.NewGuid (), TimeSpan.FromSeconds (45), timeout.Token);
		_roomStatePreserved = result.RestorationConfirmed && _navigation.HomeRestored;
		await session.RecordAsync ("result", new { Result = result, HomeRestored = _navigation.HomeRestored });
		Assert.That (result.Passed && _roomStatePreserved, Is.True, result.Detail);
		}

	private sealed partial class ScheduleSaveSession
		{
		public bool RenameDialogOpen { get; init; }
		private string[] RenameTitles => [binding.PageTitle, "Schedule"];
		private static Dictionary<string, string> ChoiceMap (DeviceInfo device) => device.PropertyValues["selectedScheduleOptions"].EnumerateArray ()
			.ToDictionary (v => v.GetProperty ("value").GetString ()!, v => v.GetProperty ("label").GetProperty ("text").GetString ()!, StringComparer.Ordinal);
		private static Dictionary<string, string> HubChoiceMap (ScheduleHubSnapshot snapshot) => snapshot.Schedules.GetProperty ("Heating").EnumerateArray ()
			.ToDictionary (s => Id (s.GetProperty ("id").GetInt32 ()), s => string.IsNullOrWhiteSpace (s.GetProperty ("Name").GetString ()) ? "Schedule " + Id (s.GetProperty ("id").GetInt32 ()) : s.GetProperty ("Name").GetString ()!, StringComparer.Ordinal);
		private static bool SameChoices (Dictionary<string, string> actual, Dictionary<string, string> expected) =>
			actual.Count == expected.Count && expected.All (pair => actual.TryGetValue (pair.Key, out var name) && name == pair.Value);
		private void RenamePage (AndroidHierarchy h)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			CrestronHomeExtensionPages.RequirePage (h, RenameTitles);
			}
		private void RenameDialog (AndroidHierarchy h)
			{
			AndroidWorkflowSession.VerifyContext (Session.Context);
			CrestronHomeExtensionPages.RequireSelection (h, RenameTitles);
			}

		public async Task ExerciseRenameAsync (ScheduleRenameCase operation, Func<CancellationToken, Task> changeHub, CancellationToken token)
			{
			string beforeName = operation.Before.GetProperty ("Name").GetString ()!;
			string afterName = operation.After.GetProperty ("Name").GetString ()!;
			var beforeChoices = HubChoiceMap (await ReadAsync (token));
			var afterChoices = new Dictionary<string, string> (beforeChoices, StringComparer.Ordinal) { [Id (operation.ScheduleId)] = afterName };
			if (beforeChoices.Values.Distinct (StringComparer.Ordinal).Count () != beforeChoices.Count || afterChoices.Values.Distinct (StringComparer.Ordinal).Count () != afterChoices.Count)
				throw new InvalidDataException ("Ambiguous schedule labels cannot be verified by the UI.");
			await ObserveDriverAsync (operation.ScheduleId, d => SameChoices (ChoiceMap (d), beforeChoices) && d.PropertyValues["selectedScheduleName"].GetString () == beforeName, token);
			await fixture._navigation!.InspectRoomExtensionPagesAsync (check, binding.RoomName, original.Name!, binding.PageTitle, async (pages, cancellation) =>
				{
				await RevealEditorNavigationAsync (pages, check + ".open-schedule", "Open", cancellation);
				await pages.OpenPageAsync (Text ("Open"), "Schedule", CrestronHomePages.Resource ("customdevices_toolbarClose"), cancellation);
				bool attemptedOpen = false;
				Exception? failure = null;
				async Task OpenDialog ()
					{
					attemptedOpen = true;
					await Session.Device.TapAsync (Text ("SELECT SCHEDULE"), h =>
						{
						RenamePage (h);
						if (h.RequireUnique (Text ("SELECT SCHEDULE")) != CrestronHomeExtensionPages.RequirePage (h, RenameTitles).RequireUnique (Text ("SELECT SCHEDULE")))
							throw new InvalidOperationException ("The schedule selector is not unique on the front page.");
						}, cancellation);
					await WaitRenameViewAsync (RenameDialog, cancellation);
					}
				try
					{
					await pages.InspectAsync (check + ".page-before", h => h.RequireUnique (Text (beforeName)), cancellation);
					if (RenameDialogOpen)
						{
						await OpenDialog ();
						await Session.CaptureAsync (check + ".dialog-before", RenameDialog, cancellation);
						}
					await RecordAsync ("open-view-before-rename", new { DialogOpen = RenameDialogOpen, SelectedId = operation.ScheduleId, Choices = beforeChoices });
					await changeHub (cancellation);
					var observed = await ObserveDriverAsync (operation.ScheduleId, d => SameChoices (ChoiceMap (d), afterChoices) && d.PropertyValues["selectedScheduleName"].GetString () == afterName, cancellation);
					await RecordAsync ("rename-driver-observed", new { SelectedId = observed.PropertyValues["selectedScheduleId"], SelectedName = afterName, Choices = ChoiceMap (observed), Activity = Activity (observed) });
					if (!RenameDialogOpen)
						{
						await WaitRenameViewAsync (h => CrestronHomeExtensionPages.RequirePage (h, RenameTitles).RequireUnique (Text (afterName)), cancellation);
						await pages.InspectAsync (check + ".page-renamed", h => h.RequireUnique (Text (afterName)), cancellation);
						await OpenDialog ();
						}
					await CaptureRenamedChoicesAsync (afterChoices.Values.ToArray (), afterName, cancellation);
					await ObserveDriverAsync (operation.ScheduleId, d => SameChoices (ChoiceMap (d), afterChoices) && d.PropertyValues["selectedScheduleName"].GetString () == afterName, cancellation);
					await RecordAsync ("rename-ui-observed", new { DialogWasOpenDuringMutation = RenameDialogOpen, SelectedId = operation.ScheduleId, SelectedName = afterName, Choices = afterChoices });
					}
				catch (Exception error) { failure = error; throw; }
				finally
					{
					using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
					try
						{
						if (attemptedOpen)
							{
							var current = await Session.Device.CaptureAsync (cleanup.Token);
							bool selection;
							try { RenameDialog (current); selection = true; }
							catch (InvalidOperationException) { RenamePage (current); selection = false; }
							if (selection)
								{
								await RecordAsync ("dialog-close-intent", new { Recovery = failure != null });
								await Session.Device.BackAsync (RenameDialog, cleanup.Token);
								await WaitRenameViewAsync (RenamePage, cleanup.Token);
								}
							await Session.CaptureAsync (check + ".dialog-closed", RenamePage, cleanup.Token);
							}
						}
					catch (Exception restoration) when (failure != null)
						{
						throw new AggregateException ("Renamed-choice inspection and dialog restoration both failed.", failure, restoration);
						}
					}
				}, token);
			}

		private async Task WaitRenameViewAsync (Action<AndroidHierarchy> verify, CancellationToken token)
			{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			deadline.CancelAfter (TimeSpan.FromSeconds (30));
			while (true)
				{
				AndroidWorkflowSession.VerifyContext (Session.Context);
				var h = await Session.Device.CaptureAsync (deadline.Token);
				try { verify (h); return; }
				catch (InvalidOperationException) { await Task.Delay (500, deadline.Token); }
				}
			}

		private async Task CaptureRenamedChoicesAsync (string[] labels, string selected, CancellationToken token, string phase = "renamed-options")
			{
			var scan = new ScheduleChoiceScan (labels, selected);
			for (int viewport = 0; viewport < 128; viewport++)
				{
				IReadOnlyList<AndroidSelectionOption> current = [];
				await Session.CaptureAsync (check + "." + phase + "-" + viewport, h =>
					{
					current = CrestronHomeExtensionPages.ReadSelectionOptions (CrestronHomeExtensionPages.RequireSelection (h, RenameTitles));
					}, token);
				var action = scan.Observe (current.Select (option => new ScheduleChoiceValue (option.Label, option.Selected)).ToArray ());
				await RecordAsync (phase + "-" + viewport, new { Options = current, Action = action.ToString () });
				if (action == ScheduleChoiceScanAction.Complete) return;
				var hierarchy = await Session.Device.CaptureAsync (token);
				RenameDialog (hierarchy);
				var container = CrestronHomeExtensionPages.RequireSelection (hierarchy, RenameTitles).RequireUnique (CrestronHomePages.Resource ("customdevice_selectionRecyclerView"));
				if (!container.Enabled || container.Bottom - container.Top < 80)
					throw new InvalidDataException ("The observed selection scroll area is unavailable.");
				int x = (container.Left + container.Right) / 2;
				int start = container.Top + (container.Bottom - container.Top) * 3 / 4;
				int end = container.Top + (container.Bottom - container.Top) / 4;
				if (action == ScheduleChoiceScanAction.ScrollUp) (start, end) = (end, start);
				await RecordAsync ("scroll-" + phase + "-" + viewport, new { X = x, Start = start, End = end, Direction = action.ToString () });
				AndroidWorkflowSession.VerifyContext (Session.Context);
				token.ThrowIfCancellationRequested ();
				var profile = Session.Context.Profile;
				var transport = new AdbCommandTransport (profile.AdbExecutable, profile.DeviceSerial, TimeSpan.FromSeconds (25));
				await transport.ExecuteAsync (["shell", "input", "swipe", Id (x), Id (start), Id (x), Id (end), "350"], token);
				// One gesture within freshly observed bounds; a failed gesture is never repeated.
				}
			throw new InvalidDataException ("Bounded scrolling did not complete the schedule list.");
			}
		}
	}