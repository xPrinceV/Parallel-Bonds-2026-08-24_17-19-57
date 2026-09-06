using System;
using UnityEngine;

[Serializable]
public class StackToActivationBuffAtom : BuffAtom
{
    // store the threshold only; each runtime instance owns its current count
    [SerializeField, Min(1)]
    private int stackCount = 1;

    [SerializeReference]
    private StackCondition condition = new HitStackCondition();

    [SerializeReference]
    private StackActivation activation = new GrantBuffActivation();

    [SerializeField]
    private StackConsumeMode consumeMode = StackConsumeMode.ConsumeRequiredStacks;

    public int RequiredStackCount => stackCount;
    public StackCondition Condition => condition;
    public StackActivation Activation => activation;
    public StackConsumeMode ConsumeMode => consumeMode;



    public override bool isValid => 
        stackCount > 0 && condition != null && activation != null && condition.isValid && activation.isValid
        && Enum.IsDefined(typeof(StackConsumeMode), consumeMode);

    public override StatModifier[] CreateModifiers()
    {
        if (!isValid){
            throw new InvalidOperationException("StackToActivationBuffAtom is not valid");
        }
        // stack instances process events separately from stat calculation
        return Array.Empty<StatModifier>();
    }
}


public enum StackConsumeMode
{
    ConsumeRequiredStacks = 0,
    ConsumeAllStacks = 1,
    TriggerOnce = 2,
} 