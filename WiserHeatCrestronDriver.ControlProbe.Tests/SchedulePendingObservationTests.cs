// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class SchedulePendingObservationTests
	{
	private const string SCHEDULES = """{"Heating":[{"id":8,"Name":"Room","Monday":{"Time":[430],"DegreesC":[220]},"CurrentSetpoint":220},{"id":9,"Name":"Other","Monday":{"Time":[0],"DegreesC":[180]}}],"OnOff":[]}""";
	private const string DOMAIN = """{"Room":[{"id":4,"Name":"Room","ScheduleId":8,"Mode":"Auto","ManualSetPoint":220,"CurrentSetPoint":220},{"id":5,"Name":"Other","ScheduleId":9,"Mode":"Auto"}]}""";
	private static JsonElement Element (string json) => JsonSerializer.Deserialize<JsonElement> (json);
	private static ScheduleHubSnapshot Hub () => new (Element (SCHEDULES), Element (DOMAIN));
	private static JsonObject Editor ()
		{
		var editor = new JsonObject { ["editSelectedDay"] = "Monday", ["editScheduleEnabled"] = true, ["editScheduleError"] = "" };
		for (int i = 1; i <= 8; i++)
			{
			editor[$"editSlot{i}Visible"] = i == 1;
			editor[$"editSlot{i}Time"] = "04:30";
			editor[$"editSlot{i}Temperature"] = 22m;
			}
		return editor;
		}

	private static IEnumerable<TestCaseData> EditorChanges ()
		{
		yield return new ("editSelectedDay", "\"Tuesday\"");
		yield return new ("editScheduleEnabled", "false");
		yield return new ("editScheduleError", "\"Conflict\"");
		for (int i = 1; i <= 8; i++)
			{
			yield return new ($"editSlot{i}Visible", i == 1 ? "false" : "true");
			yield return new ($"editSlot{i}Time", "\"05:00\"");
			yield return new ($"editSlot{i}Temperature", "22.5");
			}
		}

	[TestCaseSource (nameof (EditorChanges))]
	public void PeerPendingChangesAreRejectedIncludingInvisibleSlots (string name, string value)
		{
		var editor = Editor ();
		var observation = new SchedulePendingObservation (Hub (), JsonSerializer.SerializeToElement (editor), 8);
		editor[name] = JsonNode.Parse (value);
		Assert.Throws<InvalidDataException> (() => observation.RequirePreserved (Hub (), JsonSerializer.SerializeToElement (editor)));
		}

	[Test]
	public void CalculatedScheduleAndRoomFeedbackCanAdvanceWithoutChangingStoredPolicy ()
		{
		var editor = JsonSerializer.SerializeToElement (Editor ());
		var observation = new SchedulePendingObservation (Hub (), editor, 8);
		var after = new ScheduleHubSnapshot (Element (SCHEDULES.Replace ("\"CurrentSetpoint\":220", "\"CurrentSetpoint\":170")),
			Element (DOMAIN.Replace ("\"CurrentSetPoint\":220", "\"CurrentSetPoint\":170")));
		Assert.DoesNotThrow (() => observation.RequirePreserved (after, editor));
		}

	[TestCase (0), TestCase (1)]
	public void EditingEitherSavedScheduleIsRejected (int index)
		{
		var editor = JsonSerializer.SerializeToElement (Editor ());
		var observation = new SchedulePendingObservation (Hub (), editor, 8);
		var schedules = JsonNode.Parse (SCHEDULES)!;
		schedules["Heating"]![index]!["Monday"]!["DegreesC"]![0] = 215;
		Assert.Throws<InvalidDataException> (() => observation.RequirePreserved (new (JsonSerializer.SerializeToElement (schedules), Hub ().Domain), editor));
		}

	[TestCase ("Name", "\"Renamed\"")]
	[TestCase ("ScheduleId", "9")]
	[TestCase ("Mode", "\"Manual\"")]
	[TestCase ("ManualSetPoint", "215")]
	public void RoomPolicyChangesAreRejected (string name, string value)
		{
		var editor = JsonSerializer.SerializeToElement (Editor ());
		var observation = new SchedulePendingObservation (Hub (), editor, 8);
		var domain = JsonNode.Parse (DOMAIN)!;
		domain["Room"]![0]![name] = JsonNode.Parse (value);
		Assert.Throws<InvalidDataException> (() => observation.RequirePreserved (new (Hub ().Schedules, JsonSerializer.SerializeToElement (domain)), editor));
		}

	[Test]
	public void AlreadyDirtyPeerCannotBecomeBaseline ()
		{
		var editor = Editor ();
		editor["editSlot1Temperature"] = 22.5m;
		Assert.Throws<InvalidDataException> (() => new SchedulePendingObservation (Hub (), JsonSerializer.SerializeToElement (editor), 8));
		}
	}