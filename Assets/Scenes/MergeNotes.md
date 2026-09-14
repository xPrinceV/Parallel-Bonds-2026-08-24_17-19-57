# Dual-world scene merge: 0475186 + ff2ce1d

> Historical record: the original merge decisions and validation below are preserved. The **43a1348 scene-map compatibility migration** at the end supersedes the earlier decision to retain local maps and defer upstream map installation; it does not supersede the unrelated gameplay, UI, settings, font or weapon decisions.

## Enabled in Main and DebugRun

Both scenes retain their own HEAD hierarchy and authored debug controls. MaterialWorld and EchoWorld still have distinct heroes, experience, equipment and Buff controllers; WorldManager/PlayerHealth continue to bind shared health. Existing weapon tuning, upgrade arrays, ownership references and loadouts are unchanged.

- One active world switch controller: 15-second timer, 5-second warning, 0.8-second flip.
- WorldFlipPresentation uses the display camera (`914722973`), not a capture camera.
- Finale starts at 480 seconds, with the existing 3-second fusion entrance. The preserved RunStageController waits for entrance completion and a subsequent frame before spawning one RiftLordBoss at distance 6 with 300 health. The boss remains the local RiftLordBoss variant, not the incoming `Rift Lord.prefab`.
- Main retains `useSharedWaves: 0`; DebugRun retains `useSharedWaves: 1`.
- Local Grid/Tilemap documents and collision behavior are retained unchanged.

The upstream four-slot inventory is added to the existing shared UI Canvas in both scenes. Its root is now a bottom-right anchored RectTransform, with four 100-unit slots spaced 120 units apart at the upstream 0.75 scale. It renders below the level-up panel and does not intercept UI raycasts. Empty icons begin hidden; InventoryUI updates them from the active hero.

`InventoryUI` is wired using GUID `323dea301fe40e4468eccaedab0e9b9c`, now located at `Assets/Game/Presentation/UI/InventoryUI.cs`. Its adapted serialized contract is only `weaponIcons`, in this order:

1. `558673425`
2. `1974101963`
3. `943245635`
4. `2142785265`

No obsolete upstream `player` or `stateSwitchController` reference was transplanted. Existing weapons receive upstream inventory icons in both worlds. The pistol icon uses the incoming single-sprite fileID `21300000`; Lightning receives the incoming staff icon/name. The three level-up buttons reference the existing shared UIController. No script or weapon prefab was edited for this scene work.

## Incoming maps retained as reusable assets, NOT enabled

These standalone assets are not referenced or instantiated by either scene:

- `UpstreamRealWorldMap.prefab`: Grid / Real World / ground, Buildings, Decorations.
- `UpstreamMirrorWorldMap.prefab`: Grid / Mirror World / ground, Buildings, Decorations.
- `UpstreamMapBoundaries.prefab`: the upstream shared Bounding Box and its four boundary colliders.

The two map prefabs preserve upstream Grid settings, tile/sprite GUIDs and subasset references, transform offsets, layer-7 Buildings, TilemapCollider2D + CompositeCollider2D and static Rigidbody2D (`m_BodyType: 2`). Boundary geometry, layer 7 and offsets are preserved separately, not duplicated into both maps. Upstream collider exclusions of Enemy layer 6 (`m_Bits: 64`) are preserved in these deferred assets. Mirror World's authored inactive state is also preserved; this is not a drop-in enabled world replacement.

### Why the maps are not migrated into gameplay

`WorldManager.CommitWorldSwitch` directly copies the current hero's position to the destination hero, without a destination-solid safety test. `TryEnterFusionCore` enables the secondary content root and disables only secondary-hero nonweapon colliders, not its map colliders. The incoming layouts/collision shapes are not identical (Buildings contain 229 versus 230 tile entries, using different sprite geometry). Installing them would risk switching into solids and colliding with both maps during fusion. The local setup deliberately used identical maps as its safe starting point.

Before enabling the maps, the core owner must establish and implement destination-position safety and fusion map-collision ownership. This merge does not invent a teleport correction, remove buildings, or choose which world's collision should win. After that decision, map subtrees can be adapted into `MaterialWorld/Content/Grid` and `EchoWorld/Content/Grid`; boundary ownership must be explicit rather than doubled during fusion. Validate spawn/boss positions and enemy/projectile boundary behavior too.

## Project settings and fonts

