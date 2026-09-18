// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

using NUnit.Framework;

using WiserHeatCrestronDriver.ConfigurationProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class TemperatureUnitsCycleTests
	{
	private sealed class Session : ITemperatureUnitsSession
		{
		public TemperatureUnitsObservation State = new (new (123, "Test Wiser", ConfigurationCompatibility.Model, "1.3.011.0000", true, true, true,
			new Dictionary<string, string>
				{
				["_Host_"] = "hub.invalid", ["HubSecret"] = "synthetic-secret", ["TemperatureUnits"] = "Celsius",
				["BoostDelta"] = "2", ["BoostDurationMinutes"] = "60", ["EnableWholeHouseHotWater"] = "true", ["AllowAwayMode"] = "false"
				}.Select (v => new DriverConfigurationItem (v.Key, v.Key, "String", false, false, false, true, JsonSerializer.SerializeToElement (v.Value))).ToArray ()), true);
		public List<string> Writes = [];
		public List<string> Records = [];
		public int Exercises;
		public string? Fault;
		public string? FailedRecord;
		public CancellationTokenSource? Cancel;
		public void Set (string key, string value) => State = State with
			{
			Configuration = State.Configuration with
				{
				Items = State.Configuration.Items.Select (i => i.Id == key ? i with { CurrentValue = JsonSerializer.SerializeToElement (value) } : i).ToArray ()
				}
			};
		public Task<TemperatureUnitsObservation> ReadAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			return Task.FromResult (State);
			}
		public Task RecordAsync (string phase, object value)
			{
			if (phase == FailedRecord)
				throw new IOException ("Synthetic journal failure.");
			Records.Add (phase);
			return Task.CompletedTask;
			}
		public Task ApplyAsync (string units, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Writes.Add (units);
			if (Fault == "not-delivered")
				throw new IOException ("Uncertain request.");
			Set ("TemperatureUnits", units);
			if (Fault == "foreign")
				Set ("BoostDelta", "3");
			if (Fault == "identity")
				State = State with { Configuration = State.Configuration with { DeviceId = 456 } };
			if (Fault == "not-ready")
				State = State with { Ready = false };
			Cancel?.Cancel ();
			if (Fault == "lost-change" && Writes.Count == 1 || Fault == "lost-restore" && Writes.Count == 2)
				throw new IOException ("Synthetic lost acknowledgement.");
			return Task.CompletedTask;
			}
		public Task<TemperatureUnitsExercise> ExerciseAsync (CancellationToken token)
			{
			Exercises++;
			if (Fault == "exercise-exception")
				throw new IOException ("Uncertain controls.");
			return Task.FromResult (new TemperatureUnitsExercise (Fault is not ("exercise-failed" or "exercise-unrestored"), Fault != "exercise-unrestored"));
			}
		}
	private static Task<TemperatureUnitsResult> Run (Session session, string target = "Fahrenheit", CancellationToken token = default) =>
		TemperatureUnitsCycle.RunAsync (session, target, TimeSpan.FromMilliseconds (500), token);
	[TestCase ("Celsius", "Fahrenheit"), TestCase ("Fahrenheit", "Celsius")]
	public async Task AlternateUnitsAndCompleteConfigurationAreRestored (string original, string target)
		{
		var session = new Session ();
		session.Set ("TemperatureUnits", original);
		string identity = ConfigurationCompatibility.Identity (session.State.Configuration);
		var result = await Run (session, target);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Writes, Is.EqualTo (new[] { target, original }));
		Assert.That (session.Exercises, Is.EqualTo (1));
		Assert.That (ConfigurationCompatibility.Identity (session.State.Configuration), Is.EqualTo (identity));
		}
	[TestCase ("lost-change", 0), TestCase ("lost-restore", 1), TestCase ("exercise-failed", 1)]
	public async Task FailureRestoresConfigurationWithoutRepeatingCommands (string fault, int exercises)
		{
		var session = new Session { Fault = fault };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Writes, Is.EqualTo (new[] { "Fahrenheit", "Celsius" }));
		Assert.That (session.Exercises, Is.EqualTo (exercises));
		}
	[TestCase ("not-delivered"), TestCase ("foreign"), TestCase ("identity"), TestCase ("not-ready")]
	[TestCase ("exercise-exception"), TestCase ("exercise-unrestored")]
	public async Task UncertainOrForeignStateRetainsRecoveryWithoutFurtherCommands (string fault)
		{
		var session = new Session { Fault = fault };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Writes, Is.EqualTo (new[] { "Fahrenheit" }));
		Assert.That (session.Records, Does.Contain ("units-recovery-required"));
		}
	[TestCase ("units-original"), TestCase ("units-change-intent")]
	public async Task MissingPreflightJournalPreventsChange (string phase)
		{
		var session = new Session { FailedRecord = phase };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True);
		Assert.That (session.Writes, Is.Empty);
		}
	[Test]
	public async Task MissingRestoreIntentPreventsCompensation ()
		{
		var session = new Session { FailedRecord = "units-restore-intent" };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Writes, Is.EqualTo (new[] { "Fahrenheit" }));
		}
	[TestCase ("Celsius"), TestCase ("Kelvin")]
	public async Task UnsupportedOrUnchangedTargetNeverSendsInput (string target)
		{
		var session = new Session ();
		if (target == "Kelvin")
			Assert.ThrowsAsync<ArgumentException> (async () => await Run (session, target));
		else
			Assert.That ((await Run (session, target)).Passed, Is.False);
		Assert.That (session.Writes, Is.Empty);
		}
	[Test]
	public async Task CancellationAfterDeliveryStillRestoresConfiguration ()
		{
		using var cancel = new CancellationTokenSource ();
		var session = new Session { Cancel = cancel };
		var result = await Run (session, token: cancel.Token);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Writes, Is.EqualTo (new[] { "Fahrenheit", "Celsius" }));
		Assert.That (session.Exercises, Is.Zero);
		}
	}