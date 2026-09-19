// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record ScheduleHubSnapshot (JsonElement Schedules, JsonElement Domain);
public sealed record ScheduleSaveCase (int ScheduleId, string Day, int OriginalTemperature, int Temperature, bool AllDays, JsonElement Before, JsonElement After);
public sealed record ScheduleSaveIsolationResult (bool Passed, bool RestorationConfirmed, int? TemporaryScheduleId, string Detail);

public interface IScheduleSaveIsolationSession
	{
	Task<ScheduleHubSnapshot> ReadAsync (CancellationToken token);
	Task RecordAsync (string phase, object value);
	Task SendAsync (string method, string relativePath, object body, CancellationToken token);
	Task ExerciseAsync (ScheduleSaveCase operation, CancellationToken token);
	Task RestoreEditorAsync (CancellationToken token);
	}

/// <summary>Run both real UI saves against one owned schedule, then restore its assignment and remove it.</summary>
public static partial class ScheduleSaveIsolation
	{
	/// <summary>Explicitly exercise a room's existing, exclusively assigned schedule and restore its complete contents.</summary>
	public static async Task<ScheduleSaveIsolationResult> RunExistingAsync (IScheduleSaveIsolationSession session, int roomId,
		TimeSpan observationTimeout, CancellationToken token, IScheduleSaveObserver? observer = null)
		{
		if (roomId <= 0 || observationTimeout <= TimeSpan.Zero || observationTimeout > TimeSpan.FromMinutes (2))
			throw new ArgumentException ("A room and bounded observation timeout are required.");
		ScheduleHubSnapshot? original = null;
		JsonElement source = default;
		int scheduleId = 0;
		bool started = false, passed = false, restored = true;
		string detail = "Preflight failed; no schedule-save input was submitted.";
		var allowed = new List<JsonElement> ();
		try
			{
			original = await session.ReadAsync (token);
			var room = Room (original, roomId);
			ScheduleObservation.ValidateCapture (room, allowManualTargetInitialization: true);
			scheduleId = room.GetProperty ("ScheduleId").GetInt32 ();
			if (original.Domain.GetProperty ("Room").EnumerateArray ().Count (r => r.TryGetProperty ("ScheduleId", out var id) && id.GetInt32 () == scheduleId) != 1)
				throw new InvalidDataException ("An existing schedule must be assigned exclusively to the selected room.");
			source = Heating (original.Schedules).Single (s => Id (s) == scheduleId);
			ValidateDays (source);
			if (observer != null) await observer.BeforeChangesAsync (original, roomId, token);
			RequireOriginal (original, await session.ReadAsync (token));
			await session.RecordAsync ("original", new { RoomId = roomId, ExistingExclusiveScheduleId = scheduleId, Snapshot = original });
			await session.RecordAsync ("existing-schedule-intent", new { RoomId = roomId, ScheduleId = scheduleId, Original = source });
			started = true;
			restored = false;
			allowed.Add (source);
			var expected = source;
			int direction = source.GetProperty ("Monday").GetProperty ("DegreesC")[0].GetInt32 () <= 290 ? 5 : -5;
			foreach (bool allDays in new[] { false, true })
				{
				RequireExisting (original, await session.ReadAsync (token), scheduleId, expected);
				int before = expected.GetProperty ("Monday").GetProperty ("DegreesC")[0].GetInt32 ();
				var after = Edit (expected, "Monday", before + direction, allDays);
				string phase = allDays ? "save-all" : "save-day";
				var operation = new ScheduleSaveCase (scheduleId, "Monday", before, before + direction, allDays, expected, after);
				if (observer != null) await observer.BeforeSaveAsync (await session.ReadAsync (token), operation, token);
				RequireExisting (original, await session.ReadAsync (token), scheduleId, expected);
				await session.RecordAsync (phase + "-intent", operation);
				allowed.Add (after);
				await session.ExerciseAsync (operation, token);
				await WaitAsync (session, snapshot =>
					{
					var current = Heating (snapshot.Schedules).Single (s => Id (s) == scheduleId);
					if (!Equal (current, expected) && !Equal (current, after))
						throw new InvalidDataException ("The selected schedule changed outside the declared save.");
					RequireExisting (original, snapshot, scheduleId, current);
					return Equal (current, after);
					}, observationTimeout, token);
				expected = after;
				await session.RecordAsync (phase + "-observed", after);
				if (observer != null)
					{
					var observed = await session.ReadAsync (token);
					RequireExisting (original, observed, scheduleId, after);
					await observer.AfterSaveAsync (observed, operation, token);
					}
				}
			passed = true;
			}
		catch (Exception failure)
			{
			try { await session.RecordAsync ("failure", new { Exception = failure.ToString () }); } catch { }
			detail = "Existing schedule save validation failed. Inspect private evidence.";
			}
		finally
			{
			if (started && original != null)
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (4));
				try
					{
					var snapshot = await session.ReadAsync (cleanup.Token);
					var current = Heating (snapshot.Schedules).Single (s => Id (s) == scheduleId);
					if (!allowed.Any (s => Equal (s, current)))
						throw new InvalidDataException ("Unexpected schedule contents prevent automatic restoration.");
					RequireExisting (original, snapshot, scheduleId, current);
					if (!Equal (current, source))
						{
						await session.RecordAsync ("contents-restore-intent", new { ScheduleId = scheduleId, Original = source });
						// One compensation only. A lost response remains a failed test even if readback confirms restoration.
						try { await session.SendAsync ("PATCH", "Heating/" + scheduleId, Days (source), cleanup.Token); }
						catch (Exception failure)
							{
							passed = false;
							await session.RecordAsync ("contents-restore-error", new { Exception = failure.ToString () });
							}
						await WaitAsync (session, observed =>
							{
							var contents = Heating (observed.Schedules).Single (s => Id (s) == scheduleId);
							if (!Equal (contents, current) && !Equal (contents, source))
								throw new InvalidDataException ("Unexpected schedule contents during restoration.");
							RequireExisting (original, observed, scheduleId, contents);
							return Equal (contents, source);
							}, observationTimeout, cleanup.Token);
						}
					var physical = await session.ReadAsync (cleanup.Token);
					RequireOriginal (original, physical);
					await session.RecordAsync ("hub-restored", physical);
					if (!await ObserveRestorationAsync (session, observer, physical, roomId, cleanup.Token))
						{
						passed = false;
						detail = "Peer verification failed after physical restoration. Inspect private evidence; the test remains failed.";
						}
					await session.RecordAsync ("editor-restore-intent", new { RoomId = roomId });
					await session.RestoreEditorAsync (cleanup.Token);
					var final = await session.ReadAsync (cleanup.Token);
					RequireOriginal (original, final);
					await session.RecordAsync ("restored", new { Snapshot = final, ExistingExclusiveScheduleId = scheduleId });
					restored = true;
					detail = passed ? "Both UI saves matched independent hub observations; original schedules, room settings and editor were restored."
						: "The save test failed; original schedules, room settings and editor were restored.";
					}
				catch (Exception failure)
					{
					passed = false;
					try { await session.RecordAsync ("recovery-required", new { Original = original, ExistingExclusiveScheduleId = scheduleId, Exception = failure.ToString () }); } catch { }
					detail = "Existing schedule restoration is unconfirmed. Retain reservations and reconcile private evidence.";
					}
				}
			}
		return new (passed && restored, restored, null, detail);
		}

	private static void RequireExisting (ScheduleHubSnapshot original, ScheduleHubSnapshot current, int scheduleId, JsonElement expected)
		{
		var schedules = JsonNode.Parse (original.Schedules.GetRawText ())!.AsObject ();
		var heating = schedules["Heating"]!.AsArray ();
		int index = heating.IndexOf (heating.Single (s => s!["id"]!.GetValue<int> () == scheduleId));
		heating[index] = JsonNode.Parse (expected.GetRawText ());
		RequireOriginal (new (JsonSerializer.SerializeToElement (schedules), original.Domain), current);
		}

	public static async Task<ScheduleSaveIsolationResult> RunAsync (IScheduleSaveIsolationSession session, int roomId, Guid owner,
		TimeSpan observationTimeout, CancellationToken token, IScheduleSaveObserver? observer = null)
		{
		if (owner == Guid.Empty || roomId <= 0 || observationTimeout <= TimeSpan.Zero || observationTimeout > TimeSpan.FromMinutes (2))
			throw new ArgumentException ("A room, unique owner and bounded observation timeout are required.");
		// The hub reports Name[32]; leave space for its terminator and retain 96 random bits.
		// The full owner remains in the durable journal, and a pre-existing name is refused.
		string name = "CI-" + owner.ToString ("N")[..24];
		ScheduleHubSnapshot? original = null;
		JsonElement? created = null;
		JsonElement? expected = null;
		bool createAttempted = false, copyAttempted = false, copied = false, assigned = false, assignmentAttempted = false, passed = false, restored = true;
		int? ownedId = null;
		string phase = "capture";
		string detail = "Preflight failed; no hub mutation was submitted.";
		var allowed = new List<JsonElement> ();
		try
			{
			original = await session.ReadAsync (token);
			var room = Room (original, roomId);
			ScheduleObservation.ValidateCapture (room, allowManualTargetInitialization: true);
			int originalId = room.GetProperty ("ScheduleId").GetInt32 ();
			var source = Heating (original.Schedules).Single (s => Id (s) == originalId);
			ValidateDays (source);
			if (Heating (original.Schedules).Any (s => s.GetProperty ("Name").GetString () == name))
				throw new InvalidDataException ("The owned schedule name already exists.");
			if (observer != null) await observer.BeforeChangesAsync (original, roomId, token);
			RequireOriginal (original, await session.ReadAsync (token));
			await session.RecordAsync ("original", new { Owner = owner, RoomId = roomId, Name = name, Snapshot = original });
			phase = "create";
			await session.RecordAsync ("create-intent", new { Name = name, Assignments = Array.Empty<int> () });
			createAttempted = true;
			restored = false;
			await session.SendAsync ("POST", "Assign", new { Assignments = Array.Empty<int> (), Heating = new { Name = name } }, token);
			created = await FindCreatedAsync (session, original, name, observationTimeout, token);
			ownedId = Id (created.Value);
			allowed.Add (created.Value);
			await session.RecordAsync ("created", created.Value);
			phase = "copy";
			expected = ReplaceDays (created.Value, source);
			allowed.Add (expected.Value);
			await session.RecordAsync ("copy-intent", new { ScheduleId = ownedId, Expected = expected });
			copyAttempted = true;
			await session.SendAsync ("PATCH", "Heating/" + ownedId.Value, Days (source), token);
			await WaitAsync (session, snapshot => ObserveOwned (original, snapshot, roomId, ownedId.Value, expected.Value, false, allowed), observationTimeout, token);
			copied = true;
			await session.RecordAsync ("copied", expected.Value);
			phase = "assign";
			await session.RecordAsync ("assign-intent", new { ScheduleId = ownedId, RoomId = roomId });
			assignmentAttempted = true;
			await session.SendAsync ("PATCH", "Assign", Assignment (expected.Value, [roomId]), token);
			await WaitAsync (session, snapshot => ObserveOwned (original, snapshot, roomId, ownedId.Value, expected.Value, true, allowed), observationTimeout, token);
			assigned = true;
			await session.RecordAsync ("assigned", new { ScheduleId = ownedId, RoomId = roomId });
			int direction = source.GetProperty ("Monday").GetProperty ("DegreesC")[0].GetInt32 () <= 290 ? 5 : -5;
			foreach (bool allDays in new[] { false, true })
				{
				phase = allDays ? "save-all" : "save-day";
				RequireOwned (original, await session.ReadAsync (token), roomId, ownedId.Value, expected.Value, true);
				int before = expected.Value.GetProperty ("Monday").GetProperty ("DegreesC")[0].GetInt32 ();
				var after = Edit (expected.Value, "Monday", before + direction, allDays);
				var operation = new ScheduleSaveCase (ownedId.Value, "Monday", before, before + direction, allDays, expected.Value, after);
				if (observer != null) await observer.BeforeSaveAsync (await session.ReadAsync (token), operation, token);
				RequireOwned (original, await session.ReadAsync (token), roomId, ownedId.Value, expected.Value, true);
				allowed.Add (after);
				await session.RecordAsync (phase + "-intent", operation);
				await session.ExerciseAsync (operation, token);
				expected = after;
				await WaitAsync (session, snapshot => ObserveOwned (original, snapshot, roomId, ownedId.Value, after, true, allowed), observationTimeout, token);
				await session.RecordAsync (phase + "-observed", after);
				if (observer != null)
					{
					var observed = await session.ReadAsync (token);
					RequireOwned (original, observed, roomId, ownedId.Value, after, true);
					await observer.AfterSaveAsync (observed, operation, token);
					}
				}
			passed = true;
			}
		catch (Exception failure)
			{
			// Keep raw transport errors and household data out of the public test result.
			try { await session.RecordAsync ("failure", new { Phase = phase, Exception = failure.ToString () }); } catch { }
			detail = "Schedule save validation failed during " + phase + ". Inspect private evidence.";
			}
		finally
			{
			if (createAttempted && original != null)
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (4));
				try
					{
					// A lost create reply must be reconciled, never retried or treated as non-delivery.
					if (created == null)
						{
						created = await FindCreatedAsync (session, original, name, observationTimeout, cleanup.Token);
						ownedId = Id (created.Value);
						allowed.Add (created.Value);
						await session.RecordAsync ("reconciled-created", created.Value);
						}
					if (copyAttempted && !copied)
						await WaitAsync (session, snapshot => ObserveOwned (original, snapshot, roomId, ownedId!.Value, expected!.Value, false, allowed), observationTimeout, cleanup.Token);
					if (assignmentAttempted && !assigned)
						{
						// Do not race a possibly outstanding assignment with a deletion.
						await WaitAsync (session, snapshot => ObserveOwned (original, snapshot, roomId, ownedId!.Value, expected!.Value, true, allowed), observationTimeout, cleanup.Token);
						assigned = true;
						}
					var current = await session.ReadAsync (cleanup.Token);
					var owned = Heating (current.Schedules).Single (s => Id (s) == ownedId);
					if (!allowed.Any (s => Equal (s, owned)))
						throw new InvalidDataException ("The owned schedule has unexpected data; do not delete it.");
					RequireOwned (original, current, roomId, ownedId!.Value, owned, assigned);
					if (assigned)
						{
						int sourceId = Room (original, roomId).GetProperty ("ScheduleId").GetInt32 ();
						var source = Heating (original.Schedules).Single (s => Id (s) == sourceId);
						int[] users = original.Domain.GetProperty ("Room").EnumerateArray ()
							.Where (r => r.TryGetProperty ("ScheduleId", out var id) && id.GetInt32 () == sourceId).Select (Id).ToArray ();
						await session.RecordAsync ("assignment-restore-intent", new { ScheduleId = sourceId, Rooms = users });
						try { await session.SendAsync ("PATCH", "Assign", Assignment (source, users), cleanup.Token); }
						catch { passed = false; detail = "Restoration response was uncertain; independently reconciled without replay. The test remains failed."; }
						await WaitAsync (session, snapshot => ObserveOwned (original, snapshot, roomId, ownedId.Value, owned, false, [owned]), observationTimeout, cleanup.Token);
						await session.RecordAsync ("assignment-restored", new { ScheduleId = sourceId, Rooms = users });
						}
					RequireOwned (original, await session.ReadAsync (cleanup.Token), roomId, ownedId.Value, owned, false);
					await session.RecordAsync ("delete-intent", new { ScheduleId = ownedId, Expected = owned });
					try { await session.SendAsync ("DELETE", "Heating/" + ownedId.Value, new { }, cleanup.Token); }
					catch { passed = false; detail = "Delete response was uncertain; independently reconciled without replay. The test remains failed."; }
					await WaitAsync (session, snapshot =>
						{
						if (Heating (snapshot.Schedules).Any (s => Id (s) == ownedId))
							{
							RequireOwned (original, snapshot, roomId, ownedId.Value, owned, false);
							return false;
							}
						RequireOriginal (original, snapshot);
						return true;
						}, observationTimeout, cleanup.Token);
					var physical = await session.ReadAsync (cleanup.Token);
					RequireOriginal (original, physical);
					await session.RecordAsync ("hub-restored", physical);
					if (!await ObserveRestorationAsync (session, observer, physical, roomId, cleanup.Token))
						{
						passed = false;
						detail = "Peer verification failed after physical restoration. Inspect private evidence; the test remains failed.";
						}
					if (assigned)
						{
						await session.RecordAsync ("editor-restore-intent", new { RoomId = roomId });
						await session.RestoreEditorAsync (cleanup.Token);
						}
					var final = await session.ReadAsync (cleanup.Token);
					RequireOriginal (original, final);
					await session.RecordAsync ("restored", new { Snapshot = final, TemporaryScheduleId = ownedId });
					restored = true;
					if (passed)
						detail = "Both UI saves matched independent hub observations; original schedules and assignments were restored and the owned schedule removed.";
					}
				catch (Exception failure)
					{
					passed = false;
					try { await session.RecordAsync ("recovery-required", new { Original = original, TemporaryScheduleId = ownedId, Exception = failure.ToString () }); } catch { }
					detail = "Schedule isolation restoration is unconfirmed. Retain reservations and reconcile private evidence.";
					}
				}
			}
		return new (passed && restored, restored, ownedId, detail);
		}

	private static async Task<bool> ObserveRestorationAsync (IScheduleSaveIsolationSession session, IScheduleSaveObserver? observer,
		ScheduleHubSnapshot state, int roomId, CancellationToken token)
		{
		if (observer == null) return true;
		try { await observer.AfterRestorationAsync (state, roomId, token); return true; }
		catch (Exception failure)
			{
			try { await session.RecordAsync ("hub-restored-peer-failed", new { Type = failure.GetType ().FullName }); } catch { }
			return false;
			}
		}

	public static JsonElement Edit (JsonElement schedule, string day, int temperature, bool allDays)
		{
		ValidateDays (schedule);
		if (!Enum.GetNames<DayOfWeek> ().Contains (day) || temperature is < 50 or > 300 || temperature % 5 != 0)
			throw new InvalidDataException ("Unsupported editor temperature or day.");
		var result = JsonNode.Parse (schedule.GetRawText ())!.AsObject ();
		var changed = result[day]!.DeepClone ();
		changed["DegreesC"]![0] = temperature;
		foreach (string selected in allDays ? Enum.GetNames<DayOfWeek> () : [day])
			result[selected] = changed.DeepClone ();
		return JsonSerializer.SerializeToElement (result);
		}

	private static async Task<JsonElement> FindCreatedAsync (IScheduleSaveIsolationSession session, ScheduleHubSnapshot original, string name, TimeSpan timeout, CancellationToken token)
		{
		JsonElement found = default;
		await WaitAsync (session, snapshot =>
			{
			var ids = Heating (original.Schedules).Select (Id).ToHashSet ();
			var added = Heating (snapshot.Schedules).Where (s => !ids.Contains (Id (s))).ToArray ();
			if (added.Length == 0)
				{
				RequireOriginal (original, snapshot);
				return false;
				}
			if (added.Length != 1 || added[0].GetProperty ("Name").GetString () != name)
				throw new InvalidDataException ("Exactly one owned new heating schedule must be observed.");
			found = added[0];
			RequireOwned (original, snapshot, 0, Id (found), found, false);
			return true;
			}, timeout, token);
		return found;
		}

	private static async Task WaitAsync (IScheduleSaveIsolationSession session, Func<ScheduleHubSnapshot, bool> verify, TimeSpan timeout, CancellationToken token)
		{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (timeout);
		int matches = 0;
		while (true)
			{
			var snapshot = await session.ReadAsync (deadline.Token);
			matches = verify (snapshot) ? matches + 1 : 0;
			if (matches == 2)
				return;
			await Task.Delay (250, deadline.Token);
			}
		}

	private static bool ObserveOwned (ScheduleHubSnapshot original, ScheduleHubSnapshot current, int roomId, int ownedId,
		JsonElement expected, bool assigned, IEnumerable<JsonElement> allowed)
		{
		var owned = Heating (current.Schedules).Single (s => Id (s) == ownedId);
		if (!allowed.Any (s => Equal (s, owned)))
			throw new InvalidDataException ("The owned schedule changed outside this test's declared mutations.");
		bool nowAssigned = Room (current, roomId).GetProperty ("ScheduleId").GetInt32 () == ownedId;
		RequireOwned (original, current, roomId, ownedId, owned, nowAssigned);
		return nowAssigned == assigned && Equal (owned, expected);
		}

	public static void RequireOriginal (ScheduleHubSnapshot original, ScheduleHubSnapshot current)
		{
		if (!Equal (ScheduleEditorObservation.PersistentSchedules (original.Schedules), ScheduleEditorObservation.PersistentSchedules (current.Schedules)) ||
			!Equal (Assignments (original.Domain), Assignments (current.Domain)))
			throw new InvalidDataException ("Original schedules or guarded room settings differ.");
		}

	public static void RequireOwned (ScheduleHubSnapshot original, ScheduleHubSnapshot current, int roomId, int ownedId, JsonElement expected, bool assigned)
		{
		if (ownedId <= 0 || Heating (original.Schedules).Any (s => Id (s) == ownedId))
			throw new InvalidDataException ("The temporary schedule identity is not owned by this run.");
		var schedule = Heating (current.Schedules).SingleOrDefault (s => Id (s) == ownedId);
		if (schedule.ValueKind != JsonValueKind.Object || !Equal (schedule, expected))
			throw new InvalidDataException ("The owned schedule differs from the expected contents.");
		var without = JsonNode.Parse (current.Schedules.GetRawText ())!.AsObject ();
		without["Heating"] = new JsonArray (Heating (current.Schedules).Where (s => Id (s) != ownedId).Select (s => JsonNode.Parse (s.GetRawText ())).ToArray ());
		var expectedRooms = JsonNode.Parse (Assignments (original.Domain).GetRawText ())!.AsArray ();
		if (assigned)
			expectedRooms.Single (r => r!["id"]!.GetValue<int> () == roomId)!["ScheduleId"] = ownedId;
		if (!Equal (ScheduleEditorObservation.PersistentSchedules (original.Schedules), ScheduleEditorObservation.PersistentSchedules (JsonSerializer.SerializeToElement (without))) ||
			!Equal (JsonSerializer.SerializeToElement (expectedRooms), Assignments (current.Domain)))
			throw new InvalidDataException ("A household schedule or unrelated room setting changed.");
		}

	private static JsonElement Assignments (JsonElement domain)
		{
		var rooms = domain.GetProperty ("Room").EnumerateArray ().ToArray ();
		if (rooms.Any (r => Id (r) <= 0) || rooms.Select (Id).Distinct ().Count () != rooms.Length)
			throw new InvalidDataException ("Room identities must be unique.");
		string[] fields = ["id", "Name", "ScheduleId", "Mode", "ManualSetPoint", "AwayModeSuppressed", "WindowDetectionActive", "ComfortModeEnabled", "EcoModeEnabled", "HVACMode"];
		return JsonSerializer.SerializeToElement (rooms.OrderBy (Id).Select (r => r.EnumerateObject ()
			.Where (p => fields.Contains (p.Name, StringComparer.Ordinal)).ToDictionary (p => p.Name, p => p.Value.Clone ())));
		}
	private static JsonElement Room (ScheduleHubSnapshot snapshot, int roomId) => snapshot.Domain.GetProperty ("Room").EnumerateArray ().Single (r => Id (r) == roomId);
	private static int Id (JsonElement element) => element.GetProperty ("id").GetInt32 ();
	private static bool Equal (JsonElement a, JsonElement b) => JsonElement.DeepEquals (a, b);
	private static JsonElement[] Heating (JsonElement schedules) => ScheduleEditorObservation.PersistentSchedules (schedules).GetProperty ("Heating").EnumerateArray ().ToArray ();
	private static object Assignment (JsonElement schedule, int[] rooms) => new { Assignments = rooms, Heating = new { id = Id (schedule), Name = schedule.GetProperty ("Name").GetString () } };
	private static Dictionary<string, JsonElement> Days (JsonElement schedule) => Enum.GetNames<DayOfWeek> ().ToDictionary (day => day, day => schedule.GetProperty (day).Clone ());
	private static JsonElement ReplaceDays (JsonElement schedule, JsonElement source)
		{
		var result = JsonNode.Parse (schedule.GetRawText ())!;
		foreach (var day in Days (source))
			result[day.Key] = JsonNode.Parse (day.Value.GetRawText ());
		return JsonSerializer.SerializeToElement (result);
		}
	private static void ValidateDays (JsonElement schedule)
		{
		foreach (var day in Days (schedule).Values)
			{
			int[] times = day.GetProperty ("Time").EnumerateArray ().Select (t => t.GetInt32 ()).ToArray ();
			int[] temperatures = day.GetProperty ("DegreesC").EnumerateArray ().Select (t => t.GetInt32 ()).ToArray ();
			if (times.Length is < 1 or > 10 || times.Length != temperatures.Length ||
				times.Any (t => t is < 0 or > 2330 || t % 100 is not (0 or 30)) || times.Distinct ().Count () != times.Length ||
				!times.SequenceEqual (times.Order ()) || temperatures.Any (t => t is < 50 or > 300 || t % 5 != 0))
				throw new InvalidDataException ("The original schedule cannot be represented by this editor.");
			}
		}
	}