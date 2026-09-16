// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Threading.Tasks;

using NUnit.Framework;

using WiserHeat.CrestronDriver;

namespace WiserHeatCrestronDriver.Tests;

[TestFixture]
public sealed class CommandActivityTests
	{
	[Test]
	public void NewEntity_HasIndependentEpoch ()
		{
		Assert.That (new CommandActivity ().Snapshot, Is.Not.EqualTo (new CommandActivity ().Snapshot));
		}
	[Test]
	public void CompletionWithoutAcceptance_IsRejected ()
		{
		var activity = new CommandActivity ();
		Assert.Throws<InvalidOperationException> (() => activity.Complete ());
		Assert.That (activity.Snapshot, Does.Contain ("\"Completed\":0,\"Pending\":0"));
		}
	[Test]
	public void ConcurrentCommands_AreCountedWithoutLosingCompletions ()
		{
		var activity = new CommandActivity ();
		Parallel.For (0, 500, _ => { activity.Begin (); activity.Complete (); });
		Assert.That (activity.Snapshot, Does.Contain ("\"Completed\":500,\"Pending\":0"));
		}
	}