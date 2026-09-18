// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture, Parallelizable (ParallelScope.Children)]
public sealed class ScheduleMembershipIsolationTests
	{
	private sealed class Session : IScheduleMembershipSession
		{
		public JsonObject Schedules { get; } = new () { ["Heating"] = new JsonArray (Schedule (1), Schedule (2)), ["OnOff"] = new JsonArray () };
		public JsonObject Domain { get; } = JsonNode.Parse ("""{"Room":[{"id":1,"Name":"Test","ScheduleId":1,"Mode":"Auto"},{"id":2,"Name":"Other","ScheduleId":2,"Mode":"Manual","ManualSetPoint":220}]}""")!.AsObject ();
		public List<string> Writes { get; } = [];
		public List<string> Records { get; } = [];
		public int FailWrite { get; init; }
		public int IgnoreWrite { get; init; }
		public string? FailRecord { get; init; }
		public string? Behavior { get; init; }
		public CancellationTokenSource? Cancel { get; init; }
		public Task<ScheduleHubSnapshot> ReadAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			return Task.FromResult (new ScheduleHubSnapshot (JsonSerializer.SerializeToElement (Schedules), JsonSerializer.SerializeToElement (Domain)));
			}
		public Task RecordAsync (string phase, object value)
			{
			if (phase == FailRecord) throw new IOException ("Journal unavailable.");
			Records.Add (phase);
			return Task.CompletedTask;
			}
		public Task SendAsync (string method, string path, object body, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Writes.Add (method + " " + path);
			if (method == "POST")
				{
				Assert.That (path, Is.EqualTo ("Assign"));
				var data = JsonSerializer.SerializeToElement (body);
				Assert.That (data.GetProperty ("Assignments").GetArrayLength (), Is.Zero);
				var heating = data.GetProperty ("Heating");
				Assert.That (heating.EnumerateObject ().Select (p => p.Name), Is.EquivalentTo (Enum.GetNames<DayOfWeek> ().Append ("Name")));
				foreach (string day in Enum.GetNames<DayOfWeek> ()) Assert.That (heating.GetProperty (day).GetProperty ("Time").GetArrayLength (), Is.GreaterThan (0));
				if (IgnoreWrite != Writes.Count)
					{
					var created = JsonNode.Parse (heating.GetRawText ())!;
					created["id"] = 3;
					Schedules["Heating"]!.AsArray ().Add (created);
					}
				}
			else
				{
				Assert.That (method + " " + path, Is.EqualTo ("DELETE Heating/3"));
				if (IgnoreWrite != Writes.Count) Schedules["Heating"]!.AsArray ().RemoveAt (2);
				}
			if (FailWrite == Writes.Count) throw new IOException ("Reply lost after delivery.");
			return Task.CompletedTask;
			}
		public async Task ExerciseMembershipAsync (int id, Func<CancellationToken, Task<JsonElement>> create, Func<CancellationToken, Task> remove, CancellationToken token)
			{
			Assert.That (id, Is.EqualTo (1));
			if (Behavior == "no-change") return;
			await create (token);
			if (Behavior == "replay-create") await create (token);
			if (Behavior == "ui-failure") throw new IOException ("Added option was not visible.");
			Cancel?.Cancel ();
			token.ThrowIfCancellationRequested ();
			if (Behavior == "foreign-name") Schedules["Heating"]![2]!["Name"] = "Other";
			if (Behavior == "foreign-day") Schedules["Heating"]![2]!["Monday"]!["DegreesC"]![0] = 240;
			if (Behavior == "foreign-assignment") Domain["Room"]![1]!["ScheduleId"] = 3;
			if (Behavior == "foreign-schedule") Schedules["Heating"]![1]!["Name"] = "Other";
			await remove (token);
			if (Behavior == "replay-delete") await remove (token);
			if (Behavior == "stale-ui") throw new IOException ("Removed option was still visible.");
			}
		public Task ExerciseAsync (ScheduleSaveCase operation, CancellationToken token) => throw new InvalidOperationException ("No save expected.");
		public Task RestoreEditorAsync (CancellationToken token) => throw new InvalidOperationException ("No editor compensation expected.");
		public Task RestoreMembershipViewAsync (CancellationToken token) { token.ThrowIfCancellationRequested (); Records.Add ("view-restored"); return Task.CompletedTask; }
		}
	private static JsonObject Schedule (int id)
		{
		var value = new JsonObject { ["id"] = id, ["Name"] = "Original " + id, ["CurrentSetpoint"] = 220 };
		foreach (string day in Enum.GetNames<DayOfWeek> ()) value[day] = new JsonObject { ["Time"] = new JsonArray (600, 2200), ["DegreesC"] = new JsonArray (220, 170) };
		return value;
		}
	private static Task<ScheduleSaveIsolationResult> Run (Session session, CancellationToken token = default) =>
		ScheduleSaveIsolation.RunMembershipAsync (session, 1, Guid.Parse ("6e5f2bcabfe84d14ba5a4b9e6218b9a8"), TimeSpan.FromMilliseconds (650), token);
	[Test]
	public async Task FullCreationAndDeletionPreserveAllExistingAssignments ()
		{
		var session = new Session ();
		var original = await session.ReadAsync (default);
		var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Writes, Is.EqualTo (new[] { "POST Assign", "DELETE Heating/3" }));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}
	[TestCase (1), TestCase (2)]
	public async Task LostReplyIsNotReplayedAndRemainsFailed (int attempt)
		{
		var session = new Session { FailWrite = attempt };
		var original = await session.ReadAsync (default);
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Writes.Count, Is.EqualTo (2));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}
	[TestCase (1), TestCase (2)]
	public async Task UnobservedWriteDoesNotClaimRestoration (int attempt)
		{
		var session = new Session { IgnoreWrite = attempt };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Writes.Count, Is.EqualTo (attempt));
		}
	[TestCase ("original"), TestCase ("membership-plan"), TestCase ("membership-create-intent")]
	public async Task MissingCreationJournalPreventsWrites (string phase)
		{
		var session = new Session { FailRecord = phase };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Writes, Is.Empty);
		}
	[TestCase ("foreign-name"), TestCase ("foreign-day"), TestCase ("foreign-assignment"), TestCase ("foreign-schedule")]
	public async Task InterferencePreventsAutomaticDeletion (string behavior)
		{
		var session = new Session { Behavior = behavior };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Writes, Is.EqualTo (new[] { "POST Assign" }));
		}
	[TestCase ("no-change", 0), TestCase ("replay-create", 2), TestCase ("replay-delete", 2), TestCase ("ui-failure", 2), TestCase ("stale-ui", 2)]
	public async Task MissingUiOrReplayCannotPass (string behavior, int writes)
		{
		var session = new Session { Behavior = behavior };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Writes.Count, Is.EqualTo (writes));
		}
	[Test]
	public async Task CancellationUsesIndependentCleanup ()
		{
		using var cancel = new CancellationTokenSource ();
		var session = new Session { Cancel = cancel };
		var result = await Run (session, cancel.Token);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Writes.Count, Is.EqualTo (2));
		}
	[TestCase ("membership-delete-intent", null, true, 2), TestCase ("membership-cleanup-delete-intent", "ui-failure", false, 1)]
	public async Task DeletionRequiresItsOwnDurableIntent (string phase, string? behavior, bool restored, int writes)
		{
		var session = new Session { FailRecord = phase, Behavior = behavior };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.EqualTo (restored));
		Assert.That (session.Writes.Count, Is.EqualTo (writes));
		}
	[TestCase (16), TestCase (17)]
	public async Task FullHubFailsBeforeAnyCreationOrUiInput (int count)
		{
		var session = new Session ();
		for (int id = 3; id <= count; id++) session.Schedules["Heating"]!.AsArray ().Add (Schedule (id));
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (result.Detail, Does.Contain ("16-heating-schedule limit"));
		Assert.That (session.Writes, Is.Empty);
		Assert.That (session.Records, Does.Not.Contain ("membership-create-intent"));
		}
	[Test]
	public async Task EmptyDayCannotBeCreated ()
		{
		var session = new Session ();
		session.Schedules["Heating"]![0]!["Monday"]!["Time"] = new JsonArray ();
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Writes, Is.Empty);
		}
	}