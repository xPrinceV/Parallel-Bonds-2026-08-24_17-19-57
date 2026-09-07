using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Compile outside Assets against current Assembly-CSharp and Unity runtime assemblies (including UI).
// Call Run on Unity's main thread after Main's UI/Start initialization, in a throwaway Play session.
// No frames are yielded: trigger callbacks are manual, targeting uses a synchronous physics query.
// This does NOT test physics callback delivery, real elapsed timers, or the automatic 15-second switch.
public static class DualWorldChecks
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static bool running;

    public static string Run()
    {
        if (!Application.isPlaying || running)
            throw new InvalidOperationException("Requires Play Mode on the main thread, without reentry.");
        var manager = Object.FindAnyObjectByType<WorldManager>();
        var ui = UIController.instance;
        if (manager == null || !manager.IsInitialized || ui == null || ui.levelUpPanel == null ||
            ui.expLvlSlider == null || ui.expLvlText == null)
            throw new InvalidOperationException("Load the new Main setup and wait for UI/Start initialization.");
        var timers = Object.FindObjectsByType<StateSwitchController>(FindObjectsInactive.Include);
        var enabled = timers.Select(t => t.enabled).ToArray();
        float scale = Time.timeScale;
        bool panel = ui.levelUpPanel.activeSelf;
        var originalWorld = manager.CurrentWorldId;
        var owned = new List<Object>();
        var revoke = new List<Action>();
        var pickupsBefore = new HashSet<ExpPickup>(Resources.FindObjectsOfTypeAll<ExpPickup>());
        var numbersBefore = new HashSet<DamageNumber>(Resources.FindObjectsOfTypeAll<DamageNumber>());
        var numbers = DamageNumberController.instance;
        object savedPool = numbers == null ? null : Get(numbers, "numberPool");
        int count = 0;
        Action<bool, string> check = (ok, label) =>
        {
            if (!ok) throw new InvalidOperationException("DualWorldChecks: " + label);
            count++;
        };
        running = true;
        try
        {
            foreach (var timer in timers) timer.enabled = false;
            Time.timeScale = 1f;
            ui.levelUpPanel.SetActive(false);
            var worlds = (World[])Get(manager, "worlds");
            var a = worlds.Single(w => w.WorldId == WorldId.Material);
            var b = worlds.Single(w => w.WorldId == WorldId.Echo);
            if (manager.CurrentWorldId != a.WorldId) check(manager.SwitchWorld(a.WorldId), "enter Material");
            var ah = new Hero(a);
            check(manager.SwitchWorld(b.WorldId), "initialize Echo through real switch, not manual Awake/Start");
            var bh = new Hero(b);
            check(manager.SwitchWorld(a.WorldId), "return to Material");
            check(a != b && ah.Player != bh.Player && ah.Health != bh.Health && ah.Xp != bh.Xp &&
                ah.Weapon != bh.Weapon && !ReferenceEquals(ah.Stats, bh.Stats) && ah.Buffs != bh.Buffs &&
                !ReferenceEquals(ah.Xp.expLevels, bh.Xp.expLevels), "distinct hero state and weapon stats");
            Action<World, World> state = (awake, asleep) =>
            {
                check(manager.CurrentWorld == awake && manager.CurrentWorldId == awake.WorldId && !manager.IsSwitching &&
                    awake.IsActive && !awake.IsSuspended && !asleep.IsActive && asleep.IsSuspended &&
                    awake.ContentRoot.gameObject.activeSelf && !asleep.ContentRoot.gameObject.activeSelf,
                    "exclusive worlds and manager state");
                check(PlayerController.instance == awake.Player && PlayerHealth.instance == awake.Player.GetComponent<PlayerHealth>() &&
                    ExperienceLevelController.instance == awake.Player.GetComponent<ExperienceLevelController>() &&
                    !awake.Player.GetComponent<BuffController>().IsWorldSuspended && asleep.Player.GetComponent<BuffController>().IsWorldSuspended,
                    "active aliases and holder suspension");
                var pistol = awake.Player.GetComponentInChildren<PistolController>(true);
                check(pistol.isActiveAndEnabled && ReferenceEquals(Get(pistol, "buffHolder"), awake.Player.GetComponent<BuffController>()),
                    "active hero weapon uses its own buff holder");
                Visuals(a, check); Visuals(b, check);
                var filter = Object.FindAnyObjectByType<WorldFilter>();
                check(filter != null && ReferenceEquals(Get(filter, "worldManager"), manager), "actual shared world filter");
                Invoke(filter, "LateUpdate");
                var image = (Image)Get(filter, "overlay");
                check(image != null && image.isActiveAndEnabled && !image.raycastTarget && image.color == awake.AmbientColor &&
                    Mathf.Approximately(image.color.a, awake.AmbientColor.a), "actual overlay matches configured RGBA");
            };
            state(a, b);
            check(ah.Health.HasInitialized && bh.Health.HasInitialized && ah.Health.maxHealth > 7 && bh.Health.maxHealth > 9,
                "both health controllers initialized");
            ah.Health.currentHealth = ah.Health.maxHealth - 7;
            float aHealth = ah.Health.currentHealth, bHealth = bh.Health.currentHealth;
            // Reserve XP headroom without invoking level-up UI; runtime hero mutation is intentional.
            ah.Xp.currentExperience = bh.Xp.currentExperience = 0;
            check(ah.Xp.expLevels[ah.Xp.currentLevels] > 2 && bh.Xp.expLevels[bh.Xp.currentLevels] > 1, "XP headroom");
            int aLevel = ah.Xp.currentLevels, bLevel = bh.Xp.currentLevels;
            ah.Xp.GetExp(1);
            float bDamage = bh.Stats.damage;
            ah.Stats.damage += 0.375f;
            float aDamage = ah.Stats.damage;
            check(ah.Xp.currentExperience == 1 && bh.Xp.currentExperience == 0 && bh.Stats.damage == bDamage, "Material XP/upgrade isolation");
            var baseline = ((BuffDefinition[])Get(ah.Buffs, "initialBuffs")).Single(d => d != null && d.name == "FiveHitPower");
            check(ah.Buffs.FindBuff(baseline) != null, "existing FiveHitPower baseline initialized; never removed/reset");
            // Clone the existing recipe: fresh progress without revoking any user-owned source.
            var recipe = Object.Instantiate(baseline); owned.Add(recipe);
            recipe.name = "DualWorldChecks_FiveHitPower";
            var timed = ScriptableObject.CreateInstance<BuffDefinition>(); owned.Add(timed);
            var damage = new DamageBuffAtom(); Set(damage, "damageMultiplier", 1.5f);
            Set(timed, "duration", 20f); Set(timed, "isPermanent", false);
            Set(timed, "atoms", new List<BuffAtom> { damage });
            check(timed.isValid && recipe.isValid, "temporary recipes valid");
            object source = new object();
            Func<BuffController, BuffDefinition, BuffHandle> grant = (holder, definition) =>
            {
                var handle = holder.GrantBuff(definition, source);
                check(handle != null && handle.IsActive, "source-owned grant");
                revoke.Add(() => { if (holder != null) holder.RevokeBuff(handle); });
                return handle;
            };
            var stackA = grant(ah.Buffs, recipe);
            var timeA = grant(ah.Buffs, timed);
            var instanceA = Instance(timeA);
            var target = new GameObject("DualWorldChecks_HitTarget"); owned.Add(target);
            for (int i = 0; i < 3; i++) ah.Buffs.ReportHit(target, 1f);
            check(Instance(stackA).StackInstances.Single().CurrentStacks == 3 &&
                !Instance(stackA).StackInstances.Single().HasTriggered, "three hits retain fresh five-hit progress");
            check(instanceA.RemainingDuration == 20f && instanceA.Modifiers.Any(m => m.Stat == WeaponStatId.Damage &&
                m.Type == ModifierType.Multiplier && m.Value == 1.5f), "timed damage multiplier and initial lifetime");
            var positionA = new Vector3(10000, 10000, 0);
            ah.Player.transform.position = positionA;
            check(manager.SwitchWorld(b.WorldId), "A to B"); state(b, a);
            check(bh.Player.transform.position == positionA, "A to B position sync");
            ah.Buffs.Tick(3f); ah.Buffs.ReportHit(target, 1f);
            check(!ah.Buffs.isActiveAndEnabled && timeA.IsActive && ReferenceEquals(Instance(timeA), instanceA) &&
                instanceA.RemainingDuration == 20f && Instance(stackA).StackInstances.Single().CurrentStacks == 3,
                "inactive hierarchy ignores Tick and hits, preserves handles/progress");
            check(bh.Health.currentHealth == bHealth && bh.Xp.currentExperience == 0 && bh.Stats.damage == bDamage,
                "Echo was not overwritten on activation");
            bh.Health.currentHealth = bh.Health.maxHealth - 9;
            bHealth = bh.Health.currentHealth; bh.Xp.GetExp(1); bh.Stats.damage += 0.625f; bDamage = bh.Stats.damage;
            var stackB = grant(bh.Buffs, recipe);
            var timeB = grant(bh.Buffs, timed);
            bh.Buffs.ReportHit(target, 1f); bh.Buffs.Tick(2f);
            check(!ReferenceEquals(Instance(stackA), Instance(stackB)) && !ReferenceEquals(instanceA, Instance(timeB)) &&
                Instance(stackB).StackInstances.Single().CurrentStacks == 1 && Instance(stackA).StackInstances.Single().CurrentStacks == 3 &&
                Instance(timeB).RemainingDuration == 18f && instanceA.RemainingDuration == 20f && !bh.Buffs.RevokeBuff(timeA),
                "same recipe/source independent across holders, foreign revoke rejected");
            var positionB = positionA + new Vector3(4, 6, 0); bh.Player.transform.position = positionB;
            check(manager.SwitchWorld(a.WorldId), "B to A"); state(a, b);
            check(ah.Player.transform.position == positionB && ah.Health.currentHealth == aHealth && ah.Xp.currentExperience == 1 &&
                ah.Stats.damage == aDamage && ah.Xp.currentLevels == aLevel && ah.SameReferences(a), "Material persists without reinitialization");
            check(ReferenceEquals(ah.Buffs.GrantBuff(timed, source), timeA) && timeA.IsActive && stackA.IsActive,
                "wake retains same source handle");
            var spawner = b.ContentRoot.GetComponentInChildren<EnemySpawner>(true);
            check(spawner != null && spawner.waves.Count > 0 && spawner.waves[0].enemiesToSpawn.Count > 0, "real spawner template available");
            var enemyObject = Object.Instantiate(spawner.waves[0].enemiesToSpawn[0], ah.Weapon.transform.position,
                Quaternion.identity, World.GetContentRoot(spawner)); owned.Add(enemyObject);
            var enemy = enemyObject.GetComponent<EnemyController>();
            check(enemy != null && enemy.transform.parent == b.ContentRoot && World.GetFor(enemy) == b && !enemyObject.activeInHierarchy,
                "template enemy inherits sleeping source world");
            float hp = enemy.health; enemy.TakeDamage(1f);
            check(enemy.health == hp, "sleeping enemy rejects damage");
            Physics2D.SyncTransforms();
            check(Invoke(ah.Weapon, "FindClosestEnemy") == null, "active pistol cannot target colocated sleeping enemy");
            ah.Xp.SpawnExp(positionB, 1);
            var local = NewPickup(pickupsBefore, a);
            check(local.transform.parent == a.ContentRoot, "SpawnExp parents to owning world");
            var foreignCollider = bh.Player.GetComponent<Collider2D>();
            var localCollider = ah.Player.GetComponent<Collider2D>();
            check(foreignCollider != null && localCollider != null && foreignCollider.CompareTag("Player") && localCollider.CompareTag("Player"), "real player colliders");
            Invoke(local, "OnTriggerEnter2D", foreignCollider);
            check(!local.IsCollected && ah.Xp.currentExperience == 1 && bh.Xp.currentExperience == 1, "foreign pickup callback ignored");
            Invoke(local, "OnTriggerEnter2D", localCollider); Invoke(local, "OnTriggerEnter2D", localCollider);
            check(local.IsCollected && ah.Xp.currentExperience == 2 && bh.Xp.currentExperience == 1, "local pickup credited once before deferred Destroy");
            bh.Xp.SpawnExp(positionB, 1);
            var sleepingPickup = NewPickup(pickupsBefore, b);
            Invoke(sleepingPickup, "OnTriggerEnter2D", foreignCollider);
            check(sleepingPickup.transform.parent == b.ContentRoot && !sleepingPickup.IsCollected && bh.Xp.currentExperience == 1,
                "sleeping owner SpawnExp parenting and inactive pickup rejection");
            check(!manager.SwitchWorld((WorldId)int.MaxValue) && !manager.SwitchWorld(a.WorldId), "invalid/same switch rejected");
            ui.levelUpPanel.SetActive(true);
            check(!manager.SwitchWorld(b.WorldId), "upgrade panel blocks switch at timeScale one");
            ui.levelUpPanel.SetActive(false); Time.timeScale = 0f;
            check(!manager.SwitchWorld(b.WorldId), "paused time blocks switch independently");
            Time.timeScale = 1f; state(a, b);
            check(manager.SwitchWorld(b.WorldId), "wake Echo for persistence and targeting positive control"); state(b, a);
            check(bh.SameReferences(b) && bh.Health.currentHealth == bHealth && bh.Xp.currentExperience == 1 &&
                bh.Xp.currentLevels == bLevel && bh.Stats.damage == bDamage && timeB.IsActive &&
                Instance(timeB).RemainingDuration == 18f, "Echo HP/XP/upgrades/buffs persist without reinit");
            enemy.health = 10f; enemy.transform.position = bh.Weapon.transform.position;
            Physics2D.SyncTransforms();
            check(ReferenceEquals(Invoke(bh.Weapon, "FindClosestEnemy"), enemy), "awake same-world targeting positive control");
            bool deathChecked = numbers != null && numbers.numberToSpawn != null && numbers.numberCanvas != null;
            if (deathChecked)
            {
                // Isolate the real singleton's pool so no existing pooled number is borrowed or destroyed.
                Set(numbers, "numberPool", new List<DamageNumber>());
                var beforeDeath = new HashSet<ExpPickup>(Resources.FindObjectsOfTypeAll<ExpPickup>());
                enemy.TakeDamage(11f);
                var drop = NewPickup(beforeDeath, b);
                check(drop.transform.parent == b.ContentRoot && drop.expValue == enemy.expDrop, "death drops XP in enemy's own world");
            }
            var ordinary = new GameObject("DualWorldChecks_DisableHolder"); owned.Add(ordinary);
            var ordinaryHolder = ordinary.AddComponent<BuffController>();
            var ordinaryGrant = grant(ordinaryHolder, timed);
            ordinaryHolder.SetWorldSuspended(true); ordinary.SetActive(false); ordinaryHolder.Tick(3f);
            check(ordinaryGrant.IsActive && Instance(ordinaryGrant).RemainingDuration == 20f, "explicit world sleep preserves temporary holder");
            ordinary.SetActive(true); ordinaryHolder.SetWorldSuspended(false); ordinaryHolder.enabled = false;
            check(!ordinaryGrant.IsActive, "ordinary component disable clears buffs");
            return count + " synchronous checks passed. Manual trigger callbacks; no real timer/automatic-switch or physics-delivery assertions. Death drop: " +
                (deathChecked ? "checked." : "skipped (real damage-number singleton/template unavailable).");
        }
        finally
        {
            try
            {
                foreach (var action in revoke) action();
                foreach (var pickup in Resources.FindObjectsOfTypeAll<ExpPickup>())
                    if (pickup != null && pickup.gameObject.scene.IsValid() && !pickupsBefore.Contains(pickup)) Object.DestroyImmediate(pickup.gameObject);
                foreach (var number in Resources.FindObjectsOfTypeAll<DamageNumber>())
                    if (number != null && number.gameObject.scene.IsValid() && !numbersBefore.Contains(number)) Object.DestroyImmediate(number.gameObject);
                for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            }
            finally
            {
                if (numbers != null) Set(numbers, "numberPool", savedPool);
                try
                {
                    Time.timeScale = 1f; ui.levelUpPanel.SetActive(false);
                    if (manager.CurrentWorldId != originalWorld && !manager.SwitchWorld(originalWorld))
                        Debug.LogWarning("DualWorldChecks could not restore the original world; exit this throwaway Play session.");
                    var filter = Object.FindAnyObjectByType<WorldFilter>();
                    if (filter != null) Invoke(filter, "LateUpdate");
                }
                finally
                {
                    Time.timeScale = scale; ui.levelUpPanel.SetActive(panel);
                    for (int i = 0; i < timers.Length; i++) if (timers[i] != null) timers[i].enabled = enabled[i];
                    running = false;
                }
            }
        }
    }

    private sealed class Hero
    {
        public readonly PlayerController Player;
        public readonly PlayerHealth Health;
        public readonly ExperienceLevelController Xp;
        public readonly PistolController Weapon;
        public readonly WeaponStats Stats;
        public readonly BuffController Buffs;
        private readonly List<int> levels;
        public Hero(World world)
        {
            Player = world.Player; Health = Player.GetComponent<PlayerHealth>(); Xp = Player.GetComponent<ExperienceLevelController>();
            Buffs = Player.GetComponent<BuffController>(); Weapon = Player.GetComponentInChildren<PistolController>(true);
            if (Health == null || Xp == null || Buffs == null || Weapon == null || Weapon.stats == null)
                throw new InvalidOperationException("Incomplete new Main hero: " + world.name);
            Stats = Weapon.stats; levels = Xp.expLevels;
            // Echo's first Start cannot run during a synchronous switch; activate its existing pistol only.
            Weapon.gameObject.SetActive(true);
        }
        public bool SameReferences(World world) => world.Player == Player && Player.GetComponent<PlayerHealth>() == Health &&
            Player.GetComponent<ExperienceLevelController>() == Xp && Player.GetComponent<BuffController>() == Buffs &&
            Player.GetComponentInChildren<PistolController>(true) == Weapon && ReferenceEquals(Weapon.stats, Stats) && ReferenceEquals(Xp.expLevels, levels);
    }
    private static void Visuals(World world, Action<bool, string> check)
    {
        var zero = world.Player.transform.Find("Characters/Jeff_0"); var one = world.Player.transform.Find("Characters/Jeff_1");
        bool material = world.WorldId == WorldId.Material;
        check(zero != null && one != null && zero.gameObject.activeSelf == material && one.gameObject.activeSelf != material &&
            zero.gameObject.activeInHierarchy == (world.IsActive && material) && one.gameObject.activeInHierarchy == (world.IsActive && !material), "Jeff_0/Jeff_1 configured and effective visuals");
    }
    private static ExpPickup NewPickup(HashSet<ExpPickup> before, World world) => Resources.FindObjectsOfTypeAll<ExpPickup>()
        .Single(p => p.gameObject.scene.IsValid() && !before.Contains(p) && World.GetFor(p) == world);
    private static object Get(object obj, string name) => obj.GetType().GetField(name, Flags).GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Flags).SetValue(obj, value);
    private static object Invoke(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags).Invoke(obj, args);
    private static BuffInstance Instance(BuffHandle handle) => (BuffInstance)typeof(BuffHandle).GetProperty("Instance", Flags).GetValue(handle, null);
}
