// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using WiserHeat.CrestronDriver;

namespace WiserHeatCrestronDriver.Tests;

public sealed partial class PlatformDiscoveryTests
	{
	[Test]
	public async Task ConcurrentEditorsCannotOverwriteAnAcceptedSharedScheduleEdit ()
		{
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Shared\",\"Monday\":{\"Time\":[600],\"DegreesC\":[210]}}]";
		_transport.AllowScheduleWrites = true;
		await Refresh ("[{\"id\":4,\"Name\":\"First\",\"ScheduleId\":7},{\"id\":5,\"Name\":\"Second\",\"ScheduleId\":7}]");
		var original = _driver.GetAssignedScheduleForRoom (4).ScheduleData;
		IDictionary<string, object> Clone () => TestSupport.Call<IDictionary<string, object>> (typeof (WiserRoomEntity), "CloneScheduleData", original);
		var firstEdit = Clone ();
		var secondEdit = Clone ();
		((IDictionary<string, object>)firstEdit["Monday"])["DegreesC"] = new List<object> { 220 };
		((IDictionary<string, object>)secondEdit["Monday"])["DegreesC"] = new List<object> { 230 };
		_transport.HoldNextDomain = true;
		Task<bool> first = _driver.SaveHeatingScheduleAsync (4, 7, firstEdit, original);
		await TestSupport.Complete (_transport.Entered.Task);
		Task<bool> second = _driver.SaveHeatingScheduleAsync (5, 7, secondEdit, original);
		Assert.That (second.IsCompleted, Is.False);
		_transport.Release.TrySetResult (true);
		await TestSupport.Complete (Task.WhenAll (first, second));
		Assert.That (await first, Is.True);
		Assert.That (await second, Is.False);
		Assert.That (_transport.ScheduleWrites, Is.EqualTo (1));
		Assert.That (_transport.LastScheduleWrite, Does.Contain ("220").And.Not.Contain ("230"));
		}

	[Test]
	public async Task StoppedRoomCannotPublishLateScheduleFailure ()
		{
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Shared\",\"Monday\":{\"Time\":[600],\"DegreesC\":[210]}}]";
		_transport.AllowScheduleWrites = true;
		_transport.IgnoreScheduleWrite = true;
		await Refresh ("[{\"id\":4,\"Name\":\"First\",\"ScheduleId\":7}]");
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		room.SetEditSlot1Time ("07:00");
		_transport.HoldNextDomain = true;
		var save = (Task<bool>)typeof (WiserRoomEntity).GetMethod ("SaveEditedScheduleAsync", Private).Invoke (room, new object[] { false });
		await TestSupport.Complete (_transport.Entered.Task);
		room.StopPolling ();
		string previousError = room.EditScheduleError;
		_transport.Release.TrySetResult (true);
		await TestSupport.Complete (save);
		Assert.That (await save, Is.False);
		Assert.That (room.EditScheduleError, Is.EqualTo (previousError));
		}

	[Test]
	public async Task RoutineRoomBreadcrumbsAreCompiledOutOfRelease ()
		{
		await Refresh ("[{\"id\":4,\"Name\":\"Synthetic room\"}]");
		var logger = (TestLogger)_logger.AppLogger;
		while (logger.Entries.TryDequeue (out _))
			{
			}
		Entities["room_4"].StartPolling ();
		bool wrote = logger.Entries.Any (entry => entry.Message.Contains ("StartPolling entered"));
#if DEBUG
		Assert.That (wrote, Is.True);
#else
		Assert.That (wrote, Is.False);
#endif
		}
	}