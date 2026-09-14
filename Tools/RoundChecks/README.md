# Completed residence checks

## Run contract

- The scene/default contract is a 90-second residence in scaled, eligible gameplay time. Warning remains 5 seconds and ordinary flip duration remains 0.8 seconds in the existing scene configuration.
- `timer` remains `[SerializeField, Min(0.1f)] private float timer = ResidenceDuration`, preserving Inspector/asset configuration and reflection fixtures. `ResidenceDuration` supplies the 90-second default, not a forced override: an asset still serialized with `timer: 30` runs 30-second residences until migrated. Active scenes require explicit migration to `timer: 90`; these checks do not edit scenes. The removed serialized `finaleStartTime: 480` value has no runtime effect.
- Counts start at zero. The initial world's residence earns credit at its first successful automatic departure, not on entry. Credit belongs to the outgoing world and saturates at three per world.
- With Material first, boundaries are M1 at 90, E1 at 180, M2 at 270, E2 at 360, M3 at 450, then E3/fusion at 540 seconds. Echo-first is symmetric. These are nominal active times, subject to existing frame timing.
- There are five ordinary world commits. The last residence holds its warning past the ordinary half-flip start, then calls the existing `RunStageController.TryStartFinale()` at zero remaining time. It does not flip back to Material first.
- The sixth credit is awarded only when finale entry succeeds. Rejection holds the boundary/warning for retry, without awarding credit or starting an ordinary flip. Successful entry is one-shot through existing finale guards.
- `WorldManager.BeginFinalFusion()` already provides irreversible fusion, cancels/disables the switch flow, and starts the existing fusion entrance. The existing completion callback, next-frame boss spawn, boss death, victory/defeat and stage guards remain in charge. No new manager hook is required.
- A manual requested switch never earns residence credit, even if it commits at a normal boundary or auto is toggled back on. It abandons the outgoing incomplete residence. Already-earned credit remains. Its commit resets `timerCounter` to the full configured interval, discarding any long-frame debt so the newly entered world must complete a full residence before automatic credit. Ordinary automatic commits retain the existing `timerCounter + timer` timing/carry behavior.
- Auto-off cancels an incomplete residence/warning and resets its timer. Accepted manual requests and already-started automatic flips finish; the latter still earn credit upon successful commit. Auto-off alone cannot start a finale, even after 480 or 540 seconds. Re-enabling begins a fresh full residence after cancellation.
- Pause/upgrade selection freezes progression. Explicit debug finale entry remains available early when its existing run/stage/health/pause guards permit it, including with auto-off. It does not fabricate residence credit.

## Status API and debug GUI

`StateSwitchController` exposes `ResidenceDuration`, `RequiredResidencesPerWorld`, `CompletedMaterialResidences`, `CompletedEchoResidences`, `RemainingResidences`, `IsFinalResidence`, `RemainingTime` and `RemainingUntilFinale`.

`RunStageController` forwards both counts and the remaining-time estimate and exposes `AutomaticFinaleEnabled`. `FinaleStartTime` is retained as the nominal 540-second scene/default duration for compatibility, **not** a scheduling deadline or a configured-interval estimate. `SwitchInterval` and `RemainingUntilFinale` respect the serialized timer. `ElapsedTime` is informational only.

`RemainingUntilFinale` estimates eligible active time assuming automatic alternation continues/resumes. It accounts for pending manual departure and uneven counts (an extra visit to an already-complete world may be necessary). It is not a promise that fusion will happen while auto is disabled or gameplay is paused. The debug run panel displays M/E credit and the active-time estimate, or `Auto fusion: Off`; the manual buttons explicitly say `(debug)`.

## Deterministic tests

From the repository root, with .NET SDK 10:

```sh
dotnet run --project Tools/RoundChecks/RoundChecks.csproj
```

No NuGet test framework is required. The project links the **actual, unmodified source** of both production controllers, rather than reimplementing the scheduler. `Stubs.cs` doubles Unity time, world commit/fusion services, players, spawning and scene services. `Program.cs` advances deterministic 0.125-second frames, plus explicit long frames. The fixture explicitly sets private `timer` to 90 through reflection (30 in the configurability case). It uses reflection only for controller configuration/lifecycle and observing the existing boss collection; it does not inject completed counts or finale elapsed time.

Covered scenarios:

1. Full nominal 540-second sequence, no finale at 480, five normal midpoints, held final warning, one-shot finale, next-frame/pause-aware boss spawn and boss death completion.
2. Echo-first initial residence.
3. Manual switch rejection/acceptance, no credit, pending estimate and accepted request finishing with auto-off.
4. Auto-off cancellation, retained completed credit, no wall-clock finale and full interval after resume.
5. Accepted automatic flip finishing/crediting after auto-off.
6. Failed automatic commit: no credit/event and full retry interval.
7. Pause and upgrade freezing residence/warning/flip and retaining request guards.
8. Final-boundary pause, rejected fusion with hook cancellation, successful retry, and cancelling/repeating the last residence.
9. Manual departure during final warning, uneven counts and no substitution of Material credit for Echo.
10. Explicit early debug finale, same/invalid/final stage guards, and shared versus legacy wave scheduling.
11. Player death at the final edge.
12. Long-frame midpoint clamp and direct final fusion.
13. Serialized/reflection-compatible timer, 90-second default, and honoring a configured 30-second interval.
14. Manual commit after slight/large frame overshoot, including an already-accepted flip: no credit, full timer reset, and no automatic credit until 90 seconds after entry.

Recorded result: **14 scenarios / 166 assertions passed**.

Production-reference compile:

```sh
dotnet build Assembly-CSharp.csproj --no-restore -v minimal
```

Recorded result: succeeded with 11 warnings in existing dependencies/unmodified source (assembly-version conflicts, deprecated Unity APIs and unused weapon fields); no compiler errors.

## Evidence boundary and live follow-up

These tests verify controller decisions and the call sequence against service doubles. They do **not** prove actual map commit, rendering, Unity lifecycle ordering, fusion irreversibility inside the real manager, or real boss prefab behavior. No batch Play Mode run was performed while existing Unity editors were open.

In a disposable Main and DebugRun Play session:

1. Observe M/E counts at the six boundaries above. At 480 seconds the run must still be in ordinary Echo survival. At 539.6 seconds there must be a warning but no normal flip; at 540 seconds enter fusion from Echo.
2. Confirm the real fusion entrance completes before exactly one Rift Lord spawns; no return to split worlds or earlier stages. Kill the boss and exercise restart.
3. Repeat with manual switches, auto-off during warning, auto-off during an accepted flip, pause/upgrade during the final warning, and explicit debug finale while auto-off. Confirm GUI counts and status refresh on reopen.
4. Exercise a real rejected world commit/fusion configuration in a throwaway runtime fixture; no credit until success.

Existing broad gates and older timing/finale checks are deliberately untouched and retained as historical checks. In particular, `Tools/DualWorldTimingChecks.cs` expects a 30-second interval and `Tools/RunFinaleChecks.cs` injects the obsolete 480-second deadline; those old scheduling assertions are not acceptance evidence for this contract. Updating those gates is outside this task's ownership.