- Retain incoming TagManager names: layer 6 Enemy, layer 7 Collision (Test), layer 8 Projectile. Existing layer numbers and sorting layers remain unchanged.
- Retain HEAD's complete Physics2D collision matrix and `useMultithreading: 0`. Do not adopt upstream suppression of Enemy-Enemy (6/6), boundary-Projectile (7/8), or Projectile-Projectile (8/8) contacts. This avoids silently changing crowd/projectile policy as existing prefabs move onto the named layers.
- Keep compatible Unity physics serialization fields, incoming WebGL texture-compression setting and new-script line-ending setting. Packages are untouched by this work.
- Resolve both Kenney Pixel SDF conflicts with intact HEAD dynamic font assets. Their source font, font/material/atlas fileIDs and asset GUIDs are unchanged; empty dynamic glyph caches regenerate from the same source font. No conflict-text/texture hybrid is retained. Unity rendering/glyph generation still needs runtime validation.

## Deferred weapon wiring

Sniper/Shield scripts and prefabs are adapted to local ownership, Buff and projectile lifecycle interfaces, but are not added to either hero's loadout. Upstream Sniper has incomplete icon/name/upgrades and Shields is an inactive bare Transform; neither is silently enabled here. Before enabling them, configure the owning hero, projectile/prefab references, upgrade arrays and desired loadout. See `../Game/Features/Weapons/INTEGRATION.md` for wiring details.

## Static validation and remaining acceptance

Static checks passed for both changed scenes and all three new prefabs:

- Unique YAML document IDs, resolved local fileIDs, component ownership, reciprocal transform links and root structure.
- HEAD comparison: each scene changes only 17 existing documents (shared Canvas GO/Transform, 12 weapon presentation documents and 3 level-up button references), plus 35 inventory documents. Other documents, including gameplay, maps and authored debug UI, are unchanged.
- Correct timing, finale values, boss variant, display-camera reference and Main/DebugRun shared-wave distinction.
- External dependency traversal across 65 YAML assets: no missing GUIDs or broken supported subasset references. Checks include sprite IDs, MonoScript IDs, font/material references and the RiftLordBoss inherited component ID. Input System-generated input-action subasset IDs require Unity importer validation; their scene references were not changed.
- No duplicate GUIDs across Assets, including the migrated scripts. Duplicate legacy script files have been removed.
- Both fonts match intact HEAD assets and have coherent atlas dimensions/data byte counts.

Standalone Unity-reference compilation does not validate Unity import, player builds or Play Mode. Required Unity acceptance: import all resources; verify four inventory icons through switching/fusion and level-up; inspect glyphs and HUD at multiple aspect ratios; verify shared health with separate XP/equipment/Buffs; test 15/5/0.8 switching, the 480-second entrance and single 300-health boss at distance 6; and regression-test existing crowd/weapon collisions.

The incoming `Player_Collision` prototype is not attached to either gameplay scene. It requires transition-aware safe-position handling and cached collision queries before use with the deferred maps. The separate upstream `Rift Lord.prefab` is also unused; it must not replace the configured finale boss.

## 43a1348 scene-map compatibility migration

### Source, scope and preservation

The actual scene documents come from `origin/main:Assets/Scenes/Main.unity` at `43a1348b46530ec5abd254bbf3115dda7aac7610`, not from the three extracted prefabs. Work stays on `WIP-Chen-dual-world`; no merge, commit or push is performed. Only `Main.unity`, `DebugRun.unity`, this note and `Tools/MapMergeChecks.py` belong to this scene task. Runtime code, settings/configs, other documentation, reusable map prefabs and their metadata are not edited by this task.

Pre-edit, uncommitted versions of both scenes, this note and all three reusable map prefabs are backed up in `Library/MapMergeCompatibility/pre-43a1348/`. This is the preservation baseline, **not HEAD**: local audio/menu and other existing scene changes must survive. The staged scenes were also backed up immediately before final wiring in `Library/MapMergeCompatibility/pre-wire-boundaryRoot/`. These ignored backups provide local preservation evidence; they are not prerequisites for default checks on a clean checkout.

In each scene, only the original seven-document `MaterialWorld/Content/Grid` subtree and seven-document `EchoWorld/Content/Grid` subtree are replaced. Both Content GameObjects survive unchanged; their transforms lose only their respective old Grid child reference. The upstream Grid and shared Bounding Box are added as scene roots, preserving upstream parentage, internal child order, offsets and active states. Real World is active; Mirror World retains its authored inactive state. Nothing is reparented into either Content root. This keeps the upstream map identities and hierarchy suitable for future Unity Smart Merge.

All 51 incoming Grid/map/boundary documents retain their upstream fileIDs. **Final wiring is complete:** 49 documents remain byte-identical upstream, and the only changes to the other two are one added WorldMap component-list entry on each Real/Mirror World GameObject. Tile/sprite GUIDs and subasset IDs, renderer materials, collider data/exclusions, static Rigidbody2D settings and all shared four-wall boundary documents remain exact. Two new WorldMap MonoBehaviour documents use the actual runtime script GUID.

