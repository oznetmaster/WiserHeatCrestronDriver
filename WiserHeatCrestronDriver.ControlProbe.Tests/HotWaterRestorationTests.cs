// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class HotWaterRestorationTests
	{
	private static JsonElement Domain (bool on = false, string mode = "Auto", string source = "FromSchedule", bool stored = false) =>
		JsonSerializer.SerializeToElement (new { HotWater = new[] { new { id = 2, DeviceId = 0, ScheduleId = 1000, Mode = mode,
			HotWaterDescription = source, WaterHeatingState = on ? "On" : "Off", HotWaterRelayState = on ? "On" : "Off",
			ScheduledWaterHeatingState = on ? "On" : "Off", OverrideWaterHeatingState = stored ? "On" : "Off" } } });
	private static JsonElement Edit (JsonElement original, string name, JsonNode? value)
		{
		var root = JsonNode.Parse (original.GetRawText ())!; root["HotWater"]![0]![name] = value;
		return JsonSerializer.SerializeToElement (root);
		}
	[TestCase (false, false), TestCase (false, true), TestCase (true, false), TestCase (true, true)]
	public void ScheduledStateCancelsTemporaryOverride (bool on, bool stored)
		{
		var domain = Domain (on, stored: stored); var plan = HotWaterRestoration.Capture (domain);
		Assert.That (plan.Policy, Is.EqualTo (HotWaterControlPolicy.Schedule));
		Assert.That (plan.Requests.Length, Is.EqualTo (1));
		Assert.That (plan.Requests[0].GetProperty ("RequestOverride").GetProperty ("Type").GetString (), Is.EqualTo ("None"));
		HotWaterRestoration.RequireRestored (plan, domain);
		}
	[TestCase (false), TestCase (true)]
	public void ManualOverrideMustRemainAnOverride (bool on)
		{
		var domain = Domain (on, source: "FromManualOverride", stored: on); var plan = HotWaterRestoration.Capture (domain);
		Assert.That (plan.Policy, Is.EqualTo (HotWaterControlPolicy.ManualOverride));
		Assert.That (plan.Requests.Length, Is.EqualTo (1), "Cancelling would destroy the original override.");
		HotWaterRestoration.RequireRestored (plan, domain);
		Assert.Throws<InvalidDataException> (() => HotWaterRestoration.RequireRestored (plan, Domain (on, stored: on)));
		}
	[TestCase (false), TestCase (true)]
	public void ManualModeMustRemainManual (bool on)
		{
		var domain = Domain (on, "Manual", "FromManualMode", on); var plan = HotWaterRestoration.Capture (domain);
		Assert.That (plan.Policy, Is.EqualTo (HotWaterControlPolicy.ManualMode));
		HotWaterRestoration.RequireRestored (plan, domain);
		Assert.Throws<InvalidDataException> (() => HotWaterRestoration.RequireRestored (plan, Domain (on, stored: on)));
		}
	[Test]
	public void ReturningTheButtonToOffIsNotScheduleRestoration ()
		{
		var plan = HotWaterRestoration.Capture (Domain ());
		Assert.Throws<InvalidDataException> (() => HotWaterRestoration.RequireRestored (plan, Domain (source: "FromManualOverride")));
		}
	[Test]
	public void LaterScheduleEventCanLegitimatelyChangeRelayState ()
		{
		var plan = HotWaterRestoration.Capture (Domain ());
		Assert.DoesNotThrow (() => HotWaterRestoration.RequireRestored (plan, Domain (true)));
		}
	[Test]
	public void ClearingScheduleOverrideCanRemoveInactiveFields ()
		{
		var plan = HotWaterRestoration.Capture (Domain (true));
		var current = JsonNode.Parse (Domain (true).GetRawText ())!;
		current["HotWater"]![0]!.AsObject ().Remove ("OverrideWaterHeatingState");
		current["HotWater"]![0]!["OverrideType"] = "None";
		current["HotWater"]![0]!["AwayModeSuppressed"] = false;
		Assert.DoesNotThrow (() => HotWaterRestoration.RequireRestored (plan, JsonSerializer.SerializeToElement (current)));
		current["HotWater"]![0]!["HotWaterDescription"] = "FromManualOverride";
		current["HotWater"]![0]!["OverrideType"] = "Manual";
		Assert.Throws<InvalidDataException> (() => HotWaterRestoration.Capture (JsonSerializer.SerializeToElement (current)));
		}
	[TestCase (1234), TestCase (9999999999L), TestCase (-1)]
	public void TimedOverridesAreRejectedBeforeAnyMutationPlan (long deadline)
		{
		Assert.Throws<NotSupportedException> (() => HotWaterRestoration.Capture (Edit (Domain (), "OverrideTimeoutUnixTime", JsonValue.Create (deadline))));
		}
	[TestCase ("BoostDurationMinutes", "30"), TestCase ("OverrideType", "\"Boost\""), TestCase ("HotWaterDescription", "\"FromAwayMode\""), TestCase ("Mode", "\"Unknown\"")]
	public void UnknownOrConflictingPolicyCannotBeTreatedAsScheduled (string name, string json)
		{
		Assert.Throws<NotSupportedException> (() => HotWaterRestoration.Capture (Edit (Domain (), name, JsonNode.Parse (json))));
		}
	[TestCase ("ScheduleId", "1001"), TestCase ("DeviceId", "1"), TestCase ("AwayModeSuppressed", "true")]
	public void SettingsChangesAreNotRestoration (string name, string json)
		{
		var original = Domain (); var plan = HotWaterRestoration.Capture (original);
		Assert.Throws<InvalidDataException> (() => HotWaterRestoration.RequireRestored (plan, Edit (original, name, JsonNode.Parse (json))));
		}
	[Test]
	public void RelayMustAgreeWithTheTarget ()
		{
		Assert.Throws<InvalidDataException> (() => HotWaterRestoration.Capture (Edit (Domain (), "HotWaterRelayState", JsonValue.Create ("On"))));
		}
	}