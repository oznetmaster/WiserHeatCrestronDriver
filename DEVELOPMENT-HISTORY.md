# Development and validation history

## Auto-mode fallback fix and candidate 1.3.12 preparation - 19 September 2026

The 1.3.11 Fahrenheit endpoint run failed after eleven downward inputs because Upstairs Hall changed from Auto to Manual. The room's original Auto policy, saved manual target and gateway Celsius configuration were independently restored; the owned test child was removed and reservations released. The failed evidence remains failed.

Offline tests reproduced a driver defect: a temporarily empty global schedule index caused a setpoint request to switch an Auto room to Manual. The fix uses the room's control mode and assigned schedule, validates the schedule before cancelling an override, and checks command acceptance. Three regression cases failed before the fix and passed afterward. The full desktop driver suite passed 193 cases, with three real-hub cases deliberately skipped; the net472 build passed. The hardware failure is consistent with this path, but its exact refresh interleaving was not instrumented.

This is a production change. Candidate 1.3.12 requires new processor acceptance and endurance evidence. The completed 1.3.11 endurance record is retained for that package only. This preparation is not a published release or a completed submission.

## Completed post-endurance and paired-instance checks - 19 September 2026

The unchanged 1.3.11 candidate completed 480 periodic observations across 24 hours and 1 minute; the collection was preserved and independently reviewed. Subsequent read-only UI, feature configurations, repeated room mode, native setpoint/Off and corrected repeated Boost checks passed with restoration. The earlier failed Boost expectation is retained as failed test evidence; its correction changed test code only.

Two processors using the same household hub passed fresh shared-state observations, room Auto/Manual/Auto, Away/hot-water controls and unsaved editor isolation/cancellation. The latter exercised day/time changes and four visible temperature rows; the original editor, persistent schedules and guarded rooms matched after Cancel. Each completed phase verified candidate preservation, temporary-child cleanup and released reservations. Released audit tooling checked the retained artifacts for the successful phases. These results do not establish every official requirement, independent-hub behavior or a second rendered app UI.

Submission validation status now distinguishes these results from remaining configuration, control, outage, final-form and delivery work. The signature remains private and unapplied. No production code, driver changelog or frozen package changed.

## Retain independently observed schedule save results - 19 September 2026

Save Day and Save All acceptance now retain the complete hub snapshot that satisfied their independent comparison, alongside the existing expected schedule record. This uses the already-read confirming snapshot and adds no device request. Both exclusive existing schedules and owned temporary schedules use this evidence path. Failure to store the snapshot fails the case and triggers the existing restoration without replaying the save. Earlier retained runs remain unchanged and cannot be claimed to contain these new records. This is test tooling only; the frozen driver candidate and endurance collector are unchanged.

## Boost response assertion corrected from retained hardware evidence - 19 September 2026

Review of the original Fahrenheit Boost response found that the tested HubR reports a timed Manual override with FromBoost origin, even though the outgoing request type is Boost. The newly prepared acceptance helper and its synthetic responses now use that observed state representation. A regression uses the non-identifying target, expiry and input time from the retained response; mismatched origins and override types still fail. Configured increase, duration, identity and restoration checks remain in place. This corrects test tooling only; no installed driver, package or endurance process changes.

## Configured Boost acceptance - 19 September 2026

The native room Boost fixture now reads the actual configured Celsius increase and duration and checks their physical result, in addition to the existing active/inactive feedback. The expected raw hub target is independent of the display units. It records the input interval and checks expiry against the configured minutes, allowing one minute for hub timestamp granularity and clock tolerance. Missing settings or insufficient headroom below the thermostat limit stop the case before input. A wrong observed increase or duration fails the test while the original room policy is still independently restored, without replaying the input. These are test-tooling changes only; the frozen driver and running endurance collector are unchanged. Hardware execution of the stronger case remains pending.

## Configuration acceptance topology clarified - 19 September 2026

The configuration test guide now maps the five official items to visible Setup/Configure observations and supporting API evidence. The draft blueprint describes a reserved driver instance using a shared household hub, rather than an isolated physical hub. Masked/omitted secrets are not claimed as readable saved values; authenticated restoration is recorded separately. No hub secret rotation, processor change or passing configuration attestation is implied. This changes the draft execution instructions only; the frozen candidate and running endurance policy are unchanged.

## Read-only temperature inspection in either unit - 19 September 2026

The read-only native thermostat fixture now accepts the original Celsius or Fahrenheit configuration and independently compares hub temperatures, processor feedback and rendered text at the documented display precision. It rejects unit changes, mismatched labels, unavailable readings and nonfinite values. This removes a Celsius-only limitation in the test harness without operating the hub, changing the driver or claiming gauge calibration. Offline comparison coverage supports preparation; the expanded physical case still requires execution.

## Room command failure and busy-state coverage - 19 September 2026

The desktop/processor fixture sources now exercise rejected HTTP writes, transport exceptions and failed confirmation reads through the room's public commands and the real library with a synthetic HTTP transport. A burst of mode, boost, temperature and schedule-selection inputs is refused while the first write is pending. Controls remain unavailable through the confirmation read, recover after failure, and accept one new explicit command without replaying the failed request. The desktop lifecycle suite passed; physical and rendered Android busy/error acceptance remains separate. No production source or frozen candidate package changed.

