# Shared audio

`GameAudio.prefab` is a scene-local root in Main Menu, Main and DebugRun. It contains `AudioService` and `GameAudioEvents`, using Unity AudioSource and AudioMixer without an additional audio package or AudioListener. The service does not autoload BGM; the event binder starts gameplay music and selects MusicMenu only in the scene named exactly `Main Menu` when no initialized active scene-local WorldManager is bound. Other scenes without a bound WorldManager do not start music.

## Configuration

- Two 2D music sources, routed to Music, for linear crossfades.
- Eight reusable 2D SFX sources, routed to SFX. Ordinary indexed and all direct-clip requests are rejected when all voices are occupied; no per-shot objects are created. Only indexed `PlayerDeath` may replace one ordinary active/paused voice when no usable free slot exists. It never replaces another PlayerDeath voice or bypasses its own cooldown/max-voice limit.
- Default music fade: 0.5 seconds. A zero-duration request changes immediately.
- Direct `PlaySfx(AudioClip)` calls retain a 0.04-second per-clip cooldown. Indexed calls use catalog cooldowns and voice limits; variants share their ID's budget. DaggerImpact and ProjectileHit additionally share ProjectileHit's budget. Rejected requests do not extend cooldowns.
- `GameAudio.mixer`: Master -> Music / SFX. Exposed parameters: MasterVolume, MusicVolume, SfxVolume. Initial gain is 0 dB.

Keep one active service per scene, outside sleeping world roots. Source references belong exclusively to the service. The prefab requires exactly two music and eight distinct SFX sources; duplicated or incomplete services disable themselves with a diagnostic.

## Calls

Call through an assigned AudioService reference or `AudioService.Instance` after initialization. `GameAudioCatalog.asset` maps stable `SoundId` values to channel, clip variants, volume, cooldown, voice limit, loop and pause policy. The prefab references this asset. Lookup is cached; runtime cooldowns, voice ownership and variant cursors stay in the service, not the asset. Duplicate IDs fail closed for that ID; unassigned or invalid entries return false without repeated combat logs.

```csharp
// Preferred gameplay call: the catalog owns the clip and playback policy.
audio.Play(SoundId.ProjectileHit);

// Direct-clip APIs remain available for previews and explicit playback.
audio.PlaySfx(clip, volume: 0.6f);
audio.PlayMusic(clip, loop: true, fadeDuration: 0.5f);
audio.StopMusic();
audio.StopSfx();

// Set volume after Start. Values are linear 0..1; zero maps to -80 dB.
audio.SetMasterVolume(0.8f);
audio.SetMusicVolume(0.5f);
audio.SetSfxVolume(0.7f);
```

Play methods return false for unavailable playback, invalid input or an exhausted SFX budget. Repeating the current music request does not restart it. A third track requested during a crossfade replaces the quieter music voice. Indexed variants rotate after accepted playback without consuming gameplay RNG. Indexed SFX may loop until stopped; none of the supplied SFX entries loop. Spatial effects and per-sound stop handles are not implemented.

## Installed gameplay hooks

- `GameAudioEvents` on the prefab subscribes to actual `WorldChanged` and run-stage `StateChanged` events. Committed Material/Echo worlds select MusicMaterial/MusicEcho; boss and victory states select MusicBoss/Victory; defeat stops music. It does not subscribe to fusion display changes, so alternating presentation faces do not switch BGM.
- Pistol, Lantern, Bow, Sniper and Scythe request one action cue after a successful non-empty emission. Lightning requests one cue on the first actual hit per burst. Sniper was not additionally enabled; AuraActivate is configured only because no Aura controller exists.
- PlayerHurt follows actual positive nonlethal health loss. Shared player death emits PlayerDeath once, even across both heroes or repeated death calls; it can preempt an ordinary voice at a full pool. EnemyDeath follows real death, never disable/despawn.
- Walking and Teleport are not connected. MusicMenu (404) uses `Main Menu.ogg`, GUID `582f9e5104c314248b0967f199a1c303`. MusicEcho (401) still uses GUID `f870f407e1d8e8441be05eaa87e8345c`, now named `Ruined World.wav`; it is not the menu track. All music goes through the existing AudioService and Music mixer group, never a separate AudioManager or bare menu AudioSource.

## Projectile impacts

Bullet, Arrow, LanternProj and PhysicsBullet request `ProjectileHit` (300, existing `enemy hit.wav`). Dagger requests **only `DaggerImpact`**, never both cues. All five paths require finite positive health loss after existing world filtering and hit deduplication, independent of a Buff source.

