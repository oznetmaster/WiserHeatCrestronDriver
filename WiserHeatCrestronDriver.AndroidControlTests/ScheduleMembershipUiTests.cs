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
		public bool AllowScheduleMembership { get; init; }
		}

	[Test, Category ("LiveControl"), Category ("LiveScheduleChoices")]
	public async Task OpenScheduleChoicesTrackUnassignedScheduleAdditionAndRemoval ()
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		if (!_settings!.AllowScheduleMembership)
			Assert.Ignore ("Enable AllowScheduleMembership explicitly to create and remove one temporary unassigned schedule.");
		var rooms = _settings.ScheduleSaveRooms;
		if (rooms.Length != 1 || rooms[0].DeviceId <= 0 || string.IsNullOrWhiteSpace (rooms[0].HubRoomName) ||
			string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("One explicitly bound schedule and private hub settings are required.");
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
		string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture) + ".schedule-membership";
		var session = new ScheduleSaveSession (this, hub, http, new ControlRoomBinding (control.DeviceId, control.HubRoomName), binding, original, check);
		var snapshot = await session.ReadAsync (timeout.Token);
		var physical = snapshot.Domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("Name").GetString () == control.HubRoomName);
		int id = physical.GetProperty ("id").GetInt32 ();
		int scheduleId = physical.GetProperty ("ScheduleId").GetInt32 ();
		if (original.PropertyValues["selectedScheduleId"].GetString () != scheduleId.ToString (CultureInfo.InvariantCulture))
			throw new InvalidDataException ("Driver and independently observed assignment differ.");
		ScheduleEditorObservation.RequireMatchesHub (ScheduleEditorObservation.Editor (original.PropertyValues), snapshot.Schedules, scheduleId);
		var result = await ScheduleSaveIsolation.RunMembershipAsync (session, id, Guid.NewGuid (), TimeSpan.FromSeconds (45), timeout.Token);
		_roomStatePreserved = result.RestorationConfirmed && _navigation.HomeRestored;
		await session.RecordAsync ("result", new { Result = result, HomeRestored = _navigation.HomeRestored });
		Assert.That (result.Passed && _roomStatePreserved, Is.True, result.Detail);
		}

	private sealed partial class ScheduleSaveSession
		{
		public async Task ExerciseMembershipAsync (int selectedId, Func<CancellationToken, Task<JsonElement>> create, Func<CancellationToken, Task> remove, CancellationToken token)
			{
			string beforeName = original.PropertyValues["selectedScheduleName"].GetString ()!;
			var beforeChoices = HubChoiceMap (await ReadAsync (token));
			if (beforeChoices.Values.Distinct (StringComparer.Ordinal).Count () != beforeChoices.Count)
				throw new InvalidDataException ("Ambiguous labels cannot be independently verified in the selector.");
			await ObserveDriverAsync (selectedId, d => SameChoices (ChoiceMap (d), beforeChoices) && d.PropertyValues["selectedScheduleName"].GetString () == beforeName, token);

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
					await OpenDialog ();
					await Session.CaptureAsync (check + ".dialog-before", RenameDialog, cancellation);
					await RecordAsync ("open-view-before-membership", new { SelectedId = selectedId, SelectedName = beforeName, Choices = beforeChoices });
					var created = await create (cancellation);
					var afterChoices = new Dictionary<string, string> (beforeChoices, StringComparer.Ordinal) { [Id (created.GetProperty ("id").GetInt32 ())] = created.GetProperty ("Name").GetString ()! };
					if (afterChoices.Values.Distinct (StringComparer.Ordinal).Count () != afterChoices.Count)
						throw new InvalidDataException ("The new label is ambiguous in the selector.");
					var added = await ObserveDriverAsync (selectedId, d => SameChoices (ChoiceMap (d), afterChoices) && d.PropertyValues["selectedScheduleName"].GetString () == beforeName, cancellation);
					await RecordAsync ("membership-added-driver", new { Device = added, Choices = ChoiceMap (added), Activity = Activity (added) });
					await CaptureRenamedChoicesAsync (afterChoices.Values.ToArray (), beforeName, cancellation, "added-options");
					await ObserveDriverAsync (selectedId, d => SameChoices (ChoiceMap (d), afterChoices), cancellation);
					await RecordAsync ("membership-added-ui", new { DialogRemainedOpen = true, SelectedId = selectedId, Choices = afterChoices });
					await remove (cancellation);
					var removed = await ObserveDriverAsync (selectedId, d => SameChoices (ChoiceMap (d), beforeChoices) && d.PropertyValues["selectedScheduleName"].GetString () == beforeName, cancellation);
					await RecordAsync ("membership-removed-driver", new { Device = removed, Choices = ChoiceMap (removed), Activity = Activity (removed) });
					await CaptureRenamedChoicesAsync (beforeChoices.Values.ToArray (), beforeName, cancellation, "removed-options");
					await ObserveDriverAsync (selectedId, d => SameChoices (ChoiceMap (d), beforeChoices), cancellation);
					await RecordAsync ("membership-removed-ui", new { DialogRemainedOpen = true, SelectedId = selectedId, Choices = beforeChoices });

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
						throw new AggregateException ("Membership inspection and dialog restoration both failed.", failure, restoration);
						}
					}
				}, token);
			}

		public async Task RestoreMembershipViewAsync (CancellationToken token)
			{
			await fixture._navigation!.RestoreHomeAsync (token);
			string selected = original.PropertyValues["selectedScheduleId"].GetString ()!;
			var observed = await ObserveDriverAsync (int.Parse (selected, CultureInfo.InvariantCulture), d =>
				SameChoices (ChoiceMap (d), ChoiceMap (original)) && d.PropertyValues["selectedScheduleName"].GetString () == original.PropertyValues["selectedScheduleName"].GetString () &&
				JsonElement.DeepEquals (_originalEditor, ScheduleEditorObservation.Editor (d.PropertyValues)), token);
			if (!fixture._navigation.HomeRestored) throw new InvalidDataException ("Home restoration is unconfirmed.");
			await RecordAsync ("membership-view-restored", observed);
			}
		}
	}