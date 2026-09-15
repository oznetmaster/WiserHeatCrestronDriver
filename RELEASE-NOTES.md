# WiserHeatCrestronDriver v1.3.6

Patch release fixing delayed UI state after Wiser control commands. No public API or dependency changes.

## Fix

Successful hot-water, Away mode, room boost/cancel, schedule advance and schedule enable/disable commands now read fresh hub state immediately. Previously the normal polling throttle could reuse an old snapshot, re-enable the hot-water button with its previous state and make another press repeat the same command. Setpoint and schedule-edit commands already refreshed immediately and retain that behavior. Routine polling remains unchanged.

## Validation

- 47 local and 47 processor unit/entity tests passed, including eight command-refresh regressions.
- Three read-only live hub tests passed, followed by three installed-driver health checks.
- The installed driver was updated without rebooting or changing its configuration. A user-operated hot-water toggle updated promptly after one press.

The processor test package contains 29 unit tests, 18 lifecycle tests and three optional read-only live hub tests. Processor packages remain GitHub-only and private settings are excluded from source and packages.

See [CHANGELOG.md](CHANGELOG.md) for history and [README.md](README.md) for installation and testing.