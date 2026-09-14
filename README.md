# Parallel Bonds

A Unity 2D survival prototype built around two alternating worlds, independent heroes, and composable weapon buffs.

The current playable scene includes six configured automatic weapons, experience-based upgrades, and a five-hit buff activation sample. Pistol, Lantern, and Lightning are the starting weapons; Bow, Dagger, and Scythe are available in each hero's unassigned weapon list.

## Development baseline

This branch integrates GitHub `main` through `402fc0f`. Use main as the upstream baseline and keep dual-world features as explicit extensions, rather than maintaining another copy of the old scripts.

- Runtime scripts live under `Assets/Game/Features` and presentation under `Assets/Game/Presentation`. Keep their `.meta` GUIDs; do not restore duplicate classes under `Assets/Scripts` when merging upstream changes.
- Main's balance values and upgrade tables apply to all six configured weapons in both worlds and both Main/DebugRun scenes. Sniper and Shield remain unconfigured; merging a scene must not enable them implicitly.
- Keep the local world-flow contract: 15-second switching, 5-second warning, 0.8-second flip, and finale at 480 scaled seconds. Preserve per-world ownership, shared health, Buff calculations, audio and renderer sorting.
- Preserve upstream map identities and hierarchy. Check merged scene references even if Unity Smart Merge reports success.
- Kenney fonts retain main's complete atlas/table data with dynamic population, a source font and readable atlases restored for the current UI. Dynamic cache changes are expected; do not merge atlas bytes separately from glyph tables.

