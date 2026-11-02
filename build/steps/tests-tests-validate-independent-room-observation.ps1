dotnet test WiserHeatCrestronDriver.ControlProbe.Tests/WiserHeatCrestronDriver.ControlProbe.Tests.csproj -c Release --logger "trx;LogFileName=control-probe.trx" --results-directory TestResults/ControlProbe
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