The seven overlapping IDs are `1898056171`, `1898056172`, `1898056173`, `1982272472`, `1982272473`, `1982272474`, `1982272475`. All belong to the removed local Material map, so reusing them is safe. No incoming IDs collide with retained local documents. The checker rejects such collisions rather than renumbering upstream data. It also rejects retained references into removed maps, even if an incoming document would reuse the target ID.

### Final mapping and runtime contract

| Role | GameObject fileID | Transform fileID | Local World / attached WorldMap component |
| --- | --- | --- | --- |
| Shared Grid root | `1898056171` | `1898056173` | Grid component `1898056172` |
| Real World map | `51126211` | `51126212` | Material World `1546997187` -> WorldMap `4300134801` |
| Mirror World map | `1249588891` | `1249588892` | Echo World `1627129257` -> WorldMap `4300134802` |
| Shared Bounding Box root | `1054028103` | `1054028104` | One shared boundary, never duplicated |

Boundary wall collider IDs: Top `603671793`, Bottom `67443747`, Left `1769810518`, Right `1808044615`. These are four wall strips, **not** a filled playable-area collider. The new WorldMap IDs are deterministic and were checked unused in both scene baselines and the upstream maps.

**Both scenes are wired to the final runtime contract:** `[SerializeField] private Transform boundaryRoot` on `WorldMap`, and `[SerializeField] private WorldMap map` on `World`. WorldMap components `4300134801` and `4300134802` both serialize `boundaryRoot: {fileID: 1054028104}`. Their script GUID is `845f24de5b5041c9a462d80652f7e178`, verified from `Assets/Game/Features/Worlds/WorldMap.cs.meta`. Material World `map` references `4300134801`; Echo World `map` references `4300134802`. No unresolved references, placeholder fields or additional boundary collider geometry were introduced.

Historical staging blocker, now resolved: the first runtime draft accepted a single filled-region `Collider2D boundary`, incompatible with the four preserved wall strips. Scene wiring was deliberately withheld until the runtime owner changed the contract to `boundaryRoot`. The final runtime derives a conservative playable rectangle from the four existing axis-aligned BoxCollider2D wall shapes, clipping their common covered span rather than inventing a filled collider.

The following behavior is based on inspection of the runtime owner's code, **not a Play Mode result**:

- Normal switching asks the destination map for a safe position before committing either world's gameplay activation. The search checks the unchanged position first, then nearby 0.5-unit lattice offsets within 3 units, bounded by at most 169 lattice candidates and a 25 ms search budget. It checks a conservative hero footprint against the shared playable rectangle and destination-map solids, fails closed on ambiguous/full overlap results, and restores the destination map's prior active state after probing. A rejected search does not commit the switch.
- Fusion settles on the **entry world's map** for rendering/collision. During the entrance, the presentation component temporarily displays the other map with its physics disabled, then restores map/body/collider states after rendering. Secondary gameplay content and weapons remain independent of map activation. End-fusion cleanup restores the entry map and sleeps the captured secondary map. The shared Bounding Box remains one shared root throughout.
- Boss placement checks the prefab footprint against the entry map and boundaries before spawning. It tries the preferred 6-unit distance in different directions, then nearby radii within ±3 units. A hero touching a boundary does not invalidate a safe boss destination. Exhaustion uses the existing finale failure path; health and damage are unchanged.
- The scene task only supplies these references. Runtime files were not edited by this task, and the authored upstream map active states and geometry were not changed to implement these policies.

### Check commands and evidence

Run from the repository root (Python 3, no third-party YAML package):

- `python Tools/MapMergeChecks.py`: default structural acceptance of both fully wired scenes; no arguments, ignored backup or Unity package cache needed. The boundary field defaults to `boundaryRoot`.
- `python Tools/MapMergeChecks.py --self-test`: the same checks plus eight in-memory rejection tests and a CRLF checkout test, without a backup.
- `python Tools/MapMergeChecks.py --baseline Library/MapMergeCompatibility/pre-43a1348 --check-assets --self-test`: full local preservation evidence, all 17 rejection tests, and incoming asset GUID existence checks. This command passed after final scene writes.
- `python Tools/MapMergeChecks.py --analyze --baseline Library/MapMergeCompatibility/pre-43a1348`: read-only upstream roots, map identities and original collision analysis.
- `python Tools/MapMergeChecks.py --upstream origin/main`: explicitly compare against the current remote-tracking ref for investigation, rather than silently advancing the acceptance reference.

The actual final write command was `python Tools/MapMergeChecks.py --wire --boundary-field boundaryRoot --baseline Library/MapMergeCompatibility/pre-43a1348 --check-assets --self-test`. Do not rerun it on already-wired scenes: `--wire` requires the expected staged state. `--migrate` likewise requires scenes matching the original baseline byte-for-byte. Both write modes require an explicit `--baseline`, the personal branch and pinned upstream `43a1348`. Both complete candidates are validated before either is written, and current bytes are rechecked for concurrent edits.

