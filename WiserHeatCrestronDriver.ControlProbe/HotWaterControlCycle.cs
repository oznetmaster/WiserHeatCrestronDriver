// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record HotWaterControlSnapshot (string PhysicalIdentity, string Lifetime, DateTimeOffset RefreshUtc,
	ScheduleHubSnapshot Hub, bool HomeOn, bool ActionEnabled);
public sealed record HotWaterControlResult (bool Passed, bool RestorationConfirmed, string Detail);
public interface IHotWaterControlSession
	{
	Task<HotWaterControlSnapshot> ReadAsync (CancellationToken token);
	/// <summary>Reads independent hub and processor state without requiring the Android UI.</summary>
	Task<HotWaterControlSnapshot> ReadForRecoveryAsync (CancellationToken token);
	Task VerifyRestoredUiAsync (HotWaterControlSnapshot snapshot, CancellationToken token);
	Task RecordAsync (string phase, object value);
	Task SetHotWaterAsync (bool enabled, CancellationToken token);
	Task RestoreAsync (int index, int controllerId, JsonElement request, CancellationToken token);
	}

/// <summary>Two observed UI transitions followed by independent restoration of the captured control policy.</summary>
public static class HotWaterControlCycle
	{
	private static JsonElement Water (HotWaterControlSnapshot snapshot) => snapshot.Hub.Domain.GetProperty ("HotWater").EnumerateArray ().Single ();
	private static bool On (JsonElement water, string key) => water.GetProperty (key).GetString () switch
		{ "On" => true, "Off" => false, _ => throw new InvalidDataException ("Hot-water target is not On or Off.") };
	public static void RequireGuarded (HotWaterControlSnapshot original, HotWaterControlSnapshot current)
		{
		var before = new GatewayAwaySnapshot (original.PhysicalIdentity, original.Lifetime, original.RefreshUtc, original.Hub, false, true);
		var after = new GatewayAwaySnapshot (current.PhysicalIdentity, current.Lifetime, current.RefreshUtc, current.Hub, false, true);
		if (GatewayAwayCycle.IsAway (before) != GatewayAwayCycle.IsAway (after)) throw new InvalidDataException ("Whole-house Away state changed during hot-water control.");
		var water = Water (current);
		if (water.GetProperty ("id").GetInt32 () != Water (original).GetProperty ("id").GetInt32 ()) throw new InvalidDataException ("Hot-water controller identity changed.");
		var adjusted = JsonNode.Parse (original.Hub.Domain.GetRawText ())!;
		// Only these fields may be changed by this test's declared Manual/None requests.
		// All other gateway, schedule, room and override guards remain in force.
		foreach (string name in new[] { "OverrideType", "OverrideWaterHeatingState", "OverrideTimeoutUnixTime" })
			{
			var node = adjusted["HotWater"]![0]!.AsObject ();
			if (water.TryGetProperty (name, out var value)) node[name] = JsonNode.Parse (value.GetRawText ());
			else node.Remove (name);
			}
		before = before with { Hub = before.Hub with { Domain = JsonSerializer.SerializeToElement (adjusted) } };
		GatewayAwayCycle.RequirePreserved (before, after);
		if (water.TryGetProperty ("OverrideType", out var type) && type.GetString () is not ("None" or "Manual"))
			throw new InvalidDataException ("An unrelated hot-water override appeared.");
		if (water.TryGetProperty ("OverrideTimeoutUnixTime", out var deadline) && (!deadline.TryGetInt64 (out var seconds) || seconds < 0))
			throw new InvalidDataException ("The observed hot-water deadline is malformed.");
		if (water.GetProperty ("HotWaterDescription").GetString () is not ("FromSchedule" or "FromManualMode" or "FromManualOverride"))
			throw new InvalidDataException ("Hot water is controlled by an unexpected source.");
		}
	private static bool ManualTarget (HotWaterControlSnapshot snapshot, bool desired)
		{
		var water = Water (snapshot);
		return water.GetProperty ("HotWaterDescription").GetString () is "FromManualMode" or "FromManualOverride" &&
			On (water, "OverrideWaterHeatingState") == desired && On (water, "WaterHeatingState") == desired &&
			On (water, "HotWaterRelayState") == desired && snapshot.HomeOn == desired && snapshot.ActionEnabled;
		}
	private static bool Restored (HotWaterRestorePlan plan, HotWaterControlSnapshot snapshot)
		{
		try { HotWaterRestoration.RequireRestored (plan, snapshot.Hub.Domain); }
		catch (InvalidDataException) { return false; }
		catch (NotSupportedException) { return false; }
		return snapshot.ActionEnabled && snapshot.HomeOn == On (Water (snapshot), "WaterHeatingState");
		}
	public static async Task<HotWaterControlResult> RunAsync (IHotWaterControlSession session, TimeSpan timeout, CancellationToken token)
		{
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes (2)) throw new ArgumentOutOfRangeException (nameof (timeout));
		HotWaterControlSnapshot? original = null;
		HotWaterRestorePlan? plan = null;
		bool attempted = false, passed = false, restored = true, requested = false;
		DateTimeOffset requestedAfter = default;
		string detail = "Preflight failed before hot-water input.";
		var elapsed = new Stopwatch ();
		async Task<HotWaterControlSnapshot> Wait (Func<HotWaterControlSnapshot, bool> ready, DateTimeOffset after, CancellationToken cancellation, bool recovery = false)
			{
			int matches = 0; var last = after;
			while (true)
				{
				cancellation.ThrowIfCancellationRequested ();
				var current = recovery ? await session.ReadForRecoveryAsync (cancellation) : await session.ReadAsync (cancellation);
				RequireGuarded (original!, current);
				if (current.RefreshUtc < last) throw new InvalidDataException ("Gateway refresh evidence moved backwards.");
				last = current.RefreshUtc;
				matches = current.RefreshUtc > after && ready (current) ? matches + 1 : 0;
				if (matches == 2) return current;
				await Task.Delay (25, cancellation);
				}
			}
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (timeout);
		try
			{
			original = await session.ReadAsync (deadline.Token);
			plan = HotWaterRestoration.Capture (original.Hub.Domain);
			RequireGuarded (original, original);
			if (!Restored (plan, original)) throw new InvalidDataException ("Hub and idle Home state must agree before input.");
			await session.RecordAsync ("original", new { Snapshot = original, Plan = plan });
			var current = await session.ReadAsync (deadline.Token);
			RequireGuarded (original, current);
			if (!Restored (plan, current) || On (Water (current), "WaterHeatingState") != plan.OriginallyOn)
				throw new InvalidDataException ("Starting hot-water state changed.");
			int step = 0;
			foreach (bool enabled in new[] { !plan.OriginallyOn, plan.OriginallyOn })
				{
				await session.RecordAsync ("ui-" + (++step) + "-intent", new { Enabled = enabled, Snapshot = current });
				deadline.Token.ThrowIfCancellationRequested ();
				requested = enabled; requestedAfter = current.RefreshUtc;
				attempted = true; restored = false;
				elapsed.Restart ();
				await session.SetHotWaterAsync (enabled, deadline.Token);
				current = await Wait (s => ManualTarget (s, enabled), requestedAfter, deadline.Token);
				await session.RecordAsync ("ui-" + step + "-observed", new { Snapshot = current, Seconds = elapsed.Elapsed.TotalSeconds });
				}
			passed = true;
			}
		catch (Exception failure)
			{
			detail = "Hot-water control or observation failed; inspect private evidence.";
			try { await session.RecordAsync ("failure", new { Exception = failure.ToString () }); } catch { }
			}
		finally
			{
			if (attempted && plan != null && original != null)
				{
				using var cleanup = new CancellationTokenSource (timeout * 3);
				try
					{
					// Establish delivery of the last issued input before compensating. Never replay it.
					var current = await Wait (s => ManualTarget (s, requested), requestedAfter, cleanup.Token, recovery: true);
					int index = 0;
					foreach (var request in plan.Requests)
						{
						bool cancel = request.GetProperty ("RequestOverride").GetProperty ("Type").GetString () == "None";
						bool AlreadyDone (HotWaterControlSnapshot s) => cancel ? Restored (plan, s) : ManualTarget (s, plan.StoredOverrideOn ?? throw new InvalidDataException ("Missing original manual target."));
						if (AlreadyDone (current))
							{
							await session.RecordAsync ("restore-" + index + "-already-observed", new { Snapshot = current, Request = request });
							index++; continue;
							}
						await session.RecordAsync ("restore-" + index + "-intent", new { Request = request, Snapshot = current });
						var after = current.RefreshUtc;
						elapsed.Restart ();
						try { await session.RestoreAsync (index, plan.Id, request, cleanup.Token); }
						catch (Exception failure)
							{
							passed = false;
							await session.RecordAsync ("restore-" + index + "-input-error", new { Exception = failure.ToString () });
							}
						current = await Wait (AlreadyDone, after, cleanup.Token, recovery: true);
						await session.RecordAsync ("restore-" + index + "-observed", new { Snapshot = current, Seconds = elapsed.Elapsed.TotalSeconds });
						index++;
						}
					var final = await Wait (s => Restored (plan, s), original.RefreshUtc, cleanup.Token, recovery: true);
					await session.RecordAsync ("restored", new { Snapshot = final });
					restored = true;
					try { await session.VerifyRestoredUiAsync (final, cleanup.Token); }
					catch (Exception failure)
						{
						passed = false;
						try { await session.RecordAsync ("restored-ui-failed", new { Exception = failure.ToString () }); } catch { }
						}
					detail = passed ? "Both UI states and the original hot-water control policy were observed, with guarded hub settings preserved." : "The test failed; original hot-water policy was independently restored without replay.";
					}
				catch (Exception failure)
					{
					passed = false; detail = "Hot-water restoration unconfirmed. Retain reservations and reconcile private evidence.";
					try { await session.RecordAsync ("recovery-required", new { Exception = failure.ToString () }); } catch { }
					}
				}
			}
		return new (passed && restored, restored, detail);
		}
	}