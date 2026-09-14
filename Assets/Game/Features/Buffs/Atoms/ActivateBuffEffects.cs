using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ActivateBuffEffects : StackActivation
{
    [SerializeReference]
    private List<BuffAtom> effects = new List<BuffAtom>();

    internal IReadOnlyList<BuffAtom> Effects => effects;

    public override bool isValid
    {
        get
        {
            if (effects == null || effects.Count == 0)
                return false;

            foreach (BuffAtom effect in effects)
            {
                // only validate numeric atoms; never recurse into stack recipes
                if (!(effect is StatBuffAtom) || !effect.isValid)
                    return false;
            }

            return true;
        }
    }

    public override bool TryActivate(BuffActivationContext context)
    {
        return isValid && context.SourceInstance != null
            && context.SourceInstance.TryActivateEffects(this, context);
    }
}
