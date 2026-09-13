# Weapon integration

Merge: local `0475186` + upstream `ff2ce1d`, base `8b999a4`.

## Integration decisions

- Keep local Buff damage/count snapshots and actual health-loss hit reporting.
  Bow and Lantern use `floor(amount + stats.amount)` before count buffs; Pistol
  uses `floor(stats.amount)`. Scythe retains one base swing plus count buffs, not
  `stats.amount` swings. Existing damage, speed, range, and cooldown formulas stay.
- Retain upstream `Weapon.player` and `weaponIcon` for serialized/API compatibility.
  Bow, Pistol, Scythe, Sniper, and Shield resolve the hierarchy owner first, then a
  same-world explicit owner or source `World.Player`. No attack uses the globally
  current player. Bow/Scythe no longer hide the base `player` field.
- Preserve `World.CanInteract`, including its fusion rules. New attacks are parented
  to the firing weapon's `World.ContentRoot`, never the target/current world's root.
- Keep the latest reusable `WeaponQuery` unchanged. Sniper uses its per-owner
  result/scratch lists. Its projectile also reuses a growing cast-result list.
- Arrow still travels straight and pierces distinct enemies for six active seconds;
  Pistol bullets still home. Both use trigger/kinematic prefabs, not dynamic bodies
  competing with transform movement. Compatible upstream component-based enemy
  detection is adopted, including child colliders and local world filtering.
- Sniper adds straight, non-homing shots. Each volley uses Pistol-style count buffs
  and damage snapshots. `PhysicsBullet` casts its collider along each fixed-time
  step, selects the nearest living interactable enemy, then places its kinematic
  body. This avoids tunnelling at its nominal 973 units/s and avoids the project's
  100 units/s physics translation cap. No transform/dynamic-body or impulse mix.
  The projectile lasts five active seconds and deals one knockback hit. Non-enemy
  geometry and non-interactable enemies do not consume or deflect the shot.
- Shield retains evenly spaced orbits, speed scaling, active duration scaling, and
  a fixed cooldown. Count and damage buffs are snapshotted per activation. Each
  shield reports one hit per enemy contact episode (multiple child colliders do
  not multiply damage); exiting all contacts permits a later orbit re-hit.
  The pivot lives under the source Content root and follows the owning player.
  Weapon/world disable hides it and pauses the timer instead of destroying it.
  On resume, the pivot remains inactive until the weapon update positions it at
  its owner. One transform sync then permits exact collider-distance checks before
  orbit movement resumes. Separated, disabled, destroyed, dead, and non-interactable
  contacts are removed; continuing overlaps remain to prevent duplicate wake hits.
  Retaining any overlapping collider for an enemy preserves multi-collider suppression.
  Reconciliation reuses its removal buffer and does not sync transforms every frame.
- PhysicsBullet and ShieldOrbitController implement existing `IWorldProjectile`.
  World/encounter cleanup invokes `Despawn`; shield teardown notifies its controller
  and clears the pivot when the last shield retires. Ordinary controller destruction
  also cleans up. Effects deactivate immediately and currently destroy, matching
  the local no-pool convention. A future pool replaces destruction inside Despawn
  and calls Initialize on each checkout, NOT OnEnable/world wake. Shield effects
  detach before pivot cleanup so a pool return can survive it.
- Scythe keeps the local code-driven, synchronized visual/hitbox sweep and active-
  time lifetime. Animator is disabled in the prefab and in Awake; current upstream
  visual object/controller references remain, but do not drive gameplay. Local
  offset 0, duration 0.4, angle -225, hitbox x -0.08, and visual alignment are kept.

## Upstream gameplay intentionally not adopted

- Bow's multiplicative `amount * stats.amount` count change and Pistol's single-shot
  path that bypasses local count/damage buffs.
- Global/single-player shield following and unscoped projectile instantiation.
- Dynamic Rigidbody2D impulses for Arrow/Bullet/Sniper, pistol's new non-homing
  flight, physical bullet deflection, and owner-collider ignoring as a substitute
  for world filtering. Trigger-based attacks cannot physically push the player.
- Wall-clock delayed Destroy for projectiles/swings, or destroying/recreating
  shields on world disable. These would advance/reset dormant-world attacks.
- Tag-only hit checks, repeated multi-collider damage, missing actual-damage Buff
  reporting, and upstream APIs that remove the local projectile setters.
- Scythe's Animator-driven motion, +225 sweep, 90-degree offset, 0.45 duration, and
  changed hitbox/visual alignment. These conflict with local swing geometry/balance.
- Old duplicate scripts: the five modify/delete remnants at
  `Assets/Scripts/Weapons/{ArrowController,BulletController,LanternController,
  ScytheController,ScytheHitController}.cs` were deleted only after inspection and
  migration. The feature-local implementations/metas remain authoritative.

## Scene wiring

All paths below are relative to `Assets/`.

1. Put each weapon beneath its owning PlayerController under that world's Content
   root. `player` may be left unassigned when parented correctly. Do not assign the
   other world's player/BuffController, including for fusion. Optional `buffHolder`
   resolves from the owner/parent when unset. World.CanInteract alone controls
   temporary cross-world targeting; ownership never transfers with the target.
