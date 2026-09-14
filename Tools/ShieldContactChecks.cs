using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

// Compile outside Assets against current Assembly-CSharp and Unity assemblies.
// Invoke Run on Unity's main thread in Play Mode. Geometry queries are real; trigger
// callbacks and the weapon update are invoked explicitly, without yielding a frame.
public static class ShieldContactChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static bool running;

    public static string Run()
    {
        if (!Application.isPlaying || running)
            throw new InvalidOperationException("Requires Unity Play Mode on the main thread, without reentry.");
        running = true;
        var previousPlayer = PlayerController.instance;
        var previousNumbers = DamageNumberController.instance;
        bool autoSync = Physics2D.autoSyncTransforms;
        var fixture = new Fixture();
        try
        {
            // Isolate nonlethal damage from Main's presentation pool during this synchronous call.
            DamageNumberController.instance = null;
            Physics2D.autoSyncTransforms = false;
            fixture.Run();
            return fixture.Checks + " Shield contact checks passed. Real collider-distance queries; "
                + "manual trigger/update delivery. No real-frame fusion or physics event-order test.";
        }
        finally
        {
            try { fixture.Dispose(); }
            finally
            {
                PlayerController.instance = previousPlayer;
                DamageNumberController.instance = previousNumbers;
                Physics2D.autoSyncTransforms = autoSync;
                Physics2D.SyncTransforms();
                running = false;
            }
        }
    }

    sealed class Fixture : IDisposable
    {
        readonly List<GameObject> roots = new List<GameObject>();
        readonly Vector3 origin = new Vector3(25000f, 25000f, 0f);
        Transform content;
        Transform otherContent;
        PlayerController player;
        ShieldController weapon;
        ShieldOrbitController shield;
        Dictionary<Collider2D, EnemyController> contacts;
        public int Checks;

        public void Run()
        {
            content = MakeWorld("ShieldChecks_Source");
            otherContent = MakeWorld("ShieldChecks_Other");
            player = Make("Owner", content, origin).AddComponent<PlayerController>();
            GameObject template = Make("ShieldChecks_Template", null, origin + Vector3.up * 100f);
            template.AddComponent<BoxCollider2D>().isTrigger = true;
            template.AddComponent<ShieldOrbitController>();
            GameObject weaponObject = Make("Weapon", player.transform, origin);
            weapon = weaponObject.AddComponent<ShieldController>();
            weapon.player = player;
            weapon.stats = new WeaponStats();
            weapon.shieldPrefab = template;
            weapon.damage = 10f;
            weapon.duration = 1000f;
            weapon.cooldown = 1f;
            weapon.orbitDistance = weapon.orbitSpeed = 0f;
            Call(weapon, "Start");
            shield = weapon.shieldPivot.GetComponentInChildren<ShieldOrbitController>();
            contacts = (Dictionary<Collider2D, EnemyController>)Get(shield, "contacts");
            Check(shield != null && shield.isActiveAndEnabled, "production activation creates live shield");

            EnemyController enemy = Enemy("ContactEnemy");
            Collider2D first = enemy.GetComponent<Collider2D>();
            Collider2D second = Make("SecondCollider", enemy.transform, origin).AddComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();
            Enter(first); Enter(second);
            Check(enemy.health == 90f && contacts.Count == 2, "initial multi-collider hit occurs once");

            Pause();
            Call(shield, "OnTriggerExit2D", first);
            Call(shield, "OnTriggerExit2D", second);
            Check(contacts.Count == 2, "disabled callbacks retain records until geometry is available");
            enemy.transform.position += Vector3.right * 10f;
            Resume();
            Check(contacts.Count == 0, "enemy that moved away while shield slept is removed");
            enemy.transform.position = shield.transform.position;
            Physics2D.SyncTransforms();
            Enter(first); Enter(second);
            Check(enemy.health == 80f && contacts.Count == 2, "legitimate return hits once after stale records are removed");

            Pause(); Resume();
            Enter(first); Enter(second);
            Check(enemy.health == 80f && contacts.Count == 2, "continuing overlaps do not award duplicate wake damage");

            Pause();
            second.transform.localPosition = Vector3.right * 10f;
            Resume();
            Check(contacts.Count == 1 && contacts.ContainsKey(first), "only separated collider removed from multi-collider enemy");
            second.transform.localPosition = Vector3.zero;
            Physics2D.SyncTransforms();
            Enter(second);
            Check(enemy.health == 80f && contacts.Count == 2, "retained sibling contact suppresses returning collider damage");
            Call(shield, "OnTriggerExit2D", first);
            Call(shield, "OnTriggerExit2D", second);
            Enter(first); Enter(second);
            Check(enemy.health == 70f, "full exit still permits exactly one later contact hit");

            // Simulate projection being established after world activation/OnEnable.
            Pause();
            weapon.enabled = true;
            Check(!weapon.shieldPivot.activeSelf, "OnEnable cannot reactivate stale pivot geometry");
            player.transform.position += Vector3.right * 20f;
            enemy.transform.position = player.transform.position;
            Call(weapon, "Update");
            Check(weapon.shieldPivot.transform.position == player.transform.position,
                "resume follows latest owner position, not pre-activation pivot");
            Check(contacts.Count == 2, "overlaps at projected position survive even without prior SyncTransforms");
            Enter(first); Enter(second);
            Check(enemy.health == 70f, "projected continuing contacts suppress both wake callbacks");

            Pause();
            player.transform.position += Vector3.right * 20f;
            Resume();
            Check(contacts.Count == 0, "enemy at stale pivot position is separated from resumed pivot");
            enemy.transform.position = player.transform.position;
            Physics2D.SyncTransforms();
            Enter(first); Enter(second);
            Check(enemy.health == 60f, "post-projection return can hit once again");

            InvalidatedContacts();

            // Owner inactive while the weapon itself remains enabled uses the same resume path.
            player.enabled = false;
            Call(weapon, "Update");
            Check(!weapon.shieldPivot.activeSelf, "inactive owner hides pivot");
            enemy.transform.position += Vector3.right * 10f;
            player.enabled = true;
            Call(weapon, "Update");
            Check(!contacts.ContainsKey(first) && !contacts.ContainsKey(second),
                "owner-only disable also reconciles stale contacts");
        }

        void InvalidatedContacts()
        {
            EnemyController disabledCollider = Enemy("DisabledCollider");
            EnemyController inactive = Enemy("InactiveEnemy");
            EnemyController disabledEnemy = Enemy("DisabledBehaviour");
            EnemyController dead = Enemy("DeadEnemy");
            EnemyController destroyedCollider = Enemy("DestroyedCollider");
            EnemyController destroyedEnemy = Enemy("DestroyedEnemy");
            EnemyController otherWorld = Enemy("NoLongerInteractable");
            EnemyController unsimulated = Enemy("UnsimulatedBody");
            var body = unsimulated.gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            var enemies = new[] { disabledCollider, inactive, disabledEnemy, dead,
                destroyedCollider, destroyedEnemy, otherWorld, unsimulated };
            var colliders = new List<Collider2D>();
            Physics2D.SyncTransforms();
            foreach (EnemyController enemy in enemies)
            {
                Collider2D collider = enemy.GetComponent<Collider2D>();
                colliders.Add(collider);
                Enter(collider);
                Check(contacts.ContainsKey(collider), "seed " + enemy.name);
            }
            Pause();
            colliders[0].enabled = false;
            inactive.gameObject.SetActive(false);
            disabledEnemy.enabled = false;
            dead.health = 0f;
            Object.DestroyImmediate(colliders[4]);
            Object.DestroyImmediate(destroyedEnemy.gameObject);
            otherWorld.transform.SetParent(otherContent, true);
            body.simulated = false;
            Resume();
            foreach (Collider2D collider in colliders)
                Check(!contacts.ContainsKey(collider), "invalidated collider record removed, including destroyed Unity keys");
            Check(contacts.Count == 2, "unrelated continuing multi-collider contact is preserved");
        }

        void Pause()
        {
            float remaining = (float)Get(weapon, "timer");
            var pivot = weapon.shieldPivot;
            weapon.enabled = false;
            Check(pivot != null && weapon.shieldPivot == pivot && !pivot.activeSelf,
                "disable preserves and hides pivot");
            Check((float)Get(weapon, "timer") == remaining, "disable preserves remaining duration");
        }

        void Resume()
        {
            weapon.enabled = true;
            Check(!weapon.shieldPivot.activeSelf, "activation waits for owner-positioned weapon update");
            Call(weapon, "Update");
            Check(weapon.shieldPivot.activeSelf, "weapon update resumes pivot");
        }

        void Enter(Collider2D collider) { Call(shield, "OnTriggerEnter2D", collider); }

        EnemyController Enemy(string name)
        {
            GameObject obj = Make(name, content, shield.transform.position);
            obj.AddComponent<BoxCollider2D>();
            var enemy = obj.AddComponent<EnemyController>();
            enemy.health = 100f;
            return enemy;
        }

        Transform MakeWorld(string name)
        {
            GameObject root = Make(name, null, Vector3.zero);
            World world = root.AddComponent<World>();
            GameObject contents = Make("Content", root.transform, Vector3.zero);
            typeof(World).GetField("contentRoot", Flags).SetValue(world, contents);
            return contents.transform;
        }

        GameObject Make(string name, Transform parent, Vector3 position)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.position = position;
            if (parent == null) roots.Add(obj);
            return obj;
        }

        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("ShieldContactChecks FAIL: " + message);
            Checks++;
        }

        public void Dispose()
        {
            // Remove the pivot before its owner to avoid deferred Despawn objects escaping the fixture.
            if (weapon != null && weapon.shieldPivot != null)
                Object.DestroyImmediate(weapon.shieldPivot);
            for (int i = roots.Count - 1; i >= 0; i--)
                if (roots[i] != null) Object.DestroyImmediate(roots[i]);
        }
    }

    static object Get(object target, string name)
    {
        return target.GetType().GetField(name, Flags).GetValue(target);
    }

    static void Call(object target, string name, params object[] args)
    {
        target.GetType().GetMethod(name, Flags).Invoke(target, args);
    }
}
