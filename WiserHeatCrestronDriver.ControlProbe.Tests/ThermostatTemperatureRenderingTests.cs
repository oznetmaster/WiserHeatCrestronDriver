// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;

using NUnit.Framework;

using WiserHeatCrestronDriver.AndroidTests;

using static WiserHeatCrestronDriver.AndroidTests.ThermostatTemperatureRendering;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class ThermostatTemperatureRenderingTests
	{
	[TestCase ("Celsius", 21.1, 19.5, "21.1°C", "21.1°", "19.5°C")]
	[TestCase ("Fahrenheit", 70.0, 67.1, "70.0°F", "70.0°", "67.1°F")]
	public void ComparesIndependentHubUnitsWithProcessorAndRenderedFeedback (string units, double current, double target, string shownCurrent, string label, string shownTarget)
		{
		var saved = CultureInfo.CurrentCulture;
		try
			{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo ("de-DE");
			Assert.That (ThermostatTemperatureRendering.Matches (new (9, 211, 195),
				new (units, current, target, label), new (shownCurrent, label, shownTarget)), Is.True);
			}
		finally { CultureInfo.CurrentCulture = saved; }
		}

	[TestCase ("units")]
	[TestCase ("current value")]
	[TestCase ("target value")]
	[TestCase ("driver label")]
	[TestCase ("rendered current")]
	[TestCase ("rendered label")]
	[TestCase ("rendered target")]
	[TestCase ("missing rendering")]
	[TestCase ("nonfinite current")]
	[TestCase ("nonfinite target")]
	public void RejectsMismatchedOrMissingFeedback (string field)
		{
		var processor = new ProcessorTemperatures ("Fahrenheit", 70.0, 67.1, "70.0°");
		RenderedTemperatures? rendered = new ("70.0°F", "70.0°", "67.1°F");
		switch (field)
			{
			case "units": processor = processor with { Units = "Unknown" }; break;
			case "current value": processor = processor with { Current = 21.1 }; break;
			case "target value": processor = processor with { Target = 19.5 }; break;
			case "driver label": processor = processor with { Label = "21.1°" }; break;
			case "rendered current": rendered = rendered with { Current = "21.1°C" }; break;
			case "rendered label": rendered = rendered with { Label = "21.1°" }; break;
			case "rendered target": rendered = rendered with { Target = "67.1°C" }; break;
			case "missing rendering": rendered = null; break;
			case "nonfinite current": processor = processor with { Current = double.NaN }; break;
			case "nonfinite target": processor = processor with { Target = double.PositiveInfinity }; break;
			}
		Assert.That (ThermostatTemperatureRendering.Matches (new (9, 211, 195), processor, rendered), Is.False);
		}

	[TestCase (-32768, 195)]
	[TestCase (601, 195)]
	[TestCase (211, -20)]
	[TestCase (211, 301)]
	public void UnavailableOrOffReadingsCannotPassOrdinaryTemperatureInspection (int current, int target)
		{
		Assert.That (ThermostatTemperatureRendering.Matches (new (9, current, target),
			new ("Celsius", 21.1, 19.5, "21.1°"), new ("21.1°C", "21.1°", "19.5°C")), Is.False);
		}
	}