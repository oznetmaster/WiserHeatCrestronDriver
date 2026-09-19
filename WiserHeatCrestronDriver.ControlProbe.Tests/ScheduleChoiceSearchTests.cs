// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class ScheduleChoiceSearchTests
	{
	private static readonly string[] _labels = Enumerable.Range (0, 48).Select (n => $"{n / 2:00}:{n % 2 * 30:00}").ToArray ();
	private static ScheduleChoiceValue[] Rows (int start, int count, string selected) => _labels.Skip (start).Take (count).Select (s => new ScheduleChoiceValue (s, s == selected)).ToArray ();

	[TestCase (0), TestCase (1), TestCase (23), TestCase (24), TestCase (46), TestCase (47)]
	public void FindsExactChoiceFromAPreservedMiddleScrollPosition (int target)
		{
		var search = new ScheduleChoiceSearch (_labels, _labels[24], _labels[target]);
		int start = 22;
		for (int i = 0; i < 48; i++)
			{
			var action = search.Observe (Rows (start, 4, _labels[24]));
			if (action == ScheduleChoiceSearchAction.Select)
				{
				Assert.That (target, Is.InRange (start, start + 3));
				Assert.Throws<InvalidOperationException> (() => search.Observe (Rows (start, 4, _labels[24])));
				return;
				}
			start = Math.Clamp (start + (action == ScheduleChoiceSearchAction.ScrollUp ? -3 : 3), 0, 44);
			}
		Assert.Fail ("No exact selection was reached.");
		}

	[Test]
	public void StationaryViewportStopsInsteadOfRepeatingScroll ()
		{
		var search = new ScheduleChoiceSearch (_labels, _labels[24], _labels[0]);
		Assert.That (search.Observe (Rows (22, 4, _labels[24])), Is.EqualTo (ScheduleChoiceSearchAction.ScrollUp));
		Assert.Throws<InvalidDataException> (() => search.Observe (Rows (22, 4, _labels[24])));
		}

	[TestCase ("unknown"), TestCase ("selection"), TestCase ("order"), TestCase ("gap"), TestCase ("duplicate"), TestCase ("empty")]
	public void UnexpectedListCannotChooseAnItem (string defect)
		{
		var search = new ScheduleChoiceSearch (_labels, _labels[24], _labels[0]);
		var rows = Rows (22, 4, _labels[24]);
		rows = defect switch
			{
			"unknown" => [new ("Unknown", false)],
			"selection" => Rows (22, 4, _labels[23]),
			"order" => rows.Reverse ().ToArray (),
			"gap" => [rows[0], rows[2]],
			"duplicate" => [rows[0], rows[0]],
			_ => []
			};
		Assert.Throws<InvalidDataException> (() => search.Observe (rows));
		}

	[Test]
	public void AnOscillatingListHasAFiniteObservationLimit ()
		{
		var search = new ScheduleChoiceSearch (_labels, _labels[24], _labels[0]);
		for (int i = 0; i < 128; i++) search.Observe (Rows (22 + i % 2, 4, _labels[24]));
		Assert.Throws<InvalidOperationException> (() => search.Observe (Rows (22, 4, _labels[24])));
		}
	}