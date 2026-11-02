$frameworkReferences = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2'
$frameworkRuntime = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
dotnet build WiserHeatCrestronDriver/WiserHeatCrestronDriver.csproj -c Release -p:DeployAfterBuild=false -p:AssemblyName=NeilColvin_Thermostat_WiserHeat_IP_V2 "-p:ManifestUtilExe=$env:MANIFEST_UTIL_EXE" "-p:LocalNet472ReferenceAssemblies=$frameworkReferences" "-p:LocalNetFxRuntimeDir=$frameworkRuntime" -p:MergeDependenciesContinueOnError=false -p:PackageDriverContinueOnError=false -p:PackageDriverIgnoreExitCode=false
if ($LASTEXITCODE -ne 0) { throw 'Renamed driver package build failed.' }
dotnet run --project tools/PackageSmokeTest/PackageSmokeTest.csproj -c Release -- WiserHeatCrestronDriver/bin/Release/net472/NeilColvin_Thermostat_WiserHeat_IP_V2.pkg
if ($LASTEXITCODE -ne 0) { throw 'Packaged entry point could not initialize.' }
