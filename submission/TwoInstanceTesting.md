# Two Wiser gateway instances

The selected submission arrangement is two actual gateway-driver instances on separate Crestron Home processors, connected to one physical Wiser hub. Record this topology in the submission evidence. It does not establish operation with two independent hubs, and two managed room children are not being counted as two gateways. A reply to the clarification request is not a prerequisite for running this arrangement.

## Read-only baseline

The control test project contains this opt-in case:

```text
WiserHeatCrestronDriver.AndroidTests.GatewayUiTests.TwoGatewayInstancesRefreshSharedHubWithoutChangingLocalConfiguration
```

Select it by exact name in the released runner's installed-test plan. The outer workflow must already hold the active processor and Android reservations and verify the active candidate. In the normal private Wiser UI settings, provide `ControlHubSettingsPath` and `PeerObservationSettingsPath`. Copy [the peer settings example](examples/peer-observation.example.json) outside the checkout and fill it with the second processor's verified identities and private credentials. Both processors must already have the exact candidate installed; this case never installs, updates or configures a driver. It requires numeric processor IP addresses and distinct pinned HTTPS certificates, avoiding an accidental comparison of one processor under two names.

For a standalone baseline, leave `ManagedChildren` empty in the installed-test plan and use empty `Rooms`, `ControlRooms` and `ScheduleSaveRooms` arrays in the private Wiser settings. No managed thermostat child is needed for this gateway-only case. Keep the normal Android profile and workflow context: the existing fixture setup verifies the selected Home endpoint and teardown returns the app Home.

The peer settings identify one existing gateway by its device ID, name, room/location ID and catalogue ID. `PackagePath` is the absolute path to the same immutable package used by the active workflow. Each gateway must be configured, online and idle, with Away and whole-house hot-water controls enabled. The hub settings must identify the same configured host and secret on both instances; aliases are not automatically equated. The existing configuration inspector must expose complete values to compute private configuration fingerprints; masked or missing values cause refusal, not an inferred match. Credential values are never included in the observation snapshots.

The case takes an additional peer-processor reservation without waiting. A busy peer stops the case; there is no lease takeover. An acquisition with an uncertain outcome may retain its marker for reconciliation under the normal lease rules. An acquired reservation is released on both success and failure because this case sends no device or configuration writes. A failed release is recorded and fails the case. Do not run it against a processor reserved for endurance.

It checks peer catalogue metadata and the extracted candidate payload before and after observation. Two separate gateway lifetimes must be present. Both successful hub-refresh markers must advance beyond their initial values; cached matching displays cannot pass. Independent hub reads bracket the two processor observations, and both gateways must agree with stable Away and hot-water feedback. Each instance must preserve its own configuration, name, location and lifetime throughout sampled observations. Configuration values need not be identical between processors; numeric device IDs may also legitimately coincide.

## Evidence and limits

The workflow's private evidence folder receives `wiser.peer.*.json` records: reservation intent/release, payload comparisons, initial/final configuration fingerprints, sampled gateway feedback, independent hub responses and any failure. Keep the complete producer inventory, workflow identity checks and NUnit result with those files. An `ObservationsPassed` record alone is insufficient: require a passing test result and confirmed reservation cleanup. Hub responses and device details remain private.

This case establishes only observed shared-hub convergence and preservation of local configuration. It does not change physical state, compare the second app's UI, measure command response, prove independent-hub behavior or complete the official multiple-instance item. Read-only samples cannot establish that no change occurred between observations. Payload comparison establishes matching extracted files and reviewed catalogue/instance metadata, not a direct attestation of process memory.

## Gateway control sequence

Two additional opt-in cases connect peer observation to the existing restoration-aware controls:

```text
WiserHeatCrestronDriver.AndroidTests.GatewayUiTests.GatewayAwayUpdatesBothInstancesAndRestoresOriginal
WiserHeatCrestronDriver.AndroidTests.GatewayUiTests.GatewayHotWaterUpdatesBothInstancesAndRestoresOriginalPolicy
```

Select these exact names to require peer participation. Enable `ObservePeerDuringGatewayControls` plus the corresponding `AllowGatewayAwayControl` or `AllowGatewayHotWaterControl` flag, and provide the same private peer settings. A missing opt-in produces a skipped case, which cannot satisfy the workflow's required-test gate. The original single-instance case names are unchanged; they can optionally observe the peer when the flag is enabled.

