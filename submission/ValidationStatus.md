# Wiser submission pilot status

Updated 18 September 2026. This page describes the current candidate only. Earlier package-specific results and failed attempts remain in [development and validation history](../DEVELOPMENT-HISTORY.md); they do not establish acceptance of different package bytes.

## Candidate and release identity

The installed submission candidate is **1.3.11** (processor version `1.3.011.0000`). Its immutable package is `NeilColvin_Thermostat_WiserHeat_IP_V2.pkg`, with SHA-256 `b84375d629ac5af2f65c1f6cde4dabf74a6302ff100caafd65d157ac1e6ab3cf`. It uses published WiserHeatAPIv2 1.1.2. The public driver release remains 1.3.8; this candidate has not been submitted to Crestron, and no certification or completed acceptance is claimed.

The help retains the approved figures and official template, uses the repository/issue tracker for public support, and contains no public email address. Credentials, physical device bindings, raw screenshots, correspondence and signing material remain private.

## Verified against this candidate

| Scope | Evidence and limits |
| --- | --- |
| Build and package | Release build, merged-package startup, dependency bytes/notices and all eleven embedded help pages verified. Matching package/DLL/help names checked. |
| Deployment gate | Desktop tests, Mono processor tests, read-only hub tests and installed configured/online/ready checks passed. The existing gateway was updated; temporary test instance and owned test archive were removed. |
| Read-only Android | All three cases in the dedicated read-only project passed. The released auditor verified the complete 171-file producer inventory and 25 captures, installed payload, state/Home restoration and temporary-child cleanup. |
| Celsius native controls | Boost On/Off passed. The temperature case was interrupted by a canceled Android capture after its second tap. Independent restoration and cleanup passed, but that case remains failed and needs a fresh validation run. No uncertain tap was replayed. |
| Fahrenheit native controls | Both native temperature and Boost cases passed. The target changed by 0.9 F, independent hub readings and original control policy agreed, and the original Celsius driver configuration was restored. All 177 producer files, Home, temporary-child removal and released reservations were audited. Two captured states were visually reviewed at 1080x2400; this does not establish every layout or gauge calibration. |
| Celsius Off/resume | From an ordinary Auto policy, the fixture prepared Off directly on the hub, observed the Off row and hidden native controls, then used one Set to 5 C action. Hub and UI agreed on the resumed target. Original Auto policy, Home, inventory, temporary-child removal and released reservations were audited. An initial observation race failed and restored safely; the corrected observation-only wait passed in a fresh run. Manual/Fahrenheit Off starting variants remain separate. |
| Fixture failure handling | Offline control tests cover Celsius/Fahrenheit comparisons, restoration, uncertain replies, cancellation and unit changes. Seventeen configuration-cycle tests additionally verify preservation of unrelated settings and refusal to reconfigure while physical-control restoration is unconfirmed. These are tooling tests, not additional physical acceptance cases. |

The first candidate gate caught a case-sensitive fixture path error; its correction changed test code only. Its failed evidence is retained separately. The later control tests also leave the candidate package unchanged.

Producer hashes and source digests are retained before execution and compared afterwards. These records currently share the worker's local account: they do not establish independently authenticated producer identity. Filtered control cases do not constitute a complete Android-suite or official-plan pass.

## Remaining acceptance work

1. Finish candidate-bound control and rendered-UI coverage: reconcile the failed Celsius temperature observation, exercise manual starting states, remaining Off/resume variants, physical bounds, editor actions, save conflicts, repeated/busy/error behavior and conditional configurations. Verify labels/icons/layout and meaningful physical feedback, including the thermostat gauge; text assertions alone do not prove its calibration. The Off row's repeated long thermostat title ellipsizes at the tested viewport; the full identity remains visible above, and the Off state/action are readable.
2. Complete Configure/Setup, configuration persistence and removal coverage. Resolve the official multiple-instance requirement for a platform driver with independent bindings; two room children do not automatically prove two platform instances.
3. Validate controlled outages and recovery using appropriate physical-device isolation. A processor reboot does not prove a physical power-outage test.
4. Bind reviewed producers, private device identities and response-time budgets to every applicable official-plan scope. Preserve justified absence evidence; unknown or unimplemented cases cannot pass. The coverage blueprint remains a plan, not passing evidence.
5. Validate monitoring-worker restart, independent alerts and coordination, then collect at least 24 hours of periodic functional observations for the unchanged candidate. No formal endurance run is active. Remote logs may supplement functional checks, but cannot replace them.
6. Generate and review the actual evidence-populated official form, obtain the signature and authorization for that exact form, and validate delivery with retained receipts and reconciliation of uncertain outcomes.
7. Complete the optional protected CI handoff and demonstrate the entire submission workflow with the real delivery providers. Document the proven process for other driver repositories. Actual submission requires its specific final approval.

Ordinary development, hardware testing and GitHub releases remain independent of optional portal submission. Releases must remain possible when hardware or a local GitHub runner is unavailable. Library/client projects do not inherit driver-submission requirements.
