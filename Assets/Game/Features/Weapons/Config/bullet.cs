using System.Collections.Generic;
using UnityEngine;

public class bullet : MonoBehaviour
{
    private float speed = 10f;
    private float damageMultiplier = 1f;
    public bool isHoming = false;
    private float homingStrength = 0f;

    public ParticleSystem bulletParticleSystem;

    private Dictionary <int, bool> allowedBuffs = new Dictionary<int, bool>();

    
}