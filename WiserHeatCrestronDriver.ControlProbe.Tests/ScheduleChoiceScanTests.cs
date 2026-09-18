// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class ScheduleChoiceScanTests
	{
	private static ScheduleChoiceValue[] View (params string[] labels) => labels.Select (label => new ScheduleChoiceValue (label, label == "A")).ToArray ();

	[Test]
	public void PreservedAnchorBelowRenamedItem_ScansUpThenEntireListDown ()
		{
		var scan = new ScheduleChoiceScan (["A", "B", "C", "D"], "A");
		Assert.That (scan.Observe (View ("B", "C")), Is.EqualTo (ScheduleChoiceScanAction.ScrollUp));
		Assert.That (scan.Observe (View ("A", "B")), Is.EqualTo (ScheduleChoiceScanAction.ScrollUp));
		Assert.That (scan.Observe (View ("A", "B")), Is.EqualTo (ScheduleChoiceScanAction.ScrollDown));
		Assert.That (scan.Observe (View ("B", "C")), Is.EqualTo (ScheduleChoiceScanAction.ScrollDown));
		Assert.That (scan.Observe (View ("C", "D")), Is.EqualTo (ScheduleChoiceScanAction.ScrollDown));
		Assert.That (scan.Observe (View ("C", "D")), Is.EqualTo (ScheduleChoiceScanAction.Complete));
		}

	[Test]
	public void ActuallyMissingRenamedOption_IsNotAcceptedAfterBothEnds ()
		{
		var scan = new ScheduleChoiceScan (["A", "B", "C"], "A");
		scan.Observe (View ("B", "C"));
		scan.Observe (View ("B", "C"));
		scan.Observe (View ("B", "C"));
		Assert.Throws<InvalidDataException> (() => scan.Observe (View ("B", "C")));
		}

	[Test]
	public void EntireListFits_StillVerifiesBothEnds ()
		{
		var scan = new ScheduleChoiceScan (["A", "B"], "A");
		Assert.That (scan.Observe (View ("A", "B")), Is.EqualTo (ScheduleChoiceScanAction.ScrollUp));
		Assert.That (scan.Observe (View ("A", "B")), Is.EqualTo (ScheduleChoiceScanAction.ScrollDown));
		Assert.That (scan.Observe (View ("A", "B")), Is.EqualTo (ScheduleChoiceScanAction.ScrollDown));
		Assert.That (scan.Observe (View ("A", "B")), Is.EqualTo (ScheduleChoiceScanAction.Complete));
		Assert.Throws<InvalidOperationException> (() => scan.Observe (View ("A", "B")));
		}

	[TestCase ("Old"), TestCase ("B")]
	public void StaleOrIncorrectSelectedOption_Fails (string label)
		{
		var scan = new ScheduleChoiceScan (["A", "B"], "A");
		Assert.Throws<InvalidDataException> (() => scan.Observe ([new (label, true)]));
		}

	[Test]
	public void DuplicatedRow_Fails ()
		{
		var scan = new ScheduleChoiceScan (["A", "B"], "A");
		Assert.Throws<InvalidDataException> (() => scan.Observe (View ("A", "A")));
		}

	[Test]
	public void MissingSelectedMark_Fails ()
		{
		var scan = new ScheduleChoiceScan (["A", "B"], "A");
		Assert.Throws<InvalidDataException> (() => scan.Observe ([new ("A", false)]));
		}

	[Test]
	public void NonterminatingTraversal_IsBounded ()
		{
		var scan = new ScheduleChoiceScan (["A", "B"], "A");
		for (int i = 0; i < 128; i++) scan.Observe (View (i % 2 == 0 ? "A" : "B"));
		Assert.Throws<InvalidOperationException> (() => scan.Observe (View ("A")));
		}
	}