// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

namespace WiserHeatCrestronDriver.ControlProbe;

public enum HotWaterControlPolicy { Schedule, ManualMode, ManualOverride }
public sealed record HotWaterRestorePlan (int Id, HotWaterControlPolicy Policy, bool OriginallyOn, bool StoredOverrideOn, JsonElement Original)
	{
	/// <summary>Candidate compensations using the library's supported request shape. Actual hub acceptance is separately required.</summary>
	public JsonElement[] Requests => Policy == HotWaterControlPolicy.ManualOverride
		? [ManualRequest ()] : [ManualRequest (), JsonSerializer.SerializeToElement (new { RequestOverride = new { Type = "None" } })];
	private JsonElement ManualRequest () => JsonSerializer.SerializeToElement (new { RequestOverride = new { Type = "Manual", SetPoint = StoredOverrideOn ? 110 : -20 } });
	}

/// <summary>Capture and compare the original control policy, including latent override state. This class sends no requests.</summary>
public static class HotWaterRestoration
	{
	private static bool On (JsonElement value, string name) => value.GetProperty (name).GetString () switch
		{ "On" => true, "Off" => false, _ => throw new InvalidDataException ("Hot-water state must be explicitly On or Off.") };
	private static string? Text (JsonElement value, string name) => value.TryGetProperty (name, out var item) ? item.GetString () : null;
	private static void RequireUntimed (JsonElement value)
		{
		if (value.TryGetProperty ("OverrideTimeoutUnixTime", out var deadline) &&
			(!deadline.TryGetInt64 (out var seconds) || seconds != 0))
			throw new NotSupportedException ("Timed hot-water overrides require verified absolute-deadline restoration. No control may be sent by this plan.");
		foreach (var field in value.EnumerateObject ().Where (p => p.Name.Contains ("Boost", StringComparison.OrdinalIgnoreCase) ||
			p.Name.Contains ("Override", StringComparison.OrdinalIgnoreCase)))
			if (field.Name is not ("OverrideTimeoutUnixTime" or "OverrideType" or "OverrideWaterHeatingState"))
				throw new NotSupportedException ("An unrecognized hot-water override field needs an explicit preservation contract.");
		}
	public static HotWaterRestorePlan Capture (JsonElement domain)
		{
		var entries = domain.GetProperty ("HotWater").EnumerateArray ().ToArray ();
		if (entries.Length != 1) throw new InvalidDataException ("A gateway hot-water test requires exactly one identified controller.");
		var water = entries[0];
		int id = water.GetProperty ("id").GetInt32 ();
		if (id <= 0) throw new InvalidDataException ("Hot-water identity is invalid.");
		RequireUntimed (water);
		string? mode = Text (water, "Mode"), source = Text (water, "HotWaterDescription"), type = Text (water, "OverrideType");
		bool inactive = type is null or "None";
		HotWaterControlPolicy policy = (mode, source) switch
			{
			("Auto", "FromSchedule") when inactive => HotWaterControlPolicy.Schedule,
			("Manual", "FromManualMode") when inactive => HotWaterControlPolicy.ManualMode,
			("Auto", "FromManualOverride") when type is null or "Manual" => HotWaterControlPolicy.ManualOverride,
			_ => throw new NotSupportedException ("This hot-water mode/source/override combination does not have an implemented restoration plan.")
			};
		bool active = On (water, "WaterHeatingState"), stored = On (water, "OverrideWaterHeatingState");
		if (On (water, "HotWaterRelayState") != active || (policy == HotWaterControlPolicy.Schedule ? On (water, "ScheduledWaterHeatingState") : stored) != active)
			throw new InvalidDataException ("Hot-water target, control source and relay have not settled consistently.");
		if (policy == HotWaterControlPolicy.Schedule && water.GetProperty ("ScheduleId").GetInt32 () <= 0)
			throw new InvalidDataException ("Scheduled control requires an assigned schedule.");
		return new (id, policy, active, stored, water.Clone ());
		}

	public static void RequireRestored (HotWaterRestorePlan plan, JsonElement domain)
		{
		var current = Capture (domain);
		if (current.Id != plan.Id || current.Policy != plan.Policy || current.StoredOverrideOn != plan.StoredOverrideOn)
			throw new InvalidDataException ("The original hot-water identity, policy or latent manual state was not restored.");
		// Sensor/relay readings may follow a later schedule event; restore control policy, not historic time.
		if (plan.Policy != HotWaterControlPolicy.Schedule && current.OriginallyOn != plan.OriginallyOn)
			throw new InvalidDataException ("The original manual hot-water target was not restored.");
		string[] settings = ["Mode", "ScheduleId", "DeviceId", "AwayModeSuppressed"];
		foreach (string name in settings)
			{
			bool before = plan.Original.TryGetProperty (name, out var oldValue), now = current.Original.TryGetProperty (name, out var value);
			if (before != now || before && !JsonElement.DeepEquals (oldValue, value))
				throw new InvalidDataException ("An original hot-water setting changed.");
			}
		}
	}