// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;

namespace WiserHeatCrestronDriver.ControlProbe;

/// <summary>Count actual successful hub-read markers within one installed driver lifetime.</summary>
public sealed class SuccessfulRefreshSequence
	{
	private readonly string _lifetime;
	private DateTimeOffset _last;
	public int Advances { get; private set; }
	public SuccessfulRefreshSequence (string lifetime, string taggedUtc)
		{
		if (!Guid.TryParseExact (lifetime, "N", out var id) || id == Guid.Empty)
			throw new InvalidDataException ("A valid driver lifetime is required.");
		_lifetime = lifetime;
		_last = ParseTimestamp (taggedUtc);
		}
	public bool Observe (string lifetime, string taggedUtc)
		{
		if (lifetime != _lifetime) throw new InvalidDataException ("The driver lifetime changed.");
		var current = ParseTimestamp (taggedUtc);
		if (current < _last) throw new InvalidDataException ("The successful refresh marker moved backwards.");
		if (current == _last) return false;
		_last = current;
		Advances++;
		return true;
		}
	public static DateTimeOffset ParseTimestamp (string value)
		{
		if (value == null || !value.StartsWith ("utc:", StringComparison.Ordinal) ||
			!DateTimeOffset.TryParseExact (value[4..], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) ||
			result.Offset != TimeSpan.Zero || result == DateTimeOffset.MinValue)
			throw new InvalidDataException ("A precise tagged UTC successful-refresh marker is required.");
		return result;
		}
	}