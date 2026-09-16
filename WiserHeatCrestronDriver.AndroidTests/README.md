# Android gateway UI tests

This opt-in NUnit project compares the real Crestron Home Android gateway page with fresh configuration-management state. It opens the uniquely named gateway tile, checks the Hot Water and Away Mode status, action caption and enabled state, and closes the page. Two consecutive cases exercise repeatability. No heating, away or room-assignment commands are sent. An additional private opt-in can temporarily rename the gateway to associate the observed app tile with the installed instance, then restore its original name.

Ordinary desktop test runs skip these cases without connecting to Android or a processor. They are separate from the processor test package and must run on the Android worker while the processor workflow owns both reservations.

## UI automation dependency

The project uses the Android assembly included in `CrestronHomeNUnit.TestAdapter` 1.7.1 and the name-challenge API in `CrestronHomeDevTools` 1.5.0. It is included in the driver solution. The adapter supports portrait screens where the saved local-port field needs scrolling. No second source checkout is required. Hosted CI builds the project and verifies that all cases skip without a hardware context.

## Private settings and invocation

The workflow supplies `CRESTRON_HOME_ANDROID_CONTEXT`, including the installed gateway identity, candidate hashes, Android profile and private evidence directory. Never create a context that invents passing candidate or reservation evidence.

Set `CRESTRON_HOME_WISER_UI_SETTINGS` to an absolute path outside the repository containing:

```json
{
  "Host": "192.0.2.10",
  "UserName": "YOUR_PROCESSOR_USER",
  "Password": "YOUR_PROCESSOR_PASSWORD",
  "CertificateSha256": "YOUR_VERIFIED_PROCESSOR_CERTIFICATE_SHA256",
  "AllowNameBinding": false
}
```

The processor address must match the workflow context. Its gateway must be online, loaded, have the expected version and have a unique name in the management inventory. Both Hot Water and Away capabilities must be visible for this initial fixture. It currently checks the English captions. Unavailable capabilities and other languages require additional fixtures.

To enable instance association on a development processor, set `AllowNameBinding` to `true` and add `SshFingerprint` containing the processor's verified SSH fingerprint. Each case records rename intent, verifies the existing processor reservation and Android coordinator, observes a fresh name on the management instance and Home tile, inspects that exact tile, and restores the original name. Failed assertions still attempt bounded restoration. An uncertain name restoration prevents subsequent inspection and successful workflow cleanup; retain the private records and reconcile before another run. The fixture does not acquire or release its coordinator's processor reservation. Omitted or false `AllowNameBinding` preserves the original read-only behavior.

Add this project as `androidTests.project` in the existing private [processor workflow](../WiserHeatCrestronDriver.WorkflowTests/README.md), with `androidTests.profilePath` pointing to your private Android profile. Keep the actual-driver target and required local/processor suites in that plan. Run the workflow through Test Explorer or the CLI; do not add this project to `localTests`, because it belongs after the actual-driver update. Export the private settings environment variable in the worker process before starting that workflow. The coordinator suppresses Android context during discovery and supplies it only during execution.

Keep credentials, contexts, test reports and captures outside the repository and public release assets. The tests save only selected gateway UI properties, screenshots and masked UI hierarchies in the private evidence directory.

## What these results prove

The assertions compare real UI rows with the selected management device and verify Home restoration. Repeated Android resource IDs are resolved within each labelled control row, so the Hot Water assertion cannot accidentally read the Away button.

A matching saved Home endpoint and familiar tile name are not proof of the application's active network route. The optional fresh name challenge provides stronger association with the management instance. It is not cryptographic route or package attestation. This project does not authorize physical controls, validate the Office/second-room isolation cases, prove all submission requirements or produce a signed/approved submission. The management snapshot and challenge records are diagnostic evidence; they are not yet bound into the submission producer contract.

Development validation on 2026-09-16: both cases passed against an already-installed Entity V2 Wiser gateway, with Home restoration and seven capture hashes verified. Hot Water and Away state remained unchanged. This was a read-only fixture validation under the existing processor/Android reservations, not a new deployment, complete CI run or exact Release submission acceptance run.


The subsequent packaged workflow also passed end to end: 96 desktop tests, 53 processor tests, three live reads, the gated Debug driver update, three installed health checks and both Android cases. It used an isolated TestAdapter NuGet restore with no UI automation library source reference. Gateway and room identities, schedules and setpoints were preserved. Both reservations were released. The temporary test instance was removed; the archive preserved from the earlier failed validation was separately removed after identity/hash checks. This remains development validation in a logged-in, minimized BlueStacks session, not certification evidence or service-session validation.

The optional name-binding integration was separately validated on 16 September 2026 against the already-installed Wiser Debug gateway using released DevTools 1.5.0 and TestAdapter 1.7.1. Both fixture cases passed in the minimized Google Android emulator. Each observed a fresh temporary name, inspected the corresponding page and restored the original name. Fourteen capture pairs matched their recorded hashes; the inventory and observed gateway states were unchanged and both reservations were released. This was fixture validation, not a fresh deployment or an exact Release-candidate submission run.
