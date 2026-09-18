// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using NUnit.Framework;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class GatewayPairObservationTests
	{
	private static GatewayInstanceObservation First () => new (new ('a', 64), 7, 9, "First", "1.3.011.0000",
		new ('b', 64), new ('c', 64), new ('d', 64), Guid.Parse ("11766512-2ca3-4939-a52a-cb1196707533"),
		DateTimeOffset.Parse ("2026-09-19T00:00:00Z"), false, false);
	private static GatewayInstanceObservation Second () => First () with
		{ ProcessorIdentity = new ('e', 64), Name = "Second", ConfigurationSha256 = new ('f', 64), Lifetime = Guid.Parse ("e8c0318a-3692-4696-a513-44b85f7959bf") };
	[TestCase (false, false), TestCase (false, true), TestCase (true, false), TestCase (true, true)]
	public void SharedStateChangesAreAllowedButBothInstancesMustRefresh (bool away, bool water)
		{
		var first = First ();
		var second = Second ();
		var nextFirst = first with { RefreshUtc = first.RefreshUtc.AddSeconds (10), Away = away, HotWater = water };
		var nextSecond = second with { RefreshUtc = second.RefreshUtc.AddSeconds (15), Away = away, HotWater = water };
		Assert.That (GatewayPairObservation.HasFreshSharedState (first, second, nextFirst, nextSecond, away, water), Is.True);
		Assert.That (GatewayPairObservation.HasFreshSharedState (first, second, nextFirst, second, away, water), Is.False);
		Assert.That (GatewayPairObservation.HasFreshSharedState (first, second, first, nextSecond, away, water), Is.False);
		Assert.That (GatewayPairObservation.HasFreshSharedState (first, second, nextFirst, nextSecond with { Away = !away }, away, water), Is.False);
		Assert.That (GatewayPairObservation.HasFreshSharedState (first, second, nextFirst, nextSecond with { HotWater = !water }, away, water), Is.False);
		}
	[TestCase ("processor"), TestCase ("lifetime"), TestCase ("version"), TestCase ("package"), TestCase ("hub")]
	public void ASecondLabelCannotSubstituteForAnIndependentPinnedInstance (string difference)
		{
		var first = First ();
		var second = difference switch
			{
			"processor" => Second () with { ProcessorIdentity = first.ProcessorIdentity.ToUpperInvariant () },
			"lifetime" => Second () with { Lifetime = first.Lifetime },
			"version" => Second () with { Version = "1.3.010.0000" },
			"package" => Second () with { PackageSha256 = new ('a', 64) },
			_ => Second () with { HubBindingSha256 = new ('a', 64) }
			};
		Assert.Throws<InvalidDataException> (() => GatewayPairObservation.RequirePair (first, second));
		}
	[TestCase ("configuration"), TestCase ("device"), TestCase ("location"), TestCase ("name"), TestCase ("restart"), TestCase ("clock")]
	public void LocalIdentityAndSettingsMustRemainUnchanged (string difference)
		{
		var first = First ();
		var changed = difference switch
			{
			"configuration" => first with { ConfigurationSha256 = new ('f', 64) },
			"device" => first with { DeviceId = 8 },
			"location" => first with { LocationId = 10 },
			"name" => first with { Name = "Renamed" },
			"restart" => first with { Lifetime = Second ().Lifetime },
			_ => first with { RefreshUtc = first.RefreshUtc.AddSeconds (-1) }
			};
		Assert.Throws<InvalidDataException> (() => GatewayPairObservation.RequirePreserved (first, changed));
		}
	[Test]
	public void IncompleteIdentityCannotPassAsUnchanged ()
		{
		var empty = First () with { ConfigurationSha256 = "", Lifetime = Guid.Empty };
		Assert.Throws<InvalidDataException> (() => GatewayPairObservation.RequirePreserved (empty, empty));
		}
	}