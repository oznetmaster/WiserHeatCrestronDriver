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

The tested second-generation HubR accepts `RequestOverride.Type="Boost"` but reports the resulting timed override as `OverrideType="Manual"` with `SetpointOrigin="FromBoost"`. The physical assertion uses that observed response shape, the configured target and expiry; it does not assume that command and response enum values are identical. The outgoing command shape is covered separately by the driver tests. This is evidence for the tested HubR, not a claim about every Wiser product or firmware.

The configuration engine's 17 offline recovery regressions pass, the new cases build and are discoverable, and all five refuse execution without workflow context. Their hardware execution remains pending. The earlier retained Fahrenheit control result used a private coordinator and remains separate evidence; it is not retroactively presented as a run of these fixtures.

The Boost target assertion uses ambient room temperature and the room's reported setpoint step, retaining an already higher target. It must not assume that Boost adds the configured increase to a lower scheduled target. On the tested second-generation HubR, a 19 C scheduled target with ambient21.1/21.2 C and configured +2 C produced23 C, in both the earlier retained runs and the post-endurance run. The earlier stronger expectation of21 C caused a test failure; the driver and hub agreed and the original policy was restored. That failed attempt is retained. The updated expectation has regression coverage for warmer ambient readings, declared resolution and rejection of the old/wrong targets. Hardware evidence establishes only the actually observed temperatures and firmware; unit cases do not establish every rounding boundary on every Wiser hub. See [Drayton's installer guide](https://www.draytoncontrols.co.uk/sites/default/files/Wiser%20Thermostat%20%26%20Multi-zone%20Kits%20guide%20Drayton.pdf) for the ambient-relative Boost description.
