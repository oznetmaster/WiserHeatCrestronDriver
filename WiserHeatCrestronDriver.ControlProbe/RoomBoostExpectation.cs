// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record RoomBoostSettings (double DeltaCelsius, int DurationMinutes);

/// <summary>Independent expected physical result of the configured room Boost.</summary>
public sealed record RoomBoostExpectation (int TargetTenthsCelsius, int DurationMinutes)
	{
	public static RoomBoostExpectation Create (RoomTemperatureSnapshot original)
		{
		var settings = original.BoostSettings ?? throw new InvalidDataException ("Read the configured Boost increase and duration before input.");
		if (!double.IsFinite (settings.DeltaCelsius) || settings.DeltaCelsius is < 1 or > 5 || settings.DurationMinutes is < 5 or > 1440)
			throw new InvalidDataException ("Boost settings are outside the declared driver ranges.");
		var room = RoomTemperatureRestoration.Room (original.Gateway.Hub, original.RoomId);
		int ambient = room.GetProperty ("CalculatedTemperature").GetInt32 ();
		int step = room.GetProperty ("ClimateCapabilities").GetProperty ("SetpointStep").GetInt32 ();
		if (ambient is < -500 or > 1000 || step is <= 0 or > 10)
			throw new InvalidDataException ("A valid ambient temperature and declared setpoint resolution are required for Boost.");
		// HubR applies Boost above ambient temperature, not above a lower scheduled
		// target. Its returned setpoint is quantized to the room's declared step.
		int boostedAmbient = checked ((int)Math.Round ((ambient + settings.DeltaCelsius * 10) / step, MidpointRounding.AwayFromZero) * step);
		int target = Math.Max (room.GetProperty ("CurrentSetPoint").GetInt32 (), boostedAmbient);
		int maximum = room.TryGetProperty ("ClimateCapabilities", out var capabilities) && capabilities.TryGetProperty ("MaximumHeatSetpoint", out var limit)
			? limit.GetInt32 () : 300;
		if (target > maximum || target > 300)
			throw new InvalidDataException ("The configured Boost needs room below the physical limit; endpoint behavior requires a separate case.");
		return new (target, settings.DurationMinutes);
		}

	public bool Matches (RoomTemperatureSnapshot observed, DateTimeOffset inputStartedUtc, DateTimeOffset inputReturnedUtc)
		{
		if (inputStartedUtc == default || inputReturnedUtc < inputStartedUtc) return false;
		var room = RoomTemperatureRestoration.Room (observed.Gateway.Hub, observed.RoomId);
		// The tested HubR accepts Type=Boost in the command, but reports the active
		// override as Manual with FromBoost origin. Request and state enums differ.
		if (room.GetProperty ("CurrentSetPoint").GetInt32 () != TargetTenthsCelsius ||
			RoomTemperatureRestoration.Origin (room) != "FromBoost" ||
			!room.TryGetProperty ("OverrideType", out var kind) || kind.ValueKind != System.Text.Json.JsonValueKind.String || kind.GetString () != "Manual" ||
			!room.TryGetProperty ("OverrideTimeoutUnixTime", out var expiry) || !expiry.TryGetInt64 (out long seconds)) return false;
		// Include the observed input interval and one minute of clock/timestamp granularity
		// tolerance; this is not a precise response-time check.
		long earliest = inputStartedUtc.AddMinutes (DurationMinutes - 1).ToUnixTimeSeconds ();
		long latest = inputReturnedUtc.AddMinutes (DurationMinutes + 1).ToUnixTimeSeconds ();
		return seconds >= earliest && seconds <= latest;
		}
	}