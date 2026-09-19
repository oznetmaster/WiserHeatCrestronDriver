// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using NUnit.Framework;
using WiserHeatCrestronDriver.ConfigurationProbe;

namespace WiserHeatCrestronDriver.ControlProbe.Tests;

[TestFixture]
public sealed class GatewayFeatureCycleTests
	{
	private sealed class Session (GatewayFeatures original) : IGatewayFeatureSession
		{
		public GatewayFeatures Current = original;
		public string Identity = "gateway";
		public string Other = "unchanged-other-configuration";
		public bool Ready = true;
		public List<GatewayFeatures> Inputs { get; } = [];
		public List<GatewayFeatures> Inspections { get; } = [];
		public List<string> Records { get; } = [];
		public Func<int, GatewayFeatures, Task>? OnApply;
		public Action<string>? OnInspect;
		public string? FailRecord;
		public Task<GatewayFeatureObservation> ReadAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			return Task.FromResult (new GatewayFeatureObservation (Current, Other, Identity, Ready));
			}
		public async Task ApplyAsync (GatewayFeatures features, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Inputs.Add (features);
			if (OnApply != null) await OnApply (Inputs.Count, features);
			else Current = features;
			}
		public Task InspectAsync (string phase, GatewayFeatures features, CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			Assert.That (Current, Is.EqualTo (features));
			Inspections.Add (features);
			OnInspect?.Invoke (phase);
			return Task.CompletedTask;
			}
		public Task RecordAsync (string phase, object value)
			{
			Records.Add (phase);
			if (phase == FailRecord) throw new IOException ("Synthetic evidence failure.");
			return Task.CompletedTask;
			}
		}
	private static Task<GatewayFeatureCycleResult> Run (Session session, CancellationToken token = default) =>
		GatewayFeatureCycle.RunAsync (session, TimeSpan.FromSeconds (2), token);

	[TestCase (false, false), TestCase (false, true), TestCase (true, false), TestCase (true, true)]
	public async Task AllStartingCombinations_ObserveFourVariantsAndRestore (bool hotWater, bool away)
		{
		var original = new GatewayFeatures (hotWater, away);
		var session = new Session (original);
		var result = await Run (session);
		Assert.That (result, Is.EqualTo (new GatewayFeatureCycleResult (true, true, 4)));
		Assert.That (session.Inspections.Distinct ().Count (), Is.EqualTo (4));
		Assert.That (session.Current, Is.EqualTo (original));
		Assert.That (session.Inputs.Count, Is.EqualTo (4));
		var previous = original;
		foreach (var input in session.Inputs)
			{
			Assert.That ((previous.HotWater != input.HotWater ? 1 : 0) + (previous.Away != input.Away ? 1 : 0), Is.EqualTo (1));
			previous = input;
			}
		}

	[TestCase (false), TestCase (true)]
	public async Task LostForwardReply_IsNotRetriedAndObservedStateIsRestored (bool delivered)
		{
		var original = new GatewayFeatures (false, false);
		var session = new Session (original);
		session.OnApply = (number, flags) =>
			{
			if (number != 1 || delivered) session.Current = flags;
			if (number == 1) throw new IOException ("Lost reply.");
			return Task.CompletedTask;
			};
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ConfigurationRestored, Is.True);
		Assert.That (session.Current, Is.EqualTo (original));
		Assert.That (session.Inputs.Count, Is.EqualTo (delivered ? 2 : 1));
		Assert.That (session.Inputs.Count (f => f != original), Is.EqualTo (1));
		}

	[Test]
	public async Task ReadOnlyInspectionFailure_DoesNotPreventConfigurationCompensation ()
		{
		var original = new GatewayFeatures (true, true);
		var session = new Session (original) { OnInspect = phase => { if (phase == "features-2") throw new InvalidDataException (); } };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ConfigurationRestored, Is.True);
		Assert.That (session.Inputs.Count, Is.EqualTo (3));
		Assert.That (session.Current, Is.EqualTo (original));
		}

	[TestCase ("identity"), TestCase ("unrelated")]
	public async Task ForeignChanges_BlockFurtherInputsAndAutomaticOverwrite (string change)
		{
		var session = new Session (new (false, false));
		session.OnInspect = phase =>
			{
			if (phase != "features-1") return;
			if (change == "identity") session.Identity = "replaced-gateway";
			else session.Other = "foreign-setting";
			};
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ConfigurationRestored, Is.False);
		Assert.That (session.Inputs.Count, Is.EqualTo (1));
		Assert.That (session.Records, Does.Contain ("features-recovery-failure"));
		}

	[Test]
	public async Task UnownedFeatureCombination_IsNotOverwritten ()
		{
		var session = new Session (new (false, false));
		session.OnInspect = phase => { if (phase == "features-1") session.Current = new (false, true); };
		var result = await Run (session);
		Assert.That (result.ConfigurationRestored, Is.False);
		Assert.That (session.Inputs.Count, Is.EqualTo (1));
		}

	[Test]
	public async Task CancellationAfterDelivery_UsesIndependentCleanupToken ()
		{
		using var cancellation = new CancellationTokenSource ();
		var original = new GatewayFeatures (false, true);
		var session = new Session (original);
		session.OnApply = (number, flags) => { session.Current = flags; if (number == 1) cancellation.Cancel (); return Task.CompletedTask; };
		var result = await Run (session, cancellation.Token);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ConfigurationRestored, Is.True);
		Assert.That (session.Inputs, Has.Count.EqualTo (2));
		Assert.That (session.Current, Is.EqualTo (original));
		}

	[TestCase (false), TestCase (true)]
	public async Task LostRestoreReply_IsObservedWithoutRepeatingCompensation (bool delivered)
		{
		var original = new GatewayFeatures (false, false);
		var session = new Session (original);
		session.OnApply = (number, flags) =>
			{
			if (number != 4 || delivered) session.Current = flags;
			if (number == 4) throw new IOException ("Lost restore reply.");
			return Task.CompletedTask;
			};
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ConfigurationRestored, Is.EqualTo (delivered));
		Assert.That (session.Inputs, Has.Count.EqualTo (4));
		}

	[TestCase ("features-original"), TestCase ("features-1-intent")]
	public async Task EvidenceFailureBeforeInput_SendsNothing (string record)
		{
		var session = new Session (new (true, true)) { FailRecord = record };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		}

	[Test]
	public async Task EvidenceFailureAfterInput_StillRestores ()
		{
		var session = new Session (new (true, true)) { FailRecord = "features-1-observed" };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (result.ConfigurationRestored, Is.True);
		Assert.That (session.Inputs, Has.Count.EqualTo (2));
		}

	[Test]
	public async Task NotReadyBeforeStart_SendsNothing ()
		{
		var session = new Session (new (true, true)) { Ready = false };
		var result = await Run (session);
		Assert.That (result.Passed, Is.False);
		Assert.That (session.Inputs, Is.Empty);
		}
	}