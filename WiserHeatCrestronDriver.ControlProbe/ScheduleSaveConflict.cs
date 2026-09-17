// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record ScheduleConflictCase (int ScheduleId, string Day, bool AllDays, int OriginalTemperature,
	int PendingTemperature, JsonElement Before, JsonElement External, JsonElement IncorrectOverwrite);

public interface IScheduleConflictSession : IScheduleSaveIsolationSession
	{
	Task ExerciseConflictAsync (ScheduleConflictCase operation, Func<CancellationToken, Task> changeHub, CancellationToken token);
	}

public static partial class ScheduleSaveIsolation
	{
	/// <summary>Challenge an open editor with one independent schedule change, refuse a stale save, and restore the original.</summary>
	public static async Task<ScheduleSaveIsolationResult> RunConflictAsync (IScheduleConflictSession session, int roomId,
		bool allDays, TimeSpan observationTimeout, CancellationToken token)
		{
		if (roomId <= 0 || observationTimeout <= TimeSpan.Zero || observationTimeout > TimeSpan.FromMinutes (2))
			throw new ArgumentException ("A room and bounded observation timeout are required.");
		ScheduleHubSnapshot? original = null;
		ScheduleConflictCase? operation = null;
		bool started = false, mutationStarted = false, passed = false, restored = true;
		string detail = "Conflict preflight failed before input.";
		try
			{
			original = await session.ReadAsync (token);
			var room = Room (original, roomId);
			ScheduleObservation.ValidateCapture (room, allowManualTargetInitialization: true);
			int id = room.GetProperty ("ScheduleId").GetInt32 ();
			if (original.Domain.GetProperty ("Room").EnumerateArray ().Count (r => r.TryGetProperty ("ScheduleId", out var value) && value.GetInt32 () == id) != 1)
				throw new InvalidDataException ("A conflict test requires an existing exclusively assigned schedule.");
			var source = Heating (original.Schedules).Single (s => Id (s) == id);
			ValidateDays (source);
			int before = source.GetProperty ("Monday").GetProperty ("DegreesC")[0].GetInt32 ();
			int step = before <= 290 ? 5 : -5;
			operation = new (id, "Monday", allDays, before, before + step, source,
				Edit (source, "Monday", before + 2 * step, false), Edit (source, "Monday", before + step, allDays));
			RequireOriginal (original, await session.ReadAsync (token));
			await session.RecordAsync ("original", new { RoomId = roomId, ExistingExclusiveScheduleId = id, Snapshot = original });
			await session.RecordAsync ("existing-schedule-intent", operation);
			started = true;
			restored = false;
			async Task ChangeHub (CancellationToken cancellation)
				{
				if (mutationStarted) throw new InvalidOperationException ("The independent change must never be replayed.");
				RequireOriginal (original, await session.ReadAsync (cancellation));
				await session.RecordAsync ("external-change-intent", operation);
				mutationStarted = true;
				await session.SendAsync ("PATCH", "Heating/" + id, Days (operation.External), cancellation);
				await WaitAsync (session, current =>
					{
					var observed = Heating (current.Schedules).Single (s => Id (s) == id);
					if (!Equal (observed, source) && !Equal (observed, operation.External))
						throw new InvalidDataException ("Unexpected schedule contents during the independent change.");
					RequireExisting (original, current, id, observed);
					return Equal (observed, operation.External);
					}, observationTimeout, cancellation);
				var snapshot = await session.ReadAsync (cancellation);
				RequireExisting (original, snapshot, id, operation.External);
				await session.RecordAsync ("external-change-observed", snapshot);
				}
			await session.ExerciseConflictAsync (operation, ChangeHub, token);
			if (!mutationStarted) throw new InvalidDataException ("The fixture did not perform its independent schedule change.");
			var final = await session.ReadAsync (token);
			RequireExisting (original, final, id, operation.External);
			await session.RecordAsync ("stale-save-refused", final);
			passed = true;
			}
		catch (Exception failure)
			{
			try { await session.RecordAsync ("failure", new { Exception = failure.ToString () }); } catch { }
			detail = "Schedule conflict validation failed. Inspect private evidence.";
			}
		finally
			{
			if (started && original != null && operation != null)
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (4));
				try
					{
					var current = await session.ReadAsync (cleanup.Token);
					var contents = Heating (current.Schedules).Single (s => Id (s) == operation.ScheduleId);
					if (!Equal (contents, operation.Before) && !Equal (contents, operation.External) && !Equal (contents, operation.IncorrectOverwrite))
						throw new InvalidDataException ("Unrelated schedule changes prevent automatic restoration.");
					RequireExisting (original, current, operation.ScheduleId, contents);
					if (!Equal (contents, operation.Before))
						{
						await session.RecordAsync ("contents-restore-intent", new { Original = operation.Before });
						try { await session.SendAsync ("PATCH", "Heating/" + operation.ScheduleId, Days (operation.Before), cleanup.Token); }
						catch (Exception failure)
							{
							passed = false;
							await session.RecordAsync ("contents-restore-error", new { Exception = failure.ToString () });
							}
						await WaitAsync (session, observed =>
							{
							var value = Heating (observed.Schedules).Single (s => Id (s) == operation.ScheduleId);
							if (!Equal (value, contents) && !Equal (value, operation.Before))
								throw new InvalidDataException ("Unexpected contents during restoration.");
							RequireExisting (original, observed, operation.ScheduleId, value);
							return Equal (value, operation.Before);
							}, observationTimeout, cleanup.Token);
						}
					await session.RestoreEditorAsync (cleanup.Token);
					var final = await session.ReadAsync (cleanup.Token);
					RequireOriginal (original, final);
					await session.RecordAsync ("restored", final);
					restored = true;
					detail = passed ? "The stale save preserved the independent hub change; original schedules, room settings and editor were restored." : "Conflict validation failed; original state was restored.";
					}
				catch (Exception failure)
					{
					passed = false;
					try { await session.RecordAsync ("recovery-required", new { Exception = failure.ToString () }); } catch { }
					detail = "Restoration is unconfirmed. Retain reservations and reconcile private evidence.";
					}
				}
			}
		return new (passed && restored, restored, null, detail);
		}
	}