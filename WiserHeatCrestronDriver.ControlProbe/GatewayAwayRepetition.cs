// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

namespace WiserHeatCrestronDriver.ControlProbe;

/// <summary>Bounded repeated Away cycles with a single original-state contract.</summary>
public static class GatewayAwayRepetition
	{
	public static async Task<GatewayAwayResult> RunAsync (Func<int, IGatewayAwaySession> createSession, int cycles,
		TimeSpan cycleTimeout, CancellationToken token, IGatewayControlObserver? observer = null)
		{
		if (cycles is < 1 or > 3) throw new ArgumentOutOfRangeException (nameof (cycles));
		ArgumentNullException.ThrowIfNull (createSession);
		GatewayAwaySnapshot? original = null;
		for (int cycle = 0; cycle < cycles; cycle++)
			{
			var session = createSession (cycle);
			try
				{
				var before = await session.ReadAsync (token);
				original ??= before;
				RequireOriginal (original, before);
				await session.RecordAsync ("repetition-baseline", new { Cycle = cycle + 1, Cycles = cycles, Original = original, Before = before });
				var result = await GatewayAwayCycle.RunAsync (session, cycleTimeout, token, observer);
				if (!result.Passed || !result.RestorationConfirmed) return result;
				var after = await session.ReadAsync (token);
				RequireOriginal (original, after);
				await session.RecordAsync ("repetition-verified", new { Cycle = cycle + 1, Cycles = cycles, After = after });
				}
			catch (Exception failure)
				{
				try { await session.RecordAsync ("repetition-stopped", new { Cycle = cycle + 1, Exception = failure.ToString () }); } catch { }
				return new (false, false, "Repeated Away testing stopped; inspect the last cycle and original-state evidence before further control.");
				}
			}
		return new (true, true, $"All {cycles} Away cycles completed and the same original guarded state was verified after each cycle.");
		}

	private static void RequireOriginal (GatewayAwaySnapshot original, GatewayAwaySnapshot current)
		{
		GatewayAwayCycle.RequirePreserved (original, current);
		bool initial = GatewayAwayCycle.IsAway (original);
		if (!original.ActionEnabled || original.HomeAway != initial || !current.ActionEnabled ||
			current.HomeAway != initial || GatewayAwayCycle.IsAway (current) != initial)
			throw new InvalidDataException ("The original idle Away state must be restored before another cycle.");
		}
	}