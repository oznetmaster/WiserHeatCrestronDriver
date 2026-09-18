// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class RoomPeerObservationTests
	{
	private static ScheduleActivity Idle => new ("01234567890123456789012345678901", 3, 0);
	private static ScheduleControlSnapshot Hub => new ("hub/room/8", Idle,
		JsonSerializer.Deserialize<JsonElement> ("""{"Mode":"Auto","ScheduleId":9,"CurrentSetPoint":220}"""), true);
	private static RoomPeerSnapshot Peer => new (72, 100, "Peer room", "hub/room/8", Idle, "Celsius", true, "9", 22);

	[TestCase ("Celsius", 22)]
	[TestCase ("Fahrenheit", 71.6)]
	public void SharedStateUsesEachInstancesDisplayUnits (string units, double target) =>
		Assert.That (RoomPeerObservation.Matches (Hub, Peer with { TemperatureUnits = units, TargetTemperature = target }), Is.True);

	[Test]
	public void ManualFeedbackCanChangeWithoutPeerCommands ()
		{
		var manual = Hub with { HomeEnabled = false, Room = JsonSerializer.Deserialize<JsonElement> ("""{"Mode":"Manual","ScheduleId":9,"CurrentSetPoint":200}""") };
		var feedback = Peer with { ScheduleEnabled = false, TargetTemperature = 20 };
		Assert.DoesNotThrow (() => RoomPeerObservation.RequirePreserved (Peer, feedback));
		Assert.That (RoomPeerObservation.Matches (manual, feedback), Is.True);
		}

	[TestCase ("mode")]
	[TestCase ("schedule")]
	[TestCase ("target")]
	[TestCase ("primary")]
	public void StaleFeedbackDoesNotMatch (string field)
		{
		var peer = field switch
			{
			"mode" => Peer with { ScheduleEnabled = false },
			"schedule" => Peer with { ScheduleId = "8" },
			"target" => Peer with { TargetTemperature = 20 },
			_ => Peer
			};
		Assert.That (RoomPeerObservation.Matches (field == "primary" ? Hub with { HomeEnabled = false } : Hub, peer), Is.False);
		}

	[TestCase ("epoch")]
	[TestCase ("completed")]
	[TestCase ("pending")]
	[TestCase ("physical")]
	[TestCase ("device")]
	[TestCase ("location")]
	[TestCase ("name")]
	[TestCase ("units")]
	[TestCase ("nonfinite")]
	public void PeerActivityAndIdentityCannotBeSilentlyRebased (string change)
		{
		var changed = change switch
			{
			"epoch" => Peer with { Activity = Idle with { Epoch = Guid.NewGuid ().ToString ("N") } },
			"completed" => Peer with { Activity = Idle with { Completed = 4 } },
			"pending" => Peer with { Activity = Idle with { Pending = 1 } },
			"physical" => Peer with { PhysicalIdentity = "hub/room/9" },
			"device" => Peer with { DeviceId = 73 },
			"location" => Peer with { LocationId = 101 },
			"name" => Peer with { Name = "Other room" },
			"units" => Peer with { TemperatureUnits = "Fahrenheit" },
			_ => Peer with { TargetTemperature = double.NaN }
			};
		Assert.Throws<InvalidDataException> (() => RoomPeerObservation.RequirePreserved (Peer, changed));
		}

	[Test]
	public void SameFeedbackFromWrongPhysicalRoomIsRejected () => Assert.Throws<InvalidDataException> (() =>
		RoomPeerObservation.Matches (Hub, Peer with { PhysicalIdentity = "hub/room/9" }));
	}