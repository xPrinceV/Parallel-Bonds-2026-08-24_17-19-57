# Fusion progression native acceptance

## Status

**Native runs completed after parent readiness: Main357/0 and DebugRun357/0 assertions, three scenarios each. Neither is a clean-console pass:** both exited3 with a SearchDatabase startup exception, and raw logs also contain a licensing error. See [Results.md](Results.md) for exact results, screenshots, source-hash evidence and limitations. **The two-launch budget is exhausted; do not rerun or reset it.**

Ownership: this new `Tools/FusionProgressionChecks/` directory only. Runtime, scenes, existing tools and dirty source files are read-only to this task. Generated evidence and the temporary harness live exclusively in `Library/DebugRunValidationProject`.

## Run

From the source repository root, using Windows PowerShell:

```powershell
powershell.exe -NoProfile -NonInteractive -File Tools/FusionProgressionChecks/Run-FusionProgressionChecks.ps1 -PrepareOnly

# Only after explicit parent readiness, one scene per launch:
powershell.exe -NoProfile -NonInteractive -File Tools/FusionProgressionChecks/Run-FusionProgressionChecks.ps1 -ParentReady -Scene Main
powershell.exe -NoProfile -NonInteractive -File Tools/FusionProgressionChecks/Run-FusionProgressionChecks.ps1 -ParentReady -Scene DebugRun
```

The exact Unity binary is `E:/Unity/Editor/6000.4.6f1/Editor/Unity.exe`. The only permitted project is the existing warmed `Library/DebugRunValidationProject`. No source editor is opened, connected to, saved, or closed. Open editors and asset-import workers are never killed; an existing isolated editor blocks execution.

`-PrepareOnly` captures the current tools and source content/attribute hashes; it does not compile, mirror, stage, or launch. With `-ParentReady`, offline Unity Roslyn compilation of current source runtime/editor scripts and the harness must pass before mirroring or launching. This uses existing warmed Bee references, not an installed SDK or new package restore. Compilation failure consumes no launch.

Each launch has an external **270-second wait bound** and an internal 210-second deadline starting at `executeMethod`. There are **at most two launch intents across this runner's evidence folder**, independent of the exhausted Health UI runner. No automatic retries. External timeout leaves that process and stage untouched: it is incomplete validation, not a pass, and the wall-clock lifetime may exceed the wait bound because killing editors is forbidden. The isolated harness calls `EditorApplication.Exit` on completion/deadline. Stage cleanup occurs only when no isolated Unity process remains. Do not remove launch-intent evidence to reset the budget.

Assets, ProjectSettings and Packages are mirrored only into the idle isolated project and checked against the source snapshot. Unexpected foreign `*_Temporary` stages block the mirror instead of deleting another task's staged work. The warm Library is retained, but package configuration and embedded packages come from source; source/isolated manifest and lock hashes are recorded. Native package resolution may require network access. Offline warm references are a preflight only, not current package-resolution evidence. The historical runs in Results.md retained different package configuration and omitted an optional editor helper; this runner update does not change those results or certify a new native run. Optional `-ExcludeWebBuildProfiles` is allowed only when retained native evidence already contains a matching WebGL/build-target failure; exclusions and supporting logs are recorded. Never apply these exclusions to source.

## Native coverage per scene

Each scene starts from its actual imported `Assets/Scenes/Main.unity` or `DebugRun.unity`, with AssetDatabase dependency hashes verified before opening the isolated copy. The graphical editor enters real Play Mode and runs production MonoBehaviours, scene reloads, UI, rendering and events. No mock runtime classes or generated replacement scene are used.

1. **Early Material-entry final fusion:** checks serialized Material=3 / Echo=2 starters and Material's actual equipment. Fusion must initialize the never-activated Echo starter loadout before publishing `FusionStateChanged`.
2. **Fresh reload, early Echo-entry final fusion:** performs the production manual world switch, checks two-weapon Echo equipment, current player/health/XP aliases and HUD binding, then tests fusion from Echo. Manual switching must not award residence credit.
3. **Fresh reload, accelerated automatic boundaries:** resolves the production shared controller via reflection of `WorldManager.SwitchFlow`, ignoring disabled legacy scene controllers. Verifies the native 90-second interval and nominal 540-second/six-residence contract, then injects `StateSwitchController.timerCounter = 0.75f` before each boundary. Actual scaled `Update`, warning/flip/midpoint and final-fusion lifecycle must produce M1/E1/M2/E2/M3/E3, no premature boss/fusion, and no ordinary flip at the final Echo boundary. Pausing freezes the injected boundary; auto-off prevents automatic scheduling.

Both early-entry scenarios verify:

