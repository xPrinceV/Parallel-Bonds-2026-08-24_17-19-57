# Buff composition and stack activation

## Configuration

Create a recipe with **Assets > Create > Parallel Bonds > Buffs > Buff**.
The inspector's **Add Atom** menu creates the selected atom and exposes its parameters.

- Timed recipes require `Duration > 0`.
- Enable `Is Permanent` for effects without expiration; zero duration alone does not mean permanent.
- A recipe must contain at least one valid atom.
- A stack atom defaults to `HitStackCondition` and `GrantBuffActivation`. Assign the target Buff asset under Activation.
- Configuration resources contain no remaining time or current stack count.

## Internal five-hit upgrade (B runtime)

Use one recipe containing:

- A baseline `DamageBuffAtom` with `damageMultiplier = 1.2`.
- A `StackToActivationBuffAtom` with `stackCount = 5`, `HitStackCondition`, and `consumeMode = TriggerOnce`.
- Set that stack atom's activation to `ActivateBuffEffects`. Its `[SerializeReference] private List<BuffAtom> effects` contains a `DamageBuffAtom` with `damageMultiplier = 1.1` and a `ProjectileBuffAtom` with `projectileCount = 1`.

Before the fifth valid outgoing hit the recipe resolves to damage multiplier `1.2`. On the fifth hit it resolves to `1.3`, with one extra projectile, once per instance. With base damage `20`, an independent `1.5` damage Buff then gives `39`. No separate reward recipe is granted by this action.

`effects` must be non-null and nonempty. Each effect must derive from `StatBuffAtom` and pass its `isValid` check. The abstract `[Serializable] StatBuffAtom : BuffAtom` identifies numeric effects; `DamageBuffAtom`, `ProjectileBuffAtom`, `HealthBuffAtom`, `ArmorBuffAtom` and `StatsBuffAtom` now inherit it. Future numeric atoms and subclasses are accepted through this contract without changing activation code. Implementations must validate their numeric configuration and export stat modifiers without side effects. Null entries, invalid numeric configuration and non-stat atoms (including `StackToActivationBuffAtom`) are rejected before any nested stack validation.

`BuffAtom`, its existing API, the `List<BuffAtom>` effects field and all existing concrete atom fields remain unchanged. The new intermediate base class adds no serialized fields; existing multiplier fields still hold actual multipliers.

The Inspector provides **Use Internal Effects**, **Use Independent Buff Grant**, and **Add Internal Effect** for stack atoms. Internal effects are selected from concrete `StatBuffAtom` types. Changing an action type explicitly replaces that action's configuration.

### Main scene sample

`Assets/Game/Features/Buffs/Configs/FiveHitPower.asset` is a permanent sample assigned to the shared Player's `BuffController` in `Main.unity`. The scene pistol references that holder. **Parallel Bonds > Buffs > Configure Five Hit Sample** can configure an open scene with one PlayerController and a child pistol; it preserves existing initial buffs and does not overwrite an existing sample asset. Scene edits support Undo and are not automatically saved by the menu.

`PistolController` snapshots damage and projectile count once per volley. Damage starts from `attackDamage * stats.damage`; base count is the nonnegative floor of the existing `stats.amount` (normally one). Thus existing pistol Amount upgrades now affect firing. Extra projectiles use a small symmetric offset, with no change for a single projectile. Bullet damage is fixed at firing, so the fifth hit activates the bonus for later volleys rather than retroactively changing that hit or other in-flight bullets.

`BulletController` retains the firing holder and reports one positive confirmed hit after subtracting health, before deferred target destruction. Overkill is capped to the target's remaining health for the event. A consumed-projectile guard prevents duplicate collider callbacks; already-dead or inactive targets are ignored. A lost or inactive homing target ends the projectile. No BuffController is created automatically by combat code.

Lantern and Lightning are intentionally unchanged: they do not consume these buffs or report hit stacks. The sample's hits are accumulated across this holder's reporting pistol projectiles, not distinct enemies or volleys. The sample activates once per instance and does not expire automatically; removing it also removes its internal bonus. Disabling the holder still ends all buffs, as before.

## Independent grant example (preserved)