## Repeated room mode fixture preparation - 19 September 2026

A separately enabled Android case now performs three sequential room Auto/Manual/Auto cycles with one original-policy baseline. Each cycle has distinct evidence and requires independent restoration before continuing; unrelated settings, unexpected commands or an unsuccessful cycle stop the sequence. Missing manual targets are refused. This is prepared test code, not a hardware result, rapid-input coverage or a driver change. See submission/RepeatedRoomModeTesting.md.

## Submission build instructions - 19 September 2026

The submission guide now uses the released DevTools 1.11.0 console and its bundled help-packaging targets. It no longer asks consuming developers to configure Python or obtain a tools source checkout. LibreOffice and the official pinned template remain documented prerequisites. This documentation correction does not rebuild or change the frozen driver candidate.

## Explicit manual-target acceptance cases - 18 September 2026

The Android control project now exposes separate equal, different and absent saved-manual-target cases. Each requires its declared starting state before input; a mismatched binding or schedule boundary cannot silently satisfy another scope. The existing mode/restoration cycle remains responsible for preserving the saved target. The absent case still requires explicit permission for a potentially retained inactive value and distinguishes that outcome from exact restoration. No unsupported write or deletion of the hub's saved target is used to manufacture a case.

All 47 offline mode-cycle checks passed, including twelve new starting-state cases. Android discovery exposes the three physical cases. This prepares candidate testing; it is not evidence that those physical variants passed. No production source or frozen candidate package changed, and the ongoing endurance observation was not disturbed.

## Candidate 1.3.11 editor and saved-schedule validation - 18 September 2026

Eight further cases passed against the unchanged candidate. Five editor cases covered every current row, both 5/30 C boundaries, Cancel and restoration after deliberate day/time interruptions. Three save cases verified actual Save Day/Save All and refusal to overwrite independently changed hub schedules through either stale-save button. The evidence audit confirmed original persistent schedules and guarded room settings, editor state, Home, both temporary-child removals and released reservations. Each run retained 178 producer files; 77 editor and 31 save capture pairs matched their hashes. Reviewed screenshots showed readable boundary controls and the complete conflict recovery message. No production code, package or public release changed. Other layouts, native thermostat bounds, multi-instance and outage scopes remain separate acceptance work.

The candidate's 24-hour periodic functional collector started on the separate monitoring computer on 18 September at17:15:41UTC, reusing the validated service runtime. The first three scheduled observations passed. It binds the exact installed payload, driver lifetime and processor boot, and checks fresh successful refreshes plus independently observed gateway feedback. Completion must be supported by the actual journal and final applicable functionality checks; preparation or task registration does not establish endurance acceptance.

## Candidate 1.3.11 schedule mode validation - 18 September 2026

The unchanged candidate passed an Auto → Manual → Auto cycle on the selected room. Its saved manual target was already present and equal to the scheduled target. Independent hub and Home observations, two completed driver commands and the original room policy were audited; no new manual target was initialized. The temporary child was removed, inventory and viewport preserved, private inputs deleted and reservations released. All 178 retained producer files and ten capture pairs matched their pins. Two rendered Schedule-page screenshots showed readable identity, status and the appropriate Enable/Disable action. Other manual-target variants, layouts and complete official-plan coverage remain separate work. No driver package or public release changed.

## Candidate 1.3.11 Celsius temperature revalidation - 18 September 2026

The previously interrupted Celsius native temperature case passed in a fresh filtered run using the same candidate. Pre-execution discovery confirmed exactly the intended case; Boost was not repeated. Original policy, Home, inventory, owned-child removal, private-input deletion and reservation release were audited against the complete retained producer inventory. The original failed capture remains historical evidence; its cause was not reproduced or attributed to a shared Android-library defect.

## Candidate 1.3.11 Off/resume and display-failure restoration - 18 September 2026

The Celsius Off/resume case passed against the unchanged candidate. It prepared Off on the independently bound hub room, verified that native target controls were hidden and the explicit resume action was visible, used one Set to 5 C action, then restored and independently verified the original Auto policy. Home, temporary-child removal, inventory preservation, private-input deletion and reservation release were verified. The initial attempt caught a UI/configuration observation race and restored safely; that failure is retained separately. The corrected fixture waits for observations to agree without replaying input.

Physical recovery now uses independent hub observations when screen inspection fails, while preserving device identity, command attribution and household isolation checks. A failed final display assertion remains a failed test even when physical restoration succeeds. The focused and complete offline regression suites passed; Android fixtures built without warnings or errors. No driver package or public release changed. These cases do not establish the remaining manual/Fahrenheit Off variants or complete submission acceptance.

## Candidate 1.3.11 Fahrenheit controls and configuration restoration - 18 September 2026

The unchanged candidate passed both selected native Fahrenheit temperature and Boost cases. A guarded configuration cycle changed only the display-unit setting, verified preservation of the other saved values, then independently restored the complete original Celsius configuration after physical-control restoration and temporary-child cleanup. The producer inventory contained 177 files. Original hub policy, Home, inventory, private-settings deletion and reservation release were audited. The two reviewed 1080x2400 screenshots show readable Fahrenheit values and controls; exact gauge calibration and other layouts remain separate.

