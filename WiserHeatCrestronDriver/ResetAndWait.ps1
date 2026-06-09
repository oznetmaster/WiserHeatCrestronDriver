# Copyright © 2026 Neil Colvin.
# Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

<#
.SYNOPSIS
	Resets the Wiser driver on the Crestron processor and waits until it has
	fully initialised (i.e. "Discovery complete" appears in the day log).

.USAGE
	.\ResetAndWait.ps1 -ProcessorIP 192.168.8.241 -User admin -Password secret
#>
param(
	[Parameter(Mandatory)][string] $ProcessorIP,
	[Parameter(Mandatory)][string] $User,
	[Parameter(Mandatory)][string] $Password,
	[int]    $TimeoutSeconds = 120,
	[int]    $PollIntervalSeconds = 5,
	[string] $ReadyMarker = "Discovery complete"
)

Import-Module Posh-SSH -ErrorAction Stop

$cred      = [System.Management.Automation.PSCredential]::new(
				 $User, (ConvertTo-SecureString $Password -AsPlainText -Force))
$logDir    = "/rm/SeawolfDiagnostic"
$localTemp = "$env:TEMP\wiser_driver.log"

Write-Host "Connecting SSH to $ProcessorIP ..."
$ssh = New-SSHSession -ComputerName $ProcessorIP -Credential $cred -AcceptKey -ErrorAction Stop
try {
	Write-Host "Sending: enableprogramcmd"
	Invoke-SSHCommand -SessionId $ssh.SessionId -Command "enableprogramcmd" | Out-Null
	Start-Sleep -Seconds 1
	Write-Host "Sending: progreset -p:0"
	Invoke-SSHCommand -SessionId $ssh.SessionId -Command "progreset -p:0"  | Out-Null
	Write-Host "Reset commands sent. Waiting for driver to initialise ..."
}
finally {
	Remove-SSHSession -SessionId $ssh.SessionId | Out-Null
}

$sftp = New-SFTPSession -ComputerName $ProcessorIP -Credential $cred -AcceptKey -ErrorAction Stop
try {
	$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
	$found    = $false

	while ((Get-Date) -lt $deadline) {
		Start-Sleep -Seconds $PollIntervalSeconds
		$logFile = "$logDir/$((Get-Date -Format 'yyyy-MM-dd')).log"

		try {
			Get-SFTPItem -SessionId $sftp.SessionId `
						 -Path $logFile `
						 -Destination $env:TEMP `
						 -Force -ErrorAction Stop

			$downloaded = Join-Path $env:TEMP (Split-Path $logFile -Leaf)
			if (Test-Path $downloaded) {
				Move-Item $downloaded $localTemp -Force
			}

			if (Select-String -Path $localTemp -Pattern ([regex]::Escape($ReadyMarker)) -Quiet) {
				$found = $true
				break
			}

			Write-Host "  $(Get-Date -Format 'HH:mm:ss')  waiting for '$ReadyMarker' ..."
		}
		catch {
			Write-Host "  $(Get-Date -Format 'HH:mm:ss')  log not yet available ($_)"
		}
	}

	if ($found) {
		Write-Host ""
		Write-Host "Driver ready. Fetching last 40 lines of log ..."
		Write-Host "────────────────────────────────────────────────"
		Get-Content $localTemp | Select-Object -Last 40
		Write-Host "────────────────────────────────────────────────"
	}
	else {
		Write-Warning "Timed out after $TimeoutSeconds seconds waiting for '$ReadyMarker'."
		Write-Host "Last 20 lines of log (if available):"
		if (Test-Path $localTemp) { Get-Content $localTemp | Select-Object -Last 20 }
	}
}
finally {
	Remove-SFTPSession -SessionId $sftp.SessionId | Out-Null
}
