// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

namespace WiserHeatCrestronDriver.ControlProbe;

public enum ScheduleChoiceSearchAction { ScrollUp, ScrollDown, Select }

/// <summary>Find one exact choice in an ordered, virtualized selector without repeating a failed input.</summary>
public sealed class ScheduleChoiceSearch
	{
	private readonly string[] _labels;
	private readonly string _selected;
	private readonly string _desired;
	private ScheduleChoiceValue[]? _previous;
	private int _observations;
	private bool _complete;

	public ScheduleChoiceSearch (IEnumerable<string> labels, string selected, string desired)
		{
		_labels = labels.ToArray ();
		if (_labels.Length == 0 || _labels.Any (string.IsNullOrWhiteSpace) || _labels.Distinct (StringComparer.Ordinal).Count () != _labels.Length ||
			!_labels.Contains (selected, StringComparer.Ordinal) || !_labels.Contains (desired, StringComparer.Ordinal))
			throw new ArgumentException ("Distinct ordered choices must include both original and desired values.");
		_selected = selected;
		_desired = desired;
		}

	public ScheduleChoiceSearchAction Observe (IReadOnlyList<ScheduleChoiceValue> visible)
		{
		if (_complete || ++_observations > 128) throw new InvalidOperationException ("Choice search is complete or exhausted.");
		if (visible.Count == 0 || visible.Select (v => v.Label).Distinct (StringComparer.Ordinal).Count () != visible.Count ||
			visible.Any (v => !_labels.Contains (v.Label, StringComparer.Ordinal) || v.Selected != (v.Label == _selected)))
			throw new InvalidDataException ("The selector's labels or selected value changed.");
		var indexes = visible.Select (v => Array.IndexOf (_labels, v.Label)).ToArray ();
		if (!indexes.SequenceEqual (Enumerable.Range (indexes[0], indexes.Length)))
			throw new InvalidDataException ("The visible selector order is incomplete or unexpected.");
		if (visible.Any (v => v.Label == _desired)) { _complete = true; return ScheduleChoiceSearchAction.Select; }
		if (_previous != null && _previous.SequenceEqual (visible))
			throw new InvalidDataException ("The selector did not move toward the desired choice.");
		_previous = visible.ToArray ();
		return Array.IndexOf (_labels, _desired) < indexes[0] ? ScheduleChoiceSearchAction.ScrollUp : ScheduleChoiceSearchAction.ScrollDown;
		}
	}