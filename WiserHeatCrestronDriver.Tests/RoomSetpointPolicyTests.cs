// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Threading.Tasks;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.Tests;

public sealed partial class PlatformDiscoveryTests
	{
	[Test]
	public async Task Setpoint_AutoRoomRetainsTimedOverrideWhenScheduleIndexIsRebuilding ()
		{
		_transport.AllowRoomCommands = true;
		await Refresh ("""[{"id":4,"Name":"Synthetic","Mode":"Auto","ScheduleId":7,"CurrentSetPoint":190}]""");
		Assert.That (_api.Rooms.GetById (4).Schedule?.Next, Is.Not.Null);
		// Refresh rebuilds the global schedule index before room assignments are restored.
		// The room still has its assigned schedule; absence from the index must not change mode.
		_api.Schedules.HeatingSchedules.Clear ();
		Assert.That (await _driver.SetRoomSetpointAsync (4, 13.5), Is.True);
		Assert.That (_transport.RoomCommands, Is.EqualTo (1), "An Auto setpoint must never send a Manual-mode command.");
		var request = TemperatureCommand (_transport.LastRoomWrite).Element ("RequestOverride");
		Assert.That ((int)request.Element ("SetPoint"), Is.EqualTo (135));
		Assert.That ((int)request.Element ("DurationMinutes"), Is.GreaterThan (0));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task Setpoint_AutoRoomWithoutUsableScheduleDoesNotChangePolicyOrCancelOverride (bool boost)
		{
		_transport.AllowRoomCommands = true;
		_transport.HeatingSchedules = "[]";
		await Refresh ("[{\"id\":4,\"Name\":\"Synthetic\",\"Mode\":\"Auto\",\"ScheduleId\":7,\"CurrentSetPoint\":190,\"SetpointOrigin\":\"" + (boost ? "FromBoost" : "FromSchedule") + "\"}]");
		Assert.That (await _driver.SetRoomSetpointAsync (4, 13.5), Is.False);
		Assert.That (_transport.RoomCommands, Is.Zero, "Missing schedule information must not authorize a mode change or cancel the existing override.");
		}
	}