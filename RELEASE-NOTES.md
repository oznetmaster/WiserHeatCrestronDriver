# WiserHeatCrestronDriver v1.3.7

Patch release fixing overlapping room schedule selection and adding diagnostics for installed-driver testing. Hub API dependencies and normal room control behavior are unchanged.

- Apply the room command guard to schedule selection, preventing a second room command from overlapping an unfinished action.
- Expose room identity and command activity for the development test workflow. Completion is observed after command handling and fresh hub-state readback; independent readings determine whether the requested state was reached.
- Add optional read-only desktop probes for installed-room control observation and exact-package configuration compatibility during code rollback. Neither probe sends device control commands.
- Document private settings, strict state restoration, existing-tile preservation and the limits of compatibility verification.

## Validation

- Driver unit and lifecycle suites passed locally and on the processor. Probe tests cover observation, restoration and configuration/package refusal paths.
- An existing Office room passed Auto → Manual → Auto through the installed driver, with independent hub readings, guarded settings unchanged and its tile retained.
- A complete production workflow passed local, processor and read-only live checks, updated the driver, deliberately failed an installed read-only check, then restored the reviewed previous code. Configuration, gateway/child identities, room assignments and loaded health were preserved. The original workflow stayed failed as intended.
- Rollback evidence applies only to the exact reviewed package pair, not arbitrary future versions. The temporary test instance and its owned archive were removed and the reservation released.

See [CHANGELOG.md](CHANGELOG.md), [installed-room testing](docs/InstalledRoomControls.md) and [configuration verification](docs/ConfigurationRollback.md). Private inputs and recovery evidence are excluded. Processor test packages are separate GitHub-only releases.
