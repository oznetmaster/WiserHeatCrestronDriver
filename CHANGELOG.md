# Changelog

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