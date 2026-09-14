using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Isolated Editor only; no Play Mode or asset saves.
[InitializeOnLoad]
public static class SpriteSortingChecks
{
    static readonly string[] Enemies = { "Titan", "Enemy - Lurker", "Hollow", "Rift Lord", "RiftLordBoss",
        "Enemy - Lurker Variant 2", "Enemy - Lurker Variant 3", "Enemy - Lurker Variant 4", "Enemy - Lurker Variant 5", "Hollow Variant 2" };
    static readonly string[] Attacks = { "Arrow", "Bullet", "Dagger", "Scythe", "LightningHit", "LanternProj", "Explosion", "Fire", "Shield", "HollowProjectile" };
    static readonly string[] Scenes = { "Assets/Scenes/Main.unity", "Assets/Scenes/DebugRun.unity" };
    static readonly List<string> evidence = new List<string>();
    static int passed, failed, errors, searchErrors;
    static bool visualCompleted;
    static Dictionary<string, Row> baseline;
    [Serializable] sealed class Row { public string asset, path, kind; public int layer, order, mode; }
    [Serializable] sealed class Rows { public Row[] rows; }
    [Serializable] sealed class Result
    {
        public int passed, failed, unexpectedErrors, knownSearchErrors;
        public bool enteredPlayMode, visualCompleted;
        public string[] evidence;
    }
    static SpriteSortingChecks()
    {
        if (Environment.GetCommandLineArgs().Contains("-spriteOutput")) Application.logMessageReceived += OnLog;
    }
    public static void Run() { EditorApplication.delayCall += Execute; }
    static string Arg(string key) { var args = Environment.GetCommandLineArgs(); return args[Array.IndexOf(args, key) + 1]; }
    static string Prefab(string name) => "Assets/Prefabs/" + name + ".prefab";
    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    static string Key(string asset, Component c) => asset + "|" + PathOf(c.transform) + "|" + c.GetType().Name;
    static void Check(bool ok, string text) { if (ok) passed++; else failed++; Note((ok ? "PASS " : "FAIL ") + text); }
    static void Note(string text) { evidence.Add(text); Debug.Log("SPRITE_SORTING " + text); }
    static void Execute()
    {
        try
        {
            Check(!Application.isPlaying, "native Editor, never entered Play Mode");
            baseline = JsonUtility.FromJson<Rows>(File.ReadAllText(Path.Combine(Arg("-spriteOutput"), "baseline.json")))
                .rows.ToDictionary(r => r.asset + "|" + r.path + "|" + r.kind);
            Check(SortingLayer.layers.Select(l => l.name).SequenceEqual(new[] { "Background", "Default", "Enemy", "Pickups", "Player", "Effects", "UI", "Overlay UI" }),
                "actual SortingLayer order: " + string.Join(" < ", SortingLayer.layers.Select(l => l.name)));
            Dependencies();
            int basePoisons = 0;
            foreach (string name in Enemies)
            {
                string asset = Prefab(name); var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                Check(prefab != null, "AssetDatabase loads " + name); if (prefab == null) continue;
                if (Array.IndexOf(Enemies, name) >= 4) Check(PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant, name + " is an actual inherited variant");
                var renderers = prefab.GetComponentsInChildren<SpriteRenderer>(true);
                var body = renderers.Where(r => r.name != "PoisonEffect").ToArray();
                Check(body.Length == 1, name + " one resolved body renderer");
                foreach (var r in body) Sorting(r, "Enemy", 0, asset);
                foreach (var poison in renderers.Where(r => r.name == "PoisonEffect"))
                {
                    Sorting(poison, "Effects", 0, asset);
                    if (Array.IndexOf(Enemies, name) < 4) { basePoisons++; Unchanged(asset, poison, poison.sortingLayerID, poison.sortingOrder); }
                }
                Check(prefab.GetComponentsInChildren<SortingGroup>(true).Length == 0, name + " no ancestor SortingGroup overriding body order");
            }
            Check(basePoisons == 3, "three base PoisonEffect renderers retained");
            foreach (string name in Attacks.Concat(new[] { "Experience Orb", "TitanAttack" }))
            {
                string asset = Prefab(name); var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset);
                Check(prefab != null, "AssetDatabase loads " + name); if (prefab == null) continue;
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                Check(renderers.Length > 0, name + " resolved renderers including inactive children");
                foreach (var r in renderers) Sorting(r, name == "Experience Orb" ? "Pickups" : "Effects",
                    name == "TitanAttack" && r.name == "TitanAnimation" ? 1 : 0, asset);
                if (name == "Bullet") Check(renderers.Any(r => r is ParticleSystemRenderer), "Bullet actual particle renderer covered");
                if (name == "TitanAttack") Check(renderers.Any(r => r.name == "Indicator") && renderers.Any(r => r.name == "TitanAnimation"), "TitanAttack indicator and inactive animation both covered");
            }
            foreach (string asset in Scenes) SceneChecks(asset);
            PixelFixture();
        }
        catch (Exception e) { Check(false, "aborted: " + e); }
        finally
        {
            var result = new Result { passed = passed, failed = failed, unexpectedErrors = errors, knownSearchErrors = searchErrors,
                enteredPlayMode = Application.isPlaying, visualCompleted = visualCompleted, evidence = evidence.ToArray() };
            File.WriteAllText(Path.Combine(Arg("-spriteOutput"), "summary.json"), JsonUtility.ToJson(result, true));
            Debug.Log("SPRITE_SORTING RESULT " + passed + " passed / " + failed + " failed; unexpected=" + errors + "; knownSearch=" + searchErrors + "; visual=" + visualCompleted);
            Application.logMessageReceived -= OnLog;
            EditorApplication.Exit(failed > 0 || errors > 0 ? 1 : searchErrors > 0 ? 3 : 0);
        }
    }
    static void Sorting(Renderer r, string layer, int order, string asset)
    { Check(r.sortingLayerName == layer && r.sortingOrder == order, asset + "/" + PathOf(r.transform) + " " + r.GetType().Name + "=" + r.sortingLayerName + ":" + r.sortingOrder + "; expected=" + layer + ":" + order); }
    static void Unchanged(string asset, Component c, int layer, int order, int mode = -1)
    {
        Row old;
        Check(baseline.TryGetValue(Key(asset, c), out old) && old.layer == layer && old.order == order && old.mode == mode,
            "unchanged versus prior isolated snapshot: " + Key(asset, c));
    }
    static void SceneChecks(string asset)
    {
        var scene = EditorSceneManager.OpenScene(asset);
        var roots = scene.GetRootGameObjects();
        var canvases = roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).ToArray();
        var damage = canvases.Where(c => c.name == "Damage Number Canvas").ToArray();
        Check(damage.Length == 1, asset + " one Damage Number Canvas");
        foreach (var c in damage) Check(c.renderMode == RenderMode.WorldSpace && c.sortingLayerName == "UI" && c.sortingOrder == 0,
            asset + " Damage Number Canvas actual=" + c.renderMode + "/" + c.sortingLayerName + ":" + c.sortingOrder);
        foreach (var c in canvases.Except(damage)) Unchanged(asset, c, c.sortingLayerID, c.sortingOrder, (int)c.renderMode);
        var maps = roots.SelectMany(r => r.GetComponentsInChildren<TilemapRenderer>(true)).ToArray();
        Check(maps.Length == 6, asset + " six world tilemap renderers");
        foreach (var r in maps)
        {
            Sorting(r, r.name == "Tilemap" ? "Background" : "Default", r.name == "Buildings" ? 1 : r.name == "Decorations" ? 3 : 0, asset);
            Unchanged(asset, r, r.sortingLayerID, r.sortingOrder);
        }
        var players = roots.SelectMany(r => r.GetComponentsInChildren<PlayerController>(true)).ToArray();
        Check(players.Length == 2, asset + " two scene players");
        foreach (var p in players)
        {
            var bodies = p.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.GetComponentInParent<Weapon>() == null).ToArray();
            Check(bodies.Length > 0, asset + "/" + p.name + " body/visual renderers exist");
            foreach (var r in bodies) { Sorting(r, "Player", 1, asset); Unchanged(asset, r, r.sortingLayerID, r.sortingOrder); }
        }
    }
    static string Hash(string file) { using (var s = SHA256.Create()) using (var f = File.OpenRead(file)) return BitConverter.ToString(s.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
    static void Dependencies()
    {
        var seeds = Enemies.Concat(Attacks).Concat(new[] { "Experience Orb", "TitanAttack" }).Select(Prefab).Concat(Scenes).ToArray();
        var rows = new List<string> { "path\tsource_sha256\tisolated_sha256" };
        foreach (var asset in AssetDatabase.GetDependencies(seeds, true).Where(p => p.StartsWith("Assets/")))
            foreach (var file in new[] { asset, asset + ".meta" })
            {
                if (!File.Exists(file)) continue;
                string source = Path.Combine(Arg("-spriteSource"), file);
                if (!File.Exists(source) || Hash(source) != Hash(file)) throw new InvalidOperationException("Stale native dependency: " + file);
                rows.Add(file + "\t" + Hash(source) + "\t" + Hash(file));
            }
        File.WriteAllLines(Path.Combine(Arg("-spriteOutput"), "native-dependencies.tsv"), rows);
        Check(rows.Count > 1, "native AssetDatabase recursive dependency files match source: " + (rows.Count - 1));
    }
    static void PixelFixture()
    {
        // Synthetic overlap proves sorting, not full gameplay/art readability.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("SpriteSorting offscreen fixture");
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.SetPixels(Enumerable.Repeat(Color.white, 4).ToArray()); texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f, 1);
        var material = new Material(Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"));
        var rt = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32); rt.Create();
        var pixels = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        try
        {
            var camera = new GameObject("Fixture camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform);
            camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true; camera.orthographicSize = 1.5f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.enabled = false;
            camera.allowHDR = false; camera.allowMSAA = false;
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            var layers = new[] { "Default", "Enemy", "Effects" }; var colors = new[] { Color.red, Color.green, Color.blue };
            var sprites = new SpriteRenderer[3];
            for (int i = 0; i < 3; i++)
            {
                sprites[i] = new GameObject(layers[i]).AddComponent<SpriteRenderer>(); sprites[i].transform.SetParent(root.transform);
                sprites[i].sprite = sprite; sprites[i].sharedMaterial = material; sprites[i].color = colors[i];
                sprites[i].sortingLayerName = layers[i]; sprites[i].sortingOrder = i == 0 ? 3 : 0;
            }
            var ui = new GameObject("WorldSpace UI", typeof(RectTransform), typeof(Canvas)); ui.transform.SetParent(root.transform);
            var canvas = ui.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            canvas.overrideSorting = true; canvas.sortingLayerName = "UI"; canvas.sortingOrder = 0;
            ui.GetComponent<RectTransform>().sizeDelta = Vector2.one * 2;
            var image = new GameObject("Yellow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(ui.transform, false); image.rectTransform.sizeDelta = Vector2.one * 2; image.color = Color.yellow;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("Current pipeline cannot render fixture request");
            for (int pass = 0; pass < 4; pass++)
            {
                if (pass == 1) ui.SetActive(false);
                if (pass == 2) sprites[2].enabled = false;
                if (pass == 3) sprites[1].enabled = false;
                Canvas.ForceUpdateCanvases(); RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = rt; pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); pixels.Apply();
                Color expected = pass == 0 ? Color.yellow : colors[3 - pass];
                int matched = 0;
                for (int y = 30; y < 34; y++) for (int x = 30; x < 34; x++)
                { Color c = pixels.GetPixel(x, y); if (Mathf.Abs(c.r - expected.r) < .06f && Mathf.Abs(c.g - expected.g) < .06f && Mathf.Abs(c.b - expected.b) < .06f) matched++; }
                File.WriteAllBytes(Path.Combine(Arg("-spriteOutput"), "overlap-" + pass + ".png"), pixels.EncodeToPNG());
                Check(matched == 16, "PIXEL overlap pass=" + pass + " expected=" + expected + " center=" + pixels.GetPixel(32, 32) + "; matched=" + matched + "/16");
            }
            visualCompleted = true;
        }
        finally { RenderTexture.active = previous; Object.DestroyImmediate(root); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture); Object.DestroyImmediate(material); Object.DestroyImmediate(pixels); rt.Release(); Object.DestroyImmediate(rt); }
    }
    static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (type == LogType.Exception && message.StartsWith("ArgumentOutOfRangeException: Index was out of range.")
            && stack.Contains("UnityEditor.Search.SearchDatabase+<EnumerateAll>d__80.MoveNext") && stack.Contains("SearchInit.IndexationOnStartup")) searchErrors++;
        else errors++;
    }
}
