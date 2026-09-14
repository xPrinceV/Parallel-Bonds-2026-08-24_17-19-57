param(
    [Parameter(Mandatory = $true)][string]$Unity,

    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$EvidenceName = 'native-validation',
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'Library/DebugRunValidationProject'
$output = Join-Path (Join-Path $root 'Library/MainBaselineIntegration') $EvidenceName
$stage = Join-Path $project 'Assets/Game/Editor/MainBaselineValidation_Temporary'
$tools = @('MainBaselinePlayChecks.cs', 'MainBaselinePlayChecksBatch.cs')
function Hash([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create(); $stream = [IO.File]::OpenRead($path)
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function IsolatedProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
        $line = $_.CommandLine
        if (!$line) { throw 'Cannot inspect a Unity command line safely.' }
        $match = [regex]::Match($line, '(?i)-projectPath\s+(?:"([^"]+)"|(\S+))')
        if ($match.Success) {
            $path = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
            [IO.Path]::GetFullPath($path).TrimEnd('\','/') -eq [IO.Path]::GetFullPath($project).TrimEnd('\','/')
        }
    })
}
function Idle { if ((IsolatedProcesses).Count) { throw 'Only the isolated project is busy; source editors are permitted and never terminated.' } }
function Mirror([string]$name) {
    & robocopy (Join-Path $root $name) (Join-Path $project $name) /MIR /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Mirror failed: $name ($LASTEXITCODE)" }
}
if (!(Test-Path -LiteralPath $Unity -PathType Leaf) -or !(Test-Path (Join-Path $project 'Library'))) { throw 'Native Unity executable and existing warm isolated Library required.' }
Idle
if ((Test-Path $stage) -or (Test-Path "$stage.meta")) { throw 'Temporary stage already exists; refusing overwrite.' }
New-Item -ItemType Directory -Force $output | Out-Null
if ((Test-Path (Join-Path $output 'Unity.log')) -or (Test-Path (Join-Path $output 'offline-editor.log'))) { throw 'Existing attempt evidence must be preserved before a new run.' }
$process = $null; $failed = 0; $rows = @(); $packageRows = @()
$fontFiles = @('Kenney Pixel SDF.asset', 'Kenney Pixel SDF - Outline.asset') | ForEach-Object { 'Assets/Vampire Survival Assets/Fonts/Kenney Fonts/' + $_ }
try {
    foreach ($name in @('MainBaselineChecks', 'FontAssetChecks')) {
        & python -B (Join-Path $PSScriptRoot "$name.py") --self-test 2>&1 | Tee-Object -FilePath (Join-Path $output "$name.log")
        if ($LASTEXITCODE -ne 0) { throw "Static gate failed: $name; no native launch." }
    }
    & python -B -c "import sys,pathlib; sys.path.insert(0,sys.argv[1]); import FontAssetChecks as f; mapping,_=f.source_cmap(); assert len(mapping)==209; pathlib.Path(sys.argv[2]).write_text(''.join(str(c)+'\n' for c in sorted(mapping)),encoding='utf-8')" $PSScriptRoot (Join-Path $output 'source-repertoire.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Source repertoire generation failed; no native launch.' }
    Idle
    Mirror 'Packages'
    $packageRows = foreach ($name in @('manifest.json', 'packages-lock.json')) {
        [ordered]@{ path="Packages/$name"; source=(Hash (Join-Path $root "Packages/$name")); isolated=(Hash (Join-Path $project "Packages/$name")) }
    }
    [ordered]@{ files=$packageRows; mirroredSourcePackages=$true } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'package-context.json')
    Mirror 'Assets'; Mirror 'ProjectSettings'

    $rows = @(foreach ($tree in @('Assets', 'ProjectSettings', 'Packages')) {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $tree) -Recurse -File) {
            $relative = $file.FullName.Substring($root.Length + 1).Replace('\','/')
            $hash = Hash $file.FullName
            $copy = Hash (Join-Path $project $relative)
            if ($hash -ne $copy) { throw "Source/copy hash mismatch: $relative" }
            [ordered]@{ path=$relative; source=$hash; isolated=$copy; excluded=$false }
        }
    })
    $rows | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'source-hashes.json')
    Write-Output "Synchronized and hashed $($rows.Count) source files; isolated Packages mirrored from source."
    # Reuse warm Bee references/defines, but compile the CURRENT synchronized source list,
    # never the cached Assembly-CSharp.dll or stale source entries in the response file.
    # Offline warm references are a preflight only, not current package-resolution evidence.
    $data = Join-Path (Split-Path $Unity -Parent) 'Data'
    $dotnet = Join-Path $data 'NetCoreRuntime/dotnet.exe'
    $compiler = Join-Path $data 'DotNetSdkRoslyn/csc.dll'
    if (!(Test-Path $dotnet) -or !(Test-Path $compiler)) { throw 'Unity bundled offline Roslyn compiler unavailable; no native launch.' }
    $warm = Get-ChildItem (Join-Path $project 'Library/Bee/artifacts') -Filter 'Assembly-CSharp.rsp' -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (!$warm) { throw 'No warm Bee runtime reference/define pattern.' }
    $options = @(Get-Content $warm.FullName | Where-Object { $_ -match '^-(define:|r:|unsafe|langversion:|nostdlib)' })
    $options = @($options | ForEach-Object {
        if ($_ -match '^-r:"([^"]+)"$') {
            $reference = $Matches[1]
            if (![IO.Path]::IsPathRooted($reference)) { $reference = Join-Path $project $reference }
            if (!(Test-Path -LiteralPath $reference)) { throw "Missing warm reference: $reference" }
            '-r:"' + $reference + '"'
        } else { $_ }
    })
    foreach ($kind in @('runtime','editor')) {
        $sources = @(Get-ChildItem (Join-Path $project 'Assets') -Recurse -File -Filter '*.cs' | Where-Object {
            if ($kind -eq 'runtime') { $_.FullName -notmatch '[\\/]Editor[\\/]' } else { $_.FullName -match '[\\/]Editor[\\/]' }
        } | ForEach-Object { '"' + $_.FullName + '"' })
        $dll = Join-Path $output $(if ($kind -eq 'runtime') { 'Assembly-CSharp.dll' } else { 'Assembly-CSharp-Editor.dll' })
        $response = @('-target:library', ('-out:"' + $dll + '"')) + $options
        if ($kind -eq 'editor') {
            $response += '-r:"' + (Join-Path $output 'Assembly-CSharp.dll') + '"'
            $sources += $tools | ForEach-Object { '"' + (Join-Path $PSScriptRoot $_) + '"' }
        }
        $response += $sources
        $rsp = Join-Path $output "offline-$kind.rsp"
        [IO.File]::WriteAllLines($rsp, $response)
        & $dotnet $compiler /noconfig ("@$rsp") 2>&1 | Tee-Object -FilePath (Join-Path $output "offline-$kind.log")
        if ($LASTEXITCODE -ne 0) { throw "Offline $kind compile failed; no native launch and no automatic retry." }
    }
    $toolFiles = @($tools) + @('Run-MainBaselinePlayChecks.ps1')
    $toolFiles | ForEach-Object { [ordered]@{ path="Tools/$_"; sha256=(Hash (Join-Path $PSScriptRoot $_)) } } |
        ConvertTo-Json | Set-Content (Join-Path $output 'tool-hashes.json')
    New-Item -ItemType Directory (Join-Path $output 'executed-tools') | Out-Null
    foreach ($tool in $toolFiles) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $tool) -Destination (Join-Path $output "executed-tools/$tool") }
    if (!$PrepareOnly) {
        New-Item -ItemType Directory $stage | Out-Null
        foreach ($tool in $tools) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $tool) -Destination (Join-Path $stage $tool) }
        Idle
        $log = Join-Path $output 'Unity.log'
        # One native launch, real graphics and audio. No source project launch, -quit,
        # batchmode, nographics, noaudio, retries, or editor-wide process termination.
        $arguments = '-projectPath "{0}" -executeMethod MainBaselinePlayChecksBatch.Run -baselineSource "{1}" -baselineOutput "{2}" -logFile "{3}"' -f $project,$root,$output,$log
        $arguments | Set-Content (Join-Path $output 'command.txt')
        $started = Get-Date
        $process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru
        $timeout = !$process.WaitForExit(180000)
        if ($timeout) { & taskkill /PID $process.Id /T /F | Out-Null; $process.WaitForExit(); $failed++ }
        $process.Refresh()
        [ordered]@{ pid=$process.Id; timeout=$timeout; exitCode=$process.ExitCode; seconds=((Get-Date)-$started).TotalSeconds; nativeBoundSeconds=180; nativeStarts=1 } |
            ConvertTo-Json | Set-Content (Join-Path $output 'process.json')
        if ($process.ExitCode -ne 0) { $failed++ }
        if (!(Test-Path $log)) { throw 'Native log missing.' }
        $text = [IO.File]::ReadAllText($log)
        $search = [regex]::Matches($text, '(?m)^\s*(?:at\s+)?UnityEditor\.Search\.SearchDatabase\+<EnumerateAll>[^\r\n]*MoveNext').Count
        $compile = [regex]::Matches($text, '(?m)^.*error CS\d+').Count
        $missing = [regex]::Matches($text, '(?im)^.*(?:referenced script.*missing|missing script:|could not be loaded|failed to import|error importing)').Count
        $exceptions = [regex]::Matches($text, '(?m)^(?:\w+\.)*\w*Exception:').Count
        [ordered]@{ knownSearchStackOccurrences=$search; compileErrorLines=$compile; importOrMissingScriptLines=$missing; rawExceptionLines=$exceptions;
            note='Raw log covers pre-executeMethod errors too; Search errors prevent clean acceptance.' } |
            ConvertTo-Json | Set-Content (Join-Path $output 'log-audit.json')
        if ($search -gt 0 -or $compile -gt 0 -or $missing -gt 0 -or $exceptions -gt $search) { $failed++ }
        if ($text -notmatch 'BASELINE RESULT' -or !(Test-Path (Join-Path $output 'summary.json'))) { $failed++ }
        Select-String -LiteralPath $log -Pattern '^BASELINE (RESULT|FAIL|PREFLIGHT|FIXTURE|KNOWN_SEARCH|UNEXPECTED_ERROR)|error CS\d+' | ForEach-Object { $_.Line }
    }
} catch {
    $failed++
    $_ | Out-String | Set-Content (Join-Path $output 'blocked-or-failed.txt')
    Write-Warning $_
} finally {
    if ($process) {
        $process.Refresh()
        if (!$process.HasExited) { & taskkill /PID $process.Id /T /F | Out-Null; $process.WaitForExit() }
        # Only workers belonging to the exact disposable project may remain after parent exit.
        foreach ($worker in IsolatedProcesses) { & taskkill /PID $worker.ProcessId /T /F | Out-Null }
    }
    Remove-Item -LiteralPath $stage, "$stage.meta" -Recurse -Force -ErrorAction SilentlyContinue
    $fontCacheValid = $false
    if ($process) {
        # TMP's Editor quitting callback clears dynamic font tables when configured.
        # Accept only the two font asset cache changes, with a fresh structural gate,
        # preserving the post-exit assets and requiring every source hash to stay identical.
        & python -B -c "import sys,pathlib; sys.path.insert(0,sys.argv[1]); import FontAssetChecks as f; f.FONTS=pathlib.Path(sys.argv[2])/f.FONTS.relative_to(f.ROOT); sys.argv=['FontAssetChecks.py']; f.main()" $PSScriptRoot $project 2>&1 |
            Tee-Object -FilePath (Join-Path $output 'isolated-fonts-after.log')
        $fontCacheValid = $LASTEXITCODE -eq 0
        if (!$fontCacheValid) { $failed++ }
        foreach ($font in $fontFiles) { Copy-Item -LiteralPath (Join-Path $project $font) -Destination (Join-Path $output ('after-' + [IO.Path]::GetFileName($font))) }
    }
    $drift = @(foreach ($row in $rows) {
        $source = Hash (Join-Path $root $row.path)
        $copy = if (!$row.excluded) { Hash (Join-Path $project $row.path) } else { $null }
        $cacheOnly = $fontCacheValid -and $row.path -in $fontFiles -and $copy -ne $row.isolated
        if ($source -ne $row.source -or (!$row.excluded -and $copy -ne $row.isolated -and !$cacheOnly)) { $failed++ }
        [ordered]@{ path=$row.path; sourceBefore=$row.source; sourceAfter=$source; isolatedBefore=$row.isolated; isolatedAfter=$copy;
            sourceUnchanged=($source -eq $row.source); isolatedUnchanged=($copy -eq $row.isolated); acceptedIsolatedFontCacheChange=$cacheOnly }
    })
    $drift | ConvertTo-Json | Set-Content (Join-Path $output 'after-hashes.json')
    foreach ($row in $packageRows) {
        if ((Hash (Join-Path $root $row.path)) -ne $row.source -or (Hash (Join-Path $project $row.path)) -ne $row.isolated) { $failed++; Write-Warning "Package drift: $($row.path)" }
    }
    [ordered]@{ failures=$failed; prepareOnly=[bool]$PrepareOnly; temporaryStageRemoved=(!(Test-Path $stage) -and !(Test-Path "$stage.meta")); isolatedUnityRemaining=@(IsolatedProcesses).Count } |
        ConvertTo-Json | Set-Content (Join-Path $output 'runner-result.json')
}
Write-Output "Evidence: $output"
if ($failed -gt 0) { exit 1 }
exit 0
