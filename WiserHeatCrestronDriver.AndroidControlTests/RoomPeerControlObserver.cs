// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;

using CrestronHomeDevTools;
using CrestronHomeNUnit.Android;

using WiserHeatCrestronDriver.ConfigurationProbe;
using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed partial record Settings
		{
		public bool ObservePeerDuringRoomControls { get; init; }
		}

	private sealed partial class GatewayPeerControlObserver : IScheduleControlObserver
		{
		private PeerRoomBinding? _roomBinding;
		private RoomPeerSnapshot? _originalRoom;
		private string? _roomUnits;

		public static async Task<GatewayPeerControlObserver?> OpenForRoomAsync (GatewayUiTests fixture, HubSettings hub, string check, CancellationToken token)
			{
			if (!fixture._settings!.ObservePeerDuringRoomControls) return null;
			return await OpenCoreAsync (fixture, hub, check, false, token);
			}

		public async Task BeforeInputAsync (ScheduleControlSnapshot state, CancellationToken token)
			{
			await InitializeRoomAsync (state.Room.GetProperty ("id").GetInt32 (), token);
			await ObserveRoomAsync (state, "before", false, token);
			_after = _lastSecond.RefreshUtc;
			_inputElapsed.Restart ();
			}

		private async Task InitializeRoomAsync (int id, CancellationToken token)
			{
			if (_originalRoom != null) throw new InvalidOperationException ("A room observer cannot be reused for another cycle.");
			var rooms = _peer.Rooms;
			if (rooms == null || rooms.Any (r => r == null || r.HubRoomId <= 0 || r.DeviceId <= 0 || r.LocationId <= 0 || string.IsNullOrWhiteSpace (r.Name)) ||
				rooms.Select (r => r.HubRoomId).Distinct ().Count () != rooms.Length || rooms.Select (r => r.DeviceId).Distinct ().Count () != rooms.Length)
				throw new InvalidDataException ("Peer rooms must have distinct explicit physical and processor identities.");
			_roomBinding = rooms.Single (r => r.HubRoomId == id);
			var config = await DriverConfigurationInspection.GetAsync (_client!, _peer.DeviceId, token);
			if (ConfigurationCompatibility.Identity (config) != _second.ConfigurationSha256)
				throw new InvalidDataException ("Peer configuration changed before room observation.");
			_roomUnits = config.Items.Single (item => item.Id == "TemperatureUnits").CurrentValue!.Value.GetString ()!;
			}

		public Task AfterInputAsync (ScheduleControlSnapshot state, CancellationToken token) => ObserveRoomAsync (state, "after", true, token);
		public async Task AfterRestorationAsync (ScheduleControlSnapshot state, CancellationToken token)
			{
			_after = _lastSecond.RefreshUtc;
			await ObserveRoomAsync (state, "restored", true, token);
			}

		private async Task ObserveRoomAsync (ScheduleControlSnapshot state, string phase, bool fresh, CancellationToken token)
			{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
			deadline.CancelAfter (TimeSpan.FromSeconds (30));
			int observation = ++_sequence;
			for (int attempt = 1; ; attempt++)
				{
				var first = await ReadFirst (deadline.Token);
				var before = await ReadSecond (deadline.Token);
				GatewayPairObservation.RequirePreserved (_lastFirst, first);
				GatewayPairObservation.RequirePreserved (_lastSecond, before);
				var room = await ReadPeerRoomAsync (deadline.Token);
				var after = await ReadSecond (deadline.Token);
				GatewayPairObservation.RequirePreserved (before, after);
				GatewayPairObservation.RequirePair (first, after);
				_lastFirst = first;
				_lastSecond = after;
				_originalRoom ??= room;
				RoomPeerObservation.RequirePreserved (_originalRoom, room);
				// Bracket the room read with one stable refresh marker; require a new marker after commands.
				bool matches = before.RefreshUtc == after.RefreshUtc && (!fresh || before.RefreshUtc > _after) && RoomPeerObservation.Matches (state, room);
				await Record ($"{observation}.room-{phase}.{attempt}", new { First = first, PeerBefore = before, PeerAfter = after,
					Room = room, Hub = state, Matches = matches, SecondsSincePreInputObservation = _inputElapsed.Elapsed.TotalSeconds });
				if (matches) return;
				await Task.Delay (TimeSpan.FromSeconds (1), deadline.Token);
				}
			}

		private async Task<RoomPeerSnapshot> ReadPeerRoomAsync (CancellationToken token) => RoomSnapshot (await ReadPeerRoomDeviceAsync (token));

		private async Task<DeviceInfo> ReadPeerRoomDeviceAsync (CancellationToken token)
			{
			var binding = _roomBinding ?? throw new InvalidOperationException ("Peer room preflight has not completed.");
			var device = await _client!.GetDeviceAsync (binding.DeviceId, token) ?? throw new InvalidDataException ("Peer room disappeared.");
			string identity = hub.HubHost.Trim ().ToLowerInvariant () + "/room/" + binding.HubRoomId.ToString (CultureInfo.InvariantCulture);
			if (device.Id != binding.DeviceId || device.ParentDeviceId != _peer.DeviceId || device.LocationId != binding.LocationId || device.Name != binding.Name ||
				device.Model != "Room Thermostat" || device.PropertyValues["controlDeviceId"].GetString () != identity ||
				device.PropertyValues["cp.driverConfiguration:driverLoadingStatus"].GetString () != "Loaded" || !device.PropertyValues["onlineIndicator:isOnline"].GetBoolean ())
				throw new InvalidDataException ("Peer thermostat does not match its exact physical room and installed binding.");
			return device;
			}

		private RoomPeerSnapshot RoomSnapshot (DeviceInfo device)
			{
			var activity = JsonSerializer.Deserialize<ScheduleActivity> (device.PropertyValues["controlStatus"].GetString ()!)
				?? throw new InvalidDataException ("Peer command activity is absent.");
			return new (device.Id, device.LocationId ?? 0, device.Name!, device.PropertyValues["controlDeviceId"].GetString ()!, activity, _roomUnits!,
				device.PropertyValues["scheduleEnabled"].GetBoolean (), device.PropertyValues["selectedScheduleId"].GetString ()!,
				device.PropertyValues["targetTemperature"].GetDouble ());
			}
		}
	}