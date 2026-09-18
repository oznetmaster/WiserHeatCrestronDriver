// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

using Crestron.DeviceDrivers.EntityModel.Data;
using Crestron.DeviceDrivers.SDK.EntityModel;

using NUnit.Framework;

using WiserHeat.CrestronDriver;

using WiserHeatApiV2;

namespace WiserHeatCrestronDriver.Tests;

public sealed partial class PlatformDiscoveryTests
	{
	[Test]
	public async Task OffControls_BindToPublishedVisibilityAndExplicitCommands ()
		{
		await Refresh ("""[{"id":4,"Name":"Synthetic","Mode":"Manual","CurrentSetPoint":-200}]""");
		var state = Entities["room_4"].GetState ();
		var ui = XDocument.Load (Path.Combine (TestSupport.DataDirectory, "room", "uidefinitions", "UiDefinition.xml"));
		XElement Control (string id) => ui.Descendants ().Single (element => (string)element.Attribute ("id") == id);
		Assert.That ((string)Control ("RoomThermostat").Attribute ("heatmodeenabled"), Is.EqualTo ("{hasHeatingTarget}"));
		Assert.That ((string)Control ("HeatingOffStatus").Attribute ("visible"), Is.EqualTo ("{isHeatingOff}"));
		Assert.That (state.PropertyValues["hasHeatingTarget"].GetValue<bool> (), Is.False);
		Assert.That (state.Definition.Commands.Keys, Does.Contain ("resumeHeating"));
		for (int slot = 1; slot <= 10; slot++)
			{
			Assert.That ((string)Control ("EditSlot" + slot + "Temperature").Attribute ("visible"), Is.EqualTo ("{editSlot" + slot + "HasTemperature}"));
			var off = Control ("EditSlot" + slot + "Off");
			Assert.That ((string)off.Attribute ("visible"), Is.EqualTo ("{editSlot" + slot + "IsOff}"));
			Assert.That ((string)off.Attribute ("buttonaction"), Is.EqualTo ("command:resumeEditSlot" + slot));
			Assert.That (state.Definition.Commands.Keys, Does.Contain ("resumeEditSlot" + slot));
			Assert.That (state.Definition.Properties.Keys, Does.Contain ("editSlot" + slot + "IsOff").And.Contain ("editSlot" + slot + "HasTemperature"));
			}
		Assert.That (TemperatureCommand (File.ReadAllText (Path.Combine (TestSupport.DataDirectory, "translations", "en-US.json"))).Element ("HeatingOffLabel").Value, Is.EqualTo ("Off"));
		}

	[TestCase ("Celsius", "Set to 5°C", 5)]
	[TestCase ("Fahrenheit", "Set to 41°F", 41)]
	public async Task OffTarget_RemainsOffUntilTheExplicitResumeAction (string units, string label, double minimum)
		{
		Set ("_temperatureUnits", units);
		_transport.AllowRoomCommands = true;
		await Refresh ("""[{"id":4,"Name":"Synthetic","Mode":"Manual","CalculatedTemperature":200,"CurrentSetPoint":-200}]""");
		var room = Entities["room_4"];
		Assert.That (room.TargetTemperature, Is.EqualTo (Constants.TEMP_OFF));
		Assert.That (room.IsHeatingOff, Is.True);
		Assert.That (room.HasHeatingTarget, Is.False);
		Assert.That (room.MinimumHeatingLabel, Is.EqualTo (label));
		Assert.That (_transport.RoomCommands, Is.Zero);
		room.ResumeHeating ();
		await TestSupport.Complete (WaitForCompletion (room));
		Assert.That ((int)TemperatureCommand (_transport.LastRoomWrite).Element ("RequestOverride").Element ("SetPoint"), Is.EqualTo (50));
		_transport.Rooms = """[{"id":4,"Name":"Synthetic","Mode":"Manual","CalculatedTemperature":200,"CurrentSetPoint":50}]""";
		await _driver.RefreshSystemStateAsync (true);
		Assert.That (room.TargetTemperature, Is.EqualTo (minimum));
		Assert.That (room.HasHeatingTarget, Is.True);
		int writesBeforeStaleAction = _transport.RoomCommands;
		room.ResumeHeating ();
		Assert.That (_transport.RoomCommands, Is.EqualTo (writesBeforeStaleAction), "A stale Off button must not reset an active target.");
		}

	[TestCase ("Celsius")]
	[TestCase ("Fahrenheit")]
	public async Task OffScheduleSlots_RemainOffAcrossUnitTimeAndOtherSlotEdits (string units)
		{
		Set ("_temperatureUnits", units);
		_transport.AllowScheduleWrites = true;
		_transport.HeatingSchedules = """[{"id":7,"Name":"Synthetic","Monday":{"Time":[600,2200],"DegreesC":[-200,170]}}]""";
		await Refresh ("""[{"id":4,"Name":"Synthetic","Mode":"Auto","ScheduleId":7,"CurrentSetPoint":-200}]""");
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		Assert.That (room.EditSlot1Temperature, Is.EqualTo (Constants.TEMP_OFF));
		Assert.That (room.GetState ().PropertyValues["editSlot1IsOff"].GetValue<bool> (), Is.True);
		Assert.That (room.GetState ().PropertyValues["editSlot1HasTemperature"].GetValue<bool> (), Is.False);
		string newUnits = units == "Celsius" ? "Fahrenheit" : "Celsius";
		room.UpdateFromRoom (_api.Rooms.GetById (4), newUnits);
		room.SetEditSlot1Time ("06:30");
		room.SetEditSlot2Temperature (newUnits == "Celsius" ? 21 : 69.8);
		Assert.That (room.EditSlot1Temperature, Is.EqualTo (Constants.TEMP_OFF));
		room.SaveEditScheduleDay ();
		await TestSupport.Complete (WaitForCompletion (room));
		var monday = TemperatureCommand (_transport.LastScheduleWrite).Element ("Monday");
		Assert.That (monday.Element ("DegreesC").Elements ().Select (value => (int)value), Is.EqualTo (new[] { -200, 210 }));
		Assert.That (monday.Element ("Time").Elements ().Select (value => (int)value), Is.EqualTo (new[] { 630, 2200 }));
		}

	[TestCase ("Celsius", 5)]
	[TestCase ("Fahrenheit", 41)]
	public async Task OffScheduleSlots_ResumeOnlyTheSelectedPendingSlotAndCancelRestores (string units, double minimum)
		{
		Set ("_temperatureUnits", units);
		_transport.HeatingSchedules = "[{\"id\":7,\"Name\":\"Synthetic\",\"Monday\":{\"Time\":[0,100,200,300,400,500,600,700,800,900],\"DegreesC\":[-200,-200,-200,-200,-200,-200,-200,-200,-200,-200]}}]";
		await Refresh ("""[{"id":4,"Name":"Synthetic","Mode":"Auto","ScheduleId":7,"CurrentSetPoint":-200}]""");
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		for (int slot = 1; slot <= 10; slot++)
			{
			var completion = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
			room.ExecuteCommand ("resumeEditSlot" + slot, new Dictionary<string, DriverEntityValue> (), result => completion.TrySetResult (result.Failed));
			await TestSupport.Complete (completion.Task);
			Assert.That (await completion.Task, Is.False);
			var state = room.GetState ().PropertyValues;
			for (int index = 1; index <= 10; index++)
				{
				Assert.That (state["editSlot" + index + "Temperature"].GetValue<double> (), Is.EqualTo (index == slot ? minimum : Constants.TEMP_OFF));
				Assert.That (state["editSlot" + index + "IsOff"].GetValue<bool> (), Is.EqualTo (index != slot));
				}
			room.CancelEditSchedule ();
			Assert.That (room.GetState ().PropertyValues["editSlot" + slot + "IsOff"].GetValue<bool> (), Is.True);
			room.OpenEditSchedule ();
			}
		Assert.That (_transport.ScheduleWrites + _transport.RoomCommands, Is.Zero);
		}

	[TestCase ("Celsius", 2.5, 2.5, 25)]
	[TestCase ("Fahrenheit", 2.5, 2.5, 25)]
	[TestCase ("Fahrenheit", 10, 5, 50)]
	[TestCase ("Celsius", 0, 1, 10)]
	[TestCase ("Fahrenheit", double.NaN, 2, 20)]
	[TestCase ("Celsius", double.PositiveInfinity, 2, 20)]
	[TestCase ("Fahrenheit", double.NegativeInfinity, 2, 20)]
	public async Task BoostConfiguration_UsesFiniteCelsiusIncrementIndependentOfDisplayUnits (string units, double requested, double configured, int hubIncrement)
		{
		var values = new Dictionary<string, DriverEntityValue?>
			{
			["TemperatureUnits"] = new DriverEntityValue (units),
			["BoostDelta"] = new DriverEntityValue (requested),
			};
		// Connection step applies settings without starting background discovery.
		typeof (WiserPlatformDriver).GetMethod ("ApplyConfigurationItems", Private).Invoke (_driver,
			new object[] { DataDrivenConfigurationController.ApplyConfigurationAction.ApplyStep, "Connection", values });
		Assert.That (_driver.BoostDelta, Is.EqualTo (configured));
		_transport.AllowRoomCommands = true;
		await Refresh ("""[{"id":4,"Name":"Synthetic","Mode":"Manual","CurrentSetPoint":200,"SetPointOrigin":"FromManualMode"}]""");
		Assert.That (await _driver.TriggerRoomBoostAsync (4), Is.True);
		Assert.That (_transport.RoomCommands, Is.EqualTo (1));
		var command = TemperatureCommand (_transport.LastRoomWrite).Element ("RequestOverride");
		Assert.That ((string)command.Element ("Type"), Is.EqualTo ("Boost"));
		Assert.That ((int)command.Element ("IncreaseSetPointBy"), Is.EqualTo (hubIncrement));
		}

	private static XElement TemperatureCommand (string json)
		{
		using var reader = JsonReaderWriterFactory.CreateJsonReader (Encoding.UTF8.GetBytes (json), XmlDictionaryReaderQuotas.Max);
		return XElement.Load (reader);
		}

	[TestCase ("Celsius", 20, 21, 5, 30, 0.5)]
	[TestCase ("Fahrenheit", 68, 69.8, 41, 86, 0.9)]
	public async Task TemperatureDisplay_InitialReadingsAndAllRangesMatchUnits (string units, double current, double target, double minimum, double maximum, double step)
		{
		Set ("_temperatureUnits", units);
		_transport.HeatingSchedules = """[{"id":7,"Name":"Synthetic","Monday":{"Time":[600],"DegreesC":[210]}}]""";
		await Refresh ("""[{"id":4,"Name":"Synthetic","Mode":"Manual","ScheduleId":7,"CalculatedTemperature":200,"CurrentSetPoint":210}]""");
		var room = Entities["room_4"];
		Assert.That (room.CurrentTemperature, Is.EqualTo (current));
		Assert.That (room.TargetTemperature, Is.EqualTo (target));
		Assert.That (room.TemperatureUnits, Is.EqualTo (units));
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		Assert.That (room.EditSlot1Temperature, Is.EqualTo (target));
		var properties = room.GetState ().Definition.Properties;
		foreach (string id in new[] { "targetTemperature" }.Concat (Enumerable.Range (1, 10).Select (slot => $"editSlot{slot}Temperature")))
			{
			var definition = properties[id].TypeDef;
			Assert.That (definition.Range.Minimum, Is.EqualTo (minimum), id);
			Assert.That (definition.Range.Maximum, Is.EqualTo (maximum), id);
			Assert.That (definition.Range.StepSize, Is.EqualTo (step), id);
			}
		Assert.That (_transport.RoomCommands + _transport.ScheduleWrites, Is.Zero);
		}

	[Test]
	public async Task TemperatureDisplay_FahrenheitConnectionKeepsHubModelsInCelsius ()
		{
		Set ("_temperatureUnits", "Fahrenheit");
		WiserUnits? requested = null;
		using var transport = new SnapshotTransport { Rooms = """[{"id":4,"Name":"Synthetic","CalculatedTemperature":200,"CurrentSetPoint":210}]""" };
		_driver.ApiFactory = (_, _, units) =>
			{
			requested = units;
			return CreateApi (transport);
			};
		await TestSupport.Complete (Connect ());
		Assert.That (requested, Is.EqualTo (WiserUnits.Metric));
		Assert.That (Entities["room_4"].CurrentTemperature, Is.EqualTo (68));
		Assert.That (Entities["room_4"].TargetTemperature, Is.EqualTo (69.8));
		}

	[TestCase ("Celsius", 5, 50)]
	[TestCase ("Celsius", 21.5, 215)]
	[TestCase ("Celsius", 30, 300)]
	[TestCase ("Fahrenheit", 41, 50)]
	[TestCase ("Fahrenheit", 70.7, 215)]
	[TestCase ("Fahrenheit", 86, 300)]
	public async Task TemperatureDisplay_NativeCommandWritesTheEquivalentCelsiusValue (string units, double input, int expected)
		{
		Set ("_temperatureUnits", units);
		_transport.AllowRoomCommands = true;
		await Refresh ("""[{"id":4,"Name":"Synthetic","Mode":"Manual","CurrentSetPoint":190}]""");
		var room = Entities["room_4"];
		var completion = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		room.ExecuteCommand (ExtensionSetPropertyValueExecutor.CommandName,
			new Dictionary<string, DriverEntityValue>
				{
				["property"] = new DriverEntityValue ("targetTemperature"),
				["value"] = new DriverEntityValue (input.ToString (CultureInfo.InvariantCulture)),
				}, result => completion.TrySetResult (result.Failed));
		await TestSupport.Complete (completion.Task);
		Assert.That (await completion.Task, Is.False, "The extension UI property command must dispatch successfully.");
		await TestSupport.Complete (WaitForCompletion (room));
		Assert.That (_transport.RoomCommands, Is.EqualTo (1));
		Assert.That ((int)TemperatureCommand (_transport.LastRoomWrite).Element ("RequestOverride").Element ("SetPoint"), Is.EqualTo (expected));
		}

	[TestCase ("Celsius", 30.1)]
	[TestCase ("Celsius", 4.9)]
	[TestCase ("Fahrenheit", 86.1)]
	[TestCase ("Fahrenheit", 40.9)]
	[TestCase ("Fahrenheit", double.NaN)]
	[TestCase ("Celsius", double.PositiveInfinity)]
	public async Task TemperatureDisplay_InvalidNativeCommandDoesNotWriteOrChangeFeedback (string units, double input)
		{
		Set ("_temperatureUnits", units);
		await Refresh ("""[{"id":4,"Name":"Synthetic","Mode":"Manual","CurrentSetPoint":190}]""");
		var room = Entities["room_4"];
		double before = room.TargetTemperature;
		string activity = room.ControlStatus;
		room.SetTargetTemperature (input);
		Assert.That (room.TargetTemperature, Is.EqualTo (before));
		Assert.That (room.ControlStatus, Is.EqualTo (activity));
		Assert.That (_transport.RoomCommands, Is.Zero);
		}

	[Test]
	public async Task TemperatureDisplay_ChangingUnitsPreservesPendingEditorCelsiusAndSavesItExactly ()
		{
		_transport.AllowScheduleWrites = true;
		_transport.HeatingSchedules = """[{"id":7,"Name":"Synthetic","Monday":{"Time":[600,2200],"DegreesC":[210,170]}}]""";
		await Refresh ("""[{"id":4,"Name":"Synthetic","Mode":"Auto","ScheduleId":7,"CurrentSetPoint":210}]""");
		var room = Entities["room_4"];
		room.OpenEditSchedule ();
		room.SetEditSelectedDay ("Monday");
		room.SetEditSlot1Temperature (21.5);
		var before = room.GetState ().Definition.Properties["targetTemperature"].TypeDef.Range;
		Set ("_temperatureUnits", "Fahrenheit");
		room.UpdateFromRoom (_api.Rooms.GetById (4), "Fahrenheit");
		Assert.That (room.EditSlot1Temperature, Is.EqualTo (70.7));
		Assert.That (before.Maximum, Is.EqualTo (30), "Earlier definition snapshots must remain immutable.");
		Assert.That (room.GetState ().Definition.Properties["targetTemperature"].TypeDef.Range.Maximum, Is.EqualTo (86));
		room.SetEditSlot1Temperature (68.9);
		room.SaveEditScheduleDay ();
		await TestSupport.Complete (WaitForCompletion (room));
		Assert.That (_transport.ScheduleWrites, Is.EqualTo (1));
		Assert.That (TemperatureCommand (_transport.LastScheduleWrite).Element ("Monday").Element ("DegreesC").Elements ().Select (value => (int)value), Is.EqualTo (new[] { 205, 170 }));
		}
	}