# Development and validation history

See the [product changelog](CHANGELOG.md) for shipped changes. This document preserves test, CI, build and submission preparation history. Dated development entries describe work at that time, not a published product version or completed acceptance. Version headings identify the release alongside which development work was recorded; processor-test versions identify separate test packages.

## Where changes belong

- Product changelog and product release notes: shipped behavior, API, compatibility, fixes and runtime dependencies. Mention validation briefly when it helps explain a fix.
- This history: test coverage, CI, build tooling, test-package releases and work on pending candidates. Split mixed entries so the product effect remains easy to find.
- Testing and workflow guides: current setup and operating instructions. Submission guides, where applicable: preparation, evidence and acceptance status.
- Test-only or documentation-only changes do not require a product release. Processor-test releases update this history, not the product changelog.

<!-- development-history -->

## Editor recovery validation - 2026-09-17 (no driver release)

- Add explicitly selected day/time interruption cases with independent restoration assertions. Both passed on the development processor; an interrupted operation stays failed even when its recovery assertion succeeds.
- Use the verified extension property/command routes for editor and mode recovery, with observed state confirmation. Correct the save fixture's corrupted degree-symbol expectation. These changes affect test producers only.
- The corrected Save Day/Save All case passed on the exact development package with independent hub checks, complete schedule/editor restoration and temporary-child cleanup. No final candidate or conflict coverage is claimed.
- All ten Android cases correctly stay offline without a workflow context. See [validation status](submission/ValidationStatus.md) for the exact hardware scope and outstanding acceptance work.



## Submission candidate development - 2026-09-17 (not included in 1.3.7)

These are incremental development records, not a released driver version. Earlier open investigations describe their recorded point in time; see [current validation status](submission/ValidationStatus.md) for resolved issues and remaining work.

- Add read-only `lastHubRefreshUtc` and `driverLifetimeId` diagnostics for monitoring. Successful current-generation hub reads advance the explicitly tagged UTC timestamp; cached and failed reads do not. Recreating the root changes its lifetime identity even when the package and processor boot are unchanged. Use the documented HVAC category for managed thermostats.
- Add a separate .NET 10 endurance producer and its offline tests to the solution and CI, consuming released DevTools 1.7.0. Short real-function checks on Debug `1.3.007.0026` passed with exact active payload, fresh independent hub state and standard evidence export. A deliberate same-version root reload invalidated the previous lifetime as expected. This is not final-candidate endurance or submission acceptance.

- Restore existing room readiness and online state after a successful gateway reconnect without replacing their controllers. A stopped room previously remained not ready after restarting. Regression tests reproduce the failure, verify recovery and ensure failed refreshes do not revive offline rooms. Debug candidate `1.3.007.0014` passed the full workflow, including all six Android cases with released TestAdapter 1.9.0, both UI saves and exact state restoration. Initial room commissioning remains a separate investigation.
- Preserve pending schedule edits across ordinary hub polls. Detect changed or reassigned schedules and refresh the hub before a save so an old editor cannot overwrite an observed newer schedule. Cancel reloads the current hub values. Driver regression tests cover these cases and Save Day/Save All scope. Debug candidate `1.3.007.0013` passed the live editor-selection/Cancel case and a subsequent focused Save Day/Save All case with exact original schedule restoration.
- Add separately configured schedule-save UI tests and restoration checks. Default isolation uses a new temporary schedule; an explicit alternative operates an existing schedule only when exclusively assigned to the selected room. The latter passed on hardware after the hub rejected additional schedule creation. Keep failed attempts and recovery evidence; offline regressions cover shared-schedule refusal, uncertain replies, ignored writes, cancellation and unrelated changes. Consume TestAdapter 1.9.0 for labelled-row controls. This test work adds no production behavior beyond the existing development candidate and does not complete the submission plan.
- Publish changed day, time, temperature and conditional slot values immediately when an editor selection changes. SDK event regressions reproduced missing notifications before the correction. Require a successful fresh schedule read after Save and compare the observed day data with the requested values; unconfirmed saves retain pending edits and report an error.
- Remove read-triggered withdrawal and re-registration of room controllers. Existing room reads preserve their UI registration, while actual room discovery/removal still updates the controller list. A complete Debug workflow has passed with the registration fix.
- Validate the opt-in room mode-control UI cycle with a saved manual target different from the current target. Restore the saved target and scheduled occupancy readings; retain failed-run evidence and verified recovery. A per-room `AllowManualTargetInitialization` option now supports the no-saved-target test path: it verifies initialization from the active target and restoration of Auto/schedule settings, while reporting any retained inactive target separately from exact restoration. The option defaults to false, passes offline checks and has not yet been exercised on a room without a saved target.
- Add independent schedule/editor checks for the live Cancel test. Two Debug attempts failed before completion; both restored the editor, hub settings and Home, and released reservations. The corrected driver passed the subsequent complete Debug workflow, including all five Android cases, restored state, temporary test-instance removal and released reservations. Package cleanup preserved an archive that predated that run. This development work does not complete the official submission test plan or produce a Crestron submission.

## Submission candidate development - 2026-09-16 (not included in 1.3.7)

