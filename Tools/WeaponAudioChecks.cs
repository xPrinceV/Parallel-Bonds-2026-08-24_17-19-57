using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Standalone only: compile with the six hooked controller sources and the real SoundId.cs.
// Uses deterministic doubles, not Unity playback/physics. Run from the repository root:
// pwsh -NoProfile -Command 'Add-Type -Path Tools/WeaponAudioChecks.cs,Assets/Game/Features/Audio/SoundId.cs,Assets/Game/Features/Weapons/Controllers/PistolController.cs,Assets/Game/Features/Weapons/Controllers/BowController.cs,Assets/Game/Features/Weapons/Controllers/SniperController.cs,Assets/Game/Features/Weapons/Controllers/LanternController.cs,Assets/Game/Features/Weapons/Controllers/ScytheController.cs,Assets/Game/Features/Weapons/Controllers/LightningController.cs -CompilerOptions /nowarn:0649; [WeaponAudioChecks]::Run()'
public static class WeaponAudioChecks
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int checks;

    public static string Run()
    {
        checks = 0;
        Volleys<PistolController, BulletController>("bullet", SoundId.PistolFire, true);
        Volleys<BowController, ArrowController>("arrow", SoundId.BowFire, false);
        Volleys<SniperController, PhysicsBullet>("bullet", SoundId.SniperFire, true);
        Volleys<LanternController, LanternProjController>("lantern", SoundId.LanternFire, false);
        Volleys<ScytheController, ScytheHitController>("scythe", SoundId.ScytheSwing, false);
        Lightning();
        return checks + " weapon audio action-path checks passed (test doubles; no Unity playback).";
    }

    private static T Setup<T>() where T : Weapon, new()
    {
        AudioService.Instance = new AudioService();
        WeaponQuery.Targets.Clear();
        Probe.Reset();
        var weapon = new T { player = new PlayerController(), stats = new WeaponStats() };
        Set(weapon, "attackSpeed", 1f);
        Set(weapon, "attackDamage", 10f);
        if (typeof(T).GetField("attackRange", Fields) != null) Set(weapon, "attackRange", 10f);
        Set(weapon, "buffHolder", weapon.player.Buffs);
        return weapon;
    }

    private static EnemyController Target(float health = 1000f, bool active = true, bool interactable = true)
    {
        var enemy = new EnemyController { health = health, Interactable = interactable };
        enemy.gameObject.activeInHierarchy = active;
        enemy.transform.position = new Vector3(1f, 0f, 0f);
        var collider = new Collider2D { Interactable = interactable, gameObject = enemy.gameObject };
        collider.gameObject.Add(enemy);
        WeaponQuery.Targets.Add(collider);
        return enemy;
    }

    private static void Volleys<T, P>(string prefabField, SoundId id, bool needsTarget)
        where T : Weapon, new() where P : Component, new()
    {
        T weapon = Setup<T>();
        var prefab = new GameObject();
        prefab.Add(new P());
        Set(weapon, prefabField, prefab);
        Call(weapon, "Start");
        Check(AudioService.Instance.Calls.Count == 0, id + " startup silent");
        Target();
        // Scythe keeps its base single swing; buffs expand every weapon's volley.
        weapon.player.Buffs.ExtraCount = 3;
        Ready(weapon);
        Call(weapon, "Update");
        Check(Probe.Spawns == 4 && Probe.Initialized == 4, id + " actual buff-expanded volley");
        Check(AudioService.Instance.Calls.Count == 1 && AudioService.Instance.Calls[0] == id,
            id + " exactly one correct request");
        Check(AudioService.Instance.InitializedAtPlay[0] == 4, id + " request follows projectile initialization");
        Check(weapon.player.Buffs.DamageCalls == 1 && weapon.player.Buffs.CountCalls == 1,
            id + " buff snapshot once");
        Call(weapon, "Update");
        Check(Probe.Spawns == 4 && AudioService.Instance.Calls.Count == 1, id + " cooldown frame silent");

        ResetEvidence();
        weapon.player.Buffs.ExtraCount = -100;
        Ready(weapon);
        Call(weapon, "Update");
        Check(Probe.Spawns == 0 && AudioService.Instance.Calls.Count == 0, id + " empty volley silent");

        weapon.player.Buffs.ExtraCount = 0;
        Ready(weapon);
        Call(weapon, "Update");
        Check(Probe.Spawns == 1 && AudioService.Instance.Calls.Count == 1, id + " single shot requests once");
        ResetEvidence();
        AudioService.Instance.Accept = false;
        weapon.player.Buffs.ExtraCount = 8;
        Ready(weapon);
        Call(weapon, "Update");
        Check(Probe.Spawns == 9 && AudioService.Instance.Calls.Count == 1, id + " declined playback does not retry per projectile");

        ResetEvidence();
        AudioService.Instance = null;
        Ready(weapon);
        Call(weapon, "Update");
        Check(Probe.Spawns == 9 && Probe.Initialized == 9, id + " absent service preserves volley");
        AudioService.Instance = new AudioService();

        if (needsTarget)
        {
            foreach (int invalid in new[] { 0, 1, 2, 3 })
            {
                WeaponQuery.Targets.Clear();
                if (invalid == 1) Target(0f);
                if (invalid == 2) Target(active: false);
                if (invalid == 3) Target(interactable: false);
                ResetEvidence();
                Ready(weapon);
                Call(weapon, "Update");
                Check(Probe.Spawns == 0 && AudioService.Instance.Calls.Count == 0, id + " invalid/no target " + invalid);
            }
        }
        if (typeof(T) != typeof(LanternController))
        {
            ResetEvidence();
            weapon.player.enabled = false;
            Ready(weapon);
            Call(weapon, "Update");
            Check(Probe.Spawns == 0 && AudioService.Instance.Calls.Count == 0, id + " inactive owner silent");
        }
    }

    private static void Lightning()
    {
        LightningController weapon = Setup<LightningController>();
        weapon.lightningPrefab = new GameObject();
        weapon.amount = 3;
        Call(weapon, "Start");
        Check(AudioService.Instance.Calls.Count == 0, "lightning startup silent");
        Ready(weapon);
        Call(weapon, "Update");
        Check(Probe.Spawns == 0 && AudioService.Instance.Calls.Count == 0, "lightning idle burst silent");
        Target(0f); Target(active: false); Target(interactable: false);
        Strike(weapon);
        Check(Probe.Spawns == 0 && AudioService.Instance.Calls.Count == 0, "lightning invalid targets silent");
        EnemyController enemy = Target();
        Strike(weapon);
        Check(enemy.health == 990f && Probe.Spawns == 1 && AudioService.Instance.Calls.Count == 1,
            "lightning first valid strike later in burst requests once");
        Check(AudioService.Instance.Calls[0] == SoundId.LightningStrike && AudioService.Instance.DamageCallsAtPlay[0] == 1,
            "lightning correct ID after actual damage call");

        ResetEvidence();
        Ready(weapon);
        Call(weapon, "Update");
        Strike(weapon); Strike(weapon);
        Check(Probe.Spawns == 3 && Probe.DamageCalls == 3 && AudioService.Instance.Calls.Count == 1,
            "lightning new burst resets gate; three strikes request once");
        Check(weapon.player.Buffs.Hits == 4 && enemy.health == 960f, "lightning damage/hit reporting preserved");
        Call(weapon, "Update");
        Check(AudioService.Instance.Calls.Count == 1, "lightning between bursts silent");

        ResetEvidence();
        AudioService.Instance.Accept = false;
        Ready(weapon);
        Call(weapon, "Update");
        Strike(weapon); Strike(weapon);
        Check(Probe.DamageCalls == 3 && AudioService.Instance.Calls.Count == 1, "lightning declined playback never retried within burst");

        ResetEvidence();
        AudioService.Instance = null;
        Ready(weapon);
        Call(weapon, "Update");
        AudioService.Instance = new AudioService();
        Strike(weapon); Strike(weapon);
        Check(Probe.DamageCalls == 3 && AudioService.Instance.Calls.Count == 0, "lightning missing service preserves strikes without late replay");

        ResetEvidence();
        weapon.amount = 0;
        Ready(weapon);
        Call(weapon, "Update");
        Check(Probe.Spawns == 0 && AudioService.Instance.Calls.Count == 0, "lightning empty burst silent");
    }

    private static void Strike(LightningController weapon)
    {
        Set(weapon, "strikeCounter", 0f);
        Call(weapon, "Update");
    }
    private static void Ready(Weapon weapon) { Set(weapon, "attackCounter", 0f); }
    private static void ResetEvidence() { Probe.Reset(); AudioService.Instance = new AudioService(); }
    private static void Set(object target, string name, object value) { target.GetType().GetField(name, Fields).SetValue(target, value); }
    private static void Call(object target, string name) { target.GetType().GetMethod(name, Fields).Invoke(target, null); }
    private static void Check(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
        checks++;
    }
}

