using System;
using UnityEngine;

public class ScytheController : Weapon {
    [SerializeField] private float attackSpeed;
    [SerializeField] private float attackDamage;
    [SerializeField] private float area;
    [SerializeField] private GameObject scythe;
    private float attackCounter;
    public AudioManager audioManager;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start() {
        attackCounter = 0f;
        audioManager = AudioManager.instance;
    }

    // Update is called once per frame
    void Update() {
        attackCounter -= Time.deltaTime;
        if (attackCounter <= 0) {
            Vector2 facingDirection = player.facingDirection;
            GameObject newScythe = Instantiate(scythe, transform.position, Quaternion.identity);
            ScytheHitController handle = newScythe.GetComponent<ScytheHitController>();

            handle.damage = attackDamage * stats.damage;
            handle.area = area * stats.area;
            handle.player = player;
            handle.SetDirection(facingDirection);
            audioManager.PlaySFXPitch(audioManager.scythe, 0.2f);

            attackCounter = 1f / (attackSpeed * stats.attackSpeed);
        }
    }
}
