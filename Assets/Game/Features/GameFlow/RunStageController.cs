using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-80)]
public class RunStageController : MonoBehaviour
{
    [SerializeField] private WorldManager worldManager;
    [SerializeField] private EnemySpawner[] spawners;
    [SerializeField] private float[] stageDurations = { 20f, 20f, 20f };
    [SerializeField] private float finaleStartTime = 480f;
    [SerializeField] private bool useSharedWaves = true;
    [SerializeField] private EnemyController bossPrefab;
    [SerializeField] private float bossHealth = 600f;
    [SerializeField] private float bossDistance = 6f;

    public int CurrentStageIndex { get; private set; } = -1;
    public bool IsRunning { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool IsDefeated { get; private set; }
    public float RemainingStageTime { get; private set; }
    public float ElapsedTime { get; private set; }
    public float RemainingUntilFinale => Mathf.Max(0f, finaleStartTime - ElapsedTime);
    public float FinaleStartTime => finaleStartTime;
    public bool IsFinaleStarted { get; private set; }
    public bool IsBossPhase { get; private set; }
    public bool UsesSharedWaves => useSharedWaves;
    public int RemainingBosses => bosses.Count;
    public event Action StateChanged;

    private readonly HashSet<EnemyController> bosses = new HashSet<EnemyController>();
    private readonly HashSet<GameObject> initialObjects = new HashSet<GameObject>();
    private World[] worlds;
    private bool initialized;
    private bool transitioning;
    private bool bossStateDirty;
    private bool bossSpawned;
    private bool bossDeathRegistered;
    private bool fusionCompleted;
    private int fusionCompletedFrame;
    private FusionTransitionController finaleTransition;

    private void Awake()
    {
        if (worldManager != null && worldManager.RunController == null)
            worldManager.RunController = this;

        // Only shared waves claim spawners before their Start.
        if (useSharedWaves && spawners != null)
            foreach (EnemySpawner spawner in spawners)
                if (spawner != null && spawner.gameObject.scene == gameObject.scene)
                    spawner.ConfigureExternalStages();

        foreach (EnemySpawner spawner in FindObjectsByType<EnemySpawner>(FindObjectsInactive.Include))
            if (useSharedWaves && spawner.gameObject.scene == gameObject.scene && worldManager != null
                && World.GetFor(spawner) != null && World.GetFor(spawner).Manager == worldManager)
                spawner.ConfigureExternalStages();

        foreach (LevelManager level in FindObjectsByType<LevelManager>(FindObjectsInactive.Include))
            if (level.gameObject.scene == gameObject.scene)
                level.ConfigureStages(this);

        if (!ValidateConfiguration())
        {
            Debug.LogError("Invalid run stages: assign a shared controller, initialized WorldManager, exactly one spawner per world (two worlds), three valid waves/durations, and a Titan prefab with positive health/distance.", this);
            enabled = false;
            return;
        }

        worlds = new[] { World.GetFor(spawners[0]), World.GetFor(spawners[1]) };
        foreach (World world in worlds)
            foreach (Transform child in world.ContentRoot.GetComponentsInChildren<Transform>(true))
                initialObjects.Add(child.gameObject);
    }

    private bool ValidateConfiguration()
    {
        if (worldManager == null || !worldManager.IsInitialized || !worldManager.isActiveAndEnabled
            || worldManager.gameObject.scene != gameObject.scene || !worldManager.IsSharedObject(transform)
            || worldManager.RunController != this || !PositiveFinite(finaleStartTime)
            || spawners == null || spawners.Length != 2
            || (useSharedWaves && (stageDurations == null || stageDurations.Length != 3))
            || bossPrefab == null || !(bossPrefab is TitanEnemyController) || bossPrefab.gameObject.scene.IsValid()
            || !bossPrefab.gameObject.activeSelf || !bossPrefab.enabled || bossPrefab.RB == null
            || !PositiveFinite(bossHealth) || !PositiveFinite(bossDistance))
            return false;

        if (useSharedWaves)
            foreach (float duration in stageDurations)
                if (!PositiveFinite(duration))
                    return false;

        var configuredWorlds = new HashSet<World>();
        foreach (EnemySpawner spawner in spawners)
        {
            World world = World.GetFor(spawner);
            if (spawner == null || !spawner.enabled || !spawner.gameObject.activeSelf
                || world == null || world.Manager != worldManager || !world.IsConfigured
                || !world.ContainsContent(spawner.transform) || world.Player == null
                || !world.Player.gameObject.activeSelf || world.Player.GetComponent<PlayerHealth>() == null
                || !configuredWorlds.Add(world))
                return false;
            if (useSharedWaves)
                for (int i = 0; i < 3; i++)
                    if (!spawner.CanStartWave(i))
                        return false;
        }

        foreach (RunStageController controller in FindObjectsByType<RunStageController>(FindObjectsInactive.Include))
            if (controller != this && controller.enabled && controller.gameObject.scene == gameObject.scene)
                return false;
        foreach (World world in FindObjectsByType<World>(FindObjectsInactive.Include))
            if (world.Manager == worldManager && !configuredWorlds.Contains(world))
                return false;
        foreach (World world in configuredWorlds)
            foreach (EnemySpawner spawner in world.ContentRoot.GetComponentsInChildren<EnemySpawner>(true))
                if (Array.IndexOf(spawners, spawner) < 0)
                    return false;
        return true;
    }

    private static bool PositiveFinite(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void Start()
    {
        initialized = true;
        if (useSharedWaves)
            TryAdvanceStage();
        else
        {
            CurrentStageIndex = 0;
            IsRunning = true;
            StateChanged?.Invoke();
        }
    }

    private bool PlayerIsDead()
    {
        foreach (World world in worlds)
        {
            PlayerHealth health = world != null && world.Player != null
                ? world.Player.GetComponent<PlayerHealth>() : null;
            if (health == null || health.IsDead || (health.HasInitialized && health.currentHealth <= 0f))
                return true;
        }
        return false;
    }

    private bool UpgradeIsOpen => UIController.instance != null
        && UIController.instance.levelUpPanel != null && UIController.instance.levelUpPanel.activeSelf;

    private bool CanProgress()
    {
        if (!initialized || !isActiveAndEnabled || transitioning || IsCompleted || IsDefeated
            || worldManager == null || !worldManager.isActiveAndEnabled || !worldManager.IsInitialized
            || worldManager.IsSwitching || Time.timeScale <= 0f || PlayerIsDead())
            return false;
        if (UpgradeIsOpen)
            return false;
        foreach (World world in worlds)
        {
            if (world == null || world.Player == null || !world.Player.gameObject.activeSelf
                || !world.Player.GetComponent<PlayerHealth>().HasInitialized)
                return false;
        }
        return true;
    }

    private void Update()
    {
        if (IsCompleted || IsDefeated)
        {
            Time.timeScale = 0f;
            return;
        }
        if (initialized && PlayerIsDead())
        {
            FinishRun(false);
            return;
        }
        // A lethal hit may be inside poison, a projectile or a fire iteration.
        // Finalize here, never destroy that caller from the death event.
        if (IsRunning && IsBossPhase && bossSpawned && bossDeathRegistered && bosses.Count == 0)
        {
            FinishRun(true);
            return;
        }
        if (bossStateDirty)
        {
            bossStateDirty = false;
            StateChanged?.Invoke();
        }
        if (!initialized)
            return;
        if (IsRunning && Time.timeScale > 0f && !UpgradeIsOpen)
            ElapsedTime += Time.deltaTime;

        if (IsFinaleStarted)
        {
            if (!bossSpawned)
            {
                // Completed wins over a later disable; Cancel never counts as completion.
                if (fusionCompleted)
                {
                    if (Time.frameCount > fusionCompletedFrame && CanProgress())
                        SpawnFinalBoss();
                }
                else if (finaleTransition == null || !finaleTransition.isActiveAndEnabled
                    || !worldManager.IsFusionTransitioning)
                    FailFinale("Final fusion presentation was cancelled. Restart the run.");
            }
            else if (!bossDeathRegistered)
            {
                foreach (EnemyController boss in bosses)
                    if (boss == null)
                    {
                        FailFinale("Rift Lord was removed without a death event. Restart the run.");
                        break;
                    }
            }
            return;
        }

        if (IsRunning && ElapsedTime >= finaleStartTime)
        {
            TryStartFinale();
            return;
        }
        if (!CanProgress())
            return;
        if (!IsRunning)
        {
            TryAdvanceStage();
            return;
        }
        if (useSharedWaves && CurrentStageIndex < 2)
        {
            RemainingStageTime = Mathf.Max(0f, RemainingStageTime - Time.deltaTime);
            if (RemainingStageTime <= 0f)
                TryAdvanceStage();
        }
        else
            RemainingStageTime = RemainingUntilFinale;
    }

    private void LateUpdate()
    {
        // An upgrade UI must not resume a finished run; restart is the only exit.
        if (IsCompleted || IsDefeated)
        {
            if (UIController.instance != null && UIController.instance.levelUpPanel != null)
                UIController.instance.levelUpPanel.SetActive(false);
            Time.timeScale = 0f;
        }
    }

    public bool TryAdvanceStage()
    {
        return TryEnterStage(CurrentStageIndex + 1);
    }

    public bool TryEnterStage(int index)
    {
        if (index == 3)
            return TryStartFinale();
        if (IsFinaleStarted || !useSharedWaves || index < 0 || index > 2
            || index == CurrentStageIndex || !CanProgress())
            return false;
        // Validate before clearing anything, so a rejected request leaves the encounter intact.
        if (!ValidateConfiguration())
            return false;

        transitioning = true;
        try
        {
            ClearEncounter();
            CurrentStageIndex = index;
            RemainingStageTime = index < 2 ? stageDurations[index] : RemainingUntilFinale;
            IsRunning = true;
            foreach (EnemySpawner spawner in spawners)
                spawner.TryStartWave(index);
        }
        finally
        {
            transitioning = false;
        }
        StateChanged?.Invoke();
        return true;
    }

    public bool TryStartFinale()
    {
        if (IsFinaleStarted || !IsRunning || !CanProgress() || !ValidateConfiguration())
            return false;

        transitioning = true;
        try
        {
            finaleTransition = worldManager.FusionTransition;
            if (finaleTransition != null)
                finaleTransition.Completed += OnFusionCompleted;
            // BeginFinalFusion cancels the normal flip before committing fusion.
            if (!worldManager.BeginFinalFusion())
            {
                UnsubscribeTransition();
                return false;
            }

            IsFinaleStarted = true;
            CurrentStageIndex = 3;
            RemainingStageTime = 0f;
            // Prevent a sleeping world's late Start from restarting legacy waves.
            foreach (EnemySpawner spawner in spawners)
                spawner.ConfigureExternalStages();
            ClearEncounter();
        }
        finally
        {
            transitioning = false;
        }
        StateChanged?.Invoke();
        return true;
    }

    private void OnFusionCompleted()
    {
        if (fusionCompleted || bossSpawned || IsCompleted || IsDefeated)
            return;
        fusionCompleted = true;
        fusionCompletedFrame = Time.frameCount;
    }

    private void SpawnFinalBoss()
    {
        if (bossSpawned || !IsFinaleStarted || !fusionCompleted)
            return;
        World entryWorld = worldManager.CurrentWorld;
        if (entryWorld == null || entryWorld.ContentRoot == null || entryWorld.InteractionPlayer == null)
        {
            FailFinale("Final fusion has no boss spawn target. Restart the run.");
            return;
        }
        if (bossPrefab == null || entryWorld.Map == null || !entryWorld.Map.TryFindSafeSpawnPosition(
            bossPrefab.transform, entryWorld.ContentRoot, entryWorld.InteractionPlayer, bossDistance, out Vector3 position))
        {
            FailFinale("No safe Rift Lord spawn position within the bounded map search. Restart the run.");
            return;
        }
        bossSpawned = true;
        UnsubscribeTransition();
        EnemyController boss = Instantiate(bossPrefab, position, Quaternion.identity, entryWorld.ContentRoot);
        boss.health = bossHealth;
        if (boss.TryGetComponent(out RiftLordBarrage barrage))
            barrage.InitializeSpawnHealth(bossHealth);
        bosses.Add(boss);
        boss.Died += OnBossDied;
        IsBossPhase = true;
        StateChanged?.Invoke();
    }

    private void FailFinale(string message)
    {
        Debug.LogWarning(message, this);
        FinishRun(false);
    }

    private void UnsubscribeTransition()
    {
        if (finaleTransition != null)
            finaleTransition.Completed -= OnFusionCompleted;
        finaleTransition = null;
    }

    private void OnBossDied(EnemyController boss)
    {
        if (!IsRunning || !IsBossPhase || !bossSpawned || !bosses.Remove(boss))
            return;
        boss.Died -= OnBossDied;
        bossDeathRegistered = true;
        bossStateDirty = true;
    }

    private void ClearEncounter()
    {
        foreach (EnemyController boss in bosses)
            if (boss != null)
                boss.Died -= OnBossDied;
        bosses.Clear();
        bossStateDirty = false;
        foreach (EnemySpawner spawner in spawners)
            if (spawner != null)
                spawner.StopSpawning(true);

        var roots = new HashSet<GameObject>();
        foreach (World world in worlds)
        {
            if (world == null || world.ContentRoot == null)
                continue;
            foreach (MonoBehaviour component in world.ContentRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || World.GetFor(component) != world)
                    continue;
                bool enemy = component is EnemyController;
                bool attack = component is IWorldProjectile || component is HollowProjectile || component is TitanAttack
                    || component is ArrowController || component is BulletController
                    || component is DaggerProjectile || component is LanternProjController
                    || component is LanternFire || component is ScytheHitController;
                if (!enemy && !attack)
                    continue;
                // Production encounter prefabs are direct Content children. Never remove
                // hero-mounted weapons, configuration objects or static map hierarchies.
                Transform root = component.transform;
                while (root.parent != null && root.parent != world.ContentRoot)
                    root = root.parent;
                if (root.parent != world.ContentRoot || root.GetComponentInChildren<PlayerController>(true) != null
                    || (initialObjects.Contains(root.gameObject)
                        && (!enemy || root.GetComponent<EnemyController>() == null)))
                    continue;
                roots.Add(root.gameObject);
            }
        }

        // Snapshot roots before any Despawn or destruction mutates the hierarchy.
        foreach (GameObject root in roots)
        {
            if (root == null)
                continue;
            bool projectileRoot = false;
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || !(component is IWorldProjectile projectile))
                    continue;
                projectileRoot |= component.gameObject == root;
                projectile.Despawn();
            }
            if (root == null || projectileRoot)
                continue;
            root.SetActive(false);
            Destroy(root);
        }
    }

    private void FinishRun(bool completed)
    {
        UnsubscribeTransition();
        IsRunning = false;
        IsCompleted = completed;
        IsDefeated = !completed;
        RemainingStageTime = 0f;
        foreach (EnemySpawner spawner in spawners)
            if (spawner != null)
                spawner.ConfigureExternalStages();
        ClearEncounter();
        if (UIController.instance != null && UIController.instance.levelUpPanel != null)
            UIController.instance.levelUpPanel.SetActive(false);
        Time.timeScale = 0f;
        StateChanged?.Invoke();
    }

    public void RestartRun()
    {
        if (!isActiveAndEnabled)
            return;
        Scene scene = gameObject.scene;
        if (scene.buildIndex < 0)
        {
            Debug.LogError("Register the active run scene in Build Settings before restarting.", this);
            return;
        }
        LeaveRun(scene.path);
    }

    public void ReturnToMainMenu()
    {
        if (!isActiveAndEnabled)
            return;
        if (!Application.CanStreamedLevelBeLoaded("Main Menu"))
        {
            Debug.LogError("Register Main Menu in Build Settings before leaving a run.", this);
            return;
        }
        LeaveRun("Main Menu");
    }

    // restart and menu navigation share the same transition cleanup
    private void LeaveRun(string sceneName)
    {
        UnsubscribeTransition();
        // Release world resources before scene unload.
        if (worldManager != null)
            worldManager.enabled = false;
        // Loading completes on the next frame; the outgoing LateUpdate must not pause the new run.
        enabled = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }

    private void OnDestroy()
    {
        UnsubscribeTransition();
        if (worldManager != null && worldManager.RunController == this)
            worldManager.RunController = null;
        foreach (EnemyController boss in bosses)
            if (boss != null)
                boss.Died -= OnBossDied;
    }
}
