# Coverage source review

## Evidence-to-form checkpoint - 18 September 2026

The bundled coverage command verified the current reviewed source snapshot and reproduced the existing draft policy hash without changing the candidate declaration. Its 571 scoped assertions map to 35 official form items; these are obligations, not a required number of independent physical test runs. One verified sequence can support several assertions. The 342 assertions under the multiple-instance item still require observed results. The chosen plan uses two actual gateway-driver instances on separate processors with the existing physical hub; disclose that topology and verify independent configuration alongside expected shared-state propagation. Waiting for clarification is not a prerequisite. A second room child is not being substituted for a second gateway instance.

The packaged validator accepts the unchanged candidate's package structure and metadata. A deliberate check with no official attestations reports all 571 missing requirements and blocks completion. Existing successful unit, processor and Android results therefore cannot automatically check the official form. Their narrower evidence still needs reviewed scope/producer/latency/restoration bindings; absence proposals need actual applicability review, and collection must complete before endurance acceptance.

The retained official form confirms that power and network interruptions require at least 60 seconds, followed by bounded recovery checks. A software reboot is not the physical power-outage case. Those tests must follow the current uninterrupted endurance observation and use an explicitly agreed isolation arrangement. No interruption, final form generation or submission was performed for this checkpoint.

## Candidate 1.3.11 temperature and Off review - 18 September 2026

The reviewed source changes keep raw hub/schedule data in Celsius and convert only at the display boundary. Display ranges represent the physical 5-30 C bounds in either unit. Off remains a control sentinel, with separate visibility and explicit minimum-temperature commands for the native thermostat and every editor slot. Time edits, other-slot edits, unit changes and cancellation must preserve Off. An explicit resume must not first restore the scheduled temperature; WiserHeatAPIv2 1.1.2 corrects that command path.

The blueprint adds the native HeatingOffStatus and ten editor Off controls, plus preservation and deliberate-resume requirements for single and repeated instances. Supported one-to-eight-event layouts require observed visibility; defensive slots nine and ten retain synthetic coverage and runtime absence evidence. Current offline regressions cover values, commands, cancellation, saved payloads and real UI bindings, but do not substitute for visual/processor evidence on the new package.

The published dependency assembly is pinned by hash, with its original license notice retained and linked to the released source commit. All these changes require a new immutable candidate; prior 1.3.10 acceptance cannot be transferred to it. The expanded draft has 571 scoped assertions and remains incomplete, with producer bindings and budgets still to be finalized.

## Corrected candidate review - 18 September 2026

Candidate 1.3.10 adds two reviewed changes to the proposal below: entry-point initialization now names the embedded definition explicitly, and unchanged polling no longer emits repeated schedule snapshots. Require initialization of the actual renamed merged package, followed by successful processor loading, plus retained observation of quiet steady-state polling, meaningful schedule-change messages and error visibility. Property and definition notifications remain unchanged. The prior 1.3.9 load failure is retained as failed evidence and cannot satisfy these obligations. Updated source hashes identify this correction; the execution policy and final acceptance remain incomplete.

## Submission candidate 1.3.9 review - 18 September 2026

The current blueprint pins the candidate based on released driver 1.3.8 source `c63d2b15b51a8ac791074356e8cee4b9623fec98`, with submission version `1.3.009.0000` and illustrated help. The package and dependency inventory now participate in the source review. This is a reviewed coverage proposal, not an approved execution policy or completed self-test.

| Reviewed change | Required candidate evidence |
| --- | --- |
| Standard SDK dispatch and stable child registration replace the experimental rebind workaround. Existing children recover after a successful refresh. | Initial child configuration, including a prompt-free step; stable identity across reads; native heat-only thermostat rendering under the SDK Hvac category; recovery after a successful reconnect and no false recovery after failure. |
| Schedule selection waits for confirmed assignment; enabling Auto without an assignment requires that confirmation first. | Accepted, rejected, missing and unconfirmed assignment cases, independent hub ID/mode read-back, readable error state and original assignment/mode restoration. |
| The editor preserves pending changes during polling and rejects stale saves using fresh pre-save and post-save observations. | Both Save Day and Save All after external schedule edits and reassignment; failed reads, rejected writes and ignored writes; pending-state preservation, complete recovery message and Cancel/reopen. Restore every affected shared schedule and room setting. |
| Temperature inputs enforce finite supported bounds and publish individual day/time/slot updates. | Actual inputs, bounds, formatting and visibility for every supported one-to-eight-event day layout, including growth and shrinkage. Retain synthetic protection for defensive slots nine and ten, and runtime absence evidence on this eight-event hub. |
| Room command completion propagates failed read-back. WiserHeatAPIv2 1.1.1 propagates schedule-write results. | Independent post-command state, unsuccessful confirmation on failed read-back, recovery from busy state and no automatic command replay. Library and processor regressions support this requirement but do not replace final UI evidence. |
| Lifetime and successful-refresh properties provide monitoring observations. | Bind the exact instance lifetime, verify strictly advancing successful hub reads, and retain failures, missing samples and restarts. Cached reads cannot stand in for functional endurance. |
| New submission basename, version, help and dependency notices. | Match package/DLL/metadata/PDF names, GUID and version; verify exact embedded help/notices. Preserve approved figures, template parts and readable layout. Install and test those exact package bytes. |