2. Initialize WeaponStats multipliers to 1 and configure upgrade arrays, weapon
   names/levels and both `icon` (existing upgrade UI) and `weaponIcon` (upstream
   inventory UI). The two icon fields remain separate; no UI migration is done here.
3. Sniper (`Game/Features/Weapons/Controllers/SniperController.cs`):
   - `bullet` -> `Prefabs/PhysicsBullet.prefab`.
   - Existing upstream Main scene values were attackSpeed **0.2**, attackDamage
     **20**, attackRange **15**. These are scene values, not new C# defaults;
     unconfigured float fields otherwise start at zero.
   - New `projectileSpeed` defaults to **973** (scaled by stats.speed), and
     `projectileSpacing` to **0.15**. Count = floor(stats.amount), then count buffs.
   - PhysicsBullet prefab: kinematic Rigidbody2D, gravity 0, trigger CircleCollider2D
     radius 0.5 (upstream size retained), lifetime **5**. It has no SpriteRenderer
     in upstream; visual/tracer polish is not added by this merge.
4. Shield (`Game/Features/Weapons/Controllers/ShieldController.cs`):
   - `shieldPrefab` -> `Prefabs/Shield.prefab`.
   - Leave runtime `shieldPivot` null. Do not wire a scene pivot/shared player root.
   - Explicitly configure `orbitDistance`, `orbitSpeed`, `damage`, `duration`,
     `cooldown`; upstream code leaves these floats at **0** and upstream Main has
     no ShieldController instance. Positive duration is required for useful play.
     No new shield balance values were invented. Count = floor(stats.amount), then
     count buffs; duration scales with stats.duration; cooldown remains fixed.
   - Keep Shield's trigger collider and transform-driven orbit; do not add a dynamic
     Rigidbody2D. Enemies must have physics bodies for trigger callbacks, as for the
     existing local melee effects.
5. Bow/Pistol continue to use `Prefabs/Arrow.prefab` / `Prefabs/Bullet.prefab`.
   Keep their kinematic zero-gravity bodies and trigger colliders; their existing
   controllers own movement. Do not reintroduce upstream playerCollider/target YAML.
6. Scythe uses `Prefabs/Scythe.prefab` with the assigned hitboxPivot and
   animationTransform siblings; leave Animator disabled. The migrated new visual
   IDs are already referenced by the prefab. Existing local count rules apply.
7. Preserve projectile layer 8 and enemy collision/trigger visibility in the scene.
   No ProjectSettings, scene, enemy, player, UI, or animation assets were edited.
   PhysicsBullet's sweep uses DefaultRaycastLayers and includes enemy triggers;
   it then applies living-enemy and World.CanInteract checks.

### Retained upstream script GUIDs

| Class | Feature-relative destination | GUID |
| --- | --- | --- |
| SniperController | Controllers/SniperController.cs | 5f509c35341f6404fa102572d64f1a92 |
| ShieldController | Controllers/ShieldController.cs | 94e42513f263d434f844addfd7eb21c4 |
| PhysicsBullet | Projectiles/PhysicsBullet.cs | 5f29fe2ef866fe64991d4bedccfd7e25 |
| ShieldOrbitController | Effects/ShieldOrbitController.cs | a953f777e20a9e14897d0a283fc174da |

PhysicsBullet and ShieldOrbitController were moved together with their original
metas from Controllers. Their old Controllers copies must not coexist with the
migrated files. Both new prefab metas were preserved, not regenerated.

## Validation

Performed static checks: scoped git diff --check; unique definitions for all nine
integrated/migrated classes; empty old weapon script directory; all four upstream
script meta contents retained; all five prefab internal fileID references resolve
and have unique anchors; Arrow/Bullet/PhysicsBullet bodies are kinematic and all
five attack colliders are triggers. No new singleton attack ownership or allocating
OverlapCircleAll calls. WeaponQuery and protected Config files/metas are unchanged.

Shield resume regression coverage is in `Tools/ShieldContactChecks.cs` (repository-
relative path). Compile it outside Assets against the current runtime and Unity
assemblies, then invoke `ShieldContactChecks.Run()` synchronously on Unity's main
thread in Play Mode. It checks stale sleep records, continuing overlap, partial
multi-collider separation, invalidated colliders/enemies, owner-only disable, and
position changes after activation. It uses actual collider-distance queries with
explicit trigger/update calls; it is not a real-frame fusion/event-order test.

All 69 current runtime C# sources and the regression fixture compiled successfully
with Roslyn against the installed Unity 6000.4.6f1 references. Compiler warnings
include serialized-field/obsolete-API warnings; the fixture deliberately disables
and restores the deprecated autoSyncTransforms setting to exercise manual sync.
The fixture has not been executed in Unity. Standalone compilation does not verify
Unity import, player builds, or Play Mode geometry/lifecycle behavior.

Runtime follow-up: both worlds and fusion targeting; mid-flight/swing/orbit sleep
and resume with unchanged remaining lifetime; Sniper high-speed/initial-overlap
hits; lethal/overkill Buff reporting exactly once; Shield multi-collider re-entry;
zero/count-buff volleys; cleanup while active and asleep; Scythe visual alignment.
