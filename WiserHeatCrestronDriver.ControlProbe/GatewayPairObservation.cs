// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record GatewayInstanceObservation (string ProcessorIdentity, int DeviceId, int LocationId, string Name,
	string Version, string PackageSha256, string ConfigurationSha256, string HubBindingSha256,
	Guid Lifetime, DateTimeOffset RefreshUtc, bool Away, bool HotWater);

/// <summary>Observed two-processor identity and convergence checks, not proof of control isolation.</summary>
public static class GatewayPairObservation
	{
	private static bool Hash (string value) => value?.Length == 64 && value.All (char.IsAsciiHexDigit);
	private static void RequireValid (GatewayInstanceObservation value)
		{
		if (!Hash (value.ProcessorIdentity) || value.DeviceId <= 0 || value.LocationId <= 0 || string.IsNullOrWhiteSpace (value.Name) ||
			!System.Version.TryParse (value.Version, out var version) || version.Revision < 0 || !Hash (value.PackageSha256) ||
			!Hash (value.ConfigurationSha256) || !Hash (value.HubBindingSha256) || value.Lifetime == Guid.Empty || value.RefreshUtc == default)
			throw new InvalidDataException ("Complete pinned instance, configuration and refresh identities are required.");
		}
	public static void RequirePair (GatewayInstanceObservation first, GatewayInstanceObservation second)
		{
		RequireValid (first);
		RequireValid (second);
		if (first.ProcessorIdentity.Equals (second.ProcessorIdentity, StringComparison.OrdinalIgnoreCase) || first.Lifetime == second.Lifetime ||
			first.Version != second.Version || !first.PackageSha256.Equals (second.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
			!first.HubBindingSha256.Equals (second.HubBindingSha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException ("Require two distinct processor/driver lifetimes running the same candidate against the same configured hub.");
		}
	public static void RequirePreserved (GatewayInstanceObservation original, GatewayInstanceObservation current)
		{
		RequireValid (original);
		RequireValid (current);
		// Different processors may use the same numeric device ID, and their local settings may differ.
		// Each must retain its own identity and settings; shared live state may legitimately change.
		if ((current with { RefreshUtc = original.RefreshUtc, Away = original.Away, HotWater = original.HotWater }) != original ||
			current.RefreshUtc < original.RefreshUtc)
			throw new InvalidDataException ("A driver identity, local configuration or refresh sequence changed.");
		}
	public static bool HasFreshSharedState (GatewayInstanceObservation firstOriginal, GatewayInstanceObservation secondOriginal,
		GatewayInstanceObservation first, GatewayInstanceObservation second, bool hubAway, bool hubHotWater)
		{
		RequirePair (firstOriginal, secondOriginal);
		RequirePair (first, second);
		RequirePreserved (firstOriginal, first);
		RequirePreserved (secondOriginal, second);
		return first.RefreshUtc > firstOriginal.RefreshUtc && second.RefreshUtc > secondOriginal.RefreshUtc &&
			first.Away == hubAway && second.Away == hubAway && first.HotWater == hubHotWater && second.HotWater == hubHotWater;
		}
	}