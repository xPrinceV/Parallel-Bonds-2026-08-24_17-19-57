using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TitanEnemyController))]
public sealed class RiftLordBarrage : MonoBehaviour
{
    [SerializeField] private RiftLordProjectile projectilePrefab;
    [SerializeField] private SpriteRenderer bodyRenderer;
    [SerializeField] private float ringInterval = 6f;
    [SerializeField] private float telegraphDuration = 0.6f;
    [SerializeField] private float fanInterval = 0.8f;
    [SerializeField] private int ringDirections = 12;
    [SerializeField] private int ringGapDirections = 2;
    [SerializeField] private int fanProjectileCount = 5;
    [SerializeField] private int halfHealthRounds = 3;
    [SerializeField] private float fanSpread = 60f;
    [SerializeField] private float projectileSpeed = 3f;
    [SerializeField] private float projectileDamage = 10f;
    [SerializeField] private float projectileLifetime = 8f;
    [SerializeField] private int maxLiveProjectiles = 60;

    public bool HasTriggeredHalfHealth { get; private set; }
    public int ActiveProjectileCount => shots.Count;
    public bool IsTelegraphing => preview != null && preview.isActiveAndEnabled;

    private enum Phase { Idle, Ring, Fan, FanWait }
    private readonly List<RiftLordProjectile> shots = new List<RiftLordProjectile>(60);
    private readonly Vector2[] directions = new Vector2[32];
    private TitanEnemyController boss;
    private World world;
    private RunStageController run;
    private RiftLordProjectile preview;
    private SpriteRenderer bulletRenderer;
    private Phase phase;
    private Vector3 origin;
    private float radius;
    private float initialSpawnHealth;
    private float ringClock;
    private float phaseClock;
    private float lockedTelegraphDuration;
    private float lockedFanInterval;
    private int directionCount;
    private int lockedGapDirections;
    private int fanRoundsRemaining;
    private int gapStart;
    private bool started;
    private bool dead;
    private bool hadRun;

    internal bool CanKeepProjectiles => isActiveAndEnabled && boss != null
        && boss.isActiveAndEnabled && !dead && boss.health > 0f
        && (!hadRun || (run != null && run.isActiveAndEnabled && run.IsRunning
            && !run.IsCompleted && !run.IsDefeated))
        && (world == null || (world.IsActive
            && (world.Manager == null || world.Manager.isActiveAndEnabled)));

    private void Awake()
    {
        boss = GetComponent<TitanEnemyController>();
        initialSpawnHealth = boss.health;
        world = World.GetFor(this);
        ValidateParameters();
    }

    private void OnEnable()
    {
        boss.Died += OnBossDied;
        BindRun();
    }

    private void BindRun()
    {
        RunStageController next = world != null && world.Manager != null
            ? world.Manager.RunController : null;
        if (run == next)
            return;
        if (run != null)
            run.StateChanged -= OnRunStateChanged;
        run = next;
        if (run != null)
        {
            hadRun = true;
            run.StateChanged += OnRunStateChanged;
        }
    }

    // Call immediately after the spawn health override, before other attacks can damage the boss.
    public void InitializeSpawnHealth(float health)
    {
        if (started)
            throw new System.InvalidOperationException("Spawn health cannot change after the barrage starts.");
        if (!IsFinite(health) || health <= 0f)
            throw new System.ArgumentOutOfRangeException(nameof(health), "Spawn health must be positive and finite.");
        initialSpawnHealth = health;
    }

    private void Start()
    {
        BindRun();
        if (!IsFinite(initialSpawnHealth) || initialSpawnHealth <= 0f
            || projectilePrefab == null || !projectilePrefab.enabled
            || !projectilePrefab.gameObject.activeSelf
            || (world != null && world.ContentRoot == null))
        {
            Debug.LogError("RiftLordBarrage requires positive spawn health, an active projectile prefab and world Content.", this);
            enabled = false;
            return;
        }
        bulletRenderer = projectilePrefab.GetComponent<SpriteRenderer>();
        if (bulletRenderer == null || bulletRenderer.sprite == null)
        {
            Debug.LogError("RiftLordBarrage requires a projectile sprite for its telegraph.", this);
            enabled = false;
            return;
        }
        started = true;
    }

