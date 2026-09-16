# Wiser Android control tests

This separately selected NUnit project includes the existing Android inspections and a room schedule-control cycle. It uses the real Home app to disable schedule control, observes Manual mode directly from the Wiser hub, then enables schedule control through the app and verifies Auto mode and restoration. It does not edit a schedule or set a new temperature.

The original `WiserHeatCrestronDriver.AndroidTests` project remains read-only apart from its optional restored name challenge. Selecting that project never includes these control cases. Neither project connects to Android or a processor during ordinary desktop test runs; all fixtures skip without the workflow context.

## Select the project and rooms explicitly

Use the existing private processor workflow, with `AndroidTests.Project` pointing to this project's `.csproj`. Follow the [Android setup and room binding instructions](../WiserHeatCrestronDriver.AndroidTests/README.md). In that private UI settings file, add:

```json
{
  "ControlHubSettingsPath": "ABSOLUTE_PRIVATE_PATH_TO_LIVE_TEST_SETTINGS_JSON",
  "ControlRooms": [
    { "DeviceId": 1234, "HubRoomName": "Test Room" }
  ]
}
```

These fields augment the existing processor credentials, `Rooms` bindings and optional `AllowNameBinding` setting; they do not replace them. Each control device must have exactly one matching `Rooms` binding. `HubRoomName` is the exact physical room name returned by Wiser, which can differ from its assigned Crestron Home room. The hub settings file uses the existing library's `hubHost` and `secret` fields; other live-test settings are ignored. Credentials, paths and actual room bindings stay outside the repository.

The fixture independently matches the hub host/room ID with the installed child's `controlDeviceId`, parent gateway, version, name and location. It refuses ambiguous or changed identities. It requires an idle driver, an assigned schedule in Auto mode, no boost/override, and an existing manual setpoint between 5°C and 30°C. The manual target may be lower, equal or higher than the scheduled target; both are captured and retained. A room without a saved manual setpoint cannot currently be restored exactly after its first manual-mode change, so the fixture refuses it before sending a control.

Selecting this project opts into a brief real mode change, which can change heating demand when the targets differ. An unsuitable room fails preflight; the fixture does not silently skip the control case or choose another room. Do not change household settings merely to manufacture a passing result. Supply a suitable explicitly authorized room or run the original inspection project while control validation is deferred.

## Observation and restoration

Every control has a private original-state record and a flushed intent before input. The fixture checks the front Schedule page and the unique button immediately before one tap. It does not repeat uncertain input. Driver command counters must show exactly one completion with the same epoch, and two independent observations must agree on the requested hub mode and Home property. Changed settings, unrelated commands or a restarted driver prevent attribution.

Normal restoration uses the opposite UI control. If the first input or its observation fails but the fixture subsequently proves that exactly one command completed and the room is in the expected Manual state, it sends one distinct `enableSchedule` compensation through the installed driver. That recovery cannot turn the failed UI test into a pass. Both the hub and Home must confirm restored Auto mode, unchanged persistent settings and schedule identity. A schedule boundary may legitimately change the current target; restoration follows the hub's current schedule rather than forcing the previous reading.

Cancellation has a separate bounded restoration deadline. An uncertain restoration is reported to the workflow, which retains the processor reservation for reconciliation. A successful Android Back/Home action alone never confirms physical restoration. Inspect the private `.control.records` journal before recovering an interrupted run; do not blindly replay commands.

## Validation status

Offline regressions cover successful UI control/restoration, lost input responses, cancellation, journal failures, changed physical identity, driver restart, concurrent commands and unsafe initial setpoints. The project builds against published TestAdapter 1.8.1, DevTools 1.5.0 and WiserHeatAPIv2 1.1.0.6, and every fixture skips without a hardware context.

The live UI control case has not yet passed hardware validation. The first independent preflight sent no control. Its original restriction against a higher manual target has since been removed and covered by offline regression tests; a missing manual target still requires a separate initialization/restoration policy. Successful read-only UI runs do not prove this cycle, the remaining controls, room isolation, outage/endurance requirements or final submission acceptance.
