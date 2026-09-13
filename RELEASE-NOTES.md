# WiserHeatCrestronDriver v1.3.5

Patch release correcting lifecycle, configuration and recovery defects while preserving the public API and intended driver behavior.

## Fixes

- Clearing configuration or disposing the platform removes its room controllers and prevents delayed connections or refresh responses from restoring old rooms or status.
- A superseded connection cannot replace the current API client. Rediscovery preserves existing room identity while updating names and observed temperatures.
- Reopening saved schedules preserves time slots; edits to cloned schedules do not mutate the original schedule collections. Compact times such as `630` are accepted.
- Update WiserHeatAPIv2 to the already published 1.1.0.6 dependency.

## Tests and build process

- 29 offline tests and 10 SDK lifecycle tests. The current implementation passes on Windows in Debug and Release; both processor suites passed twice in the same host process.
- The shared net472 processor test package is available in the solution and appears under **Utility** in Configure. Its standalone Home tile and Windows NUnit runner select the test suites.
- Driver Debug build versions follow the manifest; three-part release tags select the CI release version. Test builds do not increment or deploy the production driver.
- Processor test packages are not published to NuGet. Private deployment settings, live inputs and desktop SDK runtime dependencies are excluded from source and release assets.

## Installation and documentation

The GitHub release includes the production driver package and a separate processor test package. The test package appears under Utility in Configure and is not included in the driver NuGet package. See [CHANGELOG.md](CHANGELOG.md) for release history and [README.md](README.md) for installation and testing.
