# WiserHeatCrestronDriver

A **Crestron Home** platform driver that integrates a **Drayton Wiser Heating** hub and exposes discovered rooms as managed child thermostat devices.

Drayton, Wiser, and Schneider Electric are trademarks of Schneider Electric SE, its subsidiaries, or affiliated companies. This project is an independent, unofficial Crestron Home driver and is not affiliated with or endorsed by Schneider Electric or Crestron.

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

---

## Prerequisites

| Requirement | Details |
|---|---|
| Crestron Home processor | Running a firmware version compatible with extension drivers |
| Wiser hub | Local-network-accessible Drayton Wiser hub |
| Hub secret | Required for authenticating with the local Wiser API |

---

## Installation

Preferred download source: use the attached `Thermostat_WiserHeat_IP_V2.pkg` asset from the relevant GitHub Release. The automatic GitHub `Source code (zip)` and `Source code (tar.gz)` assets are repository snapshots, not installable Crestron driver packages.

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
2. Bumps `DriverVersion` and `VersionDate` in `Thermostat_WiserHeat_IP_V2.json`
3. Merges runtime dependencies into the driver assembly
4. Patches the merged assembly for Crestron runtime compatibility
5. Packages everything into `Thermostat_WiserHeat_IP_V2.pkg`

### GitHub Release Asset

This repository includes a GitHub Actions workflow that builds and attaches the `.pkg` when a GitHub Release is published.

For end users, GitHub Releases are the preferred download point: download the attached `Thermostat_WiserHeat_IP_V2.pkg` asset, not the automatic source archive assets.

Typical release flow:

1. Push the release commit and tag.
2. Publish a GitHub Release for that tag.
3. Let the workflow build and attach the `.pkg` asset.

---

## Notes

When changing entity shape, UI definitions, or child-device property surfaces, Crestron Home may retain stale child metadata on an existing processor. If runtime UI behavior does not match the current build after such a change, remove and re-add the child device before assuming the driver logic is wrong.

---

## License

MIT + Commons Clause © 2026 Neil Colvin — see [LICENSE](LICENSE).

Free to use and modify. You may not sell the Software as a standalone product or sublicense it. Commercial system integration and commissioning work is permitted, provided the Software itself is not sold as a standalone product.

> **Note:** This project references [Crestron.DeviceDrivers.DevKit](https://www.nuget.org/packages/Crestron.DeviceDrivers.DevKit), which is subject to Crestron's SDK license agreement. That license governs the SDK libraries only; the source code in this repository is licensed independently under the terms above.
