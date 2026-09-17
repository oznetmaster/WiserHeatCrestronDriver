// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture, Parallelizable (ParallelScope.Children)]
public sealed class ScheduleSaveIsolationTests
	{
	private sealed class Session : IScheduleSaveIsolationSession
		{
		public JsonObject Schedules { get; } = new () { ["Heating"] = new JsonArray (Schedule (1, "Original"), Schedule (2, "Unrelated")), ["OnOff"] = new JsonArray (), ["Smart"] = new JsonObject () };
		public JsonObject Domain { get; } = JsonNode.Parse ("""{"Room":[{"id":1,"Name":"Test","ScheduleId":1,"Mode":"Auto","CurrentSetPoint":220,"ScheduledSetPoint":220,"SetpointOrigin":"FromSchedule","EcoModeEnabled":false},{"id":2,"Name":"Other shared user","ScheduleId":1,"Mode":"Auto"}]}""")!.AsObject ();
		public List<string> Calls { get; } = [];
		public List<string> Records { get; } = [];
		public List<ScheduleSaveCase> Saves { get; } = [];
		public string? LoseReply { get; init; }
		public string? Ignore { get; init; }
		public string? FailRecord { get; init; }
		public string? Interfere { get; init; }
		public CancellationTokenSource? CancelAfterCreate { get; init; }
		public CancellationTokenSource? CancelAfterSave { get; init; }
		private int _assignments;
		public Task<ScheduleHubSnapshot> ReadAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			return Task.FromResult (new ScheduleHubSnapshot (JsonSerializer.SerializeToElement (Schedules), JsonSerializer.SerializeToElement (Domain)));
			}
		public Task RecordAsync (string phase, object value)
			{
			if (phase == FailRecord)
				throw new IOException ("Injected evidence failure.");
			Records.Add (phase);
			return Task.CompletedTask;
			}
		public Task SendAsync (string method, string path, object body, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			string phase = method == "POST" ? "create" : method == "DELETE" ? "delete" : path == "Assign" ? ++_assignments == 1 ? "assign" : "restore" : path == "Heating/1" ? "restore-days" : "copy";
			Calls.Add (phase);
			if (Ignore == phase)
				return Task.CompletedTask;
			var payload = JsonSerializer.SerializeToElement (body);
			var heating = Schedules["Heating"]!.AsArray ();
			if (phase == "create")
				{
				string name = payload.GetProperty ("Heating").GetProperty ("Name").GetString ()!;
				if (name.Length >= 32)
					throw new InvalidDataException ("Name: string overflow: Name[32]");
				heating.Add (Schedule (3, name));
				CancelAfterCreate?.Cancel ();
				}
			else if (phase == "delete")
				heating.Remove (heating.Single (s => s!["id"]!.GetValue<int> () == 3));
			else if (phase is "copy" or "restore-days")
				{
				var owned = heating.Single (s => s!["id"]!.GetValue<int> () == (phase == "copy" ? 3 : 1))!;
				foreach (var day in payload.EnumerateObject ())
					owned[day.Name] = JsonNode.Parse (day.Value.GetRawText ());
				}
			else
				{
				int id = payload.GetProperty ("Heating").GetProperty ("id").GetInt32 ();
				int[] assigned = payload.GetProperty ("Assignments").EnumerateArray ().Select (i => i.GetInt32 ()).ToArray ();
				foreach (var room in Domain["Room"]!.AsArray ())
					{
					if (assigned.Contains (room!["id"]!.GetValue<int> ()))
						room["ScheduleId"] = id;
					else if (room["ScheduleId"]?.GetValue<int> () == id)
						room.AsObject ().Remove ("ScheduleId");
					}
				}
			if (LoseReply == phase)
				throw new IOException ("Injected uncertain reply after delivery.");
			return Task.CompletedTask;
			}
		public Task ExerciseAsync (ScheduleSaveCase operation, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Saves.Add (operation);
			if (Ignore == (operation.AllDays ? "save-all" : "save-day"))
				return Task.CompletedTask;
			var heating = Schedules["Heating"]!.AsArray ();
			int index = heating.IndexOf (heating.Single (s => s!["id"]!.GetValue<int> () == operation.ScheduleId));
			heating[index] = JsonNode.Parse (operation.After.GetRawText ());
			if (Interfere == "schedule")
				heating[0]!["Monday"]!["DegreesC"]![0] = 150;
			if (Interfere == "other-room")
				Domain["Room"]![1]!["ScheduleId"] = 3;
			if (Interfere == "mode")
				Domain["Room"]![0]!["Mode"] = "Manual";
			if (Interfere == "eco")
				Domain["Room"]![0]!["EcoModeEnabled"] = true;
			if (Interfere == "owned")
				heating[index]!["Name"] = "Another user's schedule";
			if (Interfere == "other-schedule")
				heating.Single (s => s!["id"]!.GetValue<int> () == 2)!["Monday"]!["DegreesC"]![0] = 150;
			CancelAfterSave?.Cancel ();
			if (LoseReply == (operation.AllDays ? "save-all" : "save-day"))
				throw new IOException ("Injected UI failure after save.");
			return Task.CompletedTask;
			}
		public Task RestoreEditorAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Calls.Add ("editor");
			return Task.CompletedTask;
			}
		}

	private static JsonObject Schedule (int id, string name)
		{
		var result = new JsonObject { ["id"] = id, ["Name"] = name };
		foreach (string day in Enum.GetNames<DayOfWeek> ())
			result[day] = new JsonObject { ["Time"] = new JsonArray (600, 2200), ["DegreesC"] = new JsonArray (220, 170) };
		return result;
		}
	private static Task<ScheduleSaveIsolationResult> Run (Session session, CancellationToken token = default) =>
		ScheduleSaveIsolation.RunAsync (session, 1, Guid.NewGuid (), TimeSpan.FromSeconds (1), token);
	private static Task<ScheduleSaveIsolationResult> RunExisting (Session session, CancellationToken token = default) =>
		ScheduleSaveIsolation.RunExistingAsync (session, 1, TimeSpan.FromSeconds (1), token);
	private static Session Exclusive (Session? session = null)
		{
		session ??= new Session ();
		session.Domain["Room"]![1]!["ScheduleId"] = 2;
		return session;
		}

	[Test]
	public async Task ExistingExclusiveSchedule_BothSavesAreObservedAndAllOriginalContentsRestored ()
		{
		var session = Exclusive ();
		var original = await session.ReadAsync (default);
		var result = await RunExisting (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (result.TemporaryScheduleId, Is.Null);
		Assert.That (session.Calls, Is.EqualTo (new[] { "restore-days", "editor" }));
		Assert.That (session.Saves, Has.Count.EqualTo (2));
		Assert.That (session.Saves.Select (s => s.Temperature), Is.EqualTo (new[] { 225, 230 }));
		Assert.That (session.Records, Does.Contain ("existing-schedule-intent"));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}

	[Test]
	public async Task ExistingSharedSchedule_IsRefusedBeforeAnyInput ()
		{
		var session = new Session ();
		var result = await RunExisting (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Calls, Is.Empty);
		Assert.That (session.Saves, Is.Empty);
		}

	[TestCase ("save-day")]
	[TestCase ("save-all")]
	[TestCase ("restore-days")]
	public async Task ExistingSchedule_LostResponseIsNeverReplayedAndRestoresOriginalContents (string phase)
		{
		var session = Exclusive (new () { LoseReply = phase });
		var original = await session.ReadAsync (default);
		var result = await RunExisting (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Calls.Count (c => c == "restore-days"), Is.EqualTo (1));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}

	[TestCase ("other-schedule")]
	[TestCase ("other-room")]
	[TestCase ("owned")]
	[TestCase ("mode")]
	[TestCase ("eco")]
	public async Task ExistingSchedule_ExternalChangesPreventOverwritingHouseholdState (string change)
		{
		var session = Exclusive (new () { Interfere = change });
		var result = await RunExisting (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Calls, Is.Empty);
		}

	[TestCase ("save-day", true)]
	[TestCase ("save-all", true)]
	[TestCase ("restore-days", false)]
	public async Task ExistingSchedule_IgnoredWriteCannotPass (string phase, bool restored)
		{
		var session = Exclusive (new () { Ignore = phase });
		var result = await RunExisting (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.EqualTo (restored));
		Assert.That (session.Calls.Count (c => c == "restore-days"), Is.LessThanOrEqualTo (1));
		}

	[TestCase ("original", true)]
	[TestCase ("existing-schedule-intent", true)]
	[TestCase ("contents-restore-intent", false)]
	[TestCase ("restored", false)]
	public async Task ExistingSchedule_EvidenceFailureDoesNotClaimUnprovenRestoration (string phase, bool restored)
		{
		var session = Exclusive (new () { FailRecord = phase });
		var result = await RunExisting (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.EqualTo (restored));
		if (phase is "original" or "existing-schedule-intent")
			Assert.That (session.Saves, Is.Empty);
		if (phase == "contents-restore-intent")
			Assert.That (session.Calls, Is.Empty);
		}

	[Test]
	public async Task ExistingSchedule_CancellationAfterSaveUsesIndependentRestorationDeadline ()
		{
		using var cancellation = new CancellationTokenSource ();
		var session = Exclusive (new () { CancelAfterSave = cancellation });
		var original = await session.ReadAsync (default);
		var result = await RunExisting (session, cancellation.Token);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}

	[Test]
	public async Task BothSaves_AreNontrivialAndPreserveAllOriginalSchedulesAndSharedUsers ()
		{
		var session = new Session ();
		var original = await session.ReadAsync (default);
		var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Calls, Is.EqualTo (new[] { "create", "copy", "assign", "editor", "restore", "editor", "delete" }));
		Assert.That (session.Saves, Has.Count.EqualTo (2));
		var day = session.Saves[0];
		Assert.That (day.After.GetProperty ("Monday").GetProperty ("DegreesC")[0].GetInt32 (), Is.EqualTo (225));
		foreach (string other in Enum.GetNames<DayOfWeek> ().Where (d => d != "Monday"))
			Assert.That (JsonElement.DeepEquals (day.Before.GetProperty (other), day.After.GetProperty (other)), Is.True);
		var all = session.Saves[1];
		foreach (string selected in Enum.GetNames<DayOfWeek> ())
			Assert.That (all.After.GetProperty (selected).GetProperty ("DegreesC")[0].GetInt32 (), Is.EqualTo (230));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}

	[TestCase ("create")]
	[TestCase ("copy")]
	[TestCase ("assign")]
	[TestCase ("save-day")]
	[TestCase ("save-all")]
	[TestCase ("restore")]
	[TestCase ("delete")]
	public async Task LostReply_ReconcilesAppliedMutationWithoutReplayAndRemainsFailed (string phase)
		{
		var session = new Session { LoseReply = phase };
		var original = await session.ReadAsync (default);
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Calls.Count (c => c == phase), Is.LessThanOrEqualTo (1));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}

	[TestCase ("create")]
	[TestCase ("copy")]
	[TestCase ("assign")]
	public async Task UnobservedMutation_IsNotReplayedOrAssumedSafe (string phase)
		{
		var session = new Session { Ignore = phase };
		// Make copying nontrivial rather than accepting the fake hub's default weekdays.
		session.Schedules["Heating"]![0]!["Monday"]!["DegreesC"]![0] = 225;
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Calls.Count (c => c == phase), Is.EqualTo (1));
		Assert.That (session.Calls, Does.Not.Contain ("delete"));
		}

	[TestCase ("schedule")]
	[TestCase ("other-room")]
	[TestCase ("mode")]
	[TestCase ("eco")]
	[TestCase ("owned")]
	public async Task ExternalChanges_PreventCleanupOverwritingOrDeletingOtherUsersState (string change)
		{
		var session = new Session { Interfere = change };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Calls, Does.Not.Contain ("restore").And.Not.Contain ("delete"));
		}

	[TestCase ("save-day")]
	[TestCase ("save-all")]
	public async Task AcknowledgedButIgnoredSave_IsFailedAndTheOwnedScheduleIsCleanedUp (string phase)
		{
		var session = new Session { Ignore = phase };
		var original = await session.ReadAsync (default);
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Calls.Count (c => c == "delete"), Is.EqualTo (1));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}

	[TestCase ("original", false)]
	[TestCase ("create-intent", false)]
	[TestCase ("copy-intent", true)]
	[TestCase ("assign-intent", true)]
	[TestCase ("save-day-intent", true)]
	public async Task JournalFailure_DoesNotSendAnUnrecordedMutation (string phase, bool created)
		{
		var session = new Session { FailRecord = phase };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Calls.Contains ("create"), Is.EqualTo (created));
		Assert.That (session.Calls, Does.Not.Contain (phase.Replace ("-intent", "")));
		}

	[TestCase ("assignment-restore-intent")]
	[TestCase ("delete-intent")]
	[TestCase ("restored")]
	public async Task MissingCleanupEvidence_DoesNotClaimRestoration (string phase)
		{
		var session = new Session { FailRecord = phase };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Records, Does.Contain ("recovery-required"));
		}

	[Test]
	public async Task CancellationAfterCreate_UsesIndependentRecoveryDeadline ()
		{
		using var cancellation = new CancellationTokenSource ();
		var session = new Session { CancelAfterCreate = cancellation };
		var original = await session.ReadAsync (default);
		var result = await Run (session, cancellation.Token);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Calls, Is.EqualTo (new[] { "create", "delete" }));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}
	}