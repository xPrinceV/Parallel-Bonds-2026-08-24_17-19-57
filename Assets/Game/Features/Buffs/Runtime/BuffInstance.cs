using System;
using System.Collections.Generic;

// store lifetime and generated modifiers separately from the shared recipe
public sealed class BuffInstance
{
    private readonly List<StatModifier> modifiers = new List<StatModifier>();
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
                modifiers.AddRange(atom.CreateModifiers());
        }

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
            stack.ProcessEvent(context, receiver, Definition);
        }
    }

    // removal stops both stat contribution and event processing
    public void Remove()
    {
        IsActive = false;
        foreach (StackBuffInstance stack in stacks)
            stack.Remove();
    }
}
