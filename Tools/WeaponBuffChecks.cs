using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Compile outside Assets with CodeDom, referencing the current Assembly-CSharp and Unity assemblies.
// Invoke Run on Unity's main thread in Play Mode; this runner never yields a frame.
public static class WeaponBuffChecks
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Vector3 Origin = new Vector3(10000f, 10000f, 0f);
    private static string prefix;
    private static int checks;
    private static bool running;

    public static string Run()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("WeaponBuffChecks requires Unity Play Mode on the main thread.");
        if (running)
            throw new InvalidOperationException("WeaponBuffChecks is already running.");
        DamageNumberController numbers = DamageNumberController.instance;
        if (numbers == null || ExperienceLevelController.instance == null)
            throw new InvalidOperationException("Load Main in Play Mode with its existing DamageNumberController and ExperienceLevelController singletons.");
        if (numbers.numberToSpawn == null || numbers.numberCanvas == null ||
            Get(numbers.numberToSpawn, "damageText") == null)
            throw new InvalidOperationException("Main's damage-number prefab, text and canvas must be configured.");
        if (Physics2D.OverlapCircleAll(Origin, 20f).Length != 0)
            throw new InvalidOperationException("The remote test area is occupied; no objects were created.");

        // Preserve pooled scene objects; only newly created damage-number clones are deleted.
        var originalNumbers = new HashSet<DamageNumber>(Resources.FindObjectsOfTypeAll<DamageNumber>());
        var pool = (List<DamageNumber>)Get(numbers, "numberPool");
        var originalPool = new List<DamageNumber>(pool);
        var restoreNumbers = new List<Action>();
        foreach (DamageNumber number in originalPool)
        {
            if (number == null || Get(number, "damageText") == null)
                throw new InvalidOperationException("Main's damage-number pool contains an invalid entry.");
            DamageNumber saved = number;
            Vector3 position = saved.transform.localPosition;
            bool active = saved.gameObject.activeSelf;
            object life = Get(saved, "lifeCounter");
            object text = Get(saved, "damageText");
            PropertyInfo textProperty = text.GetType().GetProperty("text");
            object value = textProperty.GetValue(text, null);
            restoreNumbers.Add(delegate
            {
                if (saved == null) return;
                saved.transform.localPosition = position;
                Set(saved, "lifeCounter", life);
                textProperty.SetValue(text, value, null);
                saved.gameObject.SetActive(active);
            });
        }

        running = true;
        prefix = "WeaponBuffChecks_" + Guid.NewGuid().ToString("N") + "_";
        checks = 0;
        UnityEngine.Random.State randomState = UnityEngine.Random.state;
        BuffDefinition definition = null;
        try
        {
            definition = Recipe();
            EnemyController enemy = Enemy("Live", 100000f, true);
            Collider2D first = enemy.GetComponent<Collider2D>();
            GameObject child = Temp("SecondHitbox", false);
            child.transform.SetParent(enemy.transform, true);
            child.tag = "Enemy";
            Collider2D second = child.AddComponent<BoxCollider2D>();
            child.SetActive(true);
            EnemyController dead = Enemy("Dead", 0f, true);
            EnemyController inactive = Enemy("Inactive", 100000f, false);

            GameObject fireTemplate = Temp("Fire", false);
            LanternFire fireComponent = fireTemplate.AddComponent<LanternFire>();
            fireComponent.enabled = false;
            GameObject explosionTemplate = Temp("Explosion", false);
            GameObject projectileTemplate = Temp("Projectile", false);
            Rigidbody2D rb = projectileTemplate.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            LanternProjController template = projectileTemplate.AddComponent<LanternProjController>();
            template.enabled = false;
            template.RB = rb;
            template.fire = fireTemplate;
            template.explosion = explosionTemplate;
            template.minForce = template.maxForce = template.vertForce = 0f;

            LanternChecks(definition, enemy, first, second, dead, inactive, projectileTemplate, fireTemplate, explosionTemplate);
            LightningChecks(definition, enemy, dead, inactive);
            return checks + " deterministic checks passed. Trigger callbacks were invoked manually; lightning used synchronous overlap queries. Nonlethal only: lethal damage capping, experience drops and real physics delivery were not tested.";
        }
        finally
        {
            try
            {
                // Synchronous execution means these new scene instances came from our TakeDamage calls.
                foreach (DamageNumber number in Resources.FindObjectsOfTypeAll<DamageNumber>())
                    if (number != null && number.gameObject.scene.IsValid() && !originalNumbers.Contains(number))
                        Object.DestroyImmediate(number.gameObject);
                foreach (Action restore in restoreNumbers)
                    restore();
                pool.Clear();
                pool.AddRange(originalPool);
            }
            finally
            {
                try
                {
                    foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
                        if (obj != null && obj.scene.IsValid() && obj.name.StartsWith(prefix, StringComparison.Ordinal))
                            Object.DestroyImmediate(obj);
                    if (definition != null) Object.DestroyImmediate(definition);
                    Physics2D.SyncTransforms();
                }
                finally
                {
                    UnityEngine.Random.state = randomState;
                    running = false;
                }
            }
        }
    }

    private static void LanternChecks(BuffDefinition recipe, EnemyController enemy, Collider2D first,
        Collider2D second, EnemyController dead, EnemyController inactive, GameObject projectileTemplate,
        GameObject fireTemplate, GameObject explosionTemplate)
    {
        BuffController holder = Temp("LanternHolder", true).AddComponent<BuffController>();
        GameObject weaponObject = Temp("LanternWeapon", false);
        weaponObject.transform.SetParent(holder.transform, true);
        LanternController weapon = weaponObject.AddComponent<LanternController>();
        weapon.enabled = false;
        weapon.stats = new WeaponStats { damage = 2f, amount = 3f, duration = 2f };
        weapon.lantern = projectileTemplate;
        Set(weapon, "attackDamage", 10f);
        Set(weapon, "attackSpeed", 1f);
        Set(weapon, "amount", 2f);
        Set(weapon, "duration", 4f);
        Call(weapon, "Start");
        Check(ReferenceEquals(Get(weapon, "buffHolder"), holder), "lantern Start resolves holder");

        List<GameObject> baseline = Volley(weapon, projectileTemplate);
        Equal(baseline.Count, 5f, "lantern baseline additive count");
        foreach (GameObject obj in baseline)
        {
            LanternProjController projectile = obj.GetComponent<LanternProjController>();
            Equal(projectile.damage, 20f, "lantern baseline damage");
            Equal(projectile.duration, 8f, "lantern duration snapshot");
            Check(!obj.activeSelf && !projectile.enabled && projectile.RB != null, "inactive initialized projectile clone");
        }
        LanternProjController baselineProjectile = baseline[0].GetComponent<LanternProjController>();
        Call(baselineProjectile, "Start");
        float before = enemy.health;
        List<GameObject> baselineFires = Impact(baselineProjectile, first, fireTemplate);
        Equal(before - enemy.health, 20f, "unbuffed actual projectile damage");
        Equal(baselineFires.Count, 1f, "unbuffed impact spawns one fire");
        Equal(baselineFires[0].GetComponent<LanternFire>().damage, 10f, "unbuffed half-fire damage");

        Check(holder.TryAddBuff(recipe), "grant lantern recipe");
        BuffInstance instance = holder.FindBuff(recipe);
        Equal(instance.StackInstances[0].CurrentStacks, 0f, "lantern starts without stacks");
        List<GameObject> volley = Volley(weapon, projectileTemplate);
        Equal(volley.Count, 5f, "lantern preactivation count");
        foreach (GameObject obj in volley)
            Equal(obj.GetComponent<LanternProjController>().damage, 24f, "lantern 1.2 damage snapshot");
        Equal(instance.StackInstances[0].CurrentStacks, 0f, "spawning does not report hits");

        LanternProjController shot = volley[0].GetComponent<LanternProjController>();
        before = enemy.health;
        var explosionsBefore = CloneSet(explosionTemplate);
        List<GameObject> fires = Impact(shot, first, fireTemplate);
        Call(shot, "OnTriggerEnter2D", second);
        Call(shot, "OnTriggerEnter2D", first);
        Equal(before - enemy.health, 24f, "duplicate projectile callbacks damage once");
        Equal(fires.Count, 1f, "projectile spawns one fire");
        Equal(NewClones(fireTemplate, new HashSet<GameObject>(baselineFires)).Count, 1f, "duplicate impact creates no extra fire");
        Equal(NewClones(explosionTemplate, explosionsBefore).Count, 1f, "duplicate impact creates one explosion");
        Equal(instance.StackInstances[0].CurrentStacks, 1f, "one confirmed projectile hit adds one stack");

        LanternFire fire = fires[0].GetComponent<LanternFire>();
        Equal(fire.damage, 12f, "fire inherits half of snapshotted projectile damage");
        Equal(fire.duration, 8f, "fire inherits duration");
        Check(ReferenceEquals(Get(fire, "buffSource"), holder), "fire inherits buff source");
        Call(fire, "Start");
        before = enemy.health;
        Call(fire, "OnTriggerEnter2D", first);
        Call(fire, "OnTriggerEnter2D", second);
        Call(fire, "OnTriggerEnter2D", first);
        Equal(before - enemy.health, 12f, "multicollider fire entry damages once");
        Equal(fire.burningList.Count, 1f, "multicollider fire tracks one enemy");
        Equal(instance.StackInstances[0].CurrentStacks, 2f, "fire entry adds one stack");
        before = enemy.health;
        Tick(fire);
        Equal(before - enemy.health, 12f, "fire tick damages once per enemy");
        Equal(instance.StackInstances[0].CurrentStacks, 3f, "fire tick reports confirmed hit");
        Call(fire, "OnTriggerExit2D", first);
        Equal(fire.burningList.Count, 1f, "partial exit keeps enemy burning");
        before = enemy.health;
        Tick(fire);
        Equal(before - enemy.health, 12f, "partial exit retains ticking");
        Equal(instance.StackInstances[0].CurrentStacks, 4f, "four confirmed hits before activation");
        Equal(holder.CalculateWeaponDamage(20f), 24f, "fourth hit does not activate");

        second.enabled = false;
        before = enemy.health;
        Tick(fire);
        Equal(enemy.health, before, "disabled remaining collider receives no tick");
        Equal(fire.burningList.Count, 0f, "disabled collider is removed without exit");
        Equal(instance.StackInstances[0].CurrentStacks, 4f, "cleanup adds no hit stack");
        second.enabled = true;
        Call(fire, "OnTriggerEnter2D", second);
        Equal(before - enemy.health, 12f, "fifth hit retains old fire snapshot");
        Equal(instance.StackInstances[0].CurrentStacks, 5f, "fifth confirmed hit reaches threshold");
        Equal(holder.CalculateWeaponDamage(20f), 26f, "internal damage bonuses combine to 1.3");
        Equal(holder.CalculateProjectileCount(5), 6f, "internal projectile plus one activates");
        foreach (GameObject obj in volley)
            Equal(obj.GetComponent<LanternProjController>().damage, 24f, "existing volley remains snapshotted");
        List<GameObject> next = Volley(weapon, projectileTemplate);
        Equal(next.Count, 6f, "fifth hit changes next lantern volley count");
        foreach (GameObject obj in next)
            Equal(obj.GetComponent<LanternProjController>().damage, 26f, "next lantern volley damage");
        before = enemy.health;
        Tick(fire);
        Equal(before - enemy.health, 12f, "existing fire damage remains fixed after activation");
        Equal(holder.CalculateWeaponDamage(20f), 26f, "TriggerOnce does not repeat damage activation");
        Equal(holder.CalculateProjectileCount(5), 6f, "TriggerOnce does not repeat projectile activation");
        Call(fire, "OnTriggerExit2D", second);
        Equal(fire.burningList.Count, 0f, "last exit removes enemy");

        LanternProjController unused = next[0].GetComponent<LanternProjController>();
        var priorFires = CloneSet(fireTemplate);
        Call(unused, "OnTriggerEnter2D", dead.GetComponent<Collider2D>());
        Call(unused, "OnTriggerEnter2D", inactive.GetComponent<Collider2D>());
        Check(!(bool)Get(unused, "hasHit"), "dead and inactive targets do not consume projectile");
        Equal(NewClones(fireTemplate, priorFires).Count, 0f, "invalid projectile targets spawn no fire");
        Call(fire, "OnTriggerEnter2D", dead.GetComponent<Collider2D>());
        Call(fire, "OnTriggerEnter2D", inactive.GetComponent<Collider2D>());
        Equal(fire.burningList.Count, 0f, "fire rejects dead and inactive targets");
        Equal(dead.health, 0f, "dead target remains undamaged");
        Equal(inactive.health, 100000f, "inactive target remains undamaged");
    }

    private static void LightningChecks(BuffDefinition recipe, EnemyController enemy,
        EnemyController dead, EnemyController inactive)
    {
        BuffController holder = Temp("LightningHolder", true).AddComponent<BuffController>();
        GameObject weaponObject = Temp("LightningWeapon", false);
        weaponObject.transform.SetParent(holder.transform, true);
        LightningController weapon = weaponObject.AddComponent<LightningController>();
        weapon.enabled = false;
        weapon.stats = new WeaponStats { damage = 2f, amount = 3f, range = 1f };
        weapon.attackDamage = 10f;
        weapon.attackSpeed = 1f;
        weapon.attackRange = 5f;
        weapon.amount = 2f;
        weapon.lightningPrefab = Temp("LightningVisual", false);
        Call(weapon, "Start");
        Check(ReferenceEquals(Get(weapon, "buffHolder"), holder), "lightning Start resolves holder");
        Physics2D.SyncTransforms();
        Check(ReferenceEquals(Call(weapon, "FindRandomEnemy"), enemy), "lightning selects only living active enemy");
        float before = enemy.health;
        var visuals = CloneSet(weapon.lightningPrefab);
        Call(weapon, "Update");
        Equal(before - enemy.health, 20f, "lightning no-buff baseline damage");
        Equal((float)Get(weapon, "strikes"), 5f, "lightning multiplicative baseline count includes first strike");
        Equal(NewClones(weapon.lightningPrefab, visuals).Count, 1f, "lightning creates one visual per confirmed strike");

        Check(holder.TryAddBuff(recipe), "grant independent lightning recipe");
        BuffInstance instance = holder.FindBuff(recipe);
        Set(weapon, "attackCounter", 0f);
        before = enemy.health;
        visuals = CloneSet(weapon.lightningPrefab);
        Call(weapon, "Update");
        Equal(before - enemy.health, 24f, "lightning first buffed strike");
        Equal((float)Get(weapon, "strikes"), 5f, "lightning preactivation burst contains six strikes");
        Equal(instance.StackInstances[0].CurrentStacks, 1f, "lightning confirmed hit adds one stack");
        for (int hit = 2; hit <= 6; hit++)
        {
            before = enemy.health;
            NextStrike(weapon);
            Equal(before - enemy.health, 24f, "lightning burst snapshot hit " + hit);
            Equal(instance.StackInstances[0].CurrentStacks, Mathf.Min(hit, 5), "lightning TriggerOnce stacks hit " + hit);
            if (hit == 4)
                Equal(holder.CalculateWeaponDamage(20f), 24f, "lightning fourth hit does not activate");
            if (hit == 5)
            {
                Equal(holder.CalculateWeaponDamage(20f), 26f, "lightning fifth hit activates internal effects");
                Equal((float)Get(weapon, "strikes"), 1f, "activation does not add strikes to current burst");
            }
        }
        Equal(NewClones(weapon.lightningPrefab, visuals).Count, 6f, "six confirmed strikes create six visuals");
        Set(weapon, "attackCounter", 0f);
        before = enemy.health;
        Call(weapon, "Update");
        Equal(before - enemy.health, 26f, "next lightning burst uses activated damage");
        Equal((float)Get(weapon, "strikes"), 6f, "next lightning burst count is two times three plus one");
        Equal((float)Get(weapon, "strikeDamage"), 26f, "lightning stores activated damage snapshot");

        // A fresh runtime instance lets no-target and zero-damage paths prove zero stack increments.
        Check(holder.RemoveBuff(recipe) && holder.TryAddBuff(recipe), "reset lightning buff runtime");
        instance = holder.FindBuff(recipe);
        enemy.gameObject.SetActive(false);
        Physics2D.SyncTransforms();
        Check(Call(weapon, "FindRandomEnemy") == null, "no dead or inactive lightning targeting");
        visuals = CloneSet(weapon.lightningPrefab);
        NextStrike(weapon);
        Equal(instance.StackInstances[0].CurrentStacks, 0f, "no target means no confirmed hit");
        Equal(NewClones(weapon.lightningPrefab, visuals).Count, 0f, "no target means no lightning visual");
        Equal(dead.health, 0f, "lightning leaves dead enemy untouched");
        Equal(inactive.health, 100000f, "lightning leaves inactive enemy untouched");
        enemy.gameObject.SetActive(true);
        Physics2D.SyncTransforms();
        weapon.attackDamage = 0f;
        Set(weapon, "attackCounter", 0f);
        before = enemy.health;
        Call(weapon, "Update");
        Equal(enemy.health, before, "zero-damage strike causes no health loss");
        Equal(instance.StackInstances[0].CurrentStacks, 0f, "zero damage adds no stack");
        holder.Tick(100000f);
        Check(instance.IsActive, "recipe is permanent");
    }

    private static BuffDefinition Recipe()
    {
        BuffDefinition definition = ScriptableObject.CreateInstance<BuffDefinition>();
        try
        {
            definition.name = prefix + "Recipe";
            var damage = new DamageBuffAtom();
            Set(damage, "damageMultiplier", 1.2f);
            var internalDamage = new DamageBuffAtom();
            Set(internalDamage, "damageMultiplier", 1.1f);
            var projectile = new ProjectileBuffAtom();
            Set(projectile, "projectileCount", 1);
            var activation = new ActivateBuffEffects();
            Set(activation, "effects", new List<BuffAtom> { internalDamage, projectile });
            var stack = new StackToActivationBuffAtom();
            Set(stack, "stackCount", 5);
            Set(stack, "condition", new HitStackCondition());
            Set(stack, "activation", activation);
            Set(stack, "consumeMode", StackConsumeMode.TriggerOnce);
            Set(definition, "isPermanent", true);
            Set(definition, "atoms", new List<BuffAtom> { damage, stack });
            Check(definition.isValid, "reflected permanent buff recipe is valid");
            return definition;
        }
        catch
        {
            Object.DestroyImmediate(definition);
            throw;
        }
    }

    private static GameObject Temp(string name, bool active)
    {
        var obj = new GameObject(prefix + name);
        obj.SetActive(false);
        obj.transform.position = Origin;
        obj.SetActive(active);
        return obj;
    }

    private static EnemyController Enemy(string name, float health, bool active)
    {
        GameObject obj = Temp(name, false);
        obj.tag = "Enemy";
        Rigidbody2D rb = obj.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        obj.AddComponent<BoxCollider2D>();
        EnemyController enemy = obj.AddComponent<EnemyController>();
        enemy.enabled = false;
        enemy.RB = rb;
        enemy.health = health;
        obj.SetActive(active);
        return enemy;
    }

    private static List<GameObject> Volley(LanternController weapon, GameObject template)
    {
        var before = CloneSet(template);
        Set(weapon, "attackCounter", 0f);
        Call(weapon, "Update");
        return NewClones(template, before);
    }

    private static List<GameObject> Impact(LanternProjController projectile, Collider2D collider, GameObject template)
    {
        var before = CloneSet(template);
        Call(projectile, "OnTriggerEnter2D", collider);
        return NewClones(template, before);
    }

    private static void Tick(LanternFire fire)
    {
        fire.tickCounter = 0f;
        fire.durationCounter = 100000f;
        Call(fire, "Update");
    }

    private static void NextStrike(LightningController weapon)
    {
        Set(weapon, "attackCounter", 100000f);
        Set(weapon, "strikeCounter", 0f);
        Call(weapon, "Update");
    }

    private static HashSet<GameObject> CloneSet(GameObject template)
    {
        var result = new HashSet<GameObject>();
        foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
            if (obj != null && obj.scene.IsValid() && obj.name == template.name + "(Clone)")
                result.Add(obj);
        return result;
    }

    private static List<GameObject> NewClones(GameObject template, HashSet<GameObject> before)
    {
        var result = new List<GameObject>();
        foreach (GameObject obj in CloneSet(template))
            if (!before.Contains(obj)) result.Add(obj);
        result.Sort(delegate(GameObject a, GameObject b) { return a.GetInstanceID().CompareTo(b.GetInstanceID()); });
        return result;
    }

    private static FieldInfo Field(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, Fields | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        throw new MissingFieldException(target.GetType().FullName, name);
    }

    private static object Get(object target, string name) { return Field(target, name).GetValue(target); }
    private static void Set(object target, string name, object value) { Field(target, name).SetValue(target, value); }

    private static object Call(object target, string name, params object[] arguments)
    {
        // world-aware damage paths require a live source; templates remain inactive until exercised
        if ((target is LanternProjController || target is LanternFire)
            && (name == "OnTriggerEnter2D" || name == "Update"))
        {
            var source = (MonoBehaviour)target;
            source.enabled = true;
            source.gameObject.SetActive(true);
        }
        MethodInfo method = target.GetType().GetMethod(name, Fields);
        if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
        try { return method.Invoke(target, arguments); }
        catch (TargetInvocationException error)
        {
            throw new InvalidOperationException(target.GetType().Name + "." + name + " failed after " + checks + " checks.", error.InnerException ?? error);
        }
    }

    private static void Equal(float actual, float expected, string label)
    {
        Check(!float.IsNaN(actual) && Mathf.Abs(actual - expected) < 0.001f,
            label + " (expected " + expected + ", got " + actual + ")");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException("WeaponBuffChecks failed after " + checks + " checks: " + label);
        checks++;
    }
}
