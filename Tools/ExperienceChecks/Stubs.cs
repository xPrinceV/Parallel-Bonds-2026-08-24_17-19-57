// Only external dependencies are doubled; progression and selection run from linked source.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public static int InstantiateCalls;
        public static T Instantiate<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent) where T : Object
        {
            InstantiateCalls++;
            throw new NotSupportedException("These checks must not instantiate pickups or clone choices.");
        }
    }
    public class GameObject : Object
    {
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf;
        public void SetActive(bool active) => activeSelf = active;
    }
    public class Component : Object
    {
        public GameObject gameObject = new GameObject();
        public World Owner;
        public readonly Dictionary<Type, object> Components = new Dictionary<Type, object>();
        public T GetComponent<T>() where T : class => Components.TryGetValue(typeof(T), out var value) ? value as T : null;
    }
    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }
    public class Transform : Component { }
    public class Sprite : Object { }
    public struct Vector3 { }
    public struct Quaternion { public static Quaternion identity => default; }
    public static class Time
    {
        public static float timeScale = 1f, unscaledTime;
    }
    public static class Mathf
    {
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
    }
    public static class Random
    {
        // Unscripted choices take the first legal item; scripts choose a specific weapon/stat.
        public static readonly Queue<int> Indices = new Queue<int>();
        public static int Range(int min, int max)
        {
            if (max <= min) throw new InvalidOperationException("Empty random choice range.");
            int value = Indices.Count == 0 ? min : Indices.Dequeue();
            if (value < min || value >= max) throw new InvalidOperationException("Invalid scripted choice.");
            return value;
        }
    }
    public static class Debug { public static void Log(object message) { } }
}
namespace UnityEngine.UI
{
    public class Image { public UnityEngine.Sprite sprite; }
    public class Slider { }
}
namespace TMPro { public class TMP_Text { public string text; } }

public class ExpPickup : UnityEngine.MonoBehaviour { public int expValue; }
public enum UpgradeType { Damage, Speed, Range, AttackSpeed, Amount, Duration, Bounces, Area }
public class WeaponStats
{
    public float damage, speed, range, attackSpeed, amount, duration, bounces, area;
    public float[] damageUpgrades = { 0.25f }, speedUpgrades = { 0.25f }, rangeUpgrades = { 0.25f },
        attackSpeedUpgrades = { 0.25f }, amountUpgrades = { 1f }, durationUpgrades = { 0.25f },
        bouncesUpgrades = { 1f }, areaUpgrades = { 0.25f };
}
public class Weapon : UnityEngine.MonoBehaviour
{
    public string weaponName;
    public UnityEngine.Sprite icon = new UnityEngine.Sprite();
    public UpgradeType[] availableUpgrades = { UpgradeType.Damage };
    public WeaponStats stats = new WeaponStats();
    public PlayerController SourceOwner;
}
public class PlayerController : UnityEngine.MonoBehaviour
{
    public static PlayerController instance;
    public readonly List<Weapon> assignedWeapons = new List<Weapon>();
    // Contract double for PlayerController's fusion-aware inventory, not an ownership transfer.
    public IReadOnlyList<Weapon> EquippedWeapons => Owner?.Manager != null && Owner.Manager.IsFused
        && Owner.Manager.FusionPlayer == this ? Owner.Manager.FusionWeapons : assignedWeapons;
    public bool HasEquippedWeapon(Weapon weapon)
    {
        foreach (Weapon equipped in EquippedWeapons)
            if (weapon != null && ReferenceEquals(equipped, weapon)) return true;
        return false;
    }
}
public class WorldManager
{
    public bool IsFused;
    public PlayerController FusionPlayer;
    public readonly List<Weapon> FusionWeapons = new List<Weapon>();
}
public class World
{
    public WorldManager Manager;
    public PlayerController Player;
    public PlayerController InteractionPlayer => Manager.IsFused ? Manager.FusionPlayer : Player;
    public static World GetFor(UnityEngine.Component component) => component?.Owner;
    public static UnityEngine.Transform GetContentRoot(UnityEngine.Component component) => null;
}
public class UIController
{
    public static UIController instance;
    public UnityEngine.GameObject levelUpPanel = new UnityEngine.GameObject();
    public LevelUpSelectionButton[] levelUpButtons;
    public UnityEngine.UI.Slider expLvlSlider = new UnityEngine.UI.Slider();
    public TMPro.TMP_Text expLvlText = new TMPro.TMP_Text();
    public int Experience, Required, Level, Refreshes;
    public void UpdateExperience(int experience, int required, int level)
    {
        Experience = experience;
        Required = required;
        Level = level;
        Refreshes++;
    }
}
