// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record RoomTemperatureRestorePlan (int RoomId, bool Scheduled, int? ManualTarget, JsonElement Original)
	{
	public JsonElement[] Requests => Scheduled
		? [JsonSerializer.SerializeToElement (new { RequestOverride = new { Type = "None" } })]
		: [JsonSerializer.SerializeToElement (new { RequestOverride = new { Type = "Manual", SetPoint = ManualTarget!.Value } }),
			JsonSerializer.SerializeToElement (new { RequestOverride = new { Type = "None" } })];
	}

/// <summary>Preserves a room's starting control policy, independently of its changing sensor readings.</summary>
public static class RoomTemperatureRestoration
	{
	private static readonly string[] OverrideFields = ["OverrideType", "OverrideSetpoint", "OverrideTimeoutUnixTime"];
	public static JsonElement Room (ScheduleHubSnapshot hub, int id) => hub.Domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("id").GetInt32 () == id);
	public static string Origin (JsonElement room)
		{
		var fields = room.EnumerateObject ().Where (p => p.Name.Equals ("SetpointOrigin", StringComparison.OrdinalIgnoreCase)).ToArray ();
		return fields.Length == 1 ? fields[0].Value.GetString () ?? throw new InvalidDataException ("Missing setpoint origin.")
			: throw new InvalidDataException ("Setpoint origin must be unambiguous.");
		}
	private static bool Inactive (JsonElement value) => value.ValueKind is JsonValueKind.Null or JsonValueKind.False ||
		value.ValueKind == JsonValueKind.Number && value.TryGetInt64 (out long n) && n == 0 ||
		value.ValueKind == JsonValueKind.String && value.GetString () is "" or "None";
	public static RoomTemperatureRestorePlan Capture (JsonElement room)
		{
		int id = room.GetProperty ("id").GetInt32 ();
		if (id <= 0 || room.GetProperty ("CurrentSetPoint").GetInt32 () is < 50 or > 350)
			throw new InvalidDataException ("An identified room and ordinary heating target are required.");
		foreach (var field in room.EnumerateObject ().Where (p => p.Name.Contains ("Override", StringComparison.OrdinalIgnoreCase) || p.Name.Contains ("Boost", StringComparison.OrdinalIgnoreCase)))
			if (!Inactive (field.Value))
				throw new NotSupportedException ("An existing override requires its own verified restoration plan; no test input may replace it.");
		bool scheduled = room.GetProperty ("Mode").GetString () switch
			{
				"Auto" => true,
				"Manual" => false,
				_ => throw new NotSupportedException ("The initial room mode is not supported by this restoration plan.")
				};
		int? manual = ScheduleObservation.ManualTarget (room);
		if (manual.HasValue && manual.Value is < 50 or > 350)
			throw new InvalidDataException ("Invalid saved manual target.");
		if (Origin (room) != (scheduled ? "FromSchedule" : "FromManualMode") ||
			(scheduled ? room.GetProperty ("ScheduleId").GetInt32 () <= 0 || room.GetProperty ("ScheduledSetPoint").GetInt32 () != room.GetProperty ("CurrentSetPoint").GetInt32 ()
			: !manual.HasValue || manual.Value != room.GetProperty ("CurrentSetPoint").GetInt32 ()))
			throw new InvalidDataException ("Room mode, target and control source must agree before input.");
		return new (id, scheduled, manual, room.Clone ());
		}
	public static void RequireRestored (RoomTemperatureRestorePlan plan, JsonElement room)
		{
		var current = Capture (room);
		if (current.RoomId != plan.RoomId || current.Scheduled != plan.Scheduled || current.ManualTarget != plan.ManualTarget)
			throw new InvalidDataException ("The original room policy or saved manual target was not restored.");
		foreach (string key in new[] { "Name", "Mode", "ScheduleId", "ManualSetPoint", "AwayModeSuppressed", "WindowDetectionActive", "ComfortModeEnabled", "EcoModeEnabled", "HVACMode" })
			{
			bool before = plan.Original.TryGetProperty (key, out var oldValue), now = room.TryGetProperty (key, out var value);
			if (before != now || before && !JsonElement.DeepEquals (oldValue, value))
				throw new InvalidDataException ("An original room setting changed.");
			}
		}
	public static void RequireGuarded (RoomTemperatureRestorePlan plan, GatewayAwaySnapshot original, GatewayAwaySnapshot current)
		{
		if (GatewayAwayCycle.IsAway (original) != GatewayAwayCycle.IsAway (current))
			throw new InvalidDataException ("Whole-house Away mode changed.");
		var room = Room (current.Hub, plan.RoomId);
		foreach (var field in room.EnumerateObject ().Where (p => p.Name.Contains ("Override", StringComparison.OrdinalIgnoreCase) || p.Name.Contains ("Boost", StringComparison.OrdinalIgnoreCase)))
			if (!OverrideFields.Contains (field.Name, StringComparer.Ordinal) && !Inactive (field.Value))
				throw new InvalidDataException ("An unrecognized room override prevents automatic compensation.");
		// Compare all other rooms, schedules, gateway and hot-water settings through the existing isolation guard.
		// Only the selected room's declared request fields may differ. Manual target is mutable only in Manual mode.
		var adjusted = JsonNode.Parse (original.Hub.Domain.GetRawText ())!;
		var selected = adjusted["Room"]!.AsArray ().Single (r => r!["id"]!.GetValue<int> () == plan.RoomId)!.AsObject ();
		foreach (string name in OverrideFields.Concat (plan.Scheduled ? [] : new[] { "ManualSetPoint" }))
			{
			if (room.TryGetProperty (name, out var value))
				selected[name] = JsonNode.Parse (value.GetRawText ());
			else
				selected.Remove (name);
			}
		var expected = original with
			{
			Hub = original.Hub with
				{
				Domain = JsonSerializer.SerializeToElement (adjusted)
				}
			};
		GatewayAwayCycle.RequirePreserved (expected, current);
		}
	}