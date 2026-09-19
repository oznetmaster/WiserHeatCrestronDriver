# Endurance evidence and final checks

The [official Extension test plan](https://sdkcon78221.crestron.com/downloads/Test-Plans/Extension-Test-Plan.pdf), System Tests item 3, requires a connected period of at least 24 hours with periodic functional checks, followed by working functionality and no performance degradation. The driver blueprint represents these as three required observations mapped to the same official item:

| Observation | Required evidence |
| --- | --- |
| `continuous-function` | Original periodic collection, its reviewed cadence, actual successful samples, candidate/instance/lifetime identity and gap/failure history. At least 24 hours; read-only collection does not invent a state-changing action or restoration. |
| `post-period-functionality` | Final tests after the collected period covering every applicable gateway, room, thermostat and schedule function. Capture, restore and independently verify state changed by these tests. |
| `performance-comparison` | Review of comparable baseline, periodic and final response/feedback measurements, with named functions, operating conditions and acceptance budgets. |

All three are required. The form mapping must not check the endurance item from the periodic export alone. A final test's duration must not be presented as its command-response latency. Screenshot generation and later navigation must not inflate the interval being compared.

## Preserve the original collection

Keep the original collection policy, worker, producer pins, sample journal, scheduler incidents and export unchanged. A collection policy and a full submission policy have different scopes and can have different identities. The original export must not be relabelled or represented as an earlier execution under the later full policy.

After known successful completion and confirmed reservation release, retain and revalidate the journal before proceeding with final tests. The DevTools source completion snapshot script can do this without replacing the pinned collector; see [Windows endurance worker](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/submission/WindowsEnduranceWorker.md) for release availability and usage. Preserve the matching collector/probe inventories separately. A snapshot is private evidence, not a public release asset or a completed submission bundle.

## Bind the final review

Use the same unchanged candidate and recorded installation. Record final test start/end times after the last periodic sample. The final acceptance record must explicitly link the original collection evidence, actual test/producer records, independent physical feedback, restoration/cleanup results and the performance comparison. Do not extend the periodic observation's endpoints to include later tests without actual samples covering that extended interval.

The three-way form mapping enforces that each observation is supplied. Generic evidence validation does not independently infer which tests cover every function, compare operating conditions, or establish chronology between different observations. The consuming review must check those facts from retained timestamps and assertions before constructing the full-policy observations. Keep its source references, original identities and rationale with the derived review. The generated policy still needs its reviewed maximum sample gap and producer bindings; draft generation is not approval or execution.

Missing or incomparable baseline measurements cannot support an unqualified no-degradation conclusion. Likewise, cached connection/readiness values cannot replace periodic functional observations. Keep any such gap visible for review instead of changing evidence timestamps, dropping failed attempts or declaring success from a test count.

## Native thermostat response records

The native-control fixture now records `input-N-first-match` separately from `input-N-observed`. The first record is the first complete matching observation of independent hub state, processor properties and rendered UI. The second still requires two matching observations; a transient first match does not pass the test. Writing the first record does not capture another screenshot or issue an input. A failed evidence write still fails the cycle and invokes its guarded restoration.

These records include `InputTiming` with the dispatch-start timestamp, observation timestamp, monotonic elapsed seconds, route and boundary. The timer starts immediately before the guarded Android tap, after preliminary reads and screenshot capture. It therefore includes the tap's own guards, dispatch and subsequent reads, but excludes earlier preparation and later confirmation screenshots. These are observed response upper bounds, not the precise physical device-change time. Direct HTTP preparation for an Off/resume case is labelled `direct-hub-off-preparation`; it must not be counted as a driver-command measurement.

The existing `Value.Seconds` remains the broader cycle timer, so earlier records keep their meaning. In the retained pre-period native-control records, approximately seven to eight seconds elapsed between the cycle intent and the tap intent. Keep those original timings as conservative bounds rather than relabelling them as device latency. Compare the same boundaries and operating conditions in final tests; do not claim a precise improvement from changing instrumentation. New records cannot supply a missing historical measurement.

This separation changes submission evidence organization only. It does not change the driver package, restart an active observation period, waive final checks or establish that the official item has passed.
