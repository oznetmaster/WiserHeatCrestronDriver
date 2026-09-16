# Wiser submission pilot status

Wiser is the first end-to-end submission pilot. No Crestron submission has been sent, no signed acceptance form has been generated, and no certification is claimed. This driver has not previously been accepted on the portal. Its submission profile uses Neil Colvin as the developer and the repository/issue tracker for public support, with an empty public email field. Private correspondence and signing assets remain outside the repository.

## Development evidence

The complete Debug workflow has passed desktop and processor tests, live hub reads, the actual-driver update, installed health checks and all discovered Android inspection cases using released TestAdapter 1.8.0 and DevTools 1.5.0. It checked the gateway options, room thermostat, Schedule and Edit Schedule pages, complete schedule/day/time selector contents and selected values. Editing was cancelled. Temporary name binding, Home restoration, checked device-state preservation and released reservations were verified. Temporary test-instance storage was removed; a cached catalogue entry can remain until a planned reboot.

The gateway and room tests live in [AndroidTests](../WiserHeatCrestronDriver.AndroidTests/README.md). Those page names, labels and expected control behavior are Wiser-specific. Navigation helpers do not supply these assertions automatically. These Debug results are not evidence for an exact final Release candidate or for physical controls that were not operated.

The separate [Android control project](../WiserHeatCrestronDriver.AndroidControlTests/README.md) adds an explicitly selected Auto/Manual/Auto UI cycle with independent hub observations and restoration. Offline regressions cover differing manual/scheduled targets, uncertain input delivery, cancellation, journal failures and identity/activity changes. Real UI control validation remains pending. The existing installed-driver command probe has earlier hardware evidence, but that does not establish the Android control path.

## Documents and package

A private illustrated help draft was rendered and inspected with the official template. Publication of household screenshots remains subject to approval. The help source still needs its final candidate version and approved figures. Dependency notices were matched to the actual development merge inputs; final help and notice inclusion by ManifestUtil must be verified in the exact Release package.

The blank Extension self-test draft was rendered and inspected. Its checkboxes contain no passing attestations. The coverage blueprint is still a draft and needs reviewed source pins, executable producer bindings and complete candidate-specific observations before it can populate the form.

## Remaining acceptance work

- Validate the real mode-control UI cycle and the remaining commands, selector actions, conditional editor slots, Save All behavior, physical feedback, timing and state restoration.
- Complete visual/icon checks and Configure/Setup coverage. Text assertions alone do not verify rendered layout.
- Establish independent room/device bindings and resolve the formal multiple-instance requirement for a platform driver; two room children do not automatically count as two platform instances.
- Provide controlled outage testing and at least 24 hours of periodic functional observations, including recovery and interruption handling.
- Build one final Release candidate with matching package/DLL/help names, verify its bytes and provenance, and execute the complete required evidence plan against that immutable artifact.
- Review applicability and the completed official form, supply and authorize the signature, and validate supported upload/email delivery with retained receipts and uncertain-outcome reconciliation.

Ordinary driver development and GitHub releases remain independent of optional portal submission. None of the incomplete items above should be presented as passed because a shared tool or another fixture has passed its own tests.
