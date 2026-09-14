using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Compile alongside the other Tools runners against Unity + Assembly-CSharp.
// Enumerate Run in an initialized, throwaway Main Play session. Never saves assets.
// Impacts are deterministic callback invocations; PhysicsAndPoison uses real frames/physics.
public static class BowDaggerIntegrationChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Vector3 Origin = new Vector3(10000, 10000, 0);
    static readonly List<Object> owned = new List<Object>();
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    public static readonly List<string> Failures = new List<string>();

    public static IEnumerator Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Requires Main Play Mode");
        Passed = Failed = 0; Failures.Clear();
        var manager = Object.FindFirstObjectByType<WorldManager>();
        Check(manager != null && manager.IsInitialized, "Main world manager initialized");
        if (manager == null || !manager.IsInitialized) yield break;
        // This runner intentionally owns a throwaway session, not a live user's game state.
        foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b is Weapon || b is EnemySpawner || b is StateSwitchController) b.enabled = false;
        if (UIController.instance != null) UIController.instance.levelUpPanel.SetActive(false);
        Time.timeScale = 1;
        try
        {
            var worlds = (World[])Get(manager, "worlds");
            Check(worlds.Length == 2, "two Main world configurations");
            foreach (var world in worlds)
            {
                Check(manager.CurrentWorldId == world.WorldId || manager.SwitchWorld(world.WorldId), "switch to " + world.WorldId);
                yield return null;
                var player = world.Player;
                var bow = player.GetComponentInChildren<BowController>(true);
                var dagger = player.GetComponentInChildren<DaggerController>(true);
                Config(bow, player, "arrow", typeof(ArrowController));
                Config(dagger, player, "dagger", typeof(DaggerProjectile));
                if (bow == null || dagger == null) continue;
                try { Volleys(world, bow, dagger); }
                catch (Exception e) { Check(false, world.WorldId + " deterministic exception: " + e); }
                Cleanup();
            }
            var material = worlds.First(w => w.WorldId == WorldId.Material);
            Check(manager.CurrentWorldId == WorldId.Material || manager.SwitchWorld(WorldId.Material), "return to Material");
            yield return null;
            var timing = PhysicsAndPoison(manager, material);
            while (timing.MoveNext()) yield return timing.Current;
        }
        finally { Cleanup(); }
        Debug.Log("BowDaggerIntegrationChecks: " + Passed + " passed, " + Failed + " failed");
    }

    static void Config(Weapon weapon, PlayerController player, string prefabField, Type projectileType)
    {
        string label = World.GetFor(player).WorldId + "/" + projectileType.Name;
        Check(weapon != null, label + " weapon exists");
        if (weapon == null) return;
        Check(player.unassignedWeapons.Contains(weapon) || player.assignedWeapons.Contains(weapon), label + " registered with owner");
        Check(Get(weapon, "buffHolder") == player.GetComponent<BuffController>(), label + " local buff holder");
        Check(World.GetFor(weapon) == World.GetFor(player), label + " owner world");
        var prefab = (GameObject)Get(weapon, prefabField);
        Check(prefab != null && prefab.GetComponent(projectileType) != null && prefab.GetComponent<Collider2D>() != null,
            label + " serialized projectile prefab resolves");
        Check(weapon.stats != null, label + " stats present");
        Check(weapon.icon != null, label + " icon resolves");
        Check(!string.IsNullOrEmpty(weapon.weaponName), label + " display name present");
    }

    static void Volleys(World world, BowController bowConfig, DaggerController daggerConfig)
    {
        var source = Make("Source", world.ContentRoot, Origin).AddComponent<BuffController>();
        var definition = ScriptableObject.CreateInstance<BuffDefinition>(); owned.Add(definition);
        var damage = new DamageBuffAtom(); Set(damage, "damageMultiplier", 2f);
        var count = new ProjectileBuffAtom(); Set(count, "projectileCount", 2);
        Set(definition, "isPermanent", true);
        Set(definition, "atoms", new List<BuffAtom> { damage, count });
        Check(definition.isValid, "buff recipe valid");
        var handle = source.GrantBuff(definition, new object());
        Check(handle != null, "buff granted");
        var a = Enemy(world.ContentRoot, Origin + Vector3.right * 3);
        var b = Enemy(world.ContentRoot, Origin + Vector3.right * 5);
        // A separate active world makes rejection prove ownership, not inactive filtering.
        var other = Make("OtherWorld", null, Vector3.zero).AddComponent<World>();
        var otherRoot = Make("Content", other.transform, Vector3.zero);
        Set(other, "contentRoot", otherRoot); Set(other, "worldId", WorldId.Echo);
        var foreign = Enemy(otherRoot.transform, Origin + Vector3.right);
        var child = Make("ExtraHitbox", a.transform, a.transform.position); child.tag = "Enemy";
        var extra = child.AddComponent<BoxCollider2D>();
        Physics2D.SyncTransforms();
        var bow = Object.Instantiate(bowConfig, source.transform); owned.Add(bow.gameObject);
        var dagger = Object.Instantiate(daggerConfig, source.transform); owned.Add(dagger.gameObject);
        foreach (var weapon in new Weapon[] { bow, dagger })
        {
            weapon.gameObject.SetActive(true); weapon.enabled = false;
            weapon.stats = new WeaponStats { damage = 1.5f, amount = 1, bounces = 0 };
            Set(weapon, "attackDamage", 10f); Set(weapon, "amount", 1.9f);
            Set(weapon, "buffHolder", source); Set(weapon, "attackCounter", 0f);
        }
        Set(bow, "player", world.Player); Set(bow, "projectileSpeed", 0f);
        Set(dagger, "attackRange", 20f); Set(dagger, "projectileSpeed", 0f); Set(dagger, "bounces", 2f);
        Check(Call(dagger, "FindClosestEnemy") == a, "dagger targeting rejects closer foreign enemy");
        var arrowsBefore = new HashSet<ArrowController>(Object.FindObjectsByType<ArrowController>(FindObjectsSortMode.None));
        var daggersBefore = new HashSet<DaggerProjectile>(Object.FindObjectsByType<DaggerProjectile>(FindObjectsSortMode.None));
        Call(bow, "Update"); Call(dagger, "Update");
        var arrows = Object.FindObjectsByType<ArrowController>(FindObjectsSortMode.None).Where(p => !arrowsBefore.Contains(p)).ToArray();
        var daggers = Object.FindObjectsByType<DaggerProjectile>(FindObjectsSortMode.None).Where(p => !daggersBefore.Contains(p)).ToArray();
        foreach (var p in arrows) owned.Add(p.gameObject);
        foreach (var p in daggers) owned.Add(p.gameObject);
        Check(arrows.Length == 4, "bow floor(base count) + buff count = 4");
        Check(daggers.Length == 4, "dagger floor(base count) + buff count = 4");
        Check(arrows.All(p => p.transform.parent == world.ContentRoot), "all arrows world-parented");
        Check(daggers.All(p => p.transform.parent == world.ContentRoot), "all daggers world-parented");
        source.RevokeBuff(handle);
        Check(arrows.Length > 0 && arrows.All(p => Mathf.Approximately(p.damage, 30)), "bow snapshots buffed damage after revoke");
        Check(daggers.Length > 0 && daggers.All(p => Mathf.Approximately(p.damage, 30)), "dagger snapshots buffed damage after revoke");
        Equal(source.CalculateWeaponDamage(15), 15, "source damage reverted independently");
        Check(source.CalculateProjectileCount(2) == 2, "source count reverted independently");
        arrowsBefore.UnionWith(arrows); daggersBefore.UnionWith(daggers);
        Set(bow, "attackCounter", 0f); Set(dagger, "attackCounter", 0f);
        Call(bow, "Update"); Call(dagger, "Update");
        var laterArrows = Object.FindObjectsByType<ArrowController>(FindObjectsSortMode.None).Where(p => !arrowsBefore.Contains(p)).ToArray();
        var laterDaggers = Object.FindObjectsByType<DaggerProjectile>(FindObjectsSortMode.None).Where(p => !daggersBefore.Contains(p)).ToArray();
        foreach (var p in laterArrows) owned.Add(p.gameObject);
        foreach (var p in laterDaggers) owned.Add(p.gameObject);
        Check(laterArrows.Length == 2 && laterArrows.All(p => Mathf.Approximately(p.damage, 15)), "next bow volley uses revoked damage/count");
        Check(laterDaggers.Length == 2 && laterDaggers.All(p => Mathf.Approximately(p.damage, 15)), "next dagger volley uses revoked damage/count");
        if (arrows.Length == 0 || daggers.Length == 0) return;
        var arrow = arrows[0]; var knife = daggers[0];
        Call(arrow, "OnTriggerEnter2D", foreign.GetComponent<Collider2D>());
        Call(knife, "OnTriggerEnter2D", foreign.GetComponent<Collider2D>());
        Equal(foreign.health, 1000, "both projectiles reject active foreign hit");
        Equal(knife.bounces, 2, "foreign hit consumes no bounce");
        Call(arrow, "OnTriggerEnter2D", a.GetComponent<Collider2D>());
        Call(arrow, "OnTriggerEnter2D", extra);
        Equal(a.health, 970, "arrow multicollider damages once");
        Call(arrow, "OnTriggerEnter2D", b.GetComponent<Collider2D>());
        Equal(b.health, 970, "arrow pierces distinct enemy");
        Call(knife, "OnTriggerEnter2D", a.GetComponent<Collider2D>());
        Call(knife, "OnTriggerEnter2D", extra);
        Equal(a.health, 940, "dagger multicollider damages once");
        Equal(knife.bounces, 2, "first impact does not consume extra bounce");
        Equal((float)Get(a, "poisonDamage"), 6, "dagger applies 20 percent snapshot poison");
        Call(knife, "OnTriggerExit2D", a.GetComponent<Collider2D>());
        Check(Call(knife, "FindClosestEnemy", b) == null, "partial collider exit blocks revisit (foreign excluded)");
        Call(knife, "OnTriggerExit2D", extra);
        Call(knife, "OnTriggerEnter2D", b.GetComponent<Collider2D>());
        Equal(b.health, 940, "first bounce damage"); Equal(knife.bounces, 1, "first bounce consumed");
        Call(knife, "OnTriggerExit2D", b.GetComponent<Collider2D>());
        Call(knife, "OnTriggerEnter2D", a.GetComponent<Collider2D>());
        Equal(a.health, 910, "revisit after all exits"); Equal(knife.bounces, 0, "last bounce consumed");
        Call(knife, "OnTriggerEnter2D", b.GetComponent<Collider2D>());
        Equal(b.health, 940, "finished dagger rejects extra callback");
    }

    static IEnumerator PhysicsAndPoison(WorldManager manager, World world)
    {
        var enemy = Enemy(world.ContentRoot, Origin);
        var knife = Make("SleepingDagger", world.ContentRoot, Origin + Vector3.up * 10).AddComponent<DaggerProjectile>();
        knife.speed = 0; knife.range = 20; knife.SetTarget(enemy);
        var extra = Make("PhysicsExtra", enemy.transform, Origin); extra.tag = "Enemy"; extra.AddComponent<BoxCollider2D>();
        var arrow = Make("PhysicsArrow", world.ContentRoot, Origin).AddComponent<ArrowController>();
        arrow.damage = 10; arrow.projectileSpeed = 0;
        arrow.gameObject.AddComponent<CircleCollider2D>().isTrigger = true;
        var rb = arrow.gameObject.AddComponent<Rigidbody2D>(); rb.gravityScale = 0; rb.constraints = RigidbodyConstraints2D.FreezeAll;
        yield return new WaitForSeconds(0.15f);
        Equal(enemy.health, 990, "real physics arrow multicollider delivers one hit");
        enemy.ApplyPoison(2, 3);
        yield return new WaitForSeconds(0.35f);
        Equal(enemy.health, 990, "poison has no immediate tick");
        float next = (float)Get(enemy, "poisonCounter");
        enemy.ApplyPoison(3, 3);
        Equal((float)Get(enemy, "poisonCounter"), next, "refresh preserves scheduled next tick");
        Equal((float)Get(enemy, "poisonDamage"), 5, "refresh stacks damage");
        Equal((float)Get(enemy, "poisonDuration"), 3, "refresh replaces duration");
        float remaining = (float)Get(enemy, "poisonDuration");
        float arrowLife = (float)Get(arrow, "lifetimeRemaining");
        float knifeLife = (float)Get(knife, "lifetimeRemaining");
        float sleepNext = (float)Get(enemy, "poisonCounter");
        Check(manager.SwitchWorld(WorldId.Echo), "sleep poisoned Material");
        yield return new WaitForSeconds(1.2f);
        Equal(enemy.health, 990, "sleep prevents poison damage across real seconds");
        Equal((float)Get(enemy, "poisonCounter"), sleepNext, "sleep freezes next poison tick");
        Equal((float)Get(enemy, "poisonDuration"), remaining, "sleep freezes poison duration");
        Equal((float)Get(arrow, "lifetimeRemaining"), arrowLife, "sleep freezes arrow lifetime");
        Equal((float)Get(knife, "lifetimeRemaining"), knifeLife, "sleep freezes dagger lifetime");
        Check(manager.SwitchWorld(WorldId.Material), "wake poisoned Material");
        yield return new WaitForSeconds(next + 0.15f);
        Equal(enemy.health, 985, "wake resumes stacked tick at preserved schedule");
        Check((float)Get(arrow, "lifetimeRemaining") < arrowLife, "wake resumes arrow lifetime");
        Check((float)Get(knife, "lifetimeRemaining") < knifeLife, "wake resumes dagger lifetime");
        yield return new WaitForSeconds(2.5f);
        Equal(enemy.health, 975, "exactly three stacked poison ticks before expiry");
        Equal((float)Get(enemy, "poisonDamage"), 0, "poison clears at expiry");
        Equal((float)Get(enemy, "poisonDuration"), 0, "duration clears at expiry");
        float beforeShortPoison = enemy.health;
        enemy.ApplyPoison(1, 0.2f);
        yield return new WaitForSeconds(0.35f);
        Equal(enemy.health, beforeShortPoison, "subsecond poison does not tick beyond duration");
    }

    static EnemyController Enemy(Transform parent, Vector3 position)
    {
        var go = Make("Enemy", parent, position); go.tag = "Enemy";
        var rb = go.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Kinematic;
        go.AddComponent<BoxCollider2D>();
        var e = go.AddComponent<EnemyController>(); e.RB = rb; e.health = 1000; e.moveSpeed = 0;
        return e;
    }
    static GameObject Make(string name, Transform parent, Vector3 position)
    {
        var go = new GameObject("BowDaggerChecks_" + name); owned.Add(go);
        go.transform.SetParent(parent, false); go.transform.position = position; return go;
    }
    static void Cleanup()
    {
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
        owned.Clear(); Physics2D.SyncTransforms();
    }
    static FieldInfo Field(object o, string name)
    {
        for (var t = o.GetType(); t != null; t = t.BaseType)
        { var f = t.GetField(name, Flags | BindingFlags.DeclaredOnly); if (f != null) return f; }
        throw new MissingFieldException(o.GetType().Name, name);
    }
    static object Get(object o, string n) { return Field(o, n).GetValue(o); }
    static void Set(object o, string n, object value) { Field(o, n).SetValue(o, value); }
    static object Call(object o, string n, params object[] args) { return o.GetType().GetMethod(n, Flags).Invoke(o, args); }
    static void Equal(float actual, float expected, string label) { Check(Mathf.Abs(actual - expected) < 0.001f, label + " expected=" + expected + " actual=" + actual); }
    static void Check(bool ok, string label)
    {
        if (ok) Passed++;
        else { Failed++; Failures.Add(label); Debug.LogError("BowDaggerIntegrationChecks FAIL: " + label); }
    }
}
