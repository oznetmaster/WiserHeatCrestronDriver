// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture, Parallelizable (ParallelScope.Children)]
public sealed class ScheduleRenameIsolationTests
	{
	private sealed class Session : IScheduleRenameSession
		{
		public JsonObject Schedules { get; } = new () { ["Heating"] = new JsonArray (Schedule (1), Schedule (2)), ["OnOff"] = new JsonArray () };
		public JsonObject Domain { get; } = JsonNode.Parse ("""{"Room":[{"id":1,"Name":"Test","ScheduleId":1,"Mode":"Auto","CurrentSetPoint":220,"ScheduledSetPoint":220,"SetpointOrigin":"FromSchedule"},{"id":2,"Name":"Other","ScheduleId":2,"Mode":"Auto"}]}""")!.AsObject ();
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
			if (phase == FailRecord) throw new IOException ("Journal failed.");
			Records.Add (phase);
			return Task.CompletedTask;
			}
		public Task SendAsync (string method, string path, object body, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Assert.That (method, Is.EqualTo ("PATCH"));
			Assert.That (path, Is.EqualTo ("Heating/1"));
			var changes = JsonSerializer.SerializeToElement (body);
			Assert.That (changes.EnumerateObject ().Select (p => p.Name), Is.EqualTo (new[] { "Name" }));
			Writes++;
			if (IgnoreWrite != Writes) Schedules["Heating"]![0]!["Name"] = changes.GetProperty ("Name").GetString ();
			if (FailWrite == Writes) throw new IOException ("Reply lost after delivery.");
			return Task.CompletedTask;
			}
		public Task ExerciseAsync (ScheduleSaveCase operation, CancellationToken token) => throw new InvalidOperationException ("Unexpected save.");
		public async Task ExerciseRenameAsync (ScheduleRenameCase operation, Func<CancellationToken, Task> changeHub, CancellationToken token)
			{
			if (Behavior == "no-change") return;
			if (Behavior == "foreign-before")
				{
				Schedules["Heating"]![0]!["Name"] = operation.After.GetProperty ("Name").GetString ();
				return;
				}
			await changeHub (token);
			if (Behavior == "replay") await changeHub (token);
			if (Behavior == "ui-failure") throw new IOException ("Open selector retained the old label.");
			if (Behavior == "foreign-name") Schedules["Heating"]![0]!["Name"] = "External name";
			if (Behavior == "foreign-day") Schedules["Heating"]![0]!["Monday"]!["DegreesC"]![0] = 230;
			if (Behavior == "foreign-room") Domain["Room"]![1]!["ScheduleId"] = 1;
			if (Behavior == "foreign-schedule") Schedules["Heating"]![1]!["Name"] = "External name";
			Cancel?.Cancel ();
			token.ThrowIfCancellationRequested ();
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
		var schedule = new JsonObject { ["id"] = id, ["Name"] = "Schedule " + id };
		foreach (string day in Enum.GetNames<DayOfWeek> ())
			schedule[day] = new JsonObject { ["Time"] = new JsonArray (600, 2200), ["DegreesC"] = new JsonArray (220, 170) };
		return schedule;
		}
	private static Task<ScheduleSaveIsolationResult> Run (Session session, CancellationToken token = default) =>
		ScheduleSaveIsolation.RunRenameAsync (session, 1, Guid.Parse ("52a00505-3c28-4324-bc52-07b1f97c8795"), TimeSpan.FromMilliseconds (650), token);
	[Test]
	public async Task Rename_PreservesIdentityAndRestoresCompleteOriginal ()
		{
		var session = new Session ();
		var before = await session.ReadAsync (default);
		var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Writes, Is.EqualTo (2));
		Assert.That (session.Records, Does.Contain ("rename-hub-observed"));
		ScheduleSaveIsolation.RequireOriginal (before, await session.ReadAsync (default));
		}
	[TestCase (1), TestCase (2)]
	public async Task LostReply_IsNotReplayedAndRemainsFailed (int attempt)
		{
		var session = new Session { FailWrite = attempt };
		var before = await session.ReadAsync (default);
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Writes, Is.EqualTo (2));
		ScheduleSaveIsolation.RequireOriginal (before, await session.ReadAsync (default));
		}
	[TestCase (1, true), TestCase (2, false)]
	public async Task IgnoredWrite_IsDetected (int attempt, bool restored)
		{
		var result = await Run (new Session { IgnoreWrite = attempt });
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.EqualTo (restored));
		}
	[TestCase ("original"), TestCase ("rename-plan"), TestCase ("rename-intent")]
	public async Task MissingIntent_PreventsWrites (string phase)
		{
		var session = new Session { FailRecord = phase };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Writes, Is.Zero);
		}
	[TestCase ("foreign-name"), TestCase ("foreign-day"), TestCase ("foreign-room"), TestCase ("foreign-schedule"), TestCase ("foreign-before")]
	public async Task Interference_PreventsAutomaticOverwrite (string behavior)
		{
		var session = new Session { Behavior = behavior };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Writes, Is.EqualTo (behavior == "foreign-before" ? 0 : 1));
		}
	[TestCase ("no-change"), TestCase ("ui-failure"), TestCase ("replay")]
	public async Task MissingOrFailedObservation_RemainsFailedAndRestores (string behavior)
		{
		var session = new Session { Behavior = behavior };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Writes, Is.EqualTo (behavior == "no-change" ? 0 : 2));
		}
	[Test]
	public async Task Cancellation_UsesIndependentCleanup ()
		{
		using var cancel = new CancellationTokenSource ();
		var session = new Session { Cancel = cancel };
		var result = await Run (session, cancel.Token);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Writes, Is.EqualTo (2));
		}
	[Test]
	public async Task SharedSchedule_IsRefusedBeforeInput ()
		{
		var session = new Session ();
		session.Domain["Room"]![1]!["ScheduleId"] = 1;
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Writes, Is.Zero);
		}
	}