The blueprint adds separate assignment-confirmation, stale-save, schedule-enable-prerequisite and command-read-back scopes, including their two-instance counterparts. All producer bindings remain unresolved. Response budgets, physical outage instrumentation, two-instance interpretation, complete candidate controls and the 24-hour run still need execution and evidence. No historical Debug result is relabelled as a candidate pass.

The initial candidate build completed with ManifestUtil 29 and verified its embedded PDF and notices. All eleven packaged help pages were visually reviewed. The canonical document preview omitted the original template logo; independent inspection of the actual packaged PDF confirmed that it retains the logo and that the other ten pages render identically. This preview difference does not justify changing the supplied template or the package.

## Schedule membership and hub capacity - 18 September 2026

The first normal add/remove fixture attempt on exact Debug 1.3.007.0029 returned HTTP 400, "Could not create new schedule", before any new schedule was observed. The hub already held 16 heating schedules. Schneider's [UK/Ireland system guide](https://www.productinfo.schneider-electric.com/wiser_home/wiser-home-sug-uk/English/System%20User%20Guide_Wiser_Home_UK%20%28Bookmap%29.pdf) documents a maximum of 16 climate schedules per hub. This full-capacity attempt cannot establish whether the complete seven-day creation payload is accepted when space is available.

The failed run and its original unconfirmed-restoration receipt are retained. A separate reconciliation independently checked the unchanged complete persistent schedules and guarded room settings three times, verified editor/selection and Home, removed only the owned temporary Home child, restored the original inventory, removed private settings and released reservations. No existing schedule was deleted or heating control sent during reconciliation.

The fixture now fails capacity preflight before opening the selector or sending a creation request. Offline regressions cover the limit, complete unassigned creation, uncertain replies without replay, required journals, cancelled/failed UI observations and external edits or assignments that prevent deletion. Actual successful creation, open-dialog addition/removal and stale-ID rejection remain unverified pending free capacity. Existing schedules are never removed automatically to satisfy a test prerequisite. The prior rename and unchanged-refresh successes do not replace this missing coverage.

## Open selector across unchanged refreshes - 18 September 2026

The normal OpenScheduleChoicesSurviveRepeatedUnchangedHubRefreshes case passed on exact Debug1.3.007.0029 using public TestAdapter1.11.0. After observing the complete initial list, it witnessed two strictly advancing successful hub-read markers from the same installed driver lifetime. All16 choices and the original selected ID remained correct after both refreshes, with the selection dialog continuously open.

The evidence audit confirmed matching independent persistent hub schedules and guarded room settings, unchanged editor/selection, no hub writes or editor compensation, observed Home restoration, original inventory/viewport, temporary-child removal, private-settings deletion and released reservations. The private producer copy and installed payload were hash-verified. This establishes the unchanged-list case for the tested candidate and display profile; added/deleted choices, stale-ID rejection, other profiles and final Release acceptance remain separate.

## Live schedule name changes - 18 September 2026

Both open-page and already-open-dialog rename cases passed on exact Debug1.3.007.0029 using public TestAdapter1.11.0. Each changed only the name of an exclusively assigned schedule through the independent hub API. The running driver and app displayed the new label with the same selected ID; every schedule option was observed. No driver reload or dialog reopening was substituted for the update.

An initial run passed the page case but failed the dialog case when a downward-only scan missed the renamed item. Restoration and cleanup passed; that run remains failed. The open list preserves its visible anchor when sorting changes. A fresh run established both list ends, found the renamed item above the original viewport, verified its selected state and covered every option. Offline regressions still reject genuinely missing, stale or incorrectly selected options.

Independent audits confirmed original names, complete persistent schedules, guarded room settings, editor and Home restoration, original inventory, temporary-child removal and released reservations. The selected long name is abbreviated on the compact native selector button; the full label was visible in the page summary and option list. This result does not establish every visual profile, choice addition/removal, repeated unchanged-refresh behavior, final Release acceptance or endurance.