- Add a separate opt-in Android control project for an independently observed Auto/Manual/Auto schedule cycle, with durable intents and bounded restoration. Initial offline failure-path checks passed; subsequent live validation is recorded in the 17 September entry. Existing manual targets below, equal to or above the scheduled target are supported and preserved; absent manual targets require a separate initialization policy. Existing inspection workflows remain read-only.
- Fix schedule, day and time selection dialogs so their choice lists and selected values are available to the Home app. The complete lists were checked in the minimized Google Android emulator.
- Shorten the hot-water actions to Turn On and Turn Off, and the shared schedule action to Save All, so their meaning remains visible on portrait screens. Save All applies the selected day's entries to all days of the selected shared schedule.
- Extend the Wiser Android fixture to inspect the thermostat, Schedule and Edit Schedule pages before checking the full selection lists. Use the published TestAdapter 1.8.0; Wiser page names and expected values remain in this repository.
- The complete Debug workflow passed local and processor tests, live hub reads, the installed driver update, health checks and every discovered Android case. Checked state and Home were restored; the temporary test instance and uploaded archive were removed. These runtime fixes await a driver patch release and final Release-candidate validation.
- Prepare submission help and reviewed dependency notices. Public help screenshots, completed acceptance evidence and signed submission remain separate requirements; this source update is not a Crestron submission.

## Android UI test development - 2026-09-16 (no driver release)

- Optional private name binding temporarily renames the gateway, observes the corresponding app tile and restores the name. Uses DevTools 1.5.0 and TestAdapter 1.7.1; both cases passed in the minimized Google emulator with the original inventory and observed states preserved. No physical control commands or new driver release.
- Add an opt-in NUnit project that opens the gateway page twice, checks the Hot Water and Away status/action/enabled state against fresh management data, verifies unchanged gateway state and returns to Home.
- Keep settings and captures private. Ordinary desktop runs skip without connecting; hosted CI verifies this behavior. Include the project in the solution and consume the Crestron Home NUnit UI automation library from the test adapter package, eliminating the source-checkout dependency. Use test adapter 1.7.0 from NuGet. The complete packaged development workflow passed; submission evidence acceptance remains separate work.

## WiserHeatCrestronDriver.ProcessorTests v1.0.2 - 2026-09-16

Published processor test package on GitHub. This is a test-package release only; no driver or library NuGet package is published. See the matching package release notes for changes and validation.

## 1.3.7 - 2026-09-16

- Add read-only observation and exact-package configuration probes, refusal/restoration tests, and documentation.

- Validate existing-room Auto/Manual/Auto restoration and a complete production update, intentional failed check and verified code rollback. Preserve the existing tile and current configuration.

## Offline release workflow option - 2026-09-15 (no package release)

- Allow an explicit manual release when local hardware or the self-hosted runner is unavailable, with the reason and exact source recorded in the workflow summary.
- Keep hosted source validation mandatory and preserve all build, test and packaging steps. No runtime, API or package-version changes.

## CI package cleanup - 2026-09-15 (no driver or processor package release)

- Update Test Explorer workflow containers to CrestronHomeNUnit.TestAdapter 1.3.0 and document opt-in storage cleanup after successful CI runs.
- Retain original deployment filenames, protect pre-existing/manual packages and preserve failed-run evidence. Cleanup frees archive storage without rebooting; Home can retain cached catalogue entries until its next planned reboot.
- Actual driver/library code and processor test packages are unchanged by this tooling update.

## 1.3.6 - 2026-09-15

- Replace duplicated test-count constants with discovery-to-execution and source-to-package identity checks; adding tests no longer requires editing CI totals.

- Add eight command-refresh regression cases; 47 local and 47 processor tests, three live hub checks and three installed-driver health checks passed. The hot-water interaction was also verified manually.

## CI validation - 2026-09-15 (no package release)

- Revalidate the current default-branch source after successful release workflows, including version commits created by GitHub Actions.
- Allow maintainers to configure exact-source, App-specific checks that must pass before publishing through `RELEASE_REQUIRED_CHECKS`; missing, failed or unconfirmed checks block the release.

## WiserHeatCrestronDriver.ProcessorTests v1.0.1 - 2026-09-15

Published processor test package on GitHub. This is a test-package release only; no driver or library NuGet package is published. See the matching package release notes for changes and validation.

## 2026-09-15 - Test and development tooling (no driver release)

- Add the published Test Explorer workflow adapter, offline discovery CI and independent GitHub processor-test releases. Private workflow plans control optional live tests, actual-driver updates and temporary-instance cleanup.

- Add three opt-in live driver checks for authenticated Wiser hub discovery, room identity/telemetry refresh and reconnect. Share the library's private live settings. Include a separate Live Hub processor suite; no room control commands are sent.
- Coordinate build deployment through the shared DevTools processor reservation.

## 1.3.5 — 2026-09-14

- Cover room discovery, stable child identity, renamed/removed rooms, cleared settings, overlapping connections and late refresh/login completion. Clearing or disposing the platform now removes its children and prevents old work from restoring them. Update WiserHeatAPIv2 to 1.1.0.6.

- Normalize the working manifest from `1.0.008.0102` to `1.3.004.0102`; this aligns the development version family with the latest existing three-part release. No historical tags or packages are changed.

- Standardize driver versioning: Debug project/package metadata follows the manifest including its build increment; local Release builds preserve it; three-part release tags select the exact CI release without another patch increment. Verify source and built package versions before publication.

- Expand driver coverage to 29 offline tests and 10 SDK entity/lifecycle tests, with a desktop SDK harness and the same lifecycle fixtures in the net472 processor package.

- Add 29 NUnit driver unit tests and a processor lifecycle suite in the existing solution.

- Add a standalone Utility processor test package with private Debug deployment settings.