# Dual-world scene merge: 0475186 + ff2ce1d

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
