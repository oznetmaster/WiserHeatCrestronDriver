// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class ScheduleObservationTests
	{
	private const string Original = """{"id":8,"Name":"Office","Mode":"Auto","ScheduleId":8,"ManualSetPoint":220,"CurrentSetPoint":220,"ScheduledSetPoint":220,"SetpointOrigin":"FromSchedule","OccupiedHeatingSetPoint":220,"UnoccupiedHeatingSetPoint":200,"AwayModeSuppressed":false}""";
	private static JsonElement Read (string text) => JsonSerializer.Deserialize<JsonElement> (text);
	private static JsonElement Changed (string name, string json)
		{
		var room = JsonNode.Parse (Original)!;
		room[name] = JsonNode.Parse (json);
		return Read (room.ToJsonString ());
		}
	[Test]
	public void ScheduleTransitionAndRestore_PreserveSettings ()
		{
		var original = Read (Original);
		var manual = Read (Original.Replace ("\"Auto\"", "\"Manual\"").Replace ("\"FromSchedule\"", "\"FromManualMode\""));
		Assert.That (ScheduleObservation.Read (original, manual), Is.False);
		Assert.That (ScheduleObservation.Read (original, original), Is.True);
		}
	[TestCase ("FromSchedule")]
	[TestCase ("FromManualOverride")]
	[TestCase ("FromBoost")]
	public void ManualModeRequiresObservedManualOrigin (string origin)
		=> Assert.Throws<InvalidDataException> (() => ScheduleObservation.Read (Read (Original), Read (Original.Replace ("\"Auto\"", "\"Manual\"").Replace ("FromSchedule", origin))));
	[Test]
	public void ManualModeRequiresUnchangedManualTarget ()
		=> Assert.Throws<InvalidDataException> (() => ScheduleObservation.Read (Read (Original), Read (Original.Replace ("\"Auto\"", "\"Manual\"").Replace ("FromSchedule", "FromManualMode").Replace ("\"CurrentSetPoint\":220", "\"CurrentSetPoint\":225"))));
	[TestCase ("ScheduleId", "9")]
	[TestCase ("id", "9")]
	[TestCase ("Name", "\"Other room\"")]
	[TestCase ("ManualSetPoint", "225")]
	[TestCase ("OccupiedHeatingSetPoint", "225")]
	[TestCase ("AwayModeSuppressed", "true")]
	[TestCase ("OverrideType", "\"Manual\"")]
	[TestCase ("OverrideTimeout", "60")]
	[TestCase ("SetpointOrigin", "\"FromBoost\"")]
	[TestCase ("SetpointOrigin", "\"FromAway\"")]
	[TestCase ("CurrentSetPoint", "225")]
	[TestCase ("Mode", "\"Off\"")]
	public void UnexpectedStateOrIdentityChange_IsRejected (string key, string json)
		=> Assert.Throws<InvalidDataException> (() => ScheduleObservation.Read (Read (Original), Changed (key, json)));
	[Test]
	public void ScheduleBoundary_UsesCurrentScheduledValue ()
		{
		var changed = Read (Original.Replace ("\"CurrentSetPoint\":220", "\"CurrentSetPoint\":200").Replace ("\"ScheduledSetPoint\":220", "\"ScheduledSetPoint\":200"));
		Assert.That (ScheduleObservation.Read (Read (Original), changed), Is.True);
		}
	[Test]
	public void ManualCapture_IsRejectedBeforeAnyCommand ()
		=> Assert.Throws<InvalidDataException> (() => ScheduleObservation.ValidateCapture (Changed ("Mode", "\"Manual\"")));
	[Test]
	public void AbsentManualSetpoint_IsRejectedBeforeHubCanCreateIt ()
		=> Assert.Throws<NotSupportedException> (() => ScheduleObservation.ValidateCapture (Read (Original.Replace ("\"ManualSetPoint\":220,", ""))));
	[TestCase (301)]
	[TestCase (0)]
	public void ManualSetpointOutsideHeatingRange_IsRejected (int value)
		=> Assert.Throws<InvalidDataException> (() => ScheduleObservation.ValidateCapture (Changed ("ManualSetPoint", value.ToString (System.Globalization.CultureInfo.InvariantCulture))));
	[TestCase ("ScheduleId", "0")]
	[TestCase ("Mode", "\"Manual\"")]
	[TestCase ("SetpointOrigin", "\"FromBoost\"")]
	[TestCase ("CurrentSetPoint", "400")]
	public void InitializationOption_PreservesCaptureGuards (string name, string json)
		{
		var room = JsonNode.Parse (Changed (name, json).GetRawText ())!.AsObject ();
		room.Remove ("ManualSetPoint");
		Assert.Throws<InvalidDataException> (() => ScheduleObservation.ValidateCapture (JsonSerializer.SerializeToElement (room), true));
		}
	[Test]
	public void InitializationOption_RequiresUnchangedAbsenceBeforeControl ()
		{
		var start = JsonNode.Parse (Original)!.AsObject ();
		start.Remove ("ManualSetPoint");
		var original = JsonSerializer.SerializeToElement (start);
		Assert.That (ScheduleObservation.Read (original, original, true), Is.True);
		Assert.Throws<InvalidDataException> (() => ScheduleObservation.Read (original, Read (Original), true));
		}
	[TestCase (170, 220)]
	[TestCase (220, 170)]
	[TestCase (220, 220)]
	public void DifferentManualAndScheduledTargets_AreObservedAndPreserved (int scheduled, int manual)
		{
		var start = JsonNode.Parse (Original)!;
		start["CurrentSetPoint"] = scheduled;
		start["ScheduledSetPoint"] = scheduled;
		start["ManualSetPoint"] = manual;
		var original = Read (start.ToJsonString ());
		ScheduleObservation.ValidateCapture (original);
		start["Mode"] = "Manual";
		start["SetpointOrigin"] = "FromManualMode";
		start["CurrentSetPoint"] = manual;
		Assert.That (ScheduleObservation.Read (original, Read (start.ToJsonString ())), Is.False);
		Assert.That (ScheduleObservation.Read (original, original), Is.True);
		}

	[TestCase (170, 220)]
	[TestCase (220, 170)]
	public void HubInitializingManualFromCurrent_IsAcceptedOnlyDuringTransition (int current, int manual)
		{
		var start = JsonNode.Parse (Original)!;
		start["CurrentSetPoint"] = current;
		start["ScheduledSetPoint"] = current;
		start["ManualSetPoint"] = manual;
		var original = JsonSerializer.SerializeToElement (start);
		start["Mode"] = "Manual";
		start["SetpointOrigin"] = "FromManualMode";
		start["ManualSetPoint"] = current;
		var changed = JsonSerializer.SerializeToElement (start);
		Assert.That (ScheduleObservation.ReadTransition (original, changed), Is.False);
		Assert.Throws<InvalidDataException> (() => ScheduleObservation.Read (original, changed));
		start["ManualSetPoint"] = 250;
		start["CurrentSetPoint"] = 250;
		Assert.Throws<InvalidDataException> (() => ScheduleObservation.ReadTransition (original, JsonSerializer.SerializeToElement (start)));
		}

	[TestCase (170)]
	[TestCase (220)]
	[TestCase (250)]
	public void ManualOccupancyReadings_FollowTargetOffsetButMustReturnInAuto (int manual)
		{
		var start = JsonNode.Parse (Original)!;
		start["ManualSetPoint"] = manual;
		var original = JsonSerializer.SerializeToElement (start);
		start["Mode"] = "Manual";
		start["SetpointOrigin"] = "FromManualMode";
		start["CurrentSetPoint"] = manual;
		start["OccupiedHeatingSetPoint"] = manual;
		start["UnoccupiedHeatingSetPoint"] = manual - 20;
		Assert.That (ScheduleObservation.Read (original, JsonSerializer.SerializeToElement (start)), Is.False);
		start["UnoccupiedHeatingSetPoint"] = manual - 10;
		Assert.Throws<InvalidDataException> (() => ScheduleObservation.Read (original, JsonSerializer.SerializeToElement (start)));
		start["UnoccupiedHeatingSetPoint"] = manual - 20;
		start["Mode"] = "Auto";
		start["SetpointOrigin"] = "FromSchedule";
		start["CurrentSetPoint"] = 220;
		if (manual != 220)
			Assert.Throws<InvalidDataException> (() => ScheduleObservation.Read (original, JsonSerializer.SerializeToElement (start)));
		start["OccupiedHeatingSetPoint"] = 220;
		start["UnoccupiedHeatingSetPoint"] = 200;
		Assert.That (ScheduleObservation.Read (original, JsonSerializer.SerializeToElement (start)), Is.True);
		}
	}