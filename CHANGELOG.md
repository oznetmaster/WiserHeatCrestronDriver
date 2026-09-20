# Changelog

This changelog records shipped features, fixes, compatibility and runtime dependency changes. See [development and validation history](DEVELOPMENT-HISTORY.md) for tests, CI, build tooling and work not yet released.

## 1.3.15 - 2026-09-20

- Detect loss of communication with the hub, mark the gateway and its thermostats offline, and recover automatically after a successful fresh read. Preserve room controllers and saved settings during the interruption.
- Correct Celsius/Fahrenheit display, conversion, temperature limits and endpoint handling. Show Off as a state and preserve automatic control when changing a target during a schedule-index rebuild.
- Load the correct embedded manifest when the package assembly is renamed, and suppress unchanged polling diagnostics.
- Update WiserHeatAPIv2 to 1.1.2.

See [release notes](RELEASE-NOTES.md) for validation and [development history](DEVELOPMENT-HISTORY.md) for the intervening builds and test work.

## 1.3.8 - 2026-09-18

- Keep schedule choices current and retain the confirmed assignment when a selection is invalid, rejected or pending.
- Preserve unsaved schedule edits during polling, refuse stale saves, refresh displayed editor values and enforce the supported temperature range.
- Restore room readiness after reconnection and avoid re-registering room controllers during state reads.
- Require successful follow-up reads for room commands and a confirmed schedule assignment before enabling automatic control. Update WiserHeatAPIv2 to 1.1.1 for schedule failure reporting.
- Keep action captions visible, use GitHub for public support, and expose driver lifetime and successful hub-refresh diagnostics.

See [release notes](RELEASE-NOTES.md) for validation scope. Test and CI details remain in [development history](DEVELOPMENT-HISTORY.md).

## 1.3.7 - 2026-09-16

- Guard schedule selection against overlapping room commands and expose room identity/activity for installed-driver verification.

- See [release notes](RELEASE-NOTES.md) for the exact validation scope and limitations.

## 1.3.6 - 2026-09-15

- Read fresh hub state after hot-water, Away, boost/cancel, schedule advance and schedule enable/disable commands instead of waiting for the polling throttle. This fixes delayed UI updates and repeated hot-water button presses. Routine polling and the public API are unchanged.

- See [release notes](RELEASE-NOTES.md) for validation and package details.

## 1.3.5 — 2026-09-14

[Driver release notes](RELEASE-NOTES.md). Test-only changes do not require a driver release.

- Clearing or disposing the platform now removes its children and prevents old work from restoring them. Update WiserHeatAPIv2 to 1.1.0.6.

- Fix reopening locally saved schedules losing time slots, and editing a cloned schedule mutating shared integer collections. Schedule diagnostics now also recognize typed collections.

- Fix compact three-digit schedule times such as `630` being rejected.