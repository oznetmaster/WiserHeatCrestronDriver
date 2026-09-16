// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record ScheduleActivity (string Epoch, long Completed, int Pending);
public sealed record ScheduleControlSnapshot (string PhysicalIdentity, ScheduleActivity Activity, JsonElement Room, bool HomeEnabled);
public sealed record ScheduleControlResult (bool Passed, bool RestorationConfirmed, string Detail);

public interface IScheduleControlSession
	{
	Task<ScheduleControlSnapshot> ReadAsync (CancellationToken token);
	Task RecordAsync (string phase, ScheduleControlSnapshot snapshot);
	Task SetAsync (bool enabled, bool recovery, CancellationToken token);
	}

/// <summary>One observed mode change and one restoration, with no command replay after uncertainty.</summary>
public static class ScheduleControlCycle
	{
	public static async Task<ScheduleControlResult> RunAsync (IScheduleControlSession session, TimeSpan timeout, CancellationToken token)
		{
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes (5))
			throw new ArgumentOutOfRangeException (nameof (timeout));
		ScheduleControlSnapshot? original = null;
		bool attempted = false, passed = false, restored = true;
		string phase = "preflight";
		string detail = "Preflight did not complete; no control was submitted.";
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (timeout);
		try
			{
			original = await session.ReadAsync (deadline.Token);
			ScheduleObservation.ValidateCapture (original.Room);
			RequireIdle (original.Activity);
			if (string.IsNullOrWhiteSpace (original.PhysicalIdentity) || !original.HomeEnabled)
				throw new InvalidDataException ("The hub and Home must agree on the scheduled starting state.");
			await session.RecordAsync ("original", original);
			var stable = await session.ReadAsync (deadline.Token);
			RequireStable (stable, original, 0);
			if (!ScheduleObservation.Read (original.Room, stable.Room) || !stable.HomeEnabled)
				throw new InvalidDataException ("Starting state changed before the test.");
			await session.RecordAsync ("disable-intent", stable);
			deadline.Token.ThrowIfCancellationRequested ();
			attempted = true;
			restored = false;
			phase = "disable input or observation";
			await session.SetAsync (false, false, deadline.Token);
			var manual = await WaitForStateAsync (session, original, 1, false, deadline.Token);
			await session.RecordAsync ("manual-observed", manual);
			passed = true;
			}
		catch
			{
			// API/transport exceptions can include private values. Retain only a fixed phase in the public result.
			detail = "Failed during " + phase + ". Inspect private evidence.";
			}
		finally
			{
			if (attempted && original != null)
				{
				using var cleanup = new CancellationTokenSource (timeout);
				try
					{
					// Wait for exactly one completed command even when an input response was lost.
					// No response/unchanged state is not proof that the input was never delivered.
					var manual = await WaitForStateAsync (session, original, 1, false, cleanup.Token);
					await session.RecordAsync ("restore-intent", manual);
					await session.SetAsync (true, recovery: !passed, cleanup.Token);
					var after = await WaitForStateAsync (session, original, 2, true, cleanup.Token);
					await session.RecordAsync ("restored", after);
					restored = true;
					if (passed)
						detail = "The UI changed schedule mode; independent hub and Home observations confirm restoration.";
					}
				catch
					{
					passed = false;
					detail = "Restoration unconfirmed. Keep the processor reservation and reconcile private control evidence.";
					// The previously flushed original and intent remain authoritative if this write fails.
					try
						{
						await session.RecordAsync ("recovery-required", original);
						}
					catch { }
					}
				}
			}
		return new (passed && restored, restored, detail);
		}

	private static void RequireIdle (ScheduleActivity activity)
		{
		if (!Guid.TryParseExact (activity.Epoch, "N", out _) || activity.Completed < 0 || activity.Completed > long.MaxValue - 2 || activity.Pending != 0)
			throw new InvalidDataException ("Driver activity is not valid and idle.");
		}

	private static void RequireStable (ScheduleControlSnapshot state, ScheduleControlSnapshot original, int completed)
		{
		RequireIdle (state.Activity);
		if (state.PhysicalIdentity != original.PhysicalIdentity || state.Activity.Epoch != original.Activity.Epoch ||
			 state.Activity.Completed != checked(original.Activity.Completed + completed))
			throw new InvalidDataException ("Driver identity changed or commands cannot be attributed to this test.");
		}

	private static async Task<ScheduleControlSnapshot> WaitForStateAsync (IScheduleControlSession session,
		 ScheduleControlSnapshot original, int completed, bool enabled, CancellationToken token)
		{
		int matches = 0;
		while (true)
			{
			token.ThrowIfCancellationRequested ();
			var state = await session.ReadAsync (token);
			long expected = checked(original.Activity.Completed + completed);
			if (state.PhysicalIdentity != original.PhysicalIdentity || state.Activity.Epoch != original.Activity.Epoch ||
				 state.Activity.Pending < 0 || state.Activity.Completed < original.Activity.Completed || state.Activity.Completed > expected)
				throw new InvalidDataException ("Driver restarted, identity changed or concurrent commands prevent attribution.");
			bool ready = state.Activity.Pending == 0 && state.Activity.Completed == expected;
			matches = ready && ScheduleObservation.Read (original.Room, state.Room) == enabled && state.HomeEnabled == enabled ? matches + 1 : 0;
			if (matches == 2)
				return state;
			await Task.Delay (250, token);
			}
		}
	}