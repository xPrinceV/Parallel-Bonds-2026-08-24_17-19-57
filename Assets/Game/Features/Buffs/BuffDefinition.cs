using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Buff", menuName = "Parallel Bonds/Buffs/Buff")]
public class BuffDefinition : ScriptableObject
{
    [SerializeField]
    private BuffType type;
    [SerializeField, Min(0f)]
    private float duration = 10f;
    [SerializeField]
    private bool isPermanent;

    // store each atom and its parameters inside this buff recipe
    [SerializeReference]
    private List<BuffAtom> atoms = new List<BuffAtom>();

    public BuffType Type => type;
    public float Duration => duration;
    public bool IsPermanent => isPermanent;
    public int AtomCount => atoms == null ? 0 : atoms.Count;

    // timed buffs require a positive duration; permanent buffs use an explicit flag
    public bool isValid
    {
        get
        {
            if (!System.Enum.IsDefined(typeof(BuffType), type)
                || float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f
                || (!isPermanent && duration <= 0f) || atoms == null || atoms.Count == 0)
                return false;

            foreach (BuffAtom atom in atoms)
                if (atom == null || !atom.isValid)
                    return false;

            return true;
        }
    }

    public BuffAtom GetAtom(int index)
    {
        return atoms[index];
    }
}
