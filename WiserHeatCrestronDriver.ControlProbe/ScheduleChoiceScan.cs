// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record ScheduleChoiceValue (string Label, bool Selected);
public enum ScheduleChoiceScanAction { ScrollUp, ScrollDown, Complete }

/// <summary>Establish the top before traversing a list whose preserved scroll anchor may follow a renamed item.</summary>
public sealed class ScheduleChoiceScan
	{
	private readonly HashSet<string> _expected;
	private readonly string _selected;
	private readonly HashSet<string> _observed = new (StringComparer.Ordinal);
	private ScheduleChoiceValue[]? _previous;
	private bool _top, _complete;
	private int _samples;

	public ScheduleChoiceScan (IEnumerable<string> labels, string selected)
		{
		var values = labels.ToArray ();
		_expected = new (values, StringComparer.Ordinal);
		if (values.Length == 0 || values.Any (string.IsNullOrWhiteSpace) || values.Length != _expected.Count || !_expected.Contains (selected))
			throw new ArgumentException ("Distinct expected labels and an included selected label are required.");
		_selected = selected;
		}

	public ScheduleChoiceScanAction Observe (IReadOnlyList<ScheduleChoiceValue> visible)
		{
		if (_complete || ++_samples > 128)
			throw new InvalidOperationException ("The bounded selection scan is complete or exhausted.");
		if (visible.Count == 0 || visible.Select (v => v.Label).Distinct (StringComparer.Ordinal).Count () != visible.Count ||
			visible.Any (v => !_expected.Contains (v.Label) || v.Selected != (v.Label == _selected)))
			throw new InvalidDataException ("The visible options or selection differ from the expected list.");
		bool stationary = _previous != null && _previous.SequenceEqual (visible);
		_previous = visible.ToArray ();
		if (!_top)
			{
			if (!stationary) return ScheduleChoiceScanAction.ScrollUp;
			_top = true;
			_previous = null;
			_observed.Clear ();
			}
		else if (stationary)
			{
			if (!_observed.SetEquals (_expected))
				throw new InvalidDataException ("Both list ends were reached but not every expected option was observed.");
			_complete = true;
			return ScheduleChoiceScanAction.Complete;
			}
		foreach (var value in visible) _observed.Add (value.Label);
		return ScheduleChoiceScanAction.ScrollDown;
		}
	}