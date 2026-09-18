// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text.Json;

namespace WiserHeatCrestronDriver.ControlProbe;

public sealed record GatewayAwaySnapshot (string PhysicalIdentity, string Lifetime, DateTimeOffset RefreshUtc,
	ScheduleHubSnapshot Hub, bool HomeAway, bool ActionEnabled);
public sealed record GatewayAwayResult (bool Passed, bool RestorationConfirmed, string Detail);

public interface IGatewayAwaySession
	{
	Task<GatewayAwaySnapshot> ReadAsync (CancellationToken token);
	Task RecordAsync (string phase, object value);
	Task SetAwayAsync (bool enabled, bool recovery, CancellationToken token);
	}

/// <summary>Observed Away transitions, preserving schedules, room settings and existing hot-water overrides.</summary>
public static class GatewayAwayCycle
	{
	public static bool IsAway (GatewayAwaySnapshot snapshot) => snapshot.Hub.Domain.GetProperty ("System").GetProperty ("OverrideType").GetString () switch
		{
		"None" => false,
		"Away" => true,
		_ => throw new InvalidDataException ("The system has an unsupported override; no replacement is permitted.")
		};

	public static void RequirePreserved (GatewayAwaySnapshot original, GatewayAwaySnapshot current)
		{
		if (string.IsNullOrWhiteSpace (original.PhysicalIdentity) || (!Guid.TryParse (original.Lifetime, out var lifetime) || lifetime == Guid.Empty) ||
			original.RefreshUtc == default || current.PhysicalIdentity != original.PhysicalIdentity ||
			current.Lifetime != original.Lifetime || current.RefreshUtc < original.RefreshUtc)
			throw new InvalidDataException ("Gateway identity, lifetime or refresh evidence changed.");
		_ = IsAway (original);
		_ = IsAway (current);
		ScheduleSaveIsolation.RequireOriginal (original.Hub, current.Hub);
		string[] settings = ["AwayModeAffectsHotWater", "AwayModeSetPointLimit", "ComfortModeEnabled", "EcoModeEnabled",
			"TimeZoneOffset", "AutomaticDaylightSaving", "ValveProtectionEnabled", "DegradedModeSetpointThreshold"];
		JsonElement Project (JsonElement value, Func<string, bool> include) => JsonSerializer.SerializeToElement (
			value.EnumerateObject ().Where (p => include (p.Name)).ToDictionary (p => p.Name, p => p.Value));
		bool SystemField (string name) => settings.Contains (name, StringComparer.Ordinal) ||
			name is not ("OverrideType" or "UserOverridesActive") && (name.Contains ("Override", StringComparison.OrdinalIgnoreCase) || name.Contains ("Boost", StringComparison.OrdinalIgnoreCase));
		bool WaterField (string name) => name is "id" or "DeviceId" or "ScheduleId" or "Mode" or "AwayModeSuppressed" ||
			name.Contains ("Override", StringComparison.OrdinalIgnoreCase) || name.Contains ("Boost", StringComparison.OrdinalIgnoreCase);
		JsonElement WaterSettings (JsonElement value)
			{
			var entries = value.EnumerateArray ().ToArray ();
			int[] ids = entries.Select (v => v.GetProperty ("id").GetInt32 ()).ToArray ();
			if (ids.Any (id => id <= 0) || ids.Distinct ().Count () != ids.Length)
				throw new InvalidDataException ("Hot-water controller identities must be unique.");
			return JsonSerializer.SerializeToElement (entries.OrderBy (v => v.GetProperty ("id").GetInt32 ()).Select (v =>
				{
				var fields = v.EnumerateObject ().Where (p => WaterField (p.Name)).ToDictionary (p => p.Name, p => p.Value);
				// The hub omits false on some reads and explicitly returns it after clearing an override.
				if (!fields.ContainsKey ("AwayModeSuppressed")) fields["AwayModeSuppressed"] = JsonSerializer.SerializeToElement (false);
				return fields;
				}));
			}
		foreach (var entry in new[] { (Name: "System", Filter: (Func<string, bool>)SystemField), (Name: "HotWater", Filter: (Func<string, bool>)WaterField) })
			{
			bool before = original.Hub.Domain.TryGetProperty (entry.Name, out var oldValue);
			bool now = current.Hub.Domain.TryGetProperty (entry.Name, out var value);
			if (before != now || before && !JsonElement.DeepEquals (
				entry.Name == "HotWater" ? WaterSettings (oldValue) : Project (oldValue, entry.Filter),
				entry.Name == "HotWater" ? WaterSettings (value) : Project (value, entry.Filter)))
				throw new InvalidDataException ("Gateway settings or existing hot-water overrides changed.");
			}
		// Keep per-room overrides, including their original absolute deadlines, intact.
		JsonElement RoomOverrides (JsonElement domain) => JsonSerializer.SerializeToElement (domain.GetProperty ("Room").EnumerateArray ()
			.OrderBy (r => r.GetProperty ("id").GetInt32 ()).Select (r => Project (r, name => name == "id" ||
				name.Contains ("Override", StringComparison.OrdinalIgnoreCase) || name.Contains ("Boost", StringComparison.OrdinalIgnoreCase))));
		if (!JsonElement.DeepEquals (RoomOverrides (original.Hub.Domain), RoomOverrides (current.Hub.Domain)))
			throw new InvalidDataException ("A room override changed during gateway control.");
		}

	public static async Task<GatewayAwayResult> RunAsync (IGatewayAwaySession session, TimeSpan timeout, CancellationToken token)
		{
		if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes (2)) throw new ArgumentOutOfRangeException (nameof (timeout));
		GatewayAwaySnapshot? original = null;
		bool attempted = false, passed = false, restored = true;
		string detail = "Preflight failed before any gateway control was sent.";
		var elapsed = new Stopwatch ();
		async Task<GatewayAwaySnapshot> Wait (bool enabled, DateTimeOffset after, CancellationToken cancellation)
			{
			int matches = 0;
			DateTimeOffset last = after;
			while (true)
				{
				cancellation.ThrowIfCancellationRequested ();
				var current = await session.ReadAsync (cancellation);
				RequirePreserved (original!, current);
				if (current.RefreshUtc < last) throw new InvalidDataException ("Gateway refresh moved backwards.");
				last = current.RefreshUtc;
				matches = current.RefreshUtc > after && current.ActionEnabled && current.HomeAway == enabled && IsAway (current) == enabled ? matches + 1 : 0;
				if (matches == 2) return current;
				await Task.Delay (25, cancellation);
				}
			}
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (token);
		deadline.CancelAfter (timeout);
		try
			{
			original = await session.ReadAsync (deadline.Token);
			RequirePreserved (original, original);
			bool before = IsAway (original);
			if (!original.ActionEnabled || original.HomeAway != before) throw new InvalidDataException ("Hub and idle Home state must agree.");
			await session.RecordAsync ("original", original);
			var stable = await session.ReadAsync (deadline.Token);
			RequirePreserved (original, stable);
			if (!stable.ActionEnabled || IsAway (stable) != before || stable.HomeAway != before)
				throw new InvalidDataException ("Starting gateway state changed.");
			await session.RecordAsync ("change-intent", new { Enabled = !before, Snapshot = stable });
			deadline.Token.ThrowIfCancellationRequested ();
			attempted = true;
			restored = false;
			elapsed.Start ();
			await session.SetAwayAsync (!before, false, deadline.Token);
			var changed = await Wait (!before, stable.RefreshUtc, deadline.Token);
			await session.RecordAsync ("changed", new { Snapshot = changed, Seconds = elapsed.Elapsed.TotalSeconds });
			passed = true;
			}
		catch (Exception failure)
			{
			detail = "Gateway control or observation failed; inspect private evidence.";
			try { await session.RecordAsync ("failure", new { Exception = failure.ToString () }); } catch { }
			}
		finally
			{
			if (attempted && original != null)
				{
				using var cleanup = new CancellationTokenSource (timeout);
				try
					{
					// An unchanged state after a lost tap is not evidence of non-delivery.
					// Only an independently observed transition permits the distinct restoring tap.
					var changed = await Wait (!IsAway (original), original.RefreshUtc, cleanup.Token);
					await session.RecordAsync ("restore-intent", new { Enabled = IsAway (original), Snapshot = changed, Recovery = !passed });
					elapsed.Restart ();
					try { await session.SetAwayAsync (IsAway (original), !passed, cleanup.Token); }
					catch (Exception failure)
						{
						passed = false;
						await session.RecordAsync ("restore-input-error", new { Exception = failure.ToString () });
						}
					var final = await Wait (IsAway (original), changed.RefreshUtc, cleanup.Token);
					await session.RecordAsync ("restored", new { Snapshot = final, Seconds = elapsed.Elapsed.TotalSeconds });
					restored = true;
					detail = passed ? "Both Away transitions were observed and original guarded hub state restored." : "The test failed; original guarded hub state was independently restored without replay.";
					}
				catch (Exception failure)
					{
					passed = false;
					detail = "Restoration unconfirmed. Retain reservations and reconcile the private gateway journal.";
					try { await session.RecordAsync ("recovery-required", new { Exception = failure.ToString () }); } catch { }
					}
				}
			}
		return new (passed && restored, restored, detail);
		}
	}