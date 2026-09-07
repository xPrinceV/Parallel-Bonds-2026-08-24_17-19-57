# Parallel Bonds

A Unity 2D survival prototype built around two alternating worlds, independent heroes, and composable weapon buffs.

The current playable scene includes three automatic weapons, experience-based upgrades, and a five-hit buff activation sample. The gameplay architecture described here is available on the `WIP-Chen-dual-world` branch.

## Getting started

### Requirements

- **Unity 6000.4.6f1**, installed through Unity Hub.
- Git to clone the repository.
- **.NET 10 SDK** only if you want to run the standalone damage formula checks.

Unity restores the project's dependencies from `Packages/manifest.json` and `Packages/packages-lock.json`. The project uses Universal Render Pipeline, Unity 2D tooling, and Unity UI.

### Open and play

```sh
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
| Switch worlds | Automatic, every 15 seconds of gameplay time |

## Dual-world gameplay

**Material** and **Echo** each own a hero, enemy spawner, and world content. The camera and HUD follow the active hero.

- Health, experience, levels, weapon upgrades, and buff runtime state belong to each hero independently.
- Switching copies only the outgoing hero's position to the incoming hero and clears the incoming velocity.
- The inactive world's content is disabled, not destroyed or unloaded. Its enemies, experience pickups, projectiles, and fire remain in memory.
- World-local active-time countdowns and buffs pause while that world sleeps and resume when it becomes active again.
- Upgrade selection and a zero time scale block switching. A dead or disabled hero cannot switch to bypass the loss flow.
- Attacks, enemy targeting, and experience collection respect world ownership.

Material currently has no screen tint; Echo uses a temporary blue overlay below the HUD. The heroes have different sprites, but both maps still share the same layout.

State retention lasts **only for the current run**. Restarting the scene does not restore the previous run from disk.

See [dual-world rules and runtime boundaries](Assets/Game/Features/Worlds/README.md) for details.

## Weapons

| Weapon | Behavior |
| --- | --- |
| Pistol | Fires homing projectiles |
| Lantern | Throws projectiles that create damaging fire on impact |
| Lightning | Strikes randomly selected live targets |

All three weapons consume buff-modified **damage** and **projectile count**. For Lightning, projectile count controls the number of strikes. Attack speed, range, projectile speed, and duration still use the existing weapon upgrade logic rather than general buff evaluation.

Damage and count are snapshotted for each volley or burst. A buff activated by a hit affects future attacks, not projectiles already in flight.

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

## Verification

### Standalone damage checks

With the .NET 10 SDK installed, run from the repository root:

```sh
dotnet run --project Tools/DamageFormulaChecks/DamageFormulaChecks.csproj
```

This exercises the production damage formula without launching Unity.

### Unity runtime checks

The runners under `Tools/` are custom verification entry points, **not automatically discovered Unity Test Runner tests**.

| Runner | Coverage |
| --- | --- |
| `BuffRuntimeChecks.cs` and `BuffRuntimeChecks/` | Buff composition, stacks, lifetime, source grants, and stat calculations |
| `WeaponBuffChecks.cs` | Weapon snapshots, hit reporting, and buff integration |
| `DualWorldChecks.cs` | Independent hero state, world ownership, switching guards, and presentation |
| `DualWorldTimingChecks.cs` | Real-frame physics, suspended lifetimes, and automatic switching |

These runners require compilation with the relevant production or Unity assemblies. See the [buff verification instructions](Assets/Game/Features/Buffs/README.md#verification) and [world verification instructions](Assets/Game/Features/Worlds/README.md#verification).

Use a **throwaway Play Mode session** for runtime checks: they mutate gameplay state. Synchronous weapon and world checks do not replace real-frame physics verification.

## Current scope

- World state is retained in memory, not saved to disk.
- World visuals are provisional: matching map layouts, distinct hero sprites, and a temporary tint.
- The source-owned buff API provides an integration point for equipment effects; it is not a complete inventory or weapon-set system.
- Only damage and projectile count currently use the shared weapon buff calculation path.

When adding or moving assets, keep their Unity `.meta` files alongside them. Do not commit generated folders such as `Library`, `Logs`, `Temp`, or `obj`. Third-party assets remain subject to their respective licenses.
