param(
	[Parameter(Mandatory)][string] $AssemblyPath,
	[string] $OutputPath = ""
)

if (-not $OutputPath) {
	$dir  = [System.IO.Path]::GetDirectoryName($AssemblyPath)
	$stem = [System.IO.Path]::GetFileNameWithoutExtension($AssemblyPath)
	$OutputPath = [System.IO.Path]::Combine($dir, $stem + "_patched.dll")
}

$cecilPath = Get-ChildItem "$env:USERPROFILE\.dotnet\tools\.store\dotnet-ilrepack" `
	-Recurse -Filter "Mono.Cecil.dll" -ErrorAction SilentlyContinue |
	Select-Object -First 1 -ExpandProperty FullName

if (-not $cecilPath) {
	Write-Warning "PatchMergedAssembly: Mono.Cecil.dll not found - skipping patch."
	exit 0
}

[System.Reflection.Assembly]::LoadFrom($cecilPath) | Out-Null

function ShouldRename([Mono.Cecil.TypeDefinition]$td) {
	return ($td.Namespace -eq 'System' -or $td.Namespace.StartsWith('System.'))
}

$asmBytes  = [System.IO.File]::ReadAllBytes($AssemblyPath)
$asmStream = [System.IO.MemoryStream]::new($asmBytes)
$rp        = [Mono.Cecil.ReaderParameters]::new()
$asmDef    = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($asmStream, $rp)
$module    = $asmDef.MainModule
$count     = 0

foreach ($td in $module.Types) {
	if (ShouldRename $td) {
		$oldName = $td.FullName
		$td.Namespace = '_Stripped.' + $td.Namespace
		$count++
		Write-Host "  Renamed: $oldName -> $($td.FullName)"
	}
}

$outDir = [System.IO.Path]::GetDirectoryName($OutputPath)
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$tempPath = $OutputPath + ".tmp"
try {
	$fs = [System.IO.File]::Open($tempPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
	try   { $asmDef.Write($fs) }
	finally { $fs.Dispose() }
	$asmDef.Dispose()
	[System.IO.File]::Copy($tempPath, $OutputPath, $true)
	Remove-Item $tempPath -Force
	Write-Host "PatchMergedAssembly: $count type(s) renamed -> $OutputPath"
	exit 0
} catch {
	$asmDef.Dispose()
	if (Test-Path $tempPath) { Remove-Item $tempPath -Force }
	Write-Error "PatchMergedAssembly: Write failed - $_"
	exit 1
}
