// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record ScheduleLayoutCase (int Index, int ScheduleId, string Day, int Count, JsonElement Schedule);

public interface IScheduleLayoutSession : IScheduleSaveIsolationSession
	{
	Task ObserveLayoutAsync (ScheduleLayoutCase operation, CancellationToken token);
	}

public static partial class ScheduleSaveIsolation
	{
	public static async Task<ScheduleSaveIsolationResult> RunLayoutsAsync (IScheduleLayoutSession session, int roomId,
		string day, IReadOnlyList<int> entryCounts, TimeSpan observationTimeout, CancellationToken token)
		{
		if (roomId <= 0 || !Enum.GetNames<DayOfWeek> ().Contains (day) || entryCounts.Count is < 2 or > 20 ||
			entryCounts.Any (count => count is < 1 or > 10) || entryCounts.Distinct ().Count () < 2 ||
			observationTimeout <= TimeSpan.Zero || observationTimeout > TimeSpan.FromMinutes (2))
			throw new ArgumentException ("Select a room, day, two or more distinct supported layouts and bounded observation timeout.");
		ScheduleHubSnapshot? original = null;
		JsonElement source = default;
		int id = 0;
		bool started = false, passed = false, restored = true;
		string detail = "Layout preflight failed before input.";
		var allowed = new List<JsonElement> ();
		try
			{
			original = await session.ReadAsync (token);
			var room = Room (original, roomId);
			ScheduleObservation.ValidateCapture (room, allowManualTargetInitialization: true);
			id = room.GetProperty ("ScheduleId").GetInt32 ();
			if (original.Domain.GetProperty ("Room").EnumerateArray ().Count (r => r.TryGetProperty ("ScheduleId", out var value) && value.GetInt32 () == id) != 1)
				throw new InvalidDataException ("A layout test requires an existing exclusively assigned schedule.");
			source = Heating (original.Schedules).Single (s => Id (s) == id);
			ValidateDays (source);
			RequireOriginal (original, await session.ReadAsync (token));
			await session.RecordAsync ("original", new { RoomId = roomId, ExistingExclusiveScheduleId = id, Snapshot = original });
			await session.RecordAsync ("existing-schedule-intent", new { Day = day, EntryCounts = entryCounts, ScheduleId = id });
			started = true;
			restored = false;
			var expected = source;
			allowed.Add (source);
			for (int index = 0; index < entryCounts.Count; index++)
				{
				RequireExisting (original, await session.ReadAsync (token), id, expected);
				var node = JsonNode.Parse (source.GetRawText ())!;
				int count = entryCounts[index];
				// Half-hour entries, distinct temperatures, one declared day only. Hub acceptance is verified, never assumed.
				node[day] = new JsonObject
					{
					["Time"] = new JsonArray (Enumerable.Range (0, count).Select (slot => (JsonNode?)JsonValue.Create ((slot / 2 * 100) + (slot % 2 * 30))).ToArray ()),
					["DegreesC"] = new JsonArray (Enumerable.Range (0, count).Select (slot => (JsonNode?)JsonValue.Create (170 + (slot * 5))).ToArray ())
					};
				var after = JsonSerializer.SerializeToElement (node);
				ValidateDays (after);
				var operation = new ScheduleLayoutCase (index, id, day, count, after);
				string prefix = "layout-" + index;
				await session.RecordAsync (prefix + "-intent", operation);
				allowed.Clear ();
				allowed.Add (source);
				allowed.Add (expected);
				allowed.Add (after);
				await session.SendAsync ("PATCH", "Heating/" + id, Days (after), token);
				await WaitAsync (session, snapshot =>
					{
					var current = Heating (snapshot.Schedules).Single (s => Id (s) == id);
					if (!Equal (current, expected) && !Equal (current, after))
						throw new InvalidDataException ("Unexpected schedule contents during layout preparation.");
					RequireExisting (original, snapshot, id, current);
					return Equal (current, after);
					}, observationTimeout, token);
				var observed = await session.ReadAsync (token);
				RequireExisting (original, observed, id, after);
				await session.RecordAsync (prefix + "-hub-observed", observed);
				await session.ObserveLayoutAsync (operation, token);
				RequireExisting (original, await session.ReadAsync (token), id, after);
				expected = after;
				}
			passed = true;
			}
		catch (Exception failure)
			{
			try { await session.RecordAsync ("failure", new { Exception = failure.ToString () }); } catch { }
			detail = "Layout validation failed; inspect private evidence.";
			}
		finally
			{
			if (started && original != null)
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (4));
				try
					{
					var current = await session.ReadAsync (cleanup.Token);
					var contents = Heating (current.Schedules).Single (s => Id (s) == id);
					if (!allowed.Any (s => Equal (s, contents)))
						throw new InvalidDataException ("Unrelated schedule contents prevent automatic restoration.");
					RequireExisting (original, current, id, contents);
					if (!Equal (contents, source))
						{
						await session.RecordAsync ("contents-restore-intent", new { Original = source });
						try { await session.SendAsync ("PATCH", "Heating/" + id, Days (source), cleanup.Token); }
						catch (Exception failure)
							{
							passed = false;
							await session.RecordAsync ("contents-restore-error", new { Exception = failure.ToString () });
							}
						await WaitAsync (session, snapshot =>
							{
							var value = Heating (snapshot.Schedules).Single (s => Id (s) == id);
							if (!Equal (value, contents) && !Equal (value, source))
								throw new InvalidDataException ("Unexpected contents during layout restoration.");
							RequireExisting (original, snapshot, id, value);
							return Equal (value, source);
							}, observationTimeout, cleanup.Token);
						}
					await session.RestoreEditorAsync (cleanup.Token);
					var final = await session.ReadAsync (cleanup.Token);
					RequireOriginal (original, final);
					await session.RecordAsync ("restored", final);
					restored = true;
					detail = passed ? "All declared layouts were observed; original schedules, room settings and editor restored." : "Layout validation failed; original state restored.";
					}
				catch (Exception failure)
					{
					passed = false;
					try { await session.RecordAsync ("recovery-required", new { Exception = failure.ToString () }); } catch { }
					detail = "Layout restoration unconfirmed; retain reservations and reconcile private evidence.";
					}
				}
			}
		return new (passed && restored, restored, null, detail);
		}
	}