    private void Update()
    {
        if (!started)
            return;
        if (!CanKeepProjectiles)
        {
            ClearOwned();
            return;
        }
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        if (!HasTriggeredHalfHealth && boss.health <= initialSpawnHealth * 0.5f)
        {
            ValidateParameters();
            HasTriggeredHalfHealth = true;
            fanRoundsRemaining = halfHealthRounds;
        }

        // World.ClearProjectiles can cancel a warning independently of this component.
        if ((phase == Phase.Ring || phase == Phase.Fan) && !IsTelegraphing)
        {
            phase = Phase.Idle;
            ringClock = 0f;
        }
        if (phase == Phase.Ring || phase == Phase.Fan)
        {
            phaseClock -= dt;
            if (phaseClock <= 0f)
                Fire();
            return;
        }
        if (phase == Phase.FanWait)
        {
            phaseClock -= dt;
            if (phaseClock <= 0f)
                BeginTelegraph(true);
            return;
        }
        if (fanRoundsRemaining > 0)
        {
            BeginTelegraph(true);
            return;
        }
        ringClock += dt;
        // The first warning starts at 5.4s; the first ring fires no earlier than 6s.
        if (ringClock >= ringInterval - telegraphDuration)
            BeginTelegraph(false);
    }

    private PlayerHealth GetLivePlayer()
    {
        PlayerController player = world != null ? world.InteractionPlayer : null;
        PlayerHealth health = world != null
            ? (player != null ? player.GetComponent<PlayerHealth>() : null)
            : PlayerHealth.instance;
        return health != null && health.isActiveAndEnabled && !health.IsDead
            && World.CanInteract(this, health) ? health : null;
    }

    private void BeginTelegraph(bool fan)
    {
        PlayerHealth player = GetLivePlayer();
        if (player == null)
            return;
        ValidateParameters();
        origin = bodyRenderer != null ? bodyRenderer.bounds.center : transform.position;
        radius = bodyRenderer != null
            ? Mathf.Clamp(Mathf.Max(bodyRenderer.bounds.extents.x, bodyRenderer.bounds.extents.y) + 0.3f, 0.5f, 4f)
            : 0.8f;
        Vector2 aim = (Vector2)(player.transform.position - origin);
        float angle = aim.sqrMagnitude > 0.0001f ? Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg : 0f;
        // Snapshot the pattern once; previews and launches share these cached directions.
        directionCount = fan ? fanProjectileCount : ringDirections;
        lockedGapDirections = fan ? 0 : ringGapDirections;
        if (!fan)
            gapStart = Random.Range(0, directionCount);
        for (int i = 0; i < directionCount; i++)
        {
            float degrees = fan
                ? (directionCount == 1 ? angle : angle - fanSpread * 0.5f + i * fanSpread / (directionCount - 1))
                : i * (360f / directionCount);
            directions[i] = new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
        }
        phase = fan ? Phase.Fan : Phase.Ring;
        lockedTelegraphDuration = telegraphDuration;
        lockedFanInterval = fanInterval;
        phaseClock = lockedTelegraphDuration;
        CreatePreview();
    }

    private bool IsGap(int index) => phase == Phase.Ring
        && (index - gapStart + directionCount) % directionCount < lockedGapDirections;

    private void CreatePreview()
    {
        GameObject root = new GameObject("RiftLord Telegraph");
        root.transform.SetParent(World.GetContentRoot(this), false);
        root.transform.position = origin;
        preview = root.AddComponent<RiftLordProjectile>();
        preview.InitializePreview(this, lockedTelegraphDuration + 1f);
        for (int i = 0; i < directionCount; i++)
        {
            bool gap = IsGap(i);
            for (int step = 0; step < 3; step++)
            {
                GameObject marker = new GameObject(gap ? "Safe Gap" : "Shot Warning");
                marker.transform.SetParent(root.transform, false);
                marker.transform.position = origin + (Vector3)directions[i] * (radius + step * 0.8f);
                marker.transform.localScale = projectilePrefab.transform.localScale * 1.4f;
                SpriteRenderer sprite = marker.AddComponent<SpriteRenderer>();
                sprite.sprite = bulletRenderer.sprite;
                sprite.sharedMaterial = bulletRenderer.sharedMaterial;
                sprite.sortingLayerID = bulletRenderer.sortingLayerID;
                sprite.sortingOrder = Mathf.Max(bulletRenderer.sortingOrder,
                    bodyRenderer != null ? bodyRenderer.sortingOrder : 0) + 2;
                sprite.color = gap ? new Color(0.3f, 1f, 0.6f, 0.85f) : new Color(1f, 0.65f, 0.85f, 0.85f);
            }
        }
    }

