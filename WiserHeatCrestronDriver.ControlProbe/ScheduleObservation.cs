// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

namespace WiserHeatCrestronDriver.ControlProbe;

public static class ScheduleObservation
	{
	// These settings must survive both mode transitions. Missing and present values are distinct.
	private static readonly string[] Settings = ["ScheduleId", "ManualSetPoint", "OccupiedHeatingSetPoint", "UnoccupiedHeatingSetPoint", "AwayModeSuppressed", "WindowDetectionActive", "ComfortModeEnabled", "EcoModeEnabled", "HVACMode"];
	public static void ValidateCapture (JsonElement room)
		{
		if (room.GetProperty ("Mode").GetString () != "Auto" || room.GetProperty ("ScheduleId").GetInt32 () <= 0)
			throw new InvalidDataException ("Capture requires an assigned schedule in Auto mode.");
		ValidateIdle (room);
		ValidateScheduled (room);
		if (room.GetProperty ("CurrentSetPoint").GetInt32 () is < 55 or > 300)
			throw new InvalidDataException ("The room must have a normal heating setpoint.");
		// Some hubs create ManualSetPoint on the first mode change and cannot remove it again.
		// Existing manual and scheduled targets are independent; either may be higher.
		if (!room.TryGetProperty ("ManualSetPoint", out var manual) || !manual.TryGetInt32 (out var setpoint)
			|| setpoint < 50 || setpoint > 300)
			throw new InvalidDataException ("An existing manual setpoint between 5 and 30 degrees Celsius is required.");
		}
	public static bool Read (JsonElement original, JsonElement room)
		{
		ValidateCapture (original);
		if (room.GetProperty ("id").GetInt32 () != original.GetProperty ("id").GetInt32 ()
			|| room.GetProperty ("Name").GetString () != original.GetProperty ("Name").GetString ())
			throw new InvalidDataException ("Room identity changed.");
		foreach (var name in Settings)
			{
			bool before = original.TryGetProperty (name, out var oldValue);
			bool now = room.TryGetProperty (name, out var value);
			if (before != now || before && !JsonElement.DeepEquals (oldValue, value))
				throw new InvalidDataException ("A room setting changed outside the selected mode transition.");
			}
		ValidateIdle (room);
		string? mode = room.GetProperty ("Mode").GetString ();
		if (mode == "Auto")
			{
			ValidateScheduled (room);
			return true;
			}
		if (mode != "Manual")
			throw new InvalidDataException ("Unexpected room mode.");
		if (room.GetProperty ("SetpointOrigin").GetString () != "FromManualMode"
			|| room.GetProperty ("CurrentSetPoint").GetInt32 () != room.GetProperty ("ManualSetPoint").GetInt32 ())
			throw new InvalidDataException ("Manual mode must use the unchanged manual setpoint.");
		return false;
		}
	private static void ValidateIdle (JsonElement room)
		{
		foreach (var origin in room.EnumerateObject ().Where (p => p.Name.Equals ("SetpointOrigin", StringComparison.OrdinalIgnoreCase)))
			if (origin.Value.GetString () is not ("FromSchedule" or "FromManualMode"))
				throw new InvalidDataException ("The room is controlled by another mode or override.");
		foreach (var property in room.EnumerateObject ())
			{
			if (property.Name.Contains ("Override", StringComparison.OrdinalIgnoreCase) || property.Name.Contains ("Boost", StringComparison.OrdinalIgnoreCase))
				{
				if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.False)
					continue;
				if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64 (out var n) && n == 0)
					continue;
				if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString () is "" or "None")
					continue;
				throw new InvalidDataException ("An override or boost prevents mode restoration.");
				}
			}
		}
	private static void ValidateScheduled (JsonElement room)
		{
		var origins = room.EnumerateObject ().Where (p => p.Name.Equals ("SetpointOrigin", StringComparison.OrdinalIgnoreCase)).ToArray ();
		if (origins.Length != 1 || origins[0].Value.GetString () != "FromSchedule"
			|| room.GetProperty ("CurrentSetPoint").GetInt32 () != room.GetProperty ("ScheduledSetPoint").GetInt32 ())
			throw new InvalidDataException ("Auto mode must be following the current schedule without an override.");
		}
	}
