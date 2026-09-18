# Wiser Android control tests

This separately selected NUnit project includes the existing Android inspections, a room schedule-control cycle, an editor Cancel case and an explicitly configured Save Day/Save All case. The mode cycle uses the real Home app to disable schedule control, observes Manual mode directly from the Wiser hub, then enables schedule control through the app and verifies Auto mode and restoration. If the hub replaces the saved manual temperature during the mode change, the test restores that captured value through the hub's temperature command before returning to Auto. The Cancel case never saves; the save case requires its own room selection and full schedule restoration.

The original `WiserHeatCrestronDriver.AndroidTests` project remains read-only apart from its optional restored name challenge. Selecting that project never includes these control cases. Neither project connects to Android or a processor during ordinary desktop test runs; all fixtures skip without the workflow context.

## Select the project and rooms explicitly

Use the existing private processor workflow, with `AndroidTests.Project` pointing to this project's `.csproj`. Follow the [Android setup and room binding instructions](../WiserHeatCrestronDriver.AndroidTests/README.md). In that private UI settings file, add:

```json
{
  "ControlHubSettingsPath": "ABSOLUTE_PRIVATE_PATH_TO_LIVE_TEST_SETTINGS_JSON",
  "ControlRooms": [
    { "DeviceId": 1234, "HubRoomName": "Test Room" }
  ],
  "ScheduleSaveRooms": [
    { "DeviceId": 1234, "HubRoomName": "Test Room" }
  ]
}
```

These fields augment the existing processor credentials, `Rooms` bindings and optional `AllowNameBinding` setting; they do not replace them. Each control device must have exactly one matching `Rooms` binding. `HubRoomName` is the exact physical room name returned by Wiser, which can differ from its assigned Crestron Home room. The hub settings file uses the existing library's `hubHost` and `secret` fields; other live-test settings are ignored. Credentials, paths and actual room bindings stay outside the repository. Configure both arrays when running the entire control project: omitting `ScheduleSaveRooms` skips the save case and cannot satisfy a gate requiring all discovered cases to pass. An intentionally filtered development run must report its narrower selected coverage.

