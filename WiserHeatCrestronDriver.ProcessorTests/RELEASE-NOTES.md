# WiserHeatCrestronDriver Tests

## 1.0.1

- Rebuild with CrestronHomeNUnit 1.2.1. Test execution now participates in the shared processor reservation used by the runner, Test Explorer, CLI and hardware CI.
- The net472 package contains 42 discovered cases, with 39 in automatic suites. Live suites remain optional and require private inputs where documented.
- Use the standalone Utility tile, Windows runner, or the solution's Test Explorer workflow project. Private workflow plans can remove the temporary instance after testing.
- This is an independent processor-test package release on GitHub; it does not publish or update a driver/library NuGet package.

- Add three optional read-only live hub tests, for 42 total cases (29 unit, 10 lifecycle, 3 live). Transfer private LiveTestSettings.json from the runner; the package contains no hub credentials.
- Use NUnit tooling 1.2.1 for shared processor reservations.

## 1.0.0 — 2026-09-14

- 29 offline tests and 10 SDK lifecycle tests, shared between desktop validation and the net472 processor package.
- Cover room discovery, stable child identity, renamed/removed rooms, cleared settings, overlapping connections and late refresh/login completion. Clearing or disposing the platform now removes its children and prevents old work from restoring them. Update WiserHeatAPIv2 to 1.1.0.6.
- Install the standalone test package from Configure’s **Utility** category. Select suites using its Home tile or the Windows NUnit runner.
- Processor test packages are GitHub release assets and are not published to NuGet. Private test inputs and deployment settings are excluded.