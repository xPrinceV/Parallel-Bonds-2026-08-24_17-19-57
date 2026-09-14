param(
    [Parameter(Mandatory = $true)]
    [string]$Unity,
    [ValidateSet('Visual','Gui','DebugRun','Merge','Fusion','Cleanup','Regression','Timing','Transition')]
    [string[]]$Suites = @('Visual','Gui','DebugRun','Merge','Fusion','Cleanup','Regression','Timing','Transition'),
    [ValidateRange(120,180)][int]$TimeoutSeconds = 180
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$isolated = Join-Path $root 'Library/DebugRunValidationProject'
if (!(Test-Path "$isolated/Library") -or !(Test-Path "$isolated/Assets/Scenes/DebugRun.unity")) { throw 'Expected prewarmed isolated DebugRun project.' }
if (!(Test-Path $Unity)) { throw "Missing Unity binary: $Unity" }
# Never terminate or launch against the source editor. Refuse concurrent access to the isolated project.
$busy = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object { $_.CommandLine -like '*DebugRunValidationProject*' }
if ($busy) { throw 'Isolated Unity project is already open.' }
$helpers = @('DebugRunChecksBatch','FusionChecksBatch')
foreach ($name in $helpers) {
    $path = "$isolated/Assets/Game/Editor/${name}_Temporary.cs"
    if ((Test-Path $path) -or (Test-Path "$path.meta")) { throw "Existing temporary helper: $path" }
}
# Exact source copies only in the isolated project; retain its warm Library and unrelated assets/settings.
# Mirroring these bounded subtrees also removes stale production scripts and old helper imports.
foreach ($folder in @('Assets/Game','Assets/Scenes','Tools')) {
    if (!(Test-Path "$root/$folder")) { continue }
    & robocopy "$root/$folder" "$isolated/$folder" /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Exact copy failed: $folder, robocopy=$LASTEXITCODE" }
}
foreach ($asset in @('fusion.png','fusion.png.meta')) {
    Copy-Item "$root/Assets/Vampire Survival Assets/Art/$asset" "$isolated/Assets/Vampire Survival Assets/Art/$asset" -Force
}
New-Item -ItemType Directory -Force "$root/Logs" | Out-Null
$failed = 0
try {
    foreach ($name in $helpers) { Copy-Item "$PSScriptRoot/$name.cs" "$isolated/Assets/Game/Editor/${name}_Temporary.cs" }
    foreach ($suite in $Suites) {
        $log = "$root/Logs/DebugRunValidation-$suite.log"
        $method = if ($suite -in @('Visual','Gui','DebugRun')) { 'DebugRunChecksBatch.Run' } else { 'FusionChecksBatch.Run' }
        $arguments = '-batchmode -projectPath "{0}" -executeMethod {1} -fusionSuite {2} -debugRunSuite {2} -logFile "{3}"' -f $isolated,$method,$suite,$log
        if ($suite -eq 'Visual') {
            # Render in a real editor, not batch mode (which may not advance end-of-frame rendering).
            $arguments = $arguments.Replace('-batchmode ', '')
            Remove-Item "$isolated/Logs/FusionVisual-*.png" -Force -ErrorAction SilentlyContinue
        }
        $process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru
        if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
            Write-Output "$suite TIMEOUT at $TimeoutSeconds seconds; killed only child PID=$($process.Id)"
            $failed++
        } else {
            $process.Refresh()
            Write-Output "$suite exit=$($process.ExitCode); log=$log"
            if ($process.ExitCode -ne 0) { $failed++ }
        }
        if ($suite -eq 'Visual') {
            foreach ($image in Get-ChildItem "$isolated/Logs/FusionVisual-*.png" -ErrorAction SilentlyContinue) {
                $destination = Join-Path "$root/Logs" $image.Name
                Copy-Item $image.FullName $destination -Force
                if ([Convert]::ToBase64String([IO.File]::ReadAllBytes($image.FullName)) -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes($destination))) { throw "Capture copy mismatch: $destination" }
                Write-Output "Verified actual capture copy: $($image.FullName) -> $destination"
            }
        }
        if (Test-Path $log) {
            Select-String -Path $log -Pattern 'DEBUG_RUN |DEVELOPER_GUI |FUSION_BATCH|Checks FAIL|error CS|Exception' | ForEach-Object { $_.Line }
            $marker = if ($suite -in @('Visual','Gui','DebugRun')) { 'DEBUG_RUN RESULT' } else { 'FUSION_BATCH RESULT' }
            if (!(Select-String -Path $log -Pattern $marker -Quiet)) { Write-Output "Missing completion marker: $suite"; $failed++ }
            if (Select-String -Path $log -Pattern 'error CS\d+' -Quiet) { $failed++ }
        } else { Write-Output "Missing log: $suite"; $failed++ }
    }
} finally {
    foreach ($name in $helpers) {
        $path = "$isolated/Assets/Game/Editor/${name}_Temporary.cs"
        Remove-Item $path,"$path.meta" -Force -ErrorAction SilentlyContinue
    }
}
if ($failed) { exit 1 }
exit 0
