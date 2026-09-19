// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.Text.Json;
using System.Text.Json.Nodes;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class RoomTemperatureCycleTests
	{
	private static RoomTemperatureSnapshot Snapshot (bool manual = false, bool absent = false)
		{
		var room = JsonSerializer.SerializeToNode (new
			{
			id = 9,
			Name = "Room",
			Mode = manual ? "Manual" : "Auto",
			ScheduleId = 10,
			ManualSetPoint = manual ? 180 : 170,
			CurrentSetPoint = 180,
			CalculatedTemperature = 180,
			ClimateCapabilities = new { SetpointStep = 5, MaximumHeatSetpoint = 300 },
			ScheduledSetPoint = 180,
			SetpointOrigin = manual ? "FromManualMode" : "FromSchedule"
			})!;
		if (absent)
			room.AsObject ().Remove ("ManualSetPoint");
		var domain = new JsonObject
			{
			["Room"] = new JsonArray (room, JsonSerializer.SerializeToNode (new { id = 8, Name = "Other", Mode = "Auto", ScheduleId = 11, ManualSetPoint = 190 })),
			["System"] = JsonSerializer.SerializeToNode (new { OverrideType = "None", AwayModeSetPointLimit = 125 }),
			["HotWater"] = JsonSerializer.SerializeToNode (new[] { new { id = 1, Mode = "Auto", ScheduleId = 1000 } })
			};
		return new (new ("test-hub", Guid.NewGuid ().ToString (), DateTimeOffset.UtcNow,
			new (JsonSerializer.SerializeToElement (new
				{
				Heating = Array.Empty<object> (),
				OnOff = Array.Empty<object> ()
				}), JsonSerializer.SerializeToElement (domain)), false, true),
			9, new (Guid.NewGuid ().ToString ("N"), 0, 0), 18, false, true) { BoostSettings = new (2, 60) };
		}
	private static RoomTemperatureSnapshot Edit (RoomTemperatureSnapshot value, Action<JsonNode> edit)
		{
		var domain = JsonNode.Parse (value.Gateway.Hub.Domain.GetRawText ())!;
		edit (domain);
		return value with
			{
			Gateway = value.Gateway with
				{
				Hub = value.Gateway.Hub with
					{
					Domain = JsonSerializer.SerializeToElement (domain)
					}
				}
			};
		}
	private sealed class Session (bool manual = false, bool absent = false) : IRoomTemperatureSession
		{
		public RoomTemperatureSnapshot State = Snapshot (manual, absent);
		private JsonElement? _originalRoom;
		public List<RoomTemperatureAction> Inputs = [];
		public List<int> Targets = [];
		public List<int> Restores = [];
		public List<string> Records = [];
		public string? Behavior;
		public string? FailRecord;
		public Action? BeforeRestoration;
		public Action? BeforeRead;
		public bool TransientUiMismatch;
		private int _postInputReads;
		public List<int> ConfirmationReadCounts = [];
		public CancellationTokenSource? CancelAfterInput;
		public Task<RoomTemperatureSnapshot> ReadAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			BeforeRead?.Invoke ();
			if (Behavior == "capture-throws" && Inputs.Count != 0)
				throw new IOException ("Synthetic unavailable Android capture.");
			if (TransientUiMismatch && Inputs.Count != 0 && ++_postInputReads == 2)
				return Task.FromResult (State with { UiMatches = false });
			return Task.FromResult (State);
			}
		public Task<RoomTemperatureSnapshot> ReadRestorationAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			return Task.FromResult (State);
			}
		public Task RecordAsync (string phase, object value)
			{
			if (FailRecord == phase)
				throw new IOException ("journal failed");
			if (phase == "restore-0-intent") BeforeRestoration?.Invoke ();
			Records.Add (phase);
			if (phase.StartsWith ("input-", StringComparison.Ordinal) && phase.EndsWith ("-observed", StringComparison.Ordinal))
				ConfirmationReadCounts.Add (_postInputReads);
			return Task.CompletedTask;
			}
		private double Displayed (int raw) => raw == -200 ? -20 : State.TemperatureUnits == "Fahrenheit" ? raw * 0.18d + 32d : raw / 10d;
		private void RestoreRoom ()
			{
			State = Edit (State, d => d["Room"]![0] = JsonNode.Parse (_originalRoom!.Value.GetRawText ())) with
				{
				HomeTarget = Displayed (_originalRoom!.Value.GetProperty ("CurrentSetPoint").GetInt32 ()),
				HomeBoost = false
				};
			}
		public Task InputAsync (RoomTemperatureAction action, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			_originalRoom ??= RoomTemperatureRestoration.Room (State.Gateway.Hub, 9);
			Inputs.Add (action);
			_postInputReads = 0;
			if (Behavior == "not-delivered")
				throw new IOException ("uncertain input");
			if (action == RoomTemperatureAction.BoostOff)
				RestoreRoom ();
			else
				{
				bool boost = action == RoomTemperatureAction.BoostOn;
				int target = action == RoomTemperatureAction.PrepareOff ? -200 : action == RoomTemperatureAction.ResumeHeating ? 50 :
					RoomTemperatureRestoration.Room (State.Gateway.Hub, 9).GetProperty ("CurrentSetPoint").GetInt32 () + (boost ? (int)(State.BoostSettings!.DeltaCelsius * 10) : action == RoomTemperatureAction.Raise ? 5 : -5);
				if (boost && Behavior == "wrong-boost-target") target += 5;
				State = Edit (State, d =>
					{
						var room = d["Room"]![0]!;
						room["CurrentSetPoint"] = target;
						// The real hub also reports FromBoost for a Manual override of an Auto schedule.
						room["SetpointOrigin"] = boost || !manual ? "FromBoost" : "FromManualMode";
						room["OverrideType"] = boost && Behavior == "wrong-boost-type" ? "None" : "Manual";
						room["OverrideSetpoint"] = target;
						room["OverrideTimeoutUnixTime"] = DateTimeOffset.UtcNow.AddMinutes (boost ? State.BoostSettings!.DurationMinutes + (Behavior == "wrong-boost-duration" ? 15 : 0) : 60).ToUnixTimeSeconds ();
						if (manual && !boost)
							room["ManualSetPoint"] = target;
					}) with
					{
					HomeTarget = Displayed (target),
					HomeBoost = boost || !manual
					};
				}
			State = State with
				{
				Activity = State.Activity with
					{
					Completed = State.Activity.Completed + (action == RoomTemperatureAction.PrepareOff ? 0 : 1)
					},
				Gateway = State.Gateway with
					{
					RefreshUtc = State.Gateway.RefreshUtc.AddSeconds (1)
					}
				};
			if (Behavior == "units-change")
				State = State with { TemperatureUnits = "Celsius", HomeTarget = RoomTemperatureRestoration.Room (State.Gateway.Hub, 9).GetProperty ("CurrentSetPoint").GetInt32 () / 10d };
			if (Behavior == "foreign")
				State = Edit (State, d => d["Room"]![1]!["ScheduleId"] = 99);
			if (Behavior == "away")
				State = Edit (State, d => d["System"]!["OverrideType"] = "Away");
			if (Behavior == "restart")
				State = State with
					{
					Activity = State.Activity with
						{
						Epoch = Guid.NewGuid ().ToString ("N")
						}
					};
			if (Behavior == "concurrent")
				State = State with
					{
					Activity = State.Activity with
						{
						Completed = State.Activity.Completed + 1
						}
					};
			if (Behavior == "ui-disagrees")
				State = State with
					{
					UiMatches = false
					};
			if (Behavior == "stale")
				State = State with
					{
					Gateway = State.Gateway with
						{
						RefreshUtc = State.Gateway.RefreshUtc.AddSeconds (-1)
						}
					};
			Targets.Add (RoomTemperatureRestoration.Room (State.Gateway.Hub, 9).GetProperty ("CurrentSetPoint").GetInt32 ());
			if (Behavior == "lost-input" && Inputs.Count == 1 || Behavior == "lost-later-input" && Inputs.Count == 3)
				throw new IOException ("input acknowledgement lost");
			CancelAfterInput?.Cancel ();
			return Task.CompletedTask;
			}
		public Task RestoreAsync (int index, RoomTemperatureRestorePlan plan, JsonElement request, RoomTemperatureSnapshot expected, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			RoomTemperatureCycle.RequireRestorationUnchanged (plan, expected, State);
			Restores.Add (index);
			Assert.That (Restores.Distinct ().Count (), Is.EqualTo (Restores.Count), "Never repeat compensation.");
			Assert.That (JsonElement.DeepEquals (request, plan.Requests[index]), Is.True);
			RestoreRoom ();
			State = State with
				{
				Gateway = State.Gateway with
					{
					RefreshUtc = State.Gateway.RefreshUtc.AddSeconds (1)
					}
				};
			if (Behavior == "lost-restore")
				throw new IOException ("restore acknowledgement lost");
			return Task.CompletedTask;
			}
		}
	private static Task<RoomTemperatureResult> Run (Session session, bool boost = false) => RoomTemperatureCycle.RunAsync (session, boost, TimeSpan.FromMilliseconds (250), CancellationToken.None);
	[TestCase (false), TestCase (true)]
	public async Task ExternalRoomTargetAfterFinalObservationIsNotOverwrittenByRestoration (bool manual)
		{
		var session = new Session (manual) { Behavior = "lost-input" };
		session.BeforeRestoration = () => session.State = Edit (session.State, d =>
			{
			d["Room"]![0]![manual ? "ManualSetPoint" : "OverrideSetpoint"] = 220;
			d["Room"]![0]!["CurrentSetPoint"] = 220;
			}) with { HomeTarget = 22 };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs, Has.Count.EqualTo (1));
		Assert.That (session.Restores, Is.Empty, "Do not overwrite a household target changed after the compensation observation.");
		Assert.That (RoomTemperatureRestoration.Room (session.State.Gateway.Hub, 9).GetProperty ("CurrentSetPoint").GetInt32 (), Is.EqualTo (220));
		Assert.That (session.Records, Does.Contain ("recovery-required"));
		}
	[TestCase (false, false, false), TestCase (false, false, true), TestCase (false, true, false), TestCase (false, true, true)]
	[TestCase (true, false, false), TestCase (true, false, true), TestCase (true, true, false), TestCase (true, true, true)]
	public async Task BoundaryWalkUsesOnlyConfirmedHalfDegreeInputsAndRestoresPolicy (bool manual, bool fahrenheit, bool maximum)
		{
		var session = new Session (manual);
		if (fahrenheit)
			session.State = session.State with { TemperatureUnits = "Fahrenheit", HomeTarget = 64.4 };
		var original = session.State;
		var result = await RoomTemperatureCycle.RunBoundaryAsync (session, maximum, TimeSpan.FromSeconds (2), CancellationToken.None);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Targets.Last (), Is.EqualTo (maximum ? 300 : 50));
		Assert.That (session.Targets, Has.All.InRange (50, 300));
		Assert.That (session.Inputs, Has.All.EqualTo (maximum ? RoomTemperatureAction.Raise : RoomTemperatureAction.Lower));
		Assert.That (session.Inputs.Count, Is.EqualTo (maximum ? 24 : 26));
		Assert.That (session.Records, Does.Contain ("boundary-observed"));
		Assert.That (session.Records.Count (p => p.StartsWith ("input-", StringComparison.Ordinal) && p.EndsWith ("-observed", StringComparison.Ordinal)), Is.EqualTo (session.Inputs.Count));
		RoomTemperatureRestoration.RequireRestored (RoomTemperatureRestoration.Capture (RoomTemperatureRestoration.Room (original.Gateway.Hub, 9)), RoomTemperatureRestoration.Room (session.State.Gateway.Hub, 9));
		}
	[TestCase (false, false), TestCase (false, true), TestCase (true, false), TestCase (true, true)]
	public async Task BoundaryStartingAtLimitUsesInwardStepOrFullSpanWithoutOutOfRangeInput (bool maximum, bool opposite)
		{
		int start = maximum != opposite ? 300 : 50;
		var session = new Session ();
		session.State = Edit (session.State, d =>
			{
			d["Room"]![0]!["CurrentSetPoint"] = start;
			d["Room"]![0]!["ScheduledSetPoint"] = start;
			}) with { HomeTarget = start / 10d };
		var result = await RoomTemperatureCycle.RunBoundaryAsync (session, maximum, TimeSpan.FromSeconds (2), CancellationToken.None);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (opposite ? 50 : 2));
		Assert.That (session.Targets, Has.All.InRange (50, 300));
		Assert.That (session.Targets.Last (), Is.EqualTo (maximum ? 300 : 50));
		Assert.That (session.State.HomeTarget, Is.EqualTo (start / 10d));
		}
	[TestCase (false), TestCase (true)]
	public async Task UncertainThirdBoundaryInputStopsSequenceAndRestoresWithoutReplay (bool maximum)
		{
		var session = new Session { Behavior = "lost-later-input" };
		var result = await RoomTemperatureCycle.RunBoundaryAsync (session, maximum, TimeSpan.FromSeconds (2), CancellationToken.None);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (3));
		Assert.That (session.Restores, Is.EqualTo (new[] { 0 }));
		Assert.That (session.Records, Does.Not.Contain ("boundary-observed"));
		Assert.That (session.State.HomeTarget, Is.EqualTo (18));
		}
	[Test]
	public async Task BoundaryRecordFailureStillRestoresButDoesNotPass ()
		{
		var session = new Session { FailRecord = "boundary-observed" };
		var result = await RoomTemperatureCycle.RunBoundaryAsync (session, true, TimeSpan.FromSeconds (2), CancellationToken.None);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (24));
		Assert.That (session.State.HomeTarget, Is.EqualTo (18));
		}
	[TestCase (false), TestCase (true)]
	public async Task NextIntentWriteFailureRestoresLastDeliveredInput (bool boundary)
		{
		var session = new Session { FailRecord = boundary ? "input-4-intent" : "input-2-intent" };
		var result = boundary
			? await RoomTemperatureCycle.RunBoundaryAsync (session, true, TimeSpan.FromSeconds (2), CancellationToken.None)
			: await RoomTemperatureCycle.RunAsync (session, false, TimeSpan.FromSeconds (2), CancellationToken.None);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (boundary ? 3 : 1));
		Assert.That (session.Restores, Is.EqualTo (new[] { 0 }));
		Assert.That (session.State.HomeTarget, Is.EqualTo (18));
		}
	[Test]
	public async Task BoundaryOffGridStartingTargetIsRejectedBeforeInput ()
		{
		var session = new Session ();
		session.State = Edit (session.State, d =>
			{
			d["Room"]![0]!["CurrentSetPoint"] = 182;
			d["Room"]![0]!["ScheduledSetPoint"] = 182;
			}) with { HomeTarget = 18.2 };
		var result = await RoomTemperatureCycle.RunBoundaryAsync (session, true, TimeSpan.FromSeconds (2), CancellationToken.None);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		}
	[TestCase (false, false), TestCase (false, true), TestCase (true, false), TestCase (true, true)]
	public async Task BothInputsRestoreOriginalPolicyAndTarget (bool manual, bool boost)
		{
		var session = new Session (manual);
		var original = session.State;
		var result = await Run (session, boost);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (2));
		foreach (int input in new[] { 1, 2 })
			{
			Assert.That (session.Records.Count (phase => phase == "input-" + input + "-first-match"), Is.EqualTo (1));
			Assert.That (session.Records.IndexOf ("input-" + input + "-first-match"), Is.LessThan (session.Records.IndexOf ("input-" + input + "-observed")));
			}
		Assert.That (session.Restores.Count, Is.EqualTo (boost ? 0 : manual ? 2 : 1));
		RoomTemperatureRestoration.RequireRestored (RoomTemperatureRestoration.Capture (RoomTemperatureRestoration.Room (original.Gateway.Hub, 9)), RoomTemperatureRestoration.Room (session.State.Gateway.Hub, 9));
		}
	[Test]
	public async Task TransientMismatchRetainsOneFirstMatchButStillRequiresStableConfirmation ()
		{
		var session = new Session { TransientUiMismatch = true };
		var result = await RoomTemperatureCycle.RunAsync (session, false, TimeSpan.FromSeconds (2), CancellationToken.None);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		foreach (int input in new[] { 1, 2 })
			Assert.That (session.Records.Count (phase => phase == "input-" + input + "-first-match"), Is.EqualTo (1));
		Assert.That (session.Inputs.Count, Is.EqualTo (2), "Observation mismatch must never replay input.");
		Assert.That (session.ConfirmationReadCounts, Has.All.GreaterThanOrEqualTo (4), "A first match followed by a mismatch requires two fresh matches before confirmation.");
		}
	[TestCase (false), TestCase (true)]
	public async Task FirstMatchRecordFailureStillRestoresWithoutAnotherInput (bool boost)
		{
		var session = new Session { FailRecord = "input-1-first-match" };
		var original = session.State;
		var result = await Run (session, boost);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (1));
		Assert.That (session.Records, Does.Not.Contain ("input-1-observed"));
		RoomTemperatureRestoration.RequireRestored (RoomTemperatureRestoration.Capture (RoomTemperatureRestoration.Room (original.Gateway.Hub, 9)), RoomTemperatureRestoration.Room (session.State.Gateway.Hub, 9));
		}
	[TestCase (false, false), TestCase (false, true), TestCase (true, false), TestCase (true, true)]
	public async Task FahrenheitInputsMatchRawCelsiusAndRestoreOriginalPolicy (bool manual, bool boost)
		{
		var session = new Session (manual);
		session.State = session.State with { TemperatureUnits = "Fahrenheit", HomeTarget = 64.4 };
		var original = session.State;
		var result = await Run (session, boost);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (2));
		Assert.That (session.State.HomeTarget, Is.EqualTo (64.4).Within (0.00001));
		RoomTemperatureRestoration.RequireRestored (RoomTemperatureRestoration.Capture (RoomTemperatureRestoration.Room (original.Gateway.Hub, 9)), RoomTemperatureRestoration.Room (session.State.Gateway.Hub, 9));
		}

	[Test]
	public async Task FahrenheitUpperLimitUsesLowerFirst ()
		{
		var session = new Session ();
		session.State = Edit (session.State, d =>
			{
			d["Room"]![0]!["CurrentSetPoint"] = 300;
			d["Room"]![0]!["ScheduledSetPoint"] = 300;
			}) with { TemperatureUnits = "Fahrenheit", HomeTarget = 86 };
		Assert.That ((await Run (session)).Passed, Is.True);
		Assert.That (session.Inputs, Is.EqualTo (new[] { RoomTemperatureAction.Lower, RoomTemperatureAction.Raise }));
		Assert.That (session.State.HomeTarget, Is.EqualTo (86));
		}

	[TestCase ("Kelvin", 18d)]
	[TestCase ("Fahrenheit", 18d)]
	public async Task UnknownOrMislabelledUnitsRejectInput (string units, double shown)
		{
		var session = new Session ();
		session.State = session.State with { TemperatureUnits = units, HomeTarget = shown };
		Assert.That ((await Run (session)).Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		}

	[TestCase ("lost-input"), TestCase ("lost-restore")]
	public async Task FahrenheitUncertainAcknowledgementsRestoreWithoutReplay (string behavior)
		{
		var session = new Session { Behavior = behavior };
		session.State = session.State with { TemperatureUnits = "Fahrenheit", HomeTarget = 64.4 };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (behavior == "lost-input" ? 1 : 2));
		Assert.That (session.Restores, Is.EqualTo (new[] { 0 }));
		Assert.That (session.State.HomeTarget, Is.EqualTo (64.4).Within (0.00001));
		}

	[Test]
	public async Task UnitsChangedDuringInputPreventsAutomaticCompensation ()
		{
		var session = new Session { Behavior = "units-change" };
		session.State = session.State with { TemperatureUnits = "Fahrenheit", HomeTarget = 64.4 };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs.Count, Is.EqualTo (1));
		Assert.That (session.Restores, Is.Empty);
		}

	[Test]
	public async Task UpperTemperatureLimitStartsWithLowerAndRestores ()
		{
		var session = new Session ();
		session.State = Edit (session.State, domain =>
			{
			domain["Room"]![0]!["CurrentSetPoint"] = 300;
			domain["Room"]![0]!["ScheduledSetPoint"] = 300;
			}) with { HomeTarget = 30 };
		var result = await Run (session);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs, Is.EqualTo (new[] { RoomTemperatureAction.Lower, RoomTemperatureAction.Raise }));
		Assert.That (session.State.HomeTarget, Is.EqualTo (30));
		}

	[TestCase (40)]
	[TestCase (310)]
	public async Task OutsideSupportedTargetRangeSendsNoInputs (int target)
		{
		var session = new Session ();
		session.State = Edit (session.State, domain =>
			{
			domain["Room"]![0]!["CurrentSetPoint"] = target;
			domain["Room"]![0]!["ScheduledSetPoint"] = target;
			}) with { HomeTarget = target / 10d };
		Assert.That ((await Run (session)).Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		Assert.That (session.Restores, Is.Empty);
		}

	[Test]
	public async Task AbsentInactiveManualTargetRemainsAbsent ()
		{
		var session = new Session (absent: true);
		Assert.That ((await Run (session)).Passed, Is.True);
		Assert.That (RoomTemperatureRestoration.Room (session.State.Gateway.Hub, 9).TryGetProperty ("ManualSetPoint", out _), Is.False);
		}
	[TestCase ("lost-input"), TestCase ("lost-restore")]
	public async Task LostAcknowledgementFailsButRestoresWithoutReplay (string behavior)
		{
		var session = new Session { Behavior = behavior };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (behavior == "lost-input" ? 1 : 2));
		Assert.That (session.Restores, Is.EqualTo (new[] { 0 }));
		}
	[TestCase ("Celsius", 18d, "ui-disagrees"), TestCase ("Fahrenheit", 64.4d, "ui-disagrees")]
	[TestCase ("Celsius", 18d, "capture-throws"), TestCase ("Fahrenheit", 64.4d, "capture-throws")]
	public async Task DisplayFailureStillRestoresIndependentlyProvenDelivery (string units, double target, string behavior)
		{
		var session = new Session { Behavior = behavior };
		session.State = session.State with { TemperatureUnits = units, HomeTarget = target };
		var original = session.State;
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (1));
		Assert.That (session.Restores, Is.EqualTo (new[] { 0 }));
		RoomTemperatureRestoration.RequireRestored (RoomTemperatureRestoration.Capture (RoomTemperatureRestoration.Room (original.Gateway.Hub, 9)), RoomTemperatureRestoration.Room (session.State.Gateway.Hub, 9));
		Assert.That (session.Records, Does.Contain ("physical-restored"));
		Assert.That (session.Records, Does.Not.Contain ("restored"));
		}
	[TestCase (false, "Celsius", 18d), TestCase (true, "Celsius", 18d)]
	[TestCase (false, "Fahrenheit", 64.4d), TestCase (true, "Fahrenheit", 64.4d)]
	public async Task PreparedOffAndSingleResumeRestoreOriginalPolicy (bool manual, string units, double target)
		{
		var session = new Session (manual);
		session.State = session.State with { TemperatureUnits = units, HomeTarget = target };
		var original = session.State;
		var result = await RoomTemperatureCycle.RunOffAsync (session, TimeSpan.FromMilliseconds (500), CancellationToken.None);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs, Is.EqualTo (new[] { RoomTemperatureAction.PrepareOff, RoomTemperatureAction.ResumeHeating }));
		Assert.That (session.State.Activity.Completed, Is.EqualTo (1), "Direct hub preparation must not be mistaken for a driver command.");
		Assert.That (session.State.HomeTarget, Is.EqualTo (target).Within (0.00001));
		RoomTemperatureRestoration.RequireRestored (RoomTemperatureRestoration.Capture (RoomTemperatureRestoration.Room (original.Gateway.Hub, 9)), RoomTemperatureRestoration.Room (session.State.Gateway.Hub, 9));
		}
	[TestCase ("lost-input"), TestCase ("ui-disagrees"), TestCase ("capture-throws")]
	public async Task FailureAfterOffPreparationRestoresWithoutSendingResume (string behavior)
		{
		var session = new Session { Behavior = behavior };
		var result = await RoomTemperatureCycle.RunOffAsync (session, TimeSpan.FromMilliseconds (250), CancellationToken.None);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs, Is.EqualTo (new[] { RoomTemperatureAction.PrepareOff }));
		Assert.That (session.Restores, Is.EqualTo (new[] { 0 }));
		Assert.That (session.State.HomeTarget, Is.EqualTo (18));
		}
	[TestCase ("not-delivered"), TestCase ("foreign"), TestCase ("away"), TestCase ("restart"), TestCase ("concurrent"), TestCase ("stale")]
	public async Task UnattributableStateNeverAuthorizesCompensation (string behavior)
		{
		var session = new Session { Behavior = behavior };
		var result = await Run (session);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs.Count, Is.EqualTo (1));
		Assert.That (session.Restores, Is.Empty);
		Assert.That (session.Records, Does.Contain ("recovery-required"));
		}
	[TestCase ("original"), TestCase ("input-1-intent")]
	public async Task MissingJournalPreventsPhysicalInput (string phase)
		{
		var session = new Session { FailRecord = phase };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		Assert.That (result.RestorationConfirmed, Is.True);
		}
	[Test]
	public async Task ExistingTimedOverrideIsNotReplaced ()
		{
		var session = new Session ();
		session.State = Edit (session.State, d => d["Room"]![0]!["OverrideTimeoutUnixTime"] = DateTimeOffset.UtcNow.AddHours (1).ToUnixTimeSeconds ());
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		}
	[TestCase (false), TestCase (true)]
	public async Task CancellationAfterDeliveredInputStillRestores (bool boost)
		{
		using var cancellation = new CancellationTokenSource ();
		var session = new Session { CancelAfterInput = cancellation };
		var result = await RoomTemperatureCycle.RunAsync (session, boost, TimeSpan.FromMilliseconds (250), cancellation.Token);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (1));
		Assert.That (session.Restores, Is.EqualTo (new[] { 0 }));
		}
	[Test]
	public async Task LostBoostInputIsObservedThenCancelledWithoutRepeatingTap ()
		{
		var session = new Session { Behavior = "lost-input" };
		var result = await Run (session, boost: true);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs, Is.EqualTo (new[] { RoomTemperatureAction.BoostOn }));
		Assert.That (session.Restores, Is.EqualTo (new[] { 0 }));
		}
	[Test]
	public void OriginalTemperatureAloneDoesNotProveScheduledRestoration ()
		{
		var original = Snapshot ();
		var plan = RoomTemperatureRestoration.Capture (RoomTemperatureRestoration.Room (original.Gateway.Hub, 9));
		var changed = Edit (original, d => d["Room"]![0]!["OverrideType"] = "Manual");
		Assert.Throws<NotSupportedException> (() => RoomTemperatureRestoration.RequireRestored (plan, RoomTemperatureRestoration.Room (changed.Gateway.Hub, 9)));
		}
	[TestCase ("Celsius"), TestCase ("Fahrenheit")]
	public async Task ConfiguredBoostUsesCelsiusIncreaseRegardlessOfDisplayUnits (string units)
		{
		var session = new Session ();
		session.State = session.State with { TemperatureUnits = units, HomeTarget = units == "Celsius" ? 18 : 64.4, BoostSettings = new (4, 15) };
		var result = await Run (session, boost: true);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Targets, Is.EqualTo (new[] { 220, 180 }));
		Assert.That (session.Records, Does.Contain ("boost-input-interval"));
		}
	[TestCase ("wrong-boost-target"), TestCase ("wrong-boost-duration"), TestCase ("wrong-boost-type")]
	public async Task WrongPhysicalBoostDoesNotPassButOriginalPolicyIsRestored (string behavior)
		{
		var session = new Session { Behavior = behavior };
		var original = session.State;
		var result = await Run (session, boost: true);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs, Is.EqualTo (new[] { RoomTemperatureAction.BoostOn }));
		Assert.That (session.Restores, Is.EqualTo (new[] { 0 }));
		RoomTemperatureRestoration.RequireRestored (RoomTemperatureRestoration.Capture (RoomTemperatureRestoration.Room (original.Gateway.Hub, 9)), RoomTemperatureRestoration.Room (session.State.Gateway.Hub, 9));
		}
	[Test]
	public async Task MissingBoostSettingsPreventsAnyControlInput ()
		{
		var session = new Session ();
		session.State = session.State with { BoostSettings = null };
		var result = await Run (session, boost: true);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		Assert.That (session.Restores, Is.Empty);
		}
	[TestCase (0, 60), TestCase (6, 60), TestCase (double.NaN, 60), TestCase (double.PositiveInfinity, 60)]
	[TestCase (2, 0), TestCase (2, 4), TestCase (2, 1441)]
	public void InvalidBoostConfigurationCannotCreateAnExpectation (double delta, int duration)
		{
		Assert.Throws<InvalidDataException> (() => RoomBoostExpectation.Create (Snapshot () with { BoostSettings = new (delta, duration) }));
		}
	[Test]
	public void BoostAtThePhysicalLimitRequiresItsOwnCaseBeforeInput ()
		{
		var state = Edit (Snapshot (), d => d["Room"]![0]!["CalculatedTemperature"] = 295);
		Assert.Throws<InvalidDataException> (() => RoomBoostExpectation.Create (state));
		}
	[TestCase (190, 212, 230)]
	[TestCase (190, 211, 230)]
	[TestCase (190, 218, 240)]
	[TestCase (250, 212, 250)]
	public void BoostExpectationUsesAmbientResolutionWithoutLoweringAnExistingHigherTarget (int scheduled, int ambient, int expected)
		{
		var original = Edit (Snapshot (), d =>
			{
				d["Room"]![0]!["CurrentSetPoint"] = scheduled;
				d["Room"]![0]!["CalculatedTemperature"] = ambient;
			});
		Assert.That (RoomBoostExpectation.Create (original).TargetTenthsCelsius, Is.EqualTo (expected));
		}
	[TestCase ("CalculatedTemperature"), TestCase ("ClimateCapabilities")]
	public void BoostWithoutAmbientOrResolutionCannotInventAnExpectedTarget (string missing)
		{
		var original = Edit (Snapshot (), d => d["Room"]![0]!.AsObject ().Remove (missing));
		Assert.Throws<KeyNotFoundException> (() => RoomBoostExpectation.Create (original));
		}
	[TestCase (210, false), TestCase (230, true), TestCase (235, false)]
	public void WarmRoomBoostRejectsScheduledTargetArithmeticAndWrongQuantizedTargets (int target, bool matches)
		{
		var original = Edit (Snapshot (), d =>
			{
				d["Room"]![0]!["CurrentSetPoint"] = 190;
				d["Room"]![0]!["CalculatedTemperature"] = 212;
			});
		var input = new DateTimeOffset (2026, 9, 19, 18, 29, 15, TimeSpan.Zero);
		var observed = Edit (original, d =>
			{
				var room = d["Room"]![0]!;
				room["CurrentSetPoint"] = target;
				room["OverrideType"] = "Manual";
				room["SetpointOrigin"] = "FromBoost";
				room["OverrideTimeoutUnixTime"] = input.AddMinutes (60).ToUnixTimeSeconds ();
			});
		Assert.That (RoomBoostExpectation.Create (original).Matches (observed, input, input), Is.EqualTo (matches));
		}
	[TestCase (-60, true), TestCase (60, true), TestCase (-61, false), TestCase (61, false)]
	public void BoostExpiryUsesDeclaredMinuteTolerance (int seconds, bool matches)
		{
		var now = new DateTimeOffset (2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
		var original = Snapshot ();
		var observed = Edit (original, d =>
			{
				d["Room"]![0]!["CurrentSetPoint"] = 200;
				d["Room"]![0]!["OverrideType"] = "Manual";
				d["Room"]![0]!["SetpointOrigin"] = "FromBoost";
				d["Room"]![0]!["OverrideTimeoutUnixTime"] = now.AddMinutes (60).AddSeconds (seconds).ToUnixTimeSeconds ();
			});
		Assert.That (RoomBoostExpectation.Create (original).Matches (observed, now, now), Is.EqualTo (matches));
		}
	[TestCase ("Manual", "FromBoost", true)]
	[TestCase ("Boost", "FromBoost", false)]
	[TestCase ("None", "FromBoost", false)]
	[TestCase ("Manual", "FromManualMode", false)]
	public void BoostReadbackUsesObservedHubStateRatherThanRequestType (string type, string origin, bool matches)
		{
		// Non-identifying values from the retained second-generation HubR response.
		// Its outgoing Boost request is already checked in PlatformTemperatureTests.
		var original = Edit (Snapshot (), d =>
			{
				d["Room"]![0]!["CurrentSetPoint"] = 190;
				d["Room"]![0]!["CalculatedTemperature"] = 212;
			});
		var observed = Edit (original, d =>
			{
				var room = d["Room"]![0]!;
				room["CurrentSetPoint"] = 230;
				room["OverrideSetpoint"] = 230;
				room["OverrideType"] = type;
				room["SetpointOrigin"] = origin;
				room["OverrideTimeoutUnixTime"] = 1789747560L;
			});
		var input = new DateTimeOffset (2026, 9, 18, 15, 6, 50, TimeSpan.Zero);
		Assert.That (RoomBoostExpectation.Create (original).Matches (observed, input, input.AddSeconds (1)), Is.EqualTo (matches));
		}

	[TestCase (false), TestCase (true)]
	public async Task RepeatedBoostRestoresOneOriginalPolicyAcrossThreeCycles (bool fahrenheit)
		{
		var session = new Session ();
		if (fahrenheit) session.State = session.State with { TemperatureUnits = "Fahrenheit", HomeTarget = 64.4 };
		var original = session.State;
		var result = await RoomBoostRepetition.RunAsync (_ => session, 3, TimeSpan.FromSeconds (1), CancellationToken.None);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs, Is.EqualTo (Enumerable.Range (0, 3).SelectMany (_ => new[] { RoomTemperatureAction.BoostOn, RoomTemperatureAction.BoostOff })));
		Assert.That (session.Records.Count (phase => phase == "repetition-verified"), Is.EqualTo (3));
		Assert.That (session.State.HomeTarget, Is.EqualTo (original.HomeTarget));
		Assert.That (session.Restores, Is.Empty);
		}

	[TestCase ("configuration"), TestCase ("units"), TestCase ("epoch"), TestCase ("commands"), TestCase ("manual-target"), TestCase ("other-room")]
	public async Task RepeatedBoostStopsInsteadOfAdoptingAChangedBaseline (string change)
		{
		var session = new Session ();
		int created = 0;
		var result = await RoomBoostRepetition.RunAsync (index =>
			{
				created++;
				if (index == 1) session.State = change switch
					{
					"configuration" => session.State with { BoostSettings = new (3, 60) },
					"units" => session.State with { TemperatureUnits = "Fahrenheit", HomeTarget = 64.4 },
					"epoch" => session.State with { Activity = session.State.Activity with { Epoch = Guid.NewGuid ().ToString ("N") } },
					"commands" => session.State with { Activity = session.State.Activity with { Completed = 3 } },
					"manual-target" => Edit (session.State, d => d["Room"]![0]!["ManualSetPoint"] = 190),
					_ => Edit (session.State, d => d["Room"]![1]!["ManualSetPoint"] = 200)
					};
				return session;
			}, 3, TimeSpan.FromSeconds (1), CancellationToken.None);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (created, Is.EqualTo (2));
		Assert.That (session.Inputs.Count, Is.EqualTo (2));
		Assert.That (session.Restores, Is.Empty);
		}

	[Test]
	public async Task RepeatedBoostRechecksTheBaselineInsideCyclePreflight ()
		{
		var session = new Session ();
		int reads = 0;
		session.BeforeRead = () =>
			{
				if (++reads == 2) session.State = session.State with { BoostSettings = new (3, 60) };
			};
		var result = await RoomBoostRepetition.RunAsync (_ => session, 3, TimeSpan.FromSeconds (1), CancellationToken.None);
		Assert.That (result.Passed || result.RestorationConfirmed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		}

	[TestCase ("wrong-boost-target", true), TestCase ("not-delivered", false)]
	public async Task FailedBoostDoesNotStartAnotherCycle (string behavior, bool restored)
		{
		var session = new Session { Behavior = behavior };
		int created = 0;
		var result = await RoomBoostRepetition.RunAsync (_ => { created++; return session; }, 3, TimeSpan.FromMilliseconds (250), CancellationToken.None);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.RestorationConfirmed, Is.EqualTo (restored));
		Assert.That (created, Is.EqualTo (1));
		Assert.That (session.Inputs, Is.EqualTo (new[] { RoomTemperatureAction.BoostOn }));
		}

	[TestCase ("repetition-baseline", 0), TestCase ("repetition-verified", 2)]
	public async Task RepeatedBoostStopsWhenItsEvidenceCannotBeWritten (string phase, int inputs)
		{
		var session = new Session { FailRecord = phase };
		int created = 0;
		var result = await RoomBoostRepetition.RunAsync (_ => { created++; return session; }, 3, TimeSpan.FromSeconds (1), CancellationToken.None);
		Assert.That (result.Passed, Is.False);
		Assert.That (created, Is.EqualTo (1));
		Assert.That (session.Inputs.Count, Is.EqualTo (inputs));
		}

	[TestCase (0), TestCase (4)]
	public void RepeatedBoostRejectsAnUnboundedOrEmptyRun (int cycles)
		{
		Assert.ThrowsAsync<ArgumentOutOfRangeException> (async () => await RoomBoostRepetition.RunAsync (_ => throw new AssertionException ("No session should open."), cycles, TimeSpan.FromSeconds (1), CancellationToken.None));
		}
	}