# Android gateway UI tests

This opt-in NUnit project compares the real Crestron Home Android gateway page with fresh configuration-management state. It opens the uniquely named gateway tile, checks the Hot Water and Away Mode status, action caption and enabled state, and closes the page. Two consecutive cases exercise repeatability. No heating, away, room-assignment or configuration commands are sent.

Ordinary desktop test runs skip these cases without connecting to Android or a processor. They are separate from the processor test package and must run on the Android worker while the processor workflow owns both reservations.

## UI automation dependency

The project uses the Android assembly included in `CrestronHomeNUnit.TestAdapter` 1.7.0 and is included in the driver solution. No second source checkout is required. Hosted CI builds the project and verifies that all cases skip without a hardware context. Restore that version from NuGet before using this project.

## Private settings and invocation

The workflow supplies `CRESTRON_HOME_ANDROID_CONTEXT`, including the installed gateway identity, candidate hashes, Android profile and private evidence directory. Never create a context that invents passing candidate or reservation evidence.

Set `CRESTRON_HOME_WISER_UI_SETTINGS` to an absolute path outside the repository containing:

```json
{
  "Host": "192.0.2.10",
  "UserName": "YOUR_PROCESSOR_USER",
  "Password": "YOUR_PROCESSOR_PASSWORD",
  "CertificateSha256": "YOUR_VERIFIED_PROCESSOR_CERTIFICATE_SHA256"
}
```

The processor address must match the workflow context. Its gateway must be online, loaded, have the expected version and have a unique name in the management inventory. Both Hot Water and Away capabilities must be visible for this initial fixture. It currently checks the English captions. Unavailable capabilities and other languages require additional fixtures.

Add this project as `androidTests.project` in the existing private [processor workflow](../WiserHeatCrestronDriver.WorkflowTests/README.md), with `androidTests.profilePath` pointing to your private Android profile. Keep the actual-driver target and required local/processor suites in that plan. Run the workflow through Test Explorer or the CLI; do not add this project to `localTests`, because it belongs after the actual-driver update. Export the private settings environment variable in the worker process before starting that workflow. The coordinator suppresses Android context during discovery and supplies it only during execution.

Keep credentials, contexts, test reports and captures outside the repository and public release assets. The tests save only selected gateway UI properties, screenshots and masked UI hierarchies in the private evidence directory.

## What these results prove

The assertions compare real UI rows with the selected management device and verify Home restoration. Repeated Android resource IDs are resolved within each labelled control row, so the Hot Water assertion cannot accidentally read the Away button.

A matching saved Home endpoint and unique tile name are not proof of the application's active network route. This project therefore performs **read-only checks only**. It does not yet authorize physical controls, validate the Office/second-room isolation cases, prove all submission requirements or produce a signed/approved submission. The management snapshot is diagnostic evidence; it is not yet bound into the submission producer contract.

Development validation on 2026-09-16: both cases passed against an already-installed Entity V2 Wiser gateway, with Home restoration and seven capture hashes verified. Hot Water and Away state remained unchanged. This was a read-only fixture validation under the existing processor/Android reservations, not a new deployment, complete CI run or exact Release submission acceptance run.


The subsequent packaged workflow also passed end to end: 96 desktop tests, 53 processor tests, three live reads, the gated Debug driver update, three installed health checks and both Android cases. It used an isolated TestAdapter NuGet restore with no UI automation library source reference. Gateway and room identities, schedules and setpoints were preserved. Both reservations were released. The temporary test instance was removed; the archive preserved from the earlier failed validation was separately removed after identity/hash checks. This remains development validation in a logged-in, minimized BlueStacks session, not certification evidence or service-session validation.
