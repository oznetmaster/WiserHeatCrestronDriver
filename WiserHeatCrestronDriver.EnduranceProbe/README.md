# Wiser endurance monitoring

This is the Wiser-specific read-only producer used by the DevTools endurance collector. It checks the running driver against the independently queried Wiser hub. It does not operate heating, hot water, Away mode or schedules.

The producer targets .NET 10 and restores CrestronHomeDevTools 1.7.0 from NuGet. Its projects are included in the Visual Studio solution, and its offline tests run in CI with discovery-versus-execution verification. The producer is used by a separately configured monitoring worker; its presence does not establish a completed monitoring run. Ordinary driver tests do not start this producer or contact a hub.

The required driver diagnostics are in the current source candidate and are **not included in the published driver 1.3.7 package**. Select a candidate built with those diagnostics; a missing diagnostic fails rather than inferring readiness from an older release.

## What each observation proves

The producer verifies the existing processor reservation before and after every remote read. It resumes ownership supplied by the monitor; it never acquires a new reservation or releases the monitor's reservation itself.

It compares every file in the active used-driver directory with the exact candidate package, before and after observing function. The uploaded catalogue archive is not a substitute for the active files. It also checks the installed root ID, name, model, room, version, configured hub address, readiness and supported hot-water/Away properties.

Two hub reads must identify the expected controller and hot-water channel and agree with two driver readings. Unknown states and a transition during observation fail the sample. The producer does not retry controls or quietly ignore a mismatch. A natural state change during the observation can therefore invalidate the run; its retained evidence supports review.

Two diagnostics strengthen this comparison:

- `lastHubRefreshUtc` advances only after a successful fresh hub read is applied by the current connection generation. Cached reads, failed reads and superseded connections cannot renew it. Its wire format is `utc:` followed by an invariant round-trip UTC timestamp. The prefix prevents the processor configuration path from converting an ISO-looking string to a local date without its timezone and precision. Missing, localized, future or stale readings fail.
- `driverLifetimeId` is created once for each root entity. It remains stable through refreshes, but changes when the root is recreated. The approved binding requires that same lifetime throughout the run, so restarting the same package cannot silently continue an earlier endurance period.

Processor uptime is checked against the original inferred UTC boot window with an explicit, bounded clock tolerance. The baseline never moves forward with later samples. The console's local last-started text is diagnostic only. Both worker and processor clocks must remain trustworthy; the checks are not cryptographic proof of uninterrupted execution between samples.

## Private inputs and immutable files

The complete published producer directory is pinned by DevTools, including its executable, dependencies, `binding.json` and `candidate.pkg`. A changed, missing or additional file invalidates the producer. Keep evidence and mutable credential settings outside that directory. These household bindings and credentials must not be committed or included in public release artifacts.

`binding.json` supplies the reviewed plan, processor address and certificate/SSH pins, installed root identity and lifetime, active driver key/version, hub address/controller UUID/model/channel, original boot window, clock tolerance and maximum successful-refresh age. Acceptance criteria belong in this reviewed binding, not the credential file.

The external request must match the complete approved plan. Inside the binding only `ApprovedPlan.ProducerId` is empty: the outer producer ID is calculated after the binding joins the pinned file manifest, avoiding a recursive hash dependency. All other plan fields, including the reservation ID, must match exactly.

The private credential file contains only `UserName`, `Password` and `HubSecret`. It cannot change the host, device selection, duration, cadence or acceptance rules. The producer never includes those credentials or raw exception messages in evidence.

## Execution and evidence

DevTools starts the executable without a window, writes one structured probe-request JSON object to stdin, closes stdin, and reads one result from stdout. Use `--help` for the protocol description. Do not invoke it with command-line passwords or interpret a successful process exit as a passing observation: a valid negative observation has `Outcome = Failed`.

The monitor holds its shared processor reservation between scheduled worker invocations. Each due invocation runs one probe; the collector preserves its evidence, identities and outcome. It rejects excessive gaps, interrupted probes and changed identities, and exports a standard observation only after the approved interval passes. Failed runs remain failed after cleanup. Uncertain acquisition, pending probes and unknown release outcomes require reconciliation.

Build and test from Visual Studio or the repository root:

```powershell
dotnet test WiserHeatCrestronDriver.EnduranceProbe.Tests -c Release
dotnet publish WiserHeatCrestronDriver.EnduranceProbe -c Release -r win-x64 --self-contained true -o artifacts/endurance-producer
```

Publish for the monitoring computer's runtime identifier and retain the complete output directory. The project is not a NuGet package. Use a private copy when adding reviewed bindings and candidate bytes; do not publish that bound copy. `DevToolsSourceProject` remains an optional local development override for testing shared-library source changes; normal builds use the released dependency.

Use a monitoring policy with the intended duration and immutable package identity. Coordinate with other users of the same physical hub and configure scheduling, alerts and restart handling for the monitoring worker. A short check does not establish a longer period of stability. UI, control and restoration tests remain separate checks.

Copyright (c) 2026 Neil Colvin. Licensed under the MIT License with Commons Clause; see the repository LICENSE. This project is independent of Crestron Electronics and Schneider Electric/Drayton Wiser and is not endorsed by them.
