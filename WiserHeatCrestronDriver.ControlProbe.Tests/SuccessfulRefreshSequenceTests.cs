// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class SuccessfulRefreshSequenceTests
	{
	private const string Lifetime = "ef64a66128b649a08d0930318f8577fa";
	private const string First = "utc:2026-09-18T00:00:00.1234567+00:00";
	[Test]
	public void SnapshotTimestampUsesTheExactTaggedDriverFormat ()
		{
		var timestamp = SuccessfulRefreshSequence.ParseTimestamp (First);
		Assert.That (timestamp.Offset, Is.EqualTo (TimeSpan.Zero));
		Assert.That (timestamp.ToString ("O", System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo (First[4..]));
		}
	[Test]
	public void CachedReadsDoNotCountAsRefreshes ()
		{
		var sequence = new SuccessfulRefreshSequence (Lifetime, First);
		for (int i = 0; i < 10; i++) Assert.That (sequence.Observe (Lifetime, First), Is.False);
		Assert.That (sequence.Advances, Is.Zero);
		Assert.That (sequence.Observe (Lifetime, "utc:2026-09-18T00:00:30.1234567+00:00"), Is.True);
		Assert.That (sequence.Observe (Lifetime, "utc:2026-09-18T00:00:30.1234567+00:00"), Is.False);
		Assert.That (sequence.Observe (Lifetime, "utc:2026-09-18T00:01:00.1234567+00:00"), Is.True);
		Assert.That (sequence.Advances, Is.EqualTo (2));
		}
	[Test]
	public void RestartCannotBeSubstitutedForARefresh ()
		{
		var sequence = new SuccessfulRefreshSequence (Lifetime, First);
		Assert.Throws<InvalidDataException> (() => sequence.Observe (Guid.NewGuid ().ToString ("N"), "utc:2026-09-18T00:01:00.1234567+00:00"));
		Assert.That (sequence.Advances, Is.Zero);
		}
	[Test]
	public void BackwardsMarkerFails ()
		{
		var sequence = new SuccessfulRefreshSequence (Lifetime, First);
		Assert.Throws<InvalidDataException> (() => sequence.Observe (Lifetime, "utc:2026-09-17T23:59:59.1234567+00:00"));
		}
	[TestCase (""), TestCase ("2026-09-18T00:00:00.1234567+00:00"), TestCase ("utc:2026-09-18"),
	 TestCase ("utc:2026-09-18T01:00:00.1234567+01:00"), TestCase ("utc:0001-01-01T00:00:00.0000000+00:00")]
	public void MalformedMarkersCannotProveARefresh (string marker)
		{
		Assert.Throws<InvalidDataException> (() => SuccessfulRefreshSequence.ParseTimestamp (marker));
		Assert.Throws<InvalidDataException> (() => new SuccessfulRefreshSequence (Lifetime, marker));
		var sequence = new SuccessfulRefreshSequence (Lifetime, First);
		Assert.Throws<InvalidDataException> (() => sequence.Observe (Lifetime, marker));
		Assert.That (sequence.Advances, Is.Zero);
		}
	[TestCase (""), TestCase ("00000000000000000000000000000000")]
	public void InvalidLifetimeCannotStartASequence (string lifetime) =>
		Assert.Throws<InvalidDataException> (() => new SuccessfulRefreshSequence (lifetime, First));
	}