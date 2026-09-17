// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.Tests;

public sealed partial class PlatformDiscoveryTests
	{
	private static string EditorDay (int count) => "{\"Time\":[" + string.Join (",", Enumerable.Range (0, count).Select (index => index * 100)) +
		"],\"DegreesC\":[" + string.Join (",", Enumerable.Range (0, count).Select (index => 160 + index * 5)) + "]}";

	[Test, Combinatorial]
	public async Task ScheduleEditor_EachSlotPublishesOnlyItsOwnPendingChangeAndCancelRestoresIt ([Range (1, 10)] int slot, [Values (false, true)] bool temperature)
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Editor room\",\"ScheduleId\":7}]";
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Ten slots\",\"Monday\":" + EditorDay (10) + "}]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		var room = Entities["room_4"];
		using var dispatcher = CreateDispatcher ();
		room.StartPolling ();
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		string suffix = temperature ? "Temperature" : "Time";
		string property = "editSlot" + slot.ToString (CultureInfo.InvariantCulture) + suffix;
		double changedTemperature = 16 + (slot - 1) * 0.5 + 0.5;
		const string changedTime = "23:30";
		var observed = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		dispatcher.ValuesChanged += (_, args) =>
			{
			if (args.ControllerId != "room_4") return;
			foreach (var change in args.Update.Changes)
				if (change.Key == property && change.Value.Value.HasValue &&
					(temperature ? change.Value.Value.Value.GetValue<double> () == changedTemperature : change.Value.Value.Value.GetValue<string> () == changedTime))
					observed.TrySetResult (true);
			};
		var setter = room.GetType ().GetMethod ("SetEditSlot" + slot.ToString (CultureInfo.InvariantCulture) + suffix);
		Assert.That (setter, Is.Not.Null, "Every displayed slot needs its matching public command.");
		setter.Invoke (room, new object[] { temperature ? (object)changedTemperature : changedTime });
		await TestSupport.Complete (observed.Task);
		var edited = room.GetState ().PropertyValues;
		for (int index = 1; index <= 10; index++)
			{
			string prefix = "editSlot" + index.ToString (CultureInfo.InvariantCulture);
			Assert.That (edited[prefix + "Visible"].GetValue<bool> (), Is.True);
			Assert.That (edited[prefix + "Time"].GetValue<string> (), Is.EqualTo (!temperature && index == slot ? changedTime : (index - 1).ToString ("00", CultureInfo.InvariantCulture) + ":00"));
			Assert.That (edited[prefix + "Temperature"].GetValue<double> (), Is.EqualTo (temperature && index == slot ? changedTemperature : 16 + (index - 1) * 0.5));
			}
		room.CancelEditSchedule ();
		var restored = room.GetState ().PropertyValues;
		Assert.That (restored["editSlot" + slot + "Time"].GetValue<string> (), Is.EqualTo ((slot - 1).ToString ("00", CultureInfo.InvariantCulture) + ":00"));
		Assert.That (restored["editSlot" + slot + "Temperature"].GetValue<double> (), Is.EqualTo (16 + (slot - 1) * 0.5));
		Assert.That (_transport.ScheduleWrites, Is.Zero, "Editor changes and Cancel must not write to the hub.");
		}

	[Test]
	public async Task ScheduleEditor_ChangingDayClearsEveryHiddenSlotAndRestoresAllTenOnReturn ([Range (1, 10)] int visibleSlots)
		{
		const string rooms = "[{\"id\":4,\"Name\":\"Editor room\",\"ScheduleId\":7}]";
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Variable day lengths\",\"Monday\":" + EditorDay (10) + ",\"Tuesday\":" + EditorDay (visibleSlots) + "}]";
		await _api.ReadHubDataAsync ();
		await Refresh (rooms);
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		room.SetEditSelectedDay ("Tuesday");
		var shortened = room.GetState ().PropertyValues;
		for (int index = 1; index <= 10; index++)
			{
			string prefix = "editSlot" + index.ToString (CultureInfo.InvariantCulture);
			Assert.That (shortened[prefix + "Visible"].GetValue<bool> (), Is.EqualTo (index <= visibleSlots));
			Assert.That (shortened[prefix + "Time"].GetValue<string> (), Is.EqualTo (index <= visibleSlots ? (index - 1).ToString ("00", CultureInfo.InvariantCulture) + ":00" : string.Empty));
			Assert.That (shortened[prefix + "Temperature"].GetValue<double> (), Is.EqualTo (index <= visibleSlots ? 16 + (index - 1) * 0.5 : 0));
			}
		room.SetEditSelectedDay ("Monday");
		var expanded = room.GetState ().PropertyValues;
		for (int index = 1; index <= 10; index++)
			{
			string prefix = "editSlot" + index.ToString (CultureInfo.InvariantCulture);
			Assert.That (expanded[prefix + "Visible"].GetValue<bool> (), Is.True);
			Assert.That (expanded[prefix + "Time"].GetValue<string> (), Is.EqualTo ((index - 1).ToString ("00", CultureInfo.InvariantCulture) + ":00"));
			Assert.That (expanded[prefix + "Temperature"].GetValue<double> (), Is.EqualTo (16 + (index - 1) * 0.5));
			}
		Assert.That (_transport.ScheduleWrites, Is.Zero);
		}
	}