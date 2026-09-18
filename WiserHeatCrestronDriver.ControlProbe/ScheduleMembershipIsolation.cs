// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

namespace WiserHeatCrestronDriver.ControlProbe;

public interface IScheduleMembershipSession : IScheduleSaveIsolationSession
	{
	Task ExerciseMembershipAsync (int selectedId, Func<CancellationToken, Task<JsonElement>> create,
		Func<CancellationToken, Task> remove, CancellationToken token);
	Task RestoreMembershipViewAsync (CancellationToken token);
	}

public static partial class ScheduleSaveIsolation
	{
	public static async Task<ScheduleSaveIsolationResult> RunMembershipAsync (IScheduleMembershipSession session, int roomId,
		Guid owner, TimeSpan observationTimeout, CancellationToken token)
		{
		if (roomId <= 0 || owner == Guid.Empty || observationTimeout <= TimeSpan.Zero || observationTimeout > TimeSpan.FromMinutes (2))
			throw new ArgumentException ("A room, owner and bounded timeout are required.");
		ScheduleHubSnapshot? original = null;
		JsonElement? created = null;
		JsonElement payload = default;
		bool started = false, createAttempted = false, deleteAttempted = false, passed = false, restored = true, capacityFull = false;
		string name = "CI-" + owner.ToString ("N")[..12];
		string detail = "Membership preflight failed before input.";
		void RequireDeclared (JsonElement value)
			{
			if (value.GetProperty ("Name").GetString () != name || Days (payload).Any (p => !Equal (p.Value, value.GetProperty (p.Key))))
				throw new InvalidDataException ("The new schedule differs from its declared name or days.");
			}
		async Task WaitRemoved (CancellationToken cancellation)
			{
			await WaitAsync (session, snapshot =>
				{
				var remaining = Heating (snapshot.Schedules).SingleOrDefault (s => Id (s) == Id (created!.Value));
				if (remaining.ValueKind == JsonValueKind.Undefined) { RequireOriginal (original!, snapshot); return true; }
				RequireOwned (original!, snapshot, 0, Id (created.Value), created.Value, false);
				return false;
				}, observationTimeout, cancellation);
			}
		try
			{
			original = await session.ReadAsync (token);
			// Schneider's UK/Ireland system guide limits a hub to sixteen climate schedules.
			if (Heating (original.Schedules).Length >= 16)
				{
				capacityFull = true;
				throw new InvalidDataException ("The hub has no free heating-schedule slot.");
				}
			int selectedId = Room (original, roomId).GetProperty ("ScheduleId").GetInt32 ();
			var source = Heating (original.Schedules).Single (s => Id (s) == selectedId);
			ValidateDays (source);
			if (Days (source).Any (day => day.Value.GetProperty ("Time").GetArrayLength () > 8) ||
				Heating (original.Schedules).Any (s => s.GetProperty ("Name").GetString () == name))
				throw new InvalidDataException ("Creation requires supported day layouts and a new unique name.");
			var contents = Days (source).ToDictionary (p => p.Key, p => (object)p.Value);
			contents["Name"] = name;
			payload = JsonSerializer.SerializeToElement (contents);
			RequireOriginal (original, await session.ReadAsync (token));
			await session.RecordAsync ("original", new { RoomId = roomId, SelectedId = selectedId, Snapshot = original });
			await session.RecordAsync ("membership-plan", new { Assignments = Array.Empty<int> (), Heating = payload });
			started = true;
			restored = false;
			async Task<JsonElement> Create (CancellationToken cancellation)
				{
				if (createAttempted) throw new InvalidOperationException ("Creation must never be replayed.");
				RequireOriginal (original, await session.ReadAsync (cancellation));
				await session.RecordAsync ("membership-create-intent", new { Assignments = Array.Empty<int> (), Heating = payload });
				cancellation.ThrowIfCancellationRequested ();
				createAttempted = true;
				await session.SendAsync ("POST", "Assign", new { Assignments = Array.Empty<int> (), Heating = payload }, cancellation);
				created = await FindCreatedAsync (session, original, name, observationTimeout, cancellation);
				RequireDeclared (created.Value);
				await WaitAsync (session, snapshot =>
					{
					RequireOwned (original, snapshot, 0, Id (created.Value), created.Value, false);
					return true;
					}, observationTimeout, cancellation);
				var observed = await session.ReadAsync (cancellation);
				RequireOwned (original, observed, 0, Id (created.Value), created.Value, false);
				await session.RecordAsync ("membership-added", new { Created = created.Value, Snapshot = observed });
				return created.Value;
				}
			async Task Remove (CancellationToken cancellation)
				{
				if (!createAttempted || !created.HasValue || deleteAttempted) throw new InvalidOperationException ("An observed owned schedule may be deleted only once.");
				RequireDeclared (created.Value);
				RequireOwned (original, await session.ReadAsync (cancellation), 0, Id (created.Value), created.Value, false);
				await session.RecordAsync ("membership-delete-intent", new { Created = created.Value });
				cancellation.ThrowIfCancellationRequested ();
				deleteAttempted = true;
				await session.SendAsync ("DELETE", "Heating/" + Id (created.Value), new { }, cancellation);
				await WaitRemoved (cancellation);
				var observed = await session.ReadAsync (cancellation);
				RequireOriginal (original, observed);
				await session.RecordAsync ("membership-removed", observed);
				}
			await session.ExerciseMembershipAsync (selectedId, Create, Remove, token);
			if (!createAttempted || !deleteAttempted) throw new InvalidDataException ("Both actual membership changes must be observed.");
			RequireOriginal (original, await session.ReadAsync (token));
			passed = true;
			}
		catch (Exception failure)
			{
			try { await session.RecordAsync ("failure", new { Exception = failure.ToString () }); } catch { }
			detail = capacityFull ? "The hub is at its 16-heating-schedule limit. Provide a free slot before running membership tests; no schedule input was sent." : "Membership validation failed; inspect private evidence.";
			}
		finally
			{
			if (started && original != null)
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (4));
				try
					{
					if (createAttempted)
						{
						if (!created.HasValue)
							{
							created = await FindCreatedAsync (session, original, name, observationTimeout, cleanup.Token);
							await session.RecordAsync ("membership-reconciled-create", created.Value);
							}
						RequireDeclared (created.Value);
						var current = await session.ReadAsync (cleanup.Token);
						bool exists = Heating (current.Schedules).Any (s => Id (s) == Id (created.Value));
						if (exists)
							{
							RequireOwned (original, current, 0, Id (created.Value), created.Value, false);
							if (!deleteAttempted)
								{
								await session.RecordAsync ("membership-cleanup-delete-intent", new { Created = created.Value });
								deleteAttempted = true;
								try { await session.SendAsync ("DELETE", "Heating/" + Id (created.Value), new { }, cleanup.Token); }
								catch (Exception failure) { passed = false; await session.RecordAsync ("membership-cleanup-delete-error", new { Exception = failure.ToString () }); }
								}
							await WaitRemoved (cleanup.Token);
							}
						else if (!deleteAttempted) passed = false; // External removal is not successful test attribution.
						}
					await session.RestoreMembershipViewAsync (cleanup.Token);
					var final = await session.ReadAsync (cleanup.Token);
					RequireOriginal (original, final);
					await session.RecordAsync ("restored", final);
					restored = true;
					detail = passed ? "Added and removed choices observed; original schedules, assignments and view restored." : "Membership validation failed; original state restored.";
					}
				catch (Exception failure)
					{
					passed = false;
					try { await session.RecordAsync ("recovery-required", new { Exception = failure.ToString () }); } catch { }
					detail = "Membership restoration unconfirmed; retain reservations and reconcile private evidence.";
					}
				}
			}
		return new (passed && restored, restored, created.HasValue ? Id (created.Value) : null, detail);
		}
	}