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

Main Menu stays silent. AudioService does not autoload BGM. Walking and Teleport are not connected.

## Player and enemies

- [x] PlayerHurt — actual positive nonlethal health loss only.
- [x] PlayerDeath — once per shared player death; full-pool priority replaces one ordinary active/paused voice when necessary, within eight voices. The 0.5-second cooldown, one-voice cap and pause permission remain intact.
- [x] ProjectileHit — Bullet, Arrow, LanternProj and PhysicsBullet use the generic cue after finite positive health loss. Dagger uses only DaggerImpact; both IDs share ProjectileHit's **0.1-second cooldown / two-voice cap**, including paused voices. Selected clips/settings remain separate; rejected requests do not extend cooldowns or advance variants.
- [x] EnemyDeath — real death only, never disable/despawn.

## Assets and policy

- All **16 IDs / 17 distinct real clip references** are configured. The package supplied 9 new clips and reused 13 existing clips without duplicate imports or GUID changes.
- LanternImpact → LanternFire (101) and DaggerSwing → DaggerImpact (106) preserve numeric serialization. Existing fireball-spawn and dagger-impact payloads/metadata match the reviewed package.
- LanternFire starts at 0.35 gain / 0.12 seconds / two voices. DaggerImpact uses 0.35 gain and the shared generic impact budget above.
- Other indexed sounds and direct PlaySfx retain full-pool rejection; direct calls gain no critical priority.
- Four music clips use Streaming with preload disabled. Initial gains remain subject to listening review; see `Assets/Game/Features/Audio/README.md`.

## Recorded validation

- Production compilation: **76 files, 0 errors / 84 warnings**. Deterministic weapon checks: **73 passed**, using test doubles rather than native audio/physics.
- Full native result: **433 passed / 1 failed** — gameplay **100/1**, service **71/0**, catalog **262/0**. The failure was the restart fixture incorrectly expecting Material instead of its captured Echo Play Mode snapshot, not a production audio bug.
- After correcting the expectation, a separate native **RestartOnly** follow-up passed **15/0**: Echo restored correctly, all ten old sources destroyed, ten fresh sources created and each new event subscription bound exactly once. The original full **433/1** result remains unchanged; this is not a new full-suite all-pass result.
- Every Unity launch emitted a known SearchDatabase exception. Latest follow-up: **exit 3, 1 known / 0 unexpected errors**. No clean-console claim.
- Evidence: `Library/AudioGameplayValidation/Acceptance.md` for the full run; `Library/AudioGameplayValidation/native04-DebugRun-Echo-RestartOnly/summary.json` for the later corrected follow-up, superseding the report's earlier correction-only status.

## Remaining acceptance

- [ ] Human listening and mix review.
- [ ] Eight-minute soak.
- [ ] Standalone player build.
- [ ] Native Bow, Scythe, Sniper and Aura validation; Aura currently has no controller and Sniper was not additionally enabled.

Installed hooks and passing policy checks do not establish these outstanding results.
