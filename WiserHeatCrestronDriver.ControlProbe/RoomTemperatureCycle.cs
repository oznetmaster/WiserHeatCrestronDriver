// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text.Json;

namespace WiserHeatCrestronDriver.ControlProbe;

public enum RoomTemperatureAction
	{
	Raise, Lower, BoostOn, BoostOff
	}
public sealed record RoomTemperatureActivity (string Epoch, long Completed, int Pending);
public sealed record RoomTemperatureSnapshot (GatewayAwaySnapshot Gateway, int RoomId, RoomTemperatureActivity Activity,
	double HomeTarget, bool HomeBoost, bool UiMatches);
public sealed record RoomTemperatureResult (bool Passed, bool RestorationConfirmed, string Detail);

public interface IRoomTemperatureSession
	{
	Task<RoomTemperatureSnapshot> ReadAsync (CancellationToken token);
	Task RecordAsync (string phase, object value);
	Task InputAsync (RoomTemperatureAction action, CancellationToken token);
	Task RestoreAsync (int index, RoomTemperatureRestorePlan plan, JsonElement request, CancellationToken token);
	}

/// <summary>One native increase/decrease pair or Boost On/Off pair, with independent policy restoration.</summary>
public static class RoomTemperatureCycle
	{
	private static JsonElement Room (RoomTemperatureSnapshot snapshot) => RoomTemperatureRestoration.Room (snapshot.Gateway.Hub, snapshot.RoomId);
	private static bool Agrees (RoomTemperatureSnapshot snapshot) => snapshot.UiMatches && double.IsFinite (snapshot.HomeTarget) &&
		Math.Abs (snapshot.HomeTarget * 10 - Room (snapshot).GetProperty ("CurrentSetPoint").GetInt32 ()) < 0.001 &&
		snapshot.HomeBoost == (RoomTemperatureRestoration.Origin (Room (snapshot)) == "FromBoost");
	private static bool Restored (RoomTemperatureRestorePlan plan, RoomTemperatureSnapshot snapshot)
		{
		try
			{
			RoomTemperatureRestoration.RequireRestored (plan, Room (snapshot));
			}
		catch (InvalidDataException) { return false; }
		catch (NotSupportedException) { return false; }
		return Agrees (snapshot) && !snapshot.HomeBoost;
		}
	public static async Task<RoomTemperatureResult> RunAsync (IRoomTemperatureSession session, bool boost, TimeSpan timeout, CancellationToken token)
		{
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes (3))
			throw new ArgumentOutOfRangeException (nameof (timeout));
		RoomTemperatureSnapshot? original = null;
		RoomTemperatureRestorePlan? plan = null;
		int submitted = 0;
		bool attempted = false, passed = false, restored = true;
		string detail = "Preflight failed before any room control input.";
		Func<RoomTemperatureSnapshot, bool>? requested = null;
		DateTimeOffset requestedAfter = default;
		var elapsed = new Stopwatch ();
		void Guard (RoomTemperatureSnapshot value)
			{
			RoomTemperatureRestoration.RequireGuarded (plan!, original!.Gateway, value.Gateway);
			if (value.RoomId != original.RoomId || value.Activity.Epoch != original.Activity.Epoch || value.Activity.Pending < 0 ||
				value.Activity.Completed < original.Activity.Completed || value.Activity.Completed > original.Activity.Completed + submitted)
				throw new InvalidDataException ("Room identity, command lifetime or attribution changed.");
			}
		async Task<RoomTemperatureSnapshot> Wait (Func<RoomTemperatureSnapshot, bool> predicate, DateTimeOffset after, CancellationToken ct)
			{
			int matches = 0;
			var last = after;
			while (true)
				{
				ct.ThrowIfCancellationRequested ();
				var value = await session.ReadAsync (ct);
				Guard (value);
				if (value.Gateway.RefreshUtc < last)
					throw new InvalidDataException ("Refresh evidence moved backwards.");
				last = value.Gateway.RefreshUtc;
				bool ready = value.Activity.Pending == 0 && value.Activity.Completed == original!.Activity.Completed + submitted;
				matches = ready && value.Gateway.RefreshUtc > after && predicate (value) ? matches + 1 : 0;
				if (matches == 2)
					return value;
				await Task.Delay (25, ct);
				}
			}
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (timeout);
		try
			{
			original = await session.ReadAsync (deadline.Token);
			await session.RecordAsync ("preflight", new { Snapshot = original, Boost = boost });
			plan = RoomTemperatureRestoration.Capture (Room (original));
			if (!Guid.TryParseExact (original.Activity.Epoch, "N", out _) || original.Activity.Pending != 0 ||
				original.Activity.Completed < 0 || original.Activity.Completed > long.MaxValue - 2 || !Restored (plan, original))
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
			int delta = start <= 345 ? 5 : -5;
			RoomTemperatureAction[] actions = boost ? [RoomTemperatureAction.BoostOn, RoomTemperatureAction.BoostOff]
				: delta > 0 ? [RoomTemperatureAction.Raise, RoomTemperatureAction.Lower] : [RoomTemperatureAction.Lower, RoomTemperatureAction.Raise];
			foreach (var action in actions)
				{
				int target = submitted == 0 ? start + delta : start;
				requested = boost
					? action == RoomTemperatureAction.BoostOn
						? value => Agrees (value) && value.HomeBoost && Room (value).GetProperty ("OverrideTimeoutUnixTime").GetInt64 () > DateTimeOffset.UtcNow.ToUnixTimeSeconds ()
						: value => Restored (plan, value)
					: value => Agrees (value) && Room (value).GetProperty ("CurrentSetPoint").GetInt32 () == target &&
						(plan.Scheduled ? Room (value).GetProperty ("OverrideType").GetString () == "Manual"
							: RoomTemperatureRestoration.Origin (Room (value)) == "FromManualMode" && ScheduleObservation.ManualTarget (Room (value)) == target);
				await session.RecordAsync ("input-" + (submitted + 1) + "-intent", new
					{
					Action = action,
					Target = boost ? (int?)null : target,
					Snapshot = current
					});
				deadline.Token.ThrowIfCancellationRequested ();
				requestedAfter = current.Gateway.RefreshUtc;
				submitted++;
				attempted = true;
				restored = false;
				elapsed.Restart ();
				await session.InputAsync (action, deadline.Token);
				current = await Wait (requested, requestedAfter, deadline.Token);
				await session.RecordAsync ("input-" + submitted + "-observed", new
					{
					Snapshot = current,
					Seconds = elapsed.Elapsed.TotalSeconds
					});
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
			if (attempted && plan != null && original != null && requested != null)
				{
				using var cleanup = new CancellationTokenSource (timeout * 3);
				try
					{
					// Observe delivery before compensation, even if input acknowledgement was lost. Never repeat the input.
					var current = await Wait (requested, requestedAfter, cleanup.Token);
					if (!Restored (plan, current))
						{
						int index = 0;
						foreach (var request in plan.Requests)
							{
							bool cancel = request.GetProperty ("RequestOverride").GetProperty ("Type").GetString () == "None";
							bool Done (RoomTemperatureSnapshot value) => cancel ? Restored (plan, value)
								: Agrees (value) && ScheduleObservation.ManualTarget (Room (value)) == plan.ManualTarget && Room (value).GetProperty ("CurrentSetPoint").GetInt32 () == plan.ManualTarget;
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
							current = await Wait (Done, after, cleanup.Token);
							await session.RecordAsync ("restore-" + index + "-observed", new
								{
								Snapshot = current
								});
							index++;
							}
						}
					await session.RecordAsync ("restored", new
						{
						Snapshot = current
						});
					restored = true;
					detail = passed ? "Both UI inputs and original room policy were independently confirmed, with household settings preserved."
						: "The test failed; original room policy was restored without replaying the uncertain input.";
					}
				catch (Exception failure)
					{
					passed = false;
					detail = "Room restoration is unconfirmed. Retain reservations and reconcile the journal.";
					try
						{
						await session.RecordAsync ("recovery-required", new
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