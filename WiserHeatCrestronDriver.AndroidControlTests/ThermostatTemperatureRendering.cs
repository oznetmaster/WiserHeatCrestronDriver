// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;

namespace WiserHeatCrestronDriver.AndroidTests;

internal static class ThermostatTemperatureRendering
	{
	internal sealed record HubTemperatures (int Id, int Current, int Target);
	internal sealed record ProcessorTemperatures (string Units, double Current, double Target, string Label);
	internal sealed record RenderedTemperatures (string Current, string Label, string Target);

	internal static bool Matches (HubTemperatures hub, ProcessorTemperatures processor, RenderedTemperatures? rendered)
		{
		// Exclude unavailable/Off sentinels; those require their own UI checks.
		if (hub.Current is < 0 or > 600 || hub.Target is < 50 or > 300 ||
			processor.Units is not ("Celsius" or "Fahrenheit") || rendered is null)
			return false;
		double current = hub.Current / 10d, target = hub.Target / 10d;
		if (processor.Units == "Fahrenheit")
			{
			current = Math.Round (current * 9d / 5d + 32d, 1);
			target = Math.Round (target * 9d / 5d + 32d, 1);
			}
		string suffix = processor.Units == "Celsius" ? "°C" : "°F";
		string currentText = current.ToString ("0.0", CultureInfo.InvariantCulture);
		string targetText = target.ToString ("0.0", CultureInfo.InvariantCulture);
		return Math.Abs (processor.Current - current) < 0.001 && Math.Abs (processor.Target - target) < 0.001 &&
			processor.Label == currentText + "°" &&
			rendered == new RenderedTemperatures (currentText + suffix, currentText + "°", targetText + suffix);
		}
	}