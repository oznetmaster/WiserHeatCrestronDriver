# Configuration acceptance on a shared Wiser hub

This is the execution guide for the five configuration items in Crestron's [official extension test plan](https://sdkcon78221.crestron.com/downloads/Test-Plans/Extension-Test-Plan.pdf). The reviewed template SHA-256 is `b84555dfbed30a577dc38600bd82cad2099aea3320e1a9b62c04cecc5038a340`. It is preparation, not a passing result. Run these phases only after the uninterrupted endurance period, its final review and reservation release.

The current pilot uses one household Drayton Wiser 2nd Generation HubR, 3 channel. A separate processor or driver instance does not isolate the heating hardware. Reserve the selected processor and coordinate access to that hub; retain the original driver configuration and independent household state before any change. Other installed drivers must retain their own configuration. Record the actual topology in the evidence.

## Required observations

| Official item | What must be observed | What API evidence can establish |
| --- | --- | --- |
| 1: Catalogue | Refreshed Third Party Devices hierarchy in Setup/Configure: device category, manufacturer and supported model. | Exact catalogue entry, GUID/version and candidate identity. It cannot prove visible hierarchy or readable labels. |
| 2: Connection dialog | The actual initial add-device dialog, its host field and required-field feedback. | Advertised field identifiers, types and required flags, plus the resulting configuration state. |
| 3: Additional attributes | Subsequent dialogs, descriptions, defaults and the relevant input/choice controls. Exercise each supported setting and restore it. | Advertised metadata, submitted settings and observed configuration/state. The visible dialog still needs inspection. |
| 4: Room installation | Successful initial installation into the selected room, including its visible identity. Commission a managed thermostat through its actual initial configuration step. | Installed parent/child identities, room assignment, exact package payload and readiness. A child alone does not establish a second gateway instance. |
| 5: Persistent settings | Reopen Installer settings after connection and reload, confirm values, change a supported driver setting and verify reconnection and recovery. | Returned unmasked saved values, continuity/identity, authenticated fresh hub reads and corresponding driver feedback. Hidden or omitted secrets cannot be compared as saved plaintext. |

Use the public DevTools `driver-configuration --device ID` command for inspection, supplying an authorized private connection profile. Its masking follows the driver's metadata. Keep the output private: unmasked fields may contain network details. `configure-driver` performs initial configuration and deliberately preserves an already configured instance; it is not a general edit-settings command. See [the DevTools configuration guide](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/DriverConfiguration.md).

## Perform and restore the settings checks

1. Bind the fresh installed instance, candidate payload, processor, hub and affected rooms. Retain the original values using private settings and independent reads; do not infer omitted credentials from masked output.
2. Inspect initial connection and option dialogs on the actual candidate. Capture secret fields only while masked. Preserve the pre-existing gateway/child inventory; remove only temporary instances or children created by this run.
3. Change one driver setting at a time. The existing [feature-configuration](GatewayFeatureConfiguration.md) and [temperature-unit](NativeThermostatUnits.md) fixtures cover their declared options and restoration. Boost amount/duration and connection-field persistence need their own retained observations; those fixtures do not silently cover them.
4. For a connection-error case, scope any deliberately invalid host/secret to the selected driver instance, make no physical control request, observe the failure, then restore the known original private connection settings once. Never replace another driver's credentials or rotate the household hub secret for this test. A successful authenticated fresh read is evidence of usable restored credentials, not proof that an unreadable stored field was inspected byte for byte.
5. Reopen settings, confirm supported persistent values, restore the original configuration and verify fresh connection, intended Home/room placement, child identities and unchanged shared physical policy. An unexpected household change, uncertain write or failed restoration stops the phase for reconciliation; it is not a reason to replay the command.

The official form's changed-passcode example does not establish that the current pilot changed a physical hub secret. Record which supported driver attributes were actually exercised, how connection restoration was observed and any remaining limitation. Do not check the item merely because a configuration API call returned success.

These are configuration checks, not physical power/network outage tests. They do not replace the separately required outage durations or recovery observations. Current source includes supporting API and fixture coverage, but the candidate's complete visible configuration and persistence acceptance is still outstanding.
