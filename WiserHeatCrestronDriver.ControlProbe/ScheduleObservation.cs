// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiserHeatCrestronDriver.ControlProbe;

public static class ScheduleObservation
	{
	// Final Auto state must preserve these settings. Missing and present values are distinct.
	private static readonly string[] Settings = ["ScheduleId", "ManualSetPoint", "OccupiedHeatingSetPoint", "UnoccupiedHeatingSetPoint", "AwayModeSuppressed", "WindowDetectionActive", "ComfortModeEnabled", "EcoModeEnabled", "HVACMode"];
	public static int? ManualTarget (JsonElement room) =>
		 !room.TryGetProperty ("ManualSetPoint", out var value) || value.ValueKind == JsonValueKind.Null ? null : value.GetInt32 ();

	public static void ValidateCapture (JsonElement room, bool allowManualTargetInitialization = false)
		{
		if (room.GetProperty ("Mode").GetString () != "Auto" || room.GetProperty ("ScheduleId").GetInt32 () <= 0)
			throw new InvalidDataException ("Capture requires an assigned schedule in Auto mode.");
		ValidateIdle (room);
		ValidateScheduled (room);
		if (room.GetProperty ("CurrentSetPoint").GetInt32 () is < 55 or > 300)
			throw new InvalidDataException ("The room must have a normal heating setpoint.");
		// Some hubs create ManualSetPoint on the first mode change and cannot remove it again.
		if (!room.TryGetProperty ("ManualSetPoint", out var manual) || manual.ValueKind == JsonValueKind.Null)
			{
			if (allowManualTargetInitialization)
				return;
			throw new NotSupportedException ("No saved manual target: the hub can create one during this test, but removing it is not supported. No control was sent.");
			}
		if (!manual.TryGetInt32 (out var setpoint)
			|| setpoint < 50 || setpoint > 300)
			throw new InvalidDataException ("An existing manual setpoint between 5 and 30 degrees Celsius is required.");
		}
	// Mode changes can initialize ManualSetPoint from the previously active target.
	// Accept that specific effect during the transition only; final restoration remains strict.
	public static bool ReadTransition (JsonElement original, JsonElement room, bool allowManualTargetInitialization = false)
		{
		ValidateCapture (original, allowManualTargetInitialization);
		if (ManualTarget (original) == null)
			{
			// Accept only initialization from the captured active target, never an arbitrary temperature.
			// If the hub removes it again in Auto, require the original absence/null representation.
			if (ManualTarget (room) == null)
				return Read (original, room, allowManualTargetInitialization);
			if (ManualTarget (room) != original.GetProperty ("CurrentSetPoint").GetInt32 ())
				throw new InvalidDataException ("The initialized manual target differs from the target at the mode transition.");
			var initialized = JsonNode.Parse (original.GetRawText ())!;
			initialized["ManualSetPoint"] = ManualTarget (room);
			return Read (JsonSerializer.SerializeToElement (initialized), room);
			}
		int manual = room.GetProperty ("ManualSetPoint").GetInt32 ();
		if (manual == original.GetProperty ("ManualSetPoint").GetInt32 ())
			return Read (original, room);
		if (manual != original.GetProperty ("CurrentSetPoint").GetInt32 ())
			throw new InvalidDataException ("The manual target is neither the original value nor the target at the mode transition.");
		var transition = JsonNode.Parse (original.GetRawText ())!;
		transition["ManualSetPoint"] = manual;
		return Read (JsonSerializer.SerializeToElement (transition), room);
		}
	public static bool Read (JsonElement original, JsonElement room, bool allowManualTargetInitialization = false)
		{
		ValidateCapture (original, allowManualTargetInitialization);
		if (room.GetProperty ("id").GetInt32 () != original.GetProperty ("id").GetInt32 ()
			|| room.GetProperty ("Name").GetString () != original.GetProperty ("Name").GetString ())
			throw new InvalidDataException ("Room identity changed.");
		foreach (var name in Settings)
			{
			bool before = original.TryGetProperty (name, out var oldValue);
			bool now = room.TryGetProperty (name, out var value);
			if (before != now || before && !JsonElement.DeepEquals (oldValue, value) && !IsManualTargetReading (original, room, name, oldValue, value))
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
	private static bool IsManualTargetReading (JsonElement original, JsonElement room, string name, JsonElement before, JsonElement now)
		{
		// These hub readings follow the active target and its occupancy offset in Manual.
		// They return to the schedule values in Auto; do not write either field directly.
		return room.GetProperty ("Mode").GetString () == "Manual"
			&& name is "OccupiedHeatingSetPoint" or "UnoccupiedHeatingSetPoint"
			&& before.TryGetInt32 (out var oldTarget) && now.TryGetInt32 (out var currentTarget)
			&& (long)currentTarget - oldTarget == (long)room.GetProperty ("ManualSetPoint").GetInt32 () - original.GetProperty ("CurrentSetPoint").GetInt32 ();
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