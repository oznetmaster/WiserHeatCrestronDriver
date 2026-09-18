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

## Remaining control scope

After the baseline, repeat the applicable candidate control/UI checks with each gateway acting in turn. Independently observe the other gateway during those actions. Shared hub changes should propagate to both, while unrelated room settings, schedules and local configuration remain intact. Preserve and restore every affected shared setting; avoid simultaneous physical commands from the two processors. Pending editor state and instance/session lifetime need separate checks, as does removing an owned temporary instance while the other continues operating.

Gateway peer observation is implemented but has not yet run on hardware. Peer observations for room/schedule controls, the second UI and temporary-instance removal/session effects remain to be integrated and executed. Do not infer them from a successful baseline or from earlier tests of different package versions. The private plan chooses actual processors, rooms and ownership; this document grants no authority to interrupt an active endurance run.
