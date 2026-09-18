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
			9, new (Guid.NewGuid ().ToString ("N"), 0, 0), 18, false, true);
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
		public List<int> Restores = [];
		public List<string> Records = [];
		public string? Behavior;
		public string? FailRecord;
		public CancellationTokenSource? CancelAfterInput;
		public Task<RoomTemperatureSnapshot> ReadAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			if (Behavior == "capture-throws" && Inputs.Count != 0)
				throw new IOException ("Synthetic unavailable Android capture.");
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
			Records.Add (phase);
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
			if (Behavior == "not-delivered")
				throw new IOException ("uncertain input");
			if (action == RoomTemperatureAction.BoostOff)
				RestoreRoom ();
			else
				{
				bool boost = action == RoomTemperatureAction.BoostOn;
				int target = action == RoomTemperatureAction.PrepareOff ? -200 : action == RoomTemperatureAction.ResumeHeating ? 50 :
					RoomTemperatureRestoration.Room (State.Gateway.Hub, 9).GetProperty ("CurrentSetPoint").GetInt32 () + (boost ? 20 : action == RoomTemperatureAction.Raise ? 5 : -5);
				State = Edit (State, d =>
					{
						var room = d["Room"]![0]!;
						room["CurrentSetPoint"] = target;
						// The real hub also reports FromBoost for a Manual override of an Auto schedule.
						room["SetpointOrigin"] = boost || !manual ? "FromBoost" : "FromManualMode";
						room["OverrideType"] = boost ? "Boost" : "Manual";
						room["OverrideSetpoint"] = target;
						room["OverrideTimeoutUnixTime"] = DateTimeOffset.UtcNow.AddHours (1).ToUnixTimeSeconds ();
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
			if (Behavior == "lost-input" && Inputs.Count == 1)
				throw new IOException ("input acknowledgement lost");
			CancelAfterInput?.Cancel ();
			return Task.CompletedTask;
			}
		public Task RestoreAsync (int index, RoomTemperatureRestorePlan plan, JsonElement request, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
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
	[TestCase (false, false), TestCase (false, true), TestCase (true, false), TestCase (true, true)]
	public async Task BothInputsRestoreOriginalPolicyAndTarget (bool manual, bool boost)
		{
		var session = new Session (manual);
		var original = session.State;
		var result = await Run (session, boost);
		Assert.That (result.Passed && result.RestorationConfirmed, Is.True, result.Detail);
		Assert.That (session.Inputs.Count, Is.EqualTo (2));
		Assert.That (session.Restores.Count, Is.EqualTo (boost ? 0 : manual ? 2 : 1));
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
	}