// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

namespace WiserHeatCrestronDriver.ControlProbe;

/// <summary>Exercise each control with distinct selections and return to its initial value.</summary>
public static class ScheduleChoiceSequence
	{
	public static string[] Create (IEnumerable<string> labels, string initial, bool exhaustive)
		{
		var choices = labels.ToArray ();
		if (choices.Length < 2 || choices.Any (string.IsNullOrWhiteSpace) ||
			choices.Distinct (StringComparer.Ordinal).Count () != choices.Length || !choices.Contains (initial, StringComparer.Ordinal))
			throw new ArgumentException ("Distinct choices must include the starting value and a different value.");
		var selected = exhaustive ? choices : new[] { choices[0], choices[^1] };
		return selected[^1] == initial ? selected : [.. selected, initial];
		}
	}