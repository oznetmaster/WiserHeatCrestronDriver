// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class ScheduleChoiceSequenceTests
	{
	private static readonly string[] _labels = Enumerable.Range (0, 48).Select (n => $"{n / 2:00}:{n % 2 * 30:00}").ToArray ();

	[TestCase (0), TestCase (23), TestCase (47)]
	public void BoundedSelectionsChangeTheValueAndReturnToTheOriginal (int start)
		{
		var sequence = ScheduleChoiceSequence.Create (_labels, _labels[start], false);
		Assert.Multiple (() =>
			{
			Assert.That (sequence.Length, Is.InRange (2, 3));
			Assert.That (sequence, Does.Contain (_labels[0]).And.Contain (_labels[^1]));
			Assert.That (sequence.Any (value => value != _labels[start]), Is.True);
			Assert.That (sequence[^1], Is.EqualTo (_labels[start]));
			Assert.That (_labels.Length, Is.EqualTo (48), "Do not mutate the offered inventory.");
			});
		}

	[TestCase (0), TestCase (47)]
	public void ExhaustiveRegressionStillSelectsEveryValue (int start)
		{
		var sequence = ScheduleChoiceSequence.Create (_labels, _labels[start], true);
		Assert.That (sequence.Take (48), Is.EqualTo (_labels));
		Assert.That (sequence[^1], Is.EqualTo (_labels[start]));
		}

	[Test]
	public void AControlWithoutAnAlternativeCannotPretendToExerciseAChange () =>
		Assert.Throws<ArgumentException> (() => ScheduleChoiceSequence.Create (["only"], "only", false));

	[Test]
	public void AmbiguousInventoryIsRejectedBeforeInput () =>
		Assert.Throws<ArgumentException> (() => ScheduleChoiceSequence.Create (["first", "first"], "first", false));

	[Test]
	public void MissingStartingValueIsRejectedBeforeInput () =>
		Assert.Throws<ArgumentException> (() => ScheduleChoiceSequence.Create (_labels, "unknown", false));
	}