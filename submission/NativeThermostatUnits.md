# Native controls in the alternate temperature units

`NativeThermostatAlternateUnitsRestoreConfigurationAndPolicy` is a selectable NUnit fixture in the Android control project. Its five cases take `"setpoint"`, `"boost"`, `"off"`, `"minimum"` or `"maximum"`. They use the released runner's reserved `installed-tests` workflow; no private coordinator program is required.

Each case observes the original gateway units, changes Celsius to Fahrenheit or Fahrenheit to Celsius, executes the selected native control exercise, restores the original physical room policy, and restores the original gateway configuration. The same candidate, gateway lifetime, managed thermostat identity and independent physical hub binding must remain unchanged. All other configuration values and guarded physical policies are compared throughout the configuration boundaries. Two ready observations, including the child's new units, are required before the exercise and after restoration.

Enable `AllowNativeThermostatUnitConfiguration` in private UI settings and also the selected control's existing opt-in:

| Exercise | Additional opt-in |
| --- | --- |
| `setpoint`, `boost` | `AllowNativeThermostatControl` |
| `off` | `AllowNativeThermostatOffControl` |
| `minimum`, `maximum` | `AllowNativeThermostatBoundaryControl` |

Supply one matching `Rooms`/`ControlRooms` binding and the absolute private `ControlHubSettingsPath`. These cases operate the named physical room; select the intended scope explicitly after endurance and reservation release. The prepared plans select one case per run. Their 90-minute workflow ceiling includes configuration observation, the bounded native exercise and independent cleanup; it is not a response-time acceptance budget or expected duration.

The fixture reuses the tested `TemperatureUnitsCycle` recovery engine. An uncertain configuration reply is observed instead of repeated. If the nested control exercise fails but confirms physical restoration, the original units are still restored and verified. If physical restoration is unconfirmed, reconfiguration stops and the workflow retains the restoration failure for reconciliation. Home restoration remains part of the normal Android workflow. A restored setting never turns a failed exercise into a pass.

Private evidence records original/requested/restored configuration, independent physical snapshots, instance identity and each native input/response. Alternate-unit native records have separate paths from the ordinary cases, so running both does not overwrite evidence. Configuration snapshots can contain private settings and must remain in the private evidence store.

Run the ordinary native cases in the original configuration as well: one alternate-unit result does not establish both unit variants. The fixture does not prove every native mode, gauge calibration, paired-instance UI behavior or persistence across a reboot. Those retain their own checks.

The Boost exercise reads `BoostDelta` and `BoostDurationMinutes` from the installed gateway on each observation. The increase is in Celsius in both display modes, as specified by the driver's configuration dialog. The case now requires the resulting physical target and expiry to agree with those settings, rather than only checking that Boost became active. The expiry check allows the recorded input interval plus one minute of timestamp/clock tolerance; synchronize the test PC and hub clocks. It does not measure precise response latency. Use a starting room target with sufficient headroom below the declared maximum; behavior at a Boost limit needs a separate case. Incorrect physical results still trigger the independently observed restoration path and remain failures. Earlier retained Boost results did not run these stronger assertions.

The configuration engine's 17 offline recovery regressions pass, the new cases build and are discoverable, and all five refuse execution without workflow context. Their hardware execution remains pending. The earlier retained Fahrenheit control result used a private coordinator and remains separate evidence; it is not retroactively presented as a run of these fixtures.
