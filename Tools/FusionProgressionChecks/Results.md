# Native fusion progression acceptance results

## Outcome

**Gameplay scenarios passed in both Main and DebugRun. Neither launch is a clean-console pass.** The two-launch budget is exhausted; no native retry was performed.

| Native scene | Evidence attempt | Assertions | Completed scenarios | Unity exit | Launch duration | Timeout |
| --- | --- | --- | --- | --- | --- | --- |
| Main | attempt-02 | 357 passed / 0 failed | 3/3 | 3 | 61.478 seconds | No |
| DebugRun | attempt-03 | 357 passed / 0 failed | 3/3 | 3 | 54.196 seconds | No |

Each `summary.json` records zero unexpected captured errors, one captured SearchDatabase exception and zero captured warnings. Each full raw Unity log contains two SearchDatabase stack lines from that exception, plus a licensing error. Both PowerShell invocations returned1 rather than misreporting clean acceptance; code3 in the native summaries denotes the captured SearchDatabase error.

## Final source verification

After the post-review safeguards, the final sources passed:

- `dotnet run --project Tools/ExperienceChecks/ExperienceChecks.csproj --configuration Release`: 16 scenarios, 119 assertions.
- `dotnet run --project Tools/RoundChecks/RoundChecks.csproj`: 14 scenarios, 166 assertions.
- Starter, health UI, Main UI and main-baseline static self-tests.
- `dotnet build Assembly-CSharp.csproj --no-restore -v minimal`: no errors, 11 existing warnings.
- Unity Roslyn runtime/editor compilation: no errors; diagnostic counts by file/code unchanged from attempt-03. New evidence is in `Library/DebugRunValidationProject/Evidence/FusionProgressionFinalCompile/`; historical native evidence was not overwritten.

The 357-per-scene native results below predate those final safeguards. The updated native harness includes a wait for the new selection debounce, but was not rerun. Deterministic tests use service doubles and do not replace native lifecycle acceptance of those last changes.

## Actual native coverage

All three scenarios completed in each scene:

1. Early Material-entry final fusion, including the sleeping Echo starter initialization.
2. Fresh native reload and early Echo-entry final fusion via production manual switching and HUD binding.
3. Fresh reload and all six accelerated native residence boundaries, alternating M1/E1/M2/E2/M3/E3 before final fusion.

Both entry scenarios passed original five-weapon identity/parent/stat/level retention, original per-hero3/2 assigned collections, entry-only XP17+7=24 at level4/remainder0, unchanged secondary stored pool, no bonus selection, redirected secondary XP, two real `SelectUpgrade` calls (+0.25/+0.5 damage), queued threshold processing and entry58 XP with secondary unchanged. Dynamic fifth-icon screen bounds/non-overlap, current aliases, XP HUD and shared HP binding checks passed.

Fusion completion event ordering, no boss in its event frame, exactly one production Titan afterward, fusion pause, boss death/victory and production same-scene restart/reset passed. The boundary case also passed pause/auto-off checks and no ordinary flip at the final Echo boundary.

**Timing limitation:** the residence test sets the private timer to0.75 seconds before each boundary and lets native production Update/transition logic run at scale1. It does not demonstrate540 naturally simulated seconds or nine minutes of wall-clock gameplay.

## Raw startup errors (both scenes)

```text
[Licensing::Module] Error: Access token is unavailable; failed to update
ArgumentOutOfRangeException: Index was out of range. Must be non-negative and less than the size of the collection.
Parameter name: index
  at System.Collections.Generic.List`1[T].get_Item (System.Int32 index)
  at UnityEditor.Search.SearchDatabase+<EnumerateAll>d__80.MoveNext ()
  at System.Linq.Enumerable.TryGetFirst[TSource] (...)
  at System.Linq.Enumerable.First[TSource] (...)
  at UnityEditor.Search.SearchDatabase.GetDefaultSearchDatabase ()
  at UnityEditor.Search.SearchInit.IndexationOnStartup ()
  at UnityEditor.EditorApplication.Internal_CallDelayFunctions ()
