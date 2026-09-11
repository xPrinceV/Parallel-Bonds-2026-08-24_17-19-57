# Dual-world run

## Rules

- Material and Echo each own a hero, experience/level table, weapons/upgrades, Buff runtime and enemy spawner. Both heroes share current and maximum health for the run.
- Switching transfers only the outgoing hero's position to the incoming hero. It does not copy or refill shared health, XP, equipment or Buffs. The incoming rigidbody velocity is cleared.
- Before activation, WorldManager binds both PlayerHealth components to the initial hero's health storage. Its serialized maximum initializes the shared pool once per run. Later health/max writes through either component affect that same pool and refresh the active HUD; inactive heroes reject damage callbacks. Existing serialized health fields migrate through FormerlySerializedAs.
- The inactive world's Content is disabled, not destroyed or unloaded. Its enemies, XP pickups, projectiles, fire and remaining active-time lifetimes stay in memory until that world resumes. Normal gameplay expiry and distance-despawn rules still apply while active.
- This is run-local retention, not a disk save. Restarting the scene starts a new run. Currency, inventories and save-file persistence are not introduced.
- An upgrade selection or zero time scale blocks switching. A dead/disabled current hero cannot switch to revive through the other hero. The existing loss flow otherwise remains unchanged.

## Scene

`Main.unity` contains MaterialWorld/Content/MaterialPlayer and EchoWorld/Content/EchoPlayer, each with its own XP component and six weapon controllers. Pistol, Lantern and Lightning remain the starting weapons; Bow, Dagger and Scythe are configured as unassigned weapons for each hero. Titan is appended to each spawner's final wave without changing wave timing. Each Content also owns its own EnemySpawner and map. WorldManager, the 15-second StateSwitchController, camera and HUD remain shared.

The temporary World Ambient Overlay uses a noninteractive screen-space overlay Canvas at sorting order -100, below the HUD. Material is transparent; Echo has a blue tint (alpha 0.22). WorldFilter observes the committed world/fusion state in LateUpdate, easing toward AmbientColor over 0.45 seconds for a world change. Fusion entry/exit use a 0.6-second blue-gray pulse with 0.045 peak overlay strength before compositing. Durations, pulse tint and strength are serialized on WorldFilter; zero duration makes a change instant. Initial loading snaps to the correct tint without an entry pulse. The maps still use the same layout; the heroes use Jeff_0 and Jeff_1 respectively.

**Tools > Parallel Bonds > Configure Dual World Run** migrates the older shared-player Main scene through Undo. It transfers serialized XP settings, clones the hero and spawner with internal references remapped, and binds each weapon to its local Buff holder. It refuses partial setups and validates a completed setup without duplicating it. The menu marks the scene dirty but does not save it automatically. The former map-only setup and single-player Buff sample setup are not migration tools for this dual-player scene.

## Runtime boundaries

- `World.GetFor(component)` finds ownership by hierarchy; `World.GetContentRoot(component)` provides the parent for new runtime objects. Spawns inherit the source world, never the currently active global world.
- `World.Player` identifies the local hero and preserves source ownership for enemy death drops. `World.InteractionPlayer` selects that hero normally, or the fusion entry hero for targeting and pickup collection. Refresh this target when the mode changes rather than caching it only in Start.
- `World.CanInteract(source, target)` permits same-world interactions normally. Cross-world interactions require two active worlds owned by the same initialized, enabled fusion manager. Ownership and spawn parenting never change with this permission.
- `PlayerController.instance`, `PlayerHealth.instance` and `ExperienceLevelController.instance` remain compatibility aliases for the active hero. On activation they rebind; they are not shared state containers. Camera and HUD follow those aliases. New world-specific gameplay should use its owning World rather than cache an active-world alias.
- `World.SetWorldActive` explicitly suspends Buff holders before disabling Content. World sleep preserves handles, stack progress, activated effects and remaining duration. Ordinary disable still clears Buffs; destruction always clears them.
- Delayed destruction was replaced with active-time countdowns for lantern/enemy projectiles and VFX so they do not expire while asleep. Fire retains overlap contacts across world sleep to avoid an extra entry hit on wake.
- Damage numbers retain their source world even on the shared canvas; they hide and freeze their lifetime while it sleeps. The pool remains shared presentation infrastructure.

