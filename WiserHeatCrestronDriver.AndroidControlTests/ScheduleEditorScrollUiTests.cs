// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Net.Http;
using System.Text.Json;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed partial record Settings
		{
		public bool RequireEditorScrolling { get; init; }
		}

	[Test, Category ("LiveControl")]
	public Task ScheduleEditorObservesEveryCurrentRowAndRestoresState () => InspectCurrentEditorRowsAsync (false);

	[Test, Category ("LiveControl")]
	public Task ScheduleEditorCurrentRowsRespectTemperatureLimitsAndCancel () => InspectCurrentEditorRowsAsync (true);

	private async Task InspectCurrentEditorRowsAsync (bool exerciseSetpoints)
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
			// Validate every original before any pending edit; unsupported values cannot be restored through the bounded setter.
			if (exerciseSetpoints && Enumerable.Range (1, 10).Where (slot => originalEditor.GetProperty ("editSlot" + slot + "Visible").GetBoolean ())
				.Select (slot => originalEditor.GetProperty ("editSlot" + slot + "Temperature").GetDecimal ())
				.Any (value => value < 5m || value > 35m || value % 0.5m != 0m))
				throw new InvalidDataException ("Boundary testing requires restorable original temperatures within 5-35 degrees in half-degree steps.");
			string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture) + (exerciseSetpoints ? ".editor-boundaries" : ".editor-scroll");
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
			JsonElement ownedBefore = originalEditor, ownedAfter = originalEditor;
			void OwnTransition (JsonElement from, JsonElement to) { ownedBefore = from; ownedAfter = to; }
			try
				{
				await _navigation!.InspectRoomExtensionPagesAsync (check, binding.RoomName, before.Name!, binding.PageTitle, async (pages, token) =>
					{
					await RevealEditorNavigationAsync (pages, check + ".open-schedule", "Open", token);
					await pages.OpenPageAsync (Text ("Open"), "Schedule", CrestronHomePages.Resource ("customdevices_toolbarClose"), token);
					await RevealEditorNavigationAsync (pages, check + ".open-editor", "Edit", token);
					await pages.OpenPageAsync (Text ("Edit"), "Edit Schedule", Text ("Cancel"), token);
					bool uncertainScroll = false;
					Exception? inspectionFailure = null;
					try
						{
						var expected = Enumerable.Range (1, 10).Where (slot => originalEditor.GetProperty ("editSlot" + slot + "Visible").GetBoolean ())
							.SelectMany (slot => new[] { (slot, "Time"), (slot, "Temperature") }).ToHashSet ();
						var observed = new HashSet<(int Slot, string Kind)> ();
						var exercised = new HashSet<int> ();
						string? previous = null;
						int gestures = 0;
						bool complete = false;
						for (int viewport = 0; viewport < 16; viewport++)
							{
							bool cancelVisible = false;
							IReadOnlyList<ScheduleEditorRenderedValue> controls = [];
							string current = "";
							await pages.InspectAsync (check + ".viewport-" + viewport, front =>
								{
								controls = ScheduleEditorRendering.RequireValues (front.MaskedXml, originalEditor, requireComplete: false);
								var container = front.RequireUnique (CrestronHomePages.Resource ("customdevices_componentRecyclerView"));
								current = front.MaskedXml;
								try
									{
									var cancel = front.RequireUnique (Text ("Cancel"));
									cancelVisible = cancel.Enabled && cancel.Left >= container.Left && cancel.Right <= container.Right && cancel.Top > container.Top && cancel.Bottom < container.Bottom;
									}
								catch (InvalidOperationException) { }
								}, token);
							if (viewport == 0 && _settings.RequireEditorScrolling) Assert.That (controls.Count, Is.LessThan (expected.Count), "This case requires an actual off-screen control, not a fully fitted editor.");
							foreach (var value in controls) observed.Add ((value.Slot, value.Kind));
							if (exerciseSetpoints)
								foreach (int slot in controls.Where (value => value.Kind == "Temperature").Select (value => value.Slot).Where (slot => !exercised.Contains (slot)))
									{
									await ExerciseEditorBoundaryRowAsync (check, [binding.PageTitle, "Schedule", "Edit Schedule"], binding, slot, originalEditor, OwnTransition, Record, token);
									exercised.Add (slot);
									}
							await Record ("viewport-" + viewport, new { Controls = controls, CancelVisible = cancelVisible, ExpectedCount = expected.Count, ObservedCount = observed.Count, Gestures = gestures });
							if (observed.SetEquals (expected) && cancelVisible) { complete = true; break; }
							if (previous == current) throw new InvalidDataException ("The editor stopped moving before all controls and Cancel were observed.");
							previous = current;
							uncertainScroll = true;
							await pages.ScrollDownAsync (front => ScheduleEditorRendering.RequireValues (front.MaskedXml, originalEditor, requireComplete: false), token);
							uncertainScroll = false;
							gestures++;
							}
						Assert.That (complete && (!_settings.RequireEditorScrolling || gestures > 0), Is.True, "Every current control must be completely observed; require scrolling only for a configured smaller-screen case.");
						Assert.That (!exerciseSetpoints || exercised.SetEquals (expected.Where (value => value.Item2 == "Temperature").Select (value => value.slot)), Is.True);
						await Record ("coverage", new { Complete = complete, ExercisedSlots = exercised, Gestures = gestures, Expected = expected.Select (value => new { Slot = value.slot, Kind = value.Item2 }), Observed = observed.Select (value => new { value.Slot, value.Kind }) });
						}
					catch (Exception error) { inspectionFailure = error; throw; }
					finally
						{
						using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
						try
							{
							var pending = ScheduleEditorObservation.Editor ((await ReadRoomAsync (binding, cleanup.Token)).PropertyValues);
							if (!JsonElement.DeepEquals (pending, ownedBefore) && !JsonElement.DeepEquals (pending, ownedAfter))
								throw new InvalidDataException ("Unrelated editor changes prevent cancelling the owned pending edit.");
							await Record ("cancel-intent", new { Expected = originalEditor, UncertainScroll = uncertainScroll });
							await RevealEditorNavigationAsync (pages, check + ".close-editor", "Cancel", cleanup.Token, allowScroll: !uncertainScroll);
							await pages.ClosePageAsync (cleanup.Token);
							}
						catch (Exception recoveryError) when (inspectionFailure != null)
							{
							throw new AggregateException ("Off-screen inspection and editor cancellation both failed.", inspectionFailure, recoveryError);
							}
						}
					var afterScroll = await ReadRoomAsync (binding, token);
					Assert.That (JsonElement.DeepEquals (originalEditor, ScheduleEditorObservation.Editor (afterScroll.PropertyValues)), Is.True);

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
						throw new InvalidDataException ("The read-only editor observation changed state. No compensating device command was sent; preserve the run for reconciliation.");
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

	private static async Task RevealEditorNavigationAsync (CrestronHomeExtensionNavigation pages, string check, string label, CancellationToken token, bool allowScroll = true)
		{
		string? previous = null;
		for (int gesture = 0; gesture < 16; gesture++)
			{
			bool ready = false;
			string current = "";
			await pages.InspectAsync (check + ".viewport-" + gesture, front =>
				{
				current = front.MaskedXml;
				var nodes = System.Xml.Linq.XDocument.Parse (current).Descendants ("node")
					.Where (node => (string?)node.Attribute ("package") == "com.crestron.phoenix.app" && (string?)node.Attribute ("text") == label).ToArray ();
				if (nodes.Length > 1) throw new InvalidDataException ("The requested navigation label is ambiguous on the front page.");
				if (nodes.Length == 0) return;
				var target = front.RequireUnique (Text (label));
				var viewport = front.RequireUnique (CrestronHomePages.Resource ("customdevices_componentRecyclerView"));
				ready = target.Enabled && target.Left >= viewport.Left && target.Right <= viewport.Right && target.Top > viewport.Top && target.Bottom < viewport.Bottom;
				}, token);
			if (ready) return;
			if (!allowScroll) throw new InvalidDataException ("The cancel control is not visible after an uncertain gesture; no gesture will be replayed.");
			if (previous == current) throw new InvalidDataException ("The page stopped moving before its navigation control was visible.");
			previous = current;
			await pages.ScrollDownAsync (front => front.RequireUnique (CrestronHomePages.Resource ("customdevices_componentRecyclerView")), token);
			}
		throw new InvalidDataException ("The navigation control was not revealed within the bounded scroll limit.");
		}
	}