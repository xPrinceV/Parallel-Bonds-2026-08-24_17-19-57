using System;
using UnityEngine;

// describe one confirmed hit without searching the scene or applying damage
public readonly struct StackEventContext
{
    public GameObject Source { get; }
    public GameObject Target { get; }
    public float DamageDealt { get; }

    public StackEventContext(GameObject source, GameObject target, float damageDealt)
    {
        Source = source;
        Target = target;
        DamageDealt = damageDealt;
    }
}

// separate granting a buff from the atom that requests it
public interface IBuffReceiver
{
    GameObject Owner { get; }
    bool TryAddBuff(BuffDefinition definition);
}

public readonly struct BuffActivationContext
{
    public IBuffReceiver Receiver { get; }
    public BuffDefinition SourceBuff { get; }

    public BuffActivationContext(IBuffReceiver receiver, BuffDefinition sourceBuff)
    {
        Receiver = receiver;
        SourceBuff = sourceBuff;
    }
}

[Serializable]
public abstract class StackCondition
{
    public abstract bool isValid { get; }
    public abstract bool Matches(StackEventContext context, GameObject owner);
}

// count positive damage dealt by the holder, not incoming or self-inflicted hits
[Serializable]
public sealed class HitStackCondition : StackCondition
{
    public override bool isValid => true;

    public override bool Matches(StackEventContext context, GameObject owner)
    {
        return owner != null && context.Source == owner && context.Target != null
            && context.Target != owner && context.DamageDealt > 0f
            && !float.IsNaN(context.DamageDealt) && !float.IsInfinity(context.DamageDealt);
    }
}

[Serializable]
public abstract class StackActivation
{
    public abstract bool isValid { get; }
    public abstract bool TryActivate(BuffActivationContext context);
}

[Serializable]
public sealed class GrantBuffActivation : StackActivation
{
    [SerializeField]
    private BuffDefinition targetBuff;

    // do not recursively validate linked recipes; the receiver validates on grant
    public override bool isValid => targetBuff != null;

    public override bool TryActivate(BuffActivationContext context)
    {
        return isValid && context.Receiver != null
            && context.Receiver.TryAddBuff(targetBuff);
    }
}