## Fusion

- `WorldManager.TryEnterFusion()` and `TryExitFusion()` implement the F-key debug toggle. They return false when unavailable, paused, selecting an upgrade, or dead. `IsFused` and `FusionPlayer` expose the current mode and entry hero.
- Both content roots run together while `CurrentWorld` remains the entry world. The normal switch timer pauses without resetting; `SwitchWorld()` rejects requests until fusion ends.
- The secondary hero remains enabled so its existing weapons and Buff runtime can run. Its body renderers and colliders are suppressed, movement input is ignored, and its position/facing follow the entry hero. Weapon children remain live; no weapon or buff instance is copied.
- The entry hero owns camera, HUD, level-up selection and collected XP. Both worlds' spawners and enemies follow it. Death drops stay under their source world's content, even when collected by the other hero.
- Exit restores secondary transform, body renderer/collider states and rigidbody state, then leaves its world asleep. Weapon upgrades, stack progress and timers accumulated during fusion remain with their original hero.
- Shared death forces cleanup and disables the entry hero; the other hero remains under sleeping content. Death is latched for the run, so direct health writes cannot revive it. Manager disable also forces cleanup, even during upgrade pause.
- If the secondary content hierarchy becomes invalid, cleanup falls back to disabling the captured content root and invalidates the manager rather than allowing two controllable heroes in a non-fused state.
- Fusion uses the entry sprite and a clear settled overlay. The brief tint transitions are presentation-only, pause with game time, and retarget from the visible color when interrupted. No objects, coroutines, camera motion, input locks or gameplay delays are added. Both map layouts and physics objects coexist without blending or collision-layer separation. There is no energy, cooldown, or dedicated fusion form yet.

## Verification

Run these outside Assets through in-memory compilation against the current game assembly:

- `Tools/DualWorldChecks.cs`: `Run()` requires a throwaway initialized Main Play session. It checks hero/state identity, position transfer, source handles, XP ownership, manual pickup callbacks, death drops, UI guards and filter/visual selection. It mutates runtime hero data and must not be used as a nondestructive inspection of a player's live run. It does not yield frames or claim real physics/timing coverage.
- `Tools/DualWorldTimingChecks.cs`: `Begin()` observes real frames/physics with a 45-second deadline and publishes its result in editor SessionState under `DualWorldTimingChecks.Result`. It checks fire entry, two seconds of world sleep, unchanged Buff/fire/VFX lifetimes, no duplicate entry on wake, resumed expiry, and a full natural 15-second automatic switch. Temporary test objects and observers are cleaned up on completion/failure. This also requires a throwaway Play session.

- `Tools/FusionChecks.cs` covers both entry worlds, real cross-world hits and pickups, source-owned weapons/buffs, cold activation, both live spawners and enemy retargeting, timer preservation, repeated toggles, shared death, manager disable and invalid-content cleanup.
- `Tools/WorldTransitionChecks.cs` verifies real-frame transition endpoints, intermediate colors, fusion pulses, pause/resume, interruption, disable/enable cleanup and rejected transitions without changing scene objects or HUD raycasting. The batch entry also samples the first four rendered-game update frames for startup tint; checks use color/state samples rather than screenshot-based visual review.
- `Tools/ScytheTitanChecks.cs` verifies the merged Scythe/Titan references, procedural swing motion, damage/count snapshots, hit deduplication, delayed attacks, active-time lifetimes and fusion interactions. Run it with `-Suites Merge`; Area UI coverage uses disposable runtime configuration.
- `Tools/Run-FusionChecks.ps1 -Unity <path-to-Unity.exe>` runs the merge, transition, fusion, cleanup, existing regression and timing groups in separate disposable Play sessions. Close Unity first. The script stages a temporary editor entry, bounds each process to 120 seconds, and removes the entry and its metadata afterward. Logs are written under `Logs/FusionChecks-*.log`. F-key dispatch is checked in source; physical keyboard input is not injected.

The Buff formula and source-grant regression suite remains separate. No world switch changes `(B + sum(P)) * product(M) + sum(Q)`.