The preceding Celsius run passed Boost but failed its temperature case on a canceled Android capture after the second tap. Original hub policy and cleanup were independently confirmed without replaying the input; that failed result remains retained and is not treated as passed. Seventeen new offline configuration-cycle cases passed, covering uncertain replies, cancellation, journal failure, foreign changes and unconfirmed physical restoration. No driver code, package bytes or release changed.

The current validation-status page now separates current-candidate evidence from historical runs and lists remaining acceptance work without stale competing current-state claims.

## Native control fixture temperature units - 18 September 2026

The native thermostat test tooling now compares Celsius or Fahrenheit display values with the hub's raw Celsius readings and requires temperature units to remain unchanged throughout input and restoration. Unknown or incorrectly labelled units prevent input; a unit change during a command stops automatic compensation for reconciliation. The Android fixture validates the configured units and checks the corresponding display suffix.

Ten new offline regression cases first reproduced the limitation. All 397 control-tooling tests now pass, including scheduled/manual restoration, temperature limits, uncertain acknowledgements and unit changes. The Android control project builds without warnings or errors. This changes test tooling only: the installed 1.3.11 candidate is unchanged, and physical Fahrenheit and Off UI validation remain outstanding.

## Candidate 1.3.11 gates and read-only UI - 18 September 2026

The unchanged candidate package passed 574 desktop tests, 186 Mono processor tests, three read-only hub tests and three installed-readiness checks. The existing submission gateway updated to 1.3.011.0000. The corrected Off-control fixture and Celsius/Fahrenheit command, range and preservation regressions passed on the processor. The failed first attempt remains separate evidence; it did not update the actual driver.

The candidate then passed all three cases in the dedicated read-only Android project. The independent auditor verified 171 producer files and 25 captures against pins retained before execution, and the installed payload matched the archived package byte for byte. State, Home, original installed inventory, temporary-child removal, private-settings deletion and released reservations were verified. No heating command was sent by this read-only stage.

The CI test package left by the first failed attempt was removed using its original ownership record and the exact replacement file hash, preserving installed and manually deployed drivers. A local cleanup helper initially stopped before deletion because its evidence subfolder was missing; resuming the same reservation completed verified cleanup. Home may retain a cached catalogue entry until a planned reboot.

These checks establish bounded candidate behaviour, not completed submission acceptance. Native control tests, Fahrenheit/Off UI variants, remaining official-plan scopes, endurance and signed-form/delivery approval are still outstanding. Pre-execution pins were retained under the same local account; independent producer authentication is not claimed.

## Processor fixture path correction - 18 September 2026

The first 1.3.11 gate passed all 574 local tests, then stopped on one processor fixture: its translation lookup used `Translations`, whereas the packaged directory is `translations` on the case-sensitive processor filesystem. The fixture now uses the actual packaged path. The production candidate bytes are unchanged. The failed run did not attempt the actual driver update; its temporary instance was removed and its reservation released. Failed evidence remains retained, and a fresh gate run is required.

## Candidate 1.3.11 preparation - 18 September 2026

The candidate uses published WiserHeatAPIv2 1.1.2, whose release passed hosted and processor checks. Its published net472 DLL and retained MIT notice were independently reviewed and pinned. The driver now displays Celsius/Fahrenheit within supported physical limits and preserves Off targets and schedule slots until an explicit minimum-temperature action is chosen. The source-bound coverage draft includes those controls; its 571 scoped assertions are a plan, not completed acceptance.

The driver passed 187 offline tests with three live cases skipped against the corrected local library package. The actual merged Release package built against the stable published dependency without warnings or errors. Its entry point initialized successfully in the desktop SDK smoke check. All eleven help pages were reviewed: the packaged PDF retains the original template logo, the seven approved figures and unchanged template geometry, with exact embedded PDF verification. The package hash is `b84375d629ac5af2f65c1f6cde4dabf74a6302ff100caafd65d157ac1e6ab3cf`. Earlier hardware evidence remains bound to unchanged installed candidate 1.3.10. Candidate 1.3.11 still requires package, processor and Android acceptance; no certification or final submission is claimed.

## Off-state preservation - 2026-09-18 (local candidate work)

Off heating targets and schedule slots retain their control marker when display units change. The UI shows Off instead of a negative temperature; an explicit button labelled with the minimum target starts heating or changes only the selected pending slot. Editing another slot or a time preserves Off, and Cancel restores pending Off slots. Regressions cover Celsius/Fahrenheit, all ten slots, stale resume actions, saved payloads and bindings to real properties, commands and translations.

The complete desktop suite passes 187 cases with three live cases skipped against an isolated local package of the corrected library, and the matching net472 build passes without warnings. The library correction prevents an intermediate scheduled target when leaving Off. The UI still needs visual verification and candidate-bound processor/Android testing. No live commands, new candidate deployment or updated help PDF are claimed by these results.


## Temperature units and limits - 2026-09-18 (local candidate work)

The driver now keeps hub models and schedule data in Celsius and converts at the UI boundary. Room readings, target commands and all ten editor slots use the selected units. Published ranges match the library's physical 5-30 C limits: 41-86 F, with half-degree Celsius or 0.9-degree Fahrenheit steps. Changing display units preserves pending schedule temperatures. Boost configuration explicitly uses a Celsius difference from 1 to 5 degrees; non-finite input is rejected and finite out-of-range input is bounded.

