// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiserHeatCrestronDriver.ControlProbe;

public static class ScheduleEditorObservation
	{
	public static JsonElement PersistentSchedules (JsonElement schedules)
		{
		var copy = JsonNode.Parse (schedules.GetRawText ())!.AsObject ();
		foreach (string group in new[] { "Heating", "OnOff" })
			{
			var entries = copy[group]?.AsArray () ?? throw new InvalidDataException ("Missing schedule group.");
			var ids = new HashSet<int> ();
			foreach (var entry in entries)
				{
				var schedule = entry!.AsObject ();
				if (!ids.Add (schedule["id"]!.GetValue<int> ()))
					throw new InvalidDataException ("Duplicate schedule identity.");
				// Calculated current/next readings advance without modifying the stored schedule.
				foreach (string name in new[] { "ActiveSetpoints", "CurrentSetpoint", "CurrentState", "Next" })
					schedule.Remove (name);
				}
			copy[group] = new JsonArray (entries.OrderBy (entry => entry!["id"]!.GetValue<int> ()).Select (entry => entry!.DeepClone ()).ToArray ());
			}
		return JsonSerializer.SerializeToElement (copy);
		}

	public static JsonElement RoomAssignments (JsonElement domain) => JsonSerializer.SerializeToElement (
		domain.GetProperty ("Room").EnumerateArray ().OrderBy (room => room.GetProperty ("id").GetInt32 ()).Select (room =>
			room.EnumerateObject ().Where (property => property.Name is "id" or "Name" or "ScheduleId" or "Mode" or "ManualSetPoint")
				.ToDictionary (property => property.Name, property => property.Value.Clone ())));

	public static JsonElement Editor (IReadOnlyDictionary<string, JsonElement> properties)
		{
		string[] keys = ["editSelectedDay", "editScheduleEnabled", "editScheduleError", .. Enumerable.Range (1, 8)
			.SelectMany (index => new[] { $"editSlot{index}Visible", $"editSlot{index}Time", $"editSlot{index}Temperature" })];
		return JsonSerializer.SerializeToElement (keys.ToDictionary (key => key, key => properties[key]));
		}

	public static void RequireMatchesHub (JsonElement editor, JsonElement schedules, int scheduleId)
		{
		if (!editor.GetProperty ("editScheduleEnabled").GetBoolean () || !string.IsNullOrEmpty (editor.GetProperty ("editScheduleError").GetString ()))
			throw new InvalidDataException ("The editor is unavailable or reports an error.");
		string day = editor.GetProperty ("editSelectedDay").GetString ()!;
		if (!Enum.GetNames<DayOfWeek> ().Contains (day, StringComparer.Ordinal))
			throw new InvalidDataException ("Unknown editor day.");
		var schedule = schedules.GetProperty ("Heating").EnumerateArray ().Single (item => item.GetProperty ("id").GetInt32 () == scheduleId);
		var entries = schedule.GetProperty (day);
		var times = entries.GetProperty ("Time").EnumerateArray ().ToArray ();
		var temperatures = entries.GetProperty ("DegreesC").EnumerateArray ().ToArray ();
		if (times.Length is < 1 or > 8 || times.Length != temperatures.Length)
			throw new InvalidDataException ("The hub schedule cannot be represented by this editor.");
		for (int index = 0; index < 8; index++)
			{
			string prefix = "editSlot" + (index + 1).ToString (CultureInfo.InvariantCulture);
			if (editor.GetProperty (prefix + "Visible").GetBoolean () != (index < times.Length))
				throw new InvalidDataException ("The editor's conditional slots differ from the hub schedule.");
			if (index >= times.Length)
				continue;
			int time = times[index].GetInt32 ();
			if (time is < 0 or > 2330 || time % 100 is not (0 or 30))
				throw new InvalidDataException ("The schedule contains a time outside the selector's supported choices.");
			string expectedTime = (time / 100).ToString ("00", CultureInfo.InvariantCulture) + ":" + (time % 100).ToString ("00", CultureInfo.InvariantCulture);
			if (editor.GetProperty (prefix + "Time").GetString () != expectedTime ||
				 editor.GetProperty (prefix + "Temperature").GetDecimal () != temperatures[index].GetDecimal () / 10m)
				throw new InvalidDataException ("The editor's values differ from the hub schedule.");
			}
		}
	}