    private void Fire()
    {
        bool fan = phase == Phase.Fan;
        ClearPreview();
        // A saturated owner skips the entire volley, never distorts the advertised pattern.
        int count = directionCount - lockedGapDirections;
        if (projectilePrefab != null && shots.Count + count <= maxLiveProjectiles)
        {
            for (int i = 0; i < directionCount; i++)
            {
                if (IsGap(i))
                    continue;
                RiftLordProjectile shot = Instantiate(projectilePrefab,
                    origin + (Vector3)directions[i] * radius, Quaternion.identity, World.GetContentRoot(this));
                shots.Add(shot);
                shot.Initialize(this, directions[i], projectileSpeed, projectileDamage, projectileLifetime);
            }
        }
        if (fan)
        {
            fanRoundsRemaining--;
            phase = fanRoundsRemaining > 0 ? Phase.FanWait : Phase.Idle;
            // Interval is measured between launches; each round has its own locked warning.
            phaseClock = lockedFanInterval - lockedTelegraphDuration;
        }
        else
            phase = Phase.Idle;
        ringClock = 0f;
    }

    internal void ReleaseProjectile(RiftLordProjectile projectile)
    {
        shots.Remove(projectile);
        if (preview == projectile)
            preview = null;
    }

    private void ClearPreview()
    {
        if (preview != null)
            preview.Despawn();
        preview = null;
    }

    private void ClearOwned()
    {
        ClearPreview();
        while (shots.Count > 0)
        {
            int last = shots.Count - 1;
            RiftLordProjectile shot = shots[last];
            shots.RemoveAt(last);
            if (shot != null)
                shot.Despawn();
        }
        phase = Phase.Idle;
        ringClock = 0f;
    }

    private void OnBossDied(EnemyController source)
    {
        dead = true;
        ClearOwned();
    }

    private void OnRunStateChanged()
    {
        if (!CanKeepProjectiles)
            ClearOwned();
    }

    private void OnDisable()
    {
        if (boss != null)
            boss.Died -= OnBossDied;
        if (run != null)
            run.StateChanged -= OnRunStateChanged;
        run = null;
        ClearOwned();
    }

    private void OnDestroy() => ClearOwned();
    private void OnValidate() => ValidateParameters();

    private void ValidateParameters()
    {
        telegraphDuration = Bounded(telegraphDuration, 0.6f, 0.2f, 2f);
        ringInterval = Bounded(ringInterval, 6f, telegraphDuration + 1f, 60f);
        fanInterval = Bounded(fanInterval, 0.8f, telegraphDuration + 0.05f, 5f);
        projectileSpeed = Bounded(projectileSpeed, 3f, 0.1f, 20f);
        projectileDamage = Bounded(projectileDamage, 10f, 0f, 100f);
        projectileLifetime = Bounded(projectileLifetime, 8f, 0.1f, 15f);
        maxLiveProjectiles = Mathf.Clamp(maxLiveProjectiles, 10, 60);
        ringDirections = Mathf.Clamp(ringDirections, 3, directions.Length);
        ringGapDirections = Mathf.Clamp(ringGapDirections,
            Mathf.Max(1, ringDirections - maxLiveProjectiles), ringDirections - 2);
        fanProjectileCount = Mathf.Clamp(fanProjectileCount, 1, Mathf.Min(directions.Length, maxLiveProjectiles));
        halfHealthRounds = Mathf.Clamp(halfHealthRounds, 1, 10);
        fanSpread = Bounded(fanSpread, 60f, 0f, 180f);
    }

    internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    internal static float Bounded(float value, float fallback, float min, float max)
        => Mathf.Clamp(IsFinite(value) ? value : fallback, min, max);
}
