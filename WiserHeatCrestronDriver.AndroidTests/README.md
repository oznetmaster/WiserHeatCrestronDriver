# Android gateway and room UI tests

This opt-in NUnit project compares the real Crestron Home Android gateway page with fresh configuration-management state. It opens the uniquely named gateway tile, checks the Hot Water and Away Mode status, action caption and enabled state, and closes the page. Two consecutive cases exercise repeatability. No heating, away or room-assignment commands are sent. An additional private opt-in can temporarily rename the gateway to associate the observed app tile with the installed instance, then restore its original name.

A room case opens each explicitly bound thermostat, verifies its Schedule action, compares the Schedule page's status with fresh management state, and checks the Edit Schedule controls. It then checks every schedule, day and half-hour time choice against expected state. It does not select a different value or save a schedule. It cancels the editor, returns Home and verifies that the room identity, assignment, target, boost and schedule state stayed unchanged. These page names and assertions belong to this Wiser fixture; the shared UI automation library supplies generic navigation and inspection helpers.

Ordinary desktop test runs skip these cases without connecting to Android or a processor. They are separate from the processor test package and must run on the Android worker while the processor workflow owns both reservations.

The separately selected [Android control project](../WiserHeatCrestronDriver.AndroidControlTests/README.md) adds an independently observed and restored schedule-mode cycle. Choosing this inspection project does not enable it.

## UI automation dependency

The project uses the Android assembly included in `CrestronHomeNUnit.TestAdapter` 1.8.0 and the name-challenge API in `CrestronHomeDevTools` 1.5.0. It is included in the driver solution. The adapter supports portrait screens where the saved local-port field needs scrolling. No second source checkout is required. Hosted CI builds the project and verifies that all cases skip without a hardware context.

## Private settings and invocation

The workflow supplies `CRESTRON_HOME_ANDROID_CONTEXT`, including the installed gateway identity, candidate hashes, Android profile and private evidence directory. Never create a context that invents passing candidate or reservation evidence.

Set `CRESTRON_HOME_WISER_UI_SETTINGS` to an absolute path outside the repository containing:

```json
{
  "Host": "192.0.2.10",
  "UserName": "YOUR_PROCESSOR_USER",
  "Password": "YOUR_PROCESSOR_PASSWORD",
  "CertificateSha256": "YOUR_VERIFIED_PROCESSOR_CERTIFICATE_SHA256",
  "AllowNameBinding": false,
  "Rooms": [
    { "DeviceId": 1234, "RoomName": "Test Room", "PageTitle": "Test Room" }
  ]
}
```

Replace the example room ID with an installed Wiser thermostat child's ID. `RoomName` is its assigned Crestron Home room; `PageTitle` is the heading shown when opening that thermostat. The device must be a loaded, online child of the workflow's gateway, assigned to the uniquely named room, with a unique tile name in that room. At least one explicit binding is required; no thermostat is chosen automatically. Additional bindings are inspected in turn. These are local household settings, so keep them outside the repository.

The processor address must match the workflow context. Its gateway must be online, loaded, have the expected version and have a unique name in the management inventory. Both Hot Water and Away capabilities must be visible for this initial fixture. It currently checks the English captions. Unavailable capabilities and other languages require additional fixtures.

To enable instance association on a development processor, set `AllowNameBinding` to `true` and add `SshFingerprint` containing the processor's verified SSH fingerprint. Each case records rename intent, verifies the existing processor reservation and Android coordinator, observes a fresh name on the management instance and Home tile, inspects that exact tile, and restores the original name. Failed assertions still attempt bounded restoration. An uncertain name restoration prevents subsequent inspection and successful workflow cleanup; retain the private records and reconcile before another run. The fixture does not acquire or release its coordinator's processor reservation. Omitted or false `AllowNameBinding` preserves the original read-only behavior.

Add this project as `androidTests.project` in the existing private [processor workflow](../WiserHeatCrestronDriver.WorkflowTests/README.md), with `androidTests.profilePath` pointing to your private Android profile. Keep the actual-driver target and required local/processor suites in that plan. Run the workflow through Test Explorer or the CLI; do not add this project to `localTests`, because it belongs after the actual-driver update. Export the private settings environment variable in the worker process before starting that workflow. The coordinator suppresses Android context during discovery and supplies it only during execution.

Keep credentials, contexts, test reports and captures outside the repository and public release assets. The tests save only selected gateway/room UI properties, restoration records, screenshots and masked UI hierarchies in the private evidence directory.

## What these results prove

The assertions compare real UI rows with the selected management device and verify Home restoration. Repeated Android resource IDs are resolved within each labelled control row, so the Hot Water assertion cannot accidentally read the Away button.

A matching saved Home endpoint and familiar tile name are not proof of the application's active network route. The optional fresh name challenge provides stronger association with the management instance. It is not cryptographic route or package attestation. The room case validates the configured child identity, visible option lists and checked state preservation. It does not test physical heating changes, writes to shared schedules, or isolation between two rooms. This project does not prove all submission requirements or produce a signed/approved submission. The management snapshot and challenge records are diagnostic evidence; they are not yet bound into the submission producer contract.

## Development validation

On 16 September 2026, the complete Debug workflow passed local tests, processor suites, live hub reads, the installed gateway update, installed health checks and both Android gateway cases in the minimized Google Android emulator. It used the published TestAdapter 1.7.1 and DevTools 1.5.0. The original gateway name was restored, the temporary processor test instance and owned archive were removed, and both reservations were released. Exact test inventories, results and capture hashes belong to the private workflow evidence rather than fixed totals in this README.

Both gateway cases also passed separately under the Windows GitHub runner service account against an already-running Google emulator. This verifies service access and fixture execution; it does not establish emulator startup after logout/reboot or a complete deployment workflow running as a service.

These historical results are development checks, not an exact Release-candidate submission or certification. The new room case adds read-only page and selector coverage; physical-control/restoration and cross-room isolation remain separate requirements.

The expanded fixture subsequently passed every discovered case against the local stable TestAdapter 1.8.0 candidate: both gateway association checks and the configured room's complete schedule/day/time selector check. All 27 accepted capture pairs matched their retained hashes. Gateway names, room control settings, installed-device identities and Home were preserved, and both reservations were released. This run used the already-installed Debug driver; it did not repeat deployment or establish an exact Release-candidate submission. The fixture also compiled and skipped every case in an ordinary offline test run.

A subsequent complete Debug workflow used the published TestAdapter 1.8.0 and DevTools 1.5.0. Local, processor, live hub, driver update, installed health and all discovered Android checks passed. This included the thermostat, Schedule and Edit Schedule pages, the complete selectors, and the shortened hot-water and Save All captions. The original gateway name, checked room state and Home were restored. Cleanup removed the temporary processor test instance and its newly uploaded archive from storage, and released the reservations. Home retained a cached catalogue entry until its next planned reboot; no reboot was performed. The screenshots are documentation candidates held privately for review, not final Release acceptance evidence.
