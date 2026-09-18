// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
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