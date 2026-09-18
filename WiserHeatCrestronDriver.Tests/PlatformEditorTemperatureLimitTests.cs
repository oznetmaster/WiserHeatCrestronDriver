// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

using Crestron.DeviceDrivers.EntityModel.Data;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.Tests;

public sealed partial class PlatformDiscoveryTests
	{
	[TestCase (double.NaN)]
	[TestCase (double.PositiveInfinity)]
	[TestCase (double.NegativeInfinity)]
	[TestCase (4.5)]
	[TestCase (4.99)]
	[TestCase (35.5)]
	[TestCase (35.01)]
	[TestCase (30.01)]
	public async Task ScheduleEditor_InvalidTemperatureCommandPreservesPendingValues (double value)
		{
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Ten slots\",\"Monday\":" + EditorDay (10) + "}]";
		await _api.ReadHubDataAsync ();
		await Refresh ("[{\"id\":4,\"Name\":\"Editor room\",\"ScheduleId\":7}]");
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		foreach (int slot in new[] { 1, 10 })
			{
			var completion = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
			room.ExecuteCommand ("setEditSlot" + slot.ToString (CultureInfo.InvariantCulture) + "Temperature",
				new Dictionary<string, DriverEntityValue> { ["value"] = new DriverEntityValue (value) }, result => completion.TrySetResult (result.Failed));
			await TestSupport.Complete (completion.Task);
			var state = room.GetState ().PropertyValues;
			for (int index = 1; index <= 10; index++)
				Assert.That (state["editSlot" + index + "Temperature"].GetValue<double> (), Is.EqualTo (16 + (index - 1) * 0.5), "Rejected input must not poison any pending slot.");
			}
		Assert.That (_transport.ScheduleWrites, Is.Zero);
		}

	[TestCase (5.0, 5.0)]
	[TestCase (30.0, 30.0)]
	[TestCase (5.24, 5.0)]
	[TestCase (5.25, 5.5)]
	[TestCase (29.75, 30.0)]
	public async Task ScheduleEditor_BoundaryTemperatureCommandIsAcceptedAndCancelRestores (double value, double expected)
		{
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Ten slots\",\"Monday\":" + EditorDay (10) + "}]";
		await _api.ReadHubDataAsync ();
		await Refresh ("[{\"id\":4,\"Name\":\"Editor room\",\"ScheduleId\":7}]");
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		foreach (int slot in new[] { 1, 10 })
			{
			var completion = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
			room.ExecuteCommand ("setEditSlot" + slot.ToString (CultureInfo.InvariantCulture) + "Temperature",
				new Dictionary<string, DriverEntityValue> { ["value"] = new DriverEntityValue (value) }, result => completion.TrySetResult (result.Failed));
			await TestSupport.Complete (completion.Task);
			Assert.That (await completion.Task, Is.False);
			Assert.That (room.GetState ().PropertyValues["editSlot" + slot + "Temperature"].GetValue<double> (), Is.EqualTo (expected));
			}
		room.CancelEditSchedule ();
		Assert.That (room.GetState ().PropertyValues["editSlot1Temperature"].GetValue<double> (), Is.EqualTo (16));
		Assert.That (room.GetState ().PropertyValues["editSlot10Temperature"].GetValue<double> (), Is.EqualTo (20.5));
		Assert.That (_transport.ScheduleWrites, Is.Zero);
		}
	[TestCase (4.5)]
	[TestCase (double.NaN)]
	public async Task ScheduleEditor_InvalidCommandDoesNotFreezeClosedEditorHubRefresh (double value)
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Editor room\",\"ScheduleId\":7}]";
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Test schedule\",\"Monday\":{\"Time\":[600],\"DegreesC\":[210]}}]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		room.CancelEditSchedule ();
		Assert.That (room.EditSlot1Temperature, Is.EqualTo (21), "The selected day must be loaded before testing a rejected command.");
		var completion = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		room.ExecuteCommand ("setEditSlot1Temperature", new Dictionary<string, DriverEntityValue> { ["value"] = new DriverEntityValue (value) }, result => completion.TrySetResult (result.Failed));
		await TestSupport.Complete (completion.Task);
		Assert.That (room.EditSlot1Temperature, Is.EqualTo (21));
		_transport.HeatingSchedules = _transport.HeatingSchedules.Replace ("[600]", "[800]");
		await Refresh (rooms);
		Assert.That (room.EditSlot1Time, Is.EqualTo ("08:00"), "An ignored command must not create a pending edit that blocks fresh hub data.");
		Assert.That (room.EditScheduleError, Is.Empty);
		Assert.That (_transport.ScheduleWrites, Is.Zero);
		}
	}