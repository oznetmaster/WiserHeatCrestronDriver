# WiserHeatCrestronDriver 1.3.17

This patch corrects the dependency and lifecycle issues described below.

- Use WiserHeatAPIv2 1.1.4.
- Serialize room commands and complete shared-schedule read/validate/write/readback transactions through the connection queue.
- Reject queued operations for replaced connections, drain active work before client retirement and suppress stopped-room completions. Publish owned schedule snapshots for UI reads.
- Compile diagnostic breadcrumbs out of Release builds while retaining warnings and errors; preserve room identities and schedule-conflict checks.

## Installation

Use the driver package from [GitHub](https://github.com/oznetmaster/WiserHeatCrestronDriver/releases/tag/v1.3.17) or the matching NuGet distribution. Import the package, then update the existing driver in Home Setup or Configure Pro. Importing alone does not update the running instance. Existing pairing and configuration formats are retained; removal and re-add is not a routine update step.

## Validation

Passed 236 workstation Debug and 236 Release test executions, plus 201 native net472/Mono processor tests. Processor validation used fake transports and synthetic credentials; no new physical-device acceptance or endurance run is claimed. Existing processor configuration and all pairing files were preserved. See [development history](DEVELOPMENT-HISTORY.md) for provenance and limits.

This GitHub/NuGet release does not replace any submitted Crestron package. Submission-specific PDFs and correspondence are excluded from public artifacts.
