# Android gateway UI tests

This opt-in NUnit project compares the real Crestron Home Android gateway page with fresh configuration-management state. It opens the uniquely named gateway tile, checks the Hot Water and Away Mode status, action caption and enabled state, and closes the page. Two consecutive cases exercise repeatability. No heating, away, room-assignment or configuration commands are sent.

Ordinary desktop test runs skip these cases without connecting to Android or a processor. They are separate from the processor test package and must run on the Android worker while the processor workflow owns both reservations.

## Current development dependency

The new extension navigation helpers are not released yet. Build with a sibling `CrestronHomeNUnit` source checkout, or pass `-p:CrestronHomeNUnitSourceRoot=<checkout>`. The project references its `CrestronHomeNUnit.Android` project and the released DevTools package. It is intentionally not yet included in the default solution or existing CI jobs, which must remain buildable without this second checkout. Integrating the project into the distributed Android workflow remains pending.

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

Keep credentials, contexts, test reports and captures outside the repository and public release assets. The tests save only selected gateway UI properties, screenshots and masked UI hierarchies in the private evidence directory.

## What these results prove

The assertions compare real UI rows with the selected management device and verify Home restoration. Repeated Android resource IDs are resolved within each labelled control row, so the Hot Water assertion cannot accidentally read the Away button.

A matching saved Home endpoint and unique tile name are not proof of the application's active network route. This project therefore performs **read-only checks only**. It does not yet authorize physical controls, validate the Office/second-room isolation cases, prove all submission requirements or produce a signed/approved submission. The management snapshot is diagnostic evidence; it is not yet bound into the submission producer contract.

Development validation on 2026-09-16: both cases passed against an already-installed Entity V2 Wiser gateway, with Home restoration and seven capture hashes verified. Hot Water and Away state remained unchanged. This was a read-only fixture validation under the existing processor/Android reservations, not a new deployment, complete CI run or exact Release submission acceptance run.
