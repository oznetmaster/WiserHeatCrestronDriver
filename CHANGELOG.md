# Changelog

This changelog records shipped features, fixes, compatibility and runtime dependency changes. See [development and validation history](DEVELOPMENT-HISTORY.md) for tests, CI, build tooling and work not yet released.

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