```

The excerpt abbreviates generic stack signatures; exact messages/stacks remain in each `Unity.log` and `native-errors.log`. These are editor startup errors, not failures of the native gameplay assertions, but **must not be classified as a clean pass**.

Main's original raw-error counter reported0 because its regex end anchor missed CRLF log lines. The counter was fixed before DebugRun. Original Main evidence is unchanged; `attempt-02/log-audit-reviewed.json` records the corrected count2 from the retained log without rerunning. Main had already returned3 for SearchDatabase before this counter correction.

## Source safety, compilation and exclusions

- Each invocation hashed2,482 source files including content and attributes: zero changed/deleted/added source files during either invocation. Cross-launch comparison differs only in `Tools/FusionProgressionChecks/Run-FusionProgressionChecks.ps1` for the raw-counter correction.
- Native AssetDatabase verification matched522 dependency files/metas per scene against source. Both temporary stages were removed; no isolated Unity processes remained. No editor/process was killed; source editor was never launched, saved, connected to, or closed by this task.
- Offline Unity Roslyn compilation passed for78 runtime sources and9 editor sources per invocation. This custom offline reference/option pattern emitted87 runtime warnings (76 CS0649,6 CS0618,2 CS0169,3 CS0414) and6 editor warnings (CS0618). Five editor warnings come from this harness's older FindObjectsByType/GetInstanceID APIs, one from existing BuffTestSetup. These are not the parent's dotnet build warning count; logs are preserved. No native compiler warning lines were found in DebugRun's Unity log.
- Before the first launch, a harness fixture bug was corrected: disabling EnemySpawner components would invalidate production finale configuration. Instead, public ConfigureExternalStages/StopSpawning suppress ordinary spawning while components remain enabled. A harness Update at order-70 follows run stages at-80 and precedes spawner Updates. Production boss spawning is untouched. This remains a runtime-only no-ordinary-enemies fixture, not combat/wave acceptance.
- An optional editor helper and Web Build Profiles were excluded **only from the isolated copy**. Profile exclusion was supported by retained prior logs containing `Native extension for WebGL target not found`. The three isolated missing profile files in runner-result are these declared exclusions, not source changes.
- Warm isolated Packages were retained and both manifest/lock hashes differ from source; exact hashes and exclusions are in `validation-exclusions.json`. Package parity, WebGL builds and standalone builds are not certified. The current runner instead mirrors source Packages and includes all source editor scripts; these historical results do not validate that updated configuration.

## Evidence and screenshots

All paths below are relative to the source repository root.

Main evidence: `Library/DebugRunValidationProject/Evidence/FusionProgressionChecks/attempt-02/`

- `summary.json`, `checks.log`, `process.json`, `runner-result.json`
- `Unity.log`, `native-errors.log`, `log-audit-reviewed.json`
- `Material-fusion-five-icons.png`
- `Echo-fusion-five-icons.png`

DebugRun evidence: `Library/DebugRunValidationProject/Evidence/FusionProgressionChecks/attempt-03/`

- `summary.json`, `checks.log`, `process.json`, `runner-result.json`
- `Unity.log`, `native-errors.log`, `raw-error-lines.txt`, `log-audit.json`
- `Material-fusion-five-icons.png`
- `Echo-fusion-five-icons.png`

All four screenshots were opened and visually inspected: the fifth framed icon is visible above the four-column bottom-right row. Automated geometry/sprite checks also passed. Screenshots are during fusion entrance, not a claim of settled finale visual review. Attempt01 is preparation-only; each native attempt retains its exact executed-tools snapshot, source before/after manifests, offline compilation logs and native dependency hashes.

## Latest post-review changes — after the 357 assertions per scene

The Main attempt-02 and DebugRun attempt-03 results above describe their retained executed snapshots, not acceptance of the latest source. **No new native run was performed; the current harness has not been rerun.** Historical results, logs, screenshots and executed-tools snapshots remain unchanged.

Subsequent runtime changes:

- `EquippedWeapons` exposes a stable combined fusion cache of the original weapon instances. Fusion entry and `AddWeapon` inventory mutations rebuild that cache; reads do not rebuild it or clone weapons, stats or Buff sources.
- Upgrade clicks are bound to their XP owner and selection version/session, consumed once, and protected by a 0.2-second unscaled debounce across queued choices. These new guards were not exercised by the retained native runs.
- Fusion cleanup subtracts only the borrowed secondary XP contribution, preserving XP earned by the entry hero while fused and leaving the secondary stored pool unchanged. Pending selections are invalidated and rebuilt against restored equipment, or cancelled on death, rather than retaining stale secondary-weapon choices.
- Entry-only cumulative XP recomputation and subsequent progression respect the `levelCount - 1` cap (bounded by the available level table), preserve excess XP and award no retroactive upgrade choices for the fusion merge.

The current `FusionProgressionChecks.cs` now yields `WaitForSecondsRealtime(.25f)` before the second actual `SelectUpgrade` click so the queued choice can clear the new debounce while game time is paused. The historical executed harness made both clicks in the same frame; its two-click passes remain evidence only for that earlier implementation. This source-only harness adjustment does not add native assertions or certify the post-review changes.
