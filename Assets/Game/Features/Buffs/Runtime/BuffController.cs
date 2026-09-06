using System;
using System.Collections.Generic;
using UnityEngine;

// attach one controller to the holder whose buffs and outgoing hits it manages
public class BuffController : MonoBehaviour, IBuffReceiver
{
    [SerializeField]
    private BuffDefinition[] initialBuffs = new BuffDefinition[0];

    private readonly List<BuffInstance> instances = new List<BuffInstance>();
    private bool isDispatching;

    public GameObject Owner => gameObject;

    private void Start()
    {
        if (initialBuffs == null)
            return;
        foreach (BuffDefinition definition in initialBuffs)
            if (!TryAddBuff(definition))
                Debug.LogError("Unable to grant an initial buff. Check its atom configuration.", this);
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

        // construct the complete instance before publishing any of its effects
        instances.Add(new BuffInstance(definition, this));
        return true;
    }

    public BuffInstance FindBuff(BuffDefinition definition)
    {
        foreach (BuffInstance instance in instances)
            if (instance.IsActive && instance.Definition == definition)
                return instance;
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
            instances[i].Tick(deltaTime);
            if (!instances[i].IsActive)
                instances.RemoveAt(i);
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

    // consumers read current modifiers rather than applying and reversing raw values
    public StatModifier[] GetModifiers()
    {
        var result = new List<StatModifier>();
        if (isActiveAndEnabled)
            foreach (BuffInstance instance in instances)
                if (instance.IsActive)
                    result.AddRange(instance.Modifiers);
        return result.ToArray();
    }

    private void OnDisable()
    {
        // this first version ends buffs when their holder is disabled
        foreach (BuffInstance instance in instances)
            instance.Remove();
        instances.Clear();
    }
}
