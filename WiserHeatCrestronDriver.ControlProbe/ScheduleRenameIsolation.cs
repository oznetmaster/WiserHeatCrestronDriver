// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record ScheduleRenameCase (int ScheduleId, JsonElement Before, JsonElement After);

public interface IScheduleRenameSession : IScheduleSaveIsolationSession
	{
	Task ExerciseRenameAsync (ScheduleRenameCase operation, Func<CancellationToken, Task> changeHub, CancellationToken token);
	}

public static partial class ScheduleSaveIsolation
	{
	public static async Task<ScheduleSaveIsolationResult> RunRenameAsync (IScheduleRenameSession session, int roomId,
		Guid owner, TimeSpan observationTimeout, CancellationToken token)
		{
		if (roomId <= 0 || owner == Guid.Empty || observationTimeout <= TimeSpan.Zero || observationTimeout > TimeSpan.FromMinutes (2))
			throw new ArgumentException ("A room, owner and bounded observation timeout are required.");
		ScheduleHubSnapshot? original = null;
		ScheduleRenameCase? operation = null;
		bool started = false, attempted = false, passed = false, restored = true;
		string detail = "Rename preflight failed before input.";
		try
			{
			original = await session.ReadAsync (token);
			var room = Room (original, roomId);
			ScheduleObservation.ValidateCapture (room, allowManualTargetInitialization: true);
			int id = room.GetProperty ("ScheduleId").GetInt32 ();
			if (original.Domain.GetProperty ("Room").EnumerateArray ().Count (r => r.TryGetProperty ("ScheduleId", out var value) && value.GetInt32 () == id) != 1)
				throw new InvalidDataException ("Rename validation requires an exclusively assigned schedule.");
			var source = Heating (original.Schedules).Single (s => Id (s) == id);
			ValidateDays (source);
			if (string.IsNullOrWhiteSpace (source.GetProperty ("Name").GetString ()))
				throw new InvalidDataException ("The original schedule needs a restorable name.");
			string name = "CI-" + owner.ToString ("N")[..12];
			if (Heating (original.Schedules).Any (s => s.GetProperty ("Name").GetString () == name))
				throw new InvalidDataException ("The declared temporary label already exists.");
			var renamed = JsonNode.Parse (source.GetRawText ())!;
			renamed["Name"] = name;
			operation = new (id, source, JsonSerializer.SerializeToElement (renamed));
			RequireOriginal (original, await session.ReadAsync (token));
			await session.RecordAsync ("original", new { RoomId = roomId, Snapshot = original });
			await session.RecordAsync ("rename-plan", operation);
			started = true;
			restored = false;
			async Task Change (CancellationToken cancellation)
				{
				if (attempted) throw new InvalidOperationException ("A rename must never be replayed.");
				RequireOriginal (original, await session.ReadAsync (cancellation));
				await session.RecordAsync ("rename-intent", operation);
				cancellation.ThrowIfCancellationRequested ();
				attempted = true;
				await session.SendAsync ("PATCH", "Heating/" + id, new { Name = name }, cancellation);
				await WaitAsync (session, current =>
					{
					var value = Heating (current.Schedules).Single (s => Id (s) == id);
					if (!Equal (value, source) && !Equal (value, operation.After))
						throw new InvalidDataException ("Unexpected schedule contents during rename.");
					RequireExisting (original, current, id, value);
					return Equal (value, operation.After);
					}, observationTimeout, cancellation);
				var observed = await session.ReadAsync (cancellation);
				RequireExisting (original, observed, id, operation.After);
				await session.RecordAsync ("rename-hub-observed", observed);
				}
			await session.ExerciseRenameAsync (operation, Change, token);
			if (!attempted) throw new InvalidDataException ("No independent rename was performed.");
			RequireExisting (original, await session.ReadAsync (token), id, operation.After);
			passed = true;
			}
		catch (Exception failure)
			{
			try { await session.RecordAsync ("failure", new { Exception = failure.ToString () }); } catch { }
			detail = "Rename validation failed; inspect private evidence.";
			}
		finally
			{
			if (started && original != null && operation != null)
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (4));
				try
					{
					var current = await session.ReadAsync (cleanup.Token);
					var value = Heating (current.Schedules).Single (s => Id (s) == operation.ScheduleId);
					if (!Equal (value, operation.Before) && !(attempted && Equal (value, operation.After)))
						throw new InvalidDataException ("An undeclared change prevents automatic name restoration.");
					RequireExisting (original, current, operation.ScheduleId, value);
					if (!Equal (value, operation.Before))
						{
						await session.RecordAsync ("name-restore-intent", operation);
						try { await session.SendAsync ("PATCH", "Heating/" + operation.ScheduleId, new { Name = operation.Before.GetProperty ("Name").GetString () }, cleanup.Token); }
						catch (Exception failure)
							{
							passed = false;
							await session.RecordAsync ("name-restore-error", new { Exception = failure.ToString () });
							}
						await WaitAsync (session, snapshot =>
							{
							var contents = Heating (snapshot.Schedules).Single (s => Id (s) == operation.ScheduleId);
							if (!Equal (contents, value) && !Equal (contents, operation.Before))
								throw new InvalidDataException ("Unexpected contents during name restoration.");
							RequireExisting (original, snapshot, operation.ScheduleId, contents);
							return Equal (contents, operation.Before);
							}, observationTimeout, cleanup.Token);
						}
					await session.RestoreEditorAsync (cleanup.Token);
					var final = await session.ReadAsync (cleanup.Token);
					RequireOriginal (original, final);
					await session.RecordAsync ("restored", final);
					restored = true;
					detail = passed ? "Renamed schedule observed; original name, schedules, room settings and editor restored." : "Rename validation failed; original state restored.";
					}
				catch (Exception failure)
					{
					passed = false;
					try { await session.RecordAsync ("recovery-required", new { Exception = failure.ToString () }); } catch { }
					detail = "Rename restoration unconfirmed; retain reservations and reconcile private evidence.";
					}
				}
			}
		return new (passed && restored, restored, null, detail);
		}
	}