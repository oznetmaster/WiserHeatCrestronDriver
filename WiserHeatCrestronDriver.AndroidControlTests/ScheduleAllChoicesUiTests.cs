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
	private static IEnumerable<TestCaseData> EditorChoiceCases () => Enumerable.Range (0, 9)
		.SelectMany (slot => new[] { new TestCaseData (slot, false), new TestCaseData (slot, true) });

	[TestCaseSource (nameof (EditorChoiceCases)), Category ("LiveControl")]
	public Task ScheduleEditorEveryChoiceAppliesAndCancels (int slot, bool requirePeer) => ExerciseEditorSelectionsAsync (null, requirePeer, slot);

	private async Task FindEditorChoiceAsync (string check, string[] titles, string[] choices, string original, string desired,
		Action<AndroidHierarchy> guard, CancellationToken token)
		{
		var session = _session!;
		var search = new ScheduleChoiceSearch (choices, original, desired);
		for (int viewport = 0; viewport < 128; viewport++)
			{
			ScheduleChoiceSearchAction action = default;
			await session.CaptureAsync (check + ".find-" + viewport, hierarchy =>
				{
				guard (hierarchy);
				var options = CrestronHomeExtensionPages.ReadSelectionOptions (CrestronHomeExtensionPages.RequireSelection (hierarchy, titles));
				action = search.Observe (options.Select (option => new ScheduleChoiceValue (option.Label, option.Selected)).ToArray ());
				}, token);
			if (action == ScheduleChoiceSearchAction.Select) return;
			var current = await session.Device.CaptureAsync (token);
			guard (current);
			var container = CrestronHomeExtensionPages.RequireSelection (current, titles).RequireUnique (CrestronHomePages.Resource ("customdevice_selectionRecyclerView"));
			if (!container.Enabled || container.Bottom - container.Top < 80)
				throw new InvalidDataException ("The selector scroll area is unavailable.");
			int x = (container.Left + container.Right) / 2;
			int start = container.Top + (container.Bottom - container.Top) * 3 / 4;
			int end = container.Top + (container.Bottom - container.Top) / 4;
			if (action == ScheduleChoiceSearchAction.ScrollUp) (start, end) = (end, start);
			AndroidWorkflowSession.VerifyContext (session.Context);
			token.ThrowIfCancellationRequested ();
			var profile = session.Context.Profile;
			var transport = new AdbCommandTransport (profile.AdbExecutable, profile.DeviceSerial, TimeSpan.FromSeconds (25));
			await transport.ExecuteAsync (["shell", "input", "swipe", x.ToString (CultureInfo.InvariantCulture), start.ToString (CultureInfo.InvariantCulture),
				x.ToString (CultureInfo.InvariantCulture), end.ToString (CultureInfo.InvariantCulture), "350"], token);
			// A failed or uncertain gesture propagates; it is never automatically repeated.
			}
		throw new InvalidDataException ("The bounded selector search did not find its requested value.");
		}

	private async Task CancelEditorOptionsAsync (string check, string[] titles, string label, string[] allowed, string original,
		Func<string, object, Task> record, CancellationToken token)
		{
		var session = _session!;
		void Page (AndroidHierarchy hierarchy)
			{
			AndroidWorkflowSession.VerifyContext (session.Context);
			var front = CrestronHomeExtensionPages.RequirePage (hierarchy, titles);
			if (front.RequireUnique (Text (label)) != hierarchy.RequireUnique (Text (label)))
				throw new InvalidOperationException ("The selector label is not unique on the front editor page.");
			}
		void Selection (AndroidHierarchy hierarchy)
			{
			AndroidWorkflowSession.VerifyContext (session.Context);
			var front = CrestronHomeExtensionPages.RequireSelection (hierarchy, titles);
			var options = CrestronHomeExtensionPages.ReadSelectionOptions (front);
			if (options.Any (option => !allowed.Contains (option.Label, StringComparer.Ordinal) || option.Selected != (option.Label == original)))
				throw new InvalidDataException ("The cancellation dialog differs from the expected choice state.");
			var back = CrestronHomePages.Resource ("customdevice_selectionToolbar_backButton");
			if (front.RequireUnique (back) != hierarchy.RequireUnique (back) || !front.RequireUnique (back).Enabled)
				throw new InvalidOperationException ("The selector's own dismissal control is unavailable.");
			}
		async Task WaitFor (Action<AndroidHierarchy> guard)
			{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			deadline.CancelAfter (TimeSpan.FromSeconds (25));
			while (true)
				{
				var hierarchy = await session.Device.CaptureAsync (deadline.Token);
				try { guard (hierarchy); return; }
				catch (InvalidOperationException) { await Task.Delay (250, deadline.Token); }
				}
			}
		await record ("choice-dialog-open-intent", new { Label = label, Original = original });
		await session.Device.TapAsync (Text (label), Page, token);
		await WaitFor (Selection);
		await session.CaptureAsync (check + ".dialog-cancel.open", Selection, token);
		await record ("choice-dialog-cancel-intent", new { Label = label, ActionChosen = false,
			Control = "customdevice_selectionToolbar_backButton", Scope = "The app's selector toolbar Back control dismisses without selecting a value." });
		await session.Device.TapAsync (CrestronHomePages.Resource ("customdevice_selectionToolbar_backButton"), Selection, token);
		await WaitFor (Page);
		await session.CaptureAsync (check + ".dialog-cancel.closed", Page, token);
		// Input failures propagate to the existing outer state-restoration path, never a repeated tap.
		}

	private async Task ExerciseAllEditorChoicesAsync (CrestronHomeExtensionNavigation pages, string check, RoomBinding binding,
		JsonElement originalEditor, ScheduleHubSnapshot originalHub, int scheduleId, int slot, Action<string, string> ownDay,
		Func<string, object, Task> record, Func<CancellationToken, Task<ScheduleHubSnapshot>> readHub, CancellationToken token)
		{
		string property = slot == 0 ? "editSelectedDay" : "editSlot" + slot.ToString (CultureInfo.InvariantCulture) + "Time";
		string label = slot == 0 ? "DAY" : "TIME " + slot.ToString (CultureInfo.InvariantCulture);
		string[] choices = slot == 0 ? _dayLabels : Enumerable.Range (0, 48)
			.Select (i => (i / 2).ToString ("00", CultureInfo.InvariantCulture) + ":" + (i % 2 * 30).ToString ("00", CultureInfo.InvariantCulture)).ToArray ();
		string initial = originalEditor.GetProperty (property).GetString ()!;
		string current = choices.Single (s => s.Trim () == initial);
		string initialLabel = current;
		string[] titles = [binding.PageTitle, "Schedule", "Edit Schedule"];
		await RevealEditorNavigationAsync (pages, check + ".dialog-cancel.reveal", label, token);
		var beforeCancel = await ReadRoomAsync (binding, token);
		if (!JsonElement.DeepEquals (originalEditor, ScheduleEditorObservation.Editor (beforeCancel.PropertyValues)))
			throw new InvalidDataException ("The editor changed before the cancellation check.");
		await CancelEditorOptionsAsync (check, titles, label, choices, current, record, token);
		var afterCancel = await ReadRoomAsync (binding, token);
		var cancelHub = await readHub (token);
		if (!JsonElement.DeepEquals (originalEditor, ScheduleEditorObservation.Editor (afterCancel.PropertyValues)) ||
			!JsonElement.DeepEquals (beforeCancel.PropertyValues["controlStatus"], afterCancel.PropertyValues["controlStatus"]) ||
			!JsonElement.DeepEquals (ScheduleEditorObservation.PersistentSchedules (originalHub.Schedules), ScheduleEditorObservation.PersistentSchedules (cancelHub.Schedules)) ||
			!JsonElement.DeepEquals (ScheduleEditorObservation.RoomAssignments (originalHub.Domain), ScheduleEditorObservation.RoomAssignments (cancelHub.Domain)))
			throw new InvalidDataException ("Dismissing the selector changed editor, command activity or persistent hub state.");
		await record ("choice-dialog-cancel-observed", new { Slot = slot, Label = label, NoSelectionMade = true,
			Editor = ScheduleEditorObservation.Editor (afterCancel.PropertyValues), Activity = afterCancel.PropertyValues["controlStatus"], Hub = cancelHub });
		var selected = new HashSet<string> (StringComparer.Ordinal);
		// Select every entry, including the initial value, then return to the captured starting value.
		var sequence = choices.Concat (choices[^1] == initialLabel ? [] : new[] { initialLabel }).ToArray ();
		for (int index = 0; index < sequence.Length; index++)
			{
			string desired = sequence[index];
			string phase = "choice-" + index.ToString (CultureInfo.InvariantCulture);
			await RevealEditorNavigationAsync (pages, check + "." + phase + ".reveal", label, token);
			var before = ScheduleEditorObservation.Editor ((await ReadRoomAsync (binding, token)).PropertyValues);
			if (before.GetProperty (property).GetString () != current.Trim ())
				throw new InvalidDataException ("The selector changed outside the recorded sequence.");
			await ChooseEditorOptionAsync (check + "." + phase, titles, label, choices, current, desired, value =>
				{
				if (slot == 0) ownDay (current.Trim (), value.Trim ());
				return record (phase + "-intent", new { Slot = slot, Label = label, Original = current, Chosen = value });
				}, token);
			var observed = ScheduleEditorObservation.Editor ((await WaitForEditorAsync (binding, property, desired.Trim (), token)).PropertyValues);
			if (slot == 0)
				{
				ScheduleEditorObservation.RequireMatchesHub (observed, originalHub.Schedules, scheduleId);
				await pages.InspectAsync (check + "." + phase + ".rendered", front =>
					{
					var value = front.RequireUnique (CrestronHomePages.Resource ("statusLabels_value") with { SiblingText = "DAY" });
					var viewport = front.RequireUnique (CrestronHomePages.Resource ("customdevices_componentRecyclerView"));
					if (value.Text.Trim () != desired.Trim () || value.Top <= viewport.Top || value.Bottom >= viewport.Bottom)
						throw new InvalidDataException ("The selected day is not fully visible in its labelled row.");
					}, token);
				}
			else
				{
				var expected = JsonSerializer.Deserialize<Dictionary<string, JsonElement>> (before.GetRawText ())!;
				expected[property] = JsonSerializer.SerializeToElement (desired);
				if (!JsonElement.DeepEquals (JsonSerializer.SerializeToElement (expected), observed))
					throw new InvalidDataException ("Selecting one time changed another editor field.");
				await pages.InspectAsync (check + "." + phase + ".rendered", front =>
					{
					var rendered = ScheduleEditorRendering.RequireValues (front.MaskedXml, observed, requireComplete: false);
					if (!rendered.Any (value => value.Slot == slot && value.Kind == "Time"))
						throw new InvalidDataException ("The selected time is not fully visible in its labelled row.");
					}, token);
				}
			var hub = await readHub (token);
			if (!JsonElement.DeepEquals (ScheduleEditorObservation.PersistentSchedules (originalHub.Schedules), ScheduleEditorObservation.PersistentSchedules (hub.Schedules)) ||
				!JsonElement.DeepEquals (ScheduleEditorObservation.RoomAssignments (originalHub.Domain), ScheduleEditorObservation.RoomAssignments (hub.Domain)))
				throw new InvalidDataException ("An unsaved selector action changed persistent hub state.");
			await record (phase + "-observed", new { Slot = slot, Chosen = desired, Editor = observed, Hub = hub });
			selected.Add (desired);
			current = desired;
			}
		Assert.That (selected.SetEquals (choices), Is.True, "Every supported choice must be selected and observed.");
		await record ("choices-coverage", new { Slot = slot, Label = label, Expected = choices, Selected = selected, StartingValue = initialLabel, FinalValue = current });
		await RevealEditorNavigationAsync (pages, check + ".choices-cancel", "Cancel", token);
		await pages.ClosePageAsync (token);
		var cancelled = ScheduleEditorObservation.Editor ((await ReadRoomAsync (binding, token)).PropertyValues);
		Assert.That (JsonElement.DeepEquals (originalEditor, cancelled), Is.True, "Cancel must restore the original editor.");
		}
	}