param(
	[string] $LogDirectory = "C:\Temp",
	[string] $TimeWindowRegex = 'L:01 \[[0-9]{2}:[0-9]{2}:[0-9]{2}\]:',
	[string] $RebootStartRegex = 'Program started\.|Power: Complete adding Rebootable Device "CP4-R"|Power: Initialized Rebootable Device "CP4-R"',
	[string] $SignalRegex = 'Wiser Heat|Drayton Wiser|Connect task started|Connected to Wiser API|Wiser connection failed|RefreshRoomsAsync|Discovered [0-9]+ total rooms|Evaluating room:|Queued room:|Updated room:|Discovery complete|ApplyConfigurationItems|platform:managedDevices|Connected - no rooms discovered|Connected - rooms discovered:|Configuration incomplete|Offline|error|exception|failed|fatal',
	[int] $TailLines = 6000,
	[int] $ContextBefore = 40,
	[int] $ContextAfter = 40
)

$ErrorActionPreference = "Stop"

$latest = Get-ChildItem $LogDirectory -Filter "build-fresh-*.log" |
	Sort-Object LastWriteTime -Descending |
	Select-Object -First 1

if ($null -eq $latest) {
	throw "No build-fresh-*.log files found in $LogDirectory"
}

Write-Host "Using log: $($latest.FullName)"

$allLines = Get-Content $latest.FullName
$rebootMatches = $allLines | Select-String -Pattern $RebootStartRegex

if ($rebootMatches.Count -gt 0) {
	$windowStartLine = $rebootMatches[-1].LineNumber
}
else {
	$windowStartLine = [Math]::Max(1, $allLines.Count - $TailLines + 1)
}

$windowLines = $allLines[($windowStartLine - 1)..($allLines.Count - 1)]

Write-Host ""
Write-Host "=== Focused Markers (latest reboot window starting at line $windowStartLine) ==="
$focused = $windowLines |
	Select-String -Pattern $TimeWindowRegex |
	Select-String -Pattern $SignalRegex

foreach ($m in $focused) {
	$m.Line
}

Write-Host ""
Write-Host "=== Connection Context Blocks ==="
$anchors = @(
	'Connect task started',
	'Connected to Wiser API',
	'Wiser connection failed',
	'Discovery complete'
)

foreach ($anchor in $anchors) {
	$matches = $windowLines | Select-String -Pattern $anchor
	foreach ($match in $matches) {
		$actualLineNumber = $windowStartLine + $match.LineNumber - 1
		$start = [Math]::Max(1, $match.LineNumber - $ContextBefore)
		$end = [Math]::Min($windowLines.Count, $match.LineNumber + $ContextAfter)
		Write-Host ""
		Write-Host ("--- Anchor: {0} @ line {1} ---" -f $anchor, $actualLineNumber)
		for ($i = $start; $i -le $end; $i++) {
			$line = $windowLines[$i - 1]
			if ($line -match $TimeWindowRegex) {
				Write-Host $line
			}
		}
	}
}
