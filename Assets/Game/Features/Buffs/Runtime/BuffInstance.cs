using System;
using System.Collections.Generic;

// store lifetime and generated modifiers separately from the shared recipe
public sealed class BuffInstance
{
    private readonly List<StatModifier> modifiers = new List<StatModifier>();
    private readonly List<StatModifier> contributions = new List<StatModifier>();
    private readonly HashSet<ActivateBuffEffects> activatedEffects = new HashSet<ActivateBuffEffects>();
    private readonly List<StackBuffInstance> stacks = new List<StackBuffInstance>();
    private readonly IBuffReceiver receiver;
    private readonly bool isPermanent;
    private readonly float duration;

    public BuffDefinition Definition { get; }
    public float RemainingDuration { get; private set; }
    public bool IsActive { get; private set; } = true;
    public IReadOnlyList<StatModifier> Modifiers { get; }
    public IReadOnlyList<StackBuffInstance> StackInstances { get; }

    public BuffInstance(BuffDefinition definition, IBuffReceiver receiver)
    {
        if (definition == null || !definition.isValid)
            throw new ArgumentException("A valid buff definition is required.", nameof(definition));
        if (receiver == null)
            throw new ArgumentNullException(nameof(receiver));

        Definition = definition;
        this.receiver = receiver;
        duration = definition.Duration;
        isPermanent = definition.IsPermanent;
        RemainingDuration = duration;

        // stack atoms are runtime triggers, not empty stat modifier sources
        for (int i = 0; i < definition.AtomCount; i++)
        {
            BuffAtom atom = definition.GetAtom(i);
            if (atom is StackToActivationBuffAtom stackAtom)
                stacks.Add(new StackBuffInstance(stackAtom));
            else
                contributions.AddRange(atom.CreateModifiers());
        }

        if (!TryResolveModifiers(contributions, out List<StatModifier> resolved))
            throw new ArgumentException("Combined damage multiplier must be finite.", nameof(definition));
        modifiers.AddRange(resolved);
        Modifiers = modifiers.AsReadOnly();
        StackInstances = stacks.AsReadOnly();
    }

    // refreshing a buff keeps stack progress and does not duplicate its modifiers
    public void RefreshDuration()
    {
        if (IsActive)
            RemainingDuration = duration;
    }

    public void Tick(float deltaTime)
    {
        if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (!IsActive || isPermanent)
            return;

        RemainingDuration = Math.Max(0f, RemainingDuration - deltaTime);
        if (RemainingDuration <= 0f)
            Remove();
    }

    public void ProcessEvent(StackEventContext context)
    {
        foreach (StackBuffInstance stack in stacks)
        {
            if (!IsActive)
                break;
            stack.ProcessEvent(context, receiver, Definition, this);
        }
    }

    internal bool TryActivateEffects(ActivateBuffEffects activation, BuffActivationContext context)
    {
        if (!IsActive || !ReferenceEquals(context.SourceInstance, this)
            || !ReferenceEquals(context.Receiver, receiver) || context.SourceBuff != Definition
            || activation == null || !activation.isValid || activatedEffects.Contains(activation))
            return false;

        // resolve the entire candidate before publishing; failed actions contribute nothing
        var candidate = new List<StatModifier>(contributions);
        foreach (BuffAtom effect in activation.Effects)
            candidate.AddRange(effect.CreateModifiers());
        if (!TryResolveModifiers(candidate, out List<StatModifier> resolved))
            return false;

        contributions.Clear();
        contributions.AddRange(candidate);
        modifiers.Clear();
        modifiers.AddRange(resolved);
        activatedEffects.Add(activation);
        return true;
    }

    private static bool TryResolveModifiers(List<StatModifier> source, out List<StatModifier> resolved)
    {
        resolved = new List<StatModifier>();
        double damageBonus = 0d;
        int damageIndex = -1;
        foreach (StatModifier modifier in source)
        {
            if (modifier.Target == ModifierTarget.Weapon && modifier.Stat == WeaponStatId.Damage
                && modifier.Type == ModifierType.Multiplier)
            {
                if (damageIndex < 0)
                {
                    damageIndex = resolved.Count;
                    resolved.Add(modifier);
                }
                // fields remain actual multipliers; only this instance combines their bonuses
                damageBonus += (double)modifier.Value - 1d;
            }
            else
                resolved.Add(modifier);
        }

        if (damageIndex >= 0)
        {
            float multiplier = (float)(1d + damageBonus);
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
                return false;
            resolved[damageIndex] = new StatModifier(WeaponStatId.Damage, ModifierType.Multiplier, multiplier);
        }
        return true;
    }

    // removal stops both stat contribution and event processing
    public void Remove()
    {
        IsActive = false;
        contributions.Clear();
        modifiers.Clear();
        activatedEffects.Clear();
        foreach (StackBuffInstance stack in stacks)
            stack.Remove();
    }
}
