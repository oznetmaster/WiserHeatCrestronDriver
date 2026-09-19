// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Threading.Tasks;

using NUnit.Framework;

using WiserHeat.CrestronDriver;

namespace WiserHeatCrestronDriver.Tests;

public sealed partial class PlatformDiscoveryTests
	{
	[TestCase ("rejected write")]
	[TestCase ("transport failure")]
	[TestCase ("failed refresh")]
	public async Task RoomModeFailureRejectsBurstAndRestoresControlsForNextUserCommand (string failure)
		{
		_transport.AllowRoomCommands = true;
		await Refresh ("[{\"id\":4,\"Name\":\"Office\",\"ScheduleId\":7,\"Mode\":\"Auto\",\"CurrentSetPoint\":205,\"SetPointOrigin\":\"FromSchedule\"}]");
		var room = Entities["room_4"];
		AssertRoomScheduleControls (room, busy: false, automatic: true);
		double originalTarget = room.TargetTemperature;
		_transport.HoldNextRoomWrite = true;
		_transport.RejectRoomWrite = failure == "rejected write";
		_transport.ThrowRoomWrite = failure == "transport failure";
		_transport.FailScheduleRead = failure == "failed refresh";
		room.DisableSchedule ();
		try
			{
			await TestSupport.Complete (_transport.Entered.Task);
			AssertRoomScheduleControls (room, busy: true, automatic: true);
			string activity = room.ControlStatus;
			for (int i = 0; i < 5; i++)
				{
				room.DisableSchedule ();
				room.EnableSchedule ();
				room.Boost ();
				room.SetTargetTemperature (22);
				room.SetSelectedScheduleId ("7");
				}
			Assert.That (_transport.RoomCommands, Is.EqualTo (1), "Repeated input while busy must not reach the hub.");
			Assert.That (_transport.ScheduleAssignments, Is.Zero);
			Assert.That (room.ControlStatus, Is.EqualTo (activity), "Rejected inputs must not create queued or completed operations.");
			Assert.That (room.TargetTemperature, Is.EqualTo (originalTarget), "Rejected temperature input must not change feedback.");
			}
		finally
			{
			_transport.Release.TrySetResult (true);
			}
		await TestSupport.Complete (WaitForRoomControls (room, completed: 1, automatic: true));
		Assert.That (_transport.RoomCommands, Is.EqualTo (1), "A failed command must not be automatically replayed.");
		AssertRoomScheduleControls (room, busy: false, automatic: true);

		_transport.RejectRoomWrite = false;
		_transport.ThrowRoomWrite = false;
		_transport.FailScheduleRead = false;
		// Supply the hub's confirmed response to the next explicit command.
		_transport.Rooms = _transport.Rooms.Replace ("\"Mode\":\"Auto\"", "\"Mode\":\"Manual\"").Replace ("FromSchedule", "FromManualOverride");
		room.DisableSchedule ();
		await TestSupport.Complete (WaitForRoomControls (room, completed: 2, automatic: false));
		Assert.That (_transport.RoomCommands, Is.EqualTo (2), "Recovery requires exactly one new user command.");
		AssertRoomScheduleControls (room, busy: false, automatic: false);
		}

	private static void AssertRoomScheduleControls (WiserRoomEntity room, bool busy, bool automatic)
		{
		Assert.That (room.ScheduleEnabled, Is.EqualTo (automatic));
		Assert.That (room.CanEnableSchedule, Is.EqualTo (!busy && !automatic));
		Assert.That (room.CanDisableSchedule, Is.EqualTo (!busy && automatic));
		Assert.That (room.ScheduleSelectorEnabled, Is.EqualTo (!busy));
		Assert.That (room.EditScheduleEnabled, Is.EqualTo (!busy));
		}

	private static async Task WaitForRoomControls (WiserRoomEntity room, int completed, bool automatic)
		{
		string expected = "\"Completed\":" + completed + ",\"Pending\":0";
		for (int i = 0; i < 200; i++)
			{
			if (room.ControlStatus.Contains (expected) && room.ScheduleSelectorEnabled && room.EditScheduleEnabled &&
				room.CanEnableSchedule == !automatic && room.CanDisableSchedule == automatic)
				return;
			await Task.Delay (10);
			}
		Assert.Fail ("The room did not release its activity and restore controls after the command.");
		}
	}