// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

namespace WiserHeatCrestronDriver.ConfigurationProbe;

public sealed record GatewayFeatures (bool HotWater, bool Away);
public sealed record GatewayFeatureObservation (GatewayFeatures Features, string OtherConfigurationIdentity, string InstanceIdentity, bool Ready);
public sealed record GatewayFeatureCycleResult (bool Passed, bool ConfigurationRestored, int VariantsObserved);

public interface IGatewayFeatureSession
	{
	Task<GatewayFeatureObservation> ReadAsync (CancellationToken token);
	Task ApplyAsync (GatewayFeatures features, CancellationToken token);
	Task InspectAsync (string phase, GatewayFeatures features, CancellationToken token);
	Task RecordAsync (string phase, object value);
	}

/// <summary>Exercises the four gateway feature configurations and restores only settings owned by this cycle.</summary>
public static class GatewayFeatureCycle
	{
	public static async Task<GatewayFeatureCycleResult> RunAsync (IGatewayFeatureSession session, TimeSpan timeout, CancellationToken token)
		{
		ArgumentNullException.ThrowIfNull (session);
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes (20)) throw new ArgumentOutOfRangeException (nameof (timeout));
		GatewayFeatureObservation? original = null;
		GatewayFeatures? previous = null, intended = null;
		bool attempted = false, passed = false, restored = true;
		int observedVariants = 0;
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (timeout);
		void Guard (GatewayFeatureObservation current)
			{
			if (string.IsNullOrWhiteSpace (current.OtherConfigurationIdentity) || string.IsNullOrWhiteSpace (current.InstanceIdentity) ||
				current.OtherConfigurationIdentity != original!.OtherConfigurationIdentity || current.InstanceIdentity != original.InstanceIdentity)
				throw new InvalidDataException ("Unrelated configuration or gateway identity changed; no automatic overwrite is allowed.");
			if (current.Features != previous && current.Features != intended && current.Features != original.Features)
				throw new InvalidDataException ("Feature settings changed outside the owned transition.");
			}
		async Task<GatewayFeatureObservation> Wait (GatewayFeatures expected, CancellationToken ct)
			{
			using var wait = CancellationTokenSource.CreateLinkedTokenSource (ct);
			wait.CancelAfter (timeout < TimeSpan.FromSeconds (90) ? timeout : TimeSpan.FromSeconds (90));
			int matches = 0;
			while (true)
				{
				wait.Token.ThrowIfCancellationRequested ();
				var value = await session.ReadAsync (wait.Token);
				Guard (value);
				matches = value.Ready && value.Features == expected ? matches + 1 : 0;
				if (matches == 2) return value;
				await Task.Delay (25, wait.Token);
				}
			}
		async Task Failure (string phase, Exception error)
			{
			try { await session.RecordAsync (phase, new { ExceptionType = error.GetType ().FullName }); }
			catch { /* Preserve the failed result even when evidence storage is unavailable. */ }
			}
		try
			{
			original = await session.ReadAsync (deadline.Token);
			previous = intended = original.Features;
			Guard (original);
			if (!original.Ready) throw new InvalidDataException ("A ready gateway is required before reconfiguration.");
			await session.RecordAsync ("features-original", original);
			await Wait (original.Features, deadline.Token);
			await session.InspectAsync ("features-baseline", original.Features, deadline.Token);
			observedVariants++;
			// Gray-code order changes one flag at each forward step, including all four combinations.
			GatewayFeatures[] targets =
				[
				new (!original.Features.HotWater, original.Features.Away),
				new (!original.Features.HotWater, !original.Features.Away),
				new (original.Features.HotWater, !original.Features.Away)
				];
			for (int index = 0; index < targets.Length; index++)
				{
				var before = await Wait (previous, deadline.Token);
				intended = targets[index];
				string phase = "features-" + (index + 1).ToString (System.Globalization.CultureInfo.InvariantCulture);
				await session.RecordAsync (phase + "-intent", new { Before = before, Intended = intended });
				deadline.Token.ThrowIfCancellationRequested ();
				attempted = true;
				restored = false;
				await session.ApplyAsync (intended, deadline.Token);
				await session.RecordAsync (phase + "-observed", await Wait (intended, deadline.Token));
				await session.InspectAsync (phase, intended, deadline.Token);
				// Bracket the rendered inspection with complete configuration/identity checks.
				await Wait (intended, deadline.Token);
				observedVariants++;
				previous = intended;
				}
			passed = true;
			}
		catch (Exception error) { await Failure ("features-failure", error); }
		finally
			{
			if (attempted && original != null)
				{
				using var cleanup = new CancellationTokenSource (timeout);
				try
					{
					var current = await session.ReadAsync (cleanup.Token);
					Guard (current);
					// Establish the delivered state before compensation; a lost reply never repeats a forward command.
					await Wait (current.Features, cleanup.Token);
					if (current.Features != original.Features)
						{
						await session.RecordAsync ("features-restore-intent", new { Current = current, Original = original.Features });
						try { await session.ApplyAsync (original.Features, cleanup.Token); }
						catch (Exception error)
							{
							passed = false;
							await Failure ("features-restore-input-error", error);
							}
						}
					var final = await Wait (original.Features, cleanup.Token);
					await session.RecordAsync ("features-restored", final);
					restored = true;
					await session.InspectAsync ("features-restored-ui", original.Features, cleanup.Token);
					await Wait (original.Features, cleanup.Token);
					}
				catch (Exception error)
					{
					passed = false;
					restored = false;
					await Failure ("features-recovery-failure", error);
					}
				}
			}
		return new (passed && restored, restored, observedVariants);
		}
	}