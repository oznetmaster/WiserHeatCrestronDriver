// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

namespace WiserHeatCrestronDriver.ControlProbe;

/// <summary>Repeated observed cycles, preserving one original room policy throughout.</summary>
public static class ScheduleControlRepetition
	{
	public static async Task<ScheduleControlResult> RunAsync (Func<int, IScheduleControlSession> createSession, int cycles,
		TimeSpan cycleTimeout, CancellationToken token)
		{
		ArgumentNullException.ThrowIfNull (createSession);
		if (cycles is < 1 or > 3)
			throw new ArgumentOutOfRangeException (nameof (cycles));
		if (cycleTimeout <= TimeSpan.Zero || cycleTimeout > TimeSpan.FromMinutes (5))
			throw new ArgumentOutOfRangeException (nameof (cycleTimeout));
		ScheduleControlSnapshot? original = null;
		for (int cycle = 0; cycle < cycles; cycle++)
			{
			token.ThrowIfCancellationRequested ();
			var session = createSession (cycle);
			try
				{
				using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
				deadline.CancelAfter (cycleTimeout + cycleTimeout + TimeSpan.FromSeconds (15));
				var before = await session.ReadAsync (deadline.Token);
				original ??= before;
				RequireOriginal (original, before, cycle * 2);
				await session.RecordAsync ("repetition-baseline", before);
				var result = await ScheduleControlCycle.RunAsync (session, cycleTimeout, deadline.Token);
				if (!result.Passed || !result.ExactRestorationConfirmed)
					return result;
				var after = await session.ReadAsync (deadline.Token);
				RequireOriginal (original, after, (cycle + 1) * 2);
				await session.RecordAsync ("repetition-verified", after);
				}
			catch
				{
				return new (false, false, "Repeated mode testing stopped. Inspect the retained cycle and original room policy before further control.");
				}
			}
		return new (true, true, $"All {cycles} mode cycles completed with the same original room policy restored after each cycle.");
		}

	private static void RequireOriginal (ScheduleControlSnapshot original, ScheduleControlSnapshot current, int completed)
		{
		ScheduleObservation.ValidateCapture (original.Room);
		if (!original.HomeEnabled || !current.HomeEnabled || original.Activity.Pending != 0 || current.Activity.Pending != 0 ||
			current.PhysicalIdentity != original.PhysicalIdentity || current.Activity.Epoch != original.Activity.Epoch ||
			current.Activity.Completed != checked(original.Activity.Completed + completed) || !ScheduleObservation.Read (original.Room, current.Room))
			throw new InvalidDataException ("The original idle room policy and command attribution must be preserved between cycles.");
		}
	}