Offline regressions cover initial and changed units, the SDK extension property-command path, raw Celsius writes, rejected inputs, pending schedule preservation and Boost configuration. The desktop driver suite passes 180 tests with three live cases skipped. The control suite passes 387 tests. The net472 driver/test build and Android control fixture build pass without warnings. An initial extension-command regression supplied a numeric value where the SDK requires text; the corrected fixture uses the actual UI input format and passes. No live heating operation was used for this work.

These changes are not in installed candidate 1.3.10 or a published release. The help source has changed but its final PDF has not yet been rebuilt or reviewed. A new immutable package and corresponding processor/Android validation are still required; existing candidate evidence must not be reused as acceptance of changed bytes. The later Off-state correction above supersedes the earlier open display question; physical UI acceptance remains outstanding.


## Candidate editor limits and interruption recovery - 2026-09-18 (no driver release)

Three existing Android cases passed against unchanged Release candidate 1.3.10 with TestAdapter 1.11.1 and DevTools 1.8.0. All four displayed schedule rows exercised the pending editor's 5–35°C limits and half-degree return steps. Separate deliberate interruptions after changing the day and time both restored the original editor, hub schedules and room settings. Home restoration, frozen producer files, original device inventory, removal of the temporary thermostat and released reservations were verified. No schedule was saved to the hub.

This is filtered coverage for one four-slot Celsius layout. Boundary preparation used SDK properties before Android button checks; it does not prove native thermostat limits or every layout. The interrupted operations remain failed in their raw receipts; their test cases pass by verifying the expected recovery. The separate Fahrenheit investigation reproduced library conversion defects offline and is being corrected before final acceptance.

## Released Android evidence integration - 2026-09-18 (test tooling, no driver release)

Both Android projects now consume TestAdapter 1.11.1 and DevTools 1.8.0. The complete read-only project passed against unchanged Release candidate 1.3.10 through the released Android workflow stage. The matching released Python auditor checked all three discovered cases, the complete 171-file producer inventory and 25 captures against pins retained before execution. State, Home, original inventory, temporary-child removal and reservation release were verified. The control project also builds with the upgraded dependencies without warnings or errors; its earlier hardware results retain their original tool versions.

The integration used a private coordinator calling the released stage, not a complete protected CI submission. An earlier coordinator readback failure was reconciled separately and remains failed; the corrected run completed normally. These results do not authenticate the worker, satisfy the whole official plan or authorize a submission. See [candidate status](submission/ValidationStatus.md).

## Native thermostat control fixtures - 2026-09-18 (test tooling, no driver release)

Added opt-in Android temperature and Boost cases with policy-aware restoration, retained original state, independently observed command completion and no replay after uncertain input. Scheduled and manual starting states, absent saved targets, cancellation, lost replies, foreign changes and incomplete restoration are covered by offline regressions. Both cases passed against unchanged candidate 1.3.10 from an Auto starting state, with original settings and schedule restoration, Home restoration and temporary-child cleanup verified. Earlier attempts corrected fixture label expectations and the hub's FromBoost origin for a Manual override; failed evidence was retained and restoration verified before rerunning. All 384 offline control tests pass. Ordinary desktop discovery skips the opt-in cases without private workflow context. See [control-test setup](WiserHeatCrestronDriver.AndroidControlTests/README.md) and [candidate status](submission/ValidationStatus.md) for scope and results.

## Entry-point test placement - 2026-09-18 (no driver release)

The embedded-definition regression belongs to the desktop SDK project: processor test packages deliberately remove dependency driver manifests so their own package identity remains unambiguous. The first 1.3.10 processor run passed the logging regressions but caught this incorrect test placement, and blocked the actual driver update. The regression now runs in the desktop SDK suite; the separate smoke test still exercises the actual merged submission package. No test is ignored and no production code or candidate package bytes changed for this correction.

## Submission startup and polling diagnostics - 2026-09-18 (not released)

The first renamed submission candidate failed in the SDK's implicit embedded-JSON lookup. The entry point now identifies its resource explicitly. A regression reproduces the former exception in the desktop SDK harness; a separate smoke tool exercises the actual merged package, and hosted tests now include the renamed package check. It does not replace final processor acceptance.

Repeated schedule construction/publication diagnostics accounted for over 99% of an 8 MB processor log sample. Unchanged polls now emit no such messages in either Debug or Release; changed options or selection produce a concise message. Existing UI notifications and failure logging remain intact. Regression checks cover repeated unchanged polls, a renamed schedule and a connection failure. The corrected submission candidate uses version 1.3.10; failed candidate 1.3.9 was not published.

See the [product changelog](CHANGELOG.md) for shipped changes. This document preserves test, CI, build and submission preparation history. Dated development entries describe work at that time, not a published product version or completed acceptance. Version headings identify the release alongside which development work was recorded; processor-test versions identify separate test packages.

## Where changes belong

