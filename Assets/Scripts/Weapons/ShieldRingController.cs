using System.Collections.Generic;

using UnityEngine;

public class ShieldRingController : Weapon {

    public GameObject template;

    public float damage;
    public float amount; // This should really be a uint but the weapon system isn't set up in a way that allows this easily
    public float revolveSpeed;

    private List<ShieldController> items = new List<ShieldController>();

    void Start() {
        GameObject individual = Instantiate(template, transform.position + new Vector3(0F, 5F, 0F), transform.rotation);
        ShieldController aura = individual.GetComponent<ShieldController>();

        aura.damage = damage * stats.damage;
        aura.speed = revolveSpeed * stats.speed;
        aura.player = player;

        items.Add(aura);
    }

    void Update() {
        uint num = (uint) Mathf.Floor(amount * stats.amount);
        if (num != items.Count) {
            foreach (ShieldController individual in items) {
                Destroy(individual.gameObject);
                items.Remove(individual);
            }

            for (uint i = 0; i < num; i += 1) {
                float angle = i * Mathf.PI * 2 / num;
                float x = Mathf.Cos(angle) * 5F;
                float y = Mathf.Sin(angle) * 5F;

                GameObject individual = Instantiate(template, transform.position + new Vector3(x, y, 0F), Quaternion.identity);
                ShieldController shield = individual.GetComponent<ShieldController>();
                shield.damage = damage * stats.damage;
                shield.speed = revolveSpeed * stats.speed;
                shield.player = player;
                items.Add(shield);
            }
        } else {
            foreach (ShieldController individual in items) {
                float speed = revolveSpeed * stats.speed;
                if (individual.speed != speed) individual.speed = speed;
            }
        }
    }

    private void OnDisable() {
        foreach (ShieldController individual in items) {
            Destroy(individual.gameObject);
            items.Remove(individual);
        }
    }
}
