# Development and validation history

See the [changelog](CHANGELOG.md) for shipped product changes and the [release notes](RELEASE-NOTES.md) for the current update. This document describes tests and development tooling, including the limits of their results.

## 1.3.16 validation - 20 September 2026

The updated dependency passed its complete 211-test library suite on the processor, including real HTTP peers that withhold headers or stall successful and error response bodies. Both desktop targets also passed. Released library assembly hashes match the tested CI artifact.

The driver passed 197 desktop unit/lifecycle cases and 196 corresponding processor cases. The extra desktop case checks the packaged entry-point definition. Offline control-probe and endurance-probe regressions also passed. These results validate the updated dependency and eight-entry editor; they do not claim a new physical outage or endurance run for this build.

## 1.3.15 validation - 20 September 2026

The fixed driver passed 203 desktop lifecycle/regression cases, covering background recovery, failed reads, cancellation deadlines, stable room controllers and stale connection callbacks. The new outage regression reproduced the defect in the previous implementation. Local release packaging, the NuGet distribution wrapper and embedded version checks passed.

A hub network block longer than 60 seconds produced gateway and child offline feedback and automatic fresh-read recovery on a CP4-R, preserving the installed child and all seven saved gateway settings. Conservative observed bounds were 12 seconds for API offline status, 15 seconds for visible Home feedback and 39 seconds for recovery. The room tile was not separately captured during the interruption. This did not test physical power loss or simultaneous processor network loss.

A separate observation recorded an 11-second offline interval and automatic recovery. Its cause remains unresolved and its failed result is retained. A later one-hour observation completed 83 full snapshots and 1,807 availability checks without an offline state or read error; it does not erase the earlier failure. A subsequent network-block observation exposed delayed offline feedback. The response-body cancellation defect fixed in library 1.1.3 was reproduced independently, but that does not prove it was the sole cause of the delayed feedback.

## Temperature and control validation - 19 September 2026

Intermediate builds exposed and corrected automatic control switching to Manual when the global schedule index was temporarily empty, floating-point endpoint rejection, and a Fahrenheit input step that could not reach the declared maximum. These changes have regression coverage. The final Fahrenheit step uses whole degrees, while physical hub targets retain half-degree Celsius resolution.

Recorded live tests covered native temperature controls in Celsius and Fahrenheit, supported endpoints, Off/resume, Boost, repeated Auto/Manual cycles, schedule editing and saving, and gateway Away/hot-water controls. Each result retains the version actually tested; these earlier-build observations are not newly executed 1.3.15 results. The tests compare independent hub state and restore original control policy, schedules and settings, with failures retained for reconciliation.

An unchanged 1.3.11 build completed 480 read-only observations over 24 hours and 1 minute. That result belongs to 1.3.11, not automatically to later binaries. Two processors sharing one physical hub also exercised independent driver instances and matching state; this does not establish two-hub compatibility.

## Package startup and polling diagnostics - 18 September 2026

The packaged entry-point smoke test reproduces failures in implicit embedded-resource lookup after an assembly rename. It now runs in hosted CI against the actual merged package. The embedded-definition regression belongs to the desktop SDK harness because processor test packages deliberately remove dependency driver manifests.

Repeated unchanged schedule messages accounted for most of an observed processor log. Logging regressions verify that unchanged polls remain quiet while changed data and failures remain observable. Desktop and processor tests also cover conversion, command readback, schedule editing and room lifecycle behavior.

## Test projects and CI

- Ordinary NUnit fixtures cover offline behavior and do not require a processor or household credentials.
- The desktop SDK lifecycle harness exercises entity registration, disposal, discovery and background refresh behavior.
- The net472 processor test package runs the shared fixtures under the processor runtime and appears in the Utility category.
- Android tests are opt-in and use private bindings. The read-only and control projects document their separate requirements; state-changing cases require explicit settings and observed restoration.
- Hosted CI builds the packages, runs desktop tests and checks that Android fixtures stay skipped without workflow context. Hardware execution remains a separate capability and is never inferred from a hosted pass.

Private test inputs, credentials and raw household evidence are excluded from the repository. Test counts should be read from discovery and retained results, rather than used as fixed CI thresholds.
