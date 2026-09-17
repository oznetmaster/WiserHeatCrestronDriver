// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture, Parallelizable (ParallelScope.Children)]
public sealed class ScheduleLayoutIsolationTests
	{
	private sealed class Session : IScheduleLayoutSession
		{
		public JsonObject Schedules { get; } = new () { ["Heating"] = new JsonArray (Schedule (1), Schedule (2)), ["OnOff"] = new JsonArray () };
		public JsonObject Domain { get; } = JsonNode.Parse ("""{"Room":[{"id":1,"Name":"Test","ScheduleId":1,"Mode":"Auto","CurrentSetPoint":220,"ScheduledSetPoint":220,"SetpointOrigin":"FromSchedule","EcoModeEnabled":false},{"id":2,"Name":"Other","ScheduleId":2,"Mode":"Auto"}]}""")!.AsObject ();
		public List<int> Observed { get; } = [];
		public List<string> Records { get; } = [];
		public int Writes { get; private set; }
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
			Writes++;
			if (IgnoreWrite != Writes)
				foreach (var day in JsonSerializer.SerializeToElement (body).EnumerateObject ())
					Schedules["Heating"]![0]![day.Name] = JsonNode.Parse (day.Value.GetRawText ());
			if (FailWrite == Writes) throw new IOException ("Reply lost after delivery.");
			return Task.CompletedTask;
			}
		public Task ExerciseAsync (ScheduleSaveCase operation, CancellationToken token) => throw new InvalidOperationException ("A UI save was unexpectedly requested.");
		public Task ObserveLayoutAsync (ScheduleLayoutCase operation, CancellationToken token)
			{
			Observed.Add (operation.Count);
			Assert.That (JsonElement.DeepEquals (operation.Schedule, JsonSerializer.SerializeToElement (Schedules["Heating"]![0])), Is.True);
			Assert.That (operation.Schedule.GetProperty (operation.Day).GetProperty ("Time").GetArrayLength (), Is.EqualTo (operation.Count));
			foreach (string day in Enum.GetNames<DayOfWeek> ().Where (d => d != operation.Day))
				Assert.That (JsonElement.DeepEquals (operation.Schedule.GetProperty (day), JsonSerializer.SerializeToElement (Schedule (1)[day])), Is.True);
			if (Behavior == "ui-failure") throw new IOException ("Missing rendered row.");
			if (Behavior == "unrelated-room") Domain["Room"]![1]!["EcoModeEnabled"] = true;
			if (Behavior == "unrelated-schedule") Schedules["Heating"]![1]!["Name"] = "Another writer";
			if (Behavior == "unexpected-selected") Schedules["Heating"]![0]!["Monday"]!["DegreesC"]![0] = 230;
			Cancel?.Cancel ();
			token.ThrowIfCancellationRequested ();
			return Task.CompletedTask;
			}
		public Task RestoreEditorAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Records.Add ("editor-restored");
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
	private static Task<ScheduleSaveIsolationResult> Run (Session session, CancellationToken token = default) =>
		ScheduleSaveIsolation.RunLayoutsAsync (session, 1, "Monday", [1, 10, 1], TimeSpan.FromMilliseconds (650), token);

	[Test]
	public async Task GrowingThenShrinkingLayouts_AreObservedAndCompleteOriginalRestored ()
		{
		var session = new Session ();
		var original = await session.ReadAsync (default);
		var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Observed, Is.EqualTo (new[] { 1, 10, 1 }));
		Assert.That (session.Writes, Is.EqualTo (4));
		Assert.That (session.Records, Does.Contain ("layout-2-hub-observed"));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}
	[TestCase (0), TestCase (11)]
	public void UnsupportedLayout_IsRefusedBeforeAnyWrite (int count)
		{
		var session = new Session ();
		Assert.ThrowsAsync<ArgumentException> (() => ScheduleSaveIsolation.RunLayoutsAsync (session, 1, "Monday", [1, count], TimeSpan.FromSeconds (1), default));
		Assert.That (session.Writes, Is.Zero);
		}
	[Test]
	public async Task SharedSchedule_IsRefusedBeforeAnyWrite ()
		{
		var session = new Session ();
		session.Domain["Room"]![1]!["ScheduleId"] = 1;
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Writes, Is.Zero);
		}
	[TestCase (1), TestCase (4)]
	public async Task LostReply_IsNotReplayedAndRemainsFailedAfterRestoration (int attempt)
		{
		var session = new Session { FailWrite = attempt };
		var original = await session.ReadAsync (default);
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Writes, Is.EqualTo (attempt == 1 ? 2 : 4));
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}
	[TestCase (1, true), TestCase (4, false)]
	public async Task IgnoredWrite_IsDetected (int attempt, bool restored)
		{
		var session = new Session { IgnoreWrite = attempt };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.EqualTo (restored));
		}
	[TestCase ("original"), TestCase ("existing-schedule-intent"), TestCase ("layout-0-intent")]
	public async Task MissingIntent_PreventsWrites (string phase)
		{
		var session = new Session { FailRecord = phase };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Writes, Is.Zero);
		}
	[TestCase ("unrelated-room"), TestCase ("unrelated-schedule"), TestCase ("unexpected-selected")]
	public async Task Interference_PreventsAutomaticOverwrite (string behavior)
		{
		var session = new Session { Behavior = behavior };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Writes, Is.EqualTo (1));
		}
	[Test]
	public async Task FailedUiCoverage_RestoresOriginalButKeepsFailure ()
		{
		var session = new Session { Behavior = "ui-failure" };
		var original = await session.ReadAsync (default);
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}
	[Test]
	public async Task Cancellation_UsesIndependentCleanup ()
		{
		using var cancel = new CancellationTokenSource ();
		var session = new Session { Cancel = cancel };
		var original = await session.ReadAsync (default);
		var result = await Run (session, cancel.Token);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		ScheduleSaveIsolation.RequireOriginal (original, await session.ReadAsync (default));
		}
	}