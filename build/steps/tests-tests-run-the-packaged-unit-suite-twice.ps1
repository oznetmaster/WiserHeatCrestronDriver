$output = Join-Path $PWD 'WiserHeatCrestronDriver.ProcessorTests/bin/Release/net472'
$extracted = Join-Path $env:RUNNER_TEMP 'driver-package-tests'
[IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $output 'WiserHeatCrestronDriver.ProcessorTests.pkg'), $extracted)
# Compare against the project actually embedded in the package. The desktop
# harness also includes a manifest-resource test which cannot run in the host.
./tools/Test-DiscoveredCoverage.ps1 -Stage Package -SdkRoot ../CrestronHomeNUnit -PackageAssembly (Join-Path $extracted 'WiserHeatCrestronDriver.ProcessorTests.dll') -SourceInventory TestResults/WiserHeatCrestronDriver.Tests/inventory.json -ResultsDirectory TestResults
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