// Doubles record requests, not accepted voices, so audio cooldown cannot hide duplicate hooks.

public sealed class AudioService
{
    public static AudioService Instance;
    public bool Accept = true;
    public readonly List<SoundId> Calls = new List<SoundId>();
    public readonly List<int> InitializedAtPlay = new List<int>();
    public readonly List<int> DamageCallsAtPlay = new List<int>();
    public bool Play(SoundId id)
    {
        Calls.Add(id); InitializedAtPlay.Add(Probe.Initialized); DamageCallsAtPlay.Add(Probe.DamageCalls);
        return Accept;
    }
}
public static class Probe
{
    public static int Spawns, Initialized, DamageCalls;
    public static void Reset() { Spawns = Initialized = DamageCalls = 0; }
}
public class Weapon : MonoBehaviour
{
    public PlayerController player;
    public WeaponStats stats;
    protected void ResolveOwner() { }
}
public class WeaponStats { public float damage = 1, amount = 1, range = 1, speed = 1, duration = 1, area = 1, attackSpeed = 1; }
public class PlayerController : MonoBehaviour
{
    public Vector2 facingDirection = Vector2.right;
    public BuffController Buffs = new BuffController();
    public PlayerController() { gameObject.Add(Buffs); }
}
public class BuffController : MonoBehaviour
{
    public int ExtraCount, DamageCalls, CountCalls, Hits;
    public float CalculateWeaponDamage(float damage) { DamageCalls++; return damage; }
    public int CalculateProjectileCount(int count) { CountCalls++; return Math.Max(0, count + ExtraCount); }
    public void ReportHit(GameObject enemy, float damage) { Hits++; }
}
public class EnemyController : MonoBehaviour
{
    public float health;
    public void TakeDamage(float damage) { Probe.DamageCalls++; health -= damage; }
}
public static class World
{
    public static bool CanInteract(Component a, Component b) { return a.Interactable && b.Interactable; }
    public static Transform GetContentRoot(Component component) { return null; }
}
public static class WeaponQuery
{
    public static readonly List<Collider2D> Targets = new List<Collider2D>();
    public static void OverlapCircle(Vector3 position, float range, List<Collider2D> results, List<Collider2D> scratch)
    { results.Clear(); results.AddRange(Targets); }
}
public class BulletController : MonoBehaviour
{
    public void SetTarget(EnemyController target) { }
    public void SetDamage(float damage) { }
    public void SetSpeed(float speed) { }
    public void SetKnockback(bool value) { }
    public void SetBuffSource(BuffController buffs) { Probe.Initialized++; }
}
public class ArrowController : BulletController { public void SetDirection(Vector2 direction) { } }
public class LanternProjController : BulletController { public void SetDuration(float duration) { } }
public class PhysicsBullet : MonoBehaviour
{
    public void Initialize(Vector2 direction, float damage, float speed, BuffController buffs) { Probe.Initialized++; }
}
public class ScytheHitController : MonoBehaviour
{
    public void SetSource(PlayerController player, BuffController buffs) { }
    public void SetDamage(float damage) { }
    public void SetArea(float area) { }
    public void SetDirection(Vector2 direction) { Probe.Initialized++; }
}

