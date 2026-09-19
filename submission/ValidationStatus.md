# Wiser submission pilot status

Updated 19 September 2026. The submission is not complete and has not been sent to Crestron. The results below apply to the frozen candidate; they do not imply certification or acceptance by Crestron.

## Candidate and test environment

- Driver candidate: **1.3.11**, manifest **1.3.011.0000**.
- Package SHA-256: `b84375d629ac5af2f65c1f6cde4dabf74a6302ff100caafd65d157ac1e6ab3cf`.
- Production source: `04a6ac45bb7374536a547d075e06d017d1f3794e`, using published WiserHeatAPIv2 **1.1.2**.
- Physical equipment: one Drayton Wiser **2nd Generation HubR, 3 channel, CCTFR6313G2D**. Current compatibility with a first-generation hub or other regional Wiser systems is unverified.
- Paired tests use two actual gateway-driver instances on separate Crestron Home processors, connected to that same household hub. They do not represent two independent hubs.
- Android tests use the Crestron Home app in the Google Android emulator. Private bindings identify the intended processor, gateway and room; temporary test children are removed after confirmed restoration.

The production package has not changed during the post-endurance tests. Test-harness corrections and updated test-tool dependencies do not change that package. Current fixtures use released CrestronHomeNUnit.TestAdapter **1.12.1** and CrestronHomeDevTools **1.13.1**; the released runner's `installed-tests` command reserves the resources and verifies the candidate before and after execution.

## Completed observations

| Area | Verified scope |
| --- | --- |
| Endurance collection | 480 passing observations across 24 hours and 1 minute. The retained collection was independently reviewed, including sample gaps, fresh hub feedback, candidate payload and stable processor/driver identity. The original monitor is stopped and its reservation released. Final functionality and performance are separate parts of the official endurance item. |
| Read-only post-period UI | Four selected cases passed, covering gateway controls, native temperature text and schedule-selector observations. |
| Gateway feature configuration | All four Away/hot-water visibility combinations were observed. Original configuration and physical policy were preserved. |
| Room Auto/Manual | Three cycles passed with exact restoration of the original room policy and existing manual target. |
| Native thermostat controls | Celsius setpoint changes and Off/resume passed with restoration. The separate native endpoint tests reached 5 C and 30 C through the actual app controls, with matching hub feedback and restoration after each case. Independent review confirmed stored schedules and core room/household policies; calculated current/next schedule readings were allowed to advance naturally. Fahrenheit endpoints remain untested. |
| Repeated Boost | Three cycles passed with independent hub target/override checks and original-state restoration. The first attempt failed because the test used the wrong expected target; that failure is retained. The corrected expectation accounts for the tested hub's ambient-relative Boost. No driver fix or new endurance run was needed. |
| Two-instance baseline | Both gateways produced fresh matching feedback with separate lifetimes. Each preserved its candidate payload, configuration, name and location. |
| Two-instance room control | One UI Auto/Manual/Auto cycle passed. Both processors agreed with the physical hub, the peer issued no room commands, and the original policy was restored. |
| Two-instance gateway controls | Away and hot-water transitions passed with independent hub observations and peer feedback. Original schedules, room policy, gateway configuration and hot-water policy were restored. |
| Two-instance unsaved editor | Day/time and four initially visible temperature-row edits remained local. Cancel restored the original editor; captured persistent schedules and guarded room settings were unchanged, as were peer configuration and command activity. Other time rows, layouts and a second rendered UI are separate scopes. |
| Two-instance saved schedules | Save Day and Save All matched the requested hub schedules and propagated to the peer. Original stored schedules, room policy and editor selections were restored, including separate restoration of the peer's preparation. Both acting-instance saved pages were visually reviewed. These observations do not establish exact response latency or a second rendered UI. |

Completed phases above have confirmed cleanup and released reservations. Released audit tooling has checked retained producer inventories, discovery/selection, test results and capture consistency for the audited runs. Artifact consistency is not, by itself, evidence that an entire official checklist item passed. Local producer receipts also do not establish independently authenticated worker identity.

The original failed attempts remain available; later success does not rewrite them. Source implementation and offline recovery tests are not presented as real outage observations.

## Work remaining before the final form

1. Finish the applicable rendered controls, schedule editing/saving and paired-instance checks. Retained assertions must be matched to their actual scope, including relevant units, native bounds, conditional layouts, configuration, timing and recovery. Prepared alternatives are not an instruction to run every plan indiscriminately.
2. Complete the applicable Configure/Setup, persistence and removal observations, and document the limits of the shared-hub topology. A peer property observation is not a second rendered app screen.
3. Resolve the physical power/network outage setup and obtain authorization for the actual household interruption. A software reboot does not establish physical power-loss behavior.
4. Reconcile final functionality and performance with the completed endurance collection, then bind reviewed observations to the official form. Do not mark unsupported or unverified assertions as passed.
5. Generate and visually review the evidence-backed form, obtain authorization for that exact signing copy, and retain the actual upload/email receipts. The supplied signature is private and has not been applied.

The [coverage plan](extension-coverage-plan.json) and [coverage review](CoverageReview.md) retain the complete proposed interpretation of the official checklist. Their expanded assertions are not a count of required independent test runs. [Endurance evidence](EnduranceEvidence.md), [two-instance testing](TwoInstanceTesting.md), [native boundaries](NativeThermostatBoundaries.md) and [alternate units](NativeThermostatUnits.md) describe the corresponding execution and evidence boundaries. Earlier implementation work remains in [development history](../DEVELOPMENT-HISTORY.md).

The unsigned form draft has been visually checked, but its checklist and signature remain blank. It is not a signing or delivery copy. Any submission with declared gaps must be an explicit decision and must identify those gaps; neither a complete nor an incomplete submission carries a guarantee of acceptance by Crestron.
