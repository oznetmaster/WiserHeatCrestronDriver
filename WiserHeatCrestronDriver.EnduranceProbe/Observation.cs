// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

namespace WiserHeatCrestronDriver.EnduranceProbe;

internal sealed record HubState (string Uuid, string Model, int HotWaterId, bool HotWaterOn, bool Away);
internal sealed record DriverState (int Id, int? Parent, string? Name, string? Model, int? Location,
	string Version, string HubHost, bool Loaded, bool Ready, bool Online, bool HotWaterVisible, bool AwayVisible, bool HotWaterOn, bool Away,
	DateTimeOffset LastHubRefreshUtc, DateTimeOffset ObservedUtc, string LifetimeId);

internal interface IObservationSource : IAsyncDisposable
	{
	Task VerifyOwnerAsync (CancellationToken token);
	Task<ProcessorUptimeSnapshot> ReadUptimeAsync (CancellationToken token);
	Task<IReadOnlyDictionary<string, string>> ReadPayloadAsync (CancellationToken token);
	Task<DriverState> ReadDriverAsync (CancellationToken token);
	Task<HubState> ReadHubAsync (CancellationToken token);
	}

internal static class Observation
	{
	internal static async Task<SubmissionEnduranceProbeResult> RunAsync (Binding binding, SubmissionEndurancePlan plan,
		IReadOnlyDictionary<string, string> expectedFiles, IObservationSource source, CancellationToken token)
		{
		var evidence = new Dictionary<string, object> ();
		string refusal = "";
		async Task<T> Read<T> (string name, Func<CancellationToken, Task<T>> read)
			{
			await source.VerifyOwnerAsync (token);
			token.ThrowIfCancellationRequested ();
			var result = await read (token);
			await source.VerifyOwnerAsync (token);
			evidence.Add (name, result!);
			return result;
			}
		try
			{
			var first = await Read ("UptimeBefore", source.ReadUptimeAsync);
			CheckBoot (binding, first);
			CheckPayload (expectedFiles, await Read ("PayloadBefore", source.ReadPayloadAsync));
			var hubBefore = await Read ("HubBefore", source.ReadHubAsync);
			CheckHub (binding, hubBefore);
			var driverBefore = await Read ("DriverBefore", source.ReadDriverAsync);
			CheckDriver (binding, driverBefore);
			var hubAfter = await Read ("HubAfter", source.ReadHubAsync);
			CheckHub (binding, hubAfter);
			var driverAfter = await Read ("DriverAfter", source.ReadDriverAsync);
			CheckDriver (binding, driverAfter);
			if (hubBefore != hubAfter) throw new Refusal ("hub-state-changed-during-observation");
			if (driverBefore with { LastHubRefreshUtc = driverAfter.LastHubRefreshUtc, ObservedUtc = driverAfter.ObservedUtc } != driverAfter ||
				driverAfter.LastHubRefreshUtc < driverBefore.LastHubRefreshUtc || driverAfter.ObservedUtc < driverBefore.ObservedUtc ||
				hubAfter.HotWaterOn != driverAfter.HotWaterOn || hubAfter.Away != driverAfter.Away)
				throw new Refusal ("driver-state-did-not-match-hub");
			CheckPayload (expectedFiles, await Read ("PayloadAfter", source.ReadPayloadAsync));
			var last = await Read ("UptimeAfter", source.ReadUptimeAsync);
			CheckBoot (binding, last);
			if (last.RequestSentUtc < first.ObservedUtc || last.Uptime <= first.Uptime)
				throw new Refusal ("clock-or-uptime-did-not-advance");
			await source.VerifyOwnerAsync (token);
			}
		catch (Refusal error) { refusal = error.Code; }
		evidence.Add ("Refusal", refusal);
		evidence.Add ("ApprovedBootWindow", binding.Boot);
		evidence.Add ("ReadOnly", true);
		return new (plan.Identity, plan.ProcessorIdentity, plan.InstallationIdentity, plan.ReservationId, plan.ProducerId,
			binding.BootIdentity, refusal.Length == 0 ? SubmissionEvidenceOutcome.Passed : SubmissionEvidenceOutcome.Failed,
			JsonSerializer.SerializeToUtf8Bytes (evidence, Input.Json));
		}

	internal static void CheckBoot (Binding binding, ProcessorUptimeSnapshot value)
		{
		if (value.Uptime < TimeSpan.Zero || value.ObservedUtc < value.RequestSentUtc ||
			value.ObservedUtc - value.RequestSentUtc > TimeSpan.FromSeconds (20) ||
			value.EarliestStartUtc > binding.Boot.LatestUtc + binding.Boot.ClockTolerance ||
			value.LatestStartUtc < binding.Boot.EarliestUtc - binding.Boot.ClockTolerance)
			throw new Refusal ("boot-window-or-clock-mismatch");
		}
	internal static void CheckPayload (IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> observed)
		{
		if (expected.Count == 0 || observed.Count != expected.Count || expected.Any (p => !observed.TryGetValue (p.Key, out var hash) || !string.Equals (hash, p.Value, StringComparison.OrdinalIgnoreCase)))
			throw new Refusal ("active-payload-differs-from-candidate");
		}
	internal static void CheckDriver (Binding binding, DriverState value)
		{
		if (value.Id != binding.DriverId || value.Parent != -6 || value.Name != binding.DriverName || value.Model != Binding.Model ||
			value.Location != binding.LocationId || value.Version != binding.DriverVersion || value.LifetimeId != binding.DriverLifetimeId || !string.Equals (value.HubHost.Trim (), binding.HubHost, StringComparison.OrdinalIgnoreCase) || !value.Loaded || !value.Ready || !value.Online ||
			!value.HotWaterVisible || !value.AwayVisible)
			throw new Refusal ("driver-identity-or-capability-mismatch");
		if (value.LastHubRefreshUtc == default || value.LastHubRefreshUtc.Offset != TimeSpan.Zero ||
			value.ObservedUtc - value.LastHubRefreshUtc > binding.MaximumHubRefreshAge ||
			value.LastHubRefreshUtc > value.ObservedUtc + binding.Boot.ClockTolerance)
			throw new Refusal ("driver-refresh-stale-or-clock-mismatch");
		}
	internal static void CheckHub (Binding binding, HubState value)
		{
		if (value.Uuid != binding.HubUuid || value.Model != binding.HubModel || value.HotWaterId != binding.HotWaterId)
			throw new Refusal ("hub-identity-mismatch");
		}
	internal sealed class Refusal (string code) : Exception
		{
		internal string Code { get; } = code;
		}
	}