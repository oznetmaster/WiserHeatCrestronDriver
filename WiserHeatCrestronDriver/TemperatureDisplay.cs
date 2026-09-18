// Copyright © 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

using System;

namespace WiserHeat.CrestronDriver;

internal static class TemperatureDisplay
	{
	internal const double MinimumCelsius = 5;
	internal const double MaximumCelsius = 30;
	internal const double StepCelsius = 0.5;

	internal static bool IsFahrenheit (string? units) => string.Equals (units, "Fahrenheit", StringComparison.OrdinalIgnoreCase);
	internal static string NormalizeUnits (string? units) => IsFahrenheit (units) ? "Fahrenheit" : "Celsius";
	internal static double FromCelsius (double value, string? units) => IsFahrenheit (units) ? Math.Round (value * 9 / 5 + 32, 1) : value;
	internal static double Step (string? units) => IsFahrenheit (units) ? 0.9 : StepCelsius;

	internal static bool TrySetpoint (double displayed, string? units, out double celsius)
		{
		celsius = IsFahrenheit (units) ? (displayed - 32) * 5 / 9 : displayed;
		if (double.IsNaN (celsius) || double.IsInfinity (celsius) || celsius < MinimumCelsius || celsius > MaximumCelsius)
			return false;
		celsius = Math.Round (celsius / StepCelsius, MidpointRounding.AwayFromZero) * StepCelsius;
		return true;
		}
	}