# Run stages

## Scenes and progression

Both `Assets/Scenes/Main.unity` and `Assets/Scenes/DebugRun.unity` have a `RunStageController`. Main uses `useSharedWaves=false`, retaining its original independent waves. DebugRun uses `useSharedWaves=true`: Wave 1 lasts the first 20 scaled seconds, Wave 2 the next 20, and Wave 3 continues until 480 accumulated scaled game seconds. Pauses, including upgrade selection, do not count toward that total.

At 480 seconds, both scenes start irreversible character and world fusion. Normal switching remains every 15 seconds with a 5-second warning and a 0.8-second flip until the finale. Fusion reuses the 3-second, four-flip animation, transitioning a centered white portrait to the fusion sprite. Both existing world roots coexist; no third map is created. Fusion cannot be toggled off, and the world-switch countdown does not resume.

The finale stops ordinary spawning. On the frame after the fusion animation reports `Completed`, exactly one RiftLord spawns with 300 HP at a distance of 6 units from the player. Boss death wins; player death loses. Restart restores a fresh run, including normal character and world state.

`Assets/Prefabs/RiftLordBoss.prefab` is a Titan variant with the dedicated `Rift Lord.png` body, point-filtered and uncompressed. Only the body is scaled/aligned to the original height and feet; collisions, physics, poison and delayed attacks remain inherited. Ordinary Titan enemies are unchanged. A unique Rift Lord moveset is not implemented yet.

Press the backquote/tilde key to open the developer GUI (Editor or Development Build); the stage panel starts hidden. Esc or Close hides it again without changing time scale. Both scenes' windows show five buttons: **15s Switch**, **Auto switch: On/Off**, **480s Finale**, **Restart**, and **Close**, plus world/fusion/shared-HP/run status. Restart remains available after death, completion, or an upgrade pause. Main's copied `RunStagePanel` stays bound to Main's own run controller and UI. The former Echo button is reused for Auto switch; the old Fusion button and Wave 1/2/3/Next controls remain hidden in both scenes; DebugRun's underlying shared-wave progression and APIs are unchanged.

**F** is an Editor/Development Build shortcut through `TryStartFinale()`, subject to the same guards. It starts the finale early; it is not an enter/exit fusion toggle.

## One transition path

```text
Shared-wave timer -> TryAdvanceStage() -> TryEnterStage(index)
15s Switch        -> StateSwitchController.RequestNextWorldSwitch()
Auto switch: On/Off -> DeveloperDebugGui.ToggleAutomaticSwitching()
480s Finale       -> TryStartFinale()
480-second timer  -> TryStartFinale()
F shortcut (Editor / Development Build) -> TryStartFinale()
```

**15s Switch** uses the same warning/flip flow (up to 5 seconds of warning; an existing warning is not restarted), with a 0.8-second flip, never an instant commit. Both event buttons only trigger events early: they do not jump `ElapsedTime`, simulate skipped time, or grant catch-up growth. Acceptance remains subject to existing gameplay guards.

`TryEnterStage` validates wave requests, clears outgoing enemies and recognized attack objects in both worlds, and starts the selected shared wave. Index 3 delegates to `TryStartFinale()` rather than spawning a boss immediately. The accumulated 480-second trigger calls that finale entry directly; Wave 3 does not advance after another 20 seconds. Buttons never write stage fields, spawn enemies, or apply damage themselves.

- Indices 0, 1, 2 select shared waves; index 3 requests the finale in either scene.
- Invalid indices, repeated current-phase requests, paused/upgrade states, and dead or finished runs are rejected without resetting the encounter. Once the finale starts, it cannot be reversed or restarted by stage controls.
- DebugRun's API still allows backward wave jumps before the finale while alive. They do not heal, reset experience or buffs, or equip weapons. Main retains autonomous waves; neither GUI exposes wave selection or Next.
- Existing XP pickups remain world-owned and are not cleared by a phase change.
- EnemySpawner's external-stage mode suppresses its own wave timer. Its first Start in a sleeping world cannot overwrite the selected phase.
- With `useSharedWaves=false` in Main, spawners retain their original autonomous waves. The controller still tracks the accumulated finale time and terminal state.
- `EnemyController.Died` is raised after lethal damage handling. Boss completion uses that event, not object destruction or an empty scene search.
- Completion is finalized on a subsequent controller update so a lethal-hit callback is not interrupted by encounter cleanup. Defeat takes priority if shared health is exhausted.
- Completion/defeat stops spawning and the run timer, closes upgrade selection, and pauses time. Restart ends fusion before unloading and disables the outgoing stage controller before restoring time scale.

