foreach ($project in @('WiserHeatCrestronDriver.AndroidTests', 'WiserHeatCrestronDriver.AndroidControlTests')) {
  dotnet test "$project/$project.csproj" -c Release --logger "trx;LogFileName=offline.trx" --results-directory "TestResults/$project"
  if ($LASTEXITCODE -ne 0) { throw 'Offline Android test validation failed.' }
  [xml]$result = Get-Content "TestResults/$project/offline.trx" -Raw
  $counts = $result.TestRun.ResultSummary.Counters
  $cases = @($result.SelectNodes("/*[local-name()='TestRun']/*[local-name()='Results']/*[local-name()='UnitTestResult']"))
  # NUnit's TRX writer can leave notExecuted at zero for skipped cases;
  # use each recorded outcome and require that none actually executed.
  if ($cases.Count -le 0 -or [int]$counts.total -ne $cases.Count -or [int]$counts.executed -ne 0 -or
      @($cases | Where-Object { $_.outcome -ne 'NotExecuted' }).Count -ne 0 -or [int]$counts.failed -ne 0 -or [int]$counts.passed -ne 0) {
    throw 'Every Android fixture must remain skipped when no processor workflow is active.'
  }
}
