// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License with Commons Clause. See LICENSE in the repository root.

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CrestronHomeDevTools;

using NUnit.Framework;

namespace WiserHeatCrestronDriver.EnduranceProbe.Tests;

[TestFixture]
public sealed class ObservationTests
	{
	private static readonly DateTimeOffset Boot = DateTimeOffset.Parse ("2026-09-17T10:00:00Z");
	private static readonly SubmissionEndurancePlan Plan = new (new (new ('a', 64), new ('b', 40), new ('c', 64), new ('d', 64)),
		new ("driver-endurance", TimeSpan.FromHours (24), Execution: new ("gateway", "endurance", SubmissionEvidenceOutcome.Passed, null, false, 60)),
		"processor-identity", "installed-identity", "11111111111111111111111111111111", "process-sha256:example", TimeSpan.FromSeconds (30), TimeSpan.FromSeconds (30));
	private static readonly Binding Binding = new (1, Plan with { ProducerId = "" }, "192.0.2.10", new ('a', 64), "ssh-pin",
		41, "Test Gateway", 42, "example.wiser.gateway", "1.3.007.0000", "192.0.2.20", "hub-uuid", "CCTFR6313G2", 2,
		new (Boot, Boot.AddMilliseconds (100), TimeSpan.FromSeconds (1)), TimeSpan.FromSeconds (90), "22222222222222222222222222222222");
	private static readonly Dictionary<string, string> Files = new () { ["driver.dll"] = new ('a', 64) };
	private static readonly HubState Hub = new (Binding.HubUuid, Binding.HubModel, 2, false, false);
	private static readonly DriverState Driver = new (41, -6, "Test Gateway", "Wiser Heat Gateway", 42, Binding.DriverVersion,
		Binding.HubHost, true, true, true, true, true, false, false, Boot.AddMinutes (10), Boot.AddMinutes (10).AddMilliseconds (100), Binding.DriverLifetimeId);
	private static SubmissionEnduranceProbeRequest Request => new (1, Plan, Path.GetFullPath ("private-credentials.json"));

	[Test]
	public void TaggedUtcTimestampRetainsOffsetAndPrecision ()
		{
		var expected = new DateTimeOffset (2026, 9, 17, 16, 26, 5, TimeSpan.Zero).AddTicks (1234567);
		Assert.That (RemoteSource.ParseRefreshTimestamp ("utc:" + expected.ToString ("O")), Is.EqualTo (expected));
		}

	[TestCase (null)]
	[TestCase ("")]
	[TestCase ("09/17/2026 17:26:05")]
	[TestCase ("2026-09-17T16:26:05.1234567+00:00")]
	[TestCase ("utc:2026-09-17T17:26:05.1234567+01:00")]
	[TestCase ("UTC:2026-09-17T16:26:05.1234567+00:00")]
	[TestCase ("utc:09/17/2026 17:26:05")]
	public void AmbiguousOrReformattedRefreshTimestampIsRejected (string? value)
		{
		Assert.Throws<FormatException> (() => RemoteSource.ParseRefreshTimestamp (value!));
		}

	[Test]
	public async Task PassingObservationChecksOwnershipAroundEveryReadAndReturnsScopedEvidence ()
		{
		var source = new Source ();
		var result = await Observation.RunAsync (Binding, Plan, Files, source, default);
		Assert.That (result.Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Passed));
		Assert.That (source.OwnerChecks, Is.EqualTo (17));
		Assert.That (source.Reads, Is.EqualTo (new[] { "uptime", "payload", "hub", "driver", "hub", "driver", "payload", "uptime" }));
		Assert.That (result.Identity, Is.EqualTo (Plan.Identity));
		Assert.That (result.ProducerId, Is.EqualTo (Plan.ProducerId));
		Assert.That (result.ReservationId, Is.EqualTo (Plan.ReservationId));
		Assert.That (result.BootIdentity, Is.EqualTo (Binding.BootIdentity));
		using var evidence = JsonDocument.Parse (result.Evidence);
		Assert.That (evidence.RootElement.GetProperty ("ReadOnly").GetBoolean (), Is.True);
		Assert.That (Encoding.UTF8.GetString (result.Evidence), Does.Not.Contain ("Password").And.Not.Contain ("HubSecret"));
		}

	[TestCase ("lifetime")]
	[TestCase ("id")]
	[TestCase ("parent")]
	[TestCase ("name")]
	[TestCase ("model")]
	[TestCase ("location")]
	[TestCase ("version")]
	[TestCase ("hub")]
	[TestCase ("ready")]
	[TestCase ("loaded")]
	[TestCase ("online")]
	[TestCase ("hot-water-capability")]
	[TestCase ("away-capability")]
	public async Task ChangedDriverIdentityOrCapabilityNeverPasses (string change)
		{
		var value = change switch
			{
			"lifetime" => Driver with { LifetimeId = "33333333333333333333333333333333" },
			"id" => Driver with { Id = 99 }, "parent" => Driver with { Parent = 99 }, "name" => Driver with { Name = "Other" },
			"model" => Driver with { Model = "Other" }, "location" => Driver with { Location = 99 }, "version" => Driver with { Version = "1.3.007.0001" },
			"hub" => Driver with { HubHost = "192.0.2.21" }, "ready" => Driver with { Ready = false }, "loaded" => Driver with { Loaded = false },
			"online" => Driver with { Online = false }, "hot-water-capability" => Driver with { HotWaterVisible = false },
			_ => Driver with { AwayVisible = false }
			};
		var result = await Run (new Source { Drivers = [value, value] });
		Assert.That (result.Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Failed));
		Assert.That (Encoding.UTF8.GetString (result.Evidence), Does.Contain ("driver-identity-or-capability-mismatch"));
		}

	[Test]
	public async Task SamePackageRestartDuringObservationCannotPass ()
		{
		var result = await Run (new Source { Drivers = [Driver, Driver with { LifetimeId = "33333333333333333333333333333333" }] });
		Assert.That (result.Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Failed));
		Assert.That (Encoding.UTF8.GetString (result.Evidence), Does.Contain ("driver-identity-or-capability-mismatch"));
		}

	[TestCase ("uuid")]
	[TestCase ("model")]
	[TestCase ("channel")]
	[TestCase ("transition")]
	[TestCase ("hot-water-mismatch")]
	[TestCase ("away-mismatch")]
	public async Task IndependentHubChangesOrDisagreementNeverPass (string change)
		{
		var value = change switch
			{
			"uuid" => Hub with { Uuid = "different" }, "model" => Hub with { Model = "different" }, "channel" => Hub with { HotWaterId = 9 },
			"away-mismatch" => Hub with { Away = true }, _ => Hub with { HotWaterOn = true }
			};
		var source = new Source { Hubs = change == "transition" ? [Hub, value] : [value, value] };
		Assert.That ((await Run (source)).Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Failed));
		}

	[TestCase (false)]
	[TestCase (true)]
	public async Task PayloadChangeBeforeOrAfterFunctionNeverPasses (bool after)
		{
		var changed = new Dictionary<string, string> { ["driver.dll"] = new ('b', 64) };
		var source = new Source { Payloads = after ? [Files, changed] : [changed, Files] };
		Assert.That ((await Run (source)).Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Failed));
		}

	[TestCase ("extra")]
	[TestCase ("missing")]
	public void IncompleteOrAdditionalPayloadFails (string change)
		{
		var files = new Dictionary<string, string> (Files);
		if (change == "extra") files.Add ("unexpected.dll", new ('a', 64)); else files.Clear ();
		Assert.Throws<Observation.Refusal> (() => Observation.CheckPayload (Files, files));
		}

	[TestCase (1)]
	[TestCase (2)]
	[TestCase (9)]
	[TestCase (17)]
	public void LostOwnershipIsNotConvertedToPassingEvidence (int check)
		=> Assert.ThrowsAsync<IOException> (() => Run (new Source { FailOwnerCheck = check }));

	[Test]
	public void CancelledObservationDoesNotReadAnything ()
		{
		var source = new Source ();
		Assert.CatchAsync<OperationCanceledException> (() => Observation.RunAsync (Binding, Plan, Files, source, new CancellationToken (true)));
		Assert.That (source.Reads, Is.Empty);
		}

	[Test]
	public async Task RestartAfterFunctionFailsTheOriginalPinnedWindow ()
		{
		var source = new Source { RestartAfter = true };
		var result = await Run (source);
		Assert.That (result.Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Failed));
		Assert.That (Encoding.UTF8.GetString (result.Evidence), Does.Contain ("boot-window-or-clock-mismatch"));
		}

	[Test]
	public void ClockToleranceCannotMoveTheReferenceWindow ()
		{
		var time = Boot.AddMinutes (10);
		var drifted = new ProcessorUptimeSnapshot (TimeSpan.FromMinutes (10), default, time.AddSeconds (3), time.AddSeconds (3.1));
		Assert.Throws<Observation.Refusal> (() => Observation.CheckBoot (Binding, drifted));
		}

	[Test]
	public void ExactReviewedPlanIsAccepted () => Assert.DoesNotThrow (() => Binding.Validate (Request));

	[TestCase ("old")]
	[TestCase ("absent")]
	[TestCase ("future")]
	public async Task StaleOrUnverifiableDriverRefreshCannotPass (string change)
		{
		var stamp = change switch { "old" => Driver.ObservedUtc.AddMinutes (-3), "future" => Driver.ObservedUtc.AddMinutes (3), _ => default };
		var stale = Driver with { LastHubRefreshUtc = stamp };
		var result = await Run (new Source { Drivers = [stale, stale] });
		Assert.That (result.Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Failed));
		Assert.That (Encoding.UTF8.GetString (result.Evidence), Does.Contain ("driver-refresh-stale-or-clock-mismatch"));
		}

	[Test]
	public async Task ANewSuccessfulRefreshCanOccurBetweenStateReads ()
		{
		var later = Driver with { LastHubRefreshUtc = Driver.LastHubRefreshUtc.AddSeconds (1), ObservedUtc = Driver.ObservedUtc.AddSeconds (1) };
		Assert.That ((await Run (new Source { Drivers = [Driver, later] })).Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Passed));
		}

	[Test]
	public async Task RegressingFreshnessRecordCannotPass ()
		{
		var earlier = Driver with { LastHubRefreshUtc = Driver.LastHubRefreshUtc.AddSeconds (-1) };
		Assert.That ((await Run (new Source { Drivers = [Driver, earlier] })).Outcome, Is.EqualTo (SubmissionEvidenceOutcome.Failed));
		}
	[TestCase ("identity")]
	[TestCase ("duration")]
	[TestCase ("reservation")]
	[TestCase ("installation")]
	[TestCase ("interval")]
	public void RequestCannotRelaxOrRebindTheReviewedPlan (string change)
		{
		var modified = change switch
			{
			"identity" => Plan with { Identity = Plan.Identity with { PackageSha256 = new ('e', 64) } },
			"duration" => Plan with { Requirement = Plan.Requirement with { MinimumDuration = TimeSpan.FromMinutes (1) } },
			"reservation" => Plan with { ReservationId = "22222222222222222222222222222222" },
			"installation" => Plan with { InstallationIdentity = "other" }, _ => Plan with { SampleInterval = TimeSpan.FromSeconds (20) }
			};
		Assert.Throws<InvalidDataException> (() => Binding.Validate (Request with { Plan = modified }));
		}

	[Test]
	public void CredentialsCannotContainAcceptanceOverrides ()
		=> Assert.Throws<JsonException> (() => JsonSerializer.Deserialize<CredentialSettings> ("""{"UserName":"x","Password":"y","HubSecret":"z","DriverId":99}""", Input.Json));

	[TestCase ("../driver.dll")]
	[TestCase ("/driver.dll")]
	[TestCase ("x/../driver.dll")]
	[TestCase ("c:driver.dll")]
	public void UnsafeCandidateArchiveEntryFails (string name)
		{
		using var data = Archive (name);
		using var archive = new ZipArchive (data, ZipArchiveMode.Read);
		Assert.Throws<InvalidDataException> (() => EnduranceProbe.Binding.ReadEntries (archive));
		}

	[Test]
	public void CaseAmbiguousCandidateArchiveFails ()
		{
		using var data = Archive ("driver.dll", "Driver.dll");
		using var archive = new ZipArchive (data, ZipArchiveMode.Read);
		Assert.Throws<InvalidDataException> (() => EnduranceProbe.Binding.ReadEntries (archive));
		}

	[Test]
	public void BackslashEntriesAreNormalizedForActualProcessorLayout ()
		{
		using var data = Archive ("translations\\en-US.json");
		using var archive = new ZipArchive (data, ZipArchiveMode.Read);
		Assert.That (EnduranceProbe.Binding.ReadEntries (archive).Keys, Is.EqualTo (new[] { "translations/en-US.json" }));
		}

	[Test]
	public void GrowingStreamsCannotExceedHashOrJsonLimits ()
		{
		Assert.ThrowsAsync<InvalidDataException> (() => Input.HashBoundedAsync (new MemoryStream (new byte[101]), 100, default));
		Assert.ThrowsAsync<InvalidDataException> (() => Input.ReadBoundedAsync (new MemoryStream (new byte[101]), 100, default));
		}

	private const string Domain = """{"Device":[{"ProductType":"Controller","UUID":"hub-uuid","ModelIdentifier":"CCTFR6313G2"}],"HotWater":[{"id":2,"WaterHeatingState":"Off"}],"System":{"OverrideType":"None"}}""";
	[Test]
	public void RawHubParsingUsesTheReviewedControllerAndChannel ()
		{
		using var data = JsonDocument.Parse (Domain);
		Assert.That (RemoteSource.ParseHub (data.RootElement, 2), Is.EqualTo (Hub));
		}
	[TestCase ("Unknown")]
	[TestCase ("")]
	public void UnknownHotWaterStateCannotBecomeFalse (string state)
		{
		using var data = JsonDocument.Parse (Domain.Replace ("Off", state));
		Assert.Throws<InvalidDataException> (() => RemoteSource.ParseHub (data.RootElement, 2));
		}
	[Test]
	public void DuplicateControllersCannotBeSilentlySelected ()
		{
		var data = JsonNode.Parse (Domain)!;
		data["Device"]!.AsArray ().Add (data["Device"]![0]!.DeepClone ());
		Assert.Throws<InvalidOperationException> (() => RemoteSource.ParseHub (JsonSerializer.SerializeToElement (data), 2));
		}

	private static Task<SubmissionEnduranceProbeResult> Run (Source source) => Observation.RunAsync (Binding, Plan, Files, source, default);
	private static MemoryStream Archive (params string[] names)
		{
		var data = new MemoryStream ();
		using (var zip = new ZipArchive (data, ZipArchiveMode.Create, true))
			foreach (string name in names)
				using (var entry = zip.CreateEntry (name).Open ()) entry.Write ("sample"u8);
		data.Position = 0;
		return data;
		}
	private sealed class Source : IObservationSource
		{
		internal int OwnerChecks;
		internal int FailOwnerCheck;
		internal bool RestartAfter;
		internal List<string> Reads { get; } = [];
		internal DriverState[] Drivers = [Driver, Driver];
		internal HubState[] Hubs = [Hub, Hub];
		internal IReadOnlyDictionary<string, string>[] Payloads = [Files, Files];
		private int _uptime, _driver, _hub, _payload;
		public Task VerifyOwnerAsync (CancellationToken token)
			{
			token.ThrowIfCancellationRequested ();
			if (++OwnerChecks == FailOwnerCheck) throw new IOException ("Synthetic lost ownership.");
			return Task.CompletedTask;
			}
		public Task<ProcessorUptimeSnapshot> ReadUptimeAsync (CancellationToken token)
			{
			Reads.Add ("uptime");
			int index = _uptime++;
			var now = Boot.AddMinutes (10).AddSeconds (index);
			var duration = RestartAfter && index > 0 ? TimeSpan.FromSeconds (1) : TimeSpan.FromMinutes (10).Add (TimeSpan.FromSeconds (index));
			return Task.FromResult (new ProcessorUptimeSnapshot (duration, default, now, now.AddMilliseconds (100)));
			}
		public Task<IReadOnlyDictionary<string, string>> ReadPayloadAsync (CancellationToken token) { Reads.Add ("payload"); return Task.FromResult (Payloads[_payload++]); }
		public Task<DriverState> ReadDriverAsync (CancellationToken token) { Reads.Add ("driver"); return Task.FromResult (Drivers[_driver++]); }
		public Task<HubState> ReadHubAsync (CancellationToken token) { Reads.Add ("hub"); return Task.FromResult (Hubs[_hub++]); }
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}