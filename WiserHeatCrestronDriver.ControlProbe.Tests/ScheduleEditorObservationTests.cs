// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class ScheduleEditorObservationTests
	{
	private const string SCHEDULES = """{"Heating":[{"id":8,"Name":"Shared","Monday":{"Time":[430,2230],"DegreesC":[220,170]},"Next":{"Time":2230},"CurrentSetpoint":220},{"id":9,"Name":"Other","Monday":{"Time":[0],"DegreesC":[180]}}],"OnOff":[],"Smart":[]}""";
	private static JsonElement Read (string text) => JsonSerializer.Deserialize<JsonElement> (text);
	private static JsonObject Editor ()
		{
		var result = new JsonObject { ["editSelectedDay"] = "Monday", ["editScheduleEnabled"] = true, ["editScheduleError"] = "" };
		for (int index = 1; index <= 8; index++)
			{
			result[$"editSlot{index}Visible"] = index <= 2;
			result[$"editSlot{index}Time"] = index == 1 ? "04:30" : "22:30";
			result[$"editSlot{index}Temperature"] = index == 1 ? 22m : 17m;
			}
		return result;
		}

	[Test]
	public void ScheduleClockReadingsDoNotMaskStoredChanges ()
		{
		var before = ScheduleEditorObservation.PersistentSchedules (Read (SCHEDULES));
		var changed = JsonNode.Parse (SCHEDULES)!;
		changed["Heating"]![0]!["CurrentSetpoint"] = 170;
		changed["Heating"]![0]!["Next"]!["Time"] = 430;
		Assert.That (JsonElement.DeepEquals (before, ScheduleEditorObservation.PersistentSchedules (JsonSerializer.SerializeToElement (changed))), Is.True);
		changed["Heating"]![1]!["Monday"]!["DegreesC"]![0] = 190;
		Assert.That (JsonElement.DeepEquals (before, ScheduleEditorObservation.PersistentSchedules (JsonSerializer.SerializeToElement (changed))), Is.False, "Changes to another shared schedule must be detected.");
		}

	[Test]
	public void DuplicateScheduleIdsAreRejected () => Assert.Throws<InvalidDataException> (() => ScheduleEditorObservation.PersistentSchedules (Read (SCHEDULES.Replace ("\"id\":9", "\"id\":8"))));

	[Test]
	public void EditorMatchesIndependentHubValuesAndVisibility () => Assert.DoesNotThrow (() => ScheduleEditorObservation.RequireMatchesHub (JsonSerializer.SerializeToElement (Editor ()), Read (SCHEDULES), 8));

	[TestCase ("editSlot1Time", "\"00:00\"")]
	[TestCase ("editSlot2Temperature", "22")]
	[TestCase ("editSlot3Visible", "true")]
	[TestCase ("editSlot2Visible", "false")]
	[TestCase ("editScheduleError", "\"Error\"")]
	public void WrongEditorStateCannotPass (string property, string value)
		{
		var editor = Editor ();
		editor[property] = JsonNode.Parse (value);
		Assert.Throws<InvalidDataException> (() => ScheduleEditorObservation.RequireMatchesHub (JsonSerializer.SerializeToElement (editor), Read (SCHEDULES), 8));
		}

	[Test]
	public void MismatchedHubArraysAreNotSilentlyTruncated () => Assert.Throws<InvalidDataException> (() => ScheduleEditorObservation.RequireMatchesHub (JsonSerializer.SerializeToElement (Editor ()), Read (SCHEDULES.Replace ("[220,170]", "[220]")), 8));

	[Test]
	public void ReassignedRoomIsDetected ()
		{
		var before = Read ("""{"Room":[{"id":8,"Name":"Room","ScheduleId":8,"Mode":"Auto","ManualSetPoint":220,"CurrentSetPoint":170}]}""");
		var after = Read (before.GetRawText ().Replace ("\"ScheduleId\":8", "\"ScheduleId\":9"));
		Assert.That (JsonElement.DeepEquals (ScheduleEditorObservation.RoomAssignments (before), ScheduleEditorObservation.RoomAssignments (after)), Is.False);
		}
	}