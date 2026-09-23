# WiserHeatCrestronDriver v1.3.16

This patch updates WiserHeatAPIv2 to 1.1.3 so a hub that stalls partway through an HTTP response cannot bypass the driver's read-cancellation deadline on Mono. It covers both successful and error response bodies. Saved hub and room configuration remains compatible.

The schedule editor now has eight entries per day, matching the hub's supported limit; the unused ninth and tenth rows have been removed.

Install the attached `Thermostat_WiserHeat_IP_V2.pkg` through the normal driver update process. The `CrestronHomeDriver.Wiser.WiserHeat` NuGet distribution wraps that installable package and is not a library reference.

See [CHANGELOG.md](CHANGELOG.md) for product changes and [development history](DEVELOPMENT-HISTORY.md) for validation scope.

## Dealer support

For installation questions, driver problems or support requests, contact Neil Colvin using the [Wiser Heat driver support form](https://github.com/oznetmaster/WiserHeatCrestronDriver/issues/new). A free GitHub account is required to submit the form. Requests are public: do not include hub secrets, passwords or other credentials.

Include the driver version, Crestron Home version, Wiser hub model and a description of the issue. The [existing support requests](https://github.com/oznetmaster/WiserHeatCrestronDriver/issues) may also contain an answer.
