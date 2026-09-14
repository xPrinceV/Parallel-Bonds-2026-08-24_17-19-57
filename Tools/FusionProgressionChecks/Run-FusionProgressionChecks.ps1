param(
    [ValidateSet('Main','DebugRun')][string]$Scene = 'Main',
    [switch]$PrepareOnly,
    [switch]$ParentReady,
    [switch]$ExcludeWebBuildProfiles
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)).TrimEnd('\','/')
$project = Join-Path $root 'Library/DebugRunValidationProject'
$unity = 'E:/Unity/Editor/6000.4.6f1/Editor/Unity.exe'
$output = Join-Path $project 'Evidence/FusionProgressionChecks'
$stage = Join-Path $project 'Assets/Game/Editor/FusionProgressionChecks_Temporary'
if (!(Test-Path $unity -PathType Leaf) -or !(Test-Path (Join-Path $project 'Library/Bee/artifacts'))) { throw 'Exact Unity binary and warmed isolated project required.' }
if (!$PrepareOnly -and !$ParentReady) { throw 'Parent readiness confirmation required. Use -PrepareOnly while APIs are pending.' }
function IsolatedProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
        $match = [regex]::Match([string]$_.CommandLine, '(?i)"?-projectPath"?\s+(?:"([^"]+)"|(\S+))')
        if ($match.Success) {
            $path = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
            [IO.Path]::GetFullPath($path).TrimEnd('\','/') -eq [IO.Path]::GetFullPath($project)
        }
    })
}
function Idle { if ((IsolatedProcesses).Count) { throw 'Isolated editor busy. No editor/process will be killed.' } }
function Hash([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create(); $stream = [IO.File]::OpenRead($path)
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function Snapshot {
    @(foreach ($name in @('Assets','ProjectSettings','Packages','Data','.vscode','Tools')) {
        $path = Join-Path $root $name
        if (Test-Path $path) {
            foreach ($file in Get-ChildItem -LiteralPath $path -Recurse -File) {
                [ordered]@{ path=$file.FullName.Substring($root.Length + 1).Replace('\','/'); sha256=(Hash $file.FullName); attributes=[string]$file.Attributes }
            }
        }
    }
    foreach ($file in Get-ChildItem -LiteralPath $root -File) {
        [ordered]@{ path=$file.Name; sha256=(Hash $file.FullName); attributes=[string]$file.Attributes }
    })
}
function Compile {
    $data = Join-Path (Split-Path $unity -Parent) 'Data'
    foreach ($kind in @('runtime','editor')) {
        $assembly = if ($kind -eq 'runtime') { 'Assembly-CSharp' } else { 'Assembly-CSharp-Editor' }
        $warm = Get-ChildItem (Join-Path $project 'Library/Bee/artifacts') -Filter "$assembly.rsp" -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (!$warm) { throw "Missing warmed reference response: $assembly" }
        $options = @(Get-Content $warm.FullName | Where-Object { $_ -match '^-(define:|r:|unsafe|langversion:|nostdlib)' } | ForEach-Object {
            if ($_ -match '^-r:"([^"]+)"$') {
                $reference = $Matches[1]
                if ($reference -match '[/\\]Assembly-CSharp(?:\.ref)?\.dll$') { $reference = Join-Path $attempt 'Assembly-CSharp.dll' }
                elseif (![IO.Path]::IsPathRooted($reference)) { $reference = Join-Path $project $reference }
                if (!(Test-Path $reference)) { throw "Missing warm reference: $reference" }
                '-r:"' + $reference + '"'
            } else { $_ }
        })
        $sources = @(Get-ChildItem (Join-Path $root 'Assets') -Recurse -File -Filter '*.cs' | Where-Object {
            ($kind -eq 'editor') -eq ($_.FullName -match '[\\/]Editor[\\/]')
        } | ForEach-Object { '"' + $_.FullName + '"' })
        if ($kind -eq 'editor') { $sources += $tools | ForEach-Object { '"' + (Join-Path $PSScriptRoot $_) + '"' } }
        $rsp = Join-Path $attempt "offline-$kind.rsp"
        [IO.File]::WriteAllLines($rsp, @('-target:library', ('-out:"' + (Join-Path $attempt "$assembly.dll") + '"')) + $options + $sources)
        & (Join-Path $data 'NetCoreRuntime/dotnet.exe') (Join-Path $data 'DotNetSdkRoslyn/csc.dll') /noconfig ("@$rsp") 2>&1 | Out-File (Join-Path $attempt "offline-$kind.log")
        Write-Output "Offline $kind exit=$LASTEXITCODE sources=$($sources.Count)"
        if ($LASTEXITCODE -ne 0) { throw "Offline $kind failed; no native launch consumed. Inspect raw compiler log." }
    }
}
Idle
New-Item -ItemType Directory -Force $output | Out-Null
$lock = [IO.File]::Open((Join-Path $output 'runner.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
$sequence = 1
do { $attempt = Join-Path $output ('attempt-{0:d2}' -f $sequence); $sequence++ } while (Test-Path $attempt)
New-Item -ItemType Directory $attempt | Out-Null
$tools = @('FusionProgressionChecks.cs','FusionProgressionChecksBatch.cs')
$before = @(); $copies = @(); $process = $null; $stageOwned = $false; $failed = 0
try {
    if ((Test-Path $stage) -or (Test-Path "$stage.meta")) { throw 'Existing owned stage; refusing overwrite.' }
    if (!$PrepareOnly -and @(Get-ChildItem $output -Filter 'launch-intent.json' -Recurse -File).Count -ge 2) { throw 'Own two-launch budget exhausted; no automatic retry.' }
    $before = Snapshot
    $before | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $attempt 'source-before.json')
    [ordered]@{ source=$root; project=$project; unity=$unity; scene=$Scene; prepareOnly=[bool]$PrepareOnly; parentReady=[bool]$ParentReady; utc=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content (Join-Path $attempt 'context.json')
    New-Item -ItemType Directory (Join-Path $attempt 'executed-tools') | Out-Null
    Get-ChildItem -LiteralPath $PSScriptRoot -File | Copy-Item -Destination (Join-Path $attempt 'executed-tools')
    if ($PrepareOnly) { Write-Output 'Prepared tool snapshot and source hashes only. No offline compilation, mirror, stage, or Unity launch while parent APIs are pending.' }
    else {
        Compile
        Idle
        $foreign = @(Get-ChildItem (Join-Path $project 'Assets') -Directory -Recurse | Where-Object { $_.Name -like '*_Temporary' })
        if ($foreign.Count) { throw 'Foreign temporary validation stages present; refuse mirror rather than delete another task work.' }
        foreach ($name in @('Assets','ProjectSettings','Packages')) {
            & robocopy (Join-Path $root $name) (Join-Path $project $name) /MIR /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "Mirror failed: $name ($LASTEXITCODE)" }
        }

        $copies = @(foreach ($row in $before | Where-Object { $_.path -match '^(Assets|ProjectSettings|Packages)/' }) {
            $hash = Hash (Join-Path $project $row.path)
            if ($hash -ne $row.sha256) { throw "Copy/source snapshot mismatch (possibly concurrent parent edit): $($row.path)" }
            [ordered]@{ path=$row.path; sha256=$hash }
        })
        $copies | ConvertTo-Json | Set-Content (Join-Path $attempt 'isolated-copy-hashes.json')
        $exclusions = @('Offline warm references are a preflight only, not current package-resolution evidence', 'Ordinary spawning suppressed with public APIs in Play Mode only; components stay enabled for finale validation; no combat balance or full540 elapsed-time claim')
        if ($ExcludeWebBuildProfiles) {
            $logs = @(Get-ChildItem (Join-Path $project 'Evidence') -Filter 'Unity.log' -Recurse -File | Where-Object { [IO.File]::ReadAllText($_.FullName) -match '(?i)(buildtarget.*(exception|fail)|build profile.*(exception|fail)|Native extension for WebGL target not found)' })
            if (!$logs.Count) { throw 'No recorded native WebGL/build-target failure; refusing profile exclusion.' }
            $logs.FullName | Set-Content (Join-Path $attempt 'prior-build-target-failure-logs.txt')
            foreach ($name in @('Build Profiles','Build Profiles.meta')) {
                $path = Join-Path $project "Assets/Settings/$name"
                if (Test-Path $path) { Move-Item -LiteralPath $path -Destination (Join-Path $attempt "excluded-$name") }
            }
            $exclusions += 'Isolated Web Build Profiles excluded after recorded native target failure; source remains unchanged'
        }
        $packages = @(foreach ($name in @('manifest.json','packages-lock.json')) { [ordered]@{ path="Packages/$name"; source=(Hash (Join-Path $root "Packages/$name")); isolated=(Hash (Join-Path $project "Packages/$name")) } })
        [ordered]@{ exclusions=$exclusions; packages=$packages } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $attempt 'validation-exclusions.json')
        Idle
        New-Item -ItemType Directory $stage | Out-Null; $stageOwned = $true
        foreach ($name in $tools) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage }
        $log = Join-Path $attempt 'Unity.log'
        $arguments = '-projectPath "{0}" -executeMethod FusionProgressionChecksBatch.Run -fusionSource "{1}" -fusionProject "{0}" -fusionOutput "{2}" -fusionScene {3} -logFile "{4}"' -f $project,$root,$attempt,$Scene,$log
        [ordered]@{ boundSeconds=270; arguments=$arguments; utc=[DateTime]::UtcNow.ToString('o'); note='Own budget max2; graphical Play Mode; no source launch/save; no automatic retry or process kill' } | ConvertTo-Json | Set-Content (Join-Path $attempt 'launch-intent.json')
        $started = Get-Date; $process = Start-Process -FilePath $unity -ArgumentList $arguments -PassThru
        $timeout = !$process.WaitForExit(270000)
        $exitCode = if ($timeout) { $null } else { $process.Refresh(); $process.ExitCode }
        [ordered]@{ pid=$process.Id; timeout=$timeout; exitCode=$exitCode; seconds=((Get-Date)-$started).TotalSeconds; noProcessKilled=$true } | ConvertTo-Json | Set-Content (Join-Path $attempt 'process.json')
        if (Test-Path $log) {
            $text = [IO.File]::ReadAllText($log)
            $search = [regex]::Matches($text, 'UnityEditor\.Search\.SearchDatabase').Count
            $errors = [regex]::Matches($text, '(?im)^.*(?:error CS\d+|\b\w*Exception:|referenced script.*missing|missing script:|could not be loaded|failed to import|error importing|\berror\b|assertion failed)[^\r\n]*')
            $errors.Value | Set-Content (Join-Path $attempt 'raw-error-lines.txt')
            [ordered]@{ searchDatabaseStackOccurrences=$search; rawErrorLines=$errors.Count; cleanConsole=($search + $errors.Count -eq 0); note='Startup included; SearchDatabase is NOT a clean pass. Full raw Unity.log retained.' } | ConvertTo-Json | Set-Content (Join-Path $attempt 'log-audit.json')
            if ($search + $errors.Count -gt 0) { $failed++ }
            Write-Output "Raw audit: SearchDatabase=$search error-lines=$($errors.Count)"
        } else { $failed++ }
        if ($timeout) { throw '270-second native timeout. No kill/retry; leave process and stage untouched. Partial evidence only.' }
        if ($exitCode -ne 0) { $failed++ }
        $summaryPath = Join-Path $attempt 'summary.json'
        if (!(Test-Path $summaryPath)) { throw 'Native summary missing; inspect raw Unity.log.' }
        $summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
        if ($summary.code -ne 0 -or @($summary.completedScenarios).Count -ne 3) { $failed++ }
        Write-Output "Native $Scene code=$($summary.code) passed=$($summary.passed) failed=$($summary.failed) completed=$(@($summary.completedScenarios).Count)/3"
    }
} catch {
    $failed++; $_ | Out-String | Set-Content (Join-Path $attempt 'blocked-or-failed.txt'); Write-Warning $_.Exception.Message
} finally {
    if ($stageOwned -and !(IsolatedProcesses).Count) { Remove-Item -LiteralPath $stage, "$stage.meta" -Recurse -Force -ErrorAction SilentlyContinue }
    $after = Snapshot; $after | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $attempt 'source-after.json')
    $lookup = @{}; foreach ($row in $after) { $lookup[$row.path] = $row }
    $oldPaths = @{}; foreach ($row in $before) { $oldPaths[$row.path] = $true }
    $drift = @(foreach ($row in $before) { $current = $lookup[$row.path]; if (!$current -or $row.sha256 -ne $current.sha256 -or $row.attributes -ne $current.attributes) { $row.path } })
    $added = @($after | Where-Object { !$oldPaths.ContainsKey($_.path) } | ForEach-Object { $_.path })
    if ($before.Count -and ($drift.Count -or $added.Count)) { $failed++; Write-Warning 'Source drift recorded (possibly concurrent parent/editor work). Never reverted.' }
    $copyDrift = @(foreach ($row in $copies) { $path = Join-Path $project $row.path; if (!(Test-Path $path) -or (Hash $path) -ne $row.sha256) { $row.path } })
    [ordered]@{ failureConditions=$failed; prepareOnly=[bool]$PrepareOnly; nativeStarted=($null -ne $process); sourceFiles=$before.Count; sourceDrift=$drift; sourceNewFiles=$added; isolatedChangedFiles=$copyDrift; temporaryStageRemoved=(!(Test-Path $stage)); isolatedUnityRemaining=@(IsolatedProcesses).Count; noProcessKilled=$true } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $attempt 'runner-result.json')
    Write-Output "Evidence: $attempt"; $lock.Dispose()
}
if ($failed -gt 0) { exit 1 }
exit 0
