# WiserHeatCrestronDriver v1.3.16

This patch updates WiserHeatAPIv2 to 1.1.3 so a hub that stalls partway through an HTTP response cannot bypass the driver's read-cancellation deadline on Mono. It covers both successful and error response bodies. Saved hub and room configuration remains compatible.

The schedule editor now has eight entries per day, matching the hub's supported limit; the unused ninth and tenth rows have been removed.

Install the attached `Thermostat_WiserHeat_IP_V2.pkg` through the normal driver update process. The `CrestronHomeDriver.Wiser.WiserHeat` NuGet distribution wraps that installable package and is not a library reference.

See [CHANGELOG.md](CHANGELOG.md) for product changes and [development history](DEVELOPMENT-HISTORY.md) for validation scope.
