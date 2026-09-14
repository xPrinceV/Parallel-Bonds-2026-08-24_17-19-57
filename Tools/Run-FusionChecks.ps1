param(
    [ValidateSet('Merge', 'Fusion', 'Cleanup', 'Regression', 'Timing', 'Transition')]
    [string[]]$Suites = @('Fusion', 'Cleanup', 'Regression', 'Timing', 'Transition'),
    [Parameter(Mandatory = $true)]
    [string]$Unity
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$helper = Join-Path $root 'Assets/Game/Editor/FusionChecksBatch_Temporary.cs'
if (Get-Process Unity -ErrorAction SilentlyContinue) { throw 'Close Unity before running disposable batch checks.' }
if ((Test-Path $helper) -or (Test-Path "$helper.meta")) { throw 'Temporary helper path already exists; refusing to overwrite.' }
$failed = 0
try {
    Copy-Item (Join-Path $PSScriptRoot 'FusionChecksBatch.cs') $helper
    foreach ($suite in $Suites) {
        $log = Join-Path $root "Logs/FusionChecks-$suite.log"
        $arguments = '-batchmode -projectPath "{0}" -executeMethod FusionChecksBatch.Run -fusionSuite {1} -logFile "{2}"' -f $root, $suite, $log
        $process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru
        if (-not $process.WaitForExit(120000)) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
            Write-Output "$suite TIMEOUT: killed at 120 seconds; log=$log"
            $failed++
        } else {
            $process.Refresh()
            Write-Output "$suite exit=$($process.ExitCode); log=$log"
            if ($process.ExitCode -ne 0) { $failed++ }
        }
        if (Test-Path $log) {
            Select-String -Path $log -Pattern 'FUSION_BATCH|ScytheTitanChecks|WorldTransitionChecks|FusionChecks FAIL|BowDaggerIntegrationChecks:|error CS|Exception' | ForEach-Object { $_.Line }
            if (-not (Select-String -Path $log -Pattern 'FUSION_BATCH RESULT' -Quiet)) { $failed++; Write-Output 'Missing completion marker' }
        } else { $failed++; Write-Output 'Missing Unity log' }
    }
} finally {
    Remove-Item $helper, "$helper.meta" -Force -ErrorAction SilentlyContinue
}
if ($failed -gt 0) { exit 1 }
exit 0
