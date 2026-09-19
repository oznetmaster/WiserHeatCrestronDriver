// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text.Json;

namespace WiserHeatCrestronDriver.ControlProbe;

public enum RoomTemperatureAction
	{
	Raise, Lower, BoostOn, BoostOff, PrepareOff, ResumeHeating
	}
public sealed record RoomTemperatureActivity (string Epoch, long Completed, int Pending);
public sealed record RoomTemperatureSnapshot (GatewayAwaySnapshot Gateway, int RoomId, RoomTemperatureActivity Activity,
	double HomeTarget, bool HomeBoost, bool UiMatches)
	{
	public string TemperatureUnits { get; init; } = "Celsius";
	}
public sealed record RoomTemperatureResult (bool Passed, bool RestorationConfirmed, string Detail);

public interface IRoomTemperatureSession
	{
	Task<RoomTemperatureSnapshot> ReadAsync (CancellationToken token);
	Task<RoomTemperatureSnapshot> ReadRestorationAsync (CancellationToken token) => ReadAsync (token);
	Task RecordAsync (string phase, object value);
	Task InputAsync (RoomTemperatureAction action, CancellationToken token);
	Task RestoreAsync (int index, RoomTemperatureRestorePlan plan, JsonElement request, CancellationToken token);
	}

/// <summary>Native setpoint, Boost, or prepared Off/resume controls, with independent policy restoration.</summary>
public static class RoomTemperatureCycle
	{
	private static JsonElement Room (RoomTemperatureSnapshot snapshot) => RoomTemperatureRestoration.Room (snapshot.Gateway.Hub, snapshot.RoomId);
	private static bool Agrees (RoomTemperatureSnapshot snapshot) => snapshot.UiMatches && double.IsFinite (snapshot.HomeTarget) &&
		Math.Abs (RawTarget (snapshot) - Room (snapshot).GetProperty ("CurrentSetPoint").GetInt32 ()) < 0.001 &&
		snapshot.HomeBoost == (RoomTemperatureRestoration.Origin (Room (snapshot)) == "FromBoost");
	private static double RawTarget (RoomTemperatureSnapshot snapshot) => snapshot.TemperatureUnits switch
		{
		"Celsius" => snapshot.HomeTarget * 10,
		"Fahrenheit" => snapshot.HomeTarget == -20 ? -200 : (snapshot.HomeTarget - 32) / 0.18d,
		_ => double.NaN
		};
	private static bool Restored (RoomTemperatureRestorePlan plan, RoomTemperatureSnapshot snapshot, bool verifyUi = true)
		{
		try
			{
			RoomTemperatureRestoration.RequireRestored (plan, Room (snapshot));
			}
		catch (InvalidDataException) { return false; }
		catch (NotSupportedException) { return false; }
		return !verifyUi || Agrees (snapshot) && !snapshot.HomeBoost;
		}
	public static Task<RoomTemperatureResult> RunAsync (IRoomTemperatureSession session, bool boost, TimeSpan timeout, CancellationToken token) =>
		RunCoreAsync (session, boost, false, timeout, token);
	public static Task<RoomTemperatureResult> RunOffAsync (IRoomTemperatureSession session, TimeSpan timeout, CancellationToken token) =>
		RunCoreAsync (session, false, true, timeout, token);
	public static Task<RoomTemperatureResult> RunBoundaryAsync (IRoomTemperatureSession session, bool maximum, TimeSpan timeout, CancellationToken token) =>
		RunCoreAsync (session, false, false, timeout, token, maximum);
	private static async Task<RoomTemperatureResult> RunCoreAsync (IRoomTemperatureSession session, bool boost, bool off, TimeSpan timeout, CancellationToken token, bool? maximum = null)
		{
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes (3))
			throw new ArgumentOutOfRangeException (nameof (timeout));
		RoomTemperatureSnapshot? original = null;
		RoomTemperatureRestorePlan? plan = null;
		int submitted = 0, commands = 0;
		bool attempted = false, passed = false, restored = true;
		string detail = "Preflight failed before any room control input.";
		Func<RoomTemperatureSnapshot, bool>? delivered = null;
		DateTimeOffset requestedAfter = default;
		var elapsed = new Stopwatch ();
		void Guard (RoomTemperatureSnapshot value)
			{
			RoomTemperatureRestoration.RequireGuarded (plan!, original!.Gateway, value.Gateway);
			if (value.RoomId != original.RoomId || value.TemperatureUnits != original.TemperatureUnits ||
				value.Activity.Epoch != original.Activity.Epoch || value.Activity.Pending < 0 ||
				value.Activity.Completed < original.Activity.Completed || value.Activity.Completed > original.Activity.Completed + commands)
				throw new InvalidDataException ("Room identity, temperature units, command lifetime or attribution changed.");
			}
		async Task<RoomTemperatureSnapshot> Wait (Func<RoomTemperatureSnapshot, bool> predicate, DateTimeOffset after, CancellationToken ct, bool independent = false, string? firstMatchPhase = null)
			{
			int matches = 0;
			bool firstRecorded = false;
			var last = after;
			while (true)
				{
				ct.ThrowIfCancellationRequested ();
				var value = independent ? await session.ReadRestorationAsync (ct) : await session.ReadAsync (ct);
				Guard (value);
				if (value.Gateway.RefreshUtc < last)
					throw new InvalidDataException ("Refresh evidence moved backwards.");
				last = value.Gateway.RefreshUtc;
				bool ready = value.Activity.Pending == 0 && value.Activity.Completed == original!.Activity.Completed + commands;
				matches = ready && value.Gateway.RefreshUtc > after && predicate (value) ? matches + 1 : 0;
				if (matches == 1 && !firstRecorded && firstMatchPhase != null)
					{
					// Retain the first full match separately; two matches still determine confirmation.
					await session.RecordAsync (firstMatchPhase, new { Snapshot = value });
					firstRecorded = true;
					}
				if (matches == 2)
					return value;
				await Task.Delay (25, ct);
				}
			}
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (maximum.HasValue ? TimeSpan.FromMinutes (30) : timeout);
		try
			{
			original = await session.ReadAsync (deadline.Token);
			await session.RecordAsync ("preflight", new { Snapshot = original, Boost = boost, Off = off, MaximumBoundary = maximum });
			plan = RoomTemperatureRestoration.Capture (Room (original));
			if (!Guid.TryParseExact (original.Activity.Epoch, "N", out _) || original.Activity.Pending != 0 ||
				original.Activity.Completed < 0 || original.Activity.Completed > long.MaxValue - (maximum.HasValue ? 50 : 2) || !Restored (plan, original))
				throw new InvalidDataException ("Idle room, hub and UI state must agree before input.");
			Guard (original);
			await session.RecordAsync ("original", new
				{
				Snapshot = original,
				Plan = plan,
				Boost = boost
				});
			var current = await session.ReadAsync (deadline.Token);
			Guard (current);
			if (!Restored (plan, current) || current.Activity != original.Activity || current.HomeTarget != original.HomeTarget)
				throw new InvalidDataException ("Starting state changed before input.");
			int start = Room (original).GetProperty ("CurrentSetPoint").GetInt32 ();
			if (start is < 50 or > 300)
				throw new InvalidDataException ("Native temperature testing requires a restorable target within 5–30°C.");
			int delta = start <= 295 ? 5 : -5;
			RoomTemperatureAction[] actions = off ? [RoomTemperatureAction.PrepareOff, RoomTemperatureAction.ResumeHeating]
				: boost ? [RoomTemperatureAction.BoostOn, RoomTemperatureAction.BoostOff]
				: delta > 0 ? [RoomTemperatureAction.Raise, RoomTemperatureAction.Lower] : [RoomTemperatureAction.Lower, RoomTemperatureAction.Raise];
			if (maximum.HasValue)
				{
				if (start % 5 != 0)
					throw new InvalidDataException ("Boundary tests require a target on the supported half-degree grid.");
				int boundary = maximum.Value ? 300 : 50;
				var direction = maximum.Value ? RoomTemperatureAction.Raise : RoomTemperatureAction.Lower;
				actions = start == boundary
					? [maximum.Value ? RoomTemperatureAction.Lower : RoomTemperatureAction.Raise, direction]
					: Enumerable.Repeat (direction, Math.Abs (boundary - start) / 5).ToArray ();
				}
			foreach (var action in actions)
				{
				int target = maximum.HasValue ? Room (current).GetProperty ("CurrentSetPoint").GetInt32 () + (action == RoomTemperatureAction.Raise ? 5 : -5)
					: off ? submitted == 0 ? -200 : 50 : submitted == 0 ? start + delta : start;
				Func<RoomTemperatureSnapshot, bool> nextDelivered = boost
					? action == RoomTemperatureAction.BoostOn
						? value => RoomTemperatureRestoration.Origin (Room (value)) == "FromBoost" && Room (value).GetProperty ("OverrideTimeoutUnixTime").GetInt64 () > DateTimeOffset.UtcNow.ToUnixTimeSeconds ()
						: value => Restored (plan, value, verifyUi: false)
					: value => Room (value).GetProperty ("CurrentSetPoint").GetInt32 () == target &&
						(plan.Scheduled ? Room (value).GetProperty ("OverrideType").GetString () == "Manual"
							: RoomTemperatureRestoration.Origin (Room (value)) == "FromManualMode" && ScheduleObservation.ManualTarget (Room (value)) == target);
				bool Requested (RoomTemperatureSnapshot value) => Agrees (value) && nextDelivered (value);
				await session.RecordAsync ("input-" + (submitted + 1) + "-intent", new
					{
					Action = action,
					Target = boost ? (int?)null : target,
					Snapshot = current
					});
				deadline.Token.ThrowIfCancellationRequested ();
				// Cleanup must track the last attempted input, not a next intent whose journal write failed.
				delivered = nextDelivered;
				requestedAfter = current.Gateway.RefreshUtc;
				submitted++;
				if (action != RoomTemperatureAction.PrepareOff)
					commands++;
				attempted = true;
				restored = false;
				elapsed.Restart ();
				using var inputDeadline = CancellationTokenSource.CreateLinkedTokenSource (deadline.Token);
				inputDeadline.CancelAfter (timeout);
				await session.InputAsync (action, inputDeadline.Token);
				current = await Wait (Requested, requestedAfter, inputDeadline.Token, firstMatchPhase: "input-" + submitted + "-first-match");
				await session.RecordAsync ("input-" + submitted + "-observed", new
					{
					Snapshot = current,
					Seconds = elapsed.Elapsed.TotalSeconds
					});
				}
			if (maximum.HasValue)
				{
				int boundary = maximum.Value ? 300 : 50;
				if (Room (current).GetProperty ("CurrentSetPoint").GetInt32 () != boundary || !Agrees (current))
					throw new InvalidDataException ("The requested thermostat boundary was not observed.");
				await session.RecordAsync ("boundary-observed", new { Snapshot = current, RawBoundary = boundary, Inputs = submitted });
				}
			passed = true;
			}
		catch (Exception failure)
			{
			detail = "Room input or observation failed; inspect private evidence.";
			try
				{
				await session.RecordAsync ("failure", new
					{
					Exception = failure.ToString ()
					});
				}
			catch { }
			}
		finally
			{
			if (attempted && plan != null && original != null && delivered != null)
				{
				using var cleanup = new CancellationTokenSource (timeout * 3);
				try
					{
					// Observe delivery before compensation, even if input acknowledgement was lost. Never repeat the input.
					var current = await Wait (delivered, requestedAfter, cleanup.Token, independent: true);
					if (!Restored (plan, current, verifyUi: false))
						{
						int index = 0;
						foreach (var request in plan.Requests)
							{
							bool cancel = request.GetProperty ("RequestOverride").GetProperty ("Type").GetString () == "None";
							bool Done (RoomTemperatureSnapshot value) => cancel ? Restored (plan, value, verifyUi: false)
								: ScheduleObservation.ManualTarget (Room (value)) == plan.ManualTarget && Room (value).GetProperty ("CurrentSetPoint").GetInt32 () == plan.ManualTarget;
							await session.RecordAsync ("restore-" + index + "-intent", new
								{
								Snapshot = current,
								Request = request
								});
							var after = current.Gateway.RefreshUtc;
							try
								{
								await session.RestoreAsync (index, plan, request, cleanup.Token);
								}
							catch (Exception failure)
								{
								passed = false;
								await session.RecordAsync ("restore-" + index + "-input-error", new
									{
									Exception = failure.ToString ()
									});
								}
							current = await Wait (Done, after, cleanup.Token, independent: true);
							await session.RecordAsync ("restore-" + index + "-observed", new
								{
								Snapshot = current
								});
							index++;
							}
						}
					await session.RecordAsync ("physical-restored", new
						{
						Snapshot = current
						});
					restored = true;
					using var display = CancellationTokenSource.CreateLinkedTokenSource (cleanup.Token);
					display.CancelAfter (timeout);
					current = await Wait (value => Restored (plan, value), requestedAfter, display.Token);
					await session.RecordAsync ("restored", new { Snapshot = current });
					detail = passed ? "The selected controls and original room policy were independently confirmed, with household settings preserved."
						: "The test failed; original room policy was restored without replaying the uncertain input.";
					}
				catch (Exception failure)
					{
					passed = false;
					detail = restored ? "Original physical policy was restored, but UI verification failed. The test remains failed."
						: "Room restoration is unconfirmed. Retain reservations and reconcile the journal.";
					try
						{
						await session.RecordAsync (restored ? "ui-verification-failed" : "recovery-required", new
							{
							Exception = failure.ToString ()
							});
						}
					catch { }
					}
				}
			}
		return new (passed && restored, restored, detail);
		}
	}