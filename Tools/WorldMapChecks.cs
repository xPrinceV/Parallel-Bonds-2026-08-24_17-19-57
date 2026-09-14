using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Compile outside Assets against current game/Unity assemblies. Run on Unity's main thread in a
// disposable, initialized Main or DebugRun Play session. Synchronous native queries, not simulated frames.
// RunLifecycle additionally performs real commits/fusion and intentionally leaves the manager disabled.
public static class WorldMapChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    static bool running;

    public static string Run()
    {
        Require(Application.isPlaying && !running, "Requires Play Mode on the main thread without reentry");
        WorldManager manager = Object.FindAnyObjectByType<WorldManager>();
        Require(manager != null && manager.IsInitialized && !manager.IsFused && !manager.IsWorldTransitioning,
            "Requires initialized, unfused scene between transitions");
        World current = manager.CurrentWorld;
        World target = ((World[])Get(manager, "worlds")).Single(w => w != current);
        Require(current.Player != null && target.Player != null && !target.IsActive, "Two heroes with secondary asleep");
        Passed = Failed = 0;
        running = true;
        Vector3 sourcePosition = current.Player.transform.position;
        Vector3 sourceScale = current.Player.transform.localScale;
        Vector3 targetScale = target.Player.transform.localScale;
        CircleCollider2D sourceCircle = current.Player.GetComponent<CircleCollider2D>();
        CircleCollider2D targetCircle = target.Player.GetComponent<CircleCollider2D>();
        Require(sourceCircle != null && targetCircle != null, "Current scene uses circle hero bodies");
        float sourceRadius = sourceCircle.radius, targetRadius = targetCircle.radius;
        Vector2 targetOffset = targetCircle.offset;
        var root = new GameObject("WorldMapChecks fixture");
        root.transform.position = new Vector3(10000, 10000, 0);
        GameObject boundaryObject = Make("Boundary", root.transform);
        BoxCollider2D left = Wall("Left", boundaryObject.transform, new Vector2(-8.5f, 0), new Vector2(1, 18));
        BoxCollider2D right = Wall("Right", boundaryObject.transform, new Vector2(8.5f, 0), new Vector2(1, 18));
        BoxCollider2D top = Wall("Top", boundaryObject.transform, new Vector2(0, 8.5f), new Vector2(18, 1));
        BoxCollider2D bottom = Wall("Bottom", boundaryObject.transform, new Vector2(0, -8.5f), new Vector2(18, 1));
        WorldMap map = Make("Target map", root.transform).AddComponent<WorldMap>();
        Set(map, "boundaryRoot", boundaryObject.transform);
        var solid = Make("Solid", map.transform).AddComponent<BoxCollider2D>();
        solid.size = Vector2.one;
        solid.enabled = false;
        map.SetMapActive(false);
        WorldMap savedMap = target.Map;
        try
        {
            Check(WorldMap.TryGetFootprint(current.Player, out Vector2 beforeOffset, out float beforeRadius)
                && WorldMap.TryGetFootprint(target.Player, out Vector2 beforeTargetOffset, out float beforeTargetRadius),
                "actual scene footprints readable without waking secondary");
            Debug.Log("WorldMapChecks current scale=" + sourceScale + " radius=" + sourceRadius
                + " footprint=" + beforeRadius + " offset=" + beforeOffset + "; target scale=" + targetScale
                + " radius=" + targetRadius + " offset=" + targetOffset);
            current.Player.transform.position = root.transform.position;
            current.Player.transform.localScale = target.Player.transform.localScale = Vector3.one;
            sourceCircle.radius = targetCircle.radius = 0.45f;
            targetCircle.offset = Vector2.zero;
            var state = new ProbeState(current, target);
            Vector3 origin = current.Player.transform.position;
            int objectsBefore = Resources.FindObjectsOfTypeAll<GameObject>().Length;
            Check(map.TryFindSafePosition(current.Player, target.Player, out Vector3 point) && point == origin,
                "clear destination preserves exact preferred position");
            Check(state.Same() && !map.gameObject.activeSelf, "successful probe restores map and every sampled gameplay state");
            Check(Resources.FindObjectsOfTypeAll<GameObject>().Length == objectsBefore,
                "probe creates no temporary geometry or GameObjects");
            map.transform.SetParent(target.ContentRoot, true);
            int sibling = map.transform.GetSiblingIndex();
            Vector3 local = map.transform.localPosition, localScale = map.transform.localScale;
            Quaternion rotation = map.transform.localRotation;
            Check(!map.TryFindSafePosition(current.Player, target.Player, out point)
                && map.transform.parent == target.ContentRoot && map.transform.GetSiblingIndex() == sibling
                && map.transform.localPosition == local && map.transform.localRotation == rotation
                && map.transform.localScale == localScale && !map.gameObject.activeSelf && state.Same(),
                "map under sleeping Content rejects without reparenting or waking gameplay");
            map.transform.SetParent(root.transform, true);
            solid.enabled = true;
            Check(map.TryFindSafePosition(current.Player, target.Player, out point)
                && point != origin && Vector2.Distance(point, origin) <= WorldMap.SearchRadius,
                "blocked destination finds a bounded nearby point");
            map.SetMapActive(true); Physics2D.SyncTransforms();
            Check(Vector2.Distance(solid.ClosestPoint(point), point) > 0.45f, "native target solid clearance covers body, not just center");
            map.SetMapActive(false);
            solid.size = new Vector2(30, 30);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Check(!map.TryFindSafePosition(current.Player, target.Player, out point), "fully blocked search cancels");
            Check(clock.ElapsedMilliseconds < 1000, "exhaustion terminates within external one-second guard");
            Check(state.Same() && !map.IsActive, "exhausted probe leaves sleeping player, Buffs, bodies and aliases unchanged");
            solid.enabled = false;
            current.Player.transform.position = origin + Vector3.right * 8;
            Check(!map.TryFindSafePosition(current.Player, target.Player, out point), "current footprint crossing shared bound is rejected");
            current.Player.transform.position = origin + Vector3.right * 20;
            Check(!map.TryFindSafePosition(current.Player, target.Player, out point), "outside origin never teleports back through the boundary");
            current.Player.transform.position = origin + Vector3.right * 7.4f;
            targetCircle.radius = 0.9f;
            Check(map.TryFindSafePosition(current.Player, target.Player, out point)
                && point.x + 0.92f <= origin.x + 8 && point.x != current.Player.transform.position.x,
                "larger target near wall moves inward without crossing boundary");
            current.Player.transform.position = origin;
            targetCircle.radius = 9;
            Check(!map.TryFindSafePosition(current.Player, target.Player, out point), "target larger than playable region is rejected");
            targetCircle.radius = 0.9f;
            solid.enabled = true;
            solid.size = new Vector2(0.2f, 8f);
            solid.transform.localPosition = Vector3.right * 0.8f;
            Check(map.TryFindSafePosition(current.Player, target.Player, out point) && point != origin,
                "larger incoming hero rejects a center that only the outgoing hero fits");
            targetCircle.radius = 0.45f;
            targetCircle.offset = Vector2.right * 0.8f;
            Check(map.TryFindSafePosition(current.Player, target.Player, out point) && point != origin,
                "incoming collider offset participates in native clearance");
            targetCircle.offset = Vector2.zero;
            solid.isTrigger = true;
            Check(map.TryFindSafePosition(current.Player, target.Player, out point) && point == origin, "map triggers do not block placement");
            solid.enabled = false;
            map.SetMapActive(true);
            Check(map.TryFindSafePosition(current.Player, target.Player, out point) && map.gameObject.activeSelf,
                "already active target map remains active after probe");
            map.SetMapActive(false);

            Check(map.TryGetPlayableRect(out Rect playable) && playable == new Rect(9992, 9992, 16, 16),
                "four offset walls define their inner world-space rectangle");
            boundaryObject.transform.localScale = new Vector3(2, 1, 1);
            Check(map.TryGetPlayableRect(out playable) && playable == new Rect(9984, 9992, 32, 16),
                "axis-aligned parent scale is applied to wall sizes and offsets");
            boundaryObject.transform.localScale = Vector3.one;
            left.enabled = false;
            Check(!map.IsConfigured && !map.TryFindSafePosition(current.Player, target.Player, out point) && state.Same(),
                "disabled wall rejects without activating map or gameplay");
            left.enabled = true;
            left.isTrigger = true;
            Check(!map.IsConfigured, "trigger wall is not a valid boundary");
            left.isTrigger = false;
            left.edgeRadius = 0.1f;
            Check(!map.IsConfigured, "rounded wall is rejected");
            left.edgeRadius = 0f;
            left.transform.localRotation = Quaternion.Euler(0, 0, 15);
            Check(!map.IsConfigured && !map.TryFindSafePosition(current.Player, target.Player, out point),
                "rotated wall rejects instead of using its enclosing AABB");
            left.transform.localRotation = Quaternion.identity;
            boundaryObject.transform.localRotation = Quaternion.Euler(0, 0, 10);
            Check(!map.IsConfigured, "rotated boundary parent is rejected");
            boundaryObject.transform.localRotation = Quaternion.identity;
            left.size = new Vector2(18, 1);
            Check(!map.IsConfigured, "three horizontal walls do not define four directional sides");
            left.size = new Vector2(1, 18);
            right.offset = left.offset;
            Check(!map.IsConfigured, "duplicate sides with no interior are rejected");
            right.offset = new Vector2(8.5f, 0);
            top.offset += Vector2.right * 40;
            Check(!map.IsConfigured, "disjoint wall spans are rejected");
            top.offset = new Vector2(0, 8.5f);
            bottom.gameObject.SetActive(false);
            Check(!map.IsConfigured, "inactive wall is rejected");
            bottom.gameObject.SetActive(true);
            top.transform.SetParent(root.transform, true);
            Check(!map.IsConfigured, "missing fourth wall is rejected");
            top.transform.SetParent(boundaryObject.transform, true);
            var extra = Wall("Extra", boundaryObject.transform, Vector2.zero, Vector2.one);
            Check(!map.IsConfigured, "extra boundary collider is rejected");
            extra.transform.SetParent(root.transform, true); extra.gameObject.SetActive(false);
            Check(map.IsConfigured && !map.IsActive && state.Same(), "valid four-wall fixture and gameplay state restored");
            TilemapGeometry(map, current, target, origin);
            BossPlacement(map, current, origin, solid);
            current.Player.transform.position = origin;
            targetCircle.radius = 0.45f;
            solid.enabled = true; solid.isTrigger = false; solid.size = new Vector2(30, 30);
            Set(target, "map", map);
            // Direct solver rejection has no gameplay activation; manager coverage follows separately.
            state = new ProbeState(current, target);
            Check(!map.TryFindSafePosition(current.Player, target.Player, out point) && state.Same(),
                "assigned external map still never activates target content to probe");
            Set(map, "boundaryRoot", null);
            Check(!map.TryFindSafePosition(current.Player, target.Player, out point) && !map.IsActive && state.Same(),
                "missing boundary fails closed without lifecycle changes");
        }
        finally
        {
            Set(target, "map", savedMap);
            current.Player.transform.position = sourcePosition;
            current.Player.transform.localScale = sourceScale;
            target.Player.transform.localScale = targetScale;
            sourceCircle.radius = sourceRadius; targetCircle.radius = targetRadius; targetCircle.offset = targetOffset;
            root.SetActive(false); Object.Destroy(root);
            Physics2D.SyncTransforms();
            running = false;
        }
        return "WorldMapChecks: " + Passed + " passed, " + Failed + " failed";
    }

    static void TilemapGeometry(WorldMap map, World current, World target, Vector3 origin)
    {
        GameObject grid = Make("Native tilemap grid", map.transform);
        grid.AddComponent<Grid>();
        GameObject tilesObject = Make("Native composite tiles", grid.transform);
        Tilemap tiles = tilesObject.AddComponent<Tilemap>();
        Rigidbody2D body = tilesObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;
        CompositeCollider2D composite = tilesObject.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        composite.generationType = CompositeCollider2D.GenerationType.Manual;
        TilemapCollider2D tileCollider = tilesObject.AddComponent<TilemapCollider2D>();
        tileCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 2, 2), Vector2.one * 0.5f, 2f);
        tile.sprite = sprite;
        tile.colliderType = Tile.ColliderType.Grid;
        try
        {
            map.SetMapActive(true);
            tiles.SetTile(Vector3Int.zero, tile);
            Check(tileCollider.hasTilemapChanges, "native tile addition is pending before probe");
            map.SetMapActive(false);
            current.Player.transform.position = origin;
            var before = new ProbeState(current, target);
            Check(map.TryFindSafePosition(current.Player, target.Player, out Vector3 safe) && safe != origin,
                "cold tilemap/manual composite rejects known solid at preferred position");
            // Sleeping a tilemap can mark it dirty again; native geometry below proves regeneration.
            Check(!map.IsActive && before.Same(),
                "tile geometry probe restores sleeping map and gameplay");
            map.SetMapActive(true); Physics2D.SyncTransforms();
            Check(composite.OverlapPoint((Vector2)origin + Vector2.one * 0.5f),
                "native composite contains occupied cell after manual generation");
            map.SetMapActive(false);
            current.Player.transform.position = origin + new Vector3(-2, -2, 0);
            Check(map.TryFindSafePosition(current.Player, target.Player, out safe) && safe == current.Player.transform.position,
                "native composite accepts known clear cell without relocation");
            map.SetMapActive(true);
            tiles.SetTile(Vector3Int.zero, null);
            Check(tileCollider.hasTilemapChanges, "native tile removal is pending before next probe");
            map.SetMapActive(false);
            current.Player.transform.position = origin;
            Check(map.TryFindSafePosition(current.Player, target.Player, out safe) && safe == origin,
                "pending removal regenerates manual composite instead of leaving stale solid");
        }
        finally
        {
            current.Player.transform.position = origin;
            map.SetMapActive(false);
            grid.SetActive(false); Object.Destroy(grid); Object.Destroy(tile); Object.Destroy(sprite);
        }
    }

    static void BossPlacement(WorldMap map, World current, Vector3 origin, BoxCollider2D solid)
    {
        GameObject prefab = new GameObject("Inactive boss footprint fixture");
        prefab.SetActive(false);
        CapsuleCollider2D capsule = prefab.AddComponent<CapsuleCollider2D>();
        capsule.size = new Vector2(4.5158005f, 9.77397f);
        capsule.offset = new Vector2(-0.03105545f, 0);
        prefab.transform.localScale = Vector3.one * 0.25f;
        GameObject scaledParent = new GameObject("Scaled spawn parent fixture");
        scaledParent.transform.localScale = Vector3.one * 2;
        try
        {
            current.Player.transform.position = origin;
            solid.enabled = false;
            Check(WorldMap.TryGetFootprint(prefab.transform, out Vector2 offset, out float radius) && radius > 1.3f,
                "generic extractor reads scaled boss capsule without activating prefab");
            Check(map.TryFindSafeSpawnPosition(prefab.transform, current.ContentRoot, current.Player, 6, out Vector3 safe)
                && safe == origin + Vector3.right * 6, "boss keeps preferred right-side distance when clear");
            Check(map.TryFindSafeSpawnPosition(prefab.transform, scaledParent.transform, current.Player, 6, out safe)
                && safe != origin + Vector3.right * 6 && NativeClear(map, safe, offset * 2, radius * 2),
                "spawn parent scale enlarges boss footprint before choosing placement");
            solid.enabled = true; solid.isTrigger = false; solid.size = new Vector2(3, 3);
            solid.transform.position = origin + Vector3.right * 6;
            Check(map.TryFindSafeSpawnPosition(prefab.transform, current.ContentRoot, current.Player, 6, out safe)
                && safe != origin + Vector3.right * 6 && Mathf.Abs(Vector2.Distance(safe, origin) - 6) < 0.001f,
                "blocked preferred boss point searches other directions at bossDistance first");
            map.SetMapActive(true); Physics2D.SyncTransforms();
            Check(Vector2.Distance(solid.ClosestPoint((Vector2)safe + offset), (Vector2)safe + offset) > radius,
                "boss candidate clears native solid with full prefab footprint");
            map.SetMapActive(false);
            solid.enabled = false;
            current.Player.transform.position = origin + Vector3.right * 7;
            Check(map.TryFindSafeSpawnPosition(prefab.transform, current.ContentRoot, current.Player, 6, out safe)
                && safe.x + offset.x + radius < origin.x + 8, "boss near right boundary spawns inward, never beyond wall");
            current.Player.transform.position = origin + Vector3.right * 7.55f;
            Check(map.TryFindSafeSpawnPosition(prefab.transform, current.ContentRoot, current.Player, 6, out safe)
                && NativeClear(map, safe, offset, radius),
                "hero touching the wall does not fail finale because of the extra spawn clearance margin");
            current.Player.transform.position = origin;
            Check(map.TryFindSafeSpawnPosition(prefab.transform, current.ContentRoot, current.Player, 12, out safe)
                && Vector2.Distance(safe, origin) >= 9 && Vector2.Distance(safe, origin) < 12,
                "boss searches bounded neighboring radii when entire preferred ring is outside rectangle");
            solid.enabled = true; solid.transform.position = origin; solid.size = new Vector2(40, 40);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Check(!map.TryFindSafeSpawnPosition(prefab.transform, current.ContentRoot, current.Player, 6, out safe)
                && clock.ElapsedMilliseconds < 1000, "fully blocked boss search exhausts a bounded candidate set");
            Check(!map.IsActive && !prefab.activeSelf && current.Player.transform.position == origin,
                "boss probes restore map and never instantiate/wake prefab or move player");
        }
        finally
        {

            current.Player.transform.position = origin;
            solid.enabled = false;
            Object.Destroy(prefab); Object.Destroy(scaledParent);
        }
    }

    // Fresh disposable session. The injected OnEnable failure intentionally logs the switch rollback error.
    public static string RunSwitchRollback()
    {
        Require(Application.isPlaying && !running, "Requires disposable Play Mode");
        var manager = Object.FindAnyObjectByType<WorldManager>();
        Require(manager != null && manager.IsInitialized && !manager.IsFused && !manager.IsWorldTransitioning,
            "Requires initialized manager between transitions");
        World current = manager.CurrentWorld;
        World target = ((World[])Get(manager, "worlds")).Single(w => w != current);
        Require(target.Map != null && target.Map.TryFindSafePosition(current.Player, target.Player, out _),
            "Safe destination must exist before activation-failure injection");
        Passed = Failed = 0;
        running = true;
        Rigidbody2D body = target.Player.GetComponent<Rigidbody2D>();
        Vector3 savedPosition = target.Player.transform.position;
        Vector2 savedBodyPosition = body.position, savedVelocity = body.linearVelocity;
        float savedRotation = body.rotation, savedAngularVelocity = body.angularVelocity;
        float timeScale = Time.timeScale;
        GameObject panel = UIController.instance == null ? null : UIController.instance.levelUpPanel;
        bool panelActive = panel != null && panel.activeSelf;
        GameObject faultObject = Make("Post-probe activation failure", target.ContentRoot);
        var fault = faultObject.AddComponent<WorldMapActivationFailure>();
        fault.MapRoot = target.Map.gameObject;
        int events = 0;
        Action changed = () => events++;
        manager.WorldChanged += changed;
        try
        {
            Time.timeScale = 1;
            if (panel != null) panel.SetActive(false);
            body.position = new Vector2(4.25f, -3.5f); body.rotation = 17;
            target.Player.transform.position = new Vector3(4.25f, -3.5f, 0);
            body.linearVelocity = new Vector2(2.75f, -1.5f); body.angularVelocity = 29;
            Vector3 position = target.Player.transform.position;
            Vector2 bodyPosition = body.position, velocity = body.linearVelocity;
            float rotation = body.rotation, angular = body.angularVelocity;
            Check(!Commit(manager, target.WorldId) && fault.Triggered,
                "native target OnEnable failure occurs after successful probe and position mutation");
            Check(body.position == bodyPosition && body.rotation == rotation && target.Player.transform.position == position
                && body.linearVelocity == velocity && body.angularVelocity == angular,
                "post-probe failure restores target transform and exact rigidbody position/rotation/velocities");
            Check(events == 0 && manager.IsInitialized && !manager.IsSwitching && manager.CurrentWorld == current
                && current.IsActive && !target.IsActive && current.Map.IsActive && !target.Map.IsActive
                && PlayerController.instance == current.Player,
                "activation rollback restores world/map ownership and aliases without commit notification");
        }
        finally
        {
            manager.WorldChanged -= changed;
            fault.enabled = false; Object.Destroy(faultObject);
            body.position = savedBodyPosition; body.rotation = savedRotation;
            body.linearVelocity = savedVelocity; body.angularVelocity = savedAngularVelocity;
            target.Player.transform.position = savedPosition;
            Time.timeScale = timeScale;
            if (panel != null) panel.SetActive(panelActive);
            running = false;
        }
        return "WorldMapChecks switch rollback: " + Passed + " passed, " + Failed + " failed";
    }

    public static string RunLifecycle()
    {
        Require(Application.isPlaying && !running, "Requires disposable initialized Play Mode");
        var manager = Object.FindAnyObjectByType<WorldManager>();
        Require(manager != null && manager.IsInitialized && !manager.IsFused && !manager.IsWorldTransitioning
            && !manager.IsFinalFusion, "Run before any finale or flip");
        World[] worlds = (World[])Get(manager, "worlds");
        Require(worlds.Length == 2 && worlds.All(w => w.Map != null && w.Map.IsConfigured), "Both scene maps must be wired");
        Passed = Failed = 0;
        running = true;
        float scale = Time.timeScale;
        object run = Property(manager, "RunController"), fusion = Property(manager, "FusionTransition");
        object flow = Property(manager, "SwitchFlow");
        GameObject panel = UIController.instance == null ? null : UIController.instance.levelUpPanel;
        bool panelActive = panel != null && panel.activeSelf;
        var blocker = new GameObject("WorldMapChecks full-search blocker");
        try
        {
            Time.timeScale = 1;
            if (panel != null) panel.SetActive(false);
            SetProperty(manager, "RunController", null); SetProperty(manager, "FusionTransition", null);
            SetProperty(manager, "SwitchFlow", null);
            World entry = manager.CurrentWorld, secondary = worlds.Single(w => w != entry);
            blocker.transform.SetParent(secondary.Map.transform, true);
            blocker.transform.position = entry.Player.transform.position;
            blocker.AddComponent<BoxCollider2D>().size = new Vector2(100, 100);
            var before = new ProbeState(entry, secondary);
            int events = 0;
            Action changed = () => events++;
            manager.WorldChanged += changed;
            try
            {
                Check(!Commit(manager, secondary.WorldId) && events == 0 && before.Same()
                    && manager.CurrentWorld == entry && entry.Map.IsActive && !secondary.Map.IsActive,
                    "blocked midpoint cancels before content, position, aliases or notifications change");
            }
            finally { manager.WorldChanged -= changed; }
            blocker.SetActive(false);
            foreach (World destination in new[] { secondary, entry })
            {
                Check(Commit(manager, destination.WorldId), "real safe midpoint into " + destination.WorldId);
                World other = worlds.Single(w => w != manager.CurrentWorld);
                Check(manager.CurrentWorld.Map.IsActive && !other.Map.IsActive && manager.CurrentWorld.IsActive && !other.IsActive,
                    "ordinary switch activates exactly one content and map");
                Check(manager.TryEnterFusion(), "enter fusion from " + destination.WorldId);
                Check(worlds.All(w => w.IsActive) && manager.CurrentWorld.Map.IsActive && !other.Map.IsActive,
                    "fusion runs secondary content with only ENTRY map visible/collidable");
                Check(manager.TryExitFusion() && manager.CurrentWorld.Map.IsActive && !other.Map.IsActive && !other.IsActive,
                    "fusion exit restores exclusive map lifecycle");
            }
            Check(manager.TryEnterFusion(), "enter fusion for manager-disable cleanup");
            manager.enabled = false;
            Check(!manager.IsFused && manager.CurrentWorld.Map.IsActive
                && worlds.Where(w => w != manager.CurrentWorld).All(w => !w.IsActive && !w.Map.IsActive),
                "manager disable restores maps and sleeps secondary content");
        }
        finally
        {
            blocker.SetActive(false); Object.Destroy(blocker);
            SetProperty(manager, "RunController", run); SetProperty(manager, "FusionTransition", fusion);
            SetProperty(manager, "SwitchFlow", flow);
            Time.timeScale = scale;
            if (panel != null) panel.SetActive(panelActive);
            running = false;
        }
        return "WorldMapChecks lifecycle: " + Passed + " passed, " + Failed + " failed";
    }

    // Expected error logs: captured-content fallback disables the manager after intentional misconfiguration.
    public static string RunCleanupFailure()
    {
        Require(Application.isPlaying && !running, "Requires a fresh disposable Play session");
        var manager = Object.FindAnyObjectByType<WorldManager>();
        Require(manager != null && manager.IsInitialized && !manager.IsFused && !manager.IsFinalFusion,
            "Requires unfused manager before the finale");
        World entry = manager.CurrentWorld;
        World secondary = ((World[])Get(manager, "worlds")).Single(w => w != entry);
        Require(entry.Map != null && secondary.Map != null && Time.timeScale > 0f, "Requires wired maps and unpaused run");
        Passed = Failed = 0;
        GameObject content = secondary.ContentRoot.gameObject;
        WorldMap capturedMap = secondary.Map;
        object run = Property(manager, "RunController"), fusion = Property(manager, "FusionTransition");
        running = true;
        try
        {
            SetProperty(manager, "RunController", null); SetProperty(manager, "FusionTransition", null);
            Require(manager.TryEnterFusion(), "Fusion must enter before fault injection");
            Set(secondary, "contentRoot", null);
            Set(secondary, "map", null);
            manager.enabled = false;
            Check(!manager.IsFused && !manager.IsInitialized && !content.activeSelf && !capturedMap.IsActive
                && entry.IsActive && entry.Map.IsActive,
                "invalid-content fallback restores entry map and disables captured secondary map despite changed references");
        }
        finally
        {
            Set(secondary, "contentRoot", content); Set(secondary, "map", capturedMap);
            SetProperty(manager, "RunController", run); SetProperty(manager, "FusionTransition", fusion);
            running = false;
        }
        return "WorldMapChecks cleanup failure: " + Passed + " passed, " + Failed + " failed";
    }

    sealed class ProbeState
    {
        readonly World a, b;
        readonly Vector3 aPosition, bPosition;
        readonly PlayerController alias;
        readonly PlayerHealth healthAlias;
        readonly ExperienceLevelController xpAlias;
        readonly bool aActive, bActive, aSuspended, bSuspended;
        readonly Rigidbody2D body;
        readonly Vector2 velocity, bodyPosition;
        readonly float angularVelocity, rotation;
        readonly RigidbodyConstraints2D constraints;
        readonly Behaviour[] behaviours;
        readonly bool[] enabled;
        readonly BuffController[] holders;
        readonly bool[] suspended;
        readonly BuffInstance[][] buffs;
        readonly float[][] durations;
        public ProbeState(World first, World second)
        {
            a = first; b = second;
            aPosition = a.Player.transform.position; bPosition = b.Player.transform.position;
            alias = PlayerController.instance; healthAlias = PlayerHealth.instance; xpAlias = ExperienceLevelController.instance;
            aActive = a.IsActive; bActive = b.IsActive; aSuspended = a.IsSuspended; bSuspended = b.IsSuspended;
            body = b.Player.GetComponent<Rigidbody2D>();
            velocity = body.linearVelocity; angularVelocity = body.angularVelocity; constraints = body.constraints;
            bodyPosition = body.position; rotation = body.rotation;
            behaviours = b.ContentRoot.GetComponentsInChildren<Behaviour>(true);
            enabled = behaviours.Select(v => v.enabled).ToArray();
            holders = b.ContentRoot.GetComponentsInChildren<BuffController>(true);
            suspended = holders.Select(v => v.IsWorldSuspended).ToArray();
            buffs = holders.Select(v => ((IEnumerable)Get(v, "instances")).Cast<BuffInstance>().ToArray()).ToArray();
            durations = buffs.Select(v => v.Select(i => i.RemainingDuration).ToArray()).ToArray();
        }
        public bool Same() => a.Player.transform.position == aPosition && b.Player.transform.position == bPosition
            && PlayerController.instance == alias && PlayerHealth.instance == healthAlias && ExperienceLevelController.instance == xpAlias
            && a.IsActive == aActive && b.IsActive == bActive && a.IsSuspended == aSuspended && b.IsSuspended == bSuspended
            && body.linearVelocity == velocity && body.angularVelocity == angularVelocity && body.constraints == constraints
            && body.position == bodyPosition && body.rotation == rotation
            && behaviours.Select((v, i) => v != null && v.enabled == enabled[i]).All(v => v)
            && holders.Select((v, i) => v.IsWorldSuspended == suspended[i]
                && ((IEnumerable)Get(v, "instances")).Cast<BuffInstance>().SequenceEqual(buffs[i])
                && buffs[i].Select((buff, j) => buff.RemainingDuration == durations[i][j]).All(ok => ok)).All(ok => ok);
    }

    // Call immediately after scene initialization, before movement. Assessment only: never relocate a hero.
    public static string AssessInitialPosition()
    {
        Require(Application.isPlaying && !running, "Requires initialized Play Mode before player movement");
        var manager = Object.FindAnyObjectByType<WorldManager>();
        Require(manager != null && manager.IsInitialized && !manager.IsFused, "Requires initialized unfused manager");
        World world = manager.CurrentWorld;
        Require(world.Map != null && world.Map.IsConfigured && world.Player != null, "Wire maps and four-wall boundary first");
        Vector3 origin = world.Player.transform.position;
        bool mapActive = world.Map.gameObject.activeSelf;
        var other = ((World[])Get(manager, "worlds")).Single(w => w != world);
        var state = new ProbeState(world, other);
        bool found = world.Map.TryFindSafePosition(world.Player, world.Player, out Vector3 safe);
        world.Map.TryGetPlayableRect(out Rect playable);
        WorldMap.TryGetFootprint(world.Player, out Vector2 offset, out float radius);
        Require(state.Same() && world.Map.gameObject.activeSelf == mapActive, "Spawn assessment must not mutate gameplay or maps");
        string result = "WorldMapChecks initial position: " + world.gameObject.scene.name + "/" + world.WorldId
            + " origin=" + origin + " offset=" + offset + " radius=" + radius + " playable=" + playable
            + (found && safe == origin ? " CLEAR" : found ? " BLOCKED; nearby candidate=" + safe
                : " UNSAFE/UNRESOLVED: outside bounds, blocked search, or query budget exhausted")
            + "; assessment only, no teleport";
        if (found && safe == origin) Debug.Log(result); else Debug.LogWarning(result);
        return result;
    }

    // Assessment on authored maps, not fixtures. Source/target heroes and prefab are never moved or activated.
    public static string AssessMapClearPoints()
    {
        Require(Application.isPlaying && !running, "Requires initialized Play Mode");
        var manager = Object.FindAnyObjectByType<WorldManager>();
        Require(manager != null && manager.IsInitialized && !manager.IsFused, "Requires unfused initialized manager");
        World current = manager.CurrentWorld;
        World[] worlds = (World[])Get(manager, "worlds");
        Passed = Failed = 0;
        Physics2D.SyncTransforms();
        foreach (World world in worlds)
        {
            Require(world.Map != null && world.Map.IsConfigured, "Wire actual map and four-wall references first");
            var before = new ProbeState(current, world);
            bool active = world.Map.gameObject.activeSelf;
            bool found = world.Map.TryFindSafePosition(current.Player, world.Player, out Vector3 safe);
            Check(WorldMap.TryGetFootprint(world.Player, out Vector2 offset, out float radius)
                && found && NativeClear(world.Map, safe, offset, radius),
                "authored " + world.WorldId + " native clear point=" + safe + "; radius=" + radius);
            Check(before.Same() && world.Map.gameObject.activeSelf == active,
                "authored map assessment preserves bodies, aliases, Buffs and map state");
        }
        var run = Object.FindAnyObjectByType<RunStageController>();
        Require(run != null, "Requires scene run controller with boss prefab");
        var prefab = (EnemyController)Get(run, "bossPrefab");
        float distance = (float)Get(run, "bossDistance");
        Check(current.Map.TryFindSafeSpawnPosition(prefab.transform, current.ContentRoot, current.Player, distance, out Vector3 spawn),
            "actual boss prefab has a bounded safe point on entry map: " + spawn);
        return "WorldMapChecks authored maps: " + Passed + " passed, " + Failed + " failed";
    }

    static bool NativeClear(WorldMap map, Vector3 position, Vector2 offset, float radius)
    {
        bool active = map.gameObject.activeSelf;
        try
        {
            map.SetMapActive(true); Physics2D.SyncTransforms();
            if (!map.TryGetPlayableRect(out Rect rect)) return false;
            Vector2 center = (Vector2)position + offset;
            float clearance = radius + 0.02f;
            if (center.x - clearance < rect.xMin || center.x + clearance > rect.xMax
                || center.y - clearance < rect.yMin || center.y + clearance > rect.yMax) return false;
            var results = new Collider2D[128];
            int count = Physics2D.OverlapCircle(center, clearance, new ContactFilter2D { useTriggers = true }, results);
            return count < results.Length && !results.Take(count).Any(hit => !hit.isTrigger && hit.transform.IsChildOf(map.transform));
        }
        finally { map.SetMapActive(active); Physics2D.SyncTransforms(); }
    }

    // Disposable session: forces placement exhaustion through the real finale failure path, not a death event.
    public static string RunBossSpawnFailure()
    {
        Require(Application.isPlaying && !running, "Requires fresh disposable Play Mode");
        var manager = Object.FindAnyObjectByType<WorldManager>();
        var run = Object.FindAnyObjectByType<RunStageController>();
        Require(manager != null && manager.IsInitialized && run != null && run.IsRunning
            && !run.IsFinaleStarted && manager.CurrentWorld.Map != null, "Requires initialized run before finale");
        Passed = Failed = 0;
        GameObject blocker = Make("Exhaust boss placement", manager.CurrentWorld.Map.transform);
        blocker.transform.position = manager.CurrentWorld.Player.transform.position;
        blocker.AddComponent<BoxCollider2D>().size = new Vector2(1000, 1000);
        try
        {
            SetProperty(run, "IsFinaleStarted", true); Set(run, "fusionCompleted", true);
            typeof(RunStageController).GetMethod("SpawnFinalBoss", Flags).Invoke(run, null);
            Check(run.IsDefeated && !(bool)Get(run, "bossSpawned") && !run.IsBossPhase && run.RemainingBosses == 0,
                "exhausted real spawn path explicitly fails without publishing bossSpawned or creating a boss");
        }
        finally { blocker.SetActive(false); Object.Destroy(blocker); }
        return "WorldMapChecks boss spawn failure: " + Passed + " passed, " + Failed + " failed";
    }

    static BoxCollider2D Wall(string name, Transform parent, Vector2 offset, Vector2 size)
    {
        var wall = Make(name, parent).AddComponent<BoxCollider2D>();
        wall.offset = offset; wall.size = size; return wall;
    }

    static GameObject Make(string name, Transform parent)
    {
        var value = new GameObject(name); value.transform.SetParent(parent, false); return value;
    }
    static bool Commit(WorldManager manager, WorldId id) => (bool)typeof(WorldManager).GetMethod("CommitWorldSwitch", Flags).Invoke(manager, new object[] { id });
    static object Get(object value, string name) => value.GetType().GetField(name, Flags).GetValue(value);
    static void Set(object value, string name, object data) => value.GetType().GetField(name, Flags).SetValue(value, data);
    static object Property(object value, string name) => value.GetType().GetProperty(name, Flags).GetValue(value);
    static void SetProperty(object value, string name, object data) => value.GetType().GetProperty(name, Flags).SetValue(value, data);
    static void Require(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); }
    static void Check(bool ok, string label)
    {
        if (ok) Passed++; else Failed++;
        Debug.Log("WorldMapChecks " + (ok ? "PASS " : "FAIL ") + label);
    }
}

// Test-only synchronous fault injection, compiled with this Tools file, never shipped in Assets.
public sealed class WorldMapActivationFailure : MonoBehaviour
{
    public GameObject MapRoot;
    public bool Triggered;
    private void OnEnable()
    {
        if (MapRoot == null) return;
        Triggered = true;
        MapRoot.SetActive(false);
    }
}
