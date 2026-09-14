param(
    [Parameter(Mandatory = $true)][string]$Unity,
    [Parameter(Mandatory = $true)][string]$ValidationProject,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\','/')
$project = [IO.Path]::GetFullPath($ValidationProject).TrimEnd('\','/')
$allowed = [IO.Path]::GetFullPath((Join-Path $root 'Library/DebugRunValidationProject'))
$Unity = [IO.Path]::GetFullPath($Unity)
if ($project -ne $allowed -or !(Test-Path -LiteralPath $Unity -PathType Leaf) -or !(Test-Path (Join-Path $project 'Library/Bee/artifacts'))) {
    throw 'Require existing Unity and exact warmed source/Library/DebugRunValidationProject.'
}
function IsolatedProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
        $match = [regex]::Match([string]$_.CommandLine, '(?i)-projectPath\s+(?:"([^"]+)"|(\S+))')
        if ($match.Success) {
            $path = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
            [IO.Path]::GetFullPath($path).TrimEnd('\','/') -eq $project
        }
    })
}
function Idle { if ((IsolatedProcesses).Count) { throw 'Isolated editor busy; no process will be killed. Source editor is not guarded.' } }
function Hash([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create(); $stream = [IO.File]::OpenRead($path)
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function Mirror([string]$name) {
    & robocopy (Join-Path $root $name) (Join-Path $project $name) /MIR /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP /XF UnityMcpSetup.cs UnityMcpSetup.cs.meta | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Mirror failed: $name ($LASTEXITCODE)" }
}
Idle
$output = Join-Path $project 'Evidence/FiringFlipChecks'
New-Item -ItemType Directory -Force $output | Out-Null
$lock = [IO.File]::Open((Join-Path $output 'runner.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$sequence = 1
do { $attempt = Join-Path $output ('attempt-{0:d2}' -f $sequence); $sequence++ } while (Test-Path $attempt)
New-Item -ItemType Directory $attempt | Out-Null
$stage = Join-Path $project 'Assets/Game/Editor/FiringFlipValidation_Temporary'
$process = $null; $stageOwned = $false; $failed = 0; $rows = @(); $protected = @()
try {
    if ((Test-Path $stage) -or (Test-Path "$stage.meta")) { throw 'Existing check stage; refusing overwrite.' }
    [ordered]@{ source=$root; validationProject=$project; unity=$Unity; prepareOnly=[bool]$PrepareOnly; output=$attempt; utc=[DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content (Join-Path $attempt 'context.json')
    $protected = @(foreach ($name in @('Packages/manifest.json','Packages/packages-lock.json','.vscode/settings.json','Assets/Game/Editor/UnityMcpSetup.cs','Assets/Game/Editor/UnityMcpSetup.cs.meta')) {
        $path = Join-Path $root $name
        if (Test-Path $path) { [ordered]@{ path=$name; sha256=(Hash $path) } }
    })
    $tools = @('FiringFlipChecks.cs','FiringFlipChecksBatch.cs')
    New-Item -ItemType Directory (Join-Path $attempt 'executed-tools') | Out-Null
    foreach ($name in ($tools + @('Run-FiringFlipChecks.ps1'))) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $attempt 'executed-tools') }
    $data = Join-Path (Split-Path $Unity -Parent) 'Data'
    $dotnet = Join-Path $data 'NetCoreRuntime/dotnet.exe'; $compiler = Join-Path $data 'DotNetSdkRoslyn/csc.dll'
    if (!(Test-Path $dotnet) -or !(Test-Path $compiler)) { throw 'Unity bundled offline Roslyn unavailable.' }
    $warm = Get-ChildItem (Join-Path $project 'Library/Bee/artifacts') -Filter 'Assembly-CSharp.rsp' -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$warm) { throw 'No warm Bee reference/define pattern.' }
    $options = @(Get-Content $warm.FullName | Where-Object { $_ -match '^-(define:|r:|unsafe|langversion:|nostdlib)' } | ForEach-Object {
        if ($_ -match '^-r:"([^"]+)"$') {
            $reference = $Matches[1]
            if (![IO.Path]::IsPathRooted($reference)) { $reference = Join-Path $project $reference }
            if (!(Test-Path -LiteralPath $reference)) { throw "Missing warm reference: $reference" }
            '-r:"' + $reference + '"'
        } else { $_ }
    })
    # Compile current source directly BEFORE staging or native launch, retaining warmed package references.
    $compileCounts = @()
    foreach ($kind in @('runtime','editor')) {
        $sources = @(Get-ChildItem (Join-Path $root 'Assets') -Recurse -File -Filter '*.cs' | Where-Object {
            if ($_.FullName -eq (Join-Path $root 'Assets/Game/Editor/UnityMcpSetup.cs')) { return $false }
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
                    if (!(Test-Path -LiteralPath $reference)) { throw "Missing warmed editor reference: $reference" }
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
                Write-Output "Offline $kind exit=$LASTEXITCODE productionSources=$productionCount totalSources=$($sources.Count); compiler details retained in evidence only."
        $compileCounts += [ordered]@{ kind=$kind; productionSources=$productionCount; totalSources=$sources.Count; exitCode=$LASTEXITCODE }
        $compileCounts | ConvertTo-Json | Set-Content (Join-Path $attempt 'offline-compilation.json')
        if ($LASTEXITCODE -ne 0) { throw "Offline $kind failed; no native launch." }
    }
    Idle
    # Packages and optional MCP integrations remain as already warmed; source packages are never copied/edited.
    $packageRows = @(foreach ($name in @('manifest.json','packages-lock.json')) {
        [ordered]@{ path="Packages/$name"; source=(Hash (Join-Path $root "Packages/$name")); isolated=(Hash (Join-Path $project "Packages/$name")) }
    })
    [ordered]@{ files=$packageRows; retainedWarmPackages=$true; excludedEditorHelper='Assets/Game/Editor/UnityMcpSetup.cs'; reason='Warm project omits optional MCP package. Personal menu helper excluded from focused offline/native editor compilation; source helper and settings untouched.' } |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $attempt 'package-context.json')
    Mirror 'Assets'; Mirror 'ProjectSettings'
    foreach ($name in @('Assets/Game/Editor/UnityMcpSetup.cs','Assets/Game/Editor/UnityMcpSetup.cs.meta')) {
        $copy = Join-Path $project $name
        if (Test-Path $copy) { Remove-Item -LiteralPath $copy -Force }
    }
    $rows = @(foreach ($tree in @('Assets','ProjectSettings')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $tree) -Recurse -File) {
            $relative = $file.FullName.Substring($root.Length + 1).Replace('\','/')
            if ($relative -in @('Assets/Game/Editor/UnityMcpSetup.cs','Assets/Game/Editor/UnityMcpSetup.cs.meta')) { continue }
            $hash = Hash $file.FullName; $copy = Hash (Join-Path $project $relative)
            if ($hash -ne $copy) { throw "Copy hash mismatch: $relative" }
            [ordered]@{ path=$relative; source=$hash; isolated=$copy }
        }
    })
    $rows | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $attempt 'source-hashes.json')
    if (!$PrepareOnly) {
        Idle
        New-Item -ItemType Directory $stage | Out-Null; $stageOwned = $true
        foreach ($name in $tools) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage $name) }
        $log = Join-Path $attempt 'Unity.log'
        $arguments = '-projectPath "{0}" -executeMethod FiringFlipChecksBatch.Run -firingSource "{1}" -firingValidationProject "{0}" -firingOutput "{2}" -logFile "{3}"' -f $project,$root,$attempt,$log
        [ordered]@{ boundSeconds=270; arguments=$arguments; utc=[DateTime]::UtcNow.ToString('o'); note='Graphical native editor; no source launch; timeout leaves process untouched.' } |
            ConvertTo-Json | Set-Content (Join-Path $attempt 'launch-intent.json')
        $started = Get-Date; $process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru
        $timeout = !$process.WaitForExit(270000)
        $exitCode = if ($timeout) { $null } else { $process.Refresh(); $process.ExitCode }
        [ordered]@{ pid=$process.Id; timeout=$timeout; exitCode=$exitCode; seconds=((Get-Date)-$started).TotalSeconds; noProcessKilled=$true } |
            ConvertTo-Json | Set-Content (Join-Path $attempt 'process.json')
        if ($timeout) { throw 'Native launch timed out; process left untouched, stage retained. User must decide next action.' }
        if (!(Test-Path $log)) { throw 'Missing Unity log.' }
        $text = [IO.File]::ReadAllText($log)
        $search = [regex]::Matches($text, '(?m)^\s*(?:at\s+)?UnityEditor\.Search\.SearchDatabase\+<EnumerateAll>[^\r\n]*MoveNext').Count
        $compile = [regex]::Matches($text, '(?m)^.*error CS\d+').Count
        $missing = [regex]::Matches($text, '(?im)^.*(?:referenced script.*missing|missing script:|could not be loaded|failed to import|error importing)').Count
        $exceptions = [regex]::Matches($text, '(?m)^(?:\w+\.)*\w*Exception:').Count
        $warnings = [regex]::Matches($text, '(?im)^.*(?:warning|fallback)[^\r\n]*$').Count
        [ordered]@{ knownSearchStackOccurrences=$search; compileErrorLines=$compile; importOrMissingScriptLines=$missing; rawExceptionLines=$exceptions;
            warningOrFallbackLines=$warnings; cleanConsole=($search + $compile + $missing + $exceptions -eq 0); note='Includes pre-executeMethod launch errors. SearchDatabase nonzero is NOT a clean console. Raw matches are not unique events.' } |
            ConvertTo-Json | Set-Content (Join-Path $attempt 'log-audit.json')
        if ($search -gt 0 -or $compile -gt 0 -or $missing -gt 0 -or $exceptions -gt 0 -or $exitCode -ne 0) { $failed++ }
        $summaryPath = Join-Path $attempt 'summary.json'
        if (!(Test-Path $summaryPath)) { $failed++; Write-Output 'Native summary missing; inspect evidence log (not echoed for token safety).' }
        else {
            $summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
            if ($summary.code -ne 0 -or @($summary.completedScenes).Count -ne 2) { $failed++ }
            Write-Output "Native assertions: passed=$($summary.passed) failed=$($summary.failed) unexpected=$($summary.unexpectedErrors); completed=$(@($summary.completedScenes).Count)/2"
        }
        Write-Output "Launch audit: SearchDatabase=$search exceptions=$exceptions compiler=$compile import/missing=$missing warning/fallbackLines=$warnings"
    }
} catch {
    $failed++
    $_ | Out-String | Set-Content (Join-Path $attempt 'blocked-or-failed.txt')
    Write-Warning $_.Exception.Message
} finally {
    $idle = (IsolatedProcesses).Count -eq 0
    if ($stageOwned -and $idle) { Remove-Item -LiteralPath $stage, "$stage.meta" -Recurse -Force -ErrorAction SilentlyContinue }
    $drift = @(foreach ($row in $rows) {
        $source = Hash (Join-Path $root $row.path); $copy = Hash (Join-Path $project $row.path)
        [ordered]@{ path=$row.path; sourceBefore=$row.source; sourceAfter=$source; sourceUnchanged=($source -eq $row.source); isolatedUnchanged=($copy -eq $row.isolated) }
    })
    $drift | ConvertTo-Json | Set-Content (Join-Path $attempt 'after-hashes.json')
    $sourceDrift = @($drift | Where-Object { !$_.sourceUnchanged })
    $protectedDrift = @($protected | Where-Object { (Hash (Join-Path $root $_.path)) -ne $_.sha256 })
    if ($sourceDrift.Count -or $protectedDrift.Count) { $failed++; Write-Warning 'Concurrent source drift recorded, not reverted; no write to source assets was performed.' }
    [ordered]@{ failureConditions=$failed; prepareOnly=[bool]$PrepareOnly; nativeStarted=($null -ne $process); sourceFiles=$rows.Count;
        sourceDrift=@($sourceDrift | ForEach-Object { $_.path }); protectedDrift=@($protectedDrift | ForEach-Object { $_.path }); isolatedChangedFiles=@($drift | Where-Object { !$_.isolatedUnchanged } | ForEach-Object { $_.path });
        temporaryStageRemoved=(!(Test-Path $stage)); isolatedUnityRemaining=@(IsolatedProcesses).Count; noProcessKilled=$true } |
        ConvertTo-Json -Depth 4 | Set-Content (Join-Path $attempt 'runner-result.json')
    Write-Output "Evidence: $attempt"
    $lock.Dispose()
}
if ($failed -gt 0) { exit 1 }
exit 0
