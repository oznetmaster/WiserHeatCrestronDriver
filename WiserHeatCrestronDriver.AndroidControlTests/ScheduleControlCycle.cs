// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

namespace WiserHeatCrestronDriver.ControlProbe;

public enum ScheduleManualTargetRequirement
	{
	Any, EqualToCurrent, DifferentFromCurrent, Absent
	}

public sealed record ScheduleActivity (string Epoch, long Completed, int Pending);
public sealed record ScheduleControlSnapshot (string PhysicalIdentity, ScheduleActivity Activity, JsonElement Room, bool HomeEnabled);
public sealed record ScheduleControlResult (bool Passed, bool RestorationConfirmed, string Detail)
	{
	public bool ManualTargetInitialized { get; init; }
	public bool ExactRestorationConfirmed => RestorationConfirmed && !ManualTargetInitialized;
	public string RequiredManualTargetState { get; init; } = nameof (ScheduleManualTargetRequirement.Any);
	}

public interface IScheduleControlSession
	{
	Task<ScheduleControlSnapshot> ReadAsync (CancellationToken token);
	Task RecordAsync (string phase, ScheduleControlSnapshot snapshot);
	Task SetAsync (bool enabled, bool recovery, CancellationToken token);
	Task SetManualTargetAsync (int target, CancellationToken token);
	}

/// <summary>One observed mode change and one restoration, with no command replay after uncertainty.</summary>
public static class ScheduleControlCycle
	{
	public static async Task<ScheduleControlResult> RunAsync (IScheduleControlSession session, TimeSpan timeout, CancellationToken token,
		 bool allowManualTargetInitialization = false, ScheduleManualTargetRequirement requiredManualTarget = ScheduleManualTargetRequirement.Any)
		{
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes (5))
			throw new ArgumentOutOfRangeException (nameof (timeout));
		if (!Enum.IsDefined (requiredManualTarget))
			throw new ArgumentOutOfRangeException (nameof (requiredManualTarget));
		ScheduleControlSnapshot? original = null;
		bool attempted = false, passed = false, restored = true;
		bool initialized = false;
		string phase = "preflight";
		string detail = "Preflight did not complete; no control was submitted.";
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (timeout);
		try
			{
			original = await session.ReadAsync (deadline.Token);
			ScheduleObservation.ValidateCapture (original.Room, allowManualTargetInitialization);
			RequireIdle (original.Activity);
			if (string.IsNullOrWhiteSpace (original.PhysicalIdentity) || !original.HomeEnabled)
				throw new InvalidDataException ("The hub and Home must agree on the scheduled starting state.");
			await session.RecordAsync ("original", original);
			int? savedTarget = ScheduleObservation.ManualTarget (original.Room);
			int currentTarget = original.Room.GetProperty ("CurrentSetPoint").GetInt32 ();
			bool matches = requiredManualTarget switch
				{
				ScheduleManualTargetRequirement.Any => true,
				ScheduleManualTargetRequirement.EqualToCurrent => savedTarget == currentTarget,
				ScheduleManualTargetRequirement.DifferentFromCurrent => savedTarget.HasValue && savedTarget != currentTarget,
				ScheduleManualTargetRequirement.Absent => !savedTarget.HasValue,
				_ => false
				};
			if (!matches)
				return new (false, true, "Required saved manual target state is " + requiredManualTarget + "; the observed room does not match. No control was sent.")
					{ RequiredManualTargetState = requiredManualTarget.ToString () };
			if (requiredManualTarget != ScheduleManualTargetRequirement.Any)
				await session.RecordAsync ("starting-state-verified-" + requiredManualTarget, original);
			if (ScheduleObservation.ManualTarget (original.Room) == null)
				await session.RecordAsync ("manual-initialization-accepted", original);
			var stable = await session.ReadAsync (deadline.Token);
			RequireStable (stable, original, 0);
			if (!ScheduleObservation.Read (original.Room, stable.Room, allowManualTargetInitialization) || !stable.HomeEnabled)
				throw new InvalidDataException ("Starting state changed before the test.");
			// A schedule boundary may change the active target while preserving Auto policy.
			// Do not claim an equal/different starting-state case after that boundary.
			if (requiredManualTarget != ScheduleManualTargetRequirement.Any && stable.Room.GetProperty ("CurrentSetPoint").GetInt32 () != currentTarget)
				throw new InvalidDataException ("The active target changed before the selected starting-state case.");
			await session.RecordAsync ("disable-intent", stable);
			deadline.Token.ThrowIfCancellationRequested ();
			attempted = true;
			restored = false;
			phase = "disable input or observation";
			await session.SetAsync (false, false, deadline.Token);
			var manual = await WaitForStateAsync (session, original, 1, false, deadline.Token, allowManualTargetInitialization, transition: true);
			await session.RecordAsync ("manual-observed", manual);
			passed = true;
			}
		catch (NotSupportedException) when (!attempted)
			{
			detail = "No saved manual target: this hub has no verified way to remove the value created by switching modes. No control was sent.";
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
					var manual = await WaitForStateAsync (session, original, 1, false, cleanup.Token, allowManualTargetInitialization, transition: true);
					int? target = ScheduleObservation.ManualTarget (original.Room);
					if (target.HasValue && ScheduleObservation.ManualTarget (manual.Room) != target)
						{
						await session.RecordAsync ("manual-target-restore-intent", manual);
						try
							{
							await session.SetManualTargetAsync (target.Value, cleanup.Token);
							}
						catch
							{
							// Observe a possibly delivered write, but never replay it.
							passed = false;
							detail = "The manual-target write response was uncertain; restoration was independently observed. The test remains failed.";
							}
						manual = await WaitForStateAsync (session, original, 1, false, cleanup.Token, allowManualTargetInitialization);
						await session.RecordAsync ("manual-target-restored", manual);
						}
					await session.RecordAsync ("restore-intent", manual);
					await session.SetAsync (true, recovery: !passed, cleanup.Token);
					var after = await WaitForStateAsync (session, original, 2, true, cleanup.Token, allowManualTargetInitialization);
					await session.RecordAsync ("restored", after);
					initialized = !target.HasValue && ScheduleObservation.ManualTarget (after.Room).HasValue;
					restored = true;
					if (passed)
						detail = initialized
							 ? "Auto mode and the original schedule settings are restored. The hub retained an inactive initialized manual target, as explicitly permitted; exact original-state restoration is not claimed."
							 : "The UI changed schedule mode; independent hub and Home observations confirm restoration.";
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
		return new (passed && restored, restored, detail) { ManualTargetInitialized = initialized, RequiredManualTargetState = requiredManualTarget.ToString () };
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
		 ScheduleControlSnapshot original, int completed, bool enabled, CancellationToken token,
		 bool allowManualTargetInitialization, bool transition = false)
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
			int? target = ScheduleObservation.ManualTarget (original.Room);
			bool targetRestored = !target.HasValue || ScheduleObservation.ManualTarget (state.Room) == target;
			matches = ready && ScheduleObservation.ReadTransition (original.Room, state.Room, allowManualTargetInitialization) == enabled &&
				 (transition || targetRestored) && state.HomeEnabled == enabled ? matches + 1 : 0;
			if (matches == 2)
				return state;
			await Task.Delay (250, token);
			}
		}
	}