These control cases retain their existing one-room binding to independently identify the selected hub and their original-state/restoration requirements. Unlike the gateway-only baseline, their plans must supply that existing or explicitly managed child. Before any physical command, the fixture verifies and reserves the peer, compares the candidate payload and records both configurations. It then checks shared state before each input and waits for a strictly newer peer refresh after observed commands. After waiting for the peer, it rechecks the primary hub state before sending the input; a changed starting state stops the test rather than operating on an old observation. Peer observations are bounded to 30 seconds, with the cycle's existing deadline still enforced. Recorded `SecondsSincePreInputObservation` is measured from the pre-input observation, not from the physical command itself; use the primary control journal for its command timing.

No peer observation is required before compensation. A failed peer check after an input fails the test but leaves independent physical restoration running. Peer agreement is checked again only after the hub's original policy is confirmed restored. Peer failure and UI failure are separately recorded; neither can turn the test green or erase confirmed physical restoration. Offline regressions exercise peer loss, cancellation, failure before the second hot-water input and a peer that remains unavailable during recovery.

The peer observer sends no control commands. At completion it checks payload/configuration preservation and releases its own reservation only when physical restoration and those checks are confirmed. Otherwise it records the unresolved outcome and retains the reservation for reconciliation; disposal does not remove it. Existing failures are preserved if peer cleanup also fails. A preflight failure before a control cycle begins releases a successfully acquired peer reservation; an uncertain acquisition is never blindly removed.

## Room Auto/Manual control

The room mode sequence also supports required peer observations:

```text
WiserHeatCrestronDriver.AndroidTests.GatewayUiTests.RoomScheduleControlUpdatesBothInstancesAndRestoresSchedule
WiserHeatCrestronDriver.AndroidTests.GatewayUiTests.RoomScheduleControlVerifiesStartingStateOnBothInstances(EqualToCurrent)
WiserHeatCrestronDriver.AndroidTests.GatewayUiTests.RoomScheduleControlVerifiesStartingStateOnBothInstances(DifferentFromCurrent)
WiserHeatCrestronDriver.AndroidTests.GatewayUiTests.RoomScheduleControlVerifiesStartingStateOnBothInstances(Absent)
```

Enable `ObservePeerDuringRoomControls`, provide the primary `ControlRooms` and `Rooms` bindings, and add an explicit `Rooms` array to the private peer settings. Each entry has `DeviceId`, `LocationId`, `Name` and `HubRoomId`: the first three identify the existing peer thermostat child; the last is the physical hub room ID. Both children must belong to their respective bound gateway and identify the same physical room. Device IDs are not interchangeable across processors. These cases do not create the peer child. A missing required opt-in is skipped and cannot satisfy the required-test gate.

The three specific starting-state cases additionally require `AllowScheduleManualStartingStateCases`. Select only a case matching the independently observed room state. `Absent` also requires `AllowManualTargetInitialization`; it may leave an inactive manual temperature initialized by the hub, and does not claim exact restoration of an absent value. The original single-instance case names remain available and optionally observe the peer when enabled.

The observer uses the same candidate, configuration, identity and reservation checks as the gateway cases. Room cases do not require enabling the unrelated Away/hot-water capabilities. Each room observation verifies the peer mode, assigned schedule and target in its own configured Celsius/Fahrenheit units. The peer's command epoch and completed count must stay unchanged and its pending count must be zero: shared feedback is expected, issuing commands from the peer is not. Reads are bracketed by a stable gateway refresh marker; after Manual and after restoration, a newer peer refresh is required. Device name, location, parent, online status and physical room identity are checked on every sample, with configuration fingerprints retained separately.

The primary hub is read again after waiting for the peer, before any input. A changed command counter or active scheduled target stops the case. Peer observation is bounded to 30 seconds per phase; the room cycle permits 90 seconds with a peer, retaining an independent recovery deadline. These limits are test execution bounds, not evidence that a submission response-time requirement passed. Use retained command and observation records to assess that requirement.

Peer failure after Manual causes guarded, independent restoration to Auto and the saved manual target. No peer callback runs before that compensation. Final peer failure leaves the test failed while preserving the physical-restoration result; original initialization limitations still apply. Completion rechecks peer child identity and command activity as well as candidate/configuration integrity before releasing its reservation. A failed integrity check retains the reservation for reconciliation.

## Schedule saves observed by the peer

```text
WiserHeatCrestronDriver.AndroidTests.GatewayUiTests.ScheduleSaveDayAndAllUpdateBothInstancesAndRestoreOriginal
```

Enable `ObservePeerDuringScheduleSaves` and select this required case, with the primary `ScheduleSaveRooms` bindings and the same explicit peer `Rooms` mapping. The original `ScheduleSaveDayAndAllRestoreOriginalSchedules` case remains available and can optionally use the observer. The existing `UseExistingExclusiveSchedule` choice is preserved: use an exclusively assigned existing schedule when capacity does not permit an owned temporary copy. The test never deletes an unrelated schedule to make space.

