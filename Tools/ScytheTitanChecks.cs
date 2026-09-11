using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Disposable Main Play session. Config uses actual scene references, impacts are explicitly
// labelled callback or physics; timers/animation run on real frames. Never saves assets.
public static class ScytheTitanChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<Object> owned = new List<Object>();
    static readonly Vector3 Origin = new Vector3(10000, 10000, 0);
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }

    public static IEnumerator Run()
    {
        Passed = Failed = 0;
        var manager = Object.FindFirstObjectByType<WorldManager>();
        if (!Application.isPlaying || manager == null || !manager.IsInitialized)
            throw new InvalidOperationException("Requires initialized Main Play session");
        foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b is Weapon || b is EnemySpawner || b is StateSwitchController) b.enabled = false;
        UIController.instance.levelUpPanel.SetActive(false); Time.timeScale = 1;
        var worlds = (World[])Get(manager, "worlds");
        try
        {
            foreach (var world in worlds)
            {
                Check(manager.CurrentWorld == world || manager.SwitchWorld(world.WorldId), "initialize " + world.WorldId);
                yield return null;
                Config(world);
                AreaUI(world.Player);
            }
            foreach (var world in worlds)
            {
                Check(manager.CurrentWorld == world || manager.SwitchWorld(world.WorldId), "Scythe select " + world.WorldId);
                world.Player.transform.position = Origin;
                var routine = Scythe(manager, world, worlds.Single(w => w != world));
                while (routine.MoveNext()) yield return routine.Current;
                Cleanup();
            }
            foreach (var world in worlds)
            {
                Check(manager.CurrentWorld == world || manager.SwitchWorld(world.WorldId), "Titan select " + world.WorldId);
                world.Player.transform.position = Origin;
                var routine = Titan(manager, world, worlds.Single(w => w != world));
                while (routine.MoveNext()) yield return routine.Current;
                Cleanup();
            }
            // Shared death is irreversible; run it last in the disposable session.
            Check(manager.TryEnterFusion(), "death enter fusion");
            var deadWorld = worlds.Single(w => w != manager.CurrentWorld);
            var titan = SpawnTitan(deadWorld);
            Set(titan, "cooldownCounter", 100f);
            var liveAttacks = new HashSet<TitanAttack>(Attacks());
            titan.ChannelAttack();
            var deathPending = Attacks().Where(a => !liveAttacks.Contains(a)).ToArray();
            foreach (var a in deathPending) owned.Add(a.gameObject);
            Check(titan.isActiveAndEnabled && deathPending.Length == 1, "live Titan can attack before shared player death");
            var hp = manager.FusionPlayer.GetComponent<PlayerHealth>();
            hp.DamageHandler(hp.currentHealth + 1);
            Time.timeScale = 1;
            int before = Attacks().Length;
            titan.ChannelAttack();
            yield return null;
            Check(Attacks().Length == before && worlds.All(w => w.Player.GetComponent<PlayerHealth>().IsDead), "shared player death prevents Titan attacks");
        }
        finally { Time.timeScale = 1; Cleanup(); }
        Debug.Log("ScytheTitanChecks: " + Passed + " passed, " + Failed + " failed");
    }

    static void Config(World world)
    {
        var hero = world.Player;
        var weapons = hero.GetComponentsInChildren<Weapon>(true);
        Check(weapons.Length == 6 && hero.assignedWeapons.Count + hero.unassignedWeapons.Count == 6,
            world.WorldId + " exactly six configured/registered weapons actual=" + weapons.Length);
        var scythe = hero.GetComponentInChildren<ScytheController>(true);
        Check(scythe != null && hero.unassignedWeapons.Contains(scythe) && !hero.assignedWeapons.Contains(scythe)
            && !scythe.gameObject.activeSelf, world.WorldId + " Scythe unassigned and inactive");
        Check(scythe != null && scythe.icon != null && scythe.stats != null && !string.IsNullOrEmpty(scythe.weaponName), "Scythe icon/stats/name resolve");
        var prefab = (GameObject)Get(scythe, "scythe");
        Check(prefab != null && prefab.GetComponent<ScytheHitController>() != null, "scene Scythe prefab root resolves: " + AssetDatabase.GetAssetPath(prefab));
        var swing = prefab.GetComponent<ScytheHitController>();
        Check(Get(swing, "hitboxPivot") != null && Get(swing, "animationTransform") != null
            && prefab.GetComponentInChildren<ScytheHitbox>() != null && prefab.GetComponentInChildren<Collider2D>().isTrigger,
            "Scythe pivot/visual/forwarder/trigger references");
        var visual = (Transform)Get(swing, "animationTransform");
                Check(visual.GetComponent<SpriteRenderer>() != null && visual.GetComponent<SpriteRenderer>().sprite != null,
                    "Scythe authored visual sprite resolves (procedural swing; no Animator trigger dependency)");
        var spawners = world.GetComponentsInChildren<EnemySpawner>(true);
        Check(spawners.Length > 0, world.WorldId + " scene spawner exists");
        foreach (var spawner in spawners)
        {
            var last = spawner.waves.Last().enemiesToSpawn;
            Check(last.Any(p => p != null && p.GetComponent<TitanEnemyController>() != null), world.WorldId + " final wave includes Titan");
            Check(!spawner.waves.Take(spawner.waves.Count - 1).SelectMany(w => w.enemiesToSpawn)
                .Any(p => p != null && p.GetComponent<TitanEnemyController>() != null), "Titan absent from earlier waves");
        }
        var titan = TitanConfig(world);
        Check(titan.RB != null && titan.GetComponent<Collider2D>() != null && titan.GetComponentInChildren<SpriteRenderer>().sprite != null,
            "scene final-wave Titan body/sprite references");
        var attack = titan.titanAttack.GetComponent<TitanAttack>();
        Check(attack != null && Get(attack, "attackCollider") != null && attack.effectAnimation != null && !attack.effectAnimation.activeSelf,
            "Titan attack collider/effect references and initially hidden effect");
        Equal(attack.attackDelay, 2.5f, "serialized Titan delay");
        Check(titan.titanAttack.GetComponentsInChildren<SpriteRenderer>(true).All(r => r.sprite != null), "Titan indicator/effect sprites resolve");
        AnimationConfig(attack.effectAnimation.GetComponent<Animator>(), "TitanAttack");
    }

    static void AnimationConfig(Animator animator, string label)
    {
        var controller = animator == null ? null : animator.runtimeAnimatorController;
        Check(controller != null, label + " controller resolves");
        if (controller == null) return;
        var asset = controller as UnityEditor.Animations.AnimatorController;
        Check(asset != null && asset.layers.Length > 0, label + " controller has playable layers path=" + AssetDatabase.GetAssetPath(controller));

        var clips = controller.animationClips;
        Check(clips.Length > 0, label + " animation clips count=" + clips.Length);
        var spriteKeys = clips.SelectMany(c => AnimationUtility.GetObjectReferenceCurveBindings(c)
            .Where(b => b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite")
            .SelectMany(b => AnimationUtility.GetObjectReferenceCurve(c, b))).ToArray();
        Check(spriteKeys.Length > 1 && spriteKeys.All(k => k.value is Sprite), label + " animated sprite key references count=" + spriteKeys.Length);
    }

    static void AreaUI(PlayerController hero)
    {
        var sceneWeapon = hero.GetComponentInChildren<ScytheController>(true);
        Debug.Log("ScytheTitanChecks CONFIG " + World.GetFor(hero).WorldId + " Area available="
            + sceneWeapon.availableUpgrades.Contains(UpgradeType.Area) + "; choices="
            + (sceneWeapon.stats.areaUpgrades == null ? 0 : sceneWeapon.stats.areaUpgrades.Length) + " (scene availability pending)");
        // Force only the disposable clone's choices, so missing scene Area offerings cannot skip code coverage.
        var weapon = Object.Instantiate(sceneWeapon, hero.transform); owned.Add(weapon.gameObject);
        weapon.enabled = false;
        weapon.stats = new WeaponStats { area = 1.25f, damage = 2f, areaUpgrades = new[] { 0.5f } };
        weapon.availableUpgrades = new[] { UpgradeType.Area };
        var button = Object.FindObjectsByType<LevelUpSelectionButton>(FindObjectsInactive.Include, FindObjectsSortMode.None).First();
        Check(button.upgradeDescText != null && button.nameLevelText != null && button.weaponIcon != null, "actual Area UI text/icon fields wired");
        button.UpdateButtonDisplay(weapon);
        Check(button.upgradeDescText.text == "+50% Area Size" && button.weaponIcon.sprite == weapon.icon && button.nameLevelText.text == weapon.weaponName,
            "Area UI displays exact selected value/name/icon actual=" + button.upgradeDescText.text);
        Equal((float)Get(button, "selectedUpgrade"), 0.5f, "Area selected value");
        Check((UpgradeType)Get(button, "selectedUpgradeType") == UpgradeType.Area, "Area selected enum");
        button.SelectUpgrade();
        Equal(weapon.stats.area, 1.75f, "Area selection applies stat"); Equal(weapon.stats.damage, 2f, "Area leaves damage unchanged");
        Object.DestroyImmediate(weapon.gameObject);
    }

    static IEnumerator Scythe(WorldManager manager, World world, World other)
    {
        var hero = world.Player; hero.facingDirection = Vector2.up;
        var holder = hero.GetComponent<BuffController>();
        var weapon = Object.Instantiate(hero.GetComponentInChildren<ScytheController>(true), hero.transform); owned.Add(weapon.gameObject);
        weapon.enabled = false; weapon.gameObject.SetActive(true);
        yield return null;
        // Disabled Start may not run: initialize through the production method, then assert its binding.
        Call(weapon, "Start");
        Check(Get(weapon, "player") == hero && Get(weapon, "buffHolder") == holder, world.WorldId + " Scythe binds own hero/holder");
        weapon.stats = new WeaponStats { damage = 1.5f, amount = 99f, area = 1.5f };
        Set(weapon, "attackDamage", 10f); Set(weapon, "area", 2f);
        var baseSwings = Fire(weapon);
        Check(baseSwings.Length == 1 && holder.CalculateProjectileCount(1) == 1, "Scythe base count ignores stats.amount=99 actual=" + baseSwings.Length);
        foreach (var s in baseSwings) Object.DestroyImmediate(s.gameObject);
        var damage = new DamageBuffAtom(); Set(damage, "damageMultiplier", 2f);
        var count = new ProjectileBuffAtom(); Set(count, "projectileCount", 2);
        var recipe = Recipe(damage, count);
        var handle = holder.GrantBuff(recipe, new object());
        float expected = holder.CalculateWeaponDamage(15f);
        int expectedCount = holder.CalculateProjectileCount(1);
        var swings = Fire(weapon);
        Check(expectedCount == 3 && swings.Length == 3, "buff adds two to single swing actual=" + swings.Length);
        for (int i = 0; i < swings.Length; i++)
        {
            var s = swings[i];
            Check(s.transform.parent == world.ContentRoot && Get(s, "buffSource") == holder, "swing keeps source world and own holder");
        }
        var angles = swings.Select(s => Mathf.Repeat((float)Get(s, "startAngle"), 360f)).OrderBy(a => a).ToArray();
        for (int i = 0; i < angles.Length; i++) Equal(Mathf.Repeat(angles[(i + 1) % angles.Length] - angles[i], 360), 120, "even angular spacing");
        holder.RevokeBuff(handle);
        foreach (var s in swings) Equal((float)Get(s, "damage"), expected, "damage snapshot after revoke");
        var swing = swings[0];
        var recorder = new HitRecorder(); var stack = new StackToActivationBuffAtom();
        Set(stack, "condition", recorder); Set(stack, "stackCount", 10000); Set(stack, "activation", new NoopActivation());
        var reportHandle = holder.GrantBuff(Recipe(stack), new object());
        var enemy = Enemy(world.ContentRoot, Origin + Vector3.right * 20);
        var extra = Make("ExtraEnemyCollider", enemy.transform, enemy.transform.position).AddComponent<BoxCollider2D>();
        var hitbox = swing.GetComponentInChildren<ScytheHitbox>();
        Call(hitbox, "OnTriggerEnter2D", enemy.GetComponent<Collider2D>()); Call(hitbox, "OnTriggerEnter2D", extra);
        Equal(enemy.health, 1000 - expected, "Scythe callback multi-collider dedup health");
        Check(recorder.Events.Count == 1 && recorder.Events[0].Source == hero.gameObject && recorder.Events[0].Target == enemy.gameObject,
            "one actual hit report source/target");
        Equal(recorder.Events[0].DamageDealt, expected, "actual nonlethal hit report");
        var lethal = Enemy(world.ContentRoot, Origin + Vector3.right * 22); lethal.health = 3;
        swing.HitEnemy(lethal.GetComponent<Collider2D>());
        Equal(recorder.Events.Last().DamageDealt, 3, "lethal hit report clamps to actual remaining HP");
        var foreign = Enemy(other.ContentRoot, Origin + Vector3.right * 25);
        // Wake only the content temporarily: prove strict identity rejection even with an active foreign enemy.
        other.ContentRoot.gameObject.SetActive(true);
        swing.HitEnemy(foreign.GetComponent<Collider2D>());
        Equal(foreign.health, 1000, "old worlds strict Scythe rejection with active foreign collider");
        other.ContentRoot.gameObject.SetActive(false);
        foreach (var s in swings.Skip(1)) Object.DestroyImmediate(s.gameObject);
        var sprite = swing.GetComponentInChildren<SpriteRenderer>();
        var visual = (Transform)Get(swing, "animationTransform");
        var authored = ((Transform)Get(((GameObject)Get(weapon, "scythe")).GetComponent<ScytheHitController>(), "animationTransform")).localRotation;
        var pivot = (Transform)Get(swing, "hitboxPivot");
        var rotation = pivot.localRotation;
        var visualRotation = visual.localRotation;
        yield return new WaitForSeconds(0.12f);
        Check(swing != null && sprite.enabled && sprite.sprite != null && sprite.color.a > 0, "live Scythe visible sprite state");
        Check(Quaternion.Angle(visualRotation, visual.localRotation) > 1, "live Scythe visual rotates procedurally");
        Check(Quaternion.Angle(visual.localRotation, pivot.localRotation * authored) < 0.05f,
            "visual sweep preserves authored alignment including Y flip");
        Check(Quaternion.Angle(rotation, pivot.localRotation) > 1, "live Scythe hitbox rotates");
        Equal(swing.transform.localScale.x, 3, "Scythe area scales swing");
        float timer = (float)Get(swing, "swingTimer"); rotation = pivot.localRotation; visualRotation = visual.localRotation;
        Check(manager.SwitchWorld(other.WorldId), "sleep Scythe world");
        yield return new WaitForSeconds(0.6f);
        Check(swing != null, "Scythe survives longer-than-duration world sleep");
        Equal((float)Get(swing, "swingTimer"), timer, "Scythe timer frozen");
        Check(pivot.localRotation == rotation && visual.localRotation == visualRotation, "Scythe hitbox and visual rotations frozen");
        Check(manager.TryEnterFusion(), "wake old Scythe through fusion");
        swing.HitEnemy(foreign.GetComponent<Collider2D>());
        Equal(foreign.health, 1000 - expected, "fused Scythe hits formerly foreign world");
        swing.HitEnemy(extra);
        Equal(enemy.health, 1000 - expected, "Scythe dedup survives sleep/fusion reentry");
        yield return null;
        Check((float)Get(swing, "swingTimer") > timer && Quaternion.Angle(visualRotation, visual.localRotation) > 0.01f, "fusion resumes Scythe timer and visible rotation");
        yield return new WaitForSeconds(0.45f);
        Check(swing == null, "Scythe expires by active lifetime after resume");
        Check(manager.TryExitFusion(), "exit Scythe fusion");
        Check(manager.SwitchWorld(world.WorldId), "return to Scythe source");
        holder.RevokeBuff(reportHandle);
        var later = Fire(weapon);
        Check(later.Length == holder.CalculateProjectileCount(1), "next volley uses revoked count actual=" + later.Length);
        foreach (var s in later) Equal((float)Get(s, "damage"), holder.CalculateWeaponDamage(15), "next volley recalculates damage");
        // Real physics delivery through the actual prefab's child trigger; large stationary target covers its arc.
        var physical = Enemy(world.ContentRoot, hero.transform.position);
        ((BoxCollider2D)physical.GetComponent<Collider2D>()).size = Vector2.one * 10;
        float total = later.Sum(s => (float)Get(s, "damage"));
        Physics2D.SyncTransforms(); yield return new WaitForSeconds(0.15f);
        Equal(physical.health, 1000 - total, "real physics Scythe hitbox forwards exactly one hit per swing");
    }

    static IEnumerator Titan(WorldManager manager, World world, World other)
    {
        var hero = world.Player; var hp = hero.GetComponent<PlayerHealth>(); hp.currentHealth = hp.maxHealth;
        var titan = SpawnTitan(world);
        titan.range = 100; titan.attackCooldown = 100; titan.attack = 7; titan.moveSpeed = 0;
        titan.transform.position = hero.transform.position + Vector3.right * 10;
        var before = new HashSet<TitanAttack>(Attacks());
        yield return new WaitForSeconds(0.12f);
        Check((bool)Get(titan, "startAttack") && Get(titan, "target") == hero.transform, "Titan starts channel against live local hero");
        Equal(titan.RB.linearVelocity.magnitude, 0, "Titan stops while channeling");
        float channel = (float)Get(titan, "attackChannelCounter");
        Check(manager.SwitchWorld(other.WorldId), "sleep pending Titan channel");
        yield return new WaitForSeconds(0.7f);
        Equal((float)Get(titan, "attackChannelCounter"), channel, "pending Titan channel freezes");
        Check(Attacks().All(a => before.Contains(a)), "pending sleeping Titan produces no attack");
        Check(manager.SwitchWorld(world.WorldId), "resume pending Titan channel");
        hero.transform.position += Vector3.up * 3;
        yield return new WaitForSeconds(channel + 0.06f);
        var spawned = Attacks().Where(a => !before.Contains(a)).ToArray();
        foreach (var a in spawned) owned.Add(a.gameObject);
        Check(spawned.Length == 1, "resumed channel spawns exactly one attack actual=" + spawned.Length);
        var attack = spawned.Single();
        titan.enabled = false;
        Check(attack.transform.parent == world.ContentRoot && attack.transform.position == hero.transform.position, "Titan snapshots latest target location and source parent");
        Equal(attack.damage, 7, "Titan snapshots damage");
        var collider = (CircleCollider2D)Get(attack, "attackCollider");
        Check(!collider.enabled && !attack.effectAnimation.activeSelf, "telegraph keeps damage collider/effect off");
        float health = hp.currentHealth;
        Call(attack, "OnTriggerEnter2D", hero.GetComponent<Collider2D>());
        Equal(hp.currentHealth, health, "telegraph callback cannot damage");
        float delay = attack.attackDelay, life = (float)Get(attack, "lifetimeRemaining");
                Equal(life - delay, 0.5f, "Titan lifetime is three active seconds, half a second beyond telegraph");
        Check(manager.SwitchWorld(other.WorldId), "sleep pending Titan telegraph");
        yield return new WaitForSeconds(3.2f);
        Check(attack != null, "Titan telegraph survives more than three wall seconds asleep");
        Equal(attack.attackDelay, delay, "Titan attack delay frozen"); Equal((float)Get(attack, "lifetimeRemaining"), life, "Titan lifetime frozen");
        Check(manager.SwitchWorld(world.WorldId), "resume Titan telegraph");
        yield return new WaitForSeconds(Mathf.Max(0, delay - 0.2f));
        Check(!collider.enabled && !attack.effectAnimation.activeSelf, "Titan still harmless before 2.5 active seconds");
        yield return new WaitForSeconds(0.27f);
        Check(collider.enabled && attack.effectAnimation.activeSelf, "Titan activates after 2.5 active seconds");
        Equal(hp.currentHealth, health - 7, "real physics Titan delivers one delayed hit");
        var extra = Make("PlayerExtra", hero.transform, hero.transform.position).AddComponent<BoxCollider2D>();
        Call(attack, "OnTriggerEnter2D", extra); Call(attack, "OnTriggerEnter2D", hero.GetComponent<Collider2D>());
        Equal(hp.currentHealth, health - 7, "Titan per-player multi-collider dedup");
        Check(manager.SwitchWorld(other.WorldId), "sleep active Titan attack");
        yield return new WaitForSeconds(0.2f);
        Check(manager.SwitchWorld(world.WorldId), "resume active Titan attack");
        Call(attack, "OnTriggerEnter2D", extra);
        Equal(hp.currentHealth, health - 7, "Titan dedup survives sleep/reentry");
        yield return new WaitForSeconds(0.6f);
        Check(attack == null, "Titan expires at three active seconds");
        Object.DestroyImmediate(extra.gameObject);
        // A fresh old-world attack is reused across fusion, never reparented to the active hero.
        titan.enabled = true; Set(titan, "cooldownCounter", 100f);
        before = new HashSet<TitanAttack>(Attacks()); titan.ChannelAttack();
        var old = Attacks().Single(a => !before.Contains(a)); owned.Add(old.gameObject);
        old.transform.position = Origin + Vector3.right * 50;
        Set(old, "channelFinished", true); ((CircleCollider2D)Get(old, "attackCollider")).enabled = true;
        other.ContentRoot.gameObject.SetActive(true);
        float beforeStrict = hp.currentHealth;
        Call(old, "OnTriggerEnter2D", other.Player.GetComponent<Collider2D>());
        Equal(hp.currentHealth, beforeStrict, "old-world Titan strict rejection of active foreign player");
        other.ContentRoot.gameObject.SetActive(false);
        Check(manager.SwitchWorld(other.WorldId), "select opposite hero for fusion retarget");
        Check(manager.TryEnterFusion(), "enter Titan retarget fusion");
        before = new HashSet<TitanAttack>(Attacks()); titan.ChannelAttack();
        var retarget = Attacks().Single(a => !before.Contains(a)); owned.Add(retarget.gameObject);
        Check(Get(titan, "target") == manager.FusionPlayer.transform && retarget.transform.position == manager.FusionPlayer.transform.position
            && retarget.transform.parent == world.ContentRoot, "Titan fusion retarget uses entry hero but source parenting");
        float shared = hp.currentHealth;
        Call(old, "OnTriggerEnter2D", world.Player.GetComponent<Collider2D>());
        Equal(hp.currentHealth, shared, "fused Titan rejects secondary body");
        Call(old, "OnTriggerEnter2D", other.Player.GetComponent<Collider2D>());
        Call(old, "OnTriggerEnter2D", other.Player.GetComponent<Collider2D>());
        Equal(hp.currentHealth, shared - 7, "old attack fused cross-world shared damage exactly once");
        Equal(other.Player.GetComponent<PlayerHealth>().currentHealth, shared - 7, "both heroes see same one-hit shared damage");
        int attackCount = Attacks().Length;
        titan.TakeDamage(titan.health + 1); titan.ChannelAttack(); Call(titan, "Update");
        Check(Attacks().Length == attackCount, "dead Titan cannot channel or Update-spawn attacks before deferred destruction");
        Check(manager.TryExitFusion(), "exit Titan fusion");
        yield return null;
    }

    static ScytheHitController[] Fire(ScytheController weapon)
    {
        var before = new HashSet<ScytheHitController>(Object.FindObjectsByType<ScytheHitController>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        Set(weapon, "attackCounter", 0f); Call(weapon, "Update");
        var result = Object.FindObjectsByType<ScytheHitController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(s => !before.Contains(s)).ToArray();
        foreach (var s in result) owned.Add(s.gameObject);
        return result;
    }
    static TitanEnemyController TitanConfig(World world)
    {
        return world.GetComponentsInChildren<EnemySpawner>(true).SelectMany(s => s.waves.Last().enemiesToSpawn)
            .Select(p => p.GetComponent<TitanEnemyController>()).First(t => t != null);
    }
    static TitanEnemyController SpawnTitan(World world)
    {
        var t = Object.Instantiate(TitanConfig(world), world.ContentRoot); owned.Add(t.gameObject);
        t.transform.position = Origin + Vector3.right * 10; t.expDrop = 0; return t;
    }
    static TitanAttack[] Attacks() { return Object.FindObjectsByType<TitanAttack>(FindObjectsInactive.Include, FindObjectsSortMode.None); }
    static BuffDefinition Recipe(params BuffAtom[] atoms)
    {
        var d = ScriptableObject.CreateInstance<BuffDefinition>(); owned.Add(d);
        Set(d, "isPermanent", true); Set(d, "atoms", atoms.ToList()); return d;
    }
    public class HitRecorder : StackCondition
    {
        public readonly List<StackEventContext> Events = new List<StackEventContext>();
        public override bool isValid { get { return true; } }
        public override bool Matches(StackEventContext context, GameObject owner) { Events.Add(context); return false; }
    }
    public class NoopActivation : StackActivation
    {
        public override bool isValid { get { return true; } }
        public override bool TryActivate(BuffActivationContext context) { return true; }
    }
    static EnemyController Enemy(Transform parent, Vector3 position)
    {
        var go = Make("Enemy", parent, position); go.tag = "Enemy";
        var rb = go.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Kinematic;
        go.AddComponent<BoxCollider2D>(); var e = go.AddComponent<EnemyController>();
        e.RB = rb; e.health = 1000; e.moveSpeed = 0; e.expDrop = 0; return e;
    }
    static GameObject Make(string name, Transform parent, Vector3 position)
    {
        var go = new GameObject("ScytheTitanChecks_" + name); owned.Add(go);
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
    static object Get(object o, string name) { return Field(o, name).GetValue(o); }
    static void Set(object o, string name, object value) { Field(o, name).SetValue(o, value); }
    static object Call(object o, string name, params object[] args) { return o.GetType().GetMethod(name, Flags).Invoke(o, args); }
    static void Equal(float actual, float expected, string label) { Check(Mathf.Abs(actual - expected) < 0.001f, label + " expected=" + expected + " actual=" + actual); }
    static void Check(bool ok, string label)
    {
        if (ok) Passed++; else Failed++;
        Debug.Log("ScytheTitanChecks " + (ok ? "PASS: " : "FAIL: ") + label);
    }
}
