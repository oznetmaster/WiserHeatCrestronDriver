# WiserHeatCrestronDriver v1.3.8

This patch fixes schedule selection and editing, room readiness after reconnection, and command completion reporting. It updates WiserHeatAPIv2 to 1.1.1.

- Keep schedule choices current when hub schedules are added, renamed or removed, and retain the confirmed assignment when a selection is invalid, rejected or still pending.
- Preserve unsaved schedule edits during polling. Refuse a stale save when the shared schedule or room assignment has changed, and require refreshed hub state to confirm a successful save.
- Refresh editor values when changing days or editing a time or temperature. Reject non-finite temperatures and values outside the supported 5-35 degree range, while preserving normal half-degree rounding and Cancel behavior.
- Restore existing rooms' readiness after reconnecting. Use the SDK's HVAC category for the heating thermostat and avoid removing and re-registering room controllers during state reads.
- Report failed follow-up reads as unsuccessful room commands. Require a confirmed schedule assignment before switching an unassigned room to automatic control.
- Keep hot-water and schedule-save button captions visible on smaller displays. Use the repository's GitHub page as the public support contact.
- Expose the driver lifetime identifier and last successful hub-refresh timestamp for diagnostics.

## Validation

The gated development build `1.3.008.0001` passed desktop and processor regression suites, read-only live-hub checks and installed-driver checks after updating an existing gateway. The temporary test instance and package files were removed. Prior Android checks apply to the particular development packages identified in the validation history; these results do not establish final Crestron submission acceptance or a completed endurance test for this release.

See [CHANGELOG.md](CHANGELOG.md) for product changes and [development and validation history](DEVELOPMENT-HISTORY.md) for detailed test and CI work. Crestron submission preparation remains separate from this GitHub driver release.
