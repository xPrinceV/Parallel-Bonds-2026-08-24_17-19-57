using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// run in a throwaway Main Play session; samples real frames and physics, then removes its observer
public static class DualWorldTimingChecks
{
    public const string ResultKey = "DualWorldTimingChecks.Result";
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static bool running;

    public static void Begin()
    {
        if (!Application.isPlaying || running)
            throw new InvalidOperationException("Requires an initialized Main Play session and no running observer.");
        var manager = Object.FindFirstObjectByType<WorldManager>();
        var ui = UIController.instance;
        if (manager == null || !manager.IsInitialized || ui == null || ui.levelUpPanel.activeSelf || Time.timeScale <= 0f)
            throw new InvalidOperationException("Close upgrade selection and resume game time first.");
        var timer = manager.GetComponent<StateSwitchController>();
        if ((float)Get(timer, "timer") != 15f)
            throw new InvalidOperationException("This check expects the scene's 15-second interval.");

        var saved = new Dictionary<Behaviour, bool>();
        foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b is Weapon || b is EnemySpawner || b is StateSwitchController)
            {
                saved.Add(b, b.enabled);
                b.enabled = false;
            }
        bool background = Application.runInBackground;
        Application.runInBackground = true;
        var created = new List<Object>();
        var log = new List<string>();
        BuffHandle handle = null;
        BuffController holder = null;
        EditorApplication.CallbackFunction observer = null;
        Action finish = () =>
        {
            if (observer != null) EditorApplication.update -= observer;
            if (holder != null && handle != null) holder.RevokeBuff(handle);
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            foreach (var pair in saved) if (pair.Key != null) pair.Key.enabled = pair.Value;
            Application.runInBackground = background;
            running = false;
        };
        try
        {
            if (manager.CurrentWorldId != WorldId.Material && !manager.SwitchWorld(WorldId.Material))
                throw new InvalidOperationException("Could not enter Material.");
            World material = manager.CurrentWorld;
            holder = material.Player.GetComponent<BuffController>();
            var recipe = ScriptableObject.CreateInstance<BuffDefinition>(); created.Add(recipe);
            Set(recipe, "duration", 20f);
            Set(recipe, "atoms", new List<BuffAtom> { new DamageBuffAtom() });
            handle = holder.GrantBuff(recipe, new object());
            Require(handle != null, "timed grant");
            var instance = (BuffInstance)typeof(BuffHandle).GetProperty("Instance", Fields).GetValue(handle, null);

            var point = new Vector3(-1000f, -1000f, 0f);
            Func<string, GameObject> make = name =>
            {
                var obj = new GameObject("DualWorldTiming_" + name);
                obj.transform.SetParent(material.ContentRoot, false);
                obj.transform.position = point;
                created.Add(obj);
                return obj;
            };
            var enemyObject = make("Enemy"); enemyObject.tag = "Enemy";
            var body = enemyObject.AddComponent<Rigidbody2D>(); body.gravityScale = 0; body.constraints = RigidbodyConstraints2D.FreezeAll;
            enemyObject.AddComponent<CircleCollider2D>();
            var enemy = enemyObject.AddComponent<EnemyController>(); enemy.enabled = false; enemy.RB = body; enemy.health = 10000f;
            var fireObject = make("Fire"); fireObject.AddComponent<CircleCollider2D>().isTrigger = true;
            var fire = fireObject.AddComponent<LanternFire>();
            fire.SetDamage(2f); fire.SetDuration(60f); fire.SetBuffSource(holder); fire.tickRate = 100f; fire.tickCounter = 100f;
            var vfx = make("ShortEffect").AddComponent<DestroyGameObject>(); vfx.duration = 1f;
            var projectileObject = make("Projectile");
            projectileObject.transform.position += Vector3.right * 10f;
            var projectile = projectileObject.AddComponent<LanternProjController>();
            projectile.RB = projectileObject.AddComponent<Rigidbody2D>(); projectile.RB.gravityScale = 0f;
            projectile.minForce = projectile.maxForce = projectile.vertForce = 0f;
            var xp = material.Player.GetComponent<ExperienceLevelController>();
            var orb = Object.Instantiate(xp.pickup, point + Vector3.up * 10f, Quaternion.identity, material.ContentRoot);
            created.Add(orb.gameObject); orb.expValue = 7;

            int phase = 0;
            float phaseStart = Time.time;
            float buffTime = 0, fireTime = 0, effectTime = 0;
            double deadline = EditorApplication.timeSinceStartup + 45d;
            observer = () =>
            {
                try
                {
                    Require(Application.isPlaying, "Play Mode remained active");
                    Require(EditorApplication.timeSinceStartup < deadline, "45-second deadline");
                    if (phase == 0)
                    {
                        if (fire.burningList.Count == 0) return;
                        Require(enemy.health == 9999f, "real physics entry applies exactly one damage");
                        buffTime = instance.RemainingDuration; fireTime = fire.durationCounter;
                        effectTime = (float)Get(vfx, "remainingDuration");
                        Require(effectTime > 0, "short effect initialized before sleep");
                        Require(manager.SwitchWorld(WorldId.Echo), "sleep Material");
                        phase = 1; phaseStart = Time.time;
                        log.Add("Material slept after real fire entry; HP=9999");
                    }
                    else if (phase == 1 && Time.time - phaseStart >= 2f)
                    {
                        Require(vfx != null && projectile != null && orb != null && enemy != null, "sleep retains effects, projectile, XP and enemy");
                        Require(!material.IsActive && holder.IsWorldSuspended && handle.IsActive, "world and holder asleep");
                        Require(instance.RemainingDuration == buffTime && fire.durationCounter == fireTime
                            && (float)Get(vfx, "remainingDuration") == effectTime && enemy.health == 9999f, "two real seconds consume no sleeping lifetime or health");
                        Require(manager.SwitchWorld(WorldId.Material), "wake Material");
                        phase = 2; phaseStart = Time.time;
                        log.Add("2s sleep retained exact Buff/fire/effect timers and resource instances");
                    }
                    else if (phase == 2 && Time.time - phaseStart >= 0.1f)
                    {
                        Require(enemy.health == 9999f && fire.burningList.Count == 1, "wake does not repeat fire entry damage");
                        Set(timer, "timerCounter", 15f);
                        timer.enabled = true;
                        phase = 3; phaseStart = Time.time;
                        log.Add("wake physics preserved fire contact; started full 15s automatic interval");
                    }
                    else if (phase == 3 && manager.CurrentWorldId == WorldId.Echo)
                    {
                        float elapsed = Time.time - phaseStart;
                        Require(elapsed >= 14.95f && elapsed < 16f, "natural 15-second switch");
                        Require(!material.IsActive && manager.CurrentWorld.IsActive && !manager.IsSwitching, "exclusive content and released switch guard");
                        Require(PlayerController.instance == manager.CurrentWorld.Player && PlayerHealth.instance == PlayerController.instance.GetComponent<PlayerHealth>()
                            && ExperienceLevelController.instance == PlayerController.instance.GetComponent<ExperienceLevelController>(), "active hero aliases");
                        Require(vfx == null && projectile == null && orb != null && enemy != null, "resumed expiry and retained nonexpiring resources");
                        Require(handle.IsActive && instance.RemainingDuration > 0f && instance.RemainingDuration < buffTime - 14f, "Buff counts only active time");
                        var filter = Object.FindFirstObjectByType<WorldFilter>();
                        var image = (Image)Get(filter, "overlay");
                        Require(image.color == manager.CurrentWorld.AmbientColor, "real LateUpdate applied Echo filter");
                        log.Add("automatic switch after " + elapsed + "s; aliases/filter correct, resources retained, active lifetimes resumed");
                        SessionState.SetString(ResultKey, "PASS\n" + string.Join("\n", log));
                        finish();
                    }
                }
                catch (Exception e)
                {
                    SessionState.SetString(ResultKey, "FAIL\n" + string.Join("\n", log) + "\n" + e);
                    finish();
                }
            };
            running = true;
            SessionState.SetString(ResultKey, "running");
            EditorApplication.update += observer;
        }
        catch
        {
            finish();
            throw;
        }
    }

    private static object Get(object obj, string name) => obj.GetType().GetField(name, Fields).GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Fields).SetValue(obj, value);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