Run `python -B Tools/MainBaselineChecks.py --self-test` and `python -B Tools/FontAssetChecks.py --self-test` after integration. These are static gates, not Play Mode acceptance. See [merge notes](Assets/Scenes/MergeNotes.md#main-baseline-integration-402fc0f) for deliberate compatibility differences and validation scope.

## Getting started

### Requirements

- **Unity 6000.4.6f1**, installed through Unity Hub.
- Git and **Git LFS** to clone the repository and download binary art assets.
- **.NET 10 SDK** only if you want to run the standalone damage formula checks.

Unity restores the project's dependencies from `Packages/manifest.json` and `Packages/packages-lock.json`. The project uses Universal Render Pipeline, Unity 2D tooling, and Unity UI.

### Open and play

```sh
git lfs install
git clone --branch WIP-Chen-dual-world https://github.com/xPrinceV/Parallel-Bonds-2026-08-24_17-19-57.git
```

1. Add the cloned repository folder to Unity Hub.
2. Open it with Unity **6000.4.6f1** and allow package restoration and asset imports to finish.
3. Open `Assets/Scenes/Main.unity`.
4. Enter **Play Mode**.

The Main scene is already configured for a dual-world run; no scene migration is required.

| Action | Control |
| --- | --- |
| Move | WASD or arrow keys |
| Attack | Automatic |
| Select an upgrade | Click an upgrade button |
| Switch worlds | Automatic, every 15 seconds outside fusion |
| Start finale early | F (Editor or Development Build only; Game view focused) |
| Developer GUI | Backquote / tilde key to toggle; Esc to close |

## Developer GUI

In the Unity editor or a **Development Build**, focus the Game view and press the backquote/tilde key (usually below Esc) to open the developer window. Shift is not required. Press the same key, Esc, or **Close** to hide it.

The window starts hidden in both Main and DebugRun. Opening it does not pause gameplay; closing it disables its canvas and raycaster and clears only its own UI selection. The shortcut remains available during pause and after defeat. Ordinary release builds disable the window.

Both scenes show five buttons: **15s Switch**, **Auto switch: On/Off**, **480s Finale**, **Restart**, and **Close**, plus world/fusion/shared-HP/run status. The former Echo button is reused for Auto switch; the old Fusion button and Wave 1/2/3/Next controls remain hidden, not merely disabled. Main's `RunStagePanel` stays bound to Main's own run controller and UI. Underlying waves are unchanged: Main uses `useSharedWaves=false`; DebugRun uses `useSharedWaves=true`.

**Auto switch: On/Off** calls `DeveloperDebugGui.ToggleAutomaticSwitching()` to invert `flow.AutomaticSwitchingEnabled`. It works while paused and controls only the 15-second automatic cycle, not manual switching or the 480-second finale. Fused states reject the callback, even when invoked directly.

## Run stages and finale

Open `Assets/Scenes/DebugRun.unity` and press Play for shared waves and a stage panel. Both scenes start the finale at 480 accumulated scaled game seconds; paused time, including upgrade selection, does not count.

| Phase | Automatic progression |
| --- | --- |
| Wave 1 | First 20 active game seconds |
| Wave 2 | Next 20 active game seconds |
| Wave 3 | Continues until 480 accumulated active game seconds |
| Finale | Irreversible character and world fusion, then one 300-HP RiftLord |
| Win / Lose | Boss death wins; player death loses; Restart restores a fresh run |

**15s Switch** calls `StateSwitchController.RequestNextWorldSwitch()` through the same warning/flip flow (up to 5 seconds of warning; an existing warning is not restarted), with a 0.8-second flip, not an instant switch. **480s Finale** calls `TryStartFinale()` directly, as does the automatic 480-second trigger. These buttons only trigger events early: they never jump `ElapsedTime` or grant catch-up growth. Existing pause, upgrade, death, and irreversible-finale guards still apply; Restart remains available.

At 480 seconds, `TryStartFinale()` starts irreversible fusion. On the frame after the fusion animation reports `Completed`, one RiftLord spawns with 300 HP, preferring a clear point 6 units from the player. Map obstacles and boundaries can move that point to another direction or a nearby radius (3–9 units); an exhausted placement search reports failure rather than spawning inside a wall. The dedicated `RiftLordBoss` prefab uses `Rift Lord.png` and Titan's delayed-strike behavior, not a unique boss moveset; ordinary Titan enemies remain unchanged. Only actual boss death wins; unloading or clearing objects does not count.

See [run-stage setup and verification](Assets/Game/Features/GameFlow/README.md) for API and configuration details.

## Dual-world gameplay

**Material** and **Echo** each own a hero, enemy spawner, and world content. The camera and HUD follow the active hero.

- Both heroes share one current and maximum health pool. The initial world's hero supplies the starting maximum; switching does not refill health, and zero health blocks further switching.
- Experience, levels, weapon upgrades, and buff runtime state belong to each hero independently.
- Switching preserves the outgoing hero's position when safe, otherwise searches within 3 units on the destination map. No safe point cancels that switch. A successful transfer clears the incoming velocity.
- The inactive world's content is disabled, not destroyed or unloaded. Its enemies, experience pickups, projectiles, and fire remain in memory.
- World-local active-time countdowns and buffs pause while that world sleeps and resume when it becomes active again.
- Upgrade selection and a zero time scale block switching. A dead or disabled hero cannot switch to bypass the loss flow.
- Attacks, enemy targeting, and experience collection respect world ownership.

Normal world switching defaults to every 15 seconds, with a 5-second warning and a 0.8-second flip. `StateSwitchController.AutomaticSwitchingEnabled` controls only this automatic cycle, not manual requests or the eight-minute finale timer; see the [switching API](Assets/Game/Features/Worlds/README.md#warning-and-horizontal-flip). Material has no screen tint; Echo uses a blue overlay below the HUD. The heroes and maps differ: Main and DebugRun use upstream's Real World and Mirror World tilemaps, buildings, decorations, and shared boundary walls.

State retention lasts **only for the current run**. Restarting the scene does not restore the previous run from disk.

See [dual-world rules and runtime boundaries](Assets/Game/Features/Worlds/README.md) for details.

## Character fusion

The finale fuses the characters and worlds irreversibly for the rest of the run. **F** is an Editor/Development Build shortcut to start it early through the same `TryStartFinale()` path, subject to its guards; it no longer toggles fusion off.

- The entry hero remains the only visible, controllable player body. Both existing world roots coexist; this is not a new third map.
- Both heroes' equipped weapons fire from the fusion position. Weapons retain their original stats, buffs, hit stacks, and source-world ownership; no equipment is copied and no buff pools are merged.
- Attacks can hit enemies from either world. Both worlds' enemies target the entry hero, and collected experience goes to that hero alone.
- Current and maximum health remain shared. Fusion does not heal or permit revival.
- Automatic and manual world switching stop for the finale; there is no voluntary exit or countdown resumption.
- Restart restores normal character and world state for a fresh run.

Fusion reuses the 3-second, four-flip animation, with a centered white portrait transitioning to the fusion sprite. The single boss spawns only on the frame after `Completed`, not during the animation. The entry map owns settled rendering and collision; the other map is shown without physics only during its animation faces. Both worlds' gameplay content remains active. This is not a separately authored fused map.

## Weapons

| Weapon | Behavior |
| --- | --- |
| Pistol | Fires homing projectiles |
| Lantern | Throws projectiles that create damaging fire on impact |
| Lightning | Strikes randomly selected live targets |
| Bow | Fires piercing arrows in the hero's last movement direction, with a spread for multiple arrows |
| Dagger | Homes toward enemies, bounces between targets, and applies poison |
| Scythe | Sweeps a melee hitbox with its visual; additional swings spread around the hero |

All six weapons consume buff-modified **damage** and **projectile count**. For Lightning, projectile count controls the number of strikes. Attack speed, range, projectile speed, and duration still use the existing weapon upgrade logic rather than general buff evaluation.

Damage and count are snapshotted for each volley or burst. A buff activated by a hit affects future attacks, not projectiles already in flight.

Bow and Dagger are configured separately for both heroes but do not replace the three starting weapons. Their projectiles belong to the firing world and pause while it sleeps. Dagger supports Bounces upgrades; poison stacks damage and refreshes duration without delaying the next tick. Poison is currently an enemy-local status effect, not a general buff atom, and its ticks do not report additional weapon hits.

Scythe uses one base swing, with extra swings provided by projectile-count buffs. Each swing damages an enemy once, including enemies with multiple colliders. Both scenes use main's tuning: 16 base damage, 0.25 attacks per second, and Damage, Area and AttackSpeed upgrades. Swing motion is procedural; the upstream empty animation assets are not required for attacks.

## Titan enemy

Titan joins the final configured wave in both worlds. It approaches the current interaction hero, channels an attack, and creates a delayed ground strike under its source world. Channeling, telegraph and strike lifetimes pause during world sleep. Fusion permits cross-world hits without changing attack ownership; each attack damages a given hero at most once.

## Buff composition

Buff configuration and runtime state are separate:

- **Definitions** describe a buff using shared configuration assets.
- **Atoms** describe stat modifiers or stack activation rules; they do not calculate final weapon damage.
- **Runtime instances** hold each recipient's duration, stack progress, and activated effects.
- **The controller** owns active instances, receives hit events, and exposes weapon-facing calculation methods.
- **The calculator** evaluates the collected stat modifiers in the agreed order.

Typical weapon calls are:

```csharp
float damage = holder.CalculateWeaponDamage(baseDamage);
int count = holder.CalculateProjectileCount(baseCount);
holder.ReportHit(target, actualDamageDealt);
```

Equipment or other effect sources can manage their own grants:

```csharp
BuffHandle handle = holder.GrantBuff(definition, source);
// revoke this source's grant when its effect ends
holder.RevokeBuff(handle);
```

Use a stable source object. Regranting the same definition from the same source refreshes its existing instance; different sources remain independent. Destroying a source alone does not revoke its grant—the owning system must revoke it explicitly.

### Damage formula

```text
D = (B + sum(Prefix)) * product(Multiplier) + sum(Postfix)
```

| Term | Meaning | Combination |
| --- | --- | --- |
| B | Base weapon damage | Calculation starting point |
| Prefix | Flat damage before multiplication | Sum |
| Multiplier | Actual damage factor | Product across independent buffs |
| Postfix | Flat damage after multiplication | Sum |

Neutral values are `0` for Prefix and Postfix, and `1` for Multiplier. A 20% increase is stored as `1.2`; a 20% reduction is stored as `0.8`.

```text
(20 + 5 + 3) * 1.2 * 1.5 + 4 = 54.4
```

Within **one buff instance**, Weapon/Damage multiplier contributions are first combined as `1 + sum(m - 1)`. The resulting factor then participates in the global formula. Atoms do not turn a single buff into several independent buffs.

### Five-hit sample

`Assets/Game/Features/Buffs/Configs/FiveHitPower.asset` is assigned to both heroes, with independent runtime instances:

- Initially grants **+20% weapon damage**.
- After five valid outgoing hits, activates **+1 projectile** and another **+10% damage within the same buff**.
- Its damage factor becomes **1.3**, not `1.2 * 1.1`.
- A separate buff with a `1.5` factor still multiplies independently: `20 * 1.3 * 1.5 = 39`.

See [buff configuration, APIs, and lifecycle rules](Assets/Game/Features/Buffs/README.md) for extension details.

## Project layout

```text
Assets/
  Game/
    Features/       Gameplay systems, including Worlds, Weapons, and Buffs
    Presentation/   Shared presentation code
    Editor/         Scene setup and editor utilities
  Scenes/           Playable scenes, including Main.unity
Packages/           Unity package manifests and lock file
ProjectSettings/    Versioned Unity project settings
Tools/              Custom verification runners outside game builds
```

### Extension rules

- Keep shared configuration separate from mutable per-hero or per-buff state.
- Spawn world-local objects under their source world's content root. Use `World.GetFor(component)` and `World.GetContentRoot(component)` rather than assigning ownership from whichever world happens to be active.
- Use the owning world's hero for world-local gameplay. Existing player singleton properties are compatibility aliases for the active hero, not shared state storage.
- Preserve the distinction between world suspension and normal disabling: world sleep retains buffs, while ordinary disable clears them.
- Keep stat descriptions in atoms and evaluation order in calculators. Weapon callers should use the controller's calculation methods.

## Scene merging

Main and DebugRun retain upstream's `Grid/Real World`, `Grid/Mirror World` and shared `Bounding Box` object IDs and internal hierarchy. `World.map` controls these external geometry roots while heroes, enemies and pickups remain under their own Content roots. Keep geometry-only scripts on maps; do not recreate map objects merely to rename or reorganize them.

The repository already selects `unityyamlmerge` in `.gitattributes` and uses Force Text serialization. Configure the installed Unity tool once per clone on Windows:

```powershell
./Tools/Configure-UnitySmartMerge.ps1 -Unity '<path-to-Editor/Unity.exe>'
```

This writes the machine-specific command to local `.git/config`, not a tracked file. It does not merge or select a conflict winner. Save your work before merging; review the resulting scenes, C# changes, asset references and gameplay parameters even if Smart Merge reports success. This does not change `main` or guarantee conflict-free future merges.

Run `python Tools/MapMergeChecks.py --self-test` for the pinned map-import baseline. It checks both scenes and local timing without launching Unity. After merging, also run `python Tools/MapMergeChecks.py --candidate Assets/Scenes/Main.unity` for structural/reference validation. The current main's whole-scene trial still exposes an upstream Shield component pointing at a missing local object, even though Smart Merge returns success; that weapon integration remains separate. Intentional subsequent map edits need review and an updated baseline. See [map migration notes](Assets/Scenes/MergeNotes.md) for preservation details.

## Verification

### Standalone damage checks

With the .NET 10 SDK installed, run from the repository root:

```sh
dotnet run --project Tools/DamageFormulaChecks/DamageFormulaChecks.csproj
```

This exercises the production damage formula without launching Unity.

### Unity runtime checks

The 82-check legacy boundary suite has passed; additional finale tests are being added. `Tools/TimedEventChecks` previously recorded 86 passes and 0 failures in Live checks across Main and DebugRun. The new Auto switch button checks have not been run in Unity. Fixtures start near timer boundaries with high HP and weapons disabled; this is not a full 15-second or eight-minute soak. Legacy results do not establish complete finale coverage.

The runners under `Tools/` are custom verification entry points, **not automatically discovered Unity Test Runner tests**.

| Runner | Coverage |
| --- | --- |
| `BuffRuntimeChecks.cs` and `BuffRuntimeChecks/` | Buff composition, stacks, lifetime, source grants, and stat calculations |
| `WeaponBuffChecks.cs` | Weapon snapshots, hit reporting, and buff integration |
| `BowDaggerIntegrationChecks.cs` | Bow/Dagger scene bindings, snapshots, hits, bounces, poison, and world suspension |
| `DualWorldChecks.cs` | Independent hero state, world ownership, switching guards, and presentation |
| `DualWorldTimingChecks.cs` | Real-frame physics, suspended lifetimes, and automatic switching |

These runners require compilation with the relevant production or Unity assemblies. See the [buff verification instructions](Assets/Game/Features/Buffs/README.md#verification) and [world verification instructions](Assets/Game/Features/Worlds/README.md#verification).

`Tools/BowDaggerIntegrationBatch.cs` contains staging and batch-execution instructions for the new weapon checks and existing weapon/world regression suites.

Use a **throwaway Play Mode session** for runtime checks: they mutate gameplay state. Synchronous weapon and world checks do not replace real-frame physics verification.

## Current scope

- World state is retained in memory, not saved to disk.
- World visuals use distinct upstream maps and hero sprites, with a temporary tint. Fusion currently settles on the entry map rather than a new combined layout.
- The source-owned buff API provides an integration point for equipment effects; it is not a complete inventory or weapon-set system.
- Only damage and projectile count currently use the shared weapon buff calculation path.

When adding or moving assets, keep their Unity `.meta` files alongside them. Do not commit generated folders such as `Library`, `Logs`, `Temp`, or `obj`. Third-party assets remain subject to their respective licenses.
