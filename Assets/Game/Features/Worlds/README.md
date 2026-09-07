# Dual-world run

## Rules

- Material and Echo each own a hero, health, experience/level table, weapons/upgrades, Buff runtime and enemy spawner.
- Switching transfers only the outgoing hero's position to the incoming hero. It does not copy health, XP, equipment or Buffs. The incoming rigidbody velocity is cleared.
- The inactive world's Content is disabled, not destroyed or unloaded. Its enemies, XP pickups, projectiles, fire and remaining active-time lifetimes stay in memory until that world resumes. Normal gameplay expiry and distance-despawn rules still apply while active.
- This is run-local retention, not a disk save. Restarting the scene starts a new run. Currency, inventories and save-file persistence are not introduced.
- An upgrade selection or zero time scale blocks switching. A dead/disabled current hero cannot switch to revive through the other hero. The existing loss flow otherwise remains unchanged.

## Scene

`Main.unity` contains MaterialWorld/Content/MaterialPlayer and EchoWorld/Content/EchoPlayer, each with its own XP component and three weapon controllers. Each Content also owns its own EnemySpawner and map. WorldManager, the 15-second StateSwitchController, camera and HUD remain shared.

The temporary World Ambient Overlay uses a noninteractive screen-space overlay Canvas at sorting order -100, below the HUD. Material is transparent; Echo has a blue tint (alpha 0.22). WorldFilter reads the current World's AmbientColor in LateUpdate. The maps still use the same layout; the heroes use Jeff_0 and Jeff_1 respectively.

**Tools > Parallel Bonds > Configure Dual World Run** migrates the older shared-player Main scene through Undo. It transfers serialized XP settings, clones the hero and spawner with internal references remapped, and binds each weapon to its local Buff holder. It refuses partial setups and validates a completed setup without duplicating it. The menu marks the scene dirty but does not save it automatically. The former map-only setup and single-player Buff sample setup are not migration tools for this dual-player scene.

## Runtime boundaries

- `World.GetFor(component)` finds ownership by hierarchy; `World.GetContentRoot(component)` provides the parent for new runtime objects. Spawns inherit the source world, never the currently active global world.
- `World.Player` identifies the local hero. Enemy targeting, XP attraction and enemy death drops use that hero. Attacks and pickup callbacks reject a different owning world.
- `PlayerController.instance`, `PlayerHealth.instance` and `ExperienceLevelController.instance` remain compatibility aliases for the active hero. On activation they rebind; they are not shared state containers. Camera and HUD follow those aliases. New world-specific gameplay should use its owning World rather than cache an active-world alias.
- `World.SetWorldActive` explicitly suspends Buff holders before disabling Content. World sleep preserves handles, stack progress, activated effects and remaining duration. Ordinary disable still clears Buffs; destruction always clears them.
- Delayed destruction was replaced with active-time countdowns for lantern/enemy projectiles and VFX so they do not expire while asleep. Fire retains overlap contacts across world sleep to avoid an extra entry hit on wake.
- Damage numbers retain their source world even on the shared canvas; they hide and freeze their lifetime while it sleeps. The pool remains shared presentation infrastructure.

## Verification

Run these outside Assets through in-memory compilation against the current game assembly:

- `Tools/DualWorldChecks.cs`: `Run()` requires a throwaway initialized Main Play session. It checks hero/state identity, position transfer, source handles, XP ownership, manual pickup callbacks, death drops, UI guards and filter/visual selection. It mutates runtime hero data and must not be used as a nondestructive inspection of a player's live run. It does not yield frames or claim real physics/timing coverage.
- `Tools/DualWorldTimingChecks.cs`: `Begin()` observes real frames/physics with a 45-second deadline and publishes its result in editor SessionState under `DualWorldTimingChecks.Result`. It checks fire entry, two seconds of world sleep, unchanged Buff/fire/VFX lifetimes, no duplicate entry on wake, resumed expiry, and a full natural 15-second automatic switch. Temporary test objects and observers are cleaned up on completion/failure. This also requires a throwaway Play session.

The Buff formula and source-grant regression suite remains separate. No world switch changes `(B + sum(P)) * product(M) + sum(Q)`.