- Product changelog and product release notes: shipped behavior, API, compatibility, fixes and runtime dependencies. Mention validation briefly when it helps explain a fix.
- This history: test coverage, CI, build tooling, test-package releases and work on pending candidates. Split mixed entries so the product effect remains easy to find.
- Testing and workflow guides: current setup and operating instructions. Submission guides, where applicable: preparation, evidence and acceptance status.
- Test-only or documentation-only changes do not require a product release. Processor-test releases update this history, not the product changelog.

<!-- development-history -->

## Room-command completion reporting - 2026-09-18 (no driver release)

Failure-injection tests reproduced success being reported for a rejected or unapplied schedule assignment and for failed follow-up reads after setpoint, boost and schedule-mode commands. Schedule assignment now requires acceptance, a successful refresh and the requested observed assignment. Other room commands return their refresh result. No uncertain write is automatically repeated. Enabling schedule control now also requires a confirmed prerequisite assignment before sending the room-mode change. Regression tests cover rejection, failed refresh, an unapplied assignment and the successful sequence. The gated Debug `1.3.008.0001` update passed 516 desktop checks, 153 processor checks, three read-only live-hub checks and three installed-state checks. The processor results include all 22 room-command and assignment result cases. The existing gateway was updated only after the test gates passed; its temporary test instance and package storage were removed and the processor reservation released. The driver now consumes published WiserHeatAPIv2 1.1.1; the restored assembly matches the release artifact. These are driver fixes prepared for patch 1.3.8, not final Release-candidate submission acceptance.

## Repeated Away-control validation - 2026-09-18 (no driver release)

Added bounded one-to-three Away cycles with a shared original-state contract, separate cycle evidence and no continuation after failure or unconfirmed restoration. The Android fixture defaults to one cycle. Two cycles passed on unchanged Debug `1.3.007.0030`: all four hub/driver/UI transitions and the original guarded state were verified, followed by Home restoration, owned-child removal, preserved inventory and released reservations. An earlier attempt restored its first cycle but timed out establishing a management connection before the second input; its failed result is retained. The short Away observations now reuse the active gateway connection instead of opening a new login for each room read. All 363 control-probe tests passed, and all 21 Android cases remained unexecuted without explicit workflow settings. This validates sequential repetition, not rapid input, every busy/error state or final Release acceptance.

## Schedule membership and rejected selections - 2026-09-18 (no driver release)

The open Android schedule selector passed temporary unassigned schedule addition and removal on Debug `1.3.007.0029`, after an explicitly approved capacity cleanup. Original hub settings and selection, Home, temporary-child removal, inventory and released reservations were verified. This result does not cover stale-ID rejection or the final Release candidate.

New regression tests reproduced a driver defect: invalid or deleted schedule IDs could replace the published selection before validation, and a different valid choice could replace it while another room command was busy. The source fix validates choices against the current catalog and publishes selection only from refreshed hub state. Nine focused checks and the desktop SDK suite pass, including pending, rejected and successful assignment requests. The gated Debug update passed 492 local checks, 135 processor checks (including all nine new cases under Mono), three read-only live-hub checks and three installed-state checks. It updated the submission driver to `1.3.007.0030`, removed its temporary test instance and package files, and released its processor reservation. Home retains a cached catalog entry until its next planned reboot. This is a tested source fix, not a published driver release or final-candidate acceptance. See [validation status](submission/ValidationStatus.md).

## Gateway control validation - 2026-09-18 (no driver release)