1. Create a reward recipe with a `DamageBuffAtom`, multiplier `1.5`, duration `10`.
2. Create a starter recipe with `StackToActivationBuffAtom`, stack count `5`, target recipe set to the reward.
3. Make the starter permanent if it should keep listening throughout the holder's lifetime.
4. Attach `BuffController` to the holder and add the starter to `Initial Buffs`.
5. After a confirmed outgoing hit, call `holderBuffs.ReportHit(target, actualDamageDealt)` exactly once, before destroying the target.
6. Before an attack, call `holderBuffs.CalculateWeaponDamage(weaponDamage)` to obtain damage with current buffs. Pass damage before buffs, including any weapon-owned upgrades already applied; do not feed a previously buffed result back into this method.

The pistol projectile now retains its firing holder reference and reports confirmed hits. Other attack implementations must also retain their holder rather than report the projectile object as the owner. The hit condition rejects other sources, self-hits, missing targets, zero/negative damage, NaN and infinity. One reported hit adds one stack; the reporter is responsible for not reporting duplicate collision callbacks.

## Calling the holder

Use the existing holder reference rather than searching the scene or inspecting its atoms:

```csharp
bool granted = holderBuffs.TryAddBuff(definition);
float attackDamage = holderBuffs.CalculateWeaponDamage(weaponDamage);
int projectileCount = holderBuffs.CalculateProjectileCount(baseProjectileCount);
// report the confirmed result once, before destroying the target
holderBuffs.ReportHit(target, actualDamageDealt);
bool removed = holderBuffs.RemoveBuff(definition);
```

The damage entry point delegates to `DamageCalculator`; it does not apply damage, publish a hit, mutate weapon stats, or add another formula. Empty or disabled holders contribute no modifiers, so finite input damage is returned unchanged. Invalid input still raises the calculator's exception. The caller must provide an existing `BuffController` reference; this API does not create one automatically.

Attack callers need only `CalculateWeaponDamage` and `CalculateProjectileCount`, not atom inspection. `CalculateProjectileCount(int baseCount)` evaluates only Weapon/ProjectileCount modifiers as `(baseCount + sum(Prefix)) * product(Multiplier) + sum(Postfix)`, floors the result, then clamps to a minimum of zero. Negative base counts, nonfinite matching modifiers and undefined matching stages throw `ArgumentOutOfRangeException`; nonfinite intermediate/final arithmetic or a floored result above `int.MaxValue` throws `OverflowException`. Double intermediates preserve `int.MaxValue` for an unmodified base count. Empty or disabled controllers return nonnegative base counts unchanged.

`GetModifiers()` remains available for lower-level consumers and inspection. Existing APIs, independent reward activation and the world-disable lifecycle are preserved. The pistol uses these entry points; other production weapon controllers still require explicit integration.

## Runtime rules

- One active instance per BuffDefinition per controller. Regrant refreshes duration, preserves stack progress and does not duplicate stat modifiers.
- `TryAddBuff` constructs a new instance before publishing it. An `ArgumentException` during construction (including combined Damage multiplier overflow despite individually finite atoms) returns `false`, leaving existing buffs unchanged. Other exception types propagate rather than hiding programming errors. Initial-buff startup logs the rejected configuration through its existing failure path and continues to later entries.
- Threshold consumption supports consuming the required count, clearing all stacks, or triggering once per instance.
- A failed grant retains stacks. Further valid hits continue accumulating them, saturating only at `int.MaxValue`.
- Internal effects belong to the source `BuffInstance`: contributions are copied as modifier values, not written into the recipe. Activation identity is deduplicated per instance. Refresh preserves activated effects; removal, expiration or disable ends them with the source, and a later new instance starts fresh.
- Internal activation resolves all candidate contributions before committing. Missing/mismatched source context, duplicate actions or nonfinite combined damage multipliers fail without consuming stacks or publishing partial effects. Invalid action configuration preserves existing stacks and ignores events until valid again, matching existing stack validation behavior. Use `TriggerOnce` for the five-hit recipe; deduplication prevents repeat contributions even under other consume modes.
- `BuffActivationContext.SourceInstance` and the three-argument constructor carry the source instance. The old two-argument constructor remains valid for `GrantBuffActivation`; internal activation requires a source. `StackBuffInstance.ProcessEvent` accepts an optional fourth instance argument; `BuffInstance` supplies itself automatically.
- At most one activation occurs per matching event; surplus stacks carry to the next matching event.
- Event dispatch uses a snapshot. Newly granted buffs do not process the event that created them; removed buffs stop processing.
- Reentrant event dispatch is rejected. Granting a buff does not automatically publish another hit event.
- Time uses `Time.deltaTime`, so upgrade pauses also pause buff duration.
- Disabling the holder/controller ends all its buffs in this version. Re-enabling does not re-run Start or restore initial buffs.
- Removing a recipe removes its contribution rather than reversing arithmetic on weapon/player values.
- The core does not yet implement target capability/allowedBuffs filtering, player stat application, world-based attack isolation, or integration for weapons other than the pistol.

