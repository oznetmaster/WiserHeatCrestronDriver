// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class ScheduleControlCycleTests
	{
	private sealed class Session : IScheduleControlSession
		{
		public const string Auto = """{"id":8,"Name":"Test room","Mode":"Auto","ScheduleId":8,"ManualSetPoint":200,"CurrentSetPoint":220,"ScheduledSetPoint":220,"SetpointOrigin":"FromSchedule"}""";
		public ScheduleControlSnapshot State = new ("192.0.2.20/room/8", new (Guid.NewGuid ().ToString ("N"), 0, 0), JsonSerializer.Deserialize<JsonElement> (Auto), true);
		public List<(bool Enabled, bool Recovery)> Inputs { get; } = [];
		public List<string> Phases { get; } = [];
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
			room["Mode"] = enabled ? "Auto" : "Manual";
			room["SetpointOrigin"] = enabled ? "FromSchedule" : "FromManualMode";
			room["CurrentSetPoint"] = room[enabled ? "ScheduledSetPoint" : "ManualSetPoint"]!.GetValue<int> ();
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
		}

	private static Task<ScheduleControlResult> Run (Session session, CancellationToken token = default) =>
		 ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), token);

	[Test]
	public async Task NormalCycle_UsesBothUiActionsAndConfirmsIndependentRestoration ()
		{
		var session = new Session ();
		var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True);
		Assert.That (session.Inputs, Is.EqualTo (new[] { (false, false), (true, false) }));
		Assert.That (session.Phases, Is.EqualTo (new[] { "original", "disable-intent", "manual-observed", "restore-intent", "restored" }));
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