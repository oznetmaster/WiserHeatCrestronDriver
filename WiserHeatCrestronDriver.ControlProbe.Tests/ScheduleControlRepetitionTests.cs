// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

public sealed partial class ScheduleControlCycleTests
	{
	[Test]
	public async Task RepeatedCyclesPreserveOneOriginalPolicyAndRestoreEachChangedTarget ()
		{
		var session = new Session ();
		var original = session.State;
		var result = await ScheduleControlRepetition.RunAsync (_ => session, 3, TimeSpan.FromSeconds (5), default);
		Assert.That (result.Passed && result.ExactRestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs, Is.EqualTo (Enumerable.Repeat (new[] { (false, false), (true, false) }, 3).SelectMany (x => x)));
		Assert.That (session.ManualWrites, Is.EqualTo (new[] { 200, 200, 200 }));
		Assert.That (session.State.Activity.Completed, Is.EqualTo (6));
		Assert.That (ScheduleObservation.Read (original.Room, session.State.Room), Is.True);
		Assert.That (session.Phases.Count (p => p == "repetition-verified"), Is.EqualTo (3));
		}

	[TestCase ("schedule")]
	[TestCase ("command")]
	[TestCase ("epoch")]
	public async Task RepetitionDoesNotAdoptAnInterveningChangeAsANewBaseline (string change)
		{
		var session = new Session ();
		int created = 0;
		var result = await ScheduleControlRepetition.RunAsync (index =>
			{
			created++;
			if (index == 1)
				{
				if (change == "schedule")
					{
					var room = JsonNode.Parse (session.State.Room.GetRawText ())!;
					room["ScheduleId"] = 99;
					session.State = session.State with { Room = JsonSerializer.SerializeToElement (room) };
					}
				else session.State = session.State with { Activity = session.State.Activity with
					{ Completed = change == "command" ? 3 : 2, Epoch = change == "epoch" ? Guid.NewGuid ().ToString ("N") : session.State.Activity.Epoch } };
				}
			return session;
			}, 3, TimeSpan.FromSeconds (5), default);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.False);
		Assert.That (created, Is.EqualTo (2));
		Assert.That (session.Inputs, Has.Count.EqualTo (2));
		}

	[Test]
	public async Task FailedCycleIsNotRepeatedEvenAfterSuccessfulCompensation ()
		{
		var session = new Session { LoseDisableReply = true };
		int created = 0;
		var result = await ScheduleControlRepetition.RunAsync (_ => { created++; return session; }, 3, TimeSpan.FromSeconds (5), default);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ExactRestorationConfirmed, Is.True);
		Assert.That (created, Is.EqualTo (1));
		Assert.That (session.Inputs, Is.EqualTo (new[] { (false, false), (true, true) }));
		}

	[Test]
	public async Task RepetitionCannotInitializeAnAbsentManualTarget ()
		{
		var session = new Session ();
		SetSavedTarget (session, null);
		var result = await ScheduleControlRepetition.RunAsync (_ => session, 3, TimeSpan.FromSeconds (5), default);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		Assert.That (session.ManualWrites, Is.Empty);
		}

	[TestCase (0)]
	[TestCase (4)]
	public void InvalidRepetitionCountNeverCreatesASession (int count) => Assert.ThrowsAsync<ArgumentOutOfRangeException> (() =>
		ScheduleControlRepetition.RunAsync (_ => throw new AssertionException ("No session should be created."), count, TimeSpan.FromSeconds (5), default));
	}