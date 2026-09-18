// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

public sealed partial class ScheduleControlCycleTests
	{
	private sealed class Observer : IScheduleControlObserver
		{
		public string? Fail { get; init; }
		public bool Cancel { get; init; }
		public Action? Before { get; init; }
		public List<string> Calls { get; } = [];
		private Task Observe (string phase, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Calls.Add (phase);
			if (phase == "before") Before?.Invoke ();
			if (Fail == phase || Fail == "persistent" && phase != "before")
				throw Cancel ? new OperationCanceledException () : new IOException ("Injected lost peer");
			return Task.CompletedTask;
			}
		public Task BeforeInputAsync (ScheduleControlSnapshot state, CancellationToken token) => Observe ("before", token);
		public Task AfterInputAsync (ScheduleControlSnapshot state, CancellationToken token) => Observe ("after", token);
		public Task AfterRestorationAsync (ScheduleControlSnapshot state, CancellationToken token) => Observe ("restored", token);
		}

	[TestCase ("before", false)]
	[TestCase ("before", true)]
	[TestCase ("after", false)]
	[TestCase ("after", true)]
	[TestCase ("restored", false)]
	[TestCase ("restored", true)]
	[TestCase ("persistent", false)]
	public async Task PeerFailureCannotPreventIndependentRestoration (string phase, bool cancel)
		{
		var session = new Session ();
		var original = session.State.Room;
		var observer = new Observer { Fail = phase, Cancel = cancel };
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), CancellationToken.None, observer: observer);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ExactRestorationConfirmed, Is.True);
		Assert.That (JsonElement.DeepEquals (session.State.Room, original), Is.True);
		Assert.That (session.Inputs, Has.Count.EqualTo (phase == "before" ? 0 : 2));
		if (phase is "after" or "persistent") Assert.That (session.Inputs.Last (), Is.EqualTo ((true, true)));
		if (phase is "restored" or "persistent") Assert.That (session.Phases, Does.Contain ("restored-peer-failed"));
		}

	[Test]
	public async Task PeerSuccessObservesAllThreeStates ()
		{
		var session = new Session ();
		var observer = new Observer ();
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), CancellationToken.None, observer: observer);
		Assert.That (result.Passed && result.ExactRestorationConfirmed, Is.True);
		Assert.That (observer.Calls, Is.EqualTo (new[] { "before", "after", "restored" }));
		Assert.That (session.Inputs, Is.EqualTo (new[] { (false, false), (true, false) }));
		}

	[Test]
	public async Task PrimaryChangeWhileWaitingForPeerStopsBeforeInput ()
		{
		var session = new Session ();
		var observer = new Observer { Before = () => session.State = session.State with { Activity = session.State.Activity with { Completed = 1 } } };
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), CancellationToken.None, observer: observer);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		Assert.That (session.ManualWrites, Is.Empty);
		}

	[Test]
	public async Task ScheduleBoundaryWhileWaitingDoesNotUseStaleStartingTarget ()
		{
		var session = new Session ();
		var observer = new Observer { Before = () => session.State = session.State with
			{ Room = JsonSerializer.Deserialize<JsonElement> (Session.Auto.Replace ("\"CurrentSetPoint\":220", "\"CurrentSetPoint\":230").Replace ("\"ScheduledSetPoint\":220", "\"ScheduledSetPoint\":230")) } };
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), CancellationToken.None, observer: observer);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		}

	[Test]
	public async Task FailedPeerRestorationObservationPreservesEarlierInputFailure ()
		{
		var session = new Session { LoseDisableReply = true };
		var observer = new Observer { Fail = "restored" };
		var result = await ScheduleControlCycle.RunAsync (session, TimeSpan.FromSeconds (5), CancellationToken.None, observer: observer);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ExactRestorationConfirmed, Is.True);
		Assert.That (result.Detail, Does.Contain ("disable input or observation").And.Contain ("peer verification also failed"));
		Assert.That (session.Inputs, Is.EqualTo (new[] { (false, false), (true, true) }));
		}
	}