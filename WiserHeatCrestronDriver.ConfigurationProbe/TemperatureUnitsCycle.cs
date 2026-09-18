// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

namespace WiserHeatCrestronDriver.ConfigurationProbe;

public sealed record TemperatureUnitsObservation (DriverConfigurationSnapshot Configuration, bool Ready);
public sealed record TemperatureUnitsExercise (bool Passed, bool RestorationConfirmed);
public sealed record TemperatureUnitsResult (bool Passed, bool RestorationConfirmed, string Detail);

public interface ITemperatureUnitsSession
	{
	Task<TemperatureUnitsObservation> ReadAsync (CancellationToken token);
	Task RecordAsync (string phase, object value);
	Task ApplyAsync (string units, CancellationToken token);
	Task<TemperatureUnitsExercise> ExerciseAsync (CancellationToken token);
	}

/// <summary>Changes only display units, runs a separately restoring exercise, then verifies the original configuration.</summary>
public static class TemperatureUnitsCycle
	{
	private static string Units (DriverConfigurationSnapshot snapshot) => snapshot.Items.Single (i => i.Id == "TemperatureUnits").CurrentValue!.Value.GetString ()!;
	private static void Guard (DriverConfigurationSnapshot original, DriverConfigurationSnapshot current)
		{
		_ = ConfigurationCompatibility.Identity (current);
		if (current.DeviceId != original.DeviceId || current.Model != original.Model || current.Name != original.Name ||
			current.Version != original.Version || current.IsReconfigurable != true)
			throw new InvalidDataException ("Driver identity or reconfiguration capability changed.");
		var normalized = current with
			{
			Items = current.Items.Select (i => i.Id == "TemperatureUnits" ? i with
				{
				CurrentValue = JsonSerializer.SerializeToElement (Units (original))
				} : i).ToArray ()
			};
		if (ConfigurationCompatibility.Identity (normalized) != ConfigurationCompatibility.Identity (original))
			throw new InvalidDataException ("An unrelated driver setting changed; automatic restoration stopped.");
		}

	public static async Task<TemperatureUnitsResult> RunAsync (ITemperatureUnitsSession session, string targetUnits, TimeSpan timeout, CancellationToken token)
		{
		if (targetUnits is not ("Celsius" or "Fahrenheit"))
			throw new ArgumentException ("Explicit Celsius or Fahrenheit units are required.", nameof (targetUnits));
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes (30))
			throw new ArgumentOutOfRangeException (nameof (timeout));
		DriverConfigurationSnapshot? original = null;
		bool attempted = false, exerciseStarted = false, exerciseRestored = false, passed = false, restored = true;
		string detail = "Configuration preflight failed before any command.";
		async Task<TemperatureUnitsObservation> Observe (string units, CancellationToken ct)
			{
			using var observation = CancellationTokenSource.CreateLinkedTokenSource (ct);
			observation.CancelAfter (timeout < TimeSpan.FromSeconds (90) ? timeout : TimeSpan.FromSeconds (90));
			ct = observation.Token;
			int matches = 0;
			while (true)
				{
				ct.ThrowIfCancellationRequested ();
				var observed = await session.ReadAsync (ct);
				Guard (original!, observed.Configuration);
				matches = observed.Ready && Units (observed.Configuration) == units ? matches + 1 : 0;
				if (matches == 2)
					return observed;
				await Task.Delay (25, ct);
				}
			}
		async Task RecordFailure (string phase, Exception failure)
			{
			try { await session.RecordAsync (phase, new { Exception = failure.ToString () }); }
			catch { }
			}
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (timeout);
		try
			{
			var first = await session.ReadAsync (deadline.Token);
			original = first.Configuration;
			_ = ConfigurationCompatibility.Identity (original);
			var item = original.Items.Single (i => i.Id == "TemperatureUnits");
			if (!first.Ready || original.IsReconfigurable != true || item.ReadOnly != false || Units (original) == targetUnits)
				throw new InvalidDataException ("A ready reconfigurable driver and a different writable unit setting are required.");
			Guard (original, original);
			await session.RecordAsync ("units-original", first);
			var before = await Observe (Units (original), deadline.Token);
			await session.RecordAsync ("units-change-intent", new { Original = before, TargetUnits = targetUnits });
			deadline.Token.ThrowIfCancellationRequested ();
			attempted = true;
			restored = false;
			await session.ApplyAsync (targetUnits, deadline.Token);
			await session.RecordAsync ("units-change-observed", await Observe (targetUnits, deadline.Token));
			exerciseStarted = true;
			var exercise = await session.ExerciseAsync (deadline.Token);
			exerciseRestored = exercise.RestorationConfirmed;
			await session.RecordAsync ("units-exercise-result", exercise);
			passed = exercise.Passed && exerciseRestored;
			}
		catch (Exception failure)
			{
			await RecordFailure ("units-failure", failure);
			}
		finally
			{
			if (attempted && original != null)
				{
				using var cleanup = new CancellationTokenSource (timeout);
				try
					{
					if (exerciseStarted && !exerciseRestored)
						throw new InvalidDataException ("Control restoration must be reconciled before driver reconfiguration.");
					// An uncertain reply never causes the request to be repeated. Establish delivery before compensation.
					var current = await Observe (targetUnits, cleanup.Token);
					await session.RecordAsync ("units-restore-intent", new { Original = original, Current = current });
					try { await session.ApplyAsync (Units (original), cleanup.Token); }
					catch (Exception failure)
						{
						passed = false;
						await RecordFailure ("units-restore-input-error", failure);
						}
					var final = await Observe (Units (original), cleanup.Token);
					await session.RecordAsync ("units-restored", final);
					restored = true;
					detail = passed ? "Alternate-unit exercise and original driver configuration verified."
						: "The exercise failed; original driver configuration was independently restored.";
					}
				catch (Exception failure)
					{
					passed = false;
					detail = "Restoration is unconfirmed. Retain reservations and reconcile the configuration/control journal.";
					await RecordFailure ("units-recovery-required", failure);
					}
				}
			}
		return new (passed && restored, restored, detail);
		}
	}