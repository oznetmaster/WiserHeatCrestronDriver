// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record ScheduleEditorRenderedValue (int Slot, string Kind, string Text);

/// <summary>Checks rendered controls on an already verified, scoped front editor page.</summary>
public static class ScheduleEditorRendering
	{
	private const string APP = "com.crestron.phoenix.app";
	private const string PREFIX = APP + ":id/";
	private static bool Is (XElement node, string id) => (string?)node.Attribute ("resource-id") == PREFIX + id;
	private static string Text (XElement node) => (string?)node.Attribute ("text") ?? string.Empty;
	private static void Enabled (XElement node, bool allowDisabledSelf = false)
		{
		foreach (var current in node.AncestorsAndSelf ("node"))
			if ((string?)current.Attribute ("package") != APP || ((string?)current.Attribute ("enabled") != "true" && !(allowDisabledSelf && current == node && (string?)current.Attribute ("enabled") == "false")) || (string?)current.Attribute ("password") == "true")
				throw new InvalidDataException ("An editor control or its containing group belongs to another app, is masked, or is disabled.");
		}
	private static (int Left, int Top, int Right, int Bottom) Bounds (XElement node)
		{
		var match = Regex.Match ((string?)node.Attribute ("bounds") ?? string.Empty, @"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$");
		if (!match.Success || !int.TryParse (match.Groups[1].Value, out int left) || !int.TryParse (match.Groups[2].Value, out int top) ||
			!int.TryParse (match.Groups[3].Value, out int right) || !int.TryParse (match.Groups[4].Value, out int bottom) || right <= left || bottom <= top)
			throw new InvalidDataException ("Invalid editor control bounds.");
		return (left, top, right, bottom);
		}
	private static bool Inside (XElement node, XElement viewport)
		{
		var row = Bounds (node);
		var view = Bounds (viewport);
		// Android clamps partially clipped controls to the viewport bounds. An edge-touching
		// control has no evidence of its full vertical extent, so observe it in another viewport.
		return row.Left >= view.Left && row.Right <= view.Right && row.Top > view.Top && row.Bottom < view.Bottom;
		}

	/// <summary>Every complete observed row must match its own labelled slot. Partial rows cannot prove coverage.</summary>
	public static IReadOnlyList<ScheduleEditorRenderedValue> RequireValues (string frontPageXml, JsonElement editor, bool requireComplete)
		{
		using var input = new StringReader (frontPageXml);
		using var reader = XmlReader.Create (input, new () { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
		var document = XDocument.Load (reader);
		var viewports = document.Descendants ("node").Where (node => Is (node, "customdevices_componentRecyclerView")).ToArray ();
		if (viewports.Length != 1 || !editor.GetProperty ("editScheduleEnabled").GetBoolean ())
			throw new InvalidDataException ("Expected one scoped, available editor viewport.");
		var viewport = viewports[0];
		Enabled (viewport);
		var result = new List<ScheduleEditorRenderedValue> ();
		var seen = new HashSet<(int Slot, string Kind)> ();
		foreach (var label in viewport.Descendants ("node").Where (node =>
			Is (node, "statusLabels_description") && Text (node).StartsWith ("TIME ", StringComparison.Ordinal) || Is (node, "customdeviceraiselowerwithtext_label")))
			{
			bool temperature = Is (label, "customdeviceraiselowerwithtext_label");
			var match = Regex.Match (Text (label), temperature ? @"^SETPOINT ([1-8])$" : @"^TIME ([1-8])$");
			if (!match.Success) throw new InvalidDataException ("Unexpected editor slot label.");
			int slot = int.Parse (match.Groups[1].Value, CultureInfo.InvariantCulture);
			string kind = temperature ? "Temperature" : "Time";
			if (!seen.Add ((slot, kind)))
				throw new InvalidDataException ("Duplicate editor slot control.");
			var row = label.Parent ?? throw new InvalidDataException ("Editor label has no row.");
			Enabled (row);
			Enabled (label);
			string property = "editSlot" + slot.ToString (CultureInfo.InvariantCulture);
			if (!editor.GetProperty (property + "Visible").GetBoolean ())
				throw new InvalidDataException ("The app displays a hidden editor slot.");
			var values = row.Elements ("node").Where (node => Is (node, temperature ? "customdeviceraiselowerwithtext_value" : "statusLabels_value")).ToArray ();
			if (values.Length != 1) throw new InvalidDataException ("The labelled editor row has a missing or ambiguous value.");
			Enabled (values[0]);
			bool complete = Inside (row, viewport) && Inside (label, viewport) && Inside (values[0], viewport);
			if (temperature)
				foreach (string id in new[] { "customdeviceraiselowerwithtext_minus", "customdeviceraiselowerwithtext_plus" })
					{
					var buttons = row.Elements ("node").Where (node => Is (node, id)).ToArray ();
					if (buttons.Length != 1) throw new InvalidDataException ("The labelled setpoint row has a missing or ambiguous action.");
					decimal value = editor.GetProperty (property + kind).GetDecimal ();
					bool outwardLimit = id.EndsWith ("_minus", StringComparison.Ordinal) ? value == 5m : value == 30m;
					// A disabled outward action is valid only at its exact endpoint; its row and ancestors must remain enabled.
					Enabled (buttons[0], allowDisabledSelf: outwardLimit);
					if ((string?)buttons[0].Attribute ("enabled") == "true" && (string?)buttons[0].Attribute ("clickable") != "true") throw new InvalidDataException ("A setpoint action is not operable.");
					complete &= Inside (buttons[0], viewport);
					}
			if (!complete) continue;
			string expected = temperature ? editor.GetProperty (property + kind).GetDecimal ().ToString ("0.0", CultureInfo.InvariantCulture) + "°" : editor.GetProperty (property + kind).GetString ()!;
			if (Text (values[0]) != expected) throw new InvalidDataException ("The rendered editor value differs from its labelled slot.");
			result.Add (new (slot, kind, expected));
			}
		if (requireComplete)
			foreach (int slot in Enumerable.Range (1, 8).Where (slot => editor.GetProperty ("editSlot" + slot.ToString (CultureInfo.InvariantCulture) + "Visible").GetBoolean ()))
				foreach (string kind in new[] { "Time", "Temperature" })
					if (!result.Any (value => value.Slot == slot && value.Kind == kind))
						throw new InvalidDataException ("Not every expected editor control was completely visible and checked.");
		return result;
		}
	}