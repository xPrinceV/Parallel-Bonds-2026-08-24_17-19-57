# Experience checks

Executable deterministic .NET checks, following `Tools/RoundChecks`. Requires the
.NET 10 SDK; no Unity installation or third-party test packages are needed.

From the repository root:

```sh
dotnet run --project Tools/ExperienceChecks/ExperienceChecks.csproj --configuration Release
```

The project compiles these **actual working-tree files** via `Compile` links:

- `Assets/Game/Features/Progression/ExperienceLevelController.cs`
- `Assets/Game/Presentation/UI/LevelUpSelectionButton.cs`

There are no copied XP formulas, controller implementations, or button selection
implementations. Expected XP/level/remainder values are explicit test fixtures.
Each scenario resets singletons, time, and random choices. `Random.Range` takes
the first legal index unless a test scripts another; unscaled time advances only
when a test sets it. No sleeps, rendering, physics, or wall-clock timing.

## Coverage

| Scenario | Assertions | What it checks |
| --- | ---: | --- |
| CumulativeMerge | 7 | Cumulative 17 + 7 = 24, level 4, remainder 0; unchanged source; presentation; no merge choices |
| EntryCurvePolicy | 5 | Both entry directions with different curves; entry curve and cap govern conversion |
| SuppliedTableCap | 6 | `levelCount - 1` limits initialization, earned levels, merge, and rollback even with a longer supplied table |
| MergeIdempotency | 5 | Null/self merges, unmatched/repeated end, repeated begin with same or different source |
| RollbackRetainsEarned | 9 | Borrowed XP returned, earned XP retained, repeated fusion cannot duplicate either |
| LongExcessAndIntOverflow | 8 | Long totals up to 15,032,385,529; clamped visible int remainder; conserved hidden excess and rollback |
| GeneratedCurveOverflow | 6 | Generated costs saturate at `int.MaxValue`; table ownership and large threshold/cap traversal |
| MultipleThresholdsThroughButtons | 9 | `GetExp(24)` drains three thresholds through actual `SelectUpgrade`; reuse of buttons and source weapon; pause/resume |
| EarningsWhilePending | 4 | Further XP does not replace a pending panel; all three choices are consumed |
| FusionSecondarySelection | 8 | Forwarded secondary XP; source-owned secondary authorized by fused inventory and upgraded in place |
| StaleChoicesAndUnscaledDebounce | 9 | Selection versions, stale sibling event, immediate double click, 0.199/0.2 and 0.399/0.4 unscaled boundaries, late completion |
| ForcedFusionEndRebuildsChoices | 13 | Borrowed XP rollback; pending earned choice rebuilt from changed current inventory; old secondary choice rejected; repeat fusion inert |
| DeathCancellationDoesNotResume | 7 | Pending and accepted selection cancellation; stale clicks/completion cannot resume paused time |
| SelectionAuthorization | 6 | Noncurrent hero, hidden panel, removed weapon, changed owner, disabled controller; rejection preserves choice |
| NonpositiveAndCap | 5 | Zero/negative ignored before/during selection; long excess at cap cannot generate repeated choices |
| MissingSelectionUi | 12 | Missing UI, panel, button array, all-null buttons, empty inventory, no eligible upgrades |

`ShowUpgradeSelection` does not clone choices: it refreshes the existing real
buttons with original weapon references. A detached **real** button captures an
old panel version to test a stale sibling event after visible buttons have been
refreshed. The instantiation stub throws if any tested path tries to instantiate.

## Boundary of these checks

`Stubs.cs` supplies only the required Unity/UI, weapon data, player inventory,
and world services. The fusion-aware player inventory is a contract double;
`WorldManager` and `PlayerController` are **not** linked. The fixture explicitly
models the inspected `WorldManager.EndFusion` ordering: end borrowed XP, clear
fusion inventory/state, rebind if alive, then reconcile/cancel selection.
Death is tested by passing cancellation to that real reconciliation API, not by
simulating health or executing Unity death events. These are controller/button
regressions, not substitutes for native fusion lifecycle or PlayMode checks.

## Recorded result

2026-09-14, .NET SDK 10.0.400, Release:

```text
EXPERIENCE_CHECKS PASS: 16 scenarios, 119 assertions, 0 failures
```

Every scenario prints its assertion count; failures print a diagnostic and the
process returns nonzero. This is separate evidence against the currently linked
post-review source, not a reuse of the earlier native 357-assertion runs.
All harness files and generated `bin`/`obj` output stay in this folder; generated
output is ignored by its local `.gitignore`.
