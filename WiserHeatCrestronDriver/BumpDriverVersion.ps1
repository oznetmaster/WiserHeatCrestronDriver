# Copyright © 2026 Neil Colvin.
# Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

param (
    [Parameter(Mandatory)]
    [string]$ManifestPath,
    [Parameter(Mandatory)]
    [string]$Configuration
)

$content = Get-Content $ManifestPath -Raw
$content = $content -replace '(?<="DriverVersion":\s*"\d+\.\d+\.\d+\.)(\d+)', {
    ([int]$_.Value + 1).ToString('D4')
}
$now = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff')
$content = $content -replace '(?<="VersionDate":\s*")[^"]+', $now
Set-Content -Path $ManifestPath -Value $content -NoNewline
Write-Host "DriverVersion build revision bumped ($Configuration) and VersionDate set to $now in $ManifestPath"
