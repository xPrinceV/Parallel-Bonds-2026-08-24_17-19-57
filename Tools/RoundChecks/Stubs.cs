// Deterministic service doubles, not a simulation of Unity rendering or physics.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class SerializeField : Attribute { }
    public class MinAttribute : Attribute { public MinAttribute(float value) { } }
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int value) { } }
    public class DisallowMultipleComponent : Attribute { }
    public enum FindObjectsInactive { Include }
    public class Object
    {
        public static T[] FindObjectsByType<T>(FindObjectsInactive inactive) => Array.Empty<T>();
        public static T Instantiate<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent) where T : Object
            => (T)Activator.CreateInstance(prefab.GetType());
        public static void Destroy(Object obj) { }
    }
    public class GameObject : Object
    {
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf;
        public SceneManagement.Scene scene = new SceneManagement.Scene(1);
        private Transform root;
        public Transform transform => root ?? (root = new Transform { gameObject = this });
        public void SetActive(bool active) => activeSelf = active;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => Array.Empty<T>();
    }
    public class Component : Object
    {
        public GameObject gameObject = new GameObject();
        public Transform transform => gameObject.transform;
        public World Owner;
        public readonly Dictionary<Type, object> Components = new Dictionary<Type, object>();
        public T GetComponent<T>() where T : class => Components.TryGetValue(typeof(T), out var value) ? value as T : null;
        public bool TryGetComponent<T>(out T value) where T : class { value = GetComponent<T>(); return value != null; }
        public T GetComponentInChildren<T>(bool includeInactive) where T : class => null;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => Array.Empty<T>();
    }
    public class Transform : Component { public Transform parent; }
    public class MonoBehaviour : Component
    {
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }
    public struct Vector3 { }
    public struct Quaternion { public static Quaternion identity => default; }
    public static class Time
    {
        public static float deltaTime = 0.125f, timeScale = 1f;
        public static int frameCount;
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
    }
    public static class Debug
    {
        public static void LogError(string message, Object context) => throw new Exception(message);
        public static void LogWarning(string message, Object context) => Console.WriteLine("EXPECTED WARNING: " + message);
    }
    public static class Application { public static bool CanStreamedLevelBeLoaded(string name) => true; }
}
namespace UnityEngine.SceneManagement
{
    public readonly record struct Scene(int buildIndex)
    {
        public bool IsValid() => buildIndex > 0;
        public string path => "Test";
    }
    public static class SceneManager { public static void LoadScene(string scene) { } }
}

public enum WorldId { Material, Echo }
public class PlayerController : UnityEngine.MonoBehaviour { }
public class PlayerHealth : UnityEngine.MonoBehaviour
{
    public bool IsDead;
    public bool HasInitialized = true;
    public float currentHealth = 100f;
}
public class UIController
{
    public static UIController instance;
    public UnityEngine.GameObject levelUpPanel;
}
public class World : UnityEngine.MonoBehaviour
{
    public WorldManager Manager;
    public PlayerController Player = new PlayerController();
    public PlayerController InteractionPlayer => Player;
    public UnityEngine.Transform ContentRoot = new UnityEngine.Transform();
    public WorldMap Map = new WorldMap();
    public bool IsConfigured = true;
    public static World GetFor(UnityEngine.Component component) => component?.Owner;
    public bool ContainsContent(UnityEngine.Transform transform) => true;
}
public class WorldMap
{
    public bool TryFindSafeSpawnPosition(UnityEngine.Transform prefab, UnityEngine.Transform root,
        PlayerController player, float distance, out UnityEngine.Vector3 position)
    { position = default; return true; }
}
public class WorldManager : UnityEngine.MonoBehaviour
{
    public bool IsInitialized = true, IsSwitching, IsFused, IsFinalFusion, IsFusionTransitioning;
    public bool RejectCommit, RejectFusion;
    public int CommitAttempts, SuccessfulCommits, FusionAttempts;
    public WorldId CurrentWorldId;
    public World[] Worlds;
    public World CurrentWorld => Worlds[(int)CurrentWorldId];
    public StateSwitchController SwitchFlow;
    public RunStageController RunController;
    public FusionTransitionController FusionTransition = new FusionTransitionController();
    public bool IsSharedObject(UnityEngine.Transform transform) => true;
    public bool CommitWorldSwitch(WorldId target)
    {
        CommitAttempts++;
        if (RejectCommit || IsFused || target == CurrentWorldId) return false;
        CurrentWorldId = target;
        SuccessfulCommits++;
        return true;
    }
    public bool BeginFinalFusion()
    {
        FusionAttempts++;
        if (IsFinalFusion) return false;
        // Match the production hook's cancellation even when fusion is rejected.
        SwitchFlow.CancelTransition();
        if (RejectFusion) return false;
        IsFused = IsFinalFusion = IsFusionTransitioning = true;
        SwitchFlow.enabled = false;
        SwitchFlow.CancelTransition();
        return true;
    }
}
public class FusionTransitionController : UnityEngine.MonoBehaviour
{
    public event Action Completed;
    public void Complete() => Completed?.Invoke();
}
public class EnemySpawner : UnityEngine.MonoBehaviour
{
    public void ConfigureExternalStages() { }
    public bool CanStartWave(int index) => true;
    public bool TryStartWave(int index) => true;
    public void StopSpawning(bool clear) { }
}
public class LevelManager : UnityEngine.MonoBehaviour { public void ConfigureStages(RunStageController run) { } }
public class EnemyController : UnityEngine.MonoBehaviour
{
    public object RB = new object();
    public float health;
    public event Action<EnemyController> Died;
    public void Die() => Died?.Invoke(this);
}
public class TitanEnemyController : EnemyController { }
public class RiftLordBarrage : UnityEngine.MonoBehaviour { public void InitializeSpawnHealth(float health) { } }
public interface IWorldProjectile { void Despawn(); }
public class HollowProjectile : UnityEngine.MonoBehaviour { }
public class TitanAttack : UnityEngine.MonoBehaviour { }
public class ArrowController : UnityEngine.MonoBehaviour { }
public class BulletController : UnityEngine.MonoBehaviour { }
public class DaggerProjectile : UnityEngine.MonoBehaviour { }
public class LanternProjController : UnityEngine.MonoBehaviour { }
public class LanternFire : UnityEngine.MonoBehaviour { }
public class ScytheHitController : UnityEngine.MonoBehaviour { }