## Configuration

`RunStageController` configuration includes `useSharedWaves`, world/spawner references, wave timing, the 480-second finale threshold, boss prefab, 300-HP boss health, and 6-unit spawn distance. World-local wave lists remain on EnemySpawner. Main preserves its original waves; DebugRun uses shared progression. Each panel must reference its own scene's controller and UI.

`RunStagePanel` owns only UI binding and presentation. The controller remains the authority for accepting transitions. `DeveloperDebugGui` controls canvas visibility; event buttons call the shared switching flow or run controller, never changing stage state directly. Its root remains active while hidden so Update still receives the toggle key, even at zero time scale. Hidden GUI elements cannot receive raycasts or retain their own EventSystem selection.

`DeveloperDebugGuiSetup.ConfigureScene(scene)` configures the developer window. Both scenes use the reduced event controls and status panel. Main's copied DebugRun panel is rebound to Main's run controller and UI, not to DebugRun objects.

The reused `echoButton` calls `ToggleAutomaticSwitching()` to set `flow.AutomaticSwitchingEnabled = !flow.AutomaticSwitchingEnabled`, then refreshes its On/Off label in Update. It remains usable during pause or upgrade selection; fused states reject even direct callback invocation.

The switch button must hold the scene's correct shared `StateSwitchController`, not a legacy inactive component found via `FindFirstObjectByType`. Its serialized `automaticSwitchingEnabled=true` field is exposed by public read/write `bool AutomaticSwitchingEnabled`. Turning it off stops only automatic 15-second switching and clears automatic warnings that have not begun flipping; accepted manual requests and active flips finish normally. Manual requests remain available. Turning it back on starts a fresh 15-second cycle without resetting an active flip. The automatic 480-second finale timer is unaffected. See the [reference-binding example](../Worlds/README.md#warning-and-horizontal-flip).

The checked-in debug scene is ready to use and registered in Build Settings. **Tools > Parallel Bonds > Create Debug Run Scene** can create it from Main when it does not exist. The setup uses AssetDatabase.CopyAsset, preserves existing build entries, and refuses to overwrite an existing DebugRun or copy a dirty Main.

## Verification

The 82-check legacy boundary suite has passed; additional finale tests are being added. `Tools/TimedEventChecks` previously recorded 86 passes and 0 failures in Live checks across Main and DebugRun. New coroutine checks cover actual Auto switch button round trips and labels, paused operation, manual switching with Auto Off, and fused callback rejection. These additions have not been run in Unity. Fixtures start near timer boundaries with high HP and weapons disabled; this is not a full 15-second or eight-minute soak. Legacy results do not establish complete coverage of the 480-second trigger, irreversible fusion, deferred single-boss spawn, or Main's new panel bindings.

`Tools/DebugRunChecks.cs` contains legacy stage, button/pointer, pause/death, world/fusion, cleanup, boss-death, and reload checks. Its former 20/40/60-second progression expectations are not the current gameplay contract and must not be cited as verification of the new finale.

Natural-progression checks use controlled survival health and injected XP; they are not a claim of an untouched full-run balance test. Upgrade choices use real button callbacks. Pointer tests use EventSystem raycasts and synthetic pointer events, not operating-system mouse input.

`Tools/Run-DebugRunChecks.ps1 -Unity <path-to-Unity.exe>` runs against the prewarmed isolated project at `Library/DebugRunValidationProject`, leaving the source editor open. It requires that disposable project to exist, overlays current files, bounds each process to 180 seconds by default, and removes temporary editor helpers. Logs are under `Logs/DebugRunValidation-*.log`. The checks can also be compiled through the batch entry in a separately prepared disposable project.

`Tools/DeveloperDebugGuiChecks.cs` (runner `-Suites Gui`) covers startup visibility, open/close at paused and terminal states, raycasts and pointer controls, repeated toggling, and fresh scene reload. Shortcut bindings are checked in source and configuration; physical keyboard input is not injected. Release/editor/development preprocessor guards are compiled separately, not verified through a shipped player build.

A specifically matched UnityEditor.Search startup exception is reported separately from gameplay errors; unexpected runtime errors still fail verification.
 