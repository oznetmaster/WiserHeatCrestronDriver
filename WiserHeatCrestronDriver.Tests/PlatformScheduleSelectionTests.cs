// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Threading.Tasks;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.Tests;

public sealed partial class PlatformDiscoveryTests
	{
	[TestCase ("9"), TestCase ("not-a-schedule"), TestCase (""), TestCase ("-1"), TestCase ("0")]
	public async Task UnknownScheduleSelectionKeepsPublishedAssignment (string requested)
		{
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Assigned schedule\"}]";
		await Refresh ("[{\"id\":4,\"Name\":\"Room\",\"ScheduleId\":7}]");
		var room = Entities["room_4"];
		string activity = room.ControlStatus;
		room.SetSelectedScheduleId (requested);
		Assert.That (room.SelectedScheduleId, Is.EqualTo ("7"), "A rejected selection must retain the actual assignment.");
		Assert.That (room.ControlStatus, Is.EqualTo (activity), "Unknown values must be rejected before starting a hub operation.");
		Assert.That (_transport.ScheduleWrites, Is.Zero);
		Assert.That (_transport.ScheduleAssignments, Is.Zero);
		}

	[Test]
	public async Task DeletedScheduleSelectionDoesNotReplaceCurrentAssignment ()
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Room\",\"ScheduleId\":7}]";
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Assigned schedule\"},{\"id\":9,\"Name\":\"Removed schedule\"}]";
		await Refresh (rooms);
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Assigned schedule\"}]";
		await Refresh (rooms);
		var room = Entities["room_4"];
		string activity = room.ControlStatus;
		room.SetSelectedScheduleId ("9");
		Assert.That (room.SelectedScheduleId, Is.EqualTo ("7"));
		Assert.That (room.ControlStatus, Is.EqualTo (activity));
		Assert.That (_transport.ScheduleWrites, Is.Zero);
		Assert.That (_transport.ScheduleAssignments, Is.Zero);
		}
	[Test]
	public async Task BusyRoomRetainsAssignmentWhenAnotherValidScheduleIsRequested ()
		{
		_transport.AllowRoomCommands = true;
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Assigned\"},{\"id\":9,\"Name\":\"Other\"}]";
		await Refresh ("[{\"id\":4,\"Name\":\"Room\",\"ScheduleId\":7,\"Mode\":\"Auto\",\"CurrentSetPoint\":205}]");
		var room = Entities["room_4"];
		_transport.HoldNextDomain = true;
		room.DisableSchedule ();
		try
			{
			await TestSupport.Complete (_transport.Entered.Task);
			string activity = room.ControlStatus;
			room.SetSelectedScheduleId ("9");
			Assert.That (room.SelectedScheduleId, Is.EqualTo ("7"));
			Assert.That (room.ControlStatus, Is.EqualTo (activity));
			Assert.That (_transport.ScheduleAssignments, Is.Zero);
			}
		finally { _transport.Release.TrySetResult (true); }
		await TestSupport.Complete (WaitForCompletion (room));
		}

	[TestCase (false), TestCase (true)]
	public async Task ScheduleSelectionPublishesOnlyAfterConfirmedHubRefresh (bool accepted)
		{
		_transport.AcceptScheduleAssignment = accepted;
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Assigned\"},{\"id\":9,\"Name\":\"Other\"}]";
		await Refresh ("[{\"id\":4,\"Name\":\"Room\",\"ScheduleId\":7}]");
		var room = Entities["room_4"];
		_transport.HoldNextDomain = true;
		room.SetSelectedScheduleId ("9");
		try
			{
			await TestSupport.Complete (_transport.Entered.Task);
			Assert.That (room.SelectedScheduleId, Is.EqualTo ("7"), "A request or HTTP reply is not a confirmed assignment.");
			Assert.That (_transport.ScheduleAssignments, Is.EqualTo (1));
			}
		finally { _transport.Release.TrySetResult (true); }
		await TestSupport.Complete (WaitForCompletion (room));
		Assert.That (room.SelectedScheduleId, Is.EqualTo (accepted ? "9" : "7"));
		Assert.That (_transport.ScheduleAssignments, Is.EqualTo (1), "Rejected requests must not be retried.");
		}
	[TestCase (false, false, false, false)]
	[TestCase (true, true, false, false)]
	[TestCase (true, false, true, false)]
	[TestCase (true, false, false, true)]
	public async Task EnableScheduleRequiresConfirmedAssignmentBeforeChangingMode (bool accepted, bool refreshFails, bool ignored, bool expected)
		{
		_transport.AcceptScheduleAssignment = accepted;
		_transport.IgnoreScheduleAssignment = ignored;
		_transport.AllowRoomCommands = true;
		_transport.HeatingSchedules = "[{\"id\":9,\"Name\":\"Available schedule\"}]";
		// The room's old schedule is no longer available, so enabling needs an assignment first.
		await Refresh ("[{\"id\":4,\"Name\":\"Room\",\"ScheduleId\":7,\"Mode\":\"Manual\",\"CurrentSetPoint\":205}]");
		_transport.FailScheduleRead = refreshFails;
		bool result = await _driver.SetRoomScheduleEnabledAsync (4, true);
		Assert.That (result, Is.EqualTo (expected));
		Assert.That (_transport.ScheduleAssignments, Is.EqualTo (1));
		Assert.That (_transport.RoomCommands, Is.EqualTo (expected ? 1 : 0), "Do not change mode when the required assignment has not been confirmed.");
		}

	[TestCase (false, false, false, false)]
	[TestCase (true, true, false, false)]
	[TestCase (true, false, true, false)]
	[TestCase (true, false, false, true)]
	public async Task ScheduleAssignmentReportsSuccessOnlyWithConfirmedAssignment (bool accepted, bool refreshFails, bool ignored, bool expected)
		{
		_transport.AcceptScheduleAssignment = accepted;
		_transport.IgnoreScheduleAssignment = ignored;
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Assigned\"},{\"id\":9,\"Name\":\"Other\"}]";
		await Refresh ("[{\"id\":4,\"Name\":\"Room\",\"ScheduleId\":7}]");
		_transport.FailScheduleRead = refreshFails;
		bool result = await _driver.SetRoomAssignedScheduleAsync (4, 9);
		Assert.That (result, Is.EqualTo (expected), "An HTTP acknowledgement is not a confirmed room assignment.");
		Assert.That (_transport.ScheduleAssignments, Is.EqualTo (1), "No failed or unconfirmed write may be replayed.");
		Assert.That (Entities["room_4"].SelectedScheduleId, Is.EqualTo (expected ? "9" : "7"));
		}
	}