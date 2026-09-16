# WiserHeatCrestronDriver

See the [changelog](CHANGELOG.md) for release history and the [release notes](RELEASE-NOTES.md) for the current driver update. Driver releases are made for runtime fixes or dependency changes; adding tests alone does not require a driver release.

The [Crestron submission help source](submission/README.md) is being prepared separately. It is a review draft and is not included in the current release or a completed portal submission.

A **Crestron Home** platform driver that integrates a **Drayton Wiser Heating** hub and exposes discovered rooms as managed child thermostat devices.

Drayton, Wiser, and Schneider Electric are trademarks of Schneider Electric SE, its subsidiaries, or affiliated companies. This project is an independent, unofficial Crestron Home driver and is not affiliated with or endorsed by Schneider Electric or Crestron.

Crestron and Crestron Home are trademarks or registered trademarks of Crestron Electronics, Inc. This project is not affiliated with, endorsed by, or sponsored by Crestron Electronics, Inc.

[![License: MIT + Commons Clause](https://img.shields.io/badge/License-MIT%20%2B%20Commons%20Clause-blue.svg)](LICENSE)

---

## Driver Architecture

This driver is a **platform driver**. It connects to a local Wiser hub, discovers rooms, and registers each room as a managed child thermostat entity under a single Crestron Home device entry.

The driver is implemented using the **Crestron Home SDK V2 Entity Model**. It derives from `ReflectedAttributeDriverEntity` and uses SDK attributes for commands and properties, while packaging the deliverable as a standard Crestron Home `.pkg` driver.

---

## Features

- Connects directly to a local Wiser hub using hub IP/hostname and secret
- Discovers Wiser rooms and exposes them as managed thermostat child devices
- Heat-only thermostat UI for each room
- Current temperature display and target temperature adjustment
- Room boost control
- Schedule enable/disable control
- Schedule assignment and schedule selection UI
- Shared schedule day editing from the Crestron Home UI
- Dynamic room tile icons reflecting heating, scheduled, or regular state
- Optional whole-house hot water control on the platform options page
- Optional global Away Mode control on the platform options page

---

## Prerequisites

| Requirement | Details |
|---|---|
| Crestron Home processor | Running a firmware version compatible with extension drivers |
| Wiser hub | Local-network-accessible Drayton Wiser hub |
| Hub secret | Required for authenticating with the local Wiser API |

---

## Installation

The best way to download and install this driver on a Crestron Home system is to use the [Crestron Home Driver Feed Installer](https://github.com/oznetmaster/Crestron-Home-Driver-Feed-Installer) repository and application.

If you prefer to install manually, use the attached `Thermostat_WiserHeat_IP_V2.pkg` asset from the relevant GitHub Release. The automatic GitHub `Source code (zip)` and `Source code (tar.gz)` assets are repository snapshots, not installable Crestron driver packages.

NuGet package availability: this driver is also published as the `CrestronHomeDriver.Wiser.WiserHeat` NuGet package. This NuGet package conforms to the **Crestron Home Driver NuGet Publishing Standard v1**. It is a distribution wrapper for the final `Thermostat_WiserHeat_IP_V2.pkg` artifact, includes the required `crestron-driver-package.json` manifest, and is not intended as a direct DLL reference package.

Crestron Home Driver NuGet Publishing Standard v1 is **not** an official Crestron product or specification. It is an open source packaging standard created to facilitate community distribution and discovery of Crestron Home drivers through NuGet.

1. Download `Thermostat_WiserHeat_IP_V2.pkg` from the GitHub Release assets, or build it yourself using the instructions in [Building from Source](#building-from-source).
2. Upload the `.pkg` to your Crestron Home processor manually, for example via SFTP to `/user/ThirdPartyDrivers/Import`.
3. In the Crestron Home setup workflow, add the Wiser platform driver.
4. Enter the required configuration values:

| Field | Description |
|---|---|
| Hub IP Address / Hostname | LAN IP address or hostname of the Wiser hub |
| Hub Secret | Wiser system secret used to authenticate to the local API |
| Temperature Units | `Celsius` or `Fahrenheit` for thermostat display |
| Boost Delta | Temperature increase applied when boost is triggered |
| Boost Duration Minutes | Duration of a timed room boost |
| Enable Whole House Hot Water Control | Adds a platform-level domestic hot water toggle |
| Allow Away Mode | Adds a platform-level Away Mode toggle |

When either platform option is enabled, the root platform tile opens a `Wiser Heat Options` page. The verified behavior is:

- Whole-house hot water and Away Mode both work from the platform options page.
- Away Mode is a global system mode, but individual room and hot water settings can still be overridden afterward.
- State-changing controls reject repeat presses while a command is already in flight to avoid double-push races.

### Obtaining the Wiser Secret

The Wiser hub secret is required to authenticate with the local API.

Reference: https://it.knightnet.org.uk/kb/nr-qa/drayton-wiser-heating-control/#controlling-the-system

Typical process:

1. Press the setup button on the HeatHub so the indicator starts flashing.
2. Connect to the temporary Wi-Fi network exposed by the hub.
3. Open `http://192.168.8.1/secret` in a browser.
4. Save the returned secret securely.
5. Return the hub to normal operation.

---

## Building from Source

### Dependencies

- [WiserHeatAPIv2](https://www.nuget.org/packages/WiserHeatAPIv2) NuGet package
- [Crestron.DeviceDrivers.DevKit](https://www.nuget.org/packages/Crestron.DeviceDrivers.DevKit) NuGet package
- `.NET Framework 4.7.2`
- `ManifestUtil.exe` from the Crestron Driver SDK
- `ILRepackMerge.ps1` and `PatchMergedAssembly.ps1` in this repository for dependency merge and patching

### Build

```powershell
dotnet build WiserHeatCrestronDriver/WiserHeatCrestronDriver.csproj -c Release
```

The build pipeline:
1. Compiles the driver targeting `net472`
2. Reads the manifest version; Debug builds increment its fourth component, while local Release builds preserve it and release CI verifies the selected tag
3. Merges runtime dependencies into the driver assembly
4. Patches the merged assembly for Crestron runtime compatibility
5. Packages everything into `Thermostat_WiserHeat_IP_V2.pkg`

### GitHub Release Asset

This repository includes a GitHub Actions workflow that builds and attaches the `.pkg` when a GitHub Release is published.

The same release workflow also publishes the `CrestronHomeDriver.Wiser.WiserHeat` NuGet package, which wraps the final `Thermostat_WiserHeat_IP_V2.pkg` artifact.

Typical release flow:

1. Push the release commit and tag.
2. Publish a GitHub Release for that tag.
3. Let the workflow build and attach the `.pkg` asset.

When publishing a release, include release notes mentioning the verified platform options behavior, specifically that whole-house hot water and Away Mode both work and that Away Mode does not prevent later individual overrides.

---

## Notes

When changing entity shape, UI definitions, or child-device property surfaces, Crestron Home may retain stale child metadata on an existing processor. If runtime UI behavior does not match the current build after such a change, remove and re-add the child device before assuming the driver logic is wrong.

---

## License

MIT + Commons Clause © 2026 Neil Colvin — see [LICENSE](LICENSE).

Free to use and modify. You may not sell the Software as a standalone product or sublicense it. Commercial system integration and commissioning work is permitted, provided the Software itself is not sold as a standalone product.

> **Note:** This project references [Crestron.DeviceDrivers.DevKit](https://www.nuget.org/packages/Crestron.DeviceDrivers.DevKit), which is subject to Crestron's SDK license agreement. That license governs the SDK libraries only; the source code in this repository is licensed independently under the terms above.



## Automated tests

The solution includes `WiserHeatCrestronDriver.Tests` (NUnit 4 with the Visual Studio NUnit adapter) and `WiserHeatCrestronDriver.ProcessorTests` (a standalone Crestron Home Utility test package). The 29 offline tests exercise driver logic without credentials or real device commands. The 18 processor lifecycle cases are excluded on Windows in this project; the dedicated desktop SDK harness exercises the same fixture sources.

```powershell
dotnet test WiserHeatCrestronDriver.Tests/WiserHeatCrestronDriver.Tests.csproj -c Release
```

Build the processor project in Debug in Visual Studio to build and deploy using private deployment settings. See [processor test instructions](WiserHeatCrestronDriver.ProcessorTests/README.md) for setup, suites, tile operation and UI separation. Processor packages are not published to NuGet. See [CHANGELOG](CHANGELOG.md) for changes.


### Command state refresh

After a state-changing command completes, the driver reads fresh hub state before publishing the result. Hot-water and Away buttons therefore show the observed state before they become available again; boost, schedule and setpoint controls also refresh immediately. Routine polling remains throttled.

### Expanded driver behavior tests

Cover room discovery, stable child identity, renamed/removed rooms, cleared settings, overlapping connections and late refresh/login completion. Clearing or disposing the platform now removes its children and prevents old work from restoring them. Update WiserHeatAPIv2 to 1.1.0.6.

Saved schedules can be reopened and saved again; integer lists and arrays are copied independently before editing; the root entity can be created and disposed repeatedly.

The current package contains **29 offline tests**, **18 SDK entity/lifecycle tests** and **3 optional live hub tests**. The processor package remains **net472 only**, appears under **Utility** in Configure, and can run independently through its own tile or the Windows NUnit runner. The offline and lifecycle fixtures use synthetic data. The separate live suite authenticates with the selected hub, discovers rooms, refreshes telemetry and reconnects; it sends no room-control commands.

`WiserHeatCrestronDriver.Lifecycle.Tests` runs the entity checks against the real desktop SDK on .NET 10. It compiles the relevant driver sources and shares fixture sources with the net472 processor tests. Building this project does not deploy a driver. A locally supplied `Newtonsoft.Json.Compact.dll` is needed by the SDK's manifest reader; it is supplied by the processor at runtime and must not be added to source control or bundled with the processor test package.

```powershell
dotnet test WiserHeatCrestronDriver.Tests/WiserHeatCrestronDriver.Tests.csproj --filter "TestCategory!=Processor"
dotnet test WiserHeatCrestronDriver.Lifecycle.Tests/WiserHeatCrestronDriver.Lifecycle.Tests.csproj
```

Set `CompactJsonPath` in the desktop test project's private `DesktopTest.Local.props`, excluded through `.git/info/exclude`, or pass it as an MSBuild property. Keep machine paths and credentials out of tracked files.

Desktop success does not establish Mono compatibility. Build the processor test project in Visual Studio, deploy it, and run both suites on the processor. The fixtures cover configuration, restoration, refresh/reconnect races and disposal using simulated responses. Installed-driver health remains a separate gate from these test-host fixtures.


### Driver build and release versions

The driver's JSON manifest is the source of its four-component build version. Debug builds increment only the fourth component; for example, `2.0.001.0005` becomes `2.0.001.0006`. MSBuild's `Version` and default `PackageVersion` are derived from that same manifest and refreshed after the increment; their numeric form is `2.0.1.6`. Assembly binding versions remain separate. Test-only references and IDE design-time builds do not increment the production driver version.

GitHub tags and NuGet releases retain three components: `v2.0.1` and `2.0.1`. Prepare the manifest's first three components for the intended release before tagging. Release CI checks that the tag matches, resets the fourth component to zero, and verifies the generated `.pkg` version against the manifest and release version before publishing. It does not increment the selected patch again. Local Release builds preserve the manifest. A later Debug build can legitimately be newer than a published release; the processor test package has its own independent version.

Deployment validation compares the exact built `.pkg` against the imported catalogue entry and installed instance, numerically including all four components. Upload/import alone does not activate the new version. Keep the tested package and its hash: rebuilding creates a new artifact that must be validated again.

Run `pwsh -File tools/Test-DriverVersioning.ps1` to check these rules with temporary manifests; this does not change the working driver manifest or deploy anything.

See [versioning details](docs/Versioning.md) for build, release and installed-instance verification rules.
### Desktop SDK dependency in CI

The SDK's desktop manifest reader needs its `Newtonsoft.Json.Compact.dll` runtime dependency. Supply a local SDK/runtime copy through the `CompactJsonPath` MSBuild property (or private `DesktopTest.Local.props`). Maintainer CI restores the same verified copy from encrypted Actions secrets into its temporary directory; it is not committed, attached to release assets or included in processor packages. Fork pull requests do not receive these secrets and require a trusted maintainer validation run.


For automated local tests, processor tests and gated driver deployment, see the [Crestron Home NUnit CI development guide](https://github.com/oznetmaster/CrestronHomeNUnit/blob/HEAD/docs/ContinuousIntegration.md). It covers private configuration, live-test gates, install/update waits, results and optional test-package removal.

Local build/deployment overrides can be created by copying [WiserHeatCrestronDriver.Local.targets.example](WiserHeatCrestronDriver/WiserHeatCrestronDriver.Local.targets.example) to `WiserHeatCrestronDriver.Local.targets` beside the project. Fill in your own paths privately and exclude the resulting local file with `.git/info/exclude`; it is not part of the published source.

Optional development probes are described in [installed room controls](docs/InstalledRoomControls.md) and [configuration-preserving rollback](docs/ConfigurationRollback.md). Their hardware validation status and restrictions are recorded in those guides.

### Optional live hub tests

Use [LiveTestSettings.example.json](WiserHeatCrestronDriver.Tests/LiveTestSettings.example.json) as the public template. The driver tests share the library's private `%LOCALAPPDATA%/WiserHeatAPIv2/LiveTestSettings.json` file (`enabled`, `hubHost`, `secret`). Keep the real file outside the repository or in `.git/info/exclude`; it is never embedded in the processor package.

On Windows, run the `Live` category in the .NET 10 desktop SDK harness with private settings enabled, or pass NUnit parameter `EnableLiveTests=true`. Ordinary CI should filter `TestCategory!=Live`. On the processor, select **Live Hub**, supply the private JSON through the runner's **Test inputs**, and run that suite. Its explicit runner enablement applies only to that run. The library's optional room-control settings are ignored by these read-only driver tests.
The complete initial-installation workflow has been validated on a development processor: local unit/lifecycle tests, processor tests and read-only live hub tests, then actual-driver installation/configuration and configured/online/ready checks. New installations use the planned Connection and HeatSettings wizard steps with explicit choices; see the NUnit CI guide for private initial-configuration files. This workflow is supported by the published NUnit tooling. No heating controls were operated.

## Visual Studio processor workflow

The solution includes [WiserHeatCrestronDriver.WorkflowTests](WiserHeatCrestronDriver.WorkflowTests/README.md), using the published Crestron Home Test Adapter. It exposes the complete gated workflow in Test Explorer while the ordinary NUnit fixtures remain available for local testing. Configure its private settings before execution; hosted CI verifies discovery without accessing hardware.


CI discovers test identities from the built source assembly and compares them with desktop results and the merged processor package. Each suite must be nonempty; missing or unexpected cases and unexpected skips fail validation. Test totals are reported, not maintained as build constants. Live cases are discovered but never executed by ordinary hosted CI.

## Publishing when local hardware is unavailable

The publish/release workflows support an explicit manual override when the processor or local self-hosted GitHub Actions runner is unavailable. Select `skip_hardware_checks` and provide a single-line `hardware_skip_reason`. Use the workflow's normal source and version controls. The override applies only to that invocation and is recorded with the exact source revision in its warning and job summary; it does not create a passing hardware-test result.

GitHub-hosted validation remains mandatory for the checked-out source, and the normal build, tests and packaging steps still run. Wait for the configured hosted workflows to pass, or run them on the same source revision first. None of these hosted checks needs the local runner or processor. Automatic tag/release-triggered runs retain the normal hardware checks; use a manual invocation of the updated release workflow when an offline override is needed.
