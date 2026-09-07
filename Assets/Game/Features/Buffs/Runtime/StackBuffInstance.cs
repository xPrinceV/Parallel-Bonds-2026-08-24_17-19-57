using System;

// keep stack state per holder instead of changing the shared atom configuration
public sealed class StackBuffInstance
{
    private readonly StackToActivationBuffAtom atom;
    private bool isProcessing;

    public int CurrentStacks { get; private set; }
    public bool HasTriggered { get; private set; }
    public bool IsActive { get; private set; } = true;

    public StackBuffInstance(StackToActivationBuffAtom atom)
    {
        if (atom == null || !atom.isValid)
            throw new ArgumentException("A valid stack atom is required.", nameof(atom));
        this.atom = atom;
    }

    internal void ProcessEvent(StackEventContext context, IBuffReceiver receiver, BuffDefinition sourceBuff,
            BuffInstance sourceInstance = null)
    {
        if (!IsActive || isProcessing || receiver == null)
            return;

        // protect condition callbacks as well as activation callbacks from reentry
        isProcessing = true;
        try
        {
            if (!atom.isValid || (atom.ConsumeMode == StackConsumeMode.TriggerOnce && HasTriggered))
                return;

            if (!atom.Condition.Matches(context, receiver.Owner) || !IsActive)
                return;

            // failed grants keep accumulated stacks; saturate only at the integer limit
            if (CurrentStacks < int.MaxValue)
                CurrentStacks++;

            if (CurrentStacks < atom.RequiredStackCount)
                return;

            // at most one activation per event; remaining stacks carry to the next event
            var activationContext = new BuffActivationContext(receiver, sourceBuff, sourceInstance);
            if (!atom.Activation.TryActivate(activationContext) || !IsActive)
                return;

            HasTriggered = true;
            switch (atom.ConsumeMode)
            {
                case StackConsumeMode.ConsumeRequiredStacks:
                    CurrentStacks -= atom.RequiredStackCount;
                    break;
                case StackConsumeMode.ConsumeAllStacks:
                    CurrentStacks = 0;
                    break;
                case StackConsumeMode.TriggerOnce:
                    break;
            }
        }
        finally
        {
            isProcessing = false;
        }
    }

    internal void Remove()
    {
        IsActive = false;
    }
}
