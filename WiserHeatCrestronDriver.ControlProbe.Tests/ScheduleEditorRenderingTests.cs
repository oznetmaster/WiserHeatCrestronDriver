// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class ScheduleEditorRenderingTests
	{
	private const string APP = "com.crestron.phoenix.app";
	private static XElement Node (string id = "", string text = "", string bounds = "[1,1][99,100]") => new ("node",
		new XAttribute ("package", APP), new XAttribute ("enabled", "true"), new XAttribute ("resource-id", id.Length == 0 ? "" : APP + ":id/" + id),
		new XAttribute ("text", text), new XAttribute ("bounds", bounds));
	private static (XElement Xml, JsonObject Editor) Page (int count)
		{
		var viewport = Node ("customdevices_componentRecyclerView", bounds: "[0,0][100,1000]");
		var editor = new JsonObject { ["editScheduleEnabled"] = true };
		for (int slot = 1; slot <= 8; slot++)
			{
			string prefix = "editSlot" + slot.ToString (CultureInfo.InvariantCulture);
			editor[prefix + "Visible"] = slot <= count;
			editor[prefix + "Time"] = slot.ToString ("00", CultureInfo.InvariantCulture) + ":30";
			editor[prefix + "Temperature"] = 15m + slot / 2m;
			if (slot > count) continue;
			var time = Node ();
			time.Add (Node ("statusLabels_value", editor[prefix + "Time"]!.GetValue<string> ()), Node ("statusLabels_description", "TIME " + slot));
			var temperature = Node ();
			temperature.Add (Node ("customdeviceraiselowerwithtext_value", (15m + slot / 2m).ToString ("0.0", CultureInfo.InvariantCulture) + "°"), Node ("customdeviceraiselowerwithtext_label", "SETPOINT " + slot));
			foreach (string action in new[] { "minus", "plus" })
				{
				var button = Node ("customdeviceraiselowerwithtext_" + action);
				button.SetAttributeValue ("clickable", "true");
				temperature.Add (button);
				}
			viewport.Add (time, temperature);
			}
		return (new XElement ("hierarchy", viewport), editor);
		}
	private static IReadOnlyList<ScheduleEditorRenderedValue> Check ((XElement Xml, JsonObject Editor) page, bool complete = true) =>
		ScheduleEditorRendering.RequireValues (page.Xml.ToString (), JsonSerializer.SerializeToElement (page.Editor), complete);

	[Test]
	public void EverySupportedSlotCountHasItsOwnVerifiedValues ([Range (1, 8)] int slots)
		{
		var rows = Check (Page (slots));
		Assert.That (rows.Count, Is.EqualTo (slots * 2));
		Assert.That (rows.Select (row => row.Slot).Distinct (), Is.EquivalentTo (Enumerable.Range (1, slots)));
		}

	[TestCase ("customdeviceraiselowerwithtext_value", "0.0°")]
	[TestCase ("statusLabels_value", "00:00")]
	[TestCase ("customdeviceraiselowerwithtext_label", "SETPOINT 2")]
	[TestCase ("statusLabels_description", "TIME 2")]
	[TestCase ("customdeviceraiselowerwithtext_label", "SETPOINT 11")]
	public void WrongOrMislabelledValueCannotPass (string id, string changed)
		{
		var page = Page (2);
		page.Xml.Descendants ("node").First (node => (string?)node.Attribute ("resource-id") == APP + ":id/" + id).SetAttributeValue ("text", changed);
		Assert.Throws<InvalidDataException> (() => Check (page));
		}

	[TestCase ("disabled")]
	[TestCase ("missing")]
	[TestCase ("not-clickable")]
	[TestCase ("other-app")]
	[TestCase ("ancestor-disabled")]
	public void BrokenSetpointActionCannotPass (string mutation)
		{
		var page = Page (1);
		var plus = page.Xml.Descendants ("node").Single (node => (string?)node.Attribute ("resource-id") == APP + ":id/customdeviceraiselowerwithtext_plus");
		switch (mutation)
			{
			case "disabled": plus.SetAttributeValue ("enabled", "false"); break;
			case "missing": plus.Remove (); break;
			case "not-clickable": plus.SetAttributeValue ("clickable", "false"); break;
			case "other-app": plus.SetAttributeValue ("package", "other.app"); break;
			case "ancestor-disabled":
				var row = plus.Parent!;
				var group = Node ();
				group.SetAttributeValue ("enabled", "false");
				row.ReplaceWith (group);
				group.Add (row);
				break;
			}
		Assert.Throws<InvalidDataException> (() => Check (page));
		}

	[Test]
	public void HiddenSlotMustNotBeRendered ()
		{
		var page = Page (2);
		page.Editor["editSlot2Visible"] = false;
		Assert.Throws<InvalidDataException> (() => Check (page));
		}

	[Test]
	public void DuplicateRowsAndUnscopedPageStacksAreRejected ()
		{
		var page = Page (1);
		page.Xml.Elements ().Single ().Add (new XElement (page.Xml.Descendants ("node").Single (node => (string?)node.Attribute ("text") == "TIME 1").Parent!));
		Assert.Throws<InvalidDataException> (() => Check (page));
		page = Page (1);
		page.Xml.Add (new XElement (page.Xml.Elements ().Single ()));
		Assert.Throws<InvalidDataException> (() => Check (page));
		}

	[TestCase (false)]
	[TestCase (true)]
	public void ClippedRowsCannotProveCompleteCoverage (bool clipOnlyAction)
		{
		var page = Page (1);
		var label = page.Xml.Descendants ("node").Single (node => (string?)node.Attribute ("text") == "SETPOINT 1");
		var target = clipOnlyAction ? label.Parent!.Elements ().Single (node => (string?)node.Attribute ("resource-id") == APP + ":id/customdeviceraiselowerwithtext_plus") : label.Parent!;
		target.SetAttributeValue ("bounds", "[0,900][100,1100]");
		Assert.That (Check (page, false).Select (row => row.Kind), Is.EqualTo (new[] { "Time" }));
		Assert.Throws<InvalidDataException> (() => Check (page));
		}
	[TestCase ("row", true)]
	[TestCase ("row", false)]
	[TestCase ("customdeviceraiselowerwithtext_label", true)]
	[TestCase ("customdeviceraiselowerwithtext_label", false)]
	[TestCase ("customdeviceraiselowerwithtext_value", true)]
	[TestCase ("customdeviceraiselowerwithtext_value", false)]
	[TestCase ("customdeviceraiselowerwithtext_minus", true)]
	[TestCase ("customdeviceraiselowerwithtext_minus", false)]
	[TestCase ("customdeviceraiselowerwithtext_plus", true)]
	[TestCase ("customdeviceraiselowerwithtext_plus", false)]
	public void BoundsClampedToViewportCannotProveFullVisibility (string component, bool top)
		{
		var page = Page (1);
		var row = page.Xml.Descendants ("node").Single (node => (string?)node.Attribute ("text") == "SETPOINT 1").Parent!;
		var target = component == "row" ? row : row.Elements ().Single (node => (string?)node.Attribute ("resource-id") == APP + ":id/" + component);
		// Android clips accessibility bounds at the viewport edge, hiding the original extent.
		target.SetAttributeValue ("bounds", top ? "[1,0][99,29]" : "[1,971][99,1000]");
		Assert.That (Check (page, false).Select (value => value.Kind), Is.EqualTo (new[] { "Time" }));
		Assert.Throws<InvalidDataException> (() => Check (page));
		}
	[TestCase (5, "minus", true)]
	[TestCase (30, "plus", true)]
	[TestCase (5, "plus", false)]
	[TestCase (30, "minus", false)]
	[TestCase (5.5, "minus", false)]
	[TestCase (29.5, "plus", false)]
	public void OnlyOutwardEndpointButtonMayBeDisabled (decimal temperature, string action, bool accepted)
		{
		var page = Page (1);
		page.Editor["editSlot1Temperature"] = temperature;
		page.Xml.Descendants ("node").Single (node => (string?)node.Attribute ("resource-id") == APP + ":id/customdeviceraiselowerwithtext_value")
			.SetAttributeValue ("text", temperature.ToString ("0.0", CultureInfo.InvariantCulture) + "°");
		var button = page.Xml.Descendants ("node").Single (node => (string?)node.Attribute ("resource-id") == APP + ":id/customdeviceraiselowerwithtext_" + action);
		button.SetAttributeValue ("enabled", "false");
		button.SetAttributeValue ("clickable", "false");
		if (accepted) Assert.That (Check (page).Count, Is.EqualTo (2));
		else Assert.Throws<InvalidDataException> (() => Check (page));
		}

	[TestCase ("masked")]
	[TestCase ("other-app")]
	[TestCase ("row-disabled")]
	[TestCase ("missing-enabled")]
	public void EndpointExceptionDoesNotHideBrokenControls (string mutation)
		{
		var page = Page (1);
		page.Editor["editSlot1Temperature"] = 5m;
		page.Xml.Descendants ("node").Single (node => (string?)node.Attribute ("resource-id") == APP + ":id/customdeviceraiselowerwithtext_value").SetAttributeValue ("text", "5.0°");
		var button = page.Xml.Descendants ("node").Single (node => (string?)node.Attribute ("resource-id") == APP + ":id/customdeviceraiselowerwithtext_minus");
		button.SetAttributeValue ("enabled", "false");
		switch (mutation)
			{
			case "masked": button.SetAttributeValue ("password", "true"); break;
			case "other-app": button.SetAttributeValue ("package", "other.app"); break;
			case "row-disabled": button.Parent!.SetAttributeValue ("enabled", "false"); break;
			case "missing-enabled": button.Attribute ("enabled")!.Remove (); break;
			}
		Assert.Throws<InvalidDataException> (() => Check (page));
		}

	}