- Five distinct original Weapon instances; no clone, removal, reparent, per-hero assigned-list rewrite, or loss/replacement of existing stats and weapon levels. Existing stats get distinct runtime sentinels before fusion.
- Runtime XP fixture: Material level3/remainder4 =17 total; Echo level2/remainder2 =7 total. **Only the entry hero** must report 24 total, level4/remainder0 after merge (thresholds5,8,11), already committed in `FusionStateChanged`. Player and XP aliases must point to the entry hero. The secondary retains its original stored total, level and remainder: Echo7 for Material entry, Material17 for Echo entry. This is checked at the event, after activation and after redirected pickups/selections. No bonus selection, pause, or delayed selection from secondary Start.
- Fused entry ownership, all five equipped/authorized weapons, dynamic inventory slot capacity and matching non-null enabled HUD sprites. After forcing canvas layout, the fifth and any later icons must have finite, nonzero screen-space bounds fully inside the GameView (one-pixel edge tolerance) and not overlap preceding equipped icons. Coordinates use the canvas render camera, or null for Overlay. PNG evidence records both entry heroes' five-icon HUDs; this does not replace subjective visual-layout review or certify mask/occlusion behavior.
- Shared health damage and the same health-slider binding on both heroes.
- Secondary-owned weapon authorization through production `LevelUpSelectionButton.SelectUpgrade`. Secondary `GetExp(34)` redirects to the entry and crosses thresholds15 and19. The fixture prepares the real button for +0.25 damage, calls `SelectUpgrade`, checks the next selection remains open/paused, then prepares +0.5 damage and calls `SelectUpgrade` again. Both upgrades must apply (net +0.75 damage); no direct `CompleteUpgradeSelection` call skips a choice. Its production integration must process the next threshold and finally close/unpause the panel. Entry XP must be58 at level6/remainder0, while the secondary's original stored pool remains unchanged.
- Actual fusion `Completed` event observed before boss phase; no boss in that event frame, exactly one production Titan afterward. Fusion pause freezes the presentation and boss gate. Boss death goes through `TakeDamage`, native victory pauses, and production `GameOverManager.Restart` reloads the same scene with normal starter equipment, zero fusion XP/residences, restored auto mode and shared HP.

Scenario failures are retained and the next independent case attempts a fresh native reload. A fatal startup/initial-contract failure or native deadline can prevent later cases; completion requires all three scenario labels, not just some passing assertions.

## Fixture boundaries and non-claims

- Ordinary spawning is suppressed **only in Play Mode**: `ConfigureExternalStages` prevents sleeping-world legacy Start from restarting it, and the harness calls `EnemySpawner.StopSpawning(true)` before spawner Updates (execution order -70, after run stages at -80). Spawner components remain enabled because production finale validation requires them. The production finale boss spawner is untouched. No saved fixture changes, weapon disable, invulnerability, or runtime script edits. This prioritizes lifecycle/UI acceptance, not combat balance or enemy-wave acceptance.
- XP/weapon stat seeds, deterministic button selection, pause (`Time.timeScale=0`) and timer reflection are runtime fixtures, explicitly logged. Non-paused simulation uses scale1.
- The six-boundary test is **accelerated deterministic boundary injection**, not 540 naturally simulated seconds and not a real nine-minute run. Existing RoundChecks mock full-time coverage remains separate evidence, not native coverage claimed here.
- Switches, upgrades and restart use production public APIs/button handlers; this does not certify keyboard input or pointer hit-testing. Restart tests the real scene reload path, not a reset mock.
- The legacy reversible-preview contract is entry-only borrowing: `EndFusionExperience` subtracts the contributed secondary pool while retaining fusion-earned XP on the entry. Thus after these seeds and34 earned XP, preview exit would leave Material-entry51/Echo-secondary7 or Echo-entry41/Material-secondary17. These final-fusion/restart scenarios do not directly certify reversible preview exit/re-entry or invoke its internal XP methods.
- Standalone builds, source-editor behavior, WebGL profiles and package parity are not certified.

## Evidence and acceptance

Every invocation gets a new `Library/DebugRunValidationProject/Evidence/FusionProgressionChecks/attempt-NN/` folder:

- `source-before.json`, `source-after.json`, `runner-result.json`: hashes and attributes of Assets, ProjectSettings, Packages, Data, .vscode, Tools and root files; added/changed/deleted source files are reported, never reverted. Parent/editor concurrent changes may cause drift and block source-preservation acceptance without implying this runner wrote them.
- `executed-tools/`, `context.json`: immutable invocation tool snapshot and paths/readiness flags.
- For readiness-gated runs: `offline-runtime.log`, `offline-editor.log` and response files; `isolated-copy-hashes.json`, `native-dependency-hashes.tsv`, `validation-exclusions.json`.
- For launches: `launch-intent.json`, `process.json`, raw `Unity.log`, `log-audit.json`, `raw-error-lines.txt`, `native-errors.log` if captured errors occur, incremental `checks.log`, screenshots and `summary.json`.
- `blocked-or-failed.txt` records preparation/launch/summary failure instead of inventing a result.

Report scene assertion results separately from the startup/console audit. **Any SearchDatabase stack occurrences are not a clean pass**, even if all gameplay scenarios pass. Retain and quote actual raw errors. Missing summaries, timeout, compiler/import failures, source drift, or fewer than three completed cases are not successful native acceptance. Preparation-only exit0 proves preparation and source snapshot stability, not compilation or Play Mode success.
