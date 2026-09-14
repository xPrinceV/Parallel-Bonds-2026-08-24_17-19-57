# Audio Checklist

Checkboxes track installed integration, not listening approval or complete native acceptance. Remaining validation is listed below.

## Weapons

- [x] Jeff pistol — one cue after successful non-empty emission; two clip variants.
- [x] Lantern — LanternFire (101), once after successful non-empty emission.
- [x] Lightning — once on the first actual hit per burst.
- [x] Bow — once after successful non-empty emission; native coverage outstanding.
- [ ] Aura — clip configured only; no controller exists.
- [x] Sniper — once after successful non-empty emission; not additionally enabled, native coverage outstanding.
- [x] Dagger — DaggerImpact (106) only, never an additional generic impact cue.
- [x] Scythe — once after successful non-empty emission; native coverage outstanding.

## BGM

`GameAudioEvents` is installed on `GameAudio.prefab`. Actual `WorldChanged` and run-stage `StateChanged` events drive music; fusion display changes are not subscribed to.

- [x] Real world — MusicMaterial follows the committed Material world.
- [x] Ruined world — MusicEcho follows the committed Echo world.
- [x] Boss fight — MusicBoss follows the boss state.
- [x] Victory — non-looping Victory plays through pause; defeat stops music.
- [x] Main Menu — MusicMenu (404), `Main Menu.ogg`, loops at 0.4 through the shared AudioService / Music mixer group. GameAudioEvents selects it only when its scene is named exactly `Main Menu` and no initialized active scene-local WorldManager is bound. Unknown scenes without a bound world do not start music.

AudioService itself does not autoload BGM; the enabled scene-local GameAudioEvents selects menu/gameplay music. No separate AudioManager or bare MenuBGM source is used. Walking and Teleport are not connected.

## Player and enemies

- [x] PlayerHurt — actual positive nonlethal health loss only.
- [x] PlayerDeath — once per shared player death; full-pool priority replaces one ordinary active/paused voice when necessary, within eight voices. The 0.5-second cooldown, one-voice cap and pause permission remain intact.
- [x] ProjectileHit — Bullet, Arrow, LanternProj and PhysicsBullet use the generic cue after finite positive health loss. Dagger uses only DaggerImpact; both IDs share ProjectileHit's **0.1-second cooldown / two-voice cap**, including paused voices. Selected clips/settings remain separate; rejected requests do not extend cooldowns or advance variants.
- [x] EnemyDeath — real death only, never disable/despawn.

## Assets and policy

- All **17 playable IDs / 18 distinct real clip references** are configured (excluding None; PistolFire has two variants). MusicMenu adds unused ID 404; the original 16 IDs and catalog mappings are preserved. The earlier package import supplied 9 new clips and reused 13 existing clips without duplicate imports or GUID changes.
- MusicMenu uses GUID `582f9e5104c314248b0967f199a1c303` (`Main Menu.ogg`). MusicEcho still uses GUID `f870f407e1d8e8441be05eaa87e8345c`, now named `Ruined World.wav`, not the menu track.
- LanternImpact → LanternFire (101) and DaggerSwing → DaggerImpact (106) preserve numeric serialization. Existing fireball-spawn and dagger-impact payloads/metadata match the reviewed package.
- LanternFire starts at 0.35 gain / 0.12 seconds / two voices. DaggerImpact uses 0.35 gain and the shared generic impact budget above.
- Other indexed sounds and direct PlaySfx retain full-pool rejection; direct calls gain no critical priority.
- Five music clips use Streaming with preload disabled (`loadType: 2`, `preloadAudioData: 0`), including the integration owner's corrected menu importer. Initial gains remain subject to listening review; see `Assets/Game/Features/Audio/README.md`.

## Current test contract (source updated; not yet run in Unity)

- `Tools/AudioCatalogChecks.cs` expects 17 IDs / 18 distinct clips, MusicMenu (404) on Music with loop / 0.4 gain / zero cooldown / one voice / pause permission, and the renamed Ruined World clip for MusicEcho. Existing policy and projectile source checks remain in place.
- `Tools/AudioServiceChecks.cs` and `Tools/AudioCatalogChecks.cs` each explicitly suspend a previously enabled menu GameAudioEvents before requiring idle sources. They restore it in Dispose/finally after restoring service/catalog/mixer state and time scale, including timeout/cancellation paths. An already-disabled or absent binder is left as-is and still requires idle sources. A running gameplay binder or broken menu startup is rejected rather than silenced to make a fixture pass. Synthetic source-idle assertions are not normal menu expectations.
- `Tools/AudioGameplayChecks.cs` observes real MusicMenu before either synthetic suite, after each suite restores the binder, and after production ReturnToMainMenu. Assertions cover enabled/unbound menu events, native looping samples and continuity, 0.4 gain, Music mixer routing, ten service-owned sources only, and no synthetic SFX left behind. The integration path never directly requests menu music to repair a failed hook.
- No Unity launch or native suite rerun was performed for this adaptation. Updated test source is not new native acceptance evidence.

## Recorded validation (historical, before main-UI/menu adaptation)

- Production compilation: **76 files, 0 errors / 84 warnings**. Deterministic weapon checks: **73 passed**, using test doubles rather than native audio/physics.
- Full native result: **433 passed / 1 failed** — gameplay **100/1**, service **71/0**, catalog **262/0**. The failure was the restart fixture incorrectly expecting Material instead of its captured Echo Play Mode snapshot, not a production audio bug.
- After correcting the expectation, a separate native **RestartOnly** follow-up passed **15/0**: Echo restored correctly, all ten old sources destroyed, ten fresh sources created and each new event subscription bound exactly once. The original full **433/1** result remains unchanged; this is not a new full-suite all-pass result.
- Every Unity launch emitted a known SearchDatabase exception. Latest follow-up: **exit 3, 1 known / 0 unexpected errors**. No clean-console claim.
- Evidence: `Library/AudioGameplayValidation/Acceptance.md` for the full run; `Library/AudioGameplayValidation/native04-DebugRun-Echo-RestartOnly/summary.json` for the later corrected follow-up, superseding the report's earlier correction-only status.

## Historical fixture limits

- The recorded **433/1** full run and **15/0** RestartOnly follow-up used the earlier scene/catalog version with 16 IDs / 17 clips and a quiet menu. They do not validate the merged menu music, imported UI layout, victory panel or new button wiring; their original evidence is unchanged.
- `AudioGameplayChecks.RestartOnly` still deliberately requires the unsaved **DebugRun/Echo** Play Mode snapshot. Its batch host sets that fixture in memory. It checks restart/audio lifecycle only, not the current full scene/menu flow. Full audio suites still target the named Main Menu/Main/DebugRun scenes and use controlled combat/finale fixtures, not an unmodified eight-minute run.
- `Tools/MenuFlowChecks.cs` still assumes one persistent button per GameOverManager method and requires those buttons to be under `gameOverUI`. Victory only asserts that Game Over is hidden; it does not inspect `victoryUI` or its buttons. Both result panels using the same callbacks require a separately authorized fixture update, not deletion of duplicate-button failures.
- `Tools/RunFinaleChecks.cs` fixes Main to `useSharedWaves=false`, DebugRun to `true`, finale time to 480 and boss HP to 300. Its existing terminal checks are not acceptance of the new victory UI wiring.
- `Tools/MainBaselineChecks.py` is a historical scene-preservation gate pinned to local `463de47` and upstream `402fc0f`, including document identities and non-allowlisted scene bytes. It is not a gate for the approved `119208b` + `41ff8e2` UI/menu merge. `Tools/MainBaselinePlayChecks.cs` also targets Main's specific two-world/timing fixture, not menu/victory UI acceptance. These files were not changed here.

## Remaining acceptance

- [ ] Rerun the adapted service/catalog/menu/gameplay suites against the final merged scenes in a disposable native session, including cancellation restoration and the enabled-binder menu checks before/after each synthetic suite.
- [ ] Validate Main/DebugRun victory/defeat panel exclusivity and both panels' actual button wiring after scene integration; older menu-flow results do not cover this.

- [ ] Human listening and mix review.
- [ ] Eight-minute soak.
- [ ] Standalone player build.
- [ ] Native Bow, Scythe, Sniper and Aura validation; Aura currently has no controller and Sniper was not additionally enabled.

Installed hooks and passing policy checks do not establish these outstanding results.
