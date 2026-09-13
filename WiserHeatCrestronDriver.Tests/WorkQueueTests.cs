// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using NUnit.Framework;

using WiserHeat.CrestronDriver;

using WiserHeatApiV2;
namespace WiserHeatCrestronDriver.Tests;

[TestFixture, FixtureLifeCycle (LifeCycle.InstancePerTestCase)]
public sealed class WorkQueueTests
	{
	private readonly WiserWorkQueue _queue = new ();
	private readonly WiserAPI _client = new ("example.invalid", "synthetic-test-secret");
	[SetUp] public void SetUp () => _queue.SetClient (_client);
	[TearDown]
	public void TearDown ()
		{
		_queue.Stop ();
		_client.Dispose ();
		}
	[Test]
	public async Task WorkReceivesTheCurrentClient ()
		{
		WiserAPI observed = null;
		await TestSupport.Complete (_queue.EnqueueAsync (c => { observed = c; return Task.CompletedTask; }));
		Assert.That (observed, Is.SameAs (_client));
		}
	[Test]
	public async Task QueuedOperations_DoNotOverlap ()
		{
		var release = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		var order = new List<string> ();
		var first = _queue.EnqueueAsync (async c => { order.Add ("first-start"); await release.Task; order.Add ("first-end"); });
		var second = _queue.EnqueueAsync (c => { order.Add ("second"); return Task.CompletedTask; });
		try
			{
			Assert.That (second.IsCompleted, Is.False);
			}
		finally { release.TrySetResult (true); await TestSupport.Complete (Task.WhenAll (first, second)); }
		Assert.That (order, Is.EqualTo (new[] { "first-start", "first-end", "second" }));
		}
	[Test]
	public async Task FailedOperation_ReleasesQueueForFollowingWork ()
		{
		var error = new InvalidOperationException ("synthetic failure");
		Assert.That (Assert.ThrowsAsync<InvalidOperationException> (async () => await TestSupport.Complete (_queue.EnqueueAsync (c => Task.FromException (error)))), Is.SameAs (error));
		bool ran = false;
		await TestSupport.Complete (_queue.EnqueueAsync (c => { ran = true; return Task.CompletedTask; }));
		Assert.That (ran, Is.True);
		}
	[Test]
	public async Task Stop_DropsPendingWorkAndCannotBeReactivated ()
		{
		var release = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		var first = _queue.EnqueueAsync (c => release.Task);
		bool ran = false;
		var pending = _queue.EnqueueAsync (c => { ran = true; return Task.CompletedTask; });
		try
			{
			_queue.Stop ();
			_queue.SetClient (_client);
			}
		finally { release.TrySetResult (true); await TestSupport.Complete (Task.WhenAll (first, pending)); }
		await TestSupport.Complete (_queue.EnqueueAsync (c => { ran = true; return Task.CompletedTask; }));
		Assert.That (ran, Is.False);
		}
	[Test]
	public async Task ClearingClient_PreventsPendingCallbackAndCanRecover ()
		{
		var release = new TaskCompletionSource<bool> (TaskCreationOptions.RunContinuationsAsynchronously);
		var first = _queue.EnqueueAsync (c => release.Task);
		bool ran = false;
		var pending = _queue.EnqueueAsync (c => { ran = true; return Task.CompletedTask; });
		try
			{
			_queue.ClearClient ();
			}
		finally { release.TrySetResult (true); await TestSupport.Complete (first); }
		Assert.ThrowsAsync<InvalidOperationException> (async () => await TestSupport.Complete (pending));
		Assert.That (ran, Is.False);
		_queue.SetClient (_client);
		await TestSupport.Complete (_queue.EnqueueAsync (c => { ran = true; return Task.CompletedTask; }));
		Assert.That (ran, Is.True);
		}
	[Test] public void NullWork_IsRejected () => Assert.ThrowsAsync<ArgumentNullException> (async () => await _queue.EnqueueAsync (null));
	[Test] public void NullClient_IsRejected () => Assert.Throws<ArgumentNullException> (() => _queue.SetClient (null));
	}