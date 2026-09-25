# WiserHeatCrestronDriver v1.3.16

**Published by Crestron:** Wiser Heat **1.3.16** was added to the Crestron production driver database on **25 September 2026** (catalog version `1.3.016.0000`).

Catalog entry: **Drayton Wiser / Wiser Heat Gateway**; device type **Platform**; developer **Neil Colvin**.

This patch updates WiserHeatAPIv2 to 1.1.3 so a hub that stalls partway through an HTTP response cannot bypass the driver's read-cancellation deadline on Mono. It covers both successful and error response bodies. Saved hub and room configuration remains compatible.

The schedule editor now has eight entries per day, matching the hub's supported limit; the unused ninth and tenth rows have been removed.

Install the attached `Thermostat_WiserHeat_IP_V2.pkg` through the normal driver update process. The `CrestronHomeDriver.Wiser.WiserHeat` NuGet distribution wraps that installable package and is not a library reference.

See [CHANGELOG.md](CHANGELOG.md) for product changes and [development history](DEVELOPMENT-HISTORY.md) for validation scope.

## Dealer support

**Support:** Contact Neil Colvin using the [driver and library support form](https://oznetmaster.github.io/support/). No GitHub account or registration is required. Requests are delivered privately; please do not include hub secrets, passwords or other credentials.

**Project:** The [Wiser Heat driver repository](https://github.com/oznetmaster/WiserHeatCrestronDriver) contains documentation, releases and public issue tracking. A GitHub account is required only if you choose to create a public issue.

Include the driver version, Crestron Home version, Wiser hub model and a description of the issue.
