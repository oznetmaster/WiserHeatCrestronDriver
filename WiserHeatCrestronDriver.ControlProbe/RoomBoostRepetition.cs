// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

namespace WiserHeatCrestronDriver.ControlProbe;

/// <summary>Observed Boost cycles against one original physical policy, never automatic input retries.</summary>
public static class RoomBoostRepetition
	{
	public static async Task<RoomTemperatureResult> RunAsync (Func<int, IRoomTemperatureSession> createSession, int cycles,
		TimeSpan cycleTimeout, CancellationToken token)
		{
		ArgumentNullException.ThrowIfNull (createSession);
		if (cycles is < 1 or > 3) throw new ArgumentOutOfRangeException (nameof (cycles));
		if (cycleTimeout <= TimeSpan.Zero || cycleTimeout > TimeSpan.FromMinutes (3))
			throw new ArgumentOutOfRangeException (nameof (cycleTimeout));
		RoomTemperatureSnapshot? original = null;
		for (int cycle = 0; cycle < cycles; cycle++)
			{
			token.ThrowIfCancellationRequested ();
			var session = createSession (cycle);
			try
				{
				using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
				deadline.CancelAfter (cycleTimeout * 2 + TimeSpan.FromSeconds (15));
				var before = await session.ReadAsync (deadline.Token);
				original ??= before;
				RequireOriginal (original, before, cycle * 2);
				await session.RecordAsync ("repetition-baseline", new { Cycle = cycle + 1, Cycles = cycles, Original = original, Before = before });
				// Recheck every pre-input read. A changed second capture must not become a new baseline.
				var guarded = new GuardedSession (session, original, cycle * 2);
				var result = await RoomTemperatureCycle.RunAsync (guarded, true, cycleTimeout, deadline.Token);
				if (guarded.BaselineRejected) return new (false, false, "The original policy changed before a repeated Boost input; no further input was sent.");
				if (!result.Passed || !result.RestorationConfirmed) return result;
				var after = await session.ReadAsync (deadline.Token);
				RequireOriginal (original, after, (cycle + 1) * 2);
				await session.RecordAsync ("repetition-verified", new { Cycle = cycle + 1, Cycles = cycles, After = after });
				}
			catch (Exception failure)
				{
				try { await session.RecordAsync ("repetition-stopped", new { Cycle = cycle + 1, Exception = failure.ToString () }); } catch { }
				return new (false, false, "Repeated Boost testing stopped; reconcile the retained original policy and last cycle before further control.");
				}
			}
		return new (true, true, $"All {cycles} observed Boost on/off cycles restored the same original room policy and guarded household settings.");
		}

	private static void RequireOriginal (RoomTemperatureSnapshot original, RoomTemperatureSnapshot current, int completed)
		{
		var room = RoomTemperatureRestoration.Room (original.Gateway.Hub, original.RoomId);
		var plan = RoomTemperatureRestoration.Capture (room);
		RoomTemperatureRestoration.RequireGuarded (plan, original.Gateway, current.Gateway);
		var observed = RoomTemperatureRestoration.Room (current.Gateway.Hub, current.RoomId);
		RoomTemperatureRestoration.RequireRestored (plan, observed);
		if (original.RoomId != current.RoomId || original.TemperatureUnits != current.TemperatureUnits || original.BoostSettings != current.BoostSettings ||
			original.HomeTarget != current.HomeTarget || original.HomeBoost || current.HomeBoost || !original.UiMatches || !current.UiMatches ||
			original.Activity.Pending != 0 || current.Activity.Pending != 0 || original.Activity.Epoch != current.Activity.Epoch ||
			current.Activity.Completed != checked(original.Activity.Completed + completed) || current.Gateway.RefreshUtc < original.Gateway.RefreshUtc ||
			room.GetProperty ("CurrentSetPoint").GetInt32 () != observed.GetProperty ("CurrentSetPoint").GetInt32 () ||
			RoomTemperatureRestoration.Origin (room) != RoomTemperatureRestoration.Origin (observed))
			throw new InvalidDataException ("Original Boost settings, idle room state and command attribution must remain unchanged between cycles.");
		}

	private sealed class GuardedSession (IRoomTemperatureSession inner, RoomTemperatureSnapshot original, int completed) : IRoomTemperatureSession
		{
		private bool _attempted;
		public bool BaselineRejected { get; private set; }
		public async Task<RoomTemperatureSnapshot> ReadAsync (CancellationToken token)
			{
			var value = await inner.ReadAsync (token);
			if (!_attempted)
				{
				try { RequireOriginal (original, value, completed); }
				catch { BaselineRejected = true; throw; }
				}
			return value;
			}
		public Task<RoomTemperatureSnapshot> ReadRestorationAsync (CancellationToken token) => inner.ReadRestorationAsync (token);
		public Task RecordAsync (string phase, object value) => inner.RecordAsync (phase, value);
		public Task InputAsync (RoomTemperatureAction action, CancellationToken token)
			{
			_attempted = true;
			return inner.InputAsync (action, token);
			}
		public Task RestoreAsync (int index, RoomTemperatureRestorePlan plan, JsonElement request, RoomTemperatureSnapshot expected, CancellationToken token) =>
			inner.RestoreAsync (index, plan, request, expected, token);
		}
	}