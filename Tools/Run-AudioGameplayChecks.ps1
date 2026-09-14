param(
    [Parameter(Mandatory = $true)][string]$Unity,
    [Parameter(Mandatory = $true)][ValidateSet('Menu','Main','DebugRun')][string]$Suite,
    [ValidateSet('Material','Echo')][string]$Entry = 'Material',
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Revision = 'current',
    [switch]$RestartOnly
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'Library/DebugRunValidationProject'
$evidence = Join-Path $root 'Library/AudioGameplayValidation'
$mode = if ($RestartOnly) { 'RestartOnly' } else { 'Full' }
if ($RestartOnly -and ($Suite -ne 'DebugRun' -or $Entry -ne 'Echo')) { throw 'RestartOnly requires -Suite DebugRun -Entry Echo.' }
$suffix = if ($RestartOnly) { '-RestartOnly' } else { '' }
$output = Join-Path $evidence "$Revision-$Suite-$Entry$suffix"
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Relative([string]$path) { $path.Substring($root.Length + 1).Replace('\','/') }
function NoUnity { if (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'") { throw 'Concurrent Unity detected; refusing staging/launch.' } }
NoUnity
if (!(Test-Path -LiteralPath $Unity) -or !(Test-Path (Join-Path $project 'Library'))) { throw 'Expected native Unity and warm isolated Library.' }
if (Test-Path $output) { throw "Preserving evidence: choose a fresh Revision; exists $output" }
New-Item -ItemType Directory -Force $output | Out-Null
$helper = Join-Path $project 'Assets/Game/Editor/AudioGameplayChecksBatch_Temporary.cs'
if ((Test-Path $helper) -or (Test-Path "$helper.meta")) { throw 'Temporary helper already exists; refusing overwrite.' }
$runtimeTools = Join-Path $project 'Assets/Game/AudioGameplayValidation_Temporary'
if ((Test-Path $runtimeTools) -or (Test-Path "$runtimeTools.meta")) { throw 'Temporary runtime tools already exist; refusing overwrite.' }

$foreignStages = @(Get-ChildItem (Join-Path $project 'Assets/Game/Editor') -Recurse -Force -Filter '*_Temporary*')
if ($foreignStages.Count) { throw 'Existing temporary editor stage; refusing mirror rather than delete staged work.' }
# Refresh package configuration and editor scripts without replacing the warm Library.
# Scene/runtime content below remains a focused serialized-dependency copy.
foreach ($tree in @('Packages','Assets/Game/Editor')) {
    & robocopy (Join-Path $root $tree) (Join-Path $project $tree) /MIR /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Mirror failed: $tree ($LASTEXITCODE)" }
}
# Resolve serialized GUID dependencies from source assets before launching Unity.
$guidPaths = @{}
Get-ChildItem -LiteralPath (Join-Path $root 'Assets') -Filter '*.meta' -File -Recurse | ForEach-Object {
    $match = [regex]::Match([IO.File]::ReadAllText($_.FullName), '(?m)^guid: ([a-fA-F0-9]{32})')
    if ($match.Success) { $guidPaths[$match.Groups[1].Value] = (Relative $_.FullName).Substring(0, (Relative $_.FullName).Length - 5) }
}
$pending = [Collections.Generic.Queue[string]]::new()
$files = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
Get-ChildItem -LiteralPath (Join-Path $root 'Assets/Game') -File -Recurse | Where-Object {
    $_.FullName -notmatch '[\\/]Editor[\\/]' -and $_.Name -notmatch '\.md(\.meta)?$'
} | ForEach-Object { $pending.Enqueue((Relative $_.FullName)) }
@('Assets/Game.meta', 'Assets/Scenes/Main Menu.unity', 'Assets/Scenes/Main.unity', 'Assets/Scenes/DebugRun.unity') | ForEach-Object { $pending.Enqueue($_) }
Get-ChildItem -LiteralPath (Join-Path $root 'ProjectSettings') -File | ForEach-Object { $pending.Enqueue((Relative $_.FullName)) }
$unresolved = [Collections.Generic.HashSet[string]]::new()
while ($pending.Count -gt 0) {
    $path = $pending.Dequeue()
    $source = Join-Path $root $path
    if (!(Test-Path -LiteralPath $source -PathType Leaf) -or !$files.Add($path)) { continue }
    if (!$path.EndsWith('.meta') -and (Test-Path -LiteralPath "$source.meta")) { $pending.Enqueue("$path.meta") }
    # Importer metas also contain dependencies (sprites/materials); binary media itself does not.
    if ([IO.Path]::GetExtension($source) -in @('.meta','.unity','.prefab','.asset','.mat','.controller','.overrideController','.mixer','.anim','.shader','.shadergraph','.shadersubgraph')) {
        foreach ($match in [regex]::Matches([IO.File]::ReadAllText($source), 'guid: ([a-fA-F0-9]{32})')) {
            $guid = $match.Groups[1].Value
            if ($guidPaths.ContainsKey($guid)) { $pending.Enqueue($guidPaths[$guid]) }
            elseif ($guid -notmatch '^0{16}') { [void]$unresolved.Add($guid) }
        }
    }
}
# Folder metadata preserves GUIDs for newly added directories without copying unrelated content.
foreach ($path in @($files)) {
    $parent = Split-Path $path -Parent
    while ($parent -and $parent -ne 'Assets' -and $parent.StartsWith('Assets')) {
        $meta = $parent.Replace('\','/') + '.meta'
        if (Test-Path -LiteralPath (Join-Path $root $meta) -PathType Leaf) { [void]$files.Add($meta) }
        $parent = Split-Path $parent -Parent
    }
}
$rows = foreach ($path in ($files | Sort-Object)) {
    $source = Join-Path $root $path; $destination = Join-Path $project $path
    $sourceHash = Hash $source
    $prior = if (Test-Path -LiteralPath $destination) { Hash $destination } else { $null }
    if ($sourceHash -ne $prior) {
        New-Item -ItemType Directory -Force (Split-Path $destination -Parent) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
    $staged = Hash $destination
    if ($sourceHash -ne $staged) { throw "Staged hash mismatch: $path" }
    [ordered]@{ path=$path; source=$sourceHash; isolated=$staged; previousIsolated=$prior; copied=($prior -ne $sourceHash) }
}
$rows | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'source-hashes.json')
$unresolved | Sort-Object | Set-Content (Join-Path $output 'non-source-guids.txt')
# AssetDatabase.GetDependencies independently verifies the actual imported scene dependency closure.
$tools = @('AudioGameplayChecks.cs','AudioGameplayChecksBatch.cs','AudioServiceChecks.cs','AudioCatalogChecks.cs','WeaponAudioChecks.cs','Run-AudioGameplayChecks.ps1')
$toolRows = foreach ($tool in $tools) { [ordered]@{ path="Tools/$tool"; source=(Hash (Join-Path $PSScriptRoot $tool)) } }
$toolRows | ConvertTo-Json | Set-Content (Join-Path $output 'tool-hashes.json')
@('AudioGameplayChecks.cs','AudioServiceChecks.cs','AudioCatalogChecks.cs') | ForEach-Object {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $_) -Destination (Join-Path $project "Tools/$_") -Force
}
$packageRows = foreach ($path in @('Packages/manifest.json','Packages/packages-lock.json')) {
    [ordered]@{ path=$path; source=(Hash (Join-Path $root $path)); isolated=(Hash (Join-Path $project $path)); note='Packages mirrored from source; source files untouched' }
}
$packageRows | ConvertTo-Json | Set-Content (Join-Path $output 'package-context.json')
$git = & git --no-pager --no-optional-locks status --short
$git | Set-Content (Join-Path $output 'git-status-before.txt')
$preservedEvidence = @()
if ($RestartOnly) {
    $priorEvidence = Join-Path $evidence 'native03-DebugRun-Echo'
    $preservedEvidence = @(Get-ChildItem -LiteralPath $priorEvidence -File -Recurse | ForEach-Object {
        [ordered]@{ path=(Relative $_.FullName); sha256=(Hash $_.FullName) }
    })
    $preservedEvidence | ConvertTo-Json | Set-Content (Join-Path $output 'original-evidence-before.json')
}
New-Item -ItemType Directory (Join-Path $output 'executed-tools') | Out-Null
$tools | ForEach-Object { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $_) -Destination (Join-Path $output "executed-tools/$_") }
$failed = 0
try {
    New-Item -ItemType Directory $runtimeTools | Out-Null
    @('AudioGameplayChecks.cs','AudioServiceChecks.cs','AudioCatalogChecks.cs') | ForEach-Object {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $_) -Destination (Join-Path $runtimeTools $_)
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AudioGameplayChecksBatch.cs') -Destination $helper
    NoUnity
    $log = Join-Path $output 'Unity.log'
    # Native graphics/audio: neither batchmode, nographics nor noaudio. Completion owns exit.
    $arguments = '-projectPath "{0}" -executeMethod AudioGameplayChecksBatch.Run -audioSuite {1} -audioEntry {2} -audioSource "{3}" -audioOutput "{4}" -logFile "{5}"' -f $project,$Suite,$Entry,$root,$output,$log
    $arguments += ' -audioMode ' + $mode
    $arguments | Set-Content (Join-Path $output 'command.txt')
    $started = Get-Date
    $process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru
    $timedOut = !$process.WaitForExit(180000)
    if ($timedOut) { & taskkill /PID $process.Id /T /F | Out-Null; $process.WaitForExit(); $failed++ }
    $process.Refresh()
    $processRow = [ordered]@{ pid=$process.Id; timeout=$timedOut; exitCode=$process.ExitCode; seconds=((Get-Date)-$started).TotalSeconds; nativeBoundSeconds=180 }
    $processRow | ConvertTo-Json | Set-Content (Join-Path $output 'process.json')
    Write-Output ($processRow | ConvertTo-Json -Compress)
    if ($process.ExitCode -ne 0) { $failed++ }
    if (Test-Path $log) {
        $text = [IO.File]::ReadAllText($log)
        $searchCount = [regex]::Matches($text, '(?m)^UnityEditor\.Search\.SearchDatabase\+<EnumerateAll>d__80\.MoveNext').Count
        $errorRows = Select-String -LiteralPath $log -Pattern 'error CS\d+|^Argument\w*Exception:|^NullReferenceException:|^MissingReferenceException:|^InvalidOperationException:|^AUDIO_GAMEPLAY UNEXPECTED_ERROR|^AUDIO_GAMEPLAY KNOWN_EDITOR_ERROR'
        $errorRows | ForEach-Object { $_.Line } | Set-Content (Join-Path $output 'error-signatures.txt')
        [ordered]@{ searchStackOccurrences=$searchCount; signatureLines=@($errorRows).Count; note='Full raw Unity.log retained; known Search error is counted and prevents clean acceptance, including before executeMethod.' } |
            ConvertTo-Json | Set-Content (Join-Path $output 'log-audit.json')
        if ($searchCount -gt 0) { $failed++ }
        Select-String -LiteralPath $log -Pattern '^AUDIO_GAMEPLAY (RESULT|FAIL|PREFLIGHT|FIXTURE|KNOWN_EDITOR_ERROR|UNEXPECTED_ERROR)|^AUDIO (CATALOG )?RESULT|error CS\d+' | ForEach-Object { $_.Line }
        if ($text -notmatch 'AUDIO_GAMEPLAY RESULT' -or !(Test-Path (Join-Path $output 'summary.json'))) { $failed++ }
    } else { $failed++ }
} finally {
    Remove-Item -LiteralPath $helper, "$helper.meta" -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $runtimeTools, "$runtimeTools.meta" -Recurse -Force -ErrorAction SilentlyContinue
    $drift = foreach ($row in @($rows) + @($toolRows)) {
        $after = Hash (Join-Path $root $row.path)
        if ($after -ne $row.source) { $failed++; Write-Warning "Source changed externally during validation: $($row.path)" }
        [ordered]@{ path=$row.path; before=$row.source; after=$after; unchanged=($after -eq $row.source) }
    }
    $drift | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output 'source-after.json')
    if ($RestartOnly) {
        $preservedEvidence | ForEach-Object {
            $after = Hash (Join-Path $root $_.path)
            if ($after -ne $_.sha256) { $failed++; Write-Warning "Original evidence changed: $($_.path)" }
            [ordered]@{ path=$_.path; before=$_.sha256; after=$after; unchanged=($after -eq $_.sha256) }
        } | ConvertTo-Json | Set-Content (Join-Path $output 'original-evidence-after.json')
        $rows | ForEach-Object {
            $after = Hash (Join-Path $project $_.path)
            if ($after -ne $_.source) { $failed++; Write-Warning "Isolated disk drift: $($_.path)" }
            [ordered]@{ path=$_.path; expected=$_.source; after=$after; unchanged=($after -eq $_.source) }
        } | ConvertTo-Json | Set-Content (Join-Path $output 'isolated-after.json')
    }
    & git --no-pager --no-optional-locks status --short | Set-Content (Join-Path $output 'git-status-after.txt')
    NoUnity
}
Write-Output "Evidence: $output"
if ($failed -gt 0) { exit 1 }
exit 0