`BuffDefinition` is now a ScriptableObject recipe. The old unused `Strength` and `isDestroyed` properties have been replaced by per-atom values and `BuffInstance.IsActive`. Existing Type/Duration are read-only configuration accessors.

## Verification

`Tools/BuffRuntimeChecks.cs` remains the standalone entry point outside Assets. Compile it **together with every `.cs` file in `Tools/BuffRuntimeChecks/`**, the Buffs sources and DamageCalculator into the same in-memory assembly using Unity's compiler APIs. Keeping the runtime sources in the same assembly permits testing internal stack event processing. Do not compile only the entry-point file.

- `BuffRuntimeChecks.Run()` runs all 100 checks (the original 45 plus 55 new checks).
- `RunStackChecks()` runs 11 stack and activation checks.
- `RunLifecycleChecks()` runs 7 lifetime and configuration checks.
- `RunControllerChecks()` runs 14 holder integration checks, including the simplified damage entry point.
- `RunDamageChecks()` runs 13 damage formula checks without creating Unity objects.
- `RunInternalActivationChecks()` runs 38 internal activation, isolation, lifecycle, validation/retry, per-Buff aggregation, extensible stat atom contract and construction-failure checks.
- `RunProjectileCountChecks()` runs 17 count formula, filtering, boundary and disabled-holder checks.

The formula group can also run independently with the .NET 10 SDK, without Unity or external test packages:

```sh
dotnet run --project Tools/DamageFormulaChecks/DamageFormulaChecks.csproj
```

This runner compiles the production `DamageCalculator` and `StatModifier` sources, not copies. It verifies `(B + sum(P)) * product(M) + sum(Q)`, including the `54.4` example, neutral values, independent multipliers, reductions, calculation stages and unrelated stat filtering. Float comparisons use a small tolerance.

## Migration: same-Buff Damage multipliers

`BuffInstance` now exports exactly one Weapon/Damage Multiplier when damage multipliers are present, resolved as `1 + sum(m - 1)` across baseline and activated contributions (including Damage-targeted `StatsBuffAtom` multipliers). Prefix and Postfix contributions retain their original stages and additive evaluation. All other properties' multipliers remain separate with their existing multiplicative behavior. `DamageCalculator` and its global formula are unchanged: independent Buff multipliers still multiply.

This is a behavior change for existing recipes containing multiple Weapon/Damage multipliers: `1.2` and `1.1` in one Buff now resolve to `1.3`, not `1.32`. Review such recipes for balance. Every serialized multiplier field remains an actual multiplier with neutral value `1`; do **not** migrate `1.2` to `0.2`. Recipes with one Damage multiplier retain their behavior. To retain independent multiplication, keep effects in distinct Buff definitions (the existing `GrantBuffActivation` remains supported).

The original 13 calculator checks remain calculator-input tests; the new runtime groups cover five-hit internal activation and aggregation through holders. They verify count calculation, not projectile spawning or combat wiring. Combat integration must additionally be verified in Play Mode using actual projectile movement and collisions.

`BuffCheckContext` owns assertions, recipe construction, reflection helpers and temporary objects. Each group has fresh state and disposes its objects even when an assertion fails. Holders are destroyed before the recipe objects they reference. Reflection failures identify the missing field or method instead of producing an unrelated null-reference error.

In Edit Mode, the disable lifecycle hook is invoked explicitly; these checks do not replace Play Mode lifecycle or combat integration tests.
