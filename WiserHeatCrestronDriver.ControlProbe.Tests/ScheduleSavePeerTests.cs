// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

public sealed partial class ScheduleSaveIsolationTests
	{
	private sealed class Peer : IScheduleSaveObserver
		{
		public string? Fail { get; init; }
		public bool Cancel { get; init; }
		public Action<string>? DuringObservation { get; init; }
		public List<string> Calls { get; } = [];
		private ScheduleHubSnapshot? _original;
		private Task Observe (string phase, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Calls.Add (phase);
			DuringObservation?.Invoke (phase);
			if (Fail == phase || Fail == "persistent" && phase is "after-day" or "after-all" or "restored")
				throw Cancel ? new OperationCanceledException () : new IOException ("Peer unavailable");
			return Task.CompletedTask;
			}
		public Task BeforeChangesAsync (ScheduleHubSnapshot state, int roomId, CancellationToken token)
			{
			_original = state;
			return Observe ("original", token);
			}
		public Task BeforeSaveAsync (ScheduleHubSnapshot state, ScheduleSaveCase operation, CancellationToken token) => Observe (operation.AllDays ? "before-all" : "before-day", token);
		public Task AfterSaveAsync (ScheduleHubSnapshot state, ScheduleSaveCase operation, CancellationToken token) => Observe (operation.AllDays ? "after-all" : "after-day", token);
		public Task AfterRestorationAsync (ScheduleHubSnapshot state, int roomId, CancellationToken token)
			{
			ScheduleSaveIsolation.RequireOriginal (_original!, state);
			return Observe ("restored", token);
			}
		}

	private static Task<ScheduleSaveIsolationResult> RunPeer (Session session, bool existing, Peer peer) => existing
		? ScheduleSaveIsolation.RunExistingAsync (session, 1, TimeSpan.FromSeconds (1), CancellationToken.None, peer)
		: ScheduleSaveIsolation.RunAsync (session, 1, Guid.NewGuid (), TimeSpan.FromSeconds (1), CancellationToken.None, peer);

	[Test]
	public async Task PeerLossCannotPreventScheduleRestoration (
		[Values (false, true)] bool existing,
		[Values ("original", "before-day", "after-day", "before-all", "after-all", "restored", "persistent")] string phase)
		{
		var session = existing ? Exclusive () : new Session ();
		var original = await session.ReadAsync (default);
		var peer = new Peer { Fail = phase };
		var result = await RunPeer (session, existing, peer);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		if (phase == "restored") Assert.That (result.Detail, Does.Not.Contain ("no hub mutation"));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		if (phase == "original") Assert.That (session.Calls, Is.Empty);
		else
			{
			Assert.That (peer.Calls.Last (), Is.EqualTo ("restored"));
			Assert.That (session.Calls.Last (), Is.EqualTo ("editor"));
			if (!existing) Assert.That (session.Calls.Count (c => c == "delete"), Is.EqualTo (1));
			}
		if (phase is "after-day" or "before-all" or "persistent") Assert.That (session.Saves, Has.Count.EqualTo (1));
		}

	[Test]
	public async Task PeerCancellationCannotCancelCompensation ([Values (false, true)] bool existing,
		[Values ("after-day", "before-all", "restored")] string phase)
		{
		var session = existing ? Exclusive () : new Session ();
		var original = await session.ReadAsync (default);
		var result = await RunPeer (session, existing, new () { Fail = phase, Cancel = true });
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}

	[Test]
	public async Task SuccessfulPeerSeesBothSavesAndRestoration ([Values (false, true)] bool existing)
		{
		var session = existing ? Exclusive () : new Session ();
		var peer = new Peer ();
		var result = await RunPeer (session, existing, peer);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (peer.Calls, Is.EqualTo (new[] { "original", "before-day", "after-day", "before-all", "after-all", "restored" }));
		}

	[Test]
	public async Task UnavailableEditorDoesNotBlockPhysicalCleanup ([Values (false, true)] bool existing)
		{
		var session = new Session { FailEditorRestoration = true };
		if (existing) Exclusive (session);
		var original = await session.ReadAsync (default);
		var peer = new Peer ();
		var result = await RunPeer (session, existing, peer);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False, "Overall restoration includes the editor; physical restoration is recorded separately.");
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		Assert.That (session.Records, Does.Contain ("hub-restored").And.Contain ("recovery-required"));
		Assert.That (peer.Calls.Last (), Is.EqualTo ("restored"));
		if (!existing) Assert.That (session.Calls.IndexOf ("delete"), Is.LessThan (session.Calls.IndexOf ("editor")));
		}

	[Test]
	public async Task PrimaryChangeWhilePeerWaitsPreventsSave ([Values (false, true)] bool existing)
		{
		var session = existing ? Exclusive () : new Session ();
		var peer = new Peer { DuringObservation = phase =>
			{
			if (phase == "before-day") session.Domain["Room"]![0]!["EcoModeEnabled"] = true;
			} };
		var result = await RunPeer (session, existing, peer);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Saves, Is.Empty);
		Assert.That (session.Domain["Room"]![0]!["EcoModeEnabled"]!.GetValue<bool> (), Is.True, "Do not undo someone else's concurrent change.");
		}
	}