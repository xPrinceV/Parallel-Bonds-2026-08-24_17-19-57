using System;
using System.Collections.Generic;
using UnityEngine;

// attach one controller to the holder whose buffs and outgoing hits it manages
public class BuffController : MonoBehaviour, IBuffReceiver
{
    [SerializeField]
    private BuffDefinition[] initialBuffs = new BuffDefinition[0];

    private readonly List<BuffInstance> instances = new List<BuffInstance>();
    private readonly Dictionary<BuffInstance, BuffHandle> grants = new Dictionary<BuffInstance, BuffHandle>();
    private bool isDispatching;

    public GameObject Owner => gameObject;

    private void Start()
    {
        if (initialBuffs == null)
            return;

        foreach (BuffDefinition definition in initialBuffs)
        {
            if (!TryAddBuff(definition))
                Debug.LogError("Unable to grant an initial buff. Check its atom configuration.", this);
        }
    }

    private void Update()
    {
        // scaled time keeps buff lifetime paused during upgrade selection
        Tick(Time.deltaTime);
    }

    public bool TryAddBuff(BuffDefinition definition)
    {
        if (!isActiveAndEnabled || definition == null || !definition.isValid)
            return false;

        BuffInstance existing = FindBuff(definition);
        if (existing != null)
        {
            existing.RefreshDuration();
            return true;
        }

        return TryCreateInstance(definition) != null;
    }

    // source identity belongs to the equipment instance, not its shared item definition
    public BuffHandle GrantBuff(BuffDefinition definition, object source)
    {
        if (!isActiveAndEnabled || definition == null || !definition.isValid
            || source == null || (source is UnityEngine.Object unitySource && unitySource == null))
            return null;

        foreach (BuffHandle grant in grants.Values)
        {
            if (grant.IsActive && grant.Definition == definition && ReferenceEquals(grant.Source, source))
            {
                grant.Instance.RefreshDuration();
                return grant;
            }
        }

        BuffInstance instance = TryCreateInstance(definition);
        if (instance == null)
            return null;

        var handle = new BuffHandle(instance, source);
        grants.Add(instance, handle);
        return handle;
    }

    // a handle can revoke only the grant issued by this controller
    public bool RevokeBuff(BuffHandle handle)
    {
        if (handle == null || !grants.TryGetValue(handle.Instance, out BuffHandle existing)
            || !ReferenceEquals(existing, handle))
            return false;

        bool wasActive = handle.IsActive;
        handle.Instance.Remove();
        instances.Remove(handle.Instance);
        grants.Remove(handle.Instance);
        return wasActive;
    }

    private BuffInstance TryCreateInstance(BuffDefinition definition)
    {
        // construct the complete instance before publishing any of its effects
        BuffInstance instance;
        try
        {
            instance = new BuffInstance(definition, this);
        }
        catch (ArgumentException)
        {
            // reject invalid combined configuration without publishing an instance
            return null;
        }

        instances.Add(instance);
        return instance;
    }

    // definition-based calls manage legacy buffs only; owned grants require their handles
    public BuffInstance FindBuff(BuffDefinition definition)
    {
        foreach (BuffInstance instance in instances)
        {
            if (instance.IsActive && instance.Definition == definition && !grants.ContainsKey(instance))
                return instance;
        }

        return null;
    }

    public bool RemoveBuff(BuffDefinition definition)
    {
        BuffInstance instance = FindBuff(definition);
        if (instance == null)
            return false;

        instance.Remove();
        instances.Remove(instance);
        return true;
    }

    public void Tick(float deltaTime)
    {
        if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (!isActiveAndEnabled)
            return;

        for (int i = instances.Count - 1; i >= 0; i--)
        {
            BuffInstance instance = instances[i];
            instance.Tick(deltaTime);
            if (!instance.IsActive)
            {
                grants.Remove(instance);
                instances.RemoveAt(i);
            }
        }
    }

    // call exactly once per confirmed outgoing hit, before destroying the target
    public void ReportHit(GameObject target, float damageDealt)
    {
        ReportEvent(new StackEventContext(Owner, target, damageDealt));
    }

    public void ReportEvent(StackEventContext context)
    {
        if (!isActiveAndEnabled || isDispatching)
            return;

        isDispatching = true;
        try
        {
            // newly granted buffs cannot consume the event that created them
            BuffInstance[] snapshot = instances.ToArray();
            foreach (BuffInstance instance in snapshot)
                instance.ProcessEvent(context);
        }
        finally
        {
            isDispatching = false;
        }
    }

    // callers provide damage before buffs; the calculator still owns the formula
    public float CalculateWeaponDamage(float weaponDamage)
    {
        return DamageCalculator.CalculateDamage(weaponDamage, GetModifiers());
    }

    public int CalculateProjectileCount(int baseCount)
    {
        if (baseCount < 0)
            throw new ArgumentOutOfRangeException(nameof(baseCount));

        double prefix = 0d;
        double multiplier = 1d;
        double postfix = 0d;
        foreach (StatModifier modifier in GetModifiers())
        {
            if (modifier.Target != ModifierTarget.Weapon || modifier.Stat != WeaponStatId.ProjectileCount)
                continue;
            if (float.IsNaN(modifier.Value) || float.IsInfinity(modifier.Value))
                throw new ArgumentOutOfRangeException(nameof(modifier), "Projectile count modifiers must be finite.");

            switch (modifier.Type)
            {
                case ModifierType.Prefix:
                    prefix += modifier.Value;
                    break;
                case ModifierType.Multiplier:
                    multiplier *= modifier.Value;
                    break;
                case ModifierType.Postfix:
                    postfix += modifier.Value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(modifier), "Unknown projectile count modifier stage.");
            }
            if (double.IsNaN(prefix) || double.IsInfinity(prefix)
                || double.IsNaN(multiplier) || double.IsInfinity(multiplier)
                || double.IsNaN(postfix) || double.IsInfinity(postfix))
                throw new OverflowException("Projectile count calculation must remain finite.");
        }

        double result = (baseCount + prefix) * multiplier + postfix;
        if (double.IsNaN(result) || double.IsInfinity(result))
            throw new OverflowException("Projectile count calculation must remain finite.");
        result = Math.Max(0d, Math.Floor(result));
        if (result > int.MaxValue)
            throw new OverflowException("Projectile count exceeds Int32.MaxValue.");
        return (int)result;
    }

    // consumers read current modifiers rather than applying and reversing raw values
    public StatModifier[] GetModifiers()
    {
        var result = new List<StatModifier>();
        if (!isActiveAndEnabled)
            return result.ToArray();

        foreach (BuffInstance instance in instances)
        {
            if (instance.IsActive)
                result.AddRange(instance.Modifiers);
        }

        return result.ToArray();
    }

    private void OnDisable()
    {
        // this first version ends buffs when their holder is disabled
        foreach (BuffInstance instance in instances)
            instance.Remove();
        instances.Clear();
        grants.Clear();
    }
}
