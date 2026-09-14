param(
    [Parameter(Mandatory = $true)][string]$Unity,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Revision = 'baseline',
    [ValidateSet('Material', 'Echo')][string[]]$EntryWorlds = @('Material', 'Echo'),
    [ValidateSet('Main', 'DebugRun')][string]$Scene = 'Main',
    [ValidateSet('Complete', 'Cancel', 'Disable')][string]$Termination = 'Complete',
    [ValidateSet('Center', 'RightEdge')][string]$Position = 'Center',
    [switch]$VisualNoClear,
    [switch]$CameraGeometry,
    [switch]$BeforeSnapshot
)
$ErrorActionPreference = 'Stop'
function Get-Sha256([string]$path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($path)
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'Library/DebugRunValidationProject'
$evidence = Join-Path $root 'Library/FusionMapRenderValidation'
if (!(Test-Path (Join-Path $project 'Library'))) { throw 'Expected the existing isolated warm Library.' }
if (!(Test-Path $Unity)) { throw 'Unity executable does not exist.' }
if (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'") { throw 'Unity is running; refusing to stage or launch.' }
New-Item -ItemType Directory -Force $evidence | Out-Null
$presentation = 'Assets/Game/Presentation/Worlds/FusionTransitionPresentation.cs'
$before = Join-Path $evidence 'FusionTransitionPresentation.before.cs'
if (!(Test-Path $before)) { Copy-Item (Join-Path $root $presentation) $before }
$selected = if ($BeforeSnapshot) { $before } else { Join-Path $root $presentation }
$camera = 'Assets/Game/Presentation/Camera/CameraController.cs'
$beforeCamera = Join-Path $evidence 'CameraController.before.cs'
if (!(Test-Path $beforeCamera)) { Copy-Item (Join-Path $root $camera) $beforeCamera }
$selectedCamera = if ($BeforeSnapshot) { $beforeCamera } else { Join-Path $root $camera }
# Synchronize only the two selected revisions; preserve frozen before snapshots and isolated
# optional editor integration exclusions. Never mirror or launch the source project.
Copy-Item $selected (Join-Path $project $presentation) -Force
Copy-Item $selectedCamera (Join-Path $project $camera) -Force
$critical = @(
    $presentation,
    $camera,
    'Assets/Game/Presentation/Worlds/WorldFlipPresentation.cs',
    'Assets/Game/Features/Worlds/FusionTransitionController.cs',
    'Assets/Game/Features/Worlds/WorldManager.cs',
    'Assets/Game/Features/Worlds/World.cs',
    'Assets/Game/Features/Worlds/WorldMap.cs',
    'Assets/Game/Features/GameFlow/RunStageController.cs',
    "Assets/Scenes/$Scene.unity"
)
$hashes = foreach ($path in $critical) {
    $source = Get-Sha256 (Join-Path $root $path)
    $isolated = Get-Sha256 (Join-Path $project $path)
    $expected = if ($path -eq $presentation) { Get-Sha256 $selected } elseif ($path -eq $camera) { Get-Sha256 $selectedCamera } else { $source }
    if ($isolated -ne $expected) { throw "Stale isolated production file: $path. Prepare the isolated copy explicitly before rerunning." }
    [ordered]@{ path=$path; source=$source; isolated=$isolated }
}
$helper = Join-Path $project 'Assets/Game/Editor/FusionMapRenderChecksBatch_Temporary.cs'
if ((Test-Path $helper) -or (Test-Path "$helper.meta")) { throw 'Temporary helper exists; refusing to overwrite.' }
Copy-Item (Join-Path $PSScriptRoot 'FusionMapRenderChecks.cs') (Join-Path $project 'Tools/FusionMapRenderChecks.cs') -Force
$failed = 0
try {
    Copy-Item (Join-Path $PSScriptRoot 'FusionMapRenderChecksBatch.cs') $helper
    foreach ($entry in $EntryWorlds) {
        if (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'") { throw 'Concurrent Unity detected; refusing to launch.' }
        $positionSuffix = if ($Position -eq 'Center') { '' } else { "-$Position" }
        $output = Join-Path $evidence "$Revision-$Scene-$entry-$Termination$positionSuffix"
        if (Test-Path $output) { throw "Preserve existing evidence; choose a new -Revision: $output" }
        New-Item -ItemType Directory $output | Out-Null
        $hashes | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'source-hashes.json')
        Copy-Item $selected (Join-Path $output 'FusionTransitionPresentation.cs')
        Copy-Item $selectedCamera (Join-Path $output 'CameraController.cs')
        @('FusionMapRenderChecks.cs', 'FusionMapRenderChecksBatch.cs') | ForEach-Object {
            [ordered]@{ path=$_; sha256=(Get-Sha256 (Join-Path $PSScriptRoot $_)) }
        } | ConvertTo-Json | Set-Content (Join-Path $output 'tool-hashes.json')
        $log = Join-Path $output 'Unity.log'
        # Not batchmode: WaitForEndOfFrame needs a visible, repainting native Game View.
        # Native graphics and an isolated editor; wait for coroutine completion before exiting.
        $arguments = '-projectPath "{0}" -executeMethod FusionMapRenderChecksBatch.Run -renderScene {1} -renderEntry {2} -renderTermination {3} -renderOutput "{4}" -logFile "{5}" -renderPosition {6} -renderVisualNoClear {7} -renderCameraGeometry {8}' -f $project, $Scene, $entry, $Termination, $output, $log, $Position, $VisualNoClear.IsPresent.ToString().ToLowerInvariant(), $CameraGeometry.IsPresent.ToString().ToLowerInvariant()
        $started = Get-Date
        $process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru
        $timedOut = -not $process.WaitForExit(180000)
        if ($timedOut) { & taskkill /PID $process.Id /T /F | Out-Null; $process.WaitForExit(); $failed++ }
        $process.Refresh()
        $row = [ordered]@{ entry=$entry; position=$Position; visualNoClear=$VisualNoClear.IsPresent; cameraGeometry=$CameraGeometry.IsPresent; pid=$process.Id; timeout=$timedOut; exitCode=$process.ExitCode; seconds=((Get-Date)-$started).TotalSeconds; log=$log }
        $row | ConvertTo-Json | Set-Content (Join-Path $output 'process.json')
        Write-Output ($row | ConvertTo-Json -Compress)
        if ($process.ExitCode -ne 0) { $failed++ }
        if (Test-Path $log) {
            Select-String -Path $log -Pattern 'FUSION_MAP_RENDER (RESULT|SUMMARY|FAIL|START|FIXTURE|REFERENCE)|error CS' | ForEach-Object { $_.Line }
            if (!(Select-String -Path $log -Pattern 'FUSION_MAP_RENDER RESULT' -Quiet)) { $failed++; Write-Output 'Missing completion marker' }
        } else { $failed++; Write-Output 'Missing Unity log' }
        if (!(Test-Path (Join-Path $output 'summary.json'))) { $failed++; Write-Output 'Missing pixel summary' }
    }
} finally {
    Remove-Item $helper, "$helper.meta" -Force -ErrorAction SilentlyContinue
    foreach ($row in $hashes) {
        $after = Get-Sha256 (Join-Path $root $row.path)
        if ($after -ne $row.source) { Write-Warning "Source changed externally during validation: $($row.path)"; $failed++ }
    }
}
if ($failed -gt 0) { exit 1 }
exit 0
