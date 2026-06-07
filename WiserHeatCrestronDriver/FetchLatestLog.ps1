param(
	[string] $ProjectUserFile = "$PSScriptRoot\WiserHeatCrestronDriver.csproj.user",
	[string] $OutputDirectory = "C:\Temp"
)

$ErrorActionPreference = "Stop"

if (!(Test-Path $ProjectUserFile)) {
	throw "Project user file not found: $ProjectUserFile"
}

[xml] $x = Get-Content $ProjectUserFile
$ip = $x.Project.PropertyGroup.CrestronHomeIP
$user = $x.Project.PropertyGroup.CrestronHomeFtpUser
$pw = $x.Project.PropertyGroup.CrestronHomeSftpPassword

if ([string]::IsNullOrWhiteSpace($ip) -or [string]::IsNullOrWhiteSpace($user) -or [string]::IsNullOrWhiteSpace($pw)) {
	throw "Missing CrestronHomeIP / CrestronHomeFtpUser / CrestronHomeSftpPassword in $ProjectUserFile"
}

Import-Module Posh-SSH -ErrorAction Stop

$sec = ConvertTo-SecureString $pw -AsPlainText -Force
$cred = [System.Management.Automation.PSCredential]::new($user, $sec)

if (!(Test-Path $OutputDirectory)) {
	New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

$sftp = New-SFTPSession -ComputerName $ip -Credential $cred -AcceptKey -ErrorAction Stop
try {
	$remote = "/rm/SeawolfDiagnostic/$((Get-Date -Format 'yyyy-MM-dd')).log"
	$local = Join-Path $OutputDirectory ("build-fresh-{0}.log" -f (Get-Date -Format "HHmmss"))

	Get-SFTPItem -SessionId $sftp.SessionId -Path $remote -Destination $OutputDirectory -Force -ErrorAction Stop
	$downloaded = Join-Path $OutputDirectory (Split-Path $remote -Leaf)
	Move-Item $downloaded $local -Force

	Get-Item $local | Select-Object FullName, Length, LastWriteTime | Format-List
}
finally {
	Remove-SFTPSession -SessionId $sftp.SessionId | Out-Null
}