Prepare the peer's editor on Monday, in Celsius, then close the editor. This observer never selects a day, opens the peer editor, changes its settings or sends a physical command. It verifies the exposed Monday slots against independently read hub schedule data before input, after Save Day, after Save All and after restoration. A stale editor is observed again within a 30-second bound; it is never overwritten. A changed peer day or command activity fails the case. An open editor retaining pending changes is a separate test scenario and must not be mistaken for this idle-view convergence case.

Both instances must retain their own configuration and identity. The peer must retain its own room command epoch/count and physical binding, while reflecting the current mode, assigned schedule and expected target. A new successful peer refresh is required after saves and restoration. The primary rechecks hub state after waiting for the peer and before sending each save. A foreign change stops further inputs; automatic cleanup still refuses to overwrite unrelated changes. The normal independent hub validation verifies complete saved contents, untouched schedules and room settings. Peer editor evidence covers its exposed Monday view, not unseen days or a second rendered Android screen.

For both the existing-schedule and temporary-copy paths, peer failures leave guarded hub restoration running. For a temporary copy, the original assignment and owned-copy deletion now complete before Android editor cleanup. A `hub-restored` receipt is flushed before the final peer/editor checks. An unavailable Android app therefore cannot block physical cleanup. Overall `RestorationConfirmed` still requires the primary editor and final hub check; if those fail, the test retains reservations and its separate physical-restoration receipt for reconciliation. A failed peer check cannot turn a test green, and peer completion still verifies child identity/activity and package/configuration integrity before releasing its reservation.

## Pending selections remain local

The opt-in case `WiserHeatCrestronDriver.AndroidTests.GatewayUiTests.ScheduleEditorPendingSelectionsRemainLocalAndCancel` requires `ObservePeerDuringPendingEdits`, explicit primary `ControlRooms`/`Rooms`, private hub settings and matching peer `Rooms`. Both gateways must have the same verified candidate and the peer must start with a Celsius editor matching its saved schedule. It need not select the same day as the primary.

The primary app changes the selected day, first time and initially visible setpoints, then cancels and reopens. After each observed edit and reopening, the reserved peer is read without sending it commands. The check preserves every exposed peer editor field, including hidden slots, its command activity and identity. Independent hub reads bracket the peer reads and compare all persistent schedules and guarded room assignments/modes/manual targets. Calculated time-dependent feedback may advance. After the initial baseline, a new successful peer refresh is required; an unexpected persistent/editor change fails immediately and is never retried away. Only stale feedback or a sample crossing a refresh is observed again within the bounded deadline.

Primary editor restoration runs before final peer checks, even if a peer observation fails. A peer failure never authorizes overwriting its settings. Final peer verification and candidate/configuration checks must pass before its reservation is released; unresolved outcomes remain recorded for reconciliation. The normal primary restoration receipt alone does not establish peer acceptance: require the test outcome and peer cleanup evidence too.

This case has offline comparison tests and builds against the released tooling. It has not run on hardware. It observes the peer's exposed properties, not a second rendered app or a peer editor containing deliberately unsaved changes. It covers the primary controls actually exercised and recorded, not every hidden row, save conflict, instance removal or simultaneous input. Run the reverse direction with independently verified bindings after the first direction restores successfully.

## Complete day/time selections

The [action-selector cases](ActionSelectorTesting.md) exercise all seven days and all 48 time choices for each supported row, with optional mandatory peer observation. They remain prepared source fixtures, not hardware acceptance. They reuse pending-state isolation and add independent hub comparisons after every choice.

## Remaining control scope

After the baseline, repeat the applicable candidate control/UI checks with each gateway acting in turn. Independently observe the other gateway during those actions. Shared hub changes should propagate to both, while unrelated room settings, schedules and local configuration remain intact. Preserve and restore every affected shared setting; avoid simultaneous physical commands from the two processors. Pending editor state and instance/session lifetime need separate checks, as does removing an owned temporary instance while the other continues operating.

Gateway peer observation is implemented but has not yet run on hardware. Room Auto/Manual peer observation is also implemented and offline-tested, but has not run on hardware. Peer observation of Save Day/Save All is implemented with offline recovery tests, but has not run on hardware. Pending-selection isolation now has the opt-in case above but still needs hardware execution. A deliberately dirty peer editor, other editing actions, the second UI and temporary-instance removal/session effects still need integration and execution. Do not infer them from a successful baseline or from earlier tests of different package versions. The private plan chooses actual processors, rooms and ownership; this document grants no authority to interrupt an active endurance run.
