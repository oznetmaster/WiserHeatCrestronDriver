# Native thermostat minimum and maximum

The official thermostat scope includes setting the supported minimum and maximum through the actual UI. The ordinary one-step control cycle and the schedule editor's limits do not establish this behavior for the native thermostat.

The control project now exposes two separately selected cases:

- `WiserHeatCrestronDriver.AndroidTests.GatewayUiTests.NativeThermostatBoundaryRestoresPolicy(False)` reaches the minimum, 5 C / 41 F.
- `WiserHeatCrestronDriver.AndroidTests.GatewayUiTests.NativeThermostatBoundaryRestoresPolicy(True)` reaches the maximum, 30 C / 86 F.

Use the released runner's reserved `installed-tests` workflow, the unchanged candidate, fresh producer pins and one explicitly authorized physical room. Set `AllowNativeThermostatBoundaryControl` in private UI settings, with the matching `Rooms`, `ControlRooms` and absolute `ControlHubSettingsPath`. The ordinary native-control opt-in does not enable these cases. Keep all actual bindings and credentials private. Do not run them during endurance.

Each case reads the original room policy, independently checks hub/processor/UI agreement, then issues one observed half-degree step at a time through the native plus/minus control. The next input is allowed only after two matching observations with idle, attributed command activity. A target already at the requested endpoint is moved inward once and back, so the case still exercises an actual boundary-setting input. A case issues at most 50 inputs and never intentionally goes outside the supported range. Starting values outside the range or off the half-degree grid are refused before input.

The existing guards reject changed driver lifetime, room identity, units, unrelated policy or unexpected command activity. A lost input response stops the sequence: it is observed, never replayed. Cleanup independently observes the last delivered state and restores the captured original policy, including scheduled/manual mode and any saved manual target. Physical restoration can proceed despite an Android failure; final UI verification remains mandatory and cannot turn an earlier failure into a pass. Unconfirmed restoration retains the failure for reconciliation.

Every step records intent, first matching response and confirmed response using the [native timing boundaries](EnduranceEvidence.md#native-thermostat-response-records). A final `boundary-observed` record and capture identify the endpoint. Each input has a three-minute observation deadline; the sequence has a 30-minute limit, with separate guarded cleanup. These are execution limits, not acceptable device-response budgets. Use a workflow timeout that also permits cleanup; the prepared single-case plans allow 55 minutes.

Run both cases in each supported unit configuration and retain the configuration's own restoration evidence. The ordinary cases observe the current units and refuse a unit change during a cycle; they do not change the driver configuration themselves. The separate [alternate-unit fixtures](NativeThermostatUnits.md) wrap these exercises with guarded configuration change and restoration. They establish neither outward-button behavior at a limit, gauge calibration/current-temperature feedback, paired-instance isolation nor official acceptance of unexecuted variants.

Offline regression covers Celsius/Fahrenheit, Auto/Manual policies, both endpoints, already-at-endpoint and full-span starts, an uncertain later input, evidence-write failure and off-grid refusal. Both cases are discoverable and skip without authorized workflow context. Physical execution remains pending after endurance; no driver package was changed by this test preparation.
