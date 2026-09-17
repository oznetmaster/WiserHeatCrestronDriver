// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using CrestronHomeNUnit.Android;

using NUnit.Framework;

using WiserHeatApiV2;

using WiserHeatCrestronDriver.ControlProbe;

namespace WiserHeatCrestronDriver.AndroidTests;

public sealed partial class GatewayUiTests
	{
	private sealed partial record Settings
		{
		public bool AllowDeliberateModeInterruption { get; init; }
		}

	private sealed class ExpectedModeInterruptionException : Exception
		{
		public ExpectedModeInterruptionException () : base ("Deliberate interruption after independently observed Manual mode.") { }
		}

	private sealed class InterruptedModeSession (IScheduleControlSession inner) : IScheduleControlSession
		{
		public bool Interrupted { get; private set; }
		public int CompensationAttempts { get; private set; }
		public Task<ScheduleControlSnapshot> ReadAsync (CancellationToken token) => inner.ReadAsync (token);
		public Task SetManualTargetAsync (int target, CancellationToken token) => inner.SetManualTargetAsync (target, token);
		public async Task RecordAsync (string phase, ScheduleControlSnapshot snapshot)
			{
			await inner.RecordAsync (phase, snapshot);
			if (phase == "manual-observed")
				{
				await inner.RecordAsync ("expected-interruption-intent", snapshot);
				Interrupted = true;
				throw new ExpectedModeInterruptionException ();
				}
			}
		public Task SetAsync (bool enabled, bool recovery, CancellationToken token)
			{
			if (enabled)
				{
				if (!Interrupted || !recovery || CompensationAttempts != 0)
					throw new InvalidOperationException ("Expected exactly one declared configuration compensation after interruption.");
				CompensationAttempts++;
				}
			return inner.SetAsync (enabled, recovery, token);
			}
		}

	[Test, Category ("LiveControl"), Category ("LiveRecovery")]
	public async Task InterruptedManualModeTestRestoresAutoThroughConfiguration ()
		{
		Assert.That (_nameRestored && _roomStatePreserved, Is.True, "An earlier restoration needs reconciliation.");
		if (!_settings!.AllowDeliberateModeInterruption)
			Assert.Ignore ("Enable AllowDeliberateModeInterruption explicitly for the controlled recovery case.");
		if (_settings.ControlRooms.Length != 1 || string.IsNullOrWhiteSpace (_settings.ControlHubSettingsPath) || !Path.IsPathFullyQualified (_settings.ControlHubSettingsPath))
			throw new InvalidDataException ("One explicitly bound control room and private hub settings are required.");
		var control = _settings.ControlRooms[0];
		var binding = _settings.Rooms.Single (r => r.DeviceId == control.DeviceId);
		var settings = JsonSerializer.Deserialize<HubSettings> (File.ReadAllText (_settings.ControlHubSettingsPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
			?? throw new InvalidDataException ("Missing private hub settings.");
		if (string.IsNullOrWhiteSpace (settings.HubHost) || string.IsNullOrWhiteSpace (settings.Secret))
			throw new InvalidDataException ("Incomplete private hub settings.");
		string host = settings.HubHost.Trim ().ToLowerInvariant ();
		using var hub = new WiserRestController (new WiserConnection (host, settings.Secret));
		using var http = new HttpClient (new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds (15) };
		http.DefaultRequestHeaders.Add ("SECRET", settings.Secret);
		using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (8));
		async Task<ScheduleHubSnapshot> ReadHubSnapshotAsync (CancellationToken token)
			{
			async Task<JsonElement> Read (string endpoint)
				{
				AndroidWorkflowSession.VerifyContext (_session!.Context);
				using var response = await http.GetAsync ("http://" + host + "/data/v2/" + endpoint + "/", token);
				response.EnsureSuccessStatusCode ();
				using var document = JsonDocument.Parse (await response.Content.ReadAsStringAsync (token));
				return document.RootElement.Clone ();
				}
			var domain = await Read ("domain");
			return new (await Read ("schedules"), domain);
			}
		await _navigation!.RestoreHomeAsync (timeout.Token);
		var original = await ReadRoomAsync (binding, timeout.Token);
		string check = "wiser.room-" + binding.DeviceId.ToString (CultureInfo.InvariantCulture) + ".mode-recovery";
		var before = await ReadHubSnapshotAsync (timeout.Token);
		await using (var capture = new FileStream (Path.Combine (_session!.Context.EvidenceDirectory, check + ".hub-before.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
			{
			await JsonSerializer.SerializeAsync (capture, new { _session.Context.RunId, _session.Context.PackageSha256, Snapshot = before });
			capture.Flush (true);
			}
		ScheduleControlResult? observed = null;
		ScheduleHubSnapshot? after = null;
		Exception? operationFailure = null;
		try
			{
			await _navigation.InspectRoomExtensionPagesAsync (check, binding.RoomName, original.Name!, binding.PageTitle, async (pages, token) =>
				{
				await pages.OpenPageAsync (Text ("Open"), "Schedule", CrestronHomePages.Resource ("customdevices_toolbarClose"), token);
				var inner = new RoomControlSession (this, hub, http, host, binding, control, original, check);
				var interrupted = new InterruptedModeSession (inner);
				observed = await ScheduleControlCycle.RunAsync (interrupted, TimeSpan.FromSeconds (60), token, control.AllowManualTargetInitialization);
				await inner.SaveResultAsync (observed);
				Assert.Multiple (() =>
					{
					Assert.That (interrupted.Interrupted, Is.True, "The intended interruption must actually occur.");
					Assert.That (interrupted.CompensationAttempts, Is.EqualTo (1));
					Assert.That (observed.Passed, Is.False, "The interrupted operation must remain failed.");
					Assert.That (observed.RestorationConfirmed, Is.True, observed.Detail);
					Assert.That (observed.Detail, Is.EqualTo ("Failed during disable input or observation. Inspect private evidence."), "An additional unexpected recovery failure must not pass as the intended interruption.");
					});
				}, timeout.Token);
			}
		catch (Exception failure)
			{
			operationFailure = failure;
			throw;
			}
		finally
			{
			if (observed?.RestorationConfirmed == true)
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromMinutes (2));
				try
					{
					Assert.That (_navigation.HomeRestored, Is.True);
					_roomStatePreserved = false;
					after = await ReadHubSnapshotAsync (cleanup.Token);
					await using (var capture = new FileStream (Path.Combine (_session!.Context.EvidenceDirectory, check + ".hub-after.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
						{
						await JsonSerializer.SerializeAsync (capture, new { _session.Context.RunId, _session.Context.PackageSha256, Snapshot = after });
						capture.Flush (true);
						}
					var comparable = before;
					if (observed!.ManualTargetInitialized)
						{
						// The explicitly permitted inactive target is the only allowed persistent difference.
						var domain = JsonNode.Parse (before.Domain.GetRawText ())!;
						var room = domain["Room"]!.AsArray ().Single (r => r!["Name"]!.GetValue<string> () == control.HubRoomName)!;
						var actual = after.Domain.GetProperty ("Room").EnumerateArray ().Single (r => r.GetProperty ("id").GetInt32 () == room["id"]!.GetValue<int> ());
						room["ManualSetPoint"] = JsonNode.Parse (actual.GetProperty ("ManualSetPoint").GetRawText ());
						comparable = before with { Domain = JsonSerializer.SerializeToElement (domain) };
						}
					ScheduleSaveIsolation.RequireOriginal (comparable, after);
					_roomStatePreserved = true;
					}
				catch (Exception recoveryFailure) when (operationFailure != null)
					{
					throw new AggregateException ("Mode recovery assertion and complete-state verification both failed.", operationFailure, recoveryFailure);
					}
				}
			}
		Assert.That (_roomStatePreserved && after != null, Is.True);

		AndroidWorkflowSession.VerifyContext (_session!.Context);
		await using var receipt = new FileStream (Path.Combine (_session.Context.EvidenceDirectory, check + ".expected-recovery.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
		await JsonSerializer.SerializeAsync (receipt, new { _session.Context.RunId, _session.Context.PackageSha256, ExpectedInterruption = true,
			Result = observed, HomeRestored = true, Before = before, After = after,
			Scope = "Recovery after deliberate interruption; only an explicitly permitted inactive manual target may differ. No arbitrary outage or process-failure claim." });
		receipt.Flush (true);
		}
	}