Default comparisons are pinned to full commit `43a1348b46530ec5abd254bbf3115dda7aac7610`; they never consult a moving remote-tracking ref. `--upstream REF` explicitly overrides the source for read-only checks. That Git object must be available locally (including when using a shallow checkout); no command fetches, merges or creates commits.

Default validation proves document uniqueness, local references, ownership/hierarchy, map-subtree identities, absence of old Grid/Tilemap data, exact upstream map content aside from the two component-list additions, WorldMap script/boundary references, World/Content bindings and local timing. It tolerates only CRLF/LF checkout conversion when comparing map documents. It clearly reports that historical preservation was not checked unless `--baseline` is supplied. Requested but missing/incomplete backups fail rather than downgrading silently. With `--baseline`, all preservation comparisons and write preconditions remain byte-exact.

`--check-assets` is opt-in because the URP material lives in Unity's ignored package cache; run it after restoring packages. Without that flag, GUIDs/subasset IDs are still protected by map-document comparison, but asset existence/import is not claimed. The default command also passed a guarded run forbidding all Library reads/scans and any lookup of `origin/main`, without moving or hiding the user's backups.

Final structural and full preservation validation passed for both written scenes: unique document identities; every local fileID; reciprocal component ownership; transform parents/children, cycles and SceneRoots including stripped prefab transforms; exact map data with only the specified component additions; all 38 incoming asset GUIDs found in Assets/Packages/Library package cache; and complete preservation of all unrelated pre-edit documents. The URP Sprite-Lit-Default material GUID `a97c105638bdf8b4a8650670310a4cd3` resolves in the existing package cache, not Assets. The three reusable map prefabs compare byte-for-byte with their original backups.

Per-scene final delta against the original uncommitted backups: **46 added IDs, 7 removed IDs, 8 changed retained documents**. Main goes from 500 to **539** documents; DebugRun from 502 to **541**. Three changed retained documents are reused map documents; the only five changed nonmap documents are Content transforms `1068728230`, `2027904951`, World components `1546997187`, `1627129257`, and SceneRoots `9223372036854775807`. The other four overlapping map documents are byte-identical upstream. Of the 51 incoming map documents, 49 are exact and only the two Real/Mirror component lists differ as specified.

For historical comparison, the earlier staged gate had 44 added IDs, 7 removed IDs and 6 changed retained documents per scene. Final wiring alone adds two documents and changes exactly four staged documents: two local World references and the two incoming map GameObject component lists.

The checks retain every StateSwitchController's `timer: 15`, `warningDuration: 5`, `flipDuration: 0.8`, finale `480`, Main shared waves `0`, DebugRun shared waves `1`, and all unrelated assets/prefab references. The nine preservation rejection tests cover duplicate IDs, retained-ID collisions, stale references into reused map IDs, dangling local references, broken parent/child links, changed tiles, changed boundary geometry, unexpected World fields and unexpected SceneRoots data. Eight backup-independent rejection tests additionally exercise map content/identity, hierarchy/root entries, swapped World.map references, wrong shared-boundary binding and wrong script GUID; CRLF checkout acceptance is also tested.

No source Unity save or scene reserialization was performed. The Python check does not establish Unity import or Play Mode behavior. Separate native Play Mode checks and merge-driver tests are recorded in `Library/MapMergeCompatibility/RESULTS.md`; the ignored evidence is local, not shipped. Full combat/pathfinding and camera-edge presentation remain outside the controlled checks.

### Smart Merge configuration

`.gitattributes` already selects `unityyamlmerge`; Force Text serialization is already enabled. `Tools/Configure-UnitySmartMerge.ps1 -Unity <path-to-Editor/Unity.exe>` installs the driver in repository-local Git configuration, refusing to overwrite a different driver without `-ReplaceExisting`. It uses headless premerge with fallback tools disabled, so conflicts do not silently choose a winner or launch an editor. Installation paths are not committed.

Isolated UnityYAMLMerge tests accepted an independent upstream map rename while retaining local WorldMap bindings and rejected conflicting edits to the same name. For actual `ff2ce1d` / `43a1348` / working-scene inputs the tool returned 0 and retained local `15/5/0.8` timing, **but the output failed structural validation**: incoming ShieldController `441805113` references missing Shields GameObject `441805110`. The old upstream player reference also requires local ownership review. This is outside the map migration; no weapon was silently enabled to make the check pass.

Run `python Tools/MapMergeChecks.py --candidate <merged-scene-path>` after a merge. It checks object identity, local references and hierarchy independently of the pinned map baseline. The actual incoming-main candidate is deliberately rejected; a tool exit code of 0 is not sufficient acceptance. Outputs remain under Library; no Git merge or scene replacement was performed. Map compatibility is established, but whole-scene/whole-repository integration still needs explicit weapon/script conflict resolution.