Validated Away-mode changes and restoration through the Android app on the unchanged Debug candidate. The hot-water check exposed test assumptions about aggregate override status and inactive fields omitted after cancelling an override. Corrected those assumptions, added regressions, and restored scheduled control independently before a fresh run. The corrected normal hot-water fixture passed both UI states and original scheduled-policy restoration, with owned-child cleanup and released reservations. No production driver or library code changed. See [validation status](submission/ValidationStatus.md#gateway-away-and-hot-water-controls---18-september-2026) for the retained initial failure, recovery and exact limits.

## Native thermostat temperature inspection - 2026-09-18 (no driver release)

Added an explicitly enabled, read-only Android check comparing the current temperature, unit label and heating target with paired independent hub readings and processor properties. It scopes the repeated target identifier to the native heating setpoint control, allows bounded refresh propagation, captures screenshots and verifies Home restoration. The dedicated fixture passed on the unchanged Debug candidate `1.3.007.0029`; its temporary child was removed and the original inventory and both reservations were verified. No heating command was sent. This establishes ordinary Celsius label rendering for the observed case, not gauge needle geometry, every temperature boundary or final Release-candidate acceptance. See the [control-project guide](WiserHeatCrestronDriver.AndroidControlTests/README.md#read-only-native-thermostat-temperatures) for opt-in settings and limitations.


## Gateway control fixture preparation - 2026-09-18 (no driver release)

- Added an opt-in Android Away-mode cycle with independent hub observations, guarded room/schedule/override preservation, response timing and observed Home restoration. It supports either initial Away state and never replays an uncertain input. Whole-house authorization and actual hardware validation remain separate prerequisites.
- Added hot-water restoration planning that distinguishes schedule control, manual mode and manual overrides. Offline checks reject a button returning to its original value with the wrong control policy, and reject timed overrides until absolute-deadline restoration is verified. Saved hub-response replay checks use the actual hot-water array layout.
- Connected the hot-water restoration contract to a complete opt-in Android cycle. Both button states, independent hub feedback, bounded compensation and observed Home restoration are required. Fault tests cover ignored cancellation, lost replies, cancellation and changes outside the test; physical acceptance remains pending.
- These are test and submission preparations, not final-candidate acceptance or a driver release. See [validation status](submission/ValidationStatus.md).


## Schedule membership fixture and capacity preflight - 2026-09-18 (no driver release)

- Added an opt-in fixture for a temporary unassigned schedule appearing and disappearing in an already-open selector. Recovery checks require durable intents, prohibit uncertain-write replay and protect schedules that are changed or assigned externally.
- The first hardware attempt was rejected with the hub at its documented 16-climate-schedule limit. Separate reconciliation confirmed unchanged hub state and cleaned the temporary Home child and reservations; the test remains failed. Capacity is now checked before attempting creation. A successful live add/remove result still requires a free schedule slot; see [validation status](submission/ValidationStatus.md).


## Unchanged schedule-choice refresh validation - 2026-09-18 (no driver release)

- Added an opt-in read-only Android fixture that keeps the schedule selector open through two observed successful hub refreshes. It verifies the entire option list, original selection, independent hub state and driver lifetime, then confirms navigation and temporary-device cleanup.
- Added regressions ensuring cached reads, malformed/backwards timestamps and restarted drivers cannot stand in for successful refresh evidence. The real development run passed without hub writes or editor compensation. See [validation status](submission/ValidationStatus.md) for exact-package scope and remaining acceptance work.


## Live schedule-choice validation - 2026-09-18 (no driver release)

- Added explicitly enabled Android cases for a schedule renamed while the Schedule page or selection dialog is already open. Independent hub and driver observations require the original selected ID, updated label, complete option list and full state restoration.
- Corrected the test scanner to establish both list ends when a name change reorders options but preserves the visible anchor. The earlier failed attempt remains separate; the fresh two-case hardware run passed with cleanup. Offline regressions cover lost writes, unsafe restoration, missing options and incorrect selection.
- See [validation status](submission/ValidationStatus.md) for candidate and visual limits. Added/deleted choices, unchanged-refresh stability and final submission acceptance remain separate.


## Mode-change recovery validation - 2026-09-18 (no driver release)

- Added a separately enabled Android case that deliberately interrupts after Manual is independently observed, then requires exactly one configuration compensation back to Auto. The interrupted operation stays failed in its evidence; the recovery case passes only after verified restoration.
- The real development-processor run passed with the original manual target, complete persistent schedules and guarded room settings restored. Home, temporary-child cleanup and reservation release were verified. See [validation status](submission/ValidationStatus.md) for the exact candidate and limits; this is not crash, outage or endurance proof.


## Schedule-layout validation - 2026-09-18 (no driver release)

- Added an explicitly selected Android fixture to prepare declared schedule sizes, inspect every labelled time/setpoint row through scrolling, and restore complete original state. Offline tests cover failed writes, interrupted inspection and unrelated changes that prevent safe restoration.
- The real hub accepted and the UI rendered every documented one-to-eight-entry layout, including growth and reduction back to one entry. Independent state, editor/Home restoration and temporary-child cleanup passed on the exact development package. An earlier ten-entry request was rejected and remains a separate failed run.
- See [validation status](submission/ValidationStatus.md) for candidate, display and acceptance limits. This test-only work does not change the public driver version.


## Conflict warning correction - 2026-09-17 (pending driver release)

- Shortened the schedule-conflict warning after actual portrait screenshots showed the recovery instruction cut off. The warning now reads "Schedule changed. Cancel and reopen."
- The gated development update and both stale-save UI cases passed with independent hub/state restoration and test cleanup. Both captured save paths display the full instruction at the tested viewport. See [validation status](submission/ValidationStatus.md) for the exact candidate and scope.

## Schedule conflict validation - 2026-09-17 (no driver release)

- Added separately selected Save Day and Save All conflict cases, backed by independent hub observations, original-state restoration and failure-path regression tests.
- Both actual UI cases passed on the preserved Debug candidate. Screenshots revealed a truncated conflict warning, so readable feedback remains an open submission item.
- The complete control-probe suite passed; all Android cases stay offline without an explicit workflow context. See [validation status](submission/ValidationStatus.md) for the exact evidence and limits.

## Editor recovery validation - 2026-09-17 (no driver release)

- Add explicitly selected day/time interruption cases with independent restoration assertions. Both passed on the development processor; an interrupted operation stays failed even when its recovery assertion succeeds.
- Use the verified extension property/command routes for editor and mode recovery, with observed state confirmation. Correct the save fixture's corrupted degree-symbol expectation. These changes affect test producers only.
- The corrected Save Day/Save All case passed on the exact development package with independent hub checks, complete schedule/editor restoration and temporary-child cleanup. No final candidate or conflict coverage is claimed.
- All ten Android cases correctly stay offline without a workflow context. See [validation status](submission/ValidationStatus.md) for the exact hardware scope and outstanding acceptance work.



## Submission candidate development - 2026-09-17 (not included in 1.3.7)

These are incremental development records, not a released driver version. Earlier open investigations describe their recorded point in time; see [current validation status](submission/ValidationStatus.md) for resolved issues and remaining work.

- Add read-only `lastHubRefreshUtc` and `driverLifetimeId` diagnostics for monitoring. Successful current-generation hub reads advance the explicitly tagged UTC timestamp; cached and failed reads do not. Recreating the root changes its lifetime identity even when the package and processor boot are unchanged. Use the documented HVAC category for managed thermostats.
- Add a separate .NET 10 endurance producer and its offline tests to the solution and CI, consuming released DevTools 1.7.0. Short real-function checks on Debug `1.3.007.0026` passed with exact active payload, fresh independent hub state and standard evidence export. A deliberate same-version root reload invalidated the previous lifetime as expected. This is not final-candidate endurance or submission acceptance.

- Restore existing room readiness and online state after a successful gateway reconnect without replacing their controllers. A stopped room previously remained not ready after restarting. Regression tests reproduce the failure, verify recovery and ensure failed refreshes do not revive offline rooms. Debug candidate `1.3.007.0014` passed the full workflow, including all six Android cases with released TestAdapter 1.9.0, both UI saves and exact state restoration. Initial room commissioning remains a separate investigation.
- Preserve pending schedule edits across ordinary hub polls. Detect changed or reassigned schedules and refresh the hub before a save so an old editor cannot overwrite an observed newer schedule. Cancel reloads the current hub values. Driver regression tests cover these cases and Save Day/Save All scope. Debug candidate `1.3.007.0013` passed the live editor-selection/Cancel case and a subsequent focused Save Day/Save All case with exact original schedule restoration.
- Add separately configured schedule-save UI tests and restoration checks. Default isolation uses a new temporary schedule; an explicit alternative operates an existing schedule only when exclusively assigned to the selected room. The latter passed on hardware after the hub rejected additional schedule creation. Keep failed attempts and recovery evidence; offline regressions cover shared-schedule refusal, uncertain replies, ignored writes, cancellation and unrelated changes. Consume TestAdapter 1.9.0 for labelled-row controls. This test work adds no production behavior beyond the existing development candidate and does not complete the submission plan.
- Publish changed day, time, temperature and conditional slot values immediately when an editor selection changes. SDK event regressions reproduced missing notifications before the correction. Require a successful fresh schedule read after Save and compare the observed day data with the requested values; unconfirmed saves retain pending edits and report an error.
- Remove read-triggered withdrawal and re-registration of room controllers. Existing room reads preserve their UI registration, while actual room discovery/removal still updates the controller list. A complete Debug workflow has passed with the registration fix.
- Validate the opt-in room mode-control UI cycle with a saved manual target different from the current target. Restore the saved target and scheduled occupancy readings; retain failed-run evidence and verified recovery. A per-room `AllowManualTargetInitialization` option now supports the no-saved-target test path: it verifies initialization from the active target and restoration of Auto/schedule settings, while reporting any retained inactive target separately from exact restoration. The option defaults to false, passes offline checks and has not yet been exercised on a room without a saved target.
- Add independent schedule/editor checks for the live Cancel test. Two Debug attempts failed before completion; both restored the editor, hub settings and Home, and released reservations. The corrected driver passed the subsequent complete Debug workflow, including all five Android cases, restored state, temporary test-instance removal and released reservations. Package cleanup preserved an archive that predated that run. This development work does not complete the official submission test plan or produce a Crestron submission.

## Submission candidate development - 2026-09-16 (not included in 1.3.7)

- Add a separate opt-in Android control project for an independently observed Auto/Manual/Auto schedule cycle, with durable intents and bounded restoration. Initial offline failure-path checks passed; subsequent live validation is recorded in the 17 September entry. Existing manual targets below, equal to or above the scheduled target are supported and preserved; absent manual targets require a separate initialization policy. Existing inspection workflows remain read-only.
- Fix schedule, day and time selection dialogs so their choice lists and selected values are available to the Home app. The complete lists were checked in the minimized Google Android emulator.
- Shorten the hot-water actions to Turn On and Turn Off, and the shared schedule action to Save All, so their meaning remains visible on portrait screens. Save All applies the selected day's entries to all days of the selected shared schedule.
- Extend the Wiser Android fixture to inspect the thermostat, Schedule and Edit Schedule pages before checking the full selection lists. Use the published TestAdapter 1.8.0; Wiser page names and expected values remain in this repository.
- The complete Debug workflow passed local and processor tests, live hub reads, the installed driver update, health checks and every discovered Android case. Checked state and Home were restored; the temporary test instance and uploaded archive were removed. These runtime fixes await a driver patch release and final Release-candidate validation.
- Prepare submission help and reviewed dependency notices. Public help screenshots, completed acceptance evidence and signed submission remain separate requirements; this source update is not a Crestron submission.

## Android UI test development - 2026-09-16 (no driver release)

- Optional private name binding temporarily renames the gateway, observes the corresponding app tile and restores the name. Uses DevTools 1.5.0 and TestAdapter 1.7.1; both cases passed in the minimized Google emulator with the original inventory and observed states preserved. No physical control commands or new driver release.
- Add an opt-in NUnit project that opens the gateway page twice, checks the Hot Water and Away status/action/enabled state against fresh management data, verifies unchanged gateway state and returns to Home.
- Keep settings and captures private. Ordinary desktop runs skip without connecting; hosted CI verifies this behavior. Include the project in the solution and consume the Crestron Home NUnit UI automation library from the test adapter package, eliminating the source-checkout dependency. Use test adapter 1.7.0 from NuGet. The complete packaged development workflow passed; submission evidence acceptance remains separate work.

## WiserHeatCrestronDriver.ProcessorTests v1.0.2 - 2026-09-16

Published processor test package on GitHub. This is a test-package release only; no driver or library NuGet package is published. See the matching package release notes for changes and validation.

## 1.3.7 - 2026-09-16

- Add read-only observation and exact-package configuration probes, refusal/restoration tests, and documentation.

- Validate existing-room Auto/Manual/Auto restoration and a complete production update, intentional failed check and verified code rollback. Preserve the existing tile and current configuration.

## Offline release workflow option - 2026-09-15 (no package release)

- Allow an explicit manual release when local hardware or the self-hosted runner is unavailable, with the reason and exact source recorded in the workflow summary.
- Keep hosted source validation mandatory and preserve all build, test and packaging steps. No runtime, API or package-version changes.

## CI package cleanup - 2026-09-15 (no driver or processor package release)

- Update Test Explorer workflow containers to CrestronHomeNUnit.TestAdapter 1.3.0 and document opt-in storage cleanup after successful CI runs.
- Retain original deployment filenames, protect pre-existing/manual packages and preserve failed-run evidence. Cleanup frees archive storage without rebooting; Home can retain cached catalogue entries until its next planned reboot.
- Actual driver/library code and processor test packages are unchanged by this tooling update.

## 1.3.6 - 2026-09-15

- Replace duplicated test-count constants with discovery-to-execution and source-to-package identity checks; adding tests no longer requires editing CI totals.

- Add eight command-refresh regression cases; 47 local and 47 processor tests, three live hub checks and three installed-driver health checks passed. The hot-water interaction was also verified manually.

## CI validation - 2026-09-15 (no package release)

- Revalidate the current default-branch source after successful release workflows, including version commits created by GitHub Actions.
- Allow maintainers to configure exact-source, App-specific checks that must pass before publishing through `RELEASE_REQUIRED_CHECKS`; missing, failed or unconfirmed checks block the release.

## WiserHeatCrestronDriver.ProcessorTests v1.0.1 - 2026-09-15

Published processor test package on GitHub. This is a test-package release only; no driver or library NuGet package is published. See the matching package release notes for changes and validation.

## 2026-09-15 - Test and development tooling (no driver release)

- Add the published Test Explorer workflow adapter, offline discovery CI and independent GitHub processor-test releases. Private workflow plans control optional live tests, actual-driver updates and temporary-instance cleanup.

- Add three opt-in live driver checks for authenticated Wiser hub discovery, room identity/telemetry refresh and reconnect. Share the library's private live settings. Include a separate Live Hub processor suite; no room control commands are sent.
- Coordinate build deployment through the shared DevTools processor reservation.

## 1.3.5 — 2026-09-14

- Cover room discovery, stable child identity, renamed/removed rooms, cleared settings, overlapping connections and late refresh/login completion. Clearing or disposing the platform now removes its children and prevents old work from restoring them. Update WiserHeatAPIv2 to 1.1.0.6.

- Normalize the working manifest from `1.0.008.0102` to `1.3.004.0102`; this aligns the development version family with the latest existing three-part release. No historical tags or packages are changed.

- Standardize driver versioning: Debug project/package metadata follows the manifest including its build increment; local Release builds preserve it; three-part release tags select the exact CI release without another patch increment. Verify source and built package versions before publication.

- Expand driver coverage to 29 offline tests and 10 SDK entity/lifecycle tests, with a desktop SDK harness and the same lifecycle fixtures in the net472 processor package.

- Add 29 NUnit driver unit tests and a processor lifecycle suite in the existing solution.

- Add a standalone Utility processor test package with private Debug deployment settings.

## Earlier Android save/Cancel validation notes

These dated candidate records were moved from the control-test setup guide. They retain their original scope and do not describe the current final acceptance status.

The explicitly selected existing-schedule case passed against Debug candidate `1.3.007.0013`: both UI saves matched independent hub contents, all original days and guarded room settings were restored, the editor and Home were restored, inventory was preserved and reservations were released. This was one selected save case, not a repeat of all six Android cases. Its frozen producer used the private 1.9.0 adapter candidate; the project was then separately built from the released public NuGet package and verified to skip all hardware cases without a workflow. The earlier read-only setup failure remains retained evidence. Temporary-schedule creation/deletion is covered offline but has not passed on this hub. Final Release-candidate validation remains outstanding.

The first complete Debug attempt and a focused repeat failed after selecting another day, then verified restoration and released reservations. SDK regressions subsequently reproduced missing day/time/temperature change notifications, and the driver correction passes those checks. The subsequent complete workflow against Debug candidate `1.3.007.0013` passed all five Android cases, including this case, and independently confirmed unchanged persistent schedules/room assignments, editor and Home restoration. The temporary test instance was removed and reservations released. The package archive was preserved because it existed before the run. These results do not establish live Save behavior or final Release-candidate acceptance.
