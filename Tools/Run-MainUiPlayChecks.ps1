param(
    [Parameter(Mandatory = $true)][string]$Unity,
    [Parameter(Mandatory = $true)][string]$ValidationProject,
    [string]$Output,
    [switch]$PrepareOnly,
    [switch]$ApprovedFinalRetest
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\','/')
$project = [IO.Path]::GetFullPath($ValidationProject).TrimEnd('\','/')
$Unity = [IO.Path]::GetFullPath($Unity)
function Within([string]$child, [string]$parent) {
    $child.Equals($parent, [StringComparison]::OrdinalIgnoreCase) -or $child.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
# Source derives only from Tools' parent. The warm disposable sibling is explicit, never guessed from source/Library.
if (!(Test-Path -LiteralPath $Unity -PathType Leaf) -or !(Test-Path (Join-Path $project 'Library/Bee/artifacts')) -or
    (Split-Path $project -Leaf) -ne 'DebugRunValidationProject' -or (Within $project $root) -or (Within $root $project)) {
    throw 'Require an existing Unity executable and distinct warm DebugRunValidationProject.'
}
if (!$Output) { $Output = Join-Path (Split-Path $project -Parent) 'MainBaselineIntegration/ui-native-119208b' }
if (![IO.Path]::IsPathRooted($Output)) { throw 'Output must be absolute.' }
$Output = [IO.Path]::GetFullPath($Output).TrimEnd('\','/')
if ((Within $Output $root) -or (Within $Output $project)) { throw 'Evidence must survive worktree/isolated-project deletion.' }
function IsolatedProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
        $match = [regex]::Match([string]$_.CommandLine, '(?i)-projectPath\s+(?:"([^"]+)"|(\S+))')
        if ($match.Success) {
            $path = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
            [IO.Path]::GetFullPath($path).TrimEnd('\','/') -eq $project
        }
    })
}
function Idle { if ((IsolatedProcesses).Count) { throw 'Isolated project is busy; source Unity is allowed and will never be terminated.' } }
function Hash([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create(); $stream = [IO.File]::OpenRead($path)
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function Mirror([string]$name) {
    & robocopy (Join-Path $root $name) (Join-Path $project $name) /MIR /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Mirror failed: $name ($LASTEXITCODE)" }
}
Idle
$stage = Join-Path $project 'Assets/Game/Editor/MainUiValidation_Temporary'
if ((Test-Path $stage) -or (Test-Path "$stage.meta")) { throw 'Existing temporary stage; refusing overwrite.' }
New-Item -ItemType Directory -Force $Output | Out-Null
$lock = [IO.File]::Open((Join-Path $Output 'runner.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$process = $null; $failed = 0; $rows = @(); $packageRows = @(); $toolRows = @(); $stageOwned = $false
try {
    $nativeStarts = @(Get-ChildItem -LiteralPath $Output -Recurse -Filter 'launch-intent.json').Count
    if ($ApprovedFinalRetest) {
        # Explicit one-time user authorization after the original two attempts, never an automatic retry.
        if ($PrepareOnly -or $nativeStarts -ne 2 -or (Test-Path (Join-Path $Output 'attempt-05'))) {
            throw 'Approved final retest requires exactly two prior native attempts and a fresh attempt-05; no further attempt allowed.'
        }
    } elseif (!$PrepareOnly -and $nativeStarts -ge 2) { throw 'Two native attempts already reserved; no further launch without explicit final-retest authorization.' }
    $sequence = 1
    do { $attempt = Join-Path $Output ('attempt-{0:d2}' -f $sequence); $sequence++ } while (Test-Path $attempt)
    if ($ApprovedFinalRetest -and (Split-Path $attempt -Leaf) -ne 'attempt-05') { throw 'Approved retest must use fresh attempt-05.' }
    New-Item -ItemType Directory $attempt | Out-Null
    [ordered]@{ source=$root; validationProject=$project; unity=$Unity; output=$attempt; prepareOnly=[bool]$PrepareOnly; priorNativeAttempts=$nativeStarts; approvedFinalRetest=[bool]$ApprovedFinalRetest } |
        ConvertTo-Json | Set-Content (Join-Path $attempt 'context.json')
    foreach ($name in @('MainUiMergeChecks','FontAssetChecks')) {
        $arguments = @('-B', (Join-Path $PSScriptRoot "$name.py"), '--self-test')
        if ($name -eq 'MainUiMergeChecks') { $arguments += '--require-runtime' }
        & python @arguments 2>&1 | Tee-Object -FilePath (Join-Path $attempt "$name.log")
        if ($LASTEXITCODE -ne 0) { throw "Static gate failed: $name; no Unity launch." }
    }
    Idle
    Mirror 'Packages'
    # Offline warm references are a preflight only, not current package-resolution evidence.
    $packageRows = @(foreach ($name in @('manifest.json','packages-lock.json')) {
        [ordered]@{ path="Packages/$name"; source=(Hash (Join-Path $root "Packages/$name")); isolated=(Hash (Join-Path $project "Packages/$name")) }
    })
    [ordered]@{ files=$packageRows; retainedIsolatedPackages=$false; mirroredSourcePackages=$true } |
        ConvertTo-Json -Depth 5 | Set-Content (Join-Path $attempt 'package-context.json')
    Mirror 'Assets'; Mirror 'ProjectSettings'
    $rows = @(foreach ($tree in @('Assets','ProjectSettings','Packages')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $tree) -Recurse -File) {
            $relative = $file.FullName.Substring($root.Length + 1).Replace('\','/')
            $hash = Hash $file.FullName; $copy = Hash (Join-Path $project $relative)
            if ($hash -ne $copy) { throw "Copy hash mismatch: $relative" }
            [ordered]@{ path=$relative; source=$hash; isolated=$copy }
        }
    })
    $rows | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $attempt 'source-hashes.json')
    $tools = @('MainUiPlayChecks.cs','MainUiPlayChecksBatch.cs')
    # Standalone doubles (notably WeaponAudioChecks) must never shadow native Unity types.
    # Other independent fixture projects are outside this UI campaign; Audio fixtures are type-checked, not executed.
    $compileNames = $tools + @('MenuFlowChecks.cs','AudioCatalogChecks.cs','AudioServiceChecks.cs','AudioGameplayChecks.cs','AudioGameplayChecksBatch.cs','BowDaggerIntegrationChecks.cs')
    $compileTools = @($compileNames | ForEach-Object { Get-Item -LiteralPath (Join-Path $PSScriptRoot $_) })
    $compileNames | Set-Content (Join-Path $attempt 'type-checked-tools.txt')
    $toolFiles = @($compileTools.FullName) + @('Run-MainUiPlayChecks.ps1','MainUiMergeChecks.py','FontAssetChecks.py','MapMergeChecks.py' | ForEach-Object { Join-Path $PSScriptRoot $_ })
    New-Item -ItemType Directory (Join-Path $attempt 'executed-tools') | Out-Null
    $toolRows = @(foreach ($file in $toolFiles) {
        Copy-Item -LiteralPath $file -Destination (Join-Path $attempt ('executed-tools/' + [IO.Path]::GetFileName($file)))
        [ordered]@{ path=('Tools/' + [IO.Path]::GetFileName($file)); sha256=(Hash $file) }
    })
    $toolRows | ConvertTo-Json | Set-Content (Join-Path $attempt 'tool-hashes.json')
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
    $compileCounts = @()
    foreach ($kind in @('runtime','editor')) {
        $sources = @(Get-ChildItem (Join-Path $project 'Assets') -Recurse -File -Filter '*.cs' | Where-Object {
            if ($kind -eq 'runtime') { $_.FullName -notmatch '[\\/]Editor[\\/]' } else { $_.FullName -match '[\\/]Editor[\\/]' }
        } | ForEach-Object { '"' + $_.FullName + '"' })
        $dllName = if ($kind -eq 'runtime') { 'Assembly-CSharp.dll' } else { 'Assembly-CSharp-Editor.dll' }
        $response = @('-target:library', ('-out:"' + (Join-Path $attempt $dllName) + '"')) + $options
        $productionCount = $sources.Count
        if ($kind -eq 'editor') {
            $response += '-r:"' + (Join-Path $attempt 'Assembly-CSharp.dll') + '"'
            $sources += $compileTools | ForEach-Object { '"' + $_.FullName + '"' }
        }
        $response += $sources; $rsp = Join-Path $attempt "offline-$kind.rsp"
        [IO.File]::WriteAllLines($rsp, $response)
        & $dotnet $compiler /noconfig ("@$rsp") 2>&1 | Tee-Object -FilePath (Join-Path $attempt "offline-$kind.log")
        $compileCounts += [ordered]@{ kind=$kind; productionSources=$productionCount; totalSources=$sources.Count; exitCode=$LASTEXITCODE }
        $compileCounts | ConvertTo-Json | Set-Content (Join-Path $attempt 'offline-compilation.json')
        if ($LASTEXITCODE -ne 0) { throw "Offline $kind compilation failed; no native launch, no automatic retry." }
    }
    if (!$PrepareOnly) {
        Idle
        New-Item -ItemType Directory $stage | Out-Null; $stageOwned = $true
        foreach ($tool in $tools) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $tool) -Destination (Join-Path $stage $tool) }
        $log = Join-Path $attempt 'Unity.log'
        $arguments = '-projectPath "{0}" -executeMethod MainUiPlayChecksBatch.Run -uiSource "{1}" -uiValidationProject "{0}" -uiOutput "{2}" -logFile "{3}"' -f $project,$root,$attempt,$log
        [ordered]@{ number=($nativeStarts + 1); boundSeconds=180; approvedFinalRetest=[bool]$ApprovedFinalRetest; command=$arguments; utc=[DateTime]::UtcNow.ToString('o') } |
            ConvertTo-Json | Set-Content (Join-Path $attempt 'launch-intent.json')
        $started = Get-Date; $process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru
        $timeout = !$process.WaitForExit(180000)
        if ($timeout) { Stop-Process -Id $process.Id -Force; $process.WaitForExit(); $failed++ }
        $process.Refresh()
        [ordered]@{ pid=$process.Id; timeout=$timeout; exitCode=$process.ExitCode; seconds=((Get-Date)-$started).TotalSeconds; boundSeconds=180 } |
            ConvertTo-Json | Set-Content (Join-Path $attempt 'process.json')
        if ($process.ExitCode -ne 0) { $failed++ }
        if (!(Test-Path $log)) { throw 'Missing Unity log.' }
        $text = [IO.File]::ReadAllText($log)
        $search = [regex]::Matches($text, '(?m)^\s*(?:at\s+)?UnityEditor\.Search\.SearchDatabase\+<EnumerateAll>[^\r\n]*MoveNext').Count
        $compile = [regex]::Matches($text, '(?m)^.*error CS\d+').Count
        $missing = [regex]::Matches($text, '(?im)^.*(?:referenced script.*missing|missing script:|could not be loaded|failed to import|error importing)').Count
        $exceptions = [regex]::Matches($text, '(?m)^(?:\w+\.)*\w*Exception:').Count
        $warningLines = [regex]::Matches($text, '(?im)^.*(?:warning|fallback)[^\r\n]*$') | ForEach-Object { $_.Value }
        $warningLines | Set-Content (Join-Path $attempt 'raw-warning-fallback-lines.txt')
        [ordered]@{ knownSearchStackOccurrences=$search; compileErrorLines=$compile; importOrMissingScriptLines=$missing; rawExceptionLines=$exceptions;
            warningOrFallbackLines=@($warningLines).Count; note='Full log includes pre-executeMethod errors; SearchDatabase nonzero is NOT clean. Warning line matches are not unique warning events.' } |
            ConvertTo-Json | Set-Content (Join-Path $attempt 'log-audit.json')
        if ($search -gt 0 -or $compile -gt 0 -or $missing -gt 0 -or $exceptions -gt $search) { $failed++ }
        if ($text -notmatch 'UI_NATIVE RESULT' -or !(Test-Path (Join-Path $attempt 'summary.json'))) { $failed++ }
        else {
            $summary = Get-Content (Join-Path $attempt 'summary.json') -Raw | ConvertFrom-Json
            if ($summary.code -ne 0 -or @($summary.completedScenarios).Count -ne 7) { $failed++ }
        }
        Select-String -LiteralPath $log -Pattern '^UI_NATIVE (RESULT|FAIL|SCENARIO|KNOWN_SEARCH|UNEXPECTED_ERROR)|error CS\d+' | ForEach-Object { $_.Line }
    }
} catch {
    $failed++
    if ($attempt -and (Test-Path $attempt)) { $_ | Out-String | Set-Content (Join-Path $attempt 'blocked-or-failed.txt') }
    Write-Warning $_
} finally {
    if ($process) {
        $process.Refresh()
        if (!$process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
        # Never enumerate-and-kill source editors or other processes: cleanup owns exactly this PID.
    }
    if ($stageOwned) { Remove-Item -LiteralPath $stage, "$stage.meta" -Recurse -Force -ErrorAction SilentlyContinue }
    if ($attempt -and (Test-Path $attempt)) {
        $drift = @(foreach ($row in $rows) {
            $source = Hash (Join-Path $root $row.path); $copy = Hash (Join-Path $project $row.path)
            if ($source -ne $row.source) { $failed++ }
            [ordered]@{ path=$row.path; sourceBefore=$row.source; sourceAfter=$source; sourceUnchanged=($source -eq $row.source);
                isolatedBefore=$row.isolated; isolatedAfter=$copy; isolatedUnchanged=($copy -eq $row.isolated) }
        })
        $drift | ConvertTo-Json | Set-Content (Join-Path $attempt 'after-hashes.json')
        $changed = @($drift | Where-Object { !$_.isolatedUnchanged })
        $fontFiles = @('Kenney Pixel SDF.asset','Kenney Pixel SDF - Outline.asset') | ForEach-Object { 'Assets/Vampire Survival Assets/Fonts/Kenney Fonts/' + $_ }
        foreach ($row in $changed) {
            if ($row.path -notin $fontFiles) { $failed++; Write-Warning "Unexpected isolated drift: $($row.path)" }
            else { Copy-Item -LiteralPath (Join-Path $project $row.path) -Destination (Join-Path $attempt ('after-' + [IO.Path]::GetFileName($row.path))) }
        }
        if ($process) {
            & python -B -c "import sys,pathlib; sys.path.insert(0,sys.argv[1]); import FontAssetChecks as f; f.FONTS=pathlib.Path(sys.argv[2])/f.FONTS.relative_to(f.ROOT); sys.argv=['FontAssetChecks.py']; f.main()" $PSScriptRoot $project 2>&1 |
                Tee-Object -FilePath (Join-Path $attempt 'isolated-fonts-after.log')
            if ($LASTEXITCODE -ne 0) { $failed++ }
        }
        foreach ($row in $packageRows) {
            if ((Hash (Join-Path $root $row.path)) -ne $row.source -or (Hash (Join-Path $project $row.path)) -ne $row.isolated) { $failed++; Write-Warning "Package drift: $($row.path)" }
        }
        foreach ($row in $toolRows) { if ((Hash (Join-Path $root $row.path)) -ne $row.sha256) { $failed++; Write-Warning "Tool drift during execution: $($row.path)" } }
        [ordered]@{ failureConditions=$failed; prepareOnly=[bool]$PrepareOnly; nativeStarted=($null -ne $process); sourceFiles=$rows.Count;
            isolatedChangedFiles=@($changed.path); temporaryStageRemoved=(!(Test-Path $stage) -and !(Test-Path "$stage.meta")); isolatedUnityRemaining=@(IsolatedProcesses).Count } |
            ConvertTo-Json -Depth 4 | Set-Content (Join-Path $attempt 'runner-result.json')
        Write-Output "Evidence: $attempt"
    }
    $lock.Dispose()
}
if ($failed -gt 0) { exit 1 }
exit 0
