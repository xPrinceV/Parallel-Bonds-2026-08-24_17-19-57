param(
    [Parameter(Mandatory = $true)][string]$Unity,
    [ValidateSet('Main', 'DebugRun')][string[]]$Scenes = @('Main', 'DebugRun'),
    [ValidateSet('Checks', 'Live', 'Rollback', 'CleanupFailure', 'BossFailure', 'Regression', 'Finale')]
    [string[]]$Suites = @('Checks', 'Live', 'Rollback', 'CleanupFailure', 'BossFailure', 'Regression'),
    [switch]$PrepareOnly,
    [switch]$SkipSync,
    [switch]$Rendered,
    [ValidatePattern('^[a-z0-9][a-z0-9.-]+$')][string[]]$ExcludedPackages = @(),
    [ValidatePattern('^Assets/.+\.cs$')][string[]]$ExcludedEditorScripts = @()
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'Library/DebugRunValidationProject'
$evidence = Join-Path $root 'Library/MapMergeCompatibility'
if (-not (Test-Path (Join-Path $project 'Library'))) { throw 'Expected existing isolated warm Library.' }
if (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'") { throw 'Unity is running; refusing to synchronize or launch.' }
New-Item -ItemType Directory -Force $evidence | Out-Null
function Mirror([string]$name) {
    # Only explicit source trees; never mirror the project root or its Library.
    & robocopy (Join-Path $root $name) (Join-Path $project $name) /MIR /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed for $name : $LASTEXITCODE" }
}
if (-not $SkipSync) {
    Mirror 'Assets'
    Mirror 'ProjectSettings'
    Mirror 'Tools'
    Mirror 'Packages'
    # Optional editor integrations can be excluded explicitly in the disposable copy only.
    foreach ($package in $ExcludedPackages) {
        Remove-Item (Join-Path $project "Packages/$package") -Recurse -Force -ErrorAction SilentlyContinue
        foreach ($name in @('manifest.json', 'packages-lock.json')) {
            $path = Join-Path $project "Packages/$name"
            $json = Get-Content $path -Raw | ConvertFrom-Json
            $json.dependencies.PSObject.Properties.Remove($package)
            [IO.File]::WriteAllText($path, ($json | ConvertTo-Json -Depth 100) + "`n")
        }
    }
}
foreach ($script in $ExcludedEditorScripts) {
    $path = [IO.Path]::GetFullPath((Join-Path $project $script))
    $assets = [IO.Path]::GetFullPath((Join-Path $project 'Assets')) + [IO.Path]::DirectorySeparatorChar
    if (!$path.StartsWith($assets, [StringComparison]::OrdinalIgnoreCase)) { throw 'Excluded script must remain inside isolated Assets.' }
    Remove-Item $path, "$path.meta" -Force -ErrorAction SilentlyContinue
}
if ($PrepareOnly) { Write-Output "Prepared $project; retained warm Library; source project unchanged."; exit 0 }
$helper = Join-Path $project 'Assets/Game/Editor/WorldMapChecksBatch_Temporary.cs'
if ((Test-Path $helper) -or (Test-Path "$helper.meta")) { throw 'Temporary helper exists; refusing to overwrite.' }
$failed = 0
try {
    Copy-Item (Join-Path $PSScriptRoot 'WorldMapChecksBatch.cs') $helper
    foreach ($scene in $Scenes) {
        foreach ($suite in $Suites) {
            $log = Join-Path $evidence "$scene-$suite.log"
            if (Test-Path $log) { throw "Preserve existing evidence before rerunning: $log" }
            $mode = if ($Rendered) { '' } else { '-batchmode' }
            $arguments = '{4} -projectPath "{0}" -executeMethod WorldMapChecksBatch.Run -mapScene {1} -mapSuite {2} -logFile "{3}"' -f $project, $scene, $suite, $log, $mode
            $started = Get-Date
            $process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru
            $timedOut = -not $process.WaitForExit(180000)
            if ($timedOut) {
                & taskkill /PID $process.Id /T /F | Out-Null
                $process.WaitForExit()
                $failed++
            }
            $process.Refresh()
            $row = [ordered]@{ scene=$scene; suite=$suite; pid=$process.Id; timeout=$timedOut; exitCode=$process.ExitCode; seconds=((Get-Date)-$started).TotalSeconds; log=$log }
            $row | ConvertTo-Json -Compress | Add-Content (Join-Path $evidence 'processes.jsonl')
            Write-Output ($row | ConvertTo-Json -Compress)
            if ($process.ExitCode -ne 0) { $failed++ }
            if (Test-Path $log) {
                Select-String -Path $log -Pattern 'MAP_BATCH|FAIL|error CS|Exception' | ForEach-Object { $_.Line }
                if (-not (Select-String -Path $log -Pattern 'MAP_BATCH RESULT' -Quiet)) { $failed++; Write-Output 'Missing completion marker' }
            } else { $failed++; Write-Output 'Missing Unity log' }
        }
    }
} finally {
    Remove-Item $helper, "$helper.meta" -Force -ErrorAction SilentlyContinue
}
if ($failed -gt 0) { exit 1 }
exit 0
