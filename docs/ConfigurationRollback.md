# Wiser configuration verification for code rollback

`WiserHeatCrestronDriver.ConfigurationProbe` is a read-only .NET 10 executable for the opt-in rollback contract in CrestronHomeNUnit 1.5.0. It does not import packages, change settings or restore an old credential file.

Wiser's driver configuration consists of seven persistent SDK items: hub address, hub secret, temperature units, boost delta, boost duration, hot-water visibility and away-mode visibility. Schedules and device state remain on the Wiser hub. This verifier covers that configuration contract only; it is not a verifier for other driver families or an assurance that arbitrary future Wiser versions are compatible.

Create a private reviewed-pair file containing absolute `previousPackage` and `nextPackage` paths, their exact `previousSha256` and `nextSha256` values, and a nonempty `review` describing the source review that established compatibility. Review both versions for configuration readers, writers, startup migrations and any additional persistent state. An identical manifest alone does not establish that review. Freeze the exact packages before filling in the hashes; rebuilding creates a different pair.

Configure the rollback probe with the executable/DLL and these arguments:

```text
--processor-settings C:/Private/Processor.json --pair C:/Private/run/ReviewedPair.json
```

Processor settings contain `Host`, `CertificateSha256`, `UserName` and `Password`. Keep them and the pair file outside tracked source. The workflow appends its `--request` and `--response` paths.

On every call the probe verifies both package hashes, driver family, increasing versions and identical configuration metadata. It opens a fresh authenticated processor connection, reads the selected installed driver's configuration, validates the complete seven-item schema and current value types, and fingerprints all values. The secret contributes to the fingerprint: changing credentials cannot pass as unchanged configuration. Missing, masked, placeholder or unknown fields cause refusal. No configuration values or credentials are printed.

The selected instance and request must match one of the two exact versions and the expected previous-package hash. The normal workflow additionally verifies the upgrade, loaded version, complete child scope, operation completion and post-rollback health. The original failed test result remains failed even when code restoration succeeds.

Package, schema and fingerprint refusal paths have automated coverage. A complete production-driver workflow passed on the development MC4-R: local, processor and read-only live tests passed; the driver was updated; a deliberately impossible read-only check failed; and the workflow restored the reviewed previous code. Current configuration fingerprints, gateway/child identities and room assignments, loaded version and health checks were verified. The existing room tile was retained. The original workflow result remained failed, its temporary test instance was removed, and the reservation was released. Its owned test archive was subsequently removed after the expected rollback result was verified.

This validates that exact reviewed package pair, not arbitrary future versions. Keep rollback disabled until your selected pair has its own compatibility review.
