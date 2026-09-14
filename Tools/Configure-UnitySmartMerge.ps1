param(
    [Parameter(Mandatory = $true)]
    [string]$Unity,
    [switch]$ReplaceExisting
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$editor = (Resolve-Path $Unity).Path
$tool = Join-Path (Split-Path $editor -Parent) 'Data/Tools/UnityYAMLMerge.exe'
if (!(Test-Path $tool)) { throw "UnityYAMLMerge not found: $tool" }
$tool = $tool.Replace('\', '/')
if ($tool.Contains("'")) { throw 'Use a Unity installation path without single quotes.' }
# Single quotes survive Windows PowerShell native argument passing and protect Git shell paths.
$driver = "'{0}' merge -h -p --fallback none --force '%O' '%B' '%A' '%A'" -f $tool
$existing = & git -C $root config --local --get merge.unityyamlmerge.driver
if ($LASTEXITCODE -notin @(0, 1)) { throw 'Cannot read repository merge configuration.' }
if ($existing -and $existing -ne $driver -and !$ReplaceExisting) {
    throw 'A different merge driver is configured. Review it before using -ReplaceExisting.'
}
# Keep machine-specific paths in .git/config, never in versioned project settings.
& git -C $root config --local merge.unityyamlmerge.name 'Unity Smart Merge'
if ($LASTEXITCODE -ne 0) { throw 'Cannot configure merge driver name.' }
& git -C $root config --local merge.unityyamlmerge.driver $driver
if ($LASTEXITCODE -ne 0) { throw 'Cannot configure merge driver command.' }
$stored = & git -C $root config --local --get merge.unityyamlmerge.driver
if ($LASTEXITCODE -ne 0 -or $stored -cne $driver) { throw 'Merge driver command did not round-trip correctly.' }
& git -C $root check-attr merge -- Assets/Scenes/Main.unity
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify scene merge attributes.' }
Write-Output "Configured repository-local Unity Smart Merge: $tool"
Write-Output 'No merge was started. Conflicts still require review; no fallback editor is launched.'
