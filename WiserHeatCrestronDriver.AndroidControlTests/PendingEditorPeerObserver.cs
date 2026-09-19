// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed partial record Settings
		{
		public bool ObservePeerDuringPendingEdits { get; init; }
		}

	private sealed partial class GatewayPeerControlObserver
		{
		private SchedulePendingObservation? _pendingOriginal;
		private Func<CancellationToken, Task<ScheduleHubSnapshot>>? _readPendingHub;

		public static async Task<GatewayPeerControlObserver?> OpenForPendingAsync (GatewayUiTests fixture, HubSettings hub, string check, CancellationToken token)
			{
			if (!fixture._settings!.ObservePeerDuringPendingEdits) return null;
			return await OpenCoreAsync (fixture, hub, check, false, token);
			}

		public async Task BeginPendingAsync (int roomId, Func<CancellationToken, Task<ScheduleHubSnapshot>> readHub, CancellationToken token)
			{
			await InitializeRoomAsync (roomId, token);
			if (_roomUnits != "Celsius") throw new InvalidDataException ("Pending-editor observation requires Celsius peer units.");
			_readPendingHub = readHub;
			await ObservePendingAsync ("original", token);
			}

		public async Task ObservePendingAsync (string phase, CancellationToken token)
			{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			deadline.CancelAfter (TimeSpan.FromSeconds (30));
			int observation = ++_sequence;
			var threshold = _lastSecond.RefreshUtc;
			bool initial = _pendingOriginal == null;
			for (int attempt = 1; ; attempt++)
				{
				var hubBefore = await _readPendingHub! (deadline.Token);
				var first = await ReadFirst (deadline.Token);
				var before = await ReadSecond (deadline.Token);
				var device = await ReadPeerRoomDeviceAsync (deadline.Token);
				var room = RoomSnapshot (device);
				var editor = ScheduleEditorObservation.Editor (device.PropertyValues);
				var after = await ReadSecond (deadline.Token);
				var hubAfter = await _readPendingHub (deadline.Token);
				await Record ($"{observation}.pending-{phase}.{attempt}", new { First = first, PeerBefore = before, PeerAfter = after,
					Room = room, Editor = editor, HubBefore = hubBefore, HubAfter = hubAfter });
				GatewayPairObservation.RequirePreserved (_lastFirst, first);
				GatewayPairObservation.RequirePreserved (_lastSecond, before);
				GatewayPairObservation.RequirePreserved (before, after);
				GatewayPairObservation.RequirePair (first, after);
				_lastFirst = first;
				_lastSecond = after;
				_originalRoom ??= room;
				RoomPeerObservation.RequirePreserved (_originalRoom, room);
				var physical = hubAfter.Domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("id").GetInt32 () == _roomBinding!.HubRoomId);
				_pendingOriginal ??= new SchedulePendingObservation (hubBefore, editor, physical.GetProperty ("ScheduleId").GetInt32 ());
				// Any mutation is a failure, even in a sample spanning a refresh. Do not retry it away.
				_pendingOriginal.RequirePreserved (hubBefore, editor);
				_pendingOriginal.RequirePreserved (hubAfter, editor);
				var expected = new ScheduleControlSnapshot (room.PhysicalIdentity, room.Activity, physical, true);
				if (before.RefreshUtc == after.RefreshUtc && (initial || before.RefreshUtc > threshold) && RoomPeerObservation.Matches (expected, room))
					return;
				await Task.Delay (TimeSpan.FromSeconds (1), deadline.Token);
				}
			}

		public async Task CompletePendingAsync (bool restored, bool preserveFailure)
			{
			bool verified = false;
			try
				{
				if (restored && _pendingOriginal != null)
					{
					using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (40));
					await ObservePendingAsync ("restored", cleanup.Token);
					}
				verified = restored;
				}
			catch
				{
				if (!preserveFailure) throw;
				}
			finally { await CompleteAsync (verified, preserveFailure || !verified); }
			}
		}
	}