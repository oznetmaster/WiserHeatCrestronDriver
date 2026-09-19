// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed partial record Settings
		{
		public bool ObservePeerDuringScheduleSaves { get; init; }
		}

	private sealed partial class GatewayPeerControlObserver : IScheduleSaveObserver
		{
		public static async Task<GatewayPeerControlObserver?> OpenForScheduleAsync (GatewayUiTests fixture, HubSettings hub, string check, CancellationToken token)
			{
			if (!fixture._settings!.ObservePeerDuringScheduleSaves) return null;
			return await OpenCoreAsync (fixture, hub, check, false, token);
			}

		public async Task BeforeChangesAsync (ScheduleHubSnapshot state, int roomId, CancellationToken token)
			{
			await InitializeRoomAsync (roomId, token);
			if (_roomUnits != "Celsius") throw new InvalidDataException ("This schedule-save peer fixture requires Celsius editor units.");
			await ObserveScheduleAsync (state, "original", false, token);
			}
		public async Task BeforeSaveAsync (ScheduleHubSnapshot state, ScheduleSaveCase operation, CancellationToken token)
			{
			if (operation.Day != "Monday") throw new InvalidDataException ("This fixture observes the Monday editor.");
			await ObserveScheduleAsync (state, operation.AllDays ? "before-all" : "before-day", false, token);
			_after = _lastSecond.RefreshUtc;
			_inputElapsed.Restart ();
			}
		public Task AfterSaveAsync (ScheduleHubSnapshot state, ScheduleSaveCase operation, CancellationToken token) =>
			ObserveScheduleAsync (state, operation.AllDays ? "after-all" : "after-day", true, token);
		public async Task AfterRestorationAsync (ScheduleHubSnapshot state, int roomId, CancellationToken token)
			{
			if (_roomBinding?.HubRoomId != roomId) throw new InvalidDataException ("Restoration selected a different room.");
			_after = _lastSecond.RefreshUtc;
			await ObserveScheduleAsync (state, "restored", true, token);
			}

		private async Task ObserveScheduleAsync (ScheduleHubSnapshot state, string phase, bool fresh, CancellationToken token)
			{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			deadline.CancelAfter (TimeSpan.FromSeconds (30));
			var physical = state.Domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("id").GetInt32 () == _roomBinding!.HubRoomId);
			int scheduleId = physical.GetProperty ("ScheduleId").GetInt32 ();
			int observation = ++_sequence;
			for (int attempt = 1; ; attempt++)
				{
				var first = await ReadFirst (deadline.Token);
				var before = await ReadSecond (deadline.Token);
				GatewayPairObservation.RequirePreserved (_lastFirst, first);
				GatewayPairObservation.RequirePreserved (_lastSecond, before);
				var device = await ReadPeerRoomDeviceAsync (deadline.Token);
				var room = RoomSnapshot (device);
				var editor = ScheduleEditorObservation.Editor (device.PropertyValues);
				var after = await ReadSecond (deadline.Token);
				GatewayPairObservation.RequirePreserved (before, after);
				GatewayPairObservation.RequirePair (first, after);
				_lastFirst = first;
				_lastSecond = after;
				_originalRoom ??= room;
				RoomPeerObservation.RequirePreserved (_originalRoom, room);
				if (editor.GetProperty ("editSelectedDay").GetString () != "Monday")
					throw new InvalidDataException ("Prepare the idle peer editor on Monday before this test; the observer does not change its selection.");
				bool editorMatches = false;
				try { ScheduleEditorObservation.RequireMatchesHub (editor, state.Schedules, scheduleId); editorMatches = true; }
				catch (InvalidDataException) { /* A stale view is observed again, never changed by this observer. */ }
				var expected = new ScheduleControlSnapshot (room.PhysicalIdentity, room.Activity, physical, true);
				bool matches = before.RefreshUtc == after.RefreshUtc && (!fresh || before.RefreshUtc > _after) &&
					RoomPeerObservation.Matches (expected, room) && editorMatches;
				await Record ($"{observation}.schedule-{phase}.{attempt}", new { First = first, PeerBefore = before, PeerAfter = after,
					Room = room, Editor = editor, Hub = state, Matches = matches, SecondsSincePreInputObservation = _inputElapsed.Elapsed.TotalSeconds });
				if (matches) return;
				await Task.Delay (TimeSpan.FromSeconds (1), deadline.Token);
				}
			}
		}
	}