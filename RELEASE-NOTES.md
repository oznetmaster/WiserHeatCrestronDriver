# WiserHeatCrestronDriver v1.3.15

This patch fixes hub offline detection and automatic recovery, thermostat temperature handling and startup with a renamed package. It includes the product fixes developed since public version 1.3.8; versions 1.3.9 through 1.3.14 were intermediate candidates.

- Request fresh hub data approximately every ten seconds. A failed bounded read marks the gateway and its room thermostats offline; polling continues and a successful fresh read restores availability without user input.
- Keep installed room controllers and saved configuration throughout a communication interruption, and ignore late state updates from a replaced connection.
- Correct Celsius/Fahrenheit conversion, display and supported heating limits of 5-30 C / 41-86 F. Fahrenheit controls use whole-degree input steps; physical targets retain the hub's half-degree Celsius resolution. Accept tiny floating-point roundoff at valid endpoints.
- Display Off without treating it as a numeric temperature. Preserve an Auto room's control mode when its target changes while the global schedule index is rebuilding.
- Resolve the embedded manifest explicitly so the driver starts with its submission filename, and avoid logging unchanged schedule data on every poll.
- Update the runtime dependency WiserHeatAPIv2 to 1.1.2.

## Installation

Use the attached `Thermostat_WiserHeat_IP_V2.pkg`, or the `CrestronHomeDriver.Wiser.WiserHeat` 1.3.15 NuGet distribution package. The NuGet package wraps the installable driver; it is not a library reference. GitHub source archives are not installable driver packages.

## Validation

The fixed driver passed 203 desktop lifecycle/regression cases. The outage regression reproduced the former defect. On a CP4-R, a hub network interruption longer than 60 seconds produced offline feedback and automatic recovery from fresh hub reads, preserving the installed child and saved configuration. The ordinary net472 package and NuGet wrapper were built and their versions verified locally.

Live validation used the Drayton Wiser second-generation, three-channel HubR. Current v1 hub compatibility and other Wiser-branded product families remain unverified. A separate observation recorded a brief offline interval and automatic recovery, with its cause unresolved; that result is retained in the [development and validation history](DEVELOPMENT-HISTORY.md).

Crestron submission evidence and endurance observations are separate from this release. This release makes no claim of Crestron acceptance or certification. See the [changelog](CHANGELOG.md) for product history.