namespace UnityEngine
{
    public sealed class SerializeField : Attribute { }
    public sealed class MinAttribute : Attribute { public MinAttribute(float value) { } }
    public class GameObject
    {
        private readonly Dictionary<Type, Component> components = new Dictionary<Type, Component>();
        public bool activeInHierarchy = true;
        public void Add(Component component) { components[component.GetType()] = component; component.gameObject = this; }
        public T GetComponent<T>() where T : class
        {
            foreach (Component component in components.Values) if (component is T) return component as T;
            return null;
        }
    }
    public class Component
    {
        public GameObject gameObject = new GameObject();
        public Transform transform = new Transform();
        public bool Interactable = true;
        public T GetComponent<T>() where T : class { return gameObject.GetComponent<T>(); }
        public T GetComponentInParent<T>() where T : class { return GetComponent<T>(); }
    }
    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled { get { return enabled && gameObject.activeInHierarchy; } }
        // Identity/physics are outside this fixture; controllers still execute every initialization call.
        protected static GameObject Instantiate(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
        { if (prefab == null) throw new ArgumentNullException("prefab"); Probe.Spawns++; return prefab; }
    }
    public class Collider2D : Component { }
    public class Transform { public Vector3 position; public Quaternion rotation; }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 right { get { return new Vector2(1, 0); } }
        public float sqrMagnitude { get { return x * x + y * y; } }
        public static implicit operator Vector3(Vector2 v) { return new Vector3(v.x, v.y, 0); }
        public static implicit operator Vector2(Vector3 v) { return new Vector2(v.x, v.y); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
        public Vector3 normalized { get { return sqrMagnitude == 0 ? this : this * (1f / (float)Math.Sqrt(sqrMagnitude)); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static float Distance(Vector3 a, Vector3 b) { return (float)Math.Sqrt((a - b).sqrMagnitude); }
        public static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x); }
    }
    public struct Quaternion
    {
        public static Quaternion identity { get { return new Quaternion(); } }
        public static Quaternion Euler(float x, float y, float z) { return identity; }
        public static Vector2 operator *(Quaternion rotation, Vector2 v) { return v; }
    }
    public static class Mathf
    {
        public const float Infinity = float.PositiveInfinity;
        public static int Max(int a, int b) { return Math.Max(a, b); }
        public static int FloorToInt(float value) { return (int)Math.Floor(value); }
        public static float Clamp(float value, float min, float max) { return Math.Max(min, Math.Min(max, value)); }
    }
    public static class Time { public static float deltaTime = .01f; }
    public static class Random { public static int Range(int min, int max) { return min; } }
    public static class Debug { public static void LogError(string message, object context) { throw new InvalidOperationException(message); } }
}
