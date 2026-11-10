// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Threading.Tasks;

using NUnit.Framework;

using WiserHeat.CrestronDriver;

using WiserHeatApiV2;

namespace WiserHeatCrestronDriver.Tests;

[TestFixture]
public sealed class ConnectionQueueTests
	{
	[Test]
	public async Task ReplacedClientDoesNotReceiveOldQueuedControl ()
		{
		using var original = new WiserAPI ("old.invalid", "fake");
		using var replacement = new WiserAPI ("new.invalid", "fake");
		var queue = new WiserWorkQueue ();
		queue.SetClient (original);
		var release = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		Task active = queue.EnqueueAsync (_ => release.Task);
		bool sent = false;
		Task pending = queue.EnqueueAsync (_ => { sent = true; return Task.CompletedTask; });
		queue.SetClient (replacement);
		release.SetResult (true);
		await TestSupport.Complete (Task.WhenAll (active, pending));
		Assert.That (sent, Is.False);
		await queue.EnqueueAsync (_ => { sent = true; return Task.CompletedTask; });
		Assert.That (sent, Is.True);
		queue.Stop ();
		}

	[Test]
	public async Task TerminalDrainWaitsForAdmittedWorkAndDropsPendingControl ()
		{
		using var client = new WiserAPI ("fake.invalid", "fake");
		var queue = new WiserWorkQueue ();
		queue.SetClient (client);
		var release = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		Task active = queue.EnqueueAsync (_ => release.Task);
		bool sent = false;
		Task pending = queue.EnqueueAsync (_ => { sent = true; return Task.CompletedTask; });
		queue.Stop ();
		Task drained = queue.DrainAsync ();
		Assert.That (drained.IsCompleted, Is.False);
		release.SetResult (true);
		await TestSupport.Complete (Task.WhenAll (active, pending, drained));
		Assert.That (sent, Is.False);
		}
	}