# Buff composition and stack activation

## Configuration

Create a recipe with **Assets > Create > Parallel Bonds > Buffs > Buff**.
The inspector's **Add Atom** menu creates the selected atom and exposes its parameters.

- Timed recipes require `Duration > 0`.
- Enable `Is Permanent` for effects without expiration; zero duration alone does not mean permanent.
- A recipe must contain at least one valid atom.
- A stack atom defaults to `HitStackCondition` and `GrantBuffActivation`. Assign the target Buff asset under Activation.
- Configuration resources contain no remaining time or current stack count.

## Minimal example

1. Create a reward recipe with a `DamageBuffAtom`, multiplier `1.5`, duration `10`.
2. Create a starter recipe with `StackToActivationBuffAtom`, stack count `5`, target recipe set to the reward.
3. Make the starter permanent if it should keep listening throughout the holder's lifetime.
4. Attach `BuffController` to the holder and add the starter to `Initial Buffs`.
5. After a confirmed outgoing hit, call `holderBuffs.ReportHit(target, actualDamageDealt)` exactly once, before destroying the target.
6. Consumers obtain current stat modifiers through `holderBuffs.GetModifiers()`. For damage, pass them to `DamageCalculator.CalculateDamage(weaponDamage, modifiers)`.

The current weapon and enemy scripts are not automatically rewired by this feature. A projectile should retain its firing holder reference rather than report itself as the buff owner. The hit condition rejects other sources, self-hits, missing targets, zero/negative damage, NaN and infinity. One reported hit adds one stack; the reporter is responsible for not reporting duplicate collision callbacks.

## Runtime rules

- One active instance per BuffDefinition per controller. Regrant refreshes duration, preserves stack progress and does not duplicate stat modifiers.
- Threshold consumption supports consuming the required count, clearing all stacks, or triggering once per instance.
- A failed grant retains stacks. Further valid hits continue accumulating them, saturating only at `int.MaxValue`.
- At most one activation occurs per matching event; surplus stacks carry to the next matching event.
- Event dispatch uses a snapshot. Newly granted buffs do not process the event that created them; removed buffs stop processing.
- Reentrant event dispatch is rejected. Granting a buff does not automatically publish another hit event.
- Time uses `Time.deltaTime`, so upgrade pauses also pause buff duration.
- Disabling the holder/controller ends all its buffs in this version. Re-enabling does not re-run Start or restore initial buffs.
- Removing a recipe removes its contribution rather than reversing arithmetic on weapon/player values.
- The core does not yet implement target capability/allowedBuffs filtering, player stat application, or weapon attack integration.

`BuffDefinition` is now a ScriptableObject recipe. The old unused `Strength` and `isDestroyed` properties have been replaced by per-atom values and `BuffInstance.IsActive`. Existing Type/Duration are read-only configuration accessors.

## Verification

`Tools/BuffRuntimeChecks.cs` remains the standalone entry point outside Assets. Compile it **together with every `.cs` file in `Tools/BuffRuntimeChecks/`**, the Buffs sources and DamageCalculator into the same in-memory assembly using Unity's compiler APIs. Keeping the runtime sources in the same assembly permits testing internal stack event processing. Do not compile only the entry-point file.

- `BuffRuntimeChecks.Run()` runs all 26 checks.
- `RunStackChecks()` runs 11 stack and activation checks.
- `RunLifecycleChecks()` runs 7 lifetime and configuration checks.
- `RunControllerChecks()` runs 8 holder integration checks.

`BuffCheckContext` owns assertions, recipe construction, reflection helpers and temporary objects. Each group has fresh state and disposes its objects even when an assertion fails. Holders are destroyed before the recipe objects they reference. Reflection failures identify the missing field or method instead of producing an unrelated null-reference error.

In Edit Mode, the disable lifecycle hook is invoked explicitly; these checks do not replace Play Mode lifecycle or combat integration tests.
