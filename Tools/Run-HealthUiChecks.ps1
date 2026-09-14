param(
    [Parameter(Mandatory = $true)][string]$Unity,
    [Parameter(Mandatory = $true)][string]$ValidationProject,
    [ValidateSet('Main','DebugRun')][string]$Scene = 'Main',
    [switch]$PrepareOnly,
    [switch]$ExcludeWebBuildProfiles
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\','/')
$project = [IO.Path]::GetFullPath($ValidationProject).TrimEnd('\','/')
$allowed = [IO.Path]::GetFullPath((Join-Path $root 'Library/DebugRunValidationProject'))
$Unity = [IO.Path]::GetFullPath($Unity)
if ($project -ne $allowed -or !(Test-Path -LiteralPath $Unity -PathType Leaf) -or !(Test-Path (Join-Path $project 'Library/Bee/artifacts'))) {
    throw 'Require exact existing warmed Library/DebugRunValidationProject and Unity binary.'
}
function IsolatedProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
        $match = [regex]::Match([string]$_.CommandLine, '(?i)"?-projectPath"?\s+(?:"([^"]+)"|(\S+))')
        if ($match.Success) {
            $path = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
            [IO.Path]::GetFullPath($path).TrimEnd('\','/') -eq $project
        }
    })
}
function Idle { if ((IsolatedProcesses).Count) { throw 'Isolated editor busy; never kill it or touch source editor.' } }
function Hash([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create(); $stream = [IO.File]::OpenRead($path)
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function SourceFiles {
    foreach ($name in @('Assets','ProjectSettings','Packages','Data','.vscode')) {
        $path = Join-Path $root $name
        if (Test-Path $path) { Get-ChildItem -LiteralPath $path -Recurse -File }
    }
    foreach ($name in @('.vsconfig')) { $path = Join-Path $root $name; if (Test-Path $path) { Get-Item -LiteralPath $path } }
}
function Snapshot {
    @(foreach ($file in SourceFiles) {
        [ordered]@{ path=$file.FullName.Substring($root.Length + 1).Replace('\','/'); sha256=(Hash $file.FullName); attributes=[string]$file.Attributes }
    })
}
function Mirror([string]$name) {
    & robocopy (Join-Path $root $name) (Join-Path $project $name) /MIR /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Mirror failed: $name ($LASTEXITCODE)" }
}
Idle
$output = Join-Path $project 'Evidence/HealthUiChecks'
New-Item -ItemType Directory -Force $output | Out-Null
$lock = [IO.File]::Open((Join-Path $output 'runner.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$sequence = 1
do { $attempt = Join-Path $output ('attempt-{0:d2}' -f $sequence); $sequence++ } while (Test-Path $attempt)
New-Item -ItemType Directory $attempt | Out-Null
$stage = Join-Path $project 'Assets/Game/Editor/HealthUiValidation_Temporary'
$process = $null; $stageOwned = $false; $failed = 0; $before = @(); $copies = @()
try {
    if ((Test-Path $stage) -or (Test-Path "$stage.meta")) { throw 'Existing health stage; refusing overwrite.' }
    if (!$PrepareOnly -and @(Get-ChildItem $output -Filter 'launch-intent.json' -Recurse -File).Count -ge 2) { throw 'Two-native-launch budget exhausted; no automatic retry.' }
    $before = Snapshot
    $before | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $attempt 'source-before.json')
    [ordered]@{ source=$root; project=$project; unity=$Unity; scene=$Scene; prepareOnly=[bool]$PrepareOnly; utc=[DateTime]::UtcNow.ToString('o'); noSourceEditorAccess=$true } |
        ConvertTo-Json | Set-Content (Join-Path $attempt 'context.json')
    $tools = @('HealthUiPlayChecks.cs','HealthUiPlayChecksBatch.cs')
    New-Item -ItemType Directory (Join-Path $attempt 'executed-tools') | Out-Null
    foreach ($name in ($tools + @('HealthUiMergeChecks.py','Run-HealthUiChecks.ps1'))) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $attempt 'executed-tools') }
    & python -B (Join-Path $PSScriptRoot 'HealthUiMergeChecks.py') --self-test 2>&1 | Out-File (Join-Path $attempt 'projection-checks.log')
    if ($LASTEXITCODE -ne 0) { throw 'Exact health projection/self-tests failed; no native launch.' }
    $data = Join-Path (Split-Path $Unity -Parent) 'Data'
    $dotnet = Join-Path $data 'NetCoreRuntime/dotnet.exe'; $compiler = Join-Path $data 'DotNetSdkRoslyn/csc.dll'
    if (!(Test-Path $dotnet) -or !(Test-Path $compiler)) { throw 'Unity offline Roslyn unavailable.' }
    $warm = Get-ChildItem (Join-Path $project 'Library/Bee/artifacts') -Filter 'Assembly-CSharp.rsp' -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$warm) { throw 'No warmed Bee reference/define pattern.' }
    $options = @(Get-Content $warm.FullName | Where-Object { $_ -match '^-(define:|r:|unsafe|langversion:|nostdlib)' } | ForEach-Object {
        if ($_ -match '^-r:"([^"]+)"$') {
            $reference = $Matches[1]
            if (![IO.Path]::IsPathRooted($reference)) { $reference = Join-Path $project $reference }
            if (!(Test-Path -LiteralPath $reference)) { throw "Missing warm reference: $reference" }
            '-r:"' + $reference + '"'
        } else { $_ }
    })
    $counts = @()
    foreach ($kind in @('runtime','editor')) {
        $sources = @(Get-ChildItem (Join-Path $root 'Assets') -Recurse -File -Filter '*.cs' | Where-Object {

            if ($kind -eq 'runtime') { $_.FullName -notmatch '[\\/]Editor[\\/]' } else { $_.FullName -match '[\\/]Editor[\\/]' }
        } | ForEach-Object { '"' + $_.FullName + '"' })
        $productionCount = $sources.Count
        $dllName = if ($kind -eq 'runtime') { 'Assembly-CSharp.dll' } else { 'Assembly-CSharp-Editor.dll' }
        $response = @('-target:library', ('-out:"' + (Join-Path $attempt $dllName) + '"')) + $options
        if ($kind -eq 'editor') {
            $editorWarm = Get-ChildItem (Join-Path $project 'Library/Bee/artifacts') -Filter 'Assembly-CSharp-Editor.rsp' -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            if (!$editorWarm) { throw 'No warmed editor reference pattern.' }
            $extra = @(Get-Content $editorWarm.FullName | Where-Object { $_ -match '^-r:' -and $_ -notmatch '[/\\]Assembly-CSharp(?:\.ref)?\.dll' } | ForEach-Object {
                if ($_ -match '^-r:"([^"]+)"$') {
                    $reference = $Matches[1]
                    if (![IO.Path]::IsPathRooted($reference)) { $reference = Join-Path $project $reference }
                    if (!(Test-Path -LiteralPath $reference)) { throw "Missing warm editor reference: $reference" }
                    '-r:"' + $reference + '"'
                }
            })
            $response = @(@($response | Where-Object { $_ -notmatch '^-r:' }) + $extra | Select-Object -Unique)
            $response += '-r:"' + (Join-Path $attempt 'Assembly-CSharp.dll') + '"'
            $sources += $tools | ForEach-Object { '"' + (Join-Path $PSScriptRoot $_) + '"' }
        }
        $response += $sources; $rsp = Join-Path $attempt "offline-$kind.rsp"
        [IO.File]::WriteAllLines($rsp, $response)
        & $dotnet $compiler /noconfig ("@$rsp") 2>&1 | Out-File (Join-Path $attempt "offline-$kind.log")
        $counts += [ordered]@{ kind=$kind; productionSources=$productionCount; totalSources=$sources.Count; exitCode=$LASTEXITCODE }
        $counts | ConvertTo-Json | Set-Content (Join-Path $attempt 'offline-compilation.json')
        Write-Output "Offline $kind exit=$LASTEXITCODE production=$productionCount total=$($sources.Count)"
        if ($LASTEXITCODE -ne 0) { throw "Offline $kind failed; no native launch." }
    }
    Idle
    # Preserve stale prior-task stage as evidence instead of deleting concurrent work.
    $stale = Join-Path $project 'Assets/Game/Editor/FiringFlipValidation_Temporary'
    if (Test-Path $stale) { Move-Item -LiteralPath $stale -Destination (Join-Path $attempt 'preserved-FiringFlipValidation_Temporary') }
    if (Test-Path "$stale.meta") { Move-Item -LiteralPath "$stale.meta" -Destination (Join-Path $attempt 'preserved-FiringFlipValidation_Temporary.meta') }
    Mirror 'Assets'; Mirror 'ProjectSettings'; Mirror 'Packages'
    $copies = @(foreach ($row in $before | Where-Object { $_.path -match '^(Assets|ProjectSettings|Packages)/' }) {
        $hash = Hash (Join-Path $project $row.path)
        if ($hash -ne $row.sha256) { throw "Mirror mismatch: $($row.path)" }
        [ordered]@{ path=$row.path; sha256=$hash }
    })
    $copies | ConvertTo-Json | Set-Content (Join-Path $attempt 'isolated-copy-hashes.json')
    $packageRows = @(foreach ($name in @('manifest.json','packages-lock.json')) {
        [ordered]@{ path="Packages/$name"; source=(Hash (Join-Path $root "Packages/$name")); isolated=(Hash (Join-Path $project "Packages/$name")) }
    })
    $excluded = @('Build output not built/tested','Offline warm references are a preflight only, not current package-resolution evidence')
    if ($ExcludeWebBuildProfiles) {
        $logs = @(Get-ChildItem (Join-Path $project 'Evidence') -Filter 'Unity.log' -Recurse -File)
        $failureLogs = @($logs | Where-Object { [IO.File]::ReadAllText($_.FullName) -match '(?i)(buildtarget.*(exception|fail)|build profile.*(exception|fail)|Native extension for WebGL target not found)' })
        if (!$failureLogs.Count) { throw 'No recorded native build-target failure; refusing profile exclusion.' }
        $failureLogs.FullName | Set-Content (Join-Path $attempt 'prior-build-target-failure-logs.txt')
        $profiles = Join-Path $project 'Assets/Settings/Build Profiles'
        if (Test-Path $profiles) { Move-Item -LiteralPath $profiles -Destination (Join-Path $attempt 'excluded-Build-Profiles') }
        if (Test-Path "$profiles.meta") { Move-Item -LiteralPath "$profiles.meta" -Destination (Join-Path $attempt 'excluded-Build-Profiles.meta') }
        $excluded += 'New Web Build Profiles excluded only from isolated Assets after recorded native WebGL extension failure; source untouched'
    }
    [ordered]@{ files=$packageRows; retainedWarmPackages=$false; exclusions=$excluded } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $attempt 'validation-exclusions.json')
    if (!$PrepareOnly) {
        Idle
        New-Item -ItemType Directory $stage | Out-Null; $stageOwned = $true
        foreach ($name in $tools) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage $name) }
        $log = Join-Path $attempt 'Unity.log'
        $arguments = '-projectPath "{0}" -executeMethod HealthUiPlayChecksBatch.Run -healthSource "{1}" -healthValidationProject "{0}" -healthOutput "{2}" -healthScene {3} -logFile "{4}"' -f $project,$root,$attempt,$Scene,$log
        [ordered]@{ boundSeconds=270; arguments=$arguments; utc=[DateTime]::UtcNow.ToString('o'); note='One scene/launch. No source launch. Timeout leaves process/stage untouched. No automatic retry.' } |
            ConvertTo-Json | Set-Content (Join-Path $attempt 'launch-intent.json')
        $started = Get-Date; $process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru
        $timeout = !$process.WaitForExit(270000)
        $exitCode = if ($timeout) { $null } else { $process.Refresh(); $process.ExitCode }
        [ordered]@{ pid=$process.Id; timeout=$timeout; exitCode=$exitCode; seconds=((Get-Date)-$started).TotalSeconds; noProcessKilled=$true } |
            ConvertTo-Json | Set-Content (Join-Path $attempt 'process.json')
        if ($timeout) { throw 'Native launch timed out; no automatic retry, no kill; stage retained. Report partial validation.' }
        if (!(Test-Path $log)) { throw 'Missing Unity log.' }
        $text = [IO.File]::ReadAllText($log)
        $search = [regex]::Matches($text, '(?m)^\s*(?:at\s+)?UnityEditor\.Search\.SearchDatabase\+<EnumerateAll>[^\r\n]*MoveNext').Count
        $compile = [regex]::Matches($text, '(?m)^.*error CS\d+').Count
        $missing = [regex]::Matches($text, '(?im)^.*(?:referenced script.*missing|missing script:|could not be loaded|failed to import|error importing)').Count
        $exceptions = [regex]::Matches($text, '(?m)^(?:\w+\.)*\w*Exception:').Count
        $warnings = [regex]::Matches($text, '(?im)^.*(?:warning|fallback)[^\r\n]*$').Count
        [ordered]@{ knownSearchStackOccurrences=$search; compileErrorLines=$compile; importOrMissingScriptLines=$missing; rawExceptionLines=$exceptions;
            warningOrFallbackLines=$warnings; cleanConsole=($search + $compile + $missing + $exceptions -eq 0); note='Includes startup. SearchDatabase nonzero is NOT clean. Raw matches, not unique events.' } |
            ConvertTo-Json | Set-Content (Join-Path $attempt 'log-audit.json')
        if ($search + $compile + $missing + $exceptions -gt 0 -or $exitCode -ne 0) { $failed++ }
        $summaryPath = Join-Path $attempt 'summary.json'
        if (!(Test-Path $summaryPath)) { $failed++; Write-Output 'Native summary missing; inspect log.' }
        else {
            $summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
            if ($summary.code -ne 0) { $failed++ }
            Write-Output "Native $Scene code=$($summary.code) passed=$($summary.passed) failed=$($summary.failed) completed=$(@($summary.completedScenarios).Count)"
        }
        Write-Output "Launch audit: SearchDatabase=$search exceptions=$exceptions compiler=$compile import/missing=$missing warning/fallback=$warnings"
    }
} catch {
    $failed++; $_ | Out-String | Set-Content (Join-Path $attempt 'blocked-or-failed.txt'); Write-Warning $_.Exception.Message
} finally {
    $idle = (IsolatedProcesses).Count -eq 0
    if ($stageOwned -and $idle) { Remove-Item -LiteralPath $stage, "$stage.meta" -Recurse -Force -ErrorAction SilentlyContinue }
    $after = Snapshot; $after | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $attempt 'source-after.json')
    $lookup = @{}; foreach ($row in $after) { $lookup[$row.path] = $row }
    $drift = @(foreach ($row in $before) {
        $current = $lookup[$row.path]
        if (!$current -or $row.sha256 -ne $current.sha256 -or $row.attributes -ne $current.attributes) { $row.path }
    })
    $oldPaths = @{}; foreach ($row in $before) { $oldPaths[$row.path] = $true }
    $newFiles = @($after | Where-Object { !$oldPaths.ContainsKey($_.path) } | ForEach-Object { $_.path })
    $isolatedDrift = @(foreach ($row in $copies) {
        $path = Join-Path $project $row.path
        if (!(Test-Path $path) -or (Hash $path) -ne $row.sha256) { $row.path }
    })
    if ($drift.Count -or $newFiles.Count) { $failed++; Write-Warning 'Concurrent source content/attribute drift recorded, never reverted.' }
    [ordered]@{ failureConditions=$failed; prepareOnly=[bool]$PrepareOnly; nativeStarted=($null -ne $process); sourceFiles=$before.Count;
        sourceDrift=$drift; sourceNewFiles=$newFiles; isolatedChangedFiles=$isolatedDrift; temporaryStageRemoved=(!(Test-Path $stage));
        isolatedUnityRemaining=@(IsolatedProcesses).Count; noProcessKilled=$true } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $attempt 'runner-result.json')
    Write-Output "Evidence: $attempt"; $lock.Dispose()
}
if ($failed -gt 0) { exit 1 }
exit 0
