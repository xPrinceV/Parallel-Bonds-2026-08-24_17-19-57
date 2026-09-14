using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

// Compile outside Assets against current game/Unity assemblies. Call Run() on Unity's main thread in Play Mode.
// Synchronous isolated callback probes, not screenshot/URP culling validation. No scene loads or gameplay updates.
public static class WorldMapPresentationChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    static bool running;

    public static string Run()
    {
        Require(Application.isPlaying && !running, "Requires Play Mode on the main thread without reentry");
        Passed = Failed = 0;
        running = true;
        var root = new GameObject("WorldMapPresentationChecks fixture");
        root.transform.position = new Vector3(10000, 10000, 0);
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.colliderType = Tile.ColliderType.Grid;
        try
        {
            Transform grid = Make("Shared Grid", root.transform).transform;
            grid.gameObject.AddComponent<Grid>();
            Transform boundary = Make("Shared boundary", root.transform).transform;
            Wall(boundary, new Vector2(-8.5f, 0), new Vector2(1, 18));
            Wall(boundary, new Vector2(8.5f, 0), new Vector2(1, 18));
            Wall(boundary, new Vector2(0, -8.5f), new Vector2(18, 1));
            Wall(boundary, new Vector2(0, 8.5f), new Vector2(18, 1));
            World entry = MakeWorld("Entry", WorldId.Material, root.transform, grid, boundary, tile);
            World other = MakeWorld("Other", WorldId.Echo, root.transform, grid, boundary, tile);
            other.ContentRoot.gameObject.SetActive(false);
            other.Map.SetMapActive(false);
            WorldMap otherMap = other.Map;
            var camera = Make("Capture camera", root.transform).AddComponent<Camera>();
            camera.enabled = false;
            var unrelatedCamera = Make("Unrelated camera", root.transform).AddComponent<Camera>();
            unrelatedCamera.enabled = false;
            var flow = Make("Clock", root.transform).AddComponent<FusionTransitionController>();
            var flip = Make("Capture", root.transform).AddComponent<WorldFlipPresentation>();
            flip.Configure(null, camera, null);
            var captureCanvas = Make("Capture canvas", root.transform).AddComponent<Canvas>();
            Set(flip, "runtimeCanvas", captureCanvas);
            Set(flip, "capturing", true);
            var view = Make("Projection", root.transform).AddComponent<FusionTransitionPresentation>();
            view.Configure(flow, flip, null);
            var portraitCanvas = Make("Portrait canvas", root.transform).AddComponent<Canvas>();
            Set(view, "canvas", portraitCanvas);
            Property(flow, "EntryWorld", entry);
            Property(flow, "OtherWorld", other);
            Property(flow, "IsPlaying", true);
            Property(flow, "Progress", 0.2f);
            Property(flow, "Turns", 1f);

            Renderer entryRenderer = entry.Map.GetComponentInChildren<TilemapRenderer>();
            Renderer otherRenderer = other.Map.GetComponentInChildren<TilemapRenderer>(true);
            Renderer hiddenBefore = Make("Already hidden", entry.Map.transform).AddComponent<SpriteRenderer>();
            hiddenBefore.forceRenderingOff = true;
            var colliders = otherMap.GetComponentsInChildren<Collider2D>(true).ToDictionary(c => c, c => c.enabled);
            var bodies = otherMap.GetComponentsInChildren<Rigidbody2D>(true).ToDictionary(b => b, b => b.simulated);
            var walls = boundary.GetComponentsInChildren<Collider2D>().ToDictionary(c => c, c => c.enabled);
            PlayerController alias = PlayerController.instance;
            int objects = Resources.FindObjectsOfTypeAll<GameObject>().Length;
            var context = default(ScriptableRenderContext);

            Action begin = () => Call(view, "BeginCameraRendering", context, camera);
            Action end = () => Call(view, "EndCameraRendering", context, camera);
            Action restored = () =>
            {
                Check(!otherMap.gameObject.activeSelf && entry.Map.IsActive,
                    "cleanup restores entry-map-only activation");
                Check(colliders.All(p => p.Key.enabled == p.Value) && bodies.All(p => p.Key.simulated == p.Value),
                    "cleanup preserves enabled/disabled colliders and simulated/unsimulated bodies");
                Check(!entryRenderer.forceRenderingOff && !otherRenderer.forceRenderingOff && hiddenBefore.forceRenderingOff,
                    "cleanup restores exact renderer suppression flags");
                Check(!other.ContentRoot.gameObject.activeSelf && entry.IsActive && PlayerController.instance == alias
                    && walls.All(p => p.Key.enabled == p.Value), "projection never wakes sleeping Content or changes aliases/shared walls");
            };

            Call(view, "BeginCameraRendering", context, unrelatedCamera);
            Check(!otherMap.IsActive && !entryRenderer.forceRenderingOff, "unrelated camera does not acquire projection");
            begin();
            Check(otherMap.IsActive && entryRenderer.forceRenderingOff && !otherRenderer.forceRenderingOff,
                "other face shows actual external map and hides entry map");
            // Composite source enable flags are managed by Unity; unsimulated bodies exclude their shapes.
            Check(colliders.Keys.All(IsInert) && bodies.Keys.All(b => !b.simulated),
                "every secondary collider and static body is inert during capture");
            Physics2D.SyncTransforms();
            Check(!Physics2D.OverlapCircleAll((Vector2)root.transform.position + Vector2.one * 0.5f, 0.1f)
                .Any(c => c.transform.IsChildOf(otherMap.transform)), "native query sees no secondary map collision during capture");
            Call(view, "EndCameraRendering", context, unrelatedCamera);
            Check(otherMap.IsActive, "unrelated camera end does not release an ongoing capture");
            end();
            restored();
            end();
            restored();

            Property(flow, "Turns", 0f);
            begin();
            Check(!otherMap.IsActive && otherRenderer.forceRenderingOff && !entryRenderer.forceRenderingOff,
                "entry face hides other map without activating it");
            end();
            restored();
            Property(flow, "Turns", 1f);
            begin();
            begin();
            end();
            restored();
            Check(Resources.FindObjectsOfTypeAll<GameObject>().Length == objects, "repeated capture creates no clones or GameObjects");

            begin();
            Call(view, "EndFrameRendering", context, new[] { camera });
            restored();
            begin();
            Call(view, "SceneUnloaded", default(UnityEngine.SceneManagement.Scene));
            restored();
            begin();
            Property(flow, "IsPlaying", false);
            Call(view, "LateUpdate");
            restored();
            Property(flow, "IsPlaying", true);
            portraitCanvas.enabled = true;
            begin();
            Set(other, "map", null);
            end();
            Set(other, "map", otherMap);
            restored();

            Property(flow, "Progress", 0.9f);
            begin();
            Check(!otherMap.IsActive && !entryRenderer.forceRenderingOff, "reveal leaves entry terrain and physics unchanged");
            end();
            restored();
            Property(flow, "Progress", 0.2f);
            Property(flow, "IsPlaying", false);
            begin();
            restored();
            Property(flow, "IsPlaying", true);

            // An already active map must retain its original root state as well as its physics flags.
            otherMap.SetMapActive(true);
            begin();
            Check(colliders.Keys.All(IsInert) && bodies.Keys.All(b => !b.simulated),
                "already active secondary map is also made render-only");
            end();
            Check(otherMap.IsActive && colliders.All(p => p.Key.enabled == p.Value)
                && bodies.All(p => p.Key.simulated == p.Value), "original active root state survives capture");
            Physics2D.SyncTransforms();
            Check(Physics2D.OverlapCircleAll((Vector2)root.transform.position + Vector2.one * 0.5f, 0.1f)
                .Any(c => c is CompositeCollider2D && c.transform.IsChildOf(otherMap.transform)),
                "native composite tile geometry remains available after repeated projection/restoration");
            otherMap.SetMapActive(false);
            restored();

            Rigidbody2D staticBody = bodies.Keys.First(b => b.simulated);
            staticBody.bodyType = RigidbodyType2D.Dynamic;
            begin();
            Check(!otherMap.IsActive && colliders.All(p => p.Key.enabled == p.Value),
                "non-static map fails closed before activation or collider mutation (warning expected)");
            end();
            staticBody.bodyType = RigidbodyType2D.Static;
            restored();
            var animated = Make("Non-geometry child", otherMap.transform).AddComponent<Animator>();
            begin();
            Check(!otherMap.IsActive && colliders.All(p => p.Key.enabled == p.Value),
                "animated map fails closed without waking non-geometry components");
            end();
            animated.transform.SetParent(root.transform, false);
            restored();

            begin();
            view.enabled = false;
            restored();
            Check(!flow.IsPlaying, "disable cancels presentation and releases projection");
        }
        finally
        {
            root.SetActive(false);
            Object.Destroy(root);
            Object.Destroy(tile);
            running = false;
        }
        return "WorldMapPresentationChecks: " + Passed + " passed, " + Failed + " failed";
    }

    static bool IsInert(Collider2D collider) => !collider.enabled
        || (collider.attachedRigidbody != null && !collider.attachedRigidbody.simulated);

    static World MakeWorld(string name, WorldId id, Transform parent, Transform grid, Transform boundary, Tile tile)
    {
        var world = Make(name, parent).AddComponent<World>();
        Set(world, "worldId", id);
        GameObject content = Make("Content", world.transform);
        content.AddComponent<SpriteRenderer>();
        Set(world, "contentRoot", content);
        WorldMap map = Make(name + " Map", grid).AddComponent<WorldMap>();
        Set(map, "boundaryRoot", boundary);
        Set(world, "map", map);
        var tiles = Make("Tiles", map.transform).AddComponent<Tilemap>();
        tiles.gameObject.AddComponent<TilemapRenderer>();
        var body = tiles.gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Static;
        var composite = tiles.gameObject.AddComponent<CompositeCollider2D>();
        composite.generationType = CompositeCollider2D.GenerationType.Manual;
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        var collider = tiles.gameObject.AddComponent<TilemapCollider2D>();
        collider.compositeOperation = Collider2D.CompositeOperation.Merge;
        tiles.SetTile(Vector3Int.zero, tile);
        collider.ProcessTilemapChanges();
        composite.GenerateGeometry();
        Make("Standalone solid", map.transform).AddComponent<BoxCollider2D>();
        var disabled = Make("Disabled collider", map.transform).AddComponent<BoxCollider2D>();
        disabled.enabled = false;
        var unsimulated = Make("Unsimulated body", map.transform).AddComponent<Rigidbody2D>();
        unsimulated.bodyType = RigidbodyType2D.Static;
        unsimulated.simulated = false;
        unsimulated.gameObject.AddComponent<BoxCollider2D>();
        var inactive = Make("Inactive child", map.transform).AddComponent<BoxCollider2D>();
        inactive.gameObject.SetActive(false);
        Require(world.IsConfigured, "Fixture must use configured external maps");
        return world;
    }

    static void Wall(Transform parent, Vector2 offset, Vector2 size)
    {
        var wall = Make("Wall", parent).AddComponent<BoxCollider2D>();
        wall.offset = offset;
        wall.size = size;
    }

    static GameObject Make(string name, Transform parent)
    {
        var value = new GameObject(name);
        value.transform.SetParent(parent, false);
        return value;
    }

    static void Set(object value, string name, object data) => value.GetType().GetField(name, Flags).SetValue(value, data);
    static void Property(object value, string name, object data) => value.GetType().GetProperty(name, Flags).SetValue(value, data);
    static void Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Flags).Invoke(value, args);
    static void Require(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); }
    static void Check(bool ok, string label)
    {
        if (ok) Passed++; else Failed++;
        Debug.Log("WorldMapPresentationChecks " + (ok ? "PASS " : "FAIL ") + label);
    }
}
