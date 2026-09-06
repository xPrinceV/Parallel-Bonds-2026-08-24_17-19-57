using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum UpgradeType
{
    Damage,
    Speed,
    Range,
    AttackSpeed,
    Amount,
    Duration,
    Bounces,
    Area
}
public class Weapon : MonoBehaviour
{
    public PlayerController player;

    public WeaponStats stats;
    public int weaponLevel;

    public Sprite icon;
    public string weaponName;
    public UpgradeType[] availableUpgrades;
}

//Weapons that are created should inherit WeaponStats
[System.Serializable]
public class WeaponStats
{
    public float speed = 1f;
    public float damage = 1f;
    public float range = 1f;
    public float attackSpeed = 1f;
    public float amount = 1f;
    public float duration = 1f;   
    public float bounces = 1f; 
    public float area = 1f;

    public float[] damageUpgrades;
    public float[] speedUpgrades;
    public float[] rangeUpgrades;
    public float[] attackSpeedUpgrades;
    public float[] amountUpgrades;
    public float[] durationUpgrades;
    public float[] bouncesUpgrades;
    public float[] areaUpgrades;
    public string UpgradeText;
}
