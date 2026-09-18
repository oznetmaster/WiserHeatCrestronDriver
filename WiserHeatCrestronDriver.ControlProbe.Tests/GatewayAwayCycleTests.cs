// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class GatewayAwayCycleTests
	{
	private static GatewayAwaySnapshot Snapshot (bool enabled = false) => new ("test-hub", Guid.NewGuid ().ToString (), DateTimeOffset.UtcNow,
		new (JsonSerializer.SerializeToElement (new { Heating = Array.Empty<object> (), OnOff = Array.Empty<object> () }),
			JsonSerializer.SerializeToElement (new
				{
				System = new { OverrideType = enabled ? "Away" : "None", AwayModeAffectsHotWater = true, AwayModeSetPointLimit = 125 },
				HotWater = new[] { new { id = 2, Mode = "Auto", ScheduleId = 1000, OverrideWaterHeatingState = "Off" } },
				Room = new[] { new { id = 9, Name = "Room", Mode = "Auto", ScheduleId = 10, ManualSetPoint = 170, OverrideType = "None" } }
				})), enabled, true);
	private static GatewayAwaySnapshot Change (GatewayAwaySnapshot snapshot, Action<JsonNode> edit)
		{
		var node = JsonNode.Parse (snapshot.Hub.Domain.GetRawText ())!;
		edit (node);
		return snapshot with { Hub = snapshot.Hub with { Domain = JsonSerializer.SerializeToElement (node) } };
		}
	private sealed class Session (bool initial = false) : IGatewayAwaySession
		{
		public GatewayAwaySnapshot State = Snapshot (initial);
		public List<bool> Inputs = [];
		public List<string> Records = [];
		public string? Behavior;
		public string? FailRecord;
		public Task<GatewayAwaySnapshot> ReadAsync (CancellationToken token) { token.ThrowIfCancellationRequested (); return Task.FromResult (State); }
		public Task RecordAsync (string phase, object value)
			{
			if (phase == FailRecord) throw new IOException ("journal unavailable");
			Records.Add (phase); return Task.CompletedTask;
			}
		public Task SetAwayAsync (bool enabled, bool recovery, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Inputs.Add (enabled);
			if (Behavior == "not-delivered") throw new IOException ("tap outcome unknown");
			State = Change (State, n => n["System"]!["OverrideType"] = enabled ? "Away" : "None") with
				{ HomeAway = enabled, RefreshUtc = State.RefreshUtc.AddSeconds (1) };
			if (Behavior == "foreign") State = Change (State, n => n["HotWater"]![0]!["Mode"] = "Manual");
			if (Behavior == "restart") State = State with { Lifetime = Guid.NewGuid ().ToString () };
			if (Behavior == "stale") State = State with { RefreshUtc = State.RefreshUtc.AddSeconds (-1) };
			if (Behavior == "ui-disagrees") State = State with { HomeAway = !enabled };
			if (Behavior == "disabled") State = State with { ActionEnabled = false };
			if (Behavior == "lost-change" && Inputs.Count == 1 || Behavior == "lost-restore" && Inputs.Count == 2)
				throw new IOException ("acknowledgement lost");
			return Task.CompletedTask;
			}
		}
	private static Task<GatewayAwayResult> Run (Session session) => GatewayAwayCycle.RunAsync (session, TimeSpan.FromMilliseconds (160), CancellationToken.None);
	[TestCase (false), TestCase (true)]
	public async Task EitherStartingStateIsRestored (bool initial)
		{
		var session = new Session (initial); var before = session.State;
		var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs, Is.EqualTo (new[] { !initial, initial }));
		GatewayAwayCycle.RequirePreserved (before, session.State);
		Assert.That (GatewayAwayCycle.IsAway (session.State), Is.EqualTo (initial));
		Assert.That (session.Records, Does.Contain ("changed").And.Contain ("restored"));
		}
	[TestCase ("lost-change"), TestCase ("lost-restore")]
	public async Task LostRepliesFailEvenWhenIndependentRestorationSucceeds (string behavior)
		{
		var session = new Session { Behavior = behavior };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False); Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs.Count, Is.EqualTo (2));
		}
	[TestCase ("not-delivered"), TestCase ("foreign"), TestCase ("restart"), TestCase ("stale"), TestCase ("ui-disagrees"), TestCase ("disabled")]
	public async Task UnsafeOrUnobservedTransitionIsNotReplayedOrBlindlyRestored (string behavior)
		{
		var session = new Session { Behavior = behavior };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs.Count, Is.EqualTo (1));
		Assert.That (session.Records, Does.Contain ("recovery-required"));
		}
	[TestCase ("original", 0, true), TestCase ("change-intent", 0, true), TestCase ("restore-intent", 1, false), TestCase ("restored", 2, false)]
	public async Task DurableEvidenceIsRequiredBeforeInputsAndRestorationClaims (string phase, int count, bool restored)
		{
		var session = new Session { FailRecord = phase }; var result = await Run (session);
		Assert.That (result.Passed, Is.False); Assert.That (result.RestorationConfirmed, Is.EqualTo (restored));
		Assert.That (session.Inputs.Count, Is.EqualTo (count));
		}
	[TestCase ("System", "AwayModeSetPointLimit", "200")]
	[TestCase ("System", "OverrideTimeoutUnixTime", "1234")]
	[TestCase ("HotWater", "OverrideWaterHeatingState", "\"On\"")]
	[TestCase ("HotWater", "OverrideTimeoutUnixTime", "1234")]
	[TestCase ("HotWater", "ScheduleId", "1001")]
	[TestCase ("HotWater", "AwayModeSuppressed", "true")]
	public void ChangedSettingsOrTimersAreNotRestoration (string group, string key, string value)
		{
		var before = Snapshot ();
		var after = Change (before, n => (group == "HotWater" ? n[group]![0]! : n[group]!)[key] = JsonNode.Parse (value));
		Assert.Throws<InvalidDataException> (() => GatewayAwayCycle.RequirePreserved (before, after));
		}
	[Test]
	public void RoomOverrideMustRemainUnchanged ()
		{
		var before = Snapshot ();
		var after = Change (before, n => n["Room"]![0]!["OverrideType"] = "Manual");
		Assert.Throws<InvalidDataException> (() => GatewayAwayCycle.RequirePreserved (before, after));
		}
	[Test]
	public void DerivedHeatingFeedbackCanChangeDuringAway ()
		{
		var before = Snapshot ();
		var after = Change (before, n => { n["HotWater"]![0]!["WaterHeatingState"] = "On"; n["Room"]![0]!["CurrentSetPoint"] = 125; });
		Assert.DoesNotThrow (() => GatewayAwayCycle.RequirePreserved (before, after));
		}
	[Test]
	public void DuplicateHotWaterIdentitiesAreRejected ()
		{
		var before = Snapshot ();
		var after = Change (before, n => n["HotWater"]!.AsArray ().Add (n["HotWater"]![0]!.DeepClone ()));
		Assert.Throws<InvalidDataException> (() => GatewayAwayCycle.RequirePreserved (before, after));
		}
	[Test]
	public void EmptyLifetimeCannotEstablishAttribution ()
		{
		var before = Snapshot () with { Lifetime = Guid.Empty.ToString () };
		Assert.Throws<InvalidDataException> (() => GatewayAwayCycle.RequirePreserved (before, before));
		}
	[Test]
	public async Task UnsupportedSystemOverrideIsRejectedBeforeInput ()
		{
		var session = new Session ();
		session.State = Change (session.State, n => n["System"]!["OverrideType"] = "Boost");
		var result = await Run (session);
		Assert.That (result.Passed, Is.False); Assert.That (session.Inputs, Is.Empty);
		}
	}