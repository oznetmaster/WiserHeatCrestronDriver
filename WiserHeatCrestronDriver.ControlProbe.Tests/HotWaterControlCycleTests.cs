// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class HotWaterControlCycleTests
	{
	[TestCase ("before", false), TestCase ("after", false), TestCase ("after", true), TestCase ("restored", false), TestCase ("restored", true)]
	public async Task PeerFailureDoesNotPreventOriginalHotWaterPolicyRestoration (string phase, bool cancel)
		{
		var session = new Session (on: true, stored: false);
		var original = await session.ReadForRecoveryAsync (CancellationToken.None);
		var plan = HotWaterRestoration.Capture (original.Hub.Domain);
		var observer = new FailingGatewayObserver (phase, cancel);
		var result = await HotWaterControlCycle.RunAsync (session, TimeSpan.FromMilliseconds (160), CancellationToken.None, observer);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		var final = await session.ReadForRecoveryAsync (CancellationToken.None);
		HotWaterControlCycle.RequireGuarded (original, final);
		HotWaterRestoration.RequireRestored (plan, final.Hub.Domain);
		Assert.That (final.HomeOn, Is.True);
		if (phase == "before") Assert.That (session.Inputs, Is.Empty);
		else
			{
			Assert.That (observer.Calls, Does.Contain ("restored"));
			Assert.That (session.UiRestorationChecks, Is.EqualTo (1));
			Assert.That (session.Inputs.Count, Is.EqualTo (phase == "after" ? 1 : 2));
			}
		}
	[Test]
	public async Task PrimaryHotWaterStateIsRecheckedAfterWaitingForPeerBeforeSendingInput ()
		{
		var session = new Session ();
		var observer = new FailingGatewayObserver () { BeforeAction = () => session.Domain["HotWater"]![0]!["WaterHeatingState"] = "On" };
		var result = await HotWaterControlCycle.RunAsync (session, TimeSpan.FromMilliseconds (160), CancellationToken.None, observer);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		Assert.That (session.Restores, Is.Empty, "Do not undo an external preflight change.");
		Assert.That ((await session.ReadForRecoveryAsync (CancellationToken.None)).HomeOn, Is.True);
		}
	[Test]
	public async Task PeerFailureBeforeSecondInputStillRestoresTheFirstInput ()
		{
		var session = new Session ();
		var original = await session.ReadForRecoveryAsync (CancellationToken.None);
		var observer = new FailingGatewayObserver ("before", failOccurrence: 2);
		var result = await HotWaterControlCycle.RunAsync (session, TimeSpan.FromMilliseconds (160), CancellationToken.None, observer);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs, Is.EqualTo (new[] { true }));
		HotWaterRestoration.RequireRestored (HotWaterRestoration.Capture (original.Hub.Domain), (await session.ReadForRecoveryAsync (CancellationToken.None)).Hub.Domain);
		}
	[Test]
	public async Task PersistentlyUnavailablePeerCannotBlockHotWaterCompensationOrFinalUiCheck ()
		{
		var session = new Session ();
		var observer = new FailingGatewayObserver ("after", persistent: true);
		var result = await HotWaterControlCycle.RunAsync (session, TimeSpan.FromMilliseconds (160), CancellationToken.None, observer);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs, Is.EqualTo (new[] { true }));
		Assert.That (session.UiRestorationChecks, Is.EqualTo (1));
		Assert.That (session.Records, Does.Contain ("restored-peer-failed"));
		}
	[Test]
	public async Task PassingPeerObservesBothHotWaterInputsAndRestoredPolicy ()
		{
		var observer = new FailingGatewayObserver ();
		var result = await HotWaterControlCycle.RunAsync (new Session (), TimeSpan.FromMilliseconds (160), CancellationToken.None, observer);
		Assert.That (result.Passed, Is.True, result.Detail);
		Assert.That (observer.Calls, Is.EqualTo (new[] { "before", "after", "before", "after", "restored" }));
		}
	private sealed class Session (bool on = false, bool stored = false, string policy = "Schedule") : IHotWaterControlSession
		{
		public readonly JsonNode Domain = JsonSerializer.SerializeToNode (new
			{
			System = new { OverrideType = "None", AwayModeAffectsHotWater = true },
			HotWater = new[] { new { id = 2, DeviceId = 0, ScheduleId = 1000, Mode = policy == "ManualMode" ? "Manual" : "Auto",
				HotWaterDescription = policy == "Schedule" ? "FromSchedule" : policy == "ManualMode" ? "FromManualMode" : "FromManualOverride",
				OverrideWaterHeatingState = stored ? "On" : "Off", WaterHeatingState = on ? "On" : "Off",
				HotWaterRelayState = on ? "On" : "Off", ScheduledWaterHeatingState = on ? "On" : "Off" } },
			Room = new[] { new { id = 9, Mode = "Auto", Name = "Room", ScheduleId = 10, ManualSetPoint = 170 } }
			})!;
		private readonly JsonElement _schedules = JsonSerializer.SerializeToElement (new { Heating = Array.Empty<object> (), OnOff = Array.Empty<object> () });
		private string _lifetime = Guid.NewGuid ().ToString ();
		private DateTimeOffset _refresh = DateTimeOffset.UtcNow;
		public List<bool> Inputs = [];
		public List<int> Restores = [];
		public List<string> Records = [];
		public string? Behavior;
		public bool SparseHubResponses;
		public string? FailRecord;
		public bool ChangeBeforeRestoration;
		public bool UiUnavailable;
		public bool RestoredUiUnavailable;
		public int UiRestorationChecks;
		public CancellationTokenSource? CancelAfterFirst;
		private JsonNode Water => Domain["HotWater"]![0]!;
		public Task<HotWaterControlSnapshot> ReadAsync (CancellationToken token)
			{
			if (UiUnavailable && Inputs.Count > 0) throw new IOException ("Android observation unavailable after input");
			return ReadSnapshotAsync (token, includeUi: true);
			}
		public Task<HotWaterControlSnapshot> ReadForRecoveryAsync (CancellationToken token) => ReadSnapshotAsync (token, includeUi: false);
		public Task VerifyRestoredUiAsync (HotWaterControlSnapshot snapshot, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			UiRestorationChecks++;
			if (UiUnavailable || RestoredUiUnavailable || Behavior == "ui-stale") throw new IOException ("Restored UI unavailable or stale");
			return RecordAsync ("restored-ui-observed", new { Snapshot = snapshot });
			}
		private Task<HotWaterControlSnapshot> ReadSnapshotAsync (CancellationToken token, bool includeUi)
			{
			token.ThrowIfCancellationRequested ();
			bool active = Water["WaterHeatingState"]!.GetValue<string> () == "On";
			return Task.FromResult (new HotWaterControlSnapshot ("fake-hub", _lifetime, _refresh,
				new (_schedules, JsonSerializer.SerializeToElement (Domain)),
				(Behavior == "processor-stale" || includeUi && Behavior == "ui-stale") && Inputs.Count > 0 ? !active : active, true));
			}
		public Task RecordAsync (string phase, object value)
			{
			if (FailRecord == phase) throw new IOException ("journal failed");
			if (phase == "restore-0-intent" && ChangeBeforeRestoration) Manual (true);
			Records.Add (phase); return Task.CompletedTask;
			}
		private void Manual (bool enabled)
			{
			string state = enabled ? "On" : "Off";
			Water["OverrideType"] = "Manual"; Water["OverrideWaterHeatingState"] = state;
			if (SparseHubResponses) Domain["System"]!["UserOverridesActive"] = true;
			Water["WaterHeatingState"] = state; Water["HotWaterRelayState"] = state;
			Water["HotWaterDescription"] = "FromManualOverride";
			if (Behavior == "own-deadline") Water["OverrideTimeoutUnixTime"] = DateTimeOffset.UtcNow.AddHours (1).ToUnixTimeSeconds ();
			_refresh = _refresh.AddSeconds (1);
			}
		public Task SetHotWaterAsync (bool enabled, CancellationToken token)
			{
			token.ThrowIfCancellationRequested (); Inputs.Add (enabled);
			if (Behavior == "not-delivered") throw new IOException ("input uncertain");
			Manual (enabled);
			if (Behavior == "foreign-room") Domain["Room"]![0]!["ManualSetPoint"] = 200;
			if (Behavior == "foreign-water") Water["AwayModeSuppressed"] = true;
			if (Behavior == "away-change") Domain["System"]!["OverrideType"] = "Away";
			if (Behavior == "restart") _lifetime = Guid.NewGuid ().ToString ();
			if (Inputs.Count == 1 && CancelAfterFirst != null) { CancelAfterFirst.Cancel (); token.ThrowIfCancellationRequested (); }
			if (Behavior == "lost-first" && Inputs.Count == 1 || Behavior == "lost-second" && Inputs.Count == 2) throw new IOException ("reply lost");
			return Task.CompletedTask;
			}
		public async Task RestoreAsync (int index, int controllerId, JsonElement request, HotWaterControlSnapshot expected, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			HotWaterControlCycle.RequireRestorationUnchanged (expected, await ReadForRecoveryAsync (token));
			Assert.That (controllerId, Is.EqualTo (2));
			Assert.That (Restores, Does.Not.Contain (index), "Uncertain compensation must never be replayed.");
			Assert.That (Records, Does.Contain ("restore-" + index + "-intent"));
			Restores.Add (index);
			var value = request.GetProperty ("RequestOverride");
			if (value.GetProperty ("Type").GetString () == "Manual") Manual (value.GetProperty ("SetPoint").GetInt32 () == 110);
			else if (Behavior != "cancel-ignored")
				{
				Water["OverrideType"] = "None"; Water.AsObject ().Remove ("OverrideTimeoutUnixTime");
				bool auto = Water["Mode"]!.GetValue<string> () == "Auto";
				Water["HotWaterDescription"] = auto ? "FromSchedule" : "FromManualMode";
				string state = Water[auto ? "ScheduledWaterHeatingState" : "OverrideWaterHeatingState"]!.GetValue<string> ();
				Water["WaterHeatingState"] = state; Water["HotWaterRelayState"] = state;
				if (SparseHubResponses)
					{
					Domain["System"]!.AsObject ().Remove ("UserOverridesActive");
					Water.AsObject ().Remove ("OverrideWaterHeatingState");
					Water["AwayModeSuppressed"] = false;
					}
				_refresh = _refresh.AddSeconds (1);
				}
			if (Behavior == "lost-compensation") throw new IOException ("compensation reply lost");
			}
		}
	private static Task<HotWaterControlResult> Run (Session session, CancellationToken token = default) =>
		HotWaterControlCycle.RunAsync (session, TimeSpan.FromMilliseconds (300), token);
	[Test]
	public async Task ExternalHotWaterChangeAfterFinalObservationIsNotOverwrittenByRestoration ()
		{
		var session = new Session () { ChangeBeforeRestoration = true };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs, Is.EqualTo (new[] { true, false }));
		Assert.That (session.Restores, Is.Empty, "A household change between observation and compensation must not be overwritten.");
		Assert.That ((await session.ReadForRecoveryAsync (CancellationToken.None)).HomeOn, Is.True);
		Assert.That (session.Records, Does.Contain ("recovery-required"));
		}

	[TestCase (false, false, "Schedule", 1), TestCase (false, true, "Schedule", 1)]
	[TestCase (true, false, "Schedule", 1), TestCase (true, true, "Schedule", 1)]
	[TestCase (false, false, "ManualMode", 1), TestCase (true, true, "ManualMode", 1)]
	[TestCase (false, false, "ManualOverride", 0), TestCase (true, true, "ManualOverride", 0)]
	public async Task BothUiStatesAndOriginalPolicyAreRestored (bool on, bool stored, string policy, int writes)
		{
		var session = new Session (on, stored, policy);
		var original = await session.ReadAsync (default); var plan = HotWaterRestoration.Capture (original.Hub.Domain);
		var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs, Is.EqualTo (new[] { !on, on }));
		Assert.That (session.Restores.Count, Is.EqualTo (writes));
		var final = await session.ReadAsync (default);
		HotWaterRestoration.RequireRestored (plan, final.Hub.Domain);
		HotWaterControlCycle.RequireGuarded (original, final);
		}
	[TestCase ("lost-first", 1), TestCase ("lost-second", 2), TestCase ("lost-compensation", 2)]
	public async Task LostRepliesRemainFailedAfterIndependentRestoration (string behavior, int inputs)
		{
		var session = new Session { Behavior = behavior }; var result = await Run (session);
		Assert.That (result.Passed, Is.False); Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (inputs));
		}
	[TestCase ("foreign-water"), TestCase ("not-delivered"), TestCase ("foreign-room"), TestCase ("away-change"), TestCase ("restart"), TestCase ("processor-stale")]
	public async Task UncertainOrForeignStateIsNotBlindlyOverwritten (string behavior)
		{
		var session = new Session { Behavior = behavior }; var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs.Count, Is.EqualTo (1)); Assert.That (session.Restores, Is.Empty);
		}
	[TestCase (false, "Schedule"), TestCase (true, "Schedule")]
	[TestCase (false, "ManualMode"), TestCase (true, "ManualOverride")]
	public async Task AndroidFailureDoesNotPreventGuardedPhysicalRestoration (bool on, string policy)
		{
		var session = new Session (on, on, policy) { UiUnavailable = true };
		var original = await session.ReadForRecoveryAsync (default);
		var plan = HotWaterRestoration.Capture (original.Hub.Domain);
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs, Is.EqualTo (new[] { !on }));
		Assert.That (session.UiRestorationChecks, Is.EqualTo (1));
		var final = await session.ReadForRecoveryAsync (default);
		HotWaterRestoration.RequireRestored (plan, final.Hub.Domain);
		HotWaterControlCycle.RequireGuarded (original, final);
		Assert.That (session.Records, Does.Contain ("restored-ui-failed"));
		}
	[TestCase ("ui-stale", false, 1), TestCase (null, true, 2)]
	public async Task UiFailureRemainsFailedAfterPhysicalRecovery (string? behavior, bool finalUiUnavailable, int inputs)
		{
		var session = new Session { Behavior = behavior, RestoredUiUnavailable = finalUiUnavailable };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (inputs));
		Assert.That (session.Records, Does.Contain ("restored"));
		Assert.That (session.Records, Does.Contain ("restored-ui-failed"));
		}
	[TestCase ("foreign-room"), TestCase ("foreign-water"), TestCase ("away-change"), TestCase ("restart"), TestCase ("not-delivered"), TestCase ("processor-stale")]
	public async Task AndroidFailureDoesNotBypassPhysicalIdentityOrStateGuards (string behavior)
		{
		var session = new Session { Behavior = behavior, UiUnavailable = true };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs.Count, Is.EqualTo (1));
		Assert.That (session.Restores, Is.Empty);
		Assert.That (session.UiRestorationChecks, Is.Zero);
		}
	[Test]
	public async Task IgnoredCancelDoesNotPassBecauseButtonReturnedToOff ()
		{
		var session = new Session { Behavior = "cancel-ignored" }; var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Restores, Is.EqualTo (new[] { 0 }));
		}
	[Test]
	public async Task DeadlineCreatedByAnOwnedOverrideMustBeCleared ()
		{
		var session = new Session { Behavior = "own-deadline" }; var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Domain["HotWater"]![0]!["OverrideTimeoutUnixTime"], Is.Null);
		}
	[Test]
	public async Task CancellationAfterDeliveryUsesIndependentRestoration ()
		{
		using var source = new CancellationTokenSource ();
		var session = new Session { CancelAfterFirst = source }; var result = await Run (session, source.Token);
		Assert.That (result.Passed, Is.False); Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (1));
		}
	[TestCase ("original", 0, true), TestCase ("ui-1-intent", 0, true), TestCase ("restore-0-intent", 2, false), TestCase ("restored", 2, false)]
	public async Task IntentAndRestorationEvidenceAreRequired (string phase, int inputs, bool restored)
		{
		var session = new Session { FailRecord = phase }; var result = await Run (session);
		Assert.That (result.Passed, Is.False); Assert.That (result.RestorationConfirmed, Is.EqualTo (restored));
		Assert.That (session.Inputs.Count, Is.EqualTo (inputs));
		}
	[TestCase (false), TestCase (true)]
	public async Task HubAggregateFlagAndRemovedInactiveTargetDoNotPreventScheduleRestoration (bool sparseBefore)
		{
		var session = new Session (true) { SparseHubResponses = true };
		if (sparseBefore) session.Domain["HotWater"]![0]!.AsObject ().Remove ("OverrideWaterHeatingState");
		var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs, Is.EqualTo (new[] { false, true }));
		Assert.That (session.Restores, Is.EqualTo (new[] { 0 }));
		Assert.That (session.Domain["HotWater"]![0]!["OverrideType"]!.GetValue<string> (), Is.EqualTo ("None"));
		Assert.That (session.Domain["HotWater"]![0]!["OverrideWaterHeatingState"], Is.Null);
		}
	[Test]
	public async Task ExistingTimerIsLeftUntouched ()
		{
		var session = new Session ();
		session.Domain["HotWater"]![0]!["OverrideTimeoutUnixTime"] = DateTimeOffset.UtcNow.AddHours (1).ToUnixTimeSeconds ();
		var result = await Run (session);
		Assert.That (result.Passed, Is.False); Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs, Is.Empty); Assert.That (session.Restores, Is.Empty);
		}
	}