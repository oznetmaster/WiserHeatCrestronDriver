# Changelog

## WiserHeatCrestronDriver.ProcessorTests v1.0.2 - 2026-09-16

Published processor test package on GitHub. This is a test-package release only; no driver or library NuGet package is published. See the matching package release notes for changes and validation.

## 1.3.7 - 2026-09-16

- Guard schedule selection against overlapping room commands and expose room identity/activity for installed-driver verification.
- Add read-only observation and exact-package configuration probes, refusal/restoration tests, and documentation.
- Validate existing-room Auto/Manual/Auto restoration and a complete production update, intentional failed check and verified code rollback. Preserve the existing tile and current configuration.
- See [release notes](RELEASE-NOTES.md) for the exact validation scope and limitations.

## Offline release workflow option - 2026-09-15 (no package release)

- Allow an explicit manual release when local hardware or the self-hosted runner is unavailable, with the reason and exact source recorded in the workflow summary.
- Keep hosted source validation mandatory and preserve all build, test and packaging steps. No runtime, API or package-version changes.

## CI package cleanup - 2026-09-15 (no driver or processor package release)

- Update Test Explorer workflow containers to CrestronHomeNUnit.TestAdapter 1.3.0 and document opt-in storage cleanup after successful CI runs.
- Retain original deployment filenames, protect pre-existing/manual packages and preserve failed-run evidence. Cleanup frees archive storage without rebooting; Home can retain cached catalogue entries until its next planned reboot.
- Actual driver/library code and processor test packages are unchanged by this tooling update.

## 1.3.6 - 2026-09-15

- Replace duplicated test-count constants with discovery-to-execution and source-to-package identity checks; adding tests no longer requires editing CI totals.

- Read fresh hub state after hot-water, Away, boost/cancel, schedule advance and schedule enable/disable commands instead of waiting for the polling throttle. This fixes delayed UI updates and repeated hot-water button presses. Routine polling and the public API are unchanged.
- Add eight command-refresh regression cases; 47 local and 47 processor tests, three live hub checks and three installed-driver health checks passed. The hot-water interaction was also verified manually.
- See [release notes](RELEASE-NOTES.md) for validation and package details.


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

[Driver release notes](RELEASE-NOTES.md). Test-only changes do not require a driver release.

- Cover room discovery, stable child identity, renamed/removed rooms, cleared settings, overlapping connections and late refresh/login completion. Clearing or disposing the platform now removes its children and prevents old work from restoring them. Update WiserHeatAPIv2 to 1.1.0.6.

- Normalize the working manifest from `1.0.008.0102` to `1.3.004.0102`; this aligns the development version family with the latest existing three-part release. No historical tags or packages are changed.

- Standardize driver versioning: Debug project/package metadata follows the manifest including its build increment; local Release builds preserve it; three-part release tags select the exact CI release without another patch increment. Verify source and built package versions before publication.


- Expand driver coverage to 29 offline tests and 10 SDK entity/lifecycle tests, with a desktop SDK harness and the same lifecycle fixtures in the net472 processor package.
- Fix reopening locally saved schedules losing time slots, and editing a cloned schedule mutating shared integer collections. Schedule diagnostics now also recognize typed collections.

- Add 29 NUnit driver unit tests and a processor lifecycle suite in the existing solution.
- Add a standalone Utility processor test package with private Debug deployment settings.
- Fix compact three-digit schedule times such as `630` being rejected.