## Deliberate mode interruption and recovery - 18 September 2026

`InterruptedManualModeTestRestoresAutoThroughConfiguration` passed on exact Debug `1.3.007.0029` with public TestAdapter 1.11.0. The fixture used the real Disable UI control, independently observed Manual on the hub and Home, then deliberately interrupted the observation phase. The existing configuration recovery sent one distinct `enableSchedule` command through `extension:doCommand`. The saved manual target was restored before Auto; the interrupted operation remained recorded as failed while its recovery was verified.

The audit confirmed exactly two completed driver commands in the same lifetime, matching independent room identity, restored original manual target, all persistent schedules and guarded room settings, Home, original inventory, temporary-child removal and released reservations. No absent-target initialization was permitted or needed in this run. The normal fixture compiles without warnings and skips without a workflow context. This is bounded development proof of compensation after an observed mode change, not arbitrary crash/outage recovery, final Release acceptance or endurance.

## Supported schedule layouts - 18 September 2026

The normal Android control fixture passed the declared sequence of one, eight, two, seven, three, six, four, five and one entries on exact Debug `1.3.007.0029` using public TestAdapter 1.11.0. Every supported time and setpoint row was observed with the correct values, including scrolling where needed. The final one-entry layout removed all surplus rows. Independent hub snapshots confirmed each requested layout and unchanged unrelated schedules and guarded room settings. Original schedules, room settings, editor, Home and inventory were restored; the temporary child was removed and reservations released.

The evidence audit checked every layout, hidden surplus model slots, original-state snapshots, frozen producer and exact active package. Visual review covered both eight-entry viewports and the final one-entry view. The full control-probe suite and ordinary context-free Android discovery also passed. This is development evidence for one existing exclusive schedule and one display profile, not final Release acceptance, endurance or certification. The separate ten-entry rejection below remains a failed run.

## Hub schedule capacity - 18 September 2026

The first layout run observed a one-entry schedule, then the hub rejected the ten-entry request with HTTP 400 (`Time: array overflow: Time`). The run remained failed; independent final snapshots confirmed complete original schedule and guarded room-setting restoration, Home, original inventory, temporary-child removal and released reservations.

[Drayton documents a maximum of eight events per day](https://wiser.draytoncontrols.co.uk/pages/wiser-app). The driver's ten modeled editor slots do not establish ten-entry hub support. Physical acceptance therefore needs every supported one-to-eight-entry layout, including growth/shrinkage and hidden surplus controls; synthetic coverage remains relevant to the defensive extra slots. The subsequent supported-layout run above is separate from the retained failed attempt and has its own live evidence. Neither a model test nor the capacity documentation alone is a passing live test.


## Conflict warning correction - 17 September 2026

Debug `1.3.007.0029` shortens the clipped warning to "Schedule changed. Cancel and reopen." The complete gated update passed local, Mono processor, read-only live and installed-state checks. A separate run repeated both real stale Save Day/Save All cases against its exact active payload using public TestAdapter 1.11.0. Independent hub observations, pending editor preservation, complete original-state restoration, temporary-child cleanup and reservation release passed. Both after-save screenshots show the complete warning at the tested 1080x2400 viewport; the earlier Debug28 truncation is resolved for that profile.

Package SHA-256: `873D8754409AABC4990C2757E6C872A23F76829023545A4883613B278EFEF705`. This is development evidence for an existing exclusive schedule and the tested display profile. Other conditional layouts, display profiles and final-candidate acceptance remain separate. The public driver release remains v1.3.7; this correction is not yet part of a tagged driver release.


## Schedule conflict validation - 17 September 2026

Both `ScheduleConflictRefusesStaleSaveAndRestoresOriginal` cases passed on exact Debug `1.3.007.0028` (SHA-256 `9146679AA8337293F508D3E7E635266CDFAFC6CC1C00244C5DC8708C6CAF5F80`) using the normal Android control project and public TestAdapter 1.11.0. Each made a pending UI edit, independently changed the exclusively assigned hub schedule, and tapped Save Day or Save All once. Actual hub snapshots confirmed that the newer schedule survived; every pending editor value and visibility flag was preserved. Cancel/reopen loaded the current hub data. The full original schedules, guarded room settings, editor, Home, inventory and emulator dimensions were restored, the temporary child was removed, and both reservations were released. The complete control-probe suite passed, including the new failure/recovery cases.

Visual inspection found that the conflict warning is ellipsized before its recovery instruction finishes. The functional conflict tests therefore do not establish readable feedback; that UI defect remains open. This run covers observed stale edits on an existing exclusive schedule, not arbitrary simultaneous writers, temporary schedule creation, final Release acceptance or endurance.

## Current review status - 17 September 2026

The historical snapshot below no longer pins the current runtime. The subsequent room-registration and schedule-editor corrections require a new final source inventory and producer bindings. Do not populate acceptance checkboxes from the old snapshot or from the successful Debug workflow alone.

Initial managed-child commissioning is now independently explained and verified. Automation had omitted the new child's first configuration step, which Configure Pro requests even when the child already reports configured and the step returns no prompts. Commandless diagnostics reproduced the difference on an unchanged package; the corrected DevTools coordinator and actual console command then passed against Wiser. This was an automation omission, not evidence of an SDK or Home defect. Temporary commissioning diagnostics have been removed from the Wiser runtime.

The cleaned Debug `1.3.007.0022` package passed the complete gated workflow: 223 local tests, 77 processor tests, three read-only hub tests and three installed-driver state checks. Its SHA-256 is `38D9B6320F419FD72996F1FFECBFBE13DCCD0CF67D68C5170D239261C0861D49`. Temporary processor tests and uploaded package bytes were removed; cached catalogue metadata can remain until a planned reboot. These are development results, not final candidate acceptance or endurance evidence.

The normal combined workflow subsequently passed on Debug `1.3.007.0023`, package SHA-256 `1C908D9C29BADE1F46A5CE105D8FFBBF4445D5E5FF7AA36F83EEF74A99480503`. Its additional editor Cancel case used the packaged 1.10.0 adapter candidate and released DevTools 1.6.0. The coordinator created the thermostat, supplied its actual ID through the explicit alias, verified restoration and removed its owned child. The selected-day and pending-time edits were discarded, reopening matched independent hub data, and persistent schedules, room settings and Home were preserved. Independent inventory and reservation checks passed after cleanup.

The producer was a private source-pinned copy selecting only that Cancel case, with exact discovery verified. This is bounded development integration evidence, not a run of every Android control case or final candidate acceptance. The complete local adapter release build subsequently passed isolated package acceptance and all current Android regressions. Earlier failed attempts remain failed; neither recovery nor this success replaces remaining conditional-slot, adjustment, save or conflict evidence.

A later focused run on exact Debug `1.3.007.0026` bytes passed with test-source checkpoint `c13aeb4`. It verified every active payload file before commissioning a temporary child, then checked all eight current rendered time/setpoint controls before and after a pending time edit. Cancel/reopen, unchanged independent schedules and assignments, Home restoration, original inventory and removal/released reservations passed. This used the published TestAdapter 1.10.0 without a local Android assembly override. Thirty additional synthetic SDK/Mono cases cover all ten model slots and variable day lengths; those cases do not replace ten-slot rendered UI or live conflict coverage. A subsequent focused run on the same installed candidate also passed pending plus/minus inputs across its four visible setpoint rows, checked isolated state changes and matching rendering after each, and verified Cancel/hub/Home/child cleanup. Both directions in every slot, limit states and off-screen inputs remain separate requirements.

A fresh `ScheduleEditorCurrentRowsRespectTemperatureLimitsAndCancel` run passed on the exact Debug `1.3.007.0028` payload using the normal control project and public TestAdapter 1.11.0. Every current setpoint row was exercised at 5 and 35 degrees, with inward and return Android inputs changing only their own pending value; an initially off-screen row was revealed by scrolling. Boundary preparation and pending-value restoration use configuration commands, not UI input. Cancel, independent saved schedules and room settings, Home, original inventory, temporary-child removal, emulator dimensions and reservation release were verified. This is current-schedule development evidence, not ten-entry rendered coverage, save/conflict validation, a final Release candidate or certification.

| Source | Subsequent change | Required evidence |
| --- | --- | --- |
| `WiserRoomEntity.cs` | Ignore non-finite/out-of-range editor temperatures before activating an edit; preserve valid endpoints and rounding. | Fourteen SDK-dispatched cases passed on desktop and Mono, including closed-editor refresh after invalid input. The full Debug28 update/readiness gate passed. The separate current-row Android boundary run passed on exact Debug28; larger conditional layouts and final-candidate UI evidence remain required. |
| `WiserDriverEntryPoint.cs`, `WiserPlatformDriver.cs` | Keep room controllers registered during reads; use standard SDK dispatch. | Preserve existing-room identity/registration during reads and verify discovery/removal. Initial commissioning is resolved by the independently verified DevTools configuration entry described above; retain the earlier failure evidence and repeat the final candidate lifecycle checks. |
| `WiserRoomEntity.cs` | Preserve pending editor state across polling, detect observed schedule changes/reassignment, and publish day/time/temperature/slot updates. | The Debug selection/Cancel case and SDK events have passed. Still bind complete conditional-slot, adjustment and conflict scenarios to the final candidate. |
| `WiserPlatformDriver.cs`, `WiserRoomEntity.cs` | Require fresh pre-save state and confirmed post-save day data. | Desktop regressions cover rejected stale saves, failed reads and ignored writes. A focused Debug case independently observed both UI saves on an exclusively assigned schedule and verified exact restoration. Bind the final producer and immutable Release candidate separately; temporary-schedule mode and remaining conditional-slot cases still need physical evidence. The later stale-save run is recorded above, including its unresolved warning truncation. No atomic exclusion of external hub edits is claimed. |
| Android control producer | Add optional initialization when a saved manual target is absent. | Pin the producer/settings policy separately. Report any retained inactive target and distinguish operating-state restoration from exact original-state restoration. The authorized prior Debug case passed with operating-state restoration and an explicitly reported retained inactive target; exact original-state restoration was not claimed. Bind and repeat the applicable final-candidate case separately. |
| `WiserPlatformDriver.cs`, `WiserRoomEntity.cs` | Restore stopped existing rooms after a successful reconnect without changing controller identity. | Regressions reproduce the readiness failure and verify recovery plus refusal to revive rooms after a failed refresh. The complete Debug `1.3.007.0014` workflow passed all six UI cases using the released adapter. That historical recovery result did not establish the initial-commissioning cause; the independent diagnostics described above now do. Final candidate recovery evidence remains required. |

## Schedule-save recovery validation

The corrected Save Day/Save All fixture subsequently passed on the exact Debug `1.3.007.0028` payload with the public TestAdapter 1.11.0. Both real UI saves matched independent hub assertions on one existing, exclusively assigned schedule. Full original schedules and guarded room settings, editor day/values, Home, inventory and emulator dimensions were restored; the temporary child was removed and reservations released. The run exercised the configuration day-write and Cancel recovery. The saved page was visually inspected for readable labels, values and action scope. This does not cover temporary schedule creation, save conflicts, mode-recovery compensation or final candidate acceptance.

## Deliberate editor interruption validation

Two deliberate editor interruption cases passed on the exact Debug `1.3.007.0028` payload with the public TestAdapter 1.11.0. The changed-day case required the observed configuration day-write and Cancel fallback; the pending-time case restored through UI cleanup without that fallback. Both independently verified original editor values, hub schedules and room settings, Home, inventory, temporary-child removal, emulator dimensions and released reservations. The interrupted operations remain failed in their journals; separate expected-interruption assertions passed only after recovery was confirmed. This does not prove arbitrary network/process failure recovery or final Release acceptance.

## Historical snapshot - 16 September 2026

The draft coverage snapshot now reflects the tracked runtime sources through `e24b5d40f41829a989279429fd207d588957902e`. This is a source review, not policy approval or completed test evidence. Generated local Debug revisions were excluded; the tracked manifest still declares release version `1.3.007.0000`. A final candidate version or any further behavior change requires another review.

Two source changes were reviewed against the prior snapshot:

| Source | Change | Coverage consequence |
| --- | --- | --- |
| `WiserRoomEntity.cs` | Publish inline schedule choices and refresh the property definition when available values change. | Retain the complete initial choice/selection checks and add a distinct live-definition check: updated labels/options must reach an already-open selector, selection must remain associated with the correct ID, and removed choices must not assign an unintended schedule. Use isolated schedules and restore every affected assignment. |
| `IncludeInPkg/Translations/en-US.json` | Shorten the hot-water actions to Turn On/Turn Off and the shared schedule action to Save All. | Check actual rendered labels, readability and action scope. Save All still affects all days in the selected shared schedule; its shorter caption does not narrow the restoration obligation. |

The other nine declared sources, including both UI definitions and the tracked manifest, still match the reviewed snapshot. The manifest already includes the approved GitHub support website and empty email field; no metadata pin was changed. Local Debug-version edits were not imported. Check that support route again in the final package/help.

The added Android control fixture does not modify production behavior. Its assembly, dependencies, discovery inventory and assertions must be bound separately as a test producer; a runtime source hash does not identify the test executable.

Reading current lists and cancelling the editor does not prove live definition refresh, selection actions, shared saves or physical behavior. The new control cycle likewise does not satisfy the complete official control requirements by itself. All generated producers remain unbound and the execution contract remains `submissionReady: false` until actual candidate-specific validation and policy review are complete.
