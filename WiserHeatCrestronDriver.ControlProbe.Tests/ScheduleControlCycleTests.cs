// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed partial class ScheduleControlCycleTests
	{
	private sealed class Session : IScheduleControlSession
		{
		public const string Auto = """{"id":8,"Name":"Test room","Mode":"Auto","ScheduleId":8,"ManualSetPoint":200,"CurrentSetPoint":220,"ScheduledSetPoint":220,"SetpointOrigin":"FromSchedule","OccupiedHeatingSetPoint":220,"UnoccupiedHeatingSetPoint":200}""";
		public ScheduleControlSnapshot State = new ("192.0.2.20/room/8", new (Guid.NewGuid ().ToString ("N"), 0, 0), JsonSerializer.Deserialize<JsonElement> (Auto), true);
		public List<(bool Enabled, bool Recovery)> Inputs { get; } = [];
		public List<string> Phases { get; } = [];
		public List<int> ManualWrites { get; } = [];
		public Action<Session>? BeforeRead { get; init; }
		public int? InitializedTarget { get; init; }
		public bool RemoveManualOnRestore { get; init; }
		public bool ChangeScheduleOnRestore { get; init; }
		public bool PreserveManual
			{
			get; init;
			}
		public bool LoseManualReply
			{
			get; init;
			}
		public bool IgnoreManualWrite
			{
			get; init;
			}
		public string? FailRecord
			{
			get; init;
			}
		public bool LoseDisableReply
			{
			get; init;
			}
		public bool LoseRestoreReply
			{
			get; init;
			}
		public bool InputNeverDelivered
			{
			get; init;
			}
		public bool ChangeEpoch
			{
			get; init;
			}
		public bool ExtraCommand
			{
			get; init;
			}
		public bool LosePhysicalIdentity
			{
			get; init;
			}
		public CancellationTokenSource? CancelAfterDisable
			{
			get; init;
			}
		public Task<ScheduleControlSnapshot> ReadAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			BeforeRead?.Invoke (this);
			return Task.FromResult (State);
			}
		public Task RecordAsync (string phase, ScheduleControlSnapshot snapshot)
			{
			if (phase == FailRecord)
				throw new IOException ("Injected journal failure");
			Phases.Add (phase);
			return Task.CompletedTask;
			}
		public Task SetAsync (bool enabled, bool recovery, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Inputs.Add ((enabled, recovery));
			if (InputNeverDelivered)
				return Task.CompletedTask;
			var room = JsonNode.Parse (State.Room.GetRawText ())!;
			if (!enabled && !PreserveManual)
				room["ManualSetPoint"] = InitializedTarget ?? room["CurrentSetPoint"]!.GetValue<int> ();
			room["Mode"] = enabled ? "Auto" : "Manual";
			room["SetpointOrigin"] = enabled ? "FromSchedule" : "FromManualMode";
			int delta = room[enabled ? "ScheduledSetPoint" : "ManualSetPoint"]!.GetValue<int> () - room["CurrentSetPoint"]!.GetValue<int> ();
			room["OccupiedHeatingSetPoint"] = room["OccupiedHeatingSetPoint"]!.GetValue<int> () + delta;
			room["UnoccupiedHeatingSetPoint"] = room["UnoccupiedHeatingSetPoint"]!.GetValue<int> () + delta;
			room["CurrentSetPoint"] = room[enabled ? "ScheduledSetPoint" : "ManualSetPoint"]!.GetValue<int> ();
			if (enabled && RemoveManualOnRestore)
				room.AsObject ().Remove ("ManualSetPoint");
			if (enabled && ChangeScheduleOnRestore)
				room["ScheduleId"] = 9;
			State = State with
				{
				Activity = State.Activity with
					{
					Completed = State.Activity.Completed + (ExtraCommand ? 2 : 1),
					Epoch = ChangeEpoch ? Guid.NewGuid ().ToString ("N") : State.Activity.Epoch
					},
				PhysicalIdentity = LosePhysicalIdentity ? "192.0.2.20/room/9" : State.PhysicalIdentity,
				Room = JsonSerializer.Deserialize<JsonElement> (room.ToJsonString ()),
				HomeEnabled = enabled
				};
			if (!enabled)
				CancelAfterDisable?.Cancel ();
			if (enabled ? LoseRestoreReply : LoseDisableReply)
				throw new IOException ("Injected lost response after delivery");
			return Task.CompletedTask;
			}
		public Task SetManualTargetAsync (int target, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			ManualWrites.Add (target);
			if (!IgnoreManualWrite)
				{
				var room = JsonNode.Parse (State.Room.GetRawText ())!;
				int delta = target - room["CurrentSetPoint"]!.GetValue<int> ();
				room["OccupiedHeatingSetPoint"] = room["OccupiedHeatingSetPoint"]!.GetValue<int> () + delta;
				room["UnoccupiedHeatingSetPoint"] = room["UnoccupiedHeatingSetPoint"]!.GetValue<int> () + delta;
				room["ManualSetPoint"] = target;
				room["CurrentSetPoint"] = target;
				State = State with { Room = JsonSerializer.SerializeToElement (room) };
				}
			if (LoseManualReply)
				throw new IOException ("Injected lost target response");
			return Task.CompletedTask;
			}
		}

	private static Task<ScheduleControlResult> Run (Session session, CancellationToken token = default) =>
		 ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), token);

	private static void SetSavedTarget (Session session, int? target)
		{
		var room = JsonNode.Parse (session.State.Room.GetRawText ())!.AsObject ();
		if (target.HasValue) room["ManualSetPoint"] = target.Value;
		else room.Remove ("ManualSetPoint");
		session.State = session.State with { Room = JsonSerializer.SerializeToElement (room) };
		}

	[TestCase (ScheduleManualTargetRequirement.EqualToCurrent, 220)]
	[TestCase (ScheduleManualTargetRequirement.DifferentFromCurrent, 200)]
	[TestCase (ScheduleManualTargetRequirement.Absent, null)]
	public async Task SelectedStartingStateIsObservedAndItsRestorationScopeIsReported (ScheduleManualTargetRequirement required, int? manual)
		{
		var session = new Session ();
		SetSavedTarget (session, manual);
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), default, allowManualTargetInitialization: true, requiredManualTarget: required);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (result.RequiredManualTargetState, Is.EqualTo (required.ToString ()));
		Assert.That (result.ExactRestorationConfirmed, Is.EqualTo (manual.HasValue));
		Assert.That (result.ManualTargetInitialized, Is.EqualTo (!manual.HasValue));
		Assert.That (session.Phases, Does.Contain ("starting-state-verified-" + required));
		Assert.That (session.Inputs, Has.Count.EqualTo (2));
		Assert.That (ScheduleObservation.ManualTarget (session.State.Room), Is.EqualTo (manual ?? 220));
		}

	[TestCase (ScheduleManualTargetRequirement.EqualToCurrent, 200)]
	[TestCase (ScheduleManualTargetRequirement.EqualToCurrent, null)]
	[TestCase (ScheduleManualTargetRequirement.DifferentFromCurrent, 220)]
	[TestCase (ScheduleManualTargetRequirement.DifferentFromCurrent, null)]
	[TestCase (ScheduleManualTargetRequirement.Absent, 200)]
	[TestCase (ScheduleManualTargetRequirement.Absent, 220)]
	public async Task WrongStartingStateNeverSendsAModeOrTemperatureCommand (ScheduleManualTargetRequirement required, int? manual)
		{
		var session = new Session ();
		SetSavedTarget (session, manual);
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), default, true, required);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (result.Detail, Does.Contain ("does not match"));
		Assert.That (session.Inputs, Is.Empty);
		Assert.That (session.ManualWrites, Is.Empty);
		}

	[Test]
	public async Task SelectedAbsentStateStillRequiresExplicitInitializationPermission ()
		{
		var session = new Session ();
		SetSavedTarget (session, null);
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), default, false, ScheduleManualTargetRequirement.Absent);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		Assert.That (session.ManualWrites, Is.Empty);
		}

	[Test]
	public async Task ScheduleBoundaryCannotTurnADifferentCaseIntoAnEqualCaseBeforeInput ()
		{
		int reads = 0;
		var session = new Session
			{
			BeforeRead = value =>
				{
				if (++reads != 2) return;
				var room = JsonNode.Parse (value.State.Room.GetRawText ())!;
				room["CurrentSetPoint"] = 200;
				room["ScheduledSetPoint"] = 200;
				value.State = value.State with { Room = JsonSerializer.SerializeToElement (room) };
				}
			};
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), default, false, ScheduleManualTargetRequirement.DifferentFromCurrent);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		Assert.That (session.ManualWrites, Is.Empty);
		}

	[Test]
	public void UnknownStartingStateIsRejected () => Assert.ThrowsAsync<ArgumentOutOfRangeException> (() =>
		ScheduleControlCycle.RunAsync (new Session (), TimeSpan.FromSeconds (5), default, false, (ScheduleManualTargetRequirement)99));

	[Test]
	public async Task NormalCycle_UsesBothUiActionsAndConfirmsIndependentRestoration ()
		{
		var session = new Session ();
		var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs, Is.EqualTo (new[] { (false, false), (true, false) }));
		Assert.That (session.Phases, Is.EqualTo (new[] { "original", "disable-intent", "manual-observed", "manual-target-restore-intent", "manual-target-restored", "restore-intent", "restored" }));
		Assert.That (session.ManualWrites, Is.EqualTo (new[] { 200 }));
		}

	[Test]
	public async Task LostDisableResponse_ObservesCompletionThenCompensatesOnceWithoutReplaying ()
		{
		var session = new Session { LoseDisableReply = true };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs, Is.EqualTo (new[] { (false, false), (true, true) }));
		}

	[Test]
	public async Task CancellationAfterDelivery_UsesIndependentRestorationDeadline ()
		{
		using var cancellation = new CancellationTokenSource ();
		var session = new Session { CancelAfterDisable = cancellation };
		var result = await Run (session, cancellation.Token);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs, Is.EqualTo (new[] { (false, false), (true, true) }));
		}

	[Test]
	public async Task LostRestoreResponse_IsUnconfirmedAndNeverRetried ()
		{
		var session = new Session { LoseRestoreReply = true };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs, Has.Count.EqualTo (2));
		Assert.That (session.Phases.Last (), Is.EqualTo ("recovery-required"));
		}

	[TestCase ("original")]
	[TestCase ("disable-intent")]
	public async Task JournalFailureBeforeControl_SendsNothing (string phase)
		{
		var session = new Session { FailRecord = phase };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs, Is.Empty);
		}

	[Test]
	public async Task ObservationRecordFailureAfterControl_StillRestores ()
		{
		var session = new Session { FailRecord = "manual-observed" };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs.Last (), Is.EqualTo ((true, true)));
		}

	[Test]
	public async Task RecoveryJournalFailure_DoesNotSendAnUnrecordedRestore ()
		{
		var session = new Session { FailRecord = "restore-intent" };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs, Has.Count.EqualTo (1));
		}

	[TestCase ("restart")]
	[TestCase ("concurrent")]
	[TestCase ("identity")]
	public async Task ChangedIdentityOrActivity_RefusesFurtherCommands (string condition)
		{
		var session = new Session { ChangeEpoch = condition == "restart", ExtraCommand = condition == "concurrent", LosePhysicalIdentity = condition == "identity" };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs, Has.Count.EqualTo (1));
		}

	[Test]
	public async Task UnsafeInitialManualSetpoint_RefusesBeforeAnyUiInput ()
		{
		var session = new Session ();
		session.State = session.State with
			{
			Room = JsonSerializer.Deserialize<JsonElement> (Session.Auto.Replace ("\"ManualSetPoint\":200", "\"ManualSetPoint\":350"))
			};
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs, Is.Empty);
		}

	[Test]
	public async Task UnchangedStateAfterInput_IsNotAssumedSafeAndInputIsNeverReplayed ()
		{
		var session = new Session { InputNeverDelivered = true };
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (1), CancellationToken.None);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs, Is.EqualTo (new[] { (false, false) }));
		}
	[TestCase (170)]
	[TestCase (220)]
	[TestCase (225)]
	public async Task ExistingManualTargetBelowEqualOrAboveSchedule_IsRetainedAfterUiCycle (int manual)
		{
		var session = new Session ();
		var room = JsonNode.Parse (Session.Auto)!;
		room["ManualSetPoint"] = manual;
		session.State = session.State with
			{
			Room = JsonSerializer.Deserialize<JsonElement> (room.ToJsonString ())
			};
		var original = session.State.Room;
		var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True);
		Assert.That (JsonElement.DeepEquals (original, session.State.Room), Is.True);
		Assert.That (session.Inputs, Is.EqualTo (new[] { (false, false), (true, false) }));
		Assert.That (session.ManualWrites, Is.EqualTo (manual == 220 ? Array.Empty<int> () : new[] { manual }));
		}

	[Test]
	public async Task HubThatPreservesManualTarget_NeedsNoTemperatureWrite ()
		{
		var session = new Session { PreserveManual = true };
		var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True);
		Assert.That (session.ManualWrites, Is.Empty);
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task MissingManualTarget_ReportsUnsupportedRestorationWithoutInput (bool nullValue)
		{
		var session = new Session ();
		var room = JsonNode.Parse (Session.Auto)!.AsObject ();
		if (nullValue)
			room["ManualSetPoint"] = null;
		else
			room.Remove ("ManualSetPoint");
		session.State = session.State with { Room = JsonSerializer.SerializeToElement (room) };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (result.Detail, Does.StartWith ("No saved manual target:"));
		Assert.That (session.Inputs, Is.Empty);
		Assert.That (session.ManualWrites, Is.Empty);
		}

	[Test]
	public async Task LostManualWriteReply_ObservesTargetAndRestoresAutoWithoutRetry ()
		{
		var session = new Session { LoseManualReply = true };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.ManualWrites, Is.EqualTo (new[] { 200 }));
		Assert.That (session.Inputs.Last (), Is.EqualTo ((true, true)));
		}

	private static void RemoveSavedTarget (Session session, bool nullValue = false)
		{
		var room = JsonNode.Parse (Session.Auto)!.AsObject ();
		if (nullValue)
			room["ManualSetPoint"] = null;
		else
			room.Remove ("ManualSetPoint");
		session.State = session.State with { Room = JsonSerializer.SerializeToElement (room) };
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task ExplicitInitialization_RestoresAutoAndReportsRetainedInactiveTarget (bool nullValue)
		{
		var session = new Session ();
		RemoveSavedTarget (session, nullValue);
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), CancellationToken.None, true);
		Assert.That (result.Passed && result.RestorationConfirmed && result.ManualTargetInitialized, Is.True);
		Assert.That (result.ExactRestorationConfirmed, Is.False);
		Assert.That (result.Detail, Does.Contain ("inactive initialized manual target"));
		Assert.That (session.State.Room.GetProperty ("Mode").GetString (), Is.EqualTo ("Auto"));
		Assert.That (session.State.Room.GetProperty ("ManualSetPoint").GetInt32 (), Is.EqualTo (220));
		Assert.That (session.State.Room.GetProperty ("ScheduleId").GetInt32 (), Is.EqualTo (8));
		Assert.That (session.Inputs, Is.EqualTo (new[] { (false, false), (true, false) }));
		Assert.That (session.ManualWrites, Is.Empty);
		Assert.That (session.Phases, Is.EqualTo (new[] { "original", "manual-initialization-accepted", "disable-intent", "manual-observed", "restore-intent", "restored" }));
		}

	[Test]
	public async Task HubRemovingInitializedTargetInAuto_ConfirmsExactRestoration ()
		{
		var session = new Session { RemoveManualOnRestore = true };
		RemoveSavedTarget (session);
		var original = session.State.Room;
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), CancellationToken.None, true);
		Assert.That (result.Passed && result.ExactRestorationConfirmed, Is.True);
		Assert.That (result.ManualTargetInitialized, Is.False);
		Assert.That (JsonElement.DeepEquals (original, session.State.Room), Is.True);
		Assert.That (session.ManualWrites, Is.Empty);
		}

	[Test]
	public async Task InitializationOption_DoesNotRelaxExistingTargetRestoration ()
		{
		var session = new Session ();
		var original = session.State.Room;
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), CancellationToken.None, true);
		Assert.That (result.Passed && result.ExactRestorationConfirmed, Is.True);
		Assert.That (result.ManualTargetInitialized, Is.False);
		Assert.That (JsonElement.DeepEquals (original, session.State.Room), Is.True);
		Assert.That (session.ManualWrites, Is.EqualTo (new[] { 200 }));
		}

	[Test]
	public async Task InitializationPolicyJournalFailure_RefusesBeforeInput ()
		{
		var session = new Session { FailRecord = "manual-initialization-accepted" };
		RemoveSavedTarget (session);
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), CancellationToken.None, true);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ExactRestorationConfirmed, Is.True);
		Assert.That (session.Inputs, Is.Empty);
		}

	[TestCase (250)]
	[TestCase (350)]
	public async Task UnexpectedInitializedTarget_RefusesFurtherCommands (int target)
		{
		var session = new Session { InitializedTarget = target };
		RemoveSavedTarget (session);
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (1), CancellationToken.None, true);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs, Has.Count.EqualTo (1));
		Assert.That (session.ManualWrites, Is.Empty);
		}

	[Test]
	public async Task InitializationOption_DoesNotAcceptChangedSchedule ()
		{
		var session = new Session { ChangeScheduleOnRestore = true };
		RemoveSavedTarget (session);
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (1), CancellationToken.None, true);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs, Has.Count.EqualTo (2));
		}

	[Test]
	public async Task MissingTargetWithLostInputReply_RestoresAutoButRemainsFailed ()
		{
		var session = new Session { LoseDisableReply = true };
		RemoveSavedTarget (session);
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), CancellationToken.None, true);
		Assert.That (result.Passed || result.ExactRestorationConfirmed, Is.False);
		Assert.That (result.RestorationConfirmed && result.ManualTargetInitialized, Is.True);
		Assert.That (session.Inputs, Is.EqualTo (new[] { (false, false), (true, true) }));
		Assert.That (session.ManualWrites, Is.Empty);
		}

	[Test]
	public async Task MissingTargetWithCancellation_RestoresUsingIndependentDeadline ()
		{
		using var cancellation = new CancellationTokenSource ();
		var session = new Session { CancelAfterDisable = cancellation };
		RemoveSavedTarget (session);
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), cancellation.Token, true);
		Assert.That (result.Passed || result.ExactRestorationConfirmed, Is.False);
		Assert.That (result.RestorationConfirmed && result.ManualTargetInitialized, Is.True);
		Assert.That (session.Inputs, Is.EqualTo (new[] { (false, false), (true, true) }));
		}

	[Test]
	public async Task UnobservedManualWrite_DoesNotReplayOrClaimRestoration ()
		{
		var session = new Session { IgnoreManualWrite = true };
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (1), CancellationToken.None);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.ManualWrites, Has.Count.EqualTo (1));
		Assert.That (session.Phases.Last (), Is.EqualTo ("recovery-required"));
		}

	[TestCase ("manual-target-restore-intent", 0)]
	[TestCase ("manual-target-restored", 1)]
	public async Task ManualRestorationJournalFailure_IsNeverReportedAsRestored (string phase, int writes)
		{
		var session = new Session { FailRecord = phase };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.ManualWrites, Has.Count.EqualTo (writes));
		}

	[Test]
	public async Task ExhaustedCounter_RefusesBeforeInput ()
		{
		var session = new Session ();
		session.State = session.State with
			{
			Activity = session.State.Activity with
				{
				Completed = long.MaxValue
				}
			};
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs, Is.Empty);
		}

	[Test]
	public async Task MissingRestorationReceipt_DoesNotClaimConfirmedRestoration ()
		{
		var session = new Session { FailRecord = "restored" };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs, Has.Count.EqualTo (2));
		Assert.That (session.Phases.Last (), Is.EqualTo ("recovery-required"));
		}
	}