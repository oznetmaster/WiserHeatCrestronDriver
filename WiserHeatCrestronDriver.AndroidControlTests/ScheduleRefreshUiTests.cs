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
		public bool AllowScheduleRefreshObservation { get; init; }
		}

	[Test, Category ("LiveReadOnly"), Category ("LiveScheduleChoices")]
	public async Task OpenScheduleChoicesSurviveRepeatedUnchangedHubRefreshes ()
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		if (!_settings!.AllowScheduleRefreshObservation)
			Assert.Ignore ("Enable AllowScheduleRefreshObservation to observe a selector across real hub refreshes.");
		var rooms = _settings.ScheduleSaveRooms;
		if (rooms.Length != 1 || rooms[0].DeviceId <= 0 || string.IsNullOrWhiteSpace (rooms[0].HubRoomName) ||
			string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("One explicit room binding and private hub settings are required.");
		var hub = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing private hub settings.");
		if (string.IsNullOrWhiteSpace (hub.HubHost) || string.IsNullOrWhiteSpace (hub.Secret))
			throw new InvalidDataException ("Incomplete private hub settings.");
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", hub.Secret);
		var control = rooms[0];
		var binding = _settings.Rooms.Single (r => r.DeviceId == control.DeviceId);
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (10));
		await _navigation!.RestoreHomeAsync (timeout.Token);
		var original = await ReadRoomAsync (binding, timeout.Token);
		string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture) + ".schedule-refresh";
		var session = new ScheduleSaveSession (this, hub, http, new ControlRoomBinding (control.DeviceId, control.HubRoomName), binding, original, check);
		Assert.That (await session.ExerciseUnchangedRefreshAsync (timeout.Token), Is.True, "Refresh inspection failed; inspect private evidence and restoration result.");
		}

	private sealed partial class ScheduleSaveSession
		{
		public async Task<bool> ExerciseUnchangedRefreshAsync (CancellationToken token)
			{
			var snapshot = await ReadAsync (token);
			int id = snapshot.Domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("Name").GetString () == control.HubRoomName).GetProperty ("ScheduleId").GetInt32 ();
			var expected = HubChoiceMap (snapshot);
			string selected = expected[Id (id)];
			var gateway = await fixture.ReadGatewayAsync (token);
			string lifetime = gateway.PropertyValues["driverLifetimeId"].GetString ()!;
			var sequence = new SuccessfulRefreshSequence (lifetime, gateway.PropertyValues["lastHubRefreshUtc"].GetString ()!);
			async Task VerifyUnchanged (CancellationToken cancellation)
				{
				ScheduleSaveIsolation.RequireOriginal (snapshot, await ReadAsync (cancellation));
				var device = await fixture.ReadRoomAsync (binding, cancellation);
				RequireIdentity (device);
				if (Activity (device) != _initial || device.PropertyValues["selectedScheduleId"].GetString () != Id (id) ||
					device.PropertyValues["selectedScheduleName"].GetString () != selected || !SameChoices (ChoiceMap (device), expected) ||
					!JsonElement.DeepEquals (_originalEditor, ScheduleEditorObservation.Editor (device.PropertyValues)))
					throw new InvalidDataException ("The unchanged room, editor or schedule choices changed.");
				}
			await VerifyUnchanged (token);
			await RecordAsync ("original", new { Snapshot = snapshot, SelectedId = id, Choices = expected, Lifetime = lifetime, Gateway = gateway });
			bool passed = false;
			fixture._roomStatePreserved = false;
			try
				{
				await fixture._navigation!.InspectRoomExtensionPagesAsync (check, binding.RoomName, original.Name!, binding.PageTitle, async (pages, cancellation) =>
					{
					await RevealEditorNavigationAsync (pages, check + ".open-schedule", "Open", cancellation);
					await pages.OpenPageAsync (Text ("Open"), "Schedule", CrestronHomePages.Resource ("customdevices_toolbarClose"), cancellation);
					Exception? failure = null;
					try
						{
						await RecordAsync ("dialog-open-intent", new { SelectedId = id });
						await Session.Device.TapAsync (Text ("SELECT SCHEDULE"), h =>
							{
							RenamePage (h);
							if (h.RequireUnique (Text ("SELECT SCHEDULE")) != CrestronHomeExtensionPages.RequirePage (h, RenameTitles).RequireUnique (Text ("SELECT SCHEDULE")))
								throw new InvalidOperationException ("The selector is not unique on the front page.");
							}, cancellation);
						await WaitRenameViewAsync (RenameDialog, cancellation);
						await CaptureRenamedChoicesAsync (expected.Values.ToArray (), selected, cancellation, "initial-options");
						gateway = await fixture.ReadGatewayAsync (cancellation);
						_ = sequence.Observe (gateway.PropertyValues["driverLifetimeId"].GetString ()!, gateway.PropertyValues["lastHubRefreshUtc"].GetString ()!);
						// Begin counting only after the dialog and its complete initial list have been observed.
						sequence = new (lifetime, gateway.PropertyValues["lastHubRefreshUtc"].GetString ()!);
						await RecordAsync ("refresh-baseline", gateway);
						using var refreshTimeout = CancellationTokenSource.CreateLinkedTokenSource (cancellation);
						refreshTimeout.CancelAfter (TimeSpan.FromMinutes (4));
						while (sequence.Advances < 2)
							{
							RenameDialog (await Session.Device.CaptureAsync (refreshTimeout.Token));
							gateway = await fixture.ReadGatewayAsync (refreshTimeout.Token);
							if (!sequence.Observe (gateway.PropertyValues["driverLifetimeId"].GetString ()!, gateway.PropertyValues["lastHubRefreshUtc"].GetString ()!))
								{
								await Task.Delay (1000, refreshTimeout.Token);
								continue;
								}
							await VerifyUnchanged (refreshTimeout.Token);
							string phase = "refresh-" + sequence.Advances.ToString (CultureInfo.InvariantCulture);
							await RecordAsync (phase + "-observed", new { Gateway = gateway, Snapshot = await ReadAsync (refreshTimeout.Token), Device = await fixture.ReadRoomAsync (binding, refreshTimeout.Token) });
							await CaptureRenamedChoicesAsync (expected.Values.ToArray (), selected, refreshTimeout.Token, phase + "-options");
							await VerifyUnchanged (refreshTimeout.Token);
							await RecordAsync (phase + "-ui-confirmed", new { Choices = expected, SelectedId = id, SelectedName = selected, DialogRemainedOpen = true });
							}
						}
					catch (Exception error) { failure = error; throw; }
					finally
						{
						using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
						try
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
						catch (Exception restoration) when (failure != null)
							{
							throw new AggregateException ("Refresh inspection and navigation cleanup both failed.", failure, restoration);
							}
						}
					}, token);
				passed = true;
				}
			catch (Exception error) { await RecordAsync ("failure", new { Exception = error.ToString () }); }
			finally
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
				try
					{
					await fixture._navigation!.RestoreHomeAsync (cleanup.Token);
					await VerifyUnchanged (cleanup.Token);
					gateway = await fixture.ReadGatewayAsync (cleanup.Token);
					if (gateway.PropertyValues["driverLifetimeId"].GetString () != lifetime) throw new InvalidDataException ("Driver restarted during inspection.");
					await RecordAsync ("restored", new { Snapshot = await ReadAsync (cleanup.Token), Device = await fixture.ReadRoomAsync (binding, cleanup.Token), Gateway = gateway });
					fixture._roomStatePreserved = fixture._navigation.HomeRestored;
					}
				catch (Exception error) { passed = false; await RecordAsync ("recovery-required", new { Exception = error.ToString () }); }
				}
			await RecordAsync ("result", new { Passed = passed && fixture._roomStatePreserved, RestorationConfirmed = fixture._roomStatePreserved, HomeRestored = fixture._navigation!.HomeRestored, ConfirmedRefreshes = sequence.Advances, HubWrites = _httpCommands });
			return passed && fixture._roomStatePreserved;
			}
		}
	}