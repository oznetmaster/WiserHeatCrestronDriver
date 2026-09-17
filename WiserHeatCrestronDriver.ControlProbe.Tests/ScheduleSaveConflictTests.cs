// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture, Parallelizable (ParallelScope.Children)]
public sealed class ScheduleSaveConflictTests
	{
	private sealed class Session : IScheduleConflictSession
		{
		public JsonObject Schedules { get; } = new () { ["Heating"] = new JsonArray (Schedule (1), Schedule (2)), ["OnOff"] = new JsonArray () };
		public JsonObject Domain { get; } = JsonNode.Parse ("""{"Room":[{"id":1,"Name":"Test","ScheduleId":1,"Mode":"Auto","CurrentSetPoint":220,"ScheduledSetPoint":220,"SetpointOrigin":"FromSchedule","EcoModeEnabled":false},{"id":2,"Name":"Other","ScheduleId":2,"Mode":"Auto"}]}""")!.AsObject ();
		public List<string> Calls { get; } = [];
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
			if (phase == FailRecord) throw new IOException ("Evidence failure.");
			Records.Add (phase);
			return Task.CompletedTask;
			}
		public Task SendAsync (string method, string path, object body, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Assert.That (method, Is.EqualTo ("PATCH"));
			Assert.That (path, Is.EqualTo ("Heating/1"));
			Calls.Add ("write");
			int attempt = Calls.Count (v => v == "write");
			if (IgnoreWrite != attempt)
				foreach (var day in JsonSerializer.SerializeToElement (body).EnumerateObject ())
					Schedules["Heating"]![0]![day.Name] = JsonNode.Parse (day.Value.GetRawText ());
			if (FailWrite == attempt) throw new IOException ("Reply lost after delivery.");
			return Task.CompletedTask;
			}
		public Task ExerciseAsync (ScheduleSaveCase operation, CancellationToken token) => throw new InvalidOperationException ("A normal save was unexpectedly requested.");
		public async Task ExerciseConflictAsync (ScheduleConflictCase operation, Func<CancellationToken, Task> changeHub, CancellationToken token)
			{
			if (Behavior == "omit-change") return;
			await changeHub (token);
			if (Behavior == "replay-change") await changeHub (token);
			if (Behavior == "overwrite") Schedules["Heating"]![0] = JsonNode.Parse (operation.IncorrectOverwrite.GetRawText ());
			if (Behavior == "unrelated-schedule") Schedules["Heating"]![1]!["Name"] = "Someone else";
			if (Behavior == "unrelated-room") Domain["Room"]![1]!["EcoModeEnabled"] = true;
			Cancel?.Cancel ();
			token.ThrowIfCancellationRequested ();
			if (Behavior == "ui-error") throw new IOException ("UI observation failed.");
			}
		public Task RestoreEditorAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Calls.Add ("editor");
			return Task.CompletedTask;
			}
		}

	private static JsonObject Schedule (int id)
		{
		var value = new JsonObject { ["id"] = id, ["Name"] = "Schedule " + id };
		foreach (string day in Enum.GetNames<DayOfWeek> ())
			value[day] = new JsonObject { ["Time"] = new JsonArray (600, 2200), ["DegreesC"] = new JsonArray (220, 170) };
		return value;
		}
	private static Task<ScheduleSaveIsolationResult> Run (Session session, bool allDays = false, CancellationToken token = default) =>
		ScheduleSaveIsolation.RunConflictAsync (session, 1, allDays, TimeSpan.FromMilliseconds (650), token);

	[TestCase (false), TestCase (true)]
	public async Task RefusedStaleSave_PreservesExternalChangeAndRestoresCompleteOriginal (bool allDays)
		{
		var session = new Session ();
		var original = await session.ReadAsync (default);
		var result = await Run (session, allDays);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Calls, Is.EqualTo (new[] { "write", "write", "editor" }));
		Assert.That (session.Records, Does.Contain ("stale-save-refused"));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}

	[Test]
	public async Task SharedSchedule_IsRefusedWithoutAnyWrite ()
		{
		var session = new Session ();
		session.Domain["Room"]![1]!["ScheduleId"] = 1;
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Calls, Is.Empty);
		}

	[TestCase (1), TestCase (2)]
	public async Task LostWriteReply_IsNotReplayedAndKeepsFailureDespiteRestoration (int attempt)
		{
		var session = new Session { FailWrite = attempt };
		var original = await session.ReadAsync (default);
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Calls.Count (c => c == "write"), Is.EqualTo (2));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}

	[TestCase ("overwrite"), TestCase ("ui-error"), TestCase ("replay-change"), TestCase ("omit-change")]
	public async Task FailedChallenge_RestoresKnownStateAndRemainsFailed (string behavior)
		{
		var session = new Session { Behavior = behavior };
		var original = await session.ReadAsync (default);
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Calls.Count (c => c == "write"), Is.LessThanOrEqualTo (2));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}

	[TestCase ("unrelated-room"), TestCase ("unrelated-schedule")]
	public async Task UnrelatedChanges_RefuseAutomaticOverwrite (string behavior)
		{
		var session = new Session { Behavior = behavior };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Calls, Is.EqualTo (new[] { "write" }));
		Assert.That (session.Records, Does.Contain ("recovery-required"));
		}

	[Test]
	public async Task CancellationAfterMutation_UsesIndependentRestorationDeadline ()
		{
		using var cancel = new CancellationTokenSource ();
		var session = new Session { Cancel = cancel };
		var original = await session.ReadAsync (default);
		var result = await Run (session, token: cancel.Token);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}

	[TestCase ("original"), TestCase ("existing-schedule-intent"), TestCase ("external-change-intent")]
	public async Task FailedIntent_DoesNotWriteTheHub (string phase)
		{
		var session = new Session { FailRecord = phase };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Calls, Does.Not.Contain ("write"));
		}

	[TestCase (1, true), TestCase (2, false)]
	public async Task IgnoredWrite_IsDetectedByIndependentReadback (int attempt, bool restored)
		{
		var session = new Session { IgnoreWrite = attempt };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.EqualTo (restored), result.Detail);
		Assert.That (session.Calls.Count (c => c == "write"), Is.EqualTo (attempt));
		}
	}