Both impact IDs share ProjectileHit's **0.1-second unscaled cooldown and two-voice cap**, including paused voices, within the global eight-voice limit. Each selected entry retains its own clip, gain, variants, loop and pause policy. Alternating IDs cannot bypass the budget; rejected requests neither extend cooldowns nor advance variants. Synthetic catalogs without the generic entry fall back to the selected entry's policy.

## Package configuration

`ParallelBondsSounds.unitypackage` supplied nine new assets, selectively imported under `Assets/Game Sounds/SFX/` with original GUIDs and provenance metadata. Thirteen existing clips were reused without moving or duplicating them. The archive itself is not part of the project.

All seventeen playable IDs now have assigned clips, with eighteen unique clip references (PistolFire has two variants). The original sixteen numeric IDs and their catalog entries are unchanged; MusicMenu adds the previously unused value 404. PistolFire rotates between Jeff Pistol and Pistol xtra under the same cooldown. `LanternImpact` was renamed to `LanternFire` (101), and `DaggerSwing` to `DaggerImpact` (106); numeric serialization is unchanged. Their package labels now match the intended spawn/impact semantics, not landing/swing feedback. Walking and Teleport remain imported for later use, without IDs or automatic playback.

Provenance was checked against `Library/SoundPackageReview/analysis.json` and `Library/SoundPackageSetup/imported.json` (the latter contains only the nine new imports). Package `SFX/Weapons/Lantern.wav` is the existing `Assets/Vampire Survival Assets/Audio/SFX/weapon - fireball spawn.wav`, GUID `a3e641690f752224d801b16969568587`. Package `SFX/Weapons/Dagger.wav` is the existing `weapon - dagger impact.wav` in that same directory, GUID `d686afed845a3c64fb3deaef2543fee2`. Both current WAV payloads and importer metadata match the package review exactly. No duplicate import or placeholder was needed; the other fourteen clip mappings are unchanged.

| ID | Initial volume | Cooldown | Max voices |
| --- | ---: | ---: | ---: |
| PistolFire | 0.25 | 0.08 s | 2 |
| LanternFire | 0.35 | 0.12 s | 2 |
| LightningStrike | 0.5 | 0.1 s | 2 |
| BowFire | 0.6 | 0.08 s | 2 |
| AuraActivate | 0.25 | 0.2 s | 1 |
| SniperFire | 0.25 | 0.15 s | 2 |
| DaggerImpact | 0.35 | 0.1 s | 2 |
| ScytheSwing | 0.45 | 0.1 s | 2 |
| PlayerHurt | 0.35 | 0.15 s | 1 |
| PlayerDeath | 0.5 | 0.5 s | 1 |
| ProjectileHit | 0.35 | 0.1 s | 2 |
| EnemyDeath | 0.4 | 0.15 s | 2 |

MusicMaterial, MusicEcho, MusicBoss and MusicMenu use volume 0.4 and loop; Victory uses 0.5 and does not loop. All five use the Music channel, zero cooldown, a catalog max-voice value of one and pause-allowed playback. Music's two-source crossfade controls concurrency rather than the SFX voice-limit field.

The main-branch filenames are GUID-preserving: MusicMaterial -> `Real World.ogg` (`314eae84901f88a4186210c5bdac8c93`), MusicEcho -> `Ruined World.wav` (`f870f407e1d8e8441be05eaa87e8345c`), MusicBoss -> `Boss.ogg` (`70346df061647e44e9f3b1a6a78c99d0`), Victory -> `Victory.ogg` (`b3ce68ecc2cbc6a4ea64fd247c6c9206`), and MusicMenu -> `Main Menu.ogg` (`582f9e5104c314248b0967f199a1c303`). The four original catalog music assets retain Streaming (`loadType: 2`) with preload disabled. At merge-adaptation inspection, the menu importer still has `loadType: 0` and `preloadAudioData: 1`; the main integration owner must apply Streaming with preload disabled to this external `.meta`. This audio adaptation does not modify importer metadata outside this directory.

Scene integration belongs to the scene owner: remove the bare MenuBGM and inactive AudioManager tree, install the existing GameAudio prefab while retaining the menu UI layout, and wire Main/DebugRun result panels and buttons. The shared GameOverManager keeps its existing run binding and adds an optional serialized `victoryUI`; completion shows victory, defeat shows Game Over, and active runs hide both. It does not change run state or pause; Restart/MainMenu/Quit retain their formal paths. Old scenes can leave `victoryUI` null.

These are conservative starting mix values, not listening-approved settings. DaggerImpact's effective cooldown and voice cap come from ProjectileHit, not an independent weapon budget. Preserve Asset Store provenance and confirm redistribution rights before publishing standalone source audio.

## Pause and lifecycle

- Ordinary SFX pause when Time.timeScale becomes zero and retain their occupied voices. New ordinary requests are rejected until gameplay resumes.
- Use `Play(SoundId.PlayerDeath)` for the configured death cue: it is allowed at zero time scale and can preempt one ordinary voice if necessary. Preemption preserves the victim's SoundId cooldown and clears its native paused playback/ownership before reuse. Direct `PlaySfx(clip, playWhilePaused: true)` permits pause playback but has no priority, even for the same death clip. Music and its fades continue using unscaled time.
- Disabled active or paused sources release their reservations on the next voice update. Stopped/completed pause-allowed voices are also cleaned during pause; ordinary paused voices remain reserved until resumed, preempted, disabled or explicitly cleared by StopSfx.
- The sources ignore AudioListener.pause; time-scale pause is handled by the service. Listener mute and mixer gains still apply.
- Disable and scene unload stop playback and release the singleton. No DontDestroyOnLoad object or cross-scene music continuity is installed. Mixer volume choices apply to the shared mixer during the session; settings persistence is not implemented.
- Projectile impact requests inherit existing projectile world filtering. Active sounds remain shared 2D playback; outgoing-world sound cancellation is not provided by the service. `GameAudioEvents` binds/unbinds with scene lifecycle. Do not play from generic OnDisable/OnDestroy callbacks.

## Verification

Historical validation before the menu-music/main-UI adaptation (unchanged evidence, not validation of the current merge):

| Check | Result |
| --- | --- |
| Production compilation, 76 files | 0 errors, 84 warnings |
| Deterministic weapon checks | 73 passed; test doubles, not native coverage |
| Full native gameplay checks | 100 passed / 1 failed |
| Native AudioServiceChecks | 71 passed / 0 failed |
| Native AudioCatalogChecks | 262 passed / 0 failed |
| Full native total | **433 passed / 1 failed** |
| Corrected native RestartOnly follow-up | **15 passed / 0 failed**, separate targeted run |

The full-run failure was an incorrect restart-test expectation of Material instead of the captured unsaved Echo Play Mode snapshot, not a production audio bug. After correcting the test, RestartOnly verified Echo restoration, destruction of all ten outgoing sources, ten fresh sources and exactly one fresh subscription to each event. This follow-up does **not** rewrite the original 433/1 result as a full all-pass run.

Every Unity launch emitted a known Editor SearchDatabase exception. The latest follow-up exited **3**, with **1 known / 0 unexpected errors**; this is not a clean-console acceptance.

Native evidence covers actual world/stage music transitions without fusion-display BGM changes, victory completion, defeat, quiet menu return, Pistol/Lantern/Lightning action cues, Dagger-only impact, player damage/death and enemy death. The catalog suite confirmed 16/16 IDs and 17 distinct real clip references, shared impact cooldown/cap in both orders, critical preemption and cleanup. Service/catalog policy probes used generated clips and temporary catalogs; they are not additional gameplay collision coverage.

Evidence: `Library/AudioGameplayValidation/Acceptance.md` records the full run; `Library/AudioGameplayValidation/native04-DebugRun-Echo-RestartOnly/summary.json` records the later corrected restart follow-up and supersedes the report's earlier correction-only status.

The current menu adaptation has not been run in Unity. The historical 16-ID/17-clip counts and quiet-menu results above describe the prior version, not the current 17-ID/18-clip catalog. Within the rename-assertion-only test-edit scope, no obsolete clip/path assertions were found in AudioCatalogChecks, AudioGameplayChecks or AudioServiceChecks, so these test sources were left unchanged. Before a future native run, the test owner must update AudioCatalogChecks' configured counts, replace AudioGameplayChecks' quiet-menu expectations with MusicMenu playback checks, and explicitly suspend/restore GameAudioEvents around the synthetic service/catalog fixtures, which still require an idle service. Do not treat those idle preconditions as gameplay menu expectations.

Still unverified: human listening/mix review, an eight-minute soak, a standalone player build, and native Bow/Scythe/Sniper/Aura coverage. Aura has no controller, and Sniper was not additionally enabled. These results do not claim complete end-to-end acceptance.
