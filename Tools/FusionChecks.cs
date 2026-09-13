using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Disposable initialized Main Play session only. Death is deliberately last.
// Projectile impacts and XP collection below use real physics, not trigger reflection.
public static class FusionChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<Object> owned = new List<Object>();
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    static readonly Queue<string> expectedErrors = new Queue<string>();
    static bool expectingErrors;

    // The batch observer accepts only exact errors within the synchronous fault injection.
    public static bool ConsumeExpectedError(string message)
    {
        if (!expectingErrors || expectedErrors.Count == 0 || expectedErrors.Peek() != message) return false;
        expectedErrors.Dequeue();
        return true;
    }

    public static IEnumerator Run()
    {
        Passed = Failed = 0;
        if (!Application.isPlaying) throw new InvalidOperationException("Requires Main Play Mode");
        var manager = Object.FindFirstObjectByType<WorldManager>();
        if (manager == null || !manager.IsInitialized) throw new InvalidOperationException("Main manager not initialized");
        var worlds = (World[])Get(manager, "worlds");
        var timer = manager.GetComponent<StateSwitchController>();
        foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b is Weapon || b is EnemySpawner || b is StateSwitchController) b.enabled = false;
        UIController.instance.levelUpPanel.SetActive(false); Time.timeScale = 1;
        var cold = ColdStart(manager, worlds.Single(w => w != manager.CurrentWorld));
        while (cold.MoveNext()) yield return cold.Current;
        // Subsequent directional checks use initialized heroes.
        foreach (var w in worlds)
        {
            if (manager.CurrentWorld != w) Check(manager.SwitchWorld(w.WorldId), "initialize " + w.WorldId);
            yield return null;
        }
        Check((KeyCode)Get(manager, "fusionKey") == KeyCode.F, "serialized debug binding is F");
        string source = File.ReadAllText("Assets/Game/Features/Worlds/WorldManager.cs").Replace("\r\n", "\n");
        Check(source.Contains("Input.GetKeyDown(fusionKey)") && source.Contains("if (IsFused)\n                TryExitFusion();\n            else\n                TryEnterFusion();"),
            "SOURCE ONLY: F dispatches enter/exit (no injected keyboard event)");
        string playerSource = File.ReadAllText("Assets/Game/Features/Player/PlayerController.cs").Replace("\r\n", "\n");
        Check(playerSource.Contains("manager.IsFused && manager.FusionPlayer != this)\n            return;"),
            "SOURCE ONLY: secondary ignores movement input");
        try
        {
            foreach (var entry in worlds)
            {
                var other = worlds.Single(w => w != entry);
                if (manager.CurrentWorld != entry) Check(manager.SwitchWorld(entry.WorldId), "select " + entry.WorldId);
                var routine = Direction(manager, entry, other, timer);
                while (routine.MoveNext()) yield return routine.Current;
                Cleanup();
            }
            var live = LiveWorlds(manager, worlds);
            while (live.MoveNext()) yield return live.Current;
            Cleanup();
            var deathEntry = manager.CurrentWorld;
            var deathSecondary = worlds.Single(w => w != deathEntry);
            var deathSnapshot = new Snapshot(deathSecondary.Player);
            Check(manager.TryEnterFusion(), "enter for lethal shared HP check");
            var entryHealth = manager.FusionPlayer.GetComponent<PlayerHealth>();
            entryHealth.DamageHandler(entryHealth.currentHealth + 1);
            Check(!manager.IsFused && manager.FusionPlayer == null, "death cleans up fusion");
            Check(worlds.All(w => w.Player.GetComponent<PlayerHealth>().IsDead && w.Player.GetComponent<PlayerHealth>().currentHealth == 0), "both heroes retain shared zero HP and death latch");
            Check(!deathEntry.Player.gameObject.activeSelf && deathSecondary.Player.gameObject.activeSelf
                && !deathSecondary.IsActive && !deathSecondary.Player.gameObject.activeInHierarchy && deathSnapshot.Same(),
                "death disables entry only; secondary activeSelf and exact state preserved under sleeping content");
            Check(PlayerController.instance == null && PlayerHealth.instance == null && ExperienceLevelController.instance == null, "death clears current aliases");
            entryHealth.currentHealth = entryHealth.maxHealth;
            deathSecondary.Player.GetComponent<PlayerHealth>().currentHealth = entryHealth.maxHealth;
            Check(worlds.All(w => w.Player.GetComponent<PlayerHealth>().currentHealth == 0), "shared death latch rejects attempted refill");
            Time.timeScale = 1; UIController.instance.levelUpPanel.SetActive(false);
            Check(!manager.TryEnterFusion() && !manager.TryExitFusion() && !manager.SwitchWorld(worlds.Single(w => w != manager.CurrentWorld).WorldId), "death cannot escape through fusion or world switch");
        }
        finally { Time.timeScale = 1; Cleanup(); }
        Debug.Log("FusionChecks: " + Passed + " passed, " + Failed + " failed");
    }

    static IEnumerator Direction(WorldManager manager, World entry, World other, StateSwitchController timer)
    {
        string label = entry.WorldId + ": ";
        var hero = entry.Player; var secondary = other.Player;
        hero.transform.position = new Vector3(10000, 10000, 0);
        secondary.transform.SetPositionAndRotation(new Vector3(10020, 10030, 0), Quaternion.Euler(0, 0, 23));
        secondary.transform.localScale = new Vector3(1.2f, 0.8f, 1);
        secondary.facingDirection = Vector2.down;
        // Mixed enabled states ensure restoration is not just enabling everything.
        var extra = Make("DisabledVisual", secondary.transform, secondary.transform.position);
        extra.AddComponent<SpriteRenderer>().enabled = false;
        extra.AddComponent<CircleCollider2D>().enabled = false;
        var before = new Snapshot(secondary);
        var entryBefore = new Snapshot(hero);
        UIController.instance.levelUpPanel.SetActive(true);
        Check(!manager.TryEnterFusion(), label + "upgrade panel blocks entry even with running time");
        UIController.instance.levelUpPanel.SetActive(false); Time.timeScale = 0;
        Check(!manager.TryEnterFusion(), label + "pause blocks entry"); Time.timeScale = 1;
        Set(timer, "timerCounter", 8f); timer.enabled = true;
        Check(manager.TryEnterFusion(), label + "enter");
        Check(!manager.TryEnterFusion() && !manager.SwitchWorld(other.WorldId), label + "reject reentry and normal switching");
        Check(manager.IsFused && manager.FusionPlayer == hero && manager.CurrentWorld == entry && worldsActive(entry, other), label + "both worlds active, entry remains owner");
        Check(entry.Manager == manager && other.Manager == manager && entry.InteractionPlayer == hero && other.InteractionPlayer == hero && World.CanInteract(hero, secondary) && World.CanInteract(secondary, hero), label + "interaction ownership and bidirectional cross-world policy");
        Check(PlayerController.instance == hero && PlayerHealth.instance == hero.GetComponent<PlayerHealth>() && ExperienceLevelController.instance == hero.GetComponent<ExperienceLevelController>(), label + "entry aliases");
        secondary.BindAsCurrent(); secondary.GetComponent<PlayerHealth>().BindAsCurrent(); secondary.GetComponent<ExperienceLevelController>().BindAsCurrent();
        Check(PlayerController.instance == hero && PlayerHealth.instance == hero.GetComponent<PlayerHealth>() && ExperienceLevelController.instance == hero.GetComponent<ExperienceLevelController>(), label + "secondary cannot steal aliases");
        Check(secondary.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponentInParent<Weapon>() == null).All(r => !r.enabled)
            && secondary.GetComponentsInChildren<Collider2D>(true).Where(c => c.GetComponentInParent<Weapon>() == null).All(c => !c.enabled), label + "secondary body invisible and noncolliding");
        var visual = hero.GetComponent<PlayerFusionVisual>();
                var fusionRenderer = (SpriteRenderer)Get(visual, "fusionRenderer");
                var bodies = (SpriteRenderer[])Get(visual, "bodyRenderers");
                Check(fusionRenderer.enabled && fusionRenderer.gameObject.activeInHierarchy && bodies.All(r => !r.enabled) && entryBefore.CollidersSame(), label + "synchronous dedicated fusion sprite active, bodies hidden, colliders unchanged");
        Check(secondary.enabled && secondary.GetComponent<Rigidbody2D>().constraints == RigidbodyConstraints2D.FreezeAll, label + "secondary weapon host active but body frozen");
        hero.transform.position += Vector3.right * 2; hero.facingDirection = Vector2.up;
        yield return new WaitForSeconds(0.2f);
        Check(secondary.transform.position == hero.transform.position && secondary.facingDirection == hero.facingDirection, label + "real frame following position/facing");
        Equal((float)Get(timer, "timerCounter"), 8, label + "automatic timer freezes in fusion");
        var holders = new[] { hero.GetComponent<BuffController>(), secondary.GetComponent<BuffController>() };
        var handles = new BuffHandle[2]; var recipes = new BuffDefinition[2];
        for (int i = 0; i < 2; i++)
        {
            recipes[i] = ScriptableObject.CreateInstance<BuffDefinition>(); owned.Add(recipes[i]);
            var atom = new DamageBuffAtom(); Set(atom, "damageMultiplier", i + 2f);
            Set(recipes[i], "duration", 60f); Set(recipes[i], "atoms", new List<BuffAtom> { atom });
            handles[i] = holders[i].GrantBuff(recipes[i], new object());
            Check(handles[i] != null && handles[i].IsActive, label + "distinct timed buff grant " + i);
        }
        var buffLists = holders.Select(h => ((IEnumerable<BuffInstance>)Get(h, "instances")).ToArray()).ToArray();
        float[] damageBefore = holders.Select(h => h.CalculateWeaponDamage(10)).ToArray();
        Check(damageBefore[0] != damageBefore[1], label + "distinct outgoing buff results (no merged pool)");
        var times = handles.Select(h => Instance(h).RemainingDuration).ToArray();
        yield return new WaitForSeconds(0.15f);
        Check(handles.Select((h, i) => Instance(h).RemainingDuration < times[i]).All(b => b), label + "both holders tick while fused");
        for (int i = 0; i < 2; i++)
        {
            World firing = i == 0 ? entry : other, targetWorld = i == 0 ? other : entry;
            var pistol = firing.Player.assignedWeapons.OfType<PistolController>().FirstOrDefault();
            Check(pistol != null, label + firing.WorldId + " equipped pistol exists");
            if (pistol == null) continue;
            Check(Get(pistol, "buffHolder") == holders[i] && World.GetFor(pistol) == firing, label + "equipped source binding " + firing.WorldId);
            var target = Enemy(targetWorld, hero.transform.position + Vector3.right * 3);
            Physics2D.SyncTransforms();
            Check(Call(pistol, "FindClosestEnemy") == target, label + firing.WorldId + " selects opposite-world target");
            var bulletsBefore = new HashSet<BulletController>(Object.FindObjectsByType<BulletController>(FindObjectsSortMode.None));
            Set(pistol, "attackCounter", 0f); pistol.enabled = true;
            yield return null;
            pistol.enabled = false;
            var bullets = Object.FindObjectsByType<BulletController>(FindObjectsSortMode.None).Where(b => !bulletsBefore.Contains(b)).ToArray();
            foreach (var bullet in bullets) owned.Add(bullet.gameObject);
            int expectedCount = holders[i].CalculateProjectileCount(Mathf.Max(0, Mathf.FloorToInt(pistol.stats.amount)));
            Check(bullets.Length == expectedCount && bullets.Length > 0, label + firing.WorldId + " real Update fires equipped volley count=" + bullets.Length);
            float expectedDamage = holders[i].CalculateWeaponDamage((float)Get(pistol, "attackDamage") * pistol.stats.damage);
            Check(bullets.All(b => b.transform.parent == firing.ContentRoot && Get(b, "buffSource") == holders[i] && Get(b, "target") == target && Mathf.Approximately(b.damage, expectedDamage)), label + "volley keeps source world/holder/damage/foreign target");
            // Move the actual prefab volley into contact; Unity dispatches the collision.
            foreach (var bullet in bullets) { bullet.speed = 0; bullet.transform.position = target.transform.position; }
            Physics2D.SyncTransforms(); yield return new WaitForSeconds(0.15f);
            Equal(target.health, 1000 - expectedDamage * bullets.Length, label + "real cross-world projectile hits " + firing.WorldId);
            Object.DestroyImmediate(target.gameObject);
        }
        var hp = hero.GetComponent<PlayerHealth>(); float health = hp.currentHealth;
        secondary.GetComponent<PlayerHealth>().DamageHandler(1);
        Equal(hp.currentHealth, health - 1, label + "secondary damage reaches shared HP");
        hp.currentHealth = health;
        var xp = hero.GetComponent<ExperienceLevelController>(); var otherXp = secondary.GetComponent<ExperienceLevelController>();
        xp.currentExperience = otherXp.currentExperience = 0;
        var pickupsBefore = new HashSet<ExpPickup>(Object.FindObjectsByType<ExpPickup>(FindObjectsSortMode.None));
        var victim = Enemy(other, hero.transform.position + Vector3.right * 20); victim.expDrop = 1;
        victim.TakeDamage(1001);
        var orb = Object.FindObjectsByType<ExpPickup>(FindObjectsSortMode.None).Single(p => !pickupsBefore.Contains(p)); owned.Add(orb.gameObject);
        Check(orb.transform.parent == other.ContentRoot && World.GetFor(orb) == other && xp.currentExperience == 0, label + "kill creates XP under enemy source before receipt");
        orb.transform.position = hero.transform.position; Physics2D.SyncTransforms();
        yield return new WaitForSeconds(0.2f);
        Check(orb == null && xp.currentExperience == 1 && otherXp.currentExperience == 0, label + "real cross-world pickup awards entry hero once");
        otherXp.GetExp(1);
        Check(xp.currentExperience == 2 && otherXp.currentExperience == 0, label + "secondary direct XP also routes to entry");
        UIController.instance.levelUpPanel.SetActive(true);
        Check(!manager.TryExitFusion() && !manager.SwitchWorld(other.WorldId) && manager.IsFused, label + "upgrade panel prevents escape");
        Time.timeScale = 0; UIController.instance.levelUpPanel.SetActive(false);
        Check(!manager.TryExitFusion() && manager.IsFused, label + "paused fusion cannot escape"); Time.timeScale = 1;
        // Sleeping holders intentionally expose no modifiers; snapshot outgoing damage while both are active.
        var outgoing = holders.Select(h => h.CalculateWeaponDamage(10)).ToArray();
        Check(manager.TryExitFusion(), label + "exit");
        Check(before.Same(), label + "exact secondary transform/facing/body/render/collider/weapon-list restoration");
        Check(entryBefore.ListsSame() && entryBefore.VisualsSame(), label + "entry weapon lists and exact visual/collider states restored");
        Check(!manager.IsFused && manager.FusionPlayer == null && entry.IsActive && !other.IsActive && !World.CanInteract(hero, secondary), label + "exit restores exclusive world and interaction policy");
        Equal((float)Get(timer, "timerCounter"), 8, label + "exit preserves timer remainder");
        float asleepTime = Instance(handles[1]).RemainingDuration;
        yield return new WaitForSeconds(0.2f);
        Check((float)Get(timer, "timerCounter") < 8 && (float)Get(timer, "timerCounter") > 7, label + "timer resumes rather than resets");
        Equal(Instance(handles[1]).RemainingDuration, asleepTime, label + "secondary buff sleeps again");
        timer.enabled = false;
        for (int cycle = 0; cycle < 5; cycle++)
        {
            Check(manager.TryEnterFusion(), label + "repeat enter " + cycle);
            Check(before.ListsSame() && entryBefore.ListsSame() && new[] { entry, other }.All(w => w.Player.assignedWeapons.All(weapon => weapon.gameObject.activeInHierarchy && World.GetFor(weapon) == w)),
                label + "both equipped sets remain active and source-owned " + cycle);
            for (int i = 0; i < 2; i++)
                Check(((IEnumerable<BuffInstance>)Get(holders[i], "instances")).SequenceEqual(buffLists[i]) && Mathf.Approximately(holders[i].CalculateWeaponDamage(10), outgoing[i]),
                    label + "no transient buff merge while fused " + cycle + "/" + i);
            Check(manager.TryExitFusion(), label + "repeat exit " + cycle);
            Check(before.Same() && entryBefore.ListsSame() && entryBefore.VisualsSame(), label + "repeat exact restoration " + cycle);
            for (int i = 0; i < 2; i++)
                Check(((IEnumerable<BuffInstance>)Get(holders[i], "instances")).SequenceEqual(buffLists[i]) && handles[i].IsActive,
                    label + "no duplicate/replaced buff instances " + cycle + "/" + i);
        }
        for (int i = 0; i < 2; i++) holders[i].RevokeBuff(handles[i]);
    }
    static IEnumerator ColdStart(WorldManager manager, World secondaryWorld)
    {
        var entry = manager.CurrentWorld.Player;
        var secondary = secondaryWorld.Player;
        var holder = secondary.GetComponent<BuffController>();
        Check(!(bool)Get(secondary, "starterWeaponsInitialized") && !secondaryWorld.IsActive,
            "cold: sleeping secondary has never run Player.Start");
        Check(!((IEnumerable<BuffInstance>)Get(holder, "instances")).Any(), "cold: secondary Buff.Start has not granted initial buffs");
        var weapons = secondary.assignedWeapons.Concat(secondary.unassignedWeapons).ToArray();
        int assigned = secondary.assignedWeapons.Count;
        int expectedAssigned = assigned > 0 ? assigned : Mathf.Min(3, secondary.unassignedWeapons.Count);
        var bodyRenderers = secondary.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponentInParent<Weapon>() == null).ToArray();
        var bodyColliders = secondary.GetComponentsInChildren<Collider2D>(true).Where(c => c.GetComponentInParent<Weapon>() == null).ToArray();
        var rendering = bodyRenderers.Select(r => r.enabled).ToArray();
        var collision = bodyColliders.Select(c => c.enabled).ToArray();
        var position = secondary.transform.position;
        entry.transform.position = new Vector3(10000, 10000, 0);
        foreach (var weapon in weapons) weapon.enabled = true;
        var contentBefore = new HashSet<Transform>(secondaryWorld.ContentRoot.GetComponentsInChildren<Transform>(true));
        Check(manager.TryEnterFusion(), "cold: enter before secondary Start");
        Check(!(bool)Get(secondary, "starterWeaponsInitialized") && PlayerController.instance == entry,
            "cold: activation binds entry without manually invoking secondary Start");
        yield return new WaitForSeconds(0.1f);
        foreach (var weapon in weapons) weapon.enabled = false;
        Check((bool)Get(secondary, "starterWeaponsInitialized") && secondary.assignedWeapons.Count == expectedAssigned,
            "cold: real Start equips starter weapons exactly once");
        Check(weapons.Length == secondary.assignedWeapons.Count + secondary.unassignedWeapons.Count
            && weapons.All(w => secondary.assignedWeapons.Contains(w) || secondary.unassignedWeapons.Contains(w)), "cold: no cloned/lost weapon registrations");
        Check(secondary.assignedWeapons.All(w => w.gameObject.activeInHierarchy && World.GetFor(w) == secondaryWorld
            && Get(w, "buffHolder") == holder), "cold: equipped weapon Start binds secondary source while fused");
        var buffs = ((IEnumerable<BuffInstance>)Get(holder, "instances")).ToArray();
        var definitions = ((BuffDefinition[])Get(holder, "initialBuffs")).Where(d => d != null).Distinct().ToArray();
        Check(buffs.Length == definitions.Length && definitions.All(d => holder.FindBuff(d) != null), "cold: real Buff.Start initializes own recipes once");
        Check(bodyRenderers.All(r => !r.enabled) && bodyColliders.All(c => !c.enabled)
            && secondary.transform.position == entry.transform.position && PlayerController.instance == entry
            && PlayerHealth.instance == entry.GetComponent<PlayerHealth>() && ExperienceLevelController.instance == entry.GetComponent<ExperienceLevelController>(),
            "cold: first Start preserves suppression, following and entry aliases");
        Check(manager.TryExitFusion(), "cold: exit after first Start");
        Check(secondary.transform.position == position && rendering.SequenceEqual(bodyRenderers.Select(r => r.enabled))
            && collision.SequenceEqual(bodyColliders.Select(c => c.enabled)), "cold: pre-Start body states restored after Start");
        Check(manager.TryEnterFusion(), "cold: enter again after initialization");
        yield return null;
        Check(secondary.assignedWeapons.Count == expectedAssigned && ((IEnumerable<BuffInstance>)Get(holder, "instances")).SequenceEqual(buffs),
            "cold: second activation does not repeat Start equipment/buffs");
        Check(manager.TryExitFusion(), "cold: finish asleep");
        // Remove only runtime effects created by the cold weapon Start/Update window.
        foreach (var child in secondaryWorld.ContentRoot.GetComponentsInChildren<Transform>(true))
            if (child != null && child.parent == secondaryWorld.ContentRoot && !contentBefore.Contains(child)) Object.DestroyImmediate(child.gameObject);
    }

    static IEnumerator LiveWorlds(WorldManager manager, World[] worlds)
    {
        var spawners = worlds.Select(w => w.ContentRoot.GetComponentsInChildren<EnemySpawner>(true).Single()).ToArray();
        var live = new List<EnemyController>();
        try
        {
            // Establish real local targets first, so fusion must retarget existing living enemies.
            for (int i = 0; i < worlds.Length; i++)
            {
                var world = worlds[i]; var spawner = spawners[i];
                if (manager.CurrentWorld != world) Check(manager.SwitchWorld(world.WorldId), "live: prepare " + world.WorldId);
                world.Player.transform.position = new Vector3(10000, 10000, 0);
                spawner.enabled = true;
                yield return new WaitForSeconds(0.05f); // real Start initializes the wave if this spawner was cold
                var before = new HashSet<GameObject>((List<GameObject>)Get(spawner, "spawnedEnemies"));
                spawner.spawnCounter = 0;
                yield return new WaitForSeconds(0.15f);
                spawner.enabled = false;
                var spawned = ((List<GameObject>)Get(spawner, "spawnedEnemies")).Where(g => g != null && !before.Contains(g)).ToArray();
                Check(spawned.Length > 0, "live: real scene spawner emits " + world.WorldId + " count=" + spawned.Length);
                foreach (var go in spawned)
                {
                    owned.Add(go);
                    var enemy = go.GetComponent<EnemyController>();
                    Check(enemy != null && enemy.isActiveAndEnabled && enemy.moveSpeed > 0 && enemy.RB.simulated,
                        "live: normal spawned enemy remains enabled and physical " + world.WorldId);
                    if (enemy == null) continue;
                    live.Add(enemy);
                    Check(go.transform.parent == world.ContentRoot && Get(enemy, "target") == world.Player.transform,
                        "live: normal enemy initially targets source hero " + world.WorldId);
                }
            }
            foreach (var entry in worlds)
            {
                if (manager.CurrentWorld != entry) Check(manager.SwitchWorld(entry.WorldId), "live: select entry " + entry.WorldId);
                var other = worlds.Single(w => w != entry);
                Check(manager.TryEnterFusion(), "live: fuse from " + entry.WorldId);
                var positions = live.Select(e => e.transform.position).ToArray();
                var before = spawners.Select(s => new HashSet<GameObject>((List<GameObject>)Get(s, "spawnedEnemies"))).ToArray();
                foreach (var spawner in spawners) { spawner.enabled = true; spawner.spawnCounter = 0; }
                yield return new WaitForSeconds(0.25f);
                for (int i = 0; i < spawners.Length; i++)
                {
                    var spawner = spawners[i];
                    Check(spawner.isActiveAndEnabled && Get(spawner, "target") == entry.Player.transform && spawner.transform.position == entry.Player.transform.position,
                        "live: both spawners follow entry " + entry.WorldId + "/" + worlds[i].WorldId);
                    var spawned = ((List<GameObject>)Get(spawner, "spawnedEnemies")).Where(g => g != null && !before[i].Contains(g)).ToArray();
                    foreach (var go in spawned) owned.Add(go);
                    Check(spawned.Length > 0 && spawned.All(g => g.transform.parent == worlds[i].ContentRoot
                        && g.GetComponent<EnemyController>().isActiveAndEnabled && Get(g.GetComponent<EnemyController>(), "target") == entry.Player.transform),
                        "live: both spawners emit source-parented enemies targeting fused entry " + worlds[i].WorldId + " count=" + spawned.Length);
                    spawner.enabled = false;
                }
                for (int i = 0; i < live.Count; i++)
                {
                    var enemy = live[i];
                    Check(enemy.isActiveAndEnabled && Get(enemy, "target") == entry.Player.transform
                        && Vector3.Distance(enemy.transform.position, entry.Player.transform.position) < Vector3.Distance(positions[i], entry.Player.transform.position) - 0.01f,
                        "live: existing enemy retargets and physically approaches fused entry " + entry.WorldId + "/" + World.GetFor(enemy).WorldId);
                }
                entry.Player.transform.position += Vector3.up * 2;
                yield return new WaitForSeconds(0.15f);
                Check(live.All(e => Vector2.Dot(e.RB.linearVelocity.normalized, ((Vector2)(entry.Player.transform.position - e.transform.position)).normalized) > 0.95f),
                    "live: normal Update steers both worlds after entry moves " + entry.WorldId);
                Check(manager.TryExitFusion(), "live: exit " + entry.WorldId);
                var sleeping = live.Where(e => World.GetFor(e) == other).ToArray();
                var asleepPositions = sleeping.Select(e => e.transform.position).ToArray();
                yield return new WaitForSeconds(0.15f);
                Check(sleeping.Select((e, i) => !e.isActiveAndEnabled && e.transform.position == asleepPositions[i]).All(b => b),
                    "live: secondary enemy physics sleeps after exit " + entry.WorldId);
                Check(manager.SwitchWorld(other.WorldId), "live: wake former secondary " + other.WorldId);
                yield return new WaitForSeconds(0.15f);
                Check(sleeping.All(e => e.isActiveAndEnabled && Get(e, "target") == other.Player.transform),
                    "live: former secondary retargets its own hero on normal wake " + other.WorldId);
            }
        }
        finally { foreach (var spawner in spawners) if (spawner != null) spawner.enabled = false; }
    }

    // Separate disposable Main session: invalid configuration must leave this manager unusable.
    public static IEnumerator RunCleanup()
    {
        Passed = Failed = 0;
        var manager = Object.FindFirstObjectByType<WorldManager>();
        if (!Application.isPlaying || manager == null || !manager.IsInitialized) throw new InvalidOperationException("Requires initialized Main Play Mode");
        var worlds = (World[])Get(manager, "worlds");
        foreach (var b in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (b is Weapon || b is EnemySpawner || b is StateSwitchController) b.enabled = false;
        UIController.instance.levelUpPanel.SetActive(false); Time.timeScale = 1;
        foreach (var world in worlds)
        {
            if (manager.CurrentWorld != world) Check(manager.SwitchWorld(world.WorldId), "cleanup: initialize " + world.WorldId);
            yield return null;
        }
        try
        {
            foreach (var entry in worlds)
            {
                if (manager.CurrentWorld != entry) Check(manager.SwitchWorld(entry.WorldId), "disable: select " + entry.WorldId);
                var secondary = worlds.Single(w => w != entry);
                var before = new Snapshot(secondary.Player);
                Check(manager.TryEnterFusion(), "disable: enter " + entry.WorldId);
                yield return null;
                Time.timeScale = 0; UIController.instance.levelUpPanel.SetActive(true);
                manager.enabled = false; // Unity dispatches OnDisable; no private callback invocation.
                Check(!manager.IsFused && manager.FusionPlayer == null && !manager.IsSwitching && manager.IsInitialized,
                    "disable: actual OnDisable clears fusion even during upgrade pause " + entry.WorldId);
                Check(entry.IsActive && !secondary.IsActive && secondary.Player.gameObject.activeSelf && before.Same(),
                    "disable: secondary sleeps before exact body/list restoration " + entry.WorldId);
                Check(PlayerController.instance == entry.Player && !World.CanInteract(entry.Player, secondary.Player),
                    "disable: entry alias retained and cross-world interaction closed " + entry.WorldId);
                Time.timeScale = 1; UIController.instance.levelUpPanel.SetActive(false);
                Check(!manager.TryEnterFusion() && !manager.SwitchWorld(secondary.WorldId), "disable: disabled manager rejects operations");
                manager.enabled = true;
                Check(manager.TryEnterFusion() && manager.TryExitFusion(), "disable: valid manager can fuse again after reenable " + entry.WorldId);
            }
            var current = manager.CurrentWorld;
            var other = worlds.Single(w => w != current);
            var capturedRoot = other.ContentRoot;
            var originalParent = capturedRoot.parent;
            var originalLocalPosition = capturedRoot.localPosition;
            var originalLocalRotation = capturedRoot.localRotation;
            var originalLocalScale = capturedRoot.localScale;
            var snapshot = new Snapshot(other.Player);
            Check(manager.TryEnterFusion(), "fault: enter before invalid Content reparent");
            var holder = other.Player.GetComponent<BuffController>();
            var recipe = ScriptableObject.CreateInstance<BuffDefinition>(); owned.Add(recipe);
            Set(recipe, "duration", 60f); Set(recipe, "atoms", new List<BuffAtom> { new DamageBuffAtom() });
            var handle = holder.GrantBuff(recipe, new object());
            Check(handle != null && handle.IsActive, "fault: timed secondary buff exists before fallback");
            var parking = Make("InvalidContentParent", null, Vector3.zero);
            try
            {
                capturedRoot.SetParent(parking.transform, true);
                Check(!other.IsConfigured && capturedRoot.gameObject.activeInHierarchy && other.Player.isActiveAndEnabled,
                    "fault: reparent makes configuration invalid without pre-sleeping content");
                expectedErrors.Clear();
                expectedErrors.Enqueue("Invalid world configuration: use a defined WorldId and assign a direct child object as contentRoot.");
                expectedErrors.Enqueue("Fusion cleanup required the captured content root fallback; manager disabled.");
                bool exited;
                Debug.Log("FusionChecks EXPECTED ERRORS BEGIN: invalid Content plus captured-root fallback");
                expectingErrors = true;
                try { exited = manager.TryExitFusion(); }
                finally { expectingErrors = false; }
                Check(expectedErrors.Count == 0, "fault: exactly both expected production errors observed by batch log handler");
                Check(!exited && !manager.enabled && !manager.IsInitialized && !manager.IsSwitching && !manager.IsFused && manager.FusionPlayer == null,
                    "fault: failed exit returns false, invalidates/disables manager, releases guards and fusion aliases");
                Check(!capturedRoot.gameObject.activeSelf && !other.Player.gameObject.activeInHierarchy && other.Player.gameObject.activeSelf && snapshot.Same(),
                    "fault: captured root safely asleep and secondary exact state restored despite reparent");
                Check(holder.IsWorldSuspended && handle.IsActive && current.IsActive && PlayerController.instance == current.Player
                    && !World.CanInteract(current.Player, other.Player), "fault: fallback preserves suspended buff and exclusive entry control");
                float remaining = Instance(handle).RemainingDuration;
                yield return new WaitForSeconds(0.15f);
                Equal(Instance(handle).RemainingDuration, remaining, "fault: fallback-suspended buff does not tick");
                Check(!manager.TryEnterFusion() && !manager.TryExitFusion() && !manager.SwitchWorld(other.WorldId), "fault: invalid disabled manager rejects all transitions");
                manager.enabled = true;
                Check(!manager.IsInitialized && !manager.TryEnterFusion() && !manager.SwitchWorld(other.WorldId), "fault: reenable cannot bypass invalidation latch");
                manager.enabled = false;
                holder.RevokeBuff(handle);
            }
            finally
            {
                expectingErrors = false; expectedErrors.Clear();
                // Restore only the test's hierarchy mutation, without reviving the invalid manager.
                capturedRoot.SetParent(originalParent, false);
                capturedRoot.localPosition = originalLocalPosition; capturedRoot.localRotation = originalLocalRotation; capturedRoot.localScale = originalLocalScale;
            }
        }
        finally { Time.timeScale = 1; UIController.instance.levelUpPanel.SetActive(false); Cleanup(); }
        Debug.Log("FusionCleanupChecks: " + Passed + " passed, " + Failed + " failed");
    }

    static bool worldsActive(World a, World b) { return a.IsActive && b.IsActive && !a.IsSuspended && !b.IsSuspended; }
    sealed class Snapshot
    {
        readonly PlayerController p; readonly Vector3 position, scale; readonly Quaternion rotation; readonly Vector2 facing, velocity;
        readonly float angular; readonly RigidbodyConstraints2D constraints;
        readonly Renderer[] renderers; readonly bool[] rendering; readonly Collider2D[] colliders; readonly bool[] collision;
        readonly Weapon[] assigned, unassigned, all; readonly Transform parent;
        readonly List<Weapon> assignedList, unassignedList;
        public Snapshot(PlayerController player)
        {
            p = player; position = p.transform.position; rotation = p.transform.rotation; scale = p.transform.localScale; parent = p.transform.parent; facing = p.facingDirection;
            var body = p.GetComponent<Rigidbody2D>(); velocity = body.linearVelocity; angular = body.angularVelocity; constraints = body.constraints;
            renderers = p.GetComponentsInChildren<Renderer>(true); rendering = renderers.Select(r => r.enabled).ToArray();
            colliders = p.GetComponentsInChildren<Collider2D>(true); collision = colliders.Select(c => c.enabled).ToArray();
            assignedList = p.assignedWeapons; unassignedList = p.unassignedWeapons;
            assigned = assignedList.ToArray(); unassigned = unassignedList.ToArray(); all = p.GetComponentsInChildren<Weapon>(true);
        }
        public bool ListsSame() { return ReferenceEquals(assignedList, p.assignedWeapons) && ReferenceEquals(unassignedList, p.unassignedWeapons) && assigned.SequenceEqual(p.assignedWeapons) && unassigned.SequenceEqual(p.unassignedWeapons) && all.SequenceEqual(p.GetComponentsInChildren<Weapon>(true)); }
        public bool CollidersSame() { return colliders.SequenceEqual(p.GetComponentsInChildren<Collider2D>(true)) && collision.SequenceEqual(colliders.Select(c => c.enabled)); }
        public bool VisualsSame() { return renderers.SequenceEqual(p.GetComponentsInChildren<Renderer>(true)) && rendering.SequenceEqual(renderers.Select(r => r.enabled)) && colliders.SequenceEqual(p.GetComponentsInChildren<Collider2D>(true)) && collision.SequenceEqual(colliders.Select(c => c.enabled)); }
        public bool Same()
        {
            var body = p.GetComponent<Rigidbody2D>();
            return position.Equals(p.transform.position) && rotation.Equals(p.transform.rotation) && scale.Equals(p.transform.localScale) && parent == p.transform.parent && facing.Equals(p.facingDirection)
                && velocity.Equals(body.linearVelocity) && angular.Equals(body.angularVelocity) && constraints == body.constraints && VisualsSame() && ListsSame();
        }
    }
    static EnemyController Enemy(World world, Vector3 point)
    {
        var go = Make("Enemy", world.ContentRoot, point); go.tag = "Enemy";
        var rb = go.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Kinematic;
        go.AddComponent<BoxCollider2D>(); var enemy = go.AddComponent<EnemyController>();
        enemy.RB = rb; enemy.health = 1000; enemy.moveSpeed = 0; enemy.enabled = false; return enemy;
    }
    static GameObject Make(string name, Transform parent, Vector3 point)
    {
        var go = new GameObject("FusionChecks_" + name); owned.Add(go); go.transform.SetParent(parent, false); go.transform.position = point; return go;
    }
    static void Cleanup() { for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); owned.Clear(); }
    static BuffInstance Instance(BuffHandle h) { return (BuffInstance)typeof(BuffHandle).GetProperty("Instance", Flags).GetValue(h, null); }
    static object Get(object o, string n) { return o.GetType().GetField(n, Flags).GetValue(o); }
    static void Set(object o, string n, object v) { o.GetType().GetField(n, Flags).SetValue(o, v); }
    static object Call(object o, string n, params object[] args) { return o.GetType().GetMethod(n, Flags).Invoke(o, args); }
    static void Equal(float a, float b, string label) { Check(Mathf.Abs(a - b) < 0.001f, label + " expected=" + b + " actual=" + a); }
    static void Check(bool ok, string label)
    {
        if (ok) { Passed++; Debug.Log("FusionChecks PASS: " + label); }
        else { Failed++; Debug.LogError("FusionChecks FAIL: " + label); }
    }
}