For a [workflow-created thermostat](../WiserHeatCrestronDriver.AndroidTests/README.md#temporary-workflow-created-thermostats), use `"DeviceId": 0, "ManagedAlias": "room"` in its `Rooms`, `ControlRooms` and, when selected, `ScheduleSaveRooms` entries. Keep the physical `HubRoomName` and all restoration options explicit. Every entry resolves the same exact runtime binding; no fixed device ID is guessed and no physical room is selected automatically. Commissioning a child does not itself authorize a different control or schedule-save scope.

The fixture independently matches the hub host/room ID with the installed child's `controlDeviceId`, parent gateway, version, name and location. It refuses ambiguous or changed identities. It requires an idle driver, an assigned schedule in Auto mode and no boost/override. An existing manual setpoint must be between 5°C and 30°C. The manual target may be lower, equal or higher than the current scheduled target. Both are captured; the test handles hubs that retain the manual target and hubs that initialize it from the active target. Equal targets need no temperature-restoration command. By default, an absent or null manual target is reported as unsupported before any control: the hub can create that value, but no verified operation removes it to restore the original absence. This is not a passing control test.

For a room where that one-time initialization is acceptable, set `"AllowManualTargetInitialization": true` on its `ControlRooms` entry. The test records this choice before input, requires the initialized target to equal the captured active target, and returns the room to Auto with its original schedule and other guarded settings. It does not invent or write a replacement temperature. If the hub retains the inactive target, the result records `ManualTargetInitialized: true` and `ExactRestorationConfirmed: false`; `RestorationConfirmed` then means the explicitly selected operating-state policy was satisfied. If the hub removes it naturally and the original state is restored, exact restoration can be confirmed. Existing saved targets still require exact restoration even when this option is enabled. The general installed-driver control probe retains its stricter policy; this option applies to the Android control cycle only.

Selecting this project opts into a brief real mode change, which can change heating demand when the targets differ. An unsuitable room fails preflight; the fixture does not silently skip the control case or choose another room. Do not change household settings merely to manufacture a passing result. Supply a suitable explicitly authorized room or run the original inspection project while control validation is deferred.

## Observation and restoration

Every control has a private original-state record and a flushed intent before input. The fixture checks the front Schedule page and the unique button immediately before one tap. It does not repeat uncertain input. Driver command counters must show exactly one completion with the same epoch, and two independent observations must agree on the requested hub mode and Home property. Changed settings, unrelated commands or a restarted driver prevent attribution.

Restoration first compares the saved manual target. If it changed to the previously active target, the fixture journals one `RequestOverride` temperature command while still in Manual and verifies the original value independently. A direct write to `ManualSetPoint` is ineffective on the tested hub. The hub's occupied/unoccupied heating readings also follow the active manual target; the fixture accepts only the corresponding target offset during Manual and requires the original readings after returning to the unchanged schedule. Unrelated target changes remain errors. The temperature request is never automatically retried; a lost reply is reconciled by observation and keeps the test failed even if restoration succeeds.

Normal restoration then uses the opposite UI control. If the first input or its observation fails but the fixture subsequently proves that exactly one command completed and the room is in the expected Manual state, it sends one distinct `enableSchedule` compensation through the installed driver. That recovery cannot turn the failed UI test into a pass. Both the hub and Home must confirm restored Auto mode, unchanged guarded settings and schedule identity. Existing saved manual targets must be restored; an explicitly permitted new inactive target is reported under the initialization policy above. A schedule boundary may legitimately change the current target; restoration follows the hub's current schedule rather than forcing the previous reading.

Cancellation has a separate bounded restoration deadline. An uncertain restoration is reported to the workflow, which retains the processor reservation for reconciliation. A successful Android Back/Home action alone never confirms physical restoration. Inspect the private `.control.records` journal before recovering an interrupted run; do not blindly replay commands.

### Deliberate editor interruptions

`ScheduleEditorInterruptionRestoresOriginalState` has separate day and time cases and requires exactly one explicitly bound `ControlRooms` entry. It interrupts after observing the selected day or a pending time change, then verifies original editor values, independent hub schedules and room settings, and Home restoration. It never saves a schedule. The day case exercises configuration recovery when UI cleanup leaves a changed day; the time case can recover through UI cleanup alone.

The deliberately interrupted operation remains recorded as failed. A separate expected-interruption result passes only when the exact sentinel exception is observed and restoration is independently confirmed. Unexpected errors or failed cleanup fail the test. These cases prove two specific recovery paths, not arbitrary connection loss or process termination. Configuration recovery writes the day through `extension:setPropertyValue` and sends Cancel through `extension:doCommand`; command acknowledgement alone is insufficient, so final state is observed.

### Pending temperature boundaries and off-screen inputs

`ScheduleEditorCurrentRowsRespectTemperatureLimitsAndCancel` uses the same private room bindings and scrolling policy. It exercises every currently displayed setpoint, revealing off-screen rows as needed. For each row it prepares a pending endpoint through the configuration interface, inspects the actual action state, taps inward by half a degree and taps back to the endpoint. A disabled outward action is accepted only at its exact limit; an enabled outward action receives one tap and must leave the pending value at that limit. Every observed change must affect only that row.

The fixture records command preparation separately from Android input. The configuration interface uses `extension:setPropertyValue` with the SDK-defined property name and string value, matching the Android control route. It restores each pending value before continuing, then selects Cancel and independently verifies that saved schedules and room state are unchanged. It never saves a schedule or operates heating. Unknown concurrent editor state prevents automatic cancellation; failed or uncertain commands and taps are not replayed. The earlier read-only scrolling fixture remains available separately.

A fresh `ScheduleEditorCurrentRowsRespectTemperatureLimitsAndCancel` run passed on the exact Debug `1.3.007.0028` payload using the normal control project and public TestAdapter 1.11.0. Every current setpoint row was exercised at 5 and 35 degrees, with inward and return Android inputs changing only their own pending value; an initially off-screen row was revealed by scrolling. Boundary preparation and pending-value restoration use configuration commands, not UI input. Cancel, independent saved schedules and room settings, Home, original inventory, temporary-child removal, emulator dimensions and reservation release were verified. This is current-schedule development evidence, not ten-entry rendered coverage, save/conflict validation, a final Release candidate or certification.

Three earlier attempts used direct numeric commands or the generic extension command wrapper. All were rejected and independently restored and cleaned up. Earlier successful Android logs identified the property-write route, whose SDK definition supplies a property name and string value. The corrected attempt has its own evidence and does not retroactively pass those failures.

## Validation status

Debug candidate `1.3.007.0014` has now passed the complete workflow using released TestAdapter 1.9.0: all six Android cases executed, including both schedule saves, with exact original control/schedule/editor state and Home restored. The workflow removed its temporary test instance and released the reservations; it preserved a package archive that predated the run. This supersedes the earlier focused-only Save validation below, while preserving those historical failures and results. Temporary-schedule creation and final immutable Release acceptance remain outstanding.

### Schedule Save Day and Save All case

`ScheduleSaveDayAndAllRestoreOriginalSchedules` requires a separate `ScheduleSaveRooms` array in the private UI settings. An empty array skips this case; a submission plan that requires save behavior must select it and reject a skipped result. Each entry identifies the installed `DeviceId` and physical `HubRoomName`, with the same matching `Rooms` binding and private `ControlHubSettingsPath` used by the other control cases.

By default, the test creates a uniquely named, unassigned schedule, copies the selected room's seven days, assigns only that room, operates both save controls, restores the original assignment and removes only its own schedule. Original schedules and other rooms must remain unchanged, including other users of the original shared schedule. A failed creation never permits deleting an existing household schedule to make space.

Where a hub cannot create another schedule, an entry can explicitly set `"UseExistingExclusiveSchedule": true`. This uses the selected room's current schedule only if no other room shares it. It captures all seven original days before input, operates Save Day and Save All, then restores the complete original contents without changing assignments or deleting any schedule. There is no automatic fallback to this mode after a rejected creation.

Both modes make a real half-degree change through the uniquely labelled first temperature row. Save Day must affect only the selected day; Save All must copy that day's complete entries to all seven days. Fresh independent hub reads, completed driver command counts, reopened editor values and Home restoration must agree. Flushed private intents precede each input and compensation. Lost replies are never automatically replayed and remain failures even if restoration succeeds. Unexpected edits, reassignment or unrelated room changes prevent automatic overwrites and retain recovery evidence and reservations.

Offline checks cover both modes, shared-schedule refusal, ignored writes, uncertain replies, cancellation, unrelated changes and missing restoration evidence. Development attempts to create an additional schedule were rejected by the hub, then independently confirmed unchanged state. No existing schedule was removed to make space.

### Stale-save conflict cases

`ScheduleConflictRefusesStaleSaveAndRestoresOriginal` runs separately for Save Day and Save All. These cases require exactly one `ScheduleSaveRooms` entry with `"UseExistingExclusiveSchedule": true`; no other room may share that schedule. To exercise temporary-schedule creation instead, select the normal save case separately with its default isolation mode.

Each conflict case captures all original schedules and guarded room settings, opens Monday in the real app and makes a pending half-degree edit. It then changes Monday's first target independently on the hub by one degree in the same direction, keeping both targets within the supported range. The app must display its conflict message and preserve every pending slot value and visibility flag. One tap of the selected save button must complete without replacing the newer hub schedule. Cancel/reopen must then show the hub's current values.

The test restores the complete original schedule, editor day and values, room settings and Home. It records actual independent hub snapshots and the displayed conflict before and after the save attempt. An incorrectly accepted stale save fails the test even when its known changes can be restored. Lost replies are not replayed, and unrelated changes prevent automatic overwrites and require reconciliation. This tests an observed stale edit; it does not claim an atomic transaction against arbitrary simultaneous writers.

An empty `ScheduleSaveRooms` skips these cases. A submission gate requiring conflict validation must bind this exact scope and reject skips. See the [development history](../DEVELOPMENT-HISTORY.md) for validation records rather than treating the presence of these fixtures as passing evidence.

### Schedule editor Cancel case

The current source also checks rendered time and setpoint values before editing and after changing the pending time. Repeated value and plus/minus identifiers are read within their own labelled row. Disabled/missing actions, unexpected visible slots, duplicate rows and mismatched values fail. Partial or off-screen rows never count as complete coverage; private records list the observed controls and whether all current controls were visible. The reader has synthetic regressions and a fresh focused live pass on Debug 1.3.007.0026: all eight controls for one four-entry schedule matched before and after the pending time edit. Cancel/reopen and independent hub, editor, Home, inventory and temporary-child cleanup checks passed using the released TestAdapter 1.10.0. A subsequent fresh run on the same exact candidate also passed four pending setpoint inputs (plus in slots 1/3, minus in slots 2/4), verified isolation and rendering after each, and confirmed full Cancel restoration and cleanup. This does not establish every possible slot layout, both directions in every slot, boundary behavior or off-screen inputs.

Android can clamp a partly hidden row or child control to the viewport boundary. The visibility reader now conservatively excludes controls touching the top or bottom edge; those controls must be observed away from the edge in another viewport before they count as fully visible. Ten regression cases reproduced the old false-positive behavior and pass with this correction. The complete control-probe suite passed. This changes test evidence, not the installed driver.

`ScheduleEditorSelectionsCancelWithoutChangingHub` uses the same explicit `ControlRooms` and private hub settings. It independently matches the assigned schedule and initial editor contents, captures all persistent schedules and room assignments, chooses another day and restores the selected day, changes the first time selection, and operates one labelled plus/minus action in every initially complete visible setpoint row before using Cancel. It alternates direction within the declared 5-35 degree range and 0.5 degree step, verifies that each input changes only its own pending slot, and checks the rendered result after every input. These pending changes are not saved to the hub. Reopening must show the hub's original data. Success requires unchanged schedules and assignments, restored editor values and Home. It does not establish Save Day/Save All UI behavior, every conditional slot or physical heating response.

The case records intents and observations in its private `.editor-cancel.records` directory. If UI validation fails, it may restore the selected day and issue one distinct Cancel through the verified installed driver; that compensation cannot turn the failure into a pass. Commands are not automatically repeated. A short-lived configuration session is reused during each property-observation period. Only an initial login timeout permits one fresh connection attempt, with the original observation deadline retained; authentication rejection and other errors propagate.

### Complete current editor rows

`ScheduleEditorObservesEveryCurrentRowAndRestoresState` inspects every time/setpoint row currently enabled by the driver model. It starts from the independently verified hub schedule, reveals the reviewed navigation controls, captures each editor viewport, and accumulates only wholly visible labelled values. It bounds scrolling, rejects unchanged pages before coverage is complete, reveals Cancel and verifies unchanged hub schedules, room assignments, editor state and Home. It does not edit a value, save a schedule or send compensating device commands for unexpected state changes.

This fixture uses TestAdapter **1.11.0**. It passes without scrolling if everything is already visible. For a deliberately smaller emulator screen, add `"RequireEditorScrolling": true` to the private UI settings: the initial view must omit at least one current control and at least one successful gesture must be observed. This option asserts the test condition; it does not resize the emulator. Screen-size changes belong to the test environment and must be restored by its coordinator. Ordinary desktop runs without a workflow still skip every Android case.

Coverage is limited to the current schedule and captured screen configuration. It does not imply that all ten possible slots, boundary actions, save conflicts or every visual layout have been tested. Captures and the private `.editor-scroll.records` files retain the actual controls observed and the independent restoration checks.

A separate fresh run of `ScheduleEditorObservesEveryCurrentRowAndRestoresState` passed using the normal Wiser control project and the published TestAdapter 1.11.0 NuGet package. On a reduced emulator viewport, six controls were completely visible initially; one scoped scroll exposed the remaining pair, and the test established all eight controls across two captures without counting the clipped edge row. Independent hub schedules, assignments, editor, Home, original inventory, temporary-child removal and emulator-size restoration were verified, and both reservations were released. All five active files matched the preserved Debug 1.3.007.0026 package. The test did not change a value, save, update the actual driver or reboot. This remains four-entry development evidence, not ten-entry UI coverage or final acceptance.

### Room mode-control case

Offline regressions cover successful UI control/restoration, lost input responses, cancellation, journal failures, changed physical identity, driver restart, concurrent commands and unsafe initial setpoints. The project references TestAdapter 1.11.0, DevTools 1.6.0 and WiserHeatAPIv2 1.1.0.6, and every fixture skips without a hardware context. Adapter 1.9.0 supplies the labelled-row selector used by the save case.

The corrected live UI control case passed against Debug driver `1.3.007.0011`. It used both UI mode controls, independently verified the hub, restored the different saved manual target and the scheduled occupancy readings, returned the app to Home, preserved the device inventory and released its reservations. This was a focused control-case run; the separate read-only workflow had already passed its inspections.

Earlier failed runs exposed the hub initializing the manual target and changing its derived occupancy readings. Their failures remain in the private evidence, with independently verified recovery. Offline regressions now model that behavior, preserved/equal targets, absent/null targets and uncertain restoration writes. Offline checks now cover opt-in absent/null initialization, exact restoration when the hub removes the new target, rejected unexpected initialization and schedule changes, cancellation, lost responses and journal failures. The actual no-saved-target room case remains unrun. These results do not establish the remaining controls, room isolation, outage/endurance requirements or final submission acceptance.

### Changing schedule definitions and conditional rows

`ScheduleDefinitionLayoutsAreRenderedAndOriginalRestored` is a separate opt-in case in `LiveScheduleLayout`. It requires one `ScheduleSaveRooms` binding with `UseExistingExclusiveSchedule: true`, the matching normal `Rooms` binding and private `ControlHubSettingsPath`. Set `ScheduleLayoutDay` and a nonempty `ScheduleLayoutCounts` sequence explicitly. For the documented Wiser HubR limit, `[1, 8, 2, 7, 3, 6, 4, 5, 1]` exercises each one-to-eight-entry layout and both growing and shrinking. [Drayton documents up to eight events per day](https://wiser.draytoncontrols.co.uk/pages/wiser-app); the test hub rejected ten with an array-overflow error. The driver models ten slots defensively, but those extra model slots are not evidence of live hub support. Select counts appropriate to the actual device; the fixture never converts a rejected count into a pass. Empty counts skip the case; a gate requiring this evidence must select the exact case and reject skips.

The case writes half-hour entries with distinct temperatures to the selected day through the independent hub API. This changes a real schedule and can affect heating; select an appropriate room and day. Other days and schedules remain guarded. Each requested layout must be accepted and independently read back before the app is inspected. A rejection, ignored write, missing row or mismatched value fails the run; unsupported hub behavior is never silently treated as passing coverage.

The app opens each prepared layout, checks every time and setpoint row against the hub, scrolls to expose off-screen controls, and requires the final Cancel action to be fully visible. Hidden or clipped controls do not count as observed. Growing and shrinking layouts must match all current visibility flags and values. No UI Save command is sent by this case. The entire original schedule, guarded room settings, editor day/values and Home are restored afterward. Lost write replies are not replayed; unrelated changes prevent automatic overwrites and retain the run for reconciliation.

This fixture's presence is not evidence that a given hub accepts every count or that every display profile has passed. Consult [validation status](../submission/ValidationStatus.md) for completed, exact-package evidence.

### Deliberate mode-change interruption

`InterruptedManualModeTestRestoresAutoThroughConfiguration` is a separately selected `LiveRecovery` case. It requires `AllowDeliberateModeInterruption: true`, exactly one explicitly bound `ControlRooms` entry, its matching `Rooms` entry and private `ControlHubSettingsPath`. Leave the flag false for ordinary live runs. A gate requiring this recovery evidence must select this exact case and reject a skipped result.

The case begins in Auto with an independently captured hub state, taps the actual Disable control, waits for Manual in both the hub and Home, records the deliberate interruption, then throws inside the observation phase. The existing recovery path must send exactly one configuration `enableSchedule` compensation through `extension:doCommand`. The recorded interrupted operation must remain failed; the recovery test itself passes only when that expected failure, the compensation and restoration are all verified. This does not simulate a processor crash, lost connection or arbitrary outage.

The original saved manual target, all persistent schedules and guarded room settings are compared after returning the app to Home. A previously absent target is refused unless that room explicitly allows `AllowManualTargetInitialization`; if the hub then retains an inactive target, the receipt distinguishes that permitted difference from exact restoration. Uncertain restoration keeps the processor reservation for reconciliation. Private `.mode-recovery.records`, before/after hub snapshots and the `.expected-recovery.json` receipt preserve the failure and recovery evidence separately.


### Schedule names changing while the app is open

`ScheduleRenameUpdatesOpenPageOrDialogAndRestoresOriginal` has two separately reported `LiveScheduleChoices` cases. Set `AllowScheduleRename` to true and provide one `ScheduleSaveRooms` binding with `UseExistingExclusiveSchedule` enabled, the matching Rooms binding and private `ControlHubSettingsPath`. The flag defaults to false. A gate requiring this evidence must select both cases and reject skips.

The fixture changes only the name of the exclusively assigned schedule through the independent hub API. The first case keeps the Schedule page open; the second keeps the actual selection dialog open while the name changes. Both require the hub and running driver to preserve the selected ID, publish the new label and retain every other option. They inspect the complete rendered option list, scrolling within its observed bounds, without choosing another schedule or reloading the driver. A stale open dialog fails; reopening it is not substituted for a live update. A rename can reorder the list while preserving its scroll anchor, so the scan first establishes the top and then traverses to the bottom. Both ends and every expected option must be observed; finding only the items below the starting viewport is insufficient.

Intents, actual hub readbacks, driver ID/label mappings and Android captures are retained privately. Cleanup restores the original name, full persistent schedules, guarded room settings, editor and Home. Uncertain writes are observed without replay; unrelated edits prevent automatic overwrites and retain the run for reconciliation. Ordinary tests stay offline without a workflow.

This fixture does not establish choice addition/removal, rejection of deleted IDs, unchanged-list behavior across confirmed refresh events, every display profile or final acceptance. Actual completed runs and limitations are recorded in [validation status](../submission/ValidationStatus.md).

### Open choices during unchanged hub refreshes

OpenScheduleChoicesSurviveRepeatedUnchangedHubRefreshes is an opt-in LiveReadOnly case in LiveScheduleChoices. Enable AllowScheduleRefreshObservation, supply one ScheduleSaveRooms binding, its matching Rooms binding and the private ControlHubSettingsPath. No exclusive assignment is needed because this case never writes to the hub or selects another schedule. A workflow requiring this evidence must select the case explicitly and reject skips.

The fixture inspects the complete open choice list, then observes two strictly advancing lastHubRefreshUtc markers from the same driverLifetimeId. These markers represent successful installed-driver hub reads, not elapsed time or cached management responses. After each observed refresh it compares the independent hub schedules, guarded room settings, driver ID/label mapping, selected ID and complete rendered options without reopening the dialog. It then returns to Home and verifies unchanged state. Private evidence records the markers and each complete list scan; stale markers time out, backwards or malformed markers and changed lifetimes fail. No hub writes or editor compensation commands are part of this case.

This is separate from name mutation, added/deleted choices and final candidate acceptance. See [validation status](../submission/ValidationStatus.md) for actual runs and their scope.


### Schedule choices added and removed while open

OpenScheduleChoicesTrackUnassignedScheduleAdditionAndRemoval is an opt-in LiveScheduleChoices case. Enable AllowScheduleMembership and provide one ScheduleSaveRooms binding, the matching Rooms binding and private ControlHubSettingsPath. It requires a valid existing selected schedule to copy, with all seven days containing one to eight supported entries, and fewer than 16 heating schedules. A full hub fails preflight before opening the selector or sending a creation request; the test never removes an existing schedule to make room. No exclusive assignment is required because the original schedule and room assignment are not changed.

The actual selector is opened before one complete seven-day schedule is created with a unique run-owned name and no assignments. Independent observations must confirm its new ID, declared contents, no assignments and unchanged original schedules/room settings. The full added list must reach the still-open dialog with the original selection preserved. One journaled deletion is allowed only while that same temporary object remains unchanged and unassigned. The open list must remove the option and retain the original selection. Both ends and every expected option are captured; reopening is not substituted for a live update.

Lost replies are independently reconciled without replay and the run remains failed. Unsafe external edits or assignments prevent automatic deletion and retain the reservation for reconciliation. Cleanup verifies complete original state and Home without editor compensation. Ordinary tests remain offline unless a workflow and this flag are supplied. The fixture is not proof that a particular hub accepts the complete creation payload; actual successful runs are listed in [validation status](../submission/ValidationStatus.md). Rejection of a deliberately supplied stale/deleted ID is a separate requirement, not established by list disappearance.

The [UK/Ireland system guide](https://www.productinfo.schneider-electric.com/wiser_home/wiser-home-sug-uk/English/System%20User%20Guide_Wiser_Home_UK%20%28Bookmap%29.pdf) lists a maximum of 16 climate schedules per hub. Free capacity is a hardware prerequisite for this case, not a reason to mark an unexecuted add/remove check as passed.


### Whole-house Away control

`GatewayAwayChangesHubStateAndRestoresOriginal` requires `AllowGatewayAwayControl`, private `ControlHubSettingsPath` and one bound `Rooms` child to verify that the selected gateway is connected to the intended physical hub. Away mode affects the household, not just that binding's room; obtain permission for that scope before enabling the flag.

The fixture captures the original Away state, schedules, guarded room settings and hot-water overrides. It supports either starting Away state. One observed labelled UI action changes Away mode, then a distinct action restores its original value. Fresh independent hub reads, a later successful driver refresh, enabled controls and the actual Android row must agree. Timing and before/after captures are saved as private evidence. Dynamic temperatures and relay feedback may legitimately change; this is not a promise to restore historic sensor readings. Existing override values and absolute deadlines must remain unchanged. Unsupported system overrides are rejected before input.

Lost input replies are never replayed. A lost reply remains a failed test even if independent observations confirm restoration. If the transition cannot be established, the driver restarts or unrelated guarded settings change, the run retains its reservations for reconciliation. Completion also requires observed navigation back to Home. The gateway exposes no per-command completion counter, so these checks establish one issued tap per transition and correlated state feedback, not a claim about internal execution counts. Hardware acceptance is recorded separately in [validation status](../submission/ValidationStatus.md).


### Hot-water restoration preparation

The control-probe project contains a read-only `HotWaterRestoration` planner and comparator. It distinguishes scheduled control, manual mode and manual overrides, including the stored inactive override state. Returning a hot-water button to its previous On/Off value does not establish restoration: a new manual override must not remain in place of the original schedule. A legitimate later schedule event may change the current relay state without changing the restored control policy.

The candidate compensation requests use the client library's existing Manual/None request shapes. Their acceptance and resulting fields must be independently validated on a hub before a live hot-water UI fixture can claim restoration. Timed overrides are rejected before producing a plan because restoring their original absolute deadline is not yet verified; the test must not silently replace them with a fresh duration. This preparation does not establish hot-water hardware acceptance and does not send requests itself.


### Hot-water UI cycle

`GatewayHotWaterChangesBothStatesAndRestoresOriginalPolicy` is an opt-in `LiveControl` case. Enable `AllowGatewayHotWaterControl` only with permission to operate the household hot water. Supply private `ControlHubSettingsPath` and one `Rooms` binding to verify the gateway's physical hub. This changes hot water, not the binding child's room temperature.

The fixture captures the current settled relay/target state and scheduled/manual/override policy before input. It issues one observed UI action to change state and one to return to the original state, recording UI captures, fresh independent hub readings and observation timings. Restoration is checked separately: direct Manual/None compensation requests may be required to restore the captured latent manual target and schedule control. Each exact request must match the captured plan, has its own flushed intent and is attempted at most once. A compensation whose effect is already independently observed is not sent again. Natural schedule progression is allowed while preserving the original control policy.

An uncertain reply remains a failed test even when original state is subsequently confirmed. Failed observation, a restarted driver or unrelated guarded changes prevent blind compensation and retain reservations. Final success requires the original policy, guarded settings, actual UI and observed Home restoration. Existing timed overrides are rejected before any input; timed behavior will be tested through a separately owned temporary override, rather than resetting a user's timer. The source fixture and synthetic fault tests do not establish actual hardware acceptance; see [validation status](../submission/ValidationStatus.md).
