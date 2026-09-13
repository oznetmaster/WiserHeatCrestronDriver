// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System;
using System.Collections;
using System.Collections.Generic;

using NUnit.Framework;

using WiserHeat.CrestronDriver;
namespace WiserHeatCrestronDriver.Tests;

[TestFixture, FixtureLifeCycle (LifeCycle.InstancePerTestCase)]
public sealed class ScheduleTests
	{
	private static T Call<T> (string name, params object[] args) => TestSupport.Call<T> (typeof (WiserRoomEntity), name, args);
	[TestCase ("6:30", "06:30")]
	[TestCase ("06:30", "06:30")]
	[TestCase ("630", "06:30")]
	[TestCase ("0630", "06:30")]
	[TestCase (" 23:59 ", "23:59")]
	[TestCase ("0000", "00:00")]
	public void TimeEditor_AcceptsSupportedFormats (string input, string expected)
		{
		object[] args = { input, null };
		Assert.That (Call<bool> ("TryParseTimeText", args), Is.True);
		Assert.That (args[1], Is.EqualTo (expected));
		}
	[TestCase (null)]
	[TestCase ("")]
	[TestCase (" ")]
	[TestCase ("24:00")]
	[TestCase ("12:60")]
	[TestCase ("-1:00")]
	[TestCase ("morning")]
	public void TimeEditor_RejectsInvalidTimes (string input)
		{
		object[] args = { input, null };
		Assert.That (Call<bool> ("TryParseTimeText", args), Is.False);
		Assert.That (args[1], Is.EqualTo (string.Empty));
		}
	[Test]
	public void EditingSchedule_DeepCopiesNestedCollectionsAndPreservesOriginal ()
		{
		var times = new List<object> { 630, 2200 };
		var day = new Dictionary<string, object> { ["Time"] = times, ["DegreesC"] = new List<object> { 200, 160 } };
		var original = new Dictionary<string, object> { ["Monday"] = day, ["Name"] = "Office", ["Optional"] = null };
		var copy = Call<IDictionary<string, object>> ("CloneScheduleData", original);
		var copiedDay = (IDictionary<string, object>)copy["monday"];
		((IList<object>)copiedDay["time"])[0] = 700;
		copiedDay["Added"] = true;
		Assert.That (times[0], Is.EqualTo (630));
		Assert.That (day.ContainsKey ("Added"), Is.False);
		Assert.That (copy["Name"], Is.EqualTo ("Office"));
		Assert.That (copy["Optional"], Is.Null);
		}
	[Test] public void MissingSchedule_CreatesEmptyEditableCopy () => Assert.That (Call<IDictionary<string, object>> ("CloneScheduleData", (object)null), Is.Empty);
	[Test] public void MissingDay_HasNoEditableSlots () => Assert.That (Call<IList> ("BuildEditableSlots", (object)null), Is.Empty);
	[Test] public void MissingTemperatures_HasNoEditableSlots () => Assert.That (Call<IList> ("BuildEditableSlots", new Dictionary<string, object> { ["Time"] = new List<object> { 600 } }), Is.Empty);
	[Test]
	public void ScheduleEditor_DecodesTimesAndTemperaturesThenSortsOnSave ()
		{
		var day = new Dictionary<string, object> { ["Time"] = new List<object> { 2200, 630 }, ["DegreesC"] = new List<object> { 160, 205 } };
		var slots = Call<IList> ("BuildEditableSlots", day);
		Assert.That (slots.Count, Is.EqualTo (2));
		Assert.That (slots[1].GetType ().GetProperty ("Time").GetValue (slots[1]), Is.EqualTo ("06:30"));
		Assert.That (slots[1].GetType ().GetProperty ("Temperature").GetValue (slots[1]), Is.EqualTo (20.5));
		var saved = Call<Dictionary<string, object>> ("BuildRawDaySchedule", slots);
		Assert.That (saved["Time"], Is.EqualTo (new[] { 630, 2200 }));
		Assert.That (saved["DegreesC"], Is.EqualTo (new[] { 205, 160 }));
		}
	[Test]
	public void MismatchedScheduleArrays_UseOnlyCompleteSlots ()
		{
		var day = new Dictionary<string, object> { ["Time"] = new List<object> { 600, 1800 }, ["DegreesC"] = new List<object> { 200 } };
		Assert.That (Call<IList> ("BuildEditableSlots", day).Count, Is.EqualTo (1));
		}
	[Test]
	public void SavedSchedule_CanBeOpenedAndSavedAgainWithoutLosingSlots ()
		{
		var original = new Dictionary<string, object> { ["Time"] = new List<object> { 630, 2200 }, ["DegreesC"] = new List<object> { 205, 160 } };
		var saved = Call<Dictionary<string, object>> ("BuildRawDaySchedule", Call<IList> ("BuildEditableSlots", original));
		var reopened = Call<IList> ("BuildEditableSlots", saved);
		Assert.That (reopened.Count, Is.EqualTo (2), "Opening a locally saved day must preserve its slots.");
		var resaved = Call<Dictionary<string, object>> ("BuildRawDaySchedule", reopened);
		Assert.That (resaved["Time"], Is.EqualTo (original["Time"]));
		Assert.That (resaved["DegreesC"], Is.EqualTo (original["DegreesC"]));
		}
	[TestCase (false)]
	[TestCase (true)]
	public void TypedScheduleCollections_AreCopiedBeforeEditing (bool useArray)
		{
		IList times = useArray ? new[] { 630, 2200 } : new List<int> { 630, 2200 };
		var day = new Dictionary<string, object> { ["Time"] = times, ["DegreesC"] = new[] { 205, 160 } };
		var original = new Dictionary<string, object> { ["Monday"] = day, ["Name"] = "Office" };
		var copy = Call<IDictionary<string, object>> ("CloneScheduleData", original);
		var copiedDay = (IDictionary<string, object>)copy["Monday"];
		var copiedTimes = (IList)copiedDay["Time"];
		Assert.That (copiedTimes, Is.Not.SameAs (times));
		copiedTimes[0] = 700;
		Assert.That (times[0], Is.EqualTo (630));
		Assert.That (copy["Name"], Is.EqualTo ("Office"));
		Assert.That (Call<IList> ("BuildEditableSlots", copiedDay).Count, Is.EqualTo (2));
		}

	}