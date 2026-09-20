# Packaged driver startup check

This offline check opens the actual Wiser `.pkg`, loads its merged assembly, calls the entry point named by its metadata, and verifies that an unconfigured gateway initializes offline. It does not connect to a hub or processor.

The desktop SDK requires the same privately provisioned `CompactJsonPath` dependency as the lifecycle harness. CI already supplies it. Do not commit that dependency or a machine-specific path.

```powershell
dotnet run --project tools/PackageSmokeTest/PackageSmokeTest.csproj -c Release -- path/to/driver.pkg
```

The test workflow builds the driver with an alternate assembly name and runs this check. That catches assembly-name-dependent resource lookup failures that source-level tests and successful ManifestUtil packaging can miss. The check prints the package hash and version it exercised. It does not establish processor compatibility, embedded-help correctness or live operation; those remain separate gates for the final package.
