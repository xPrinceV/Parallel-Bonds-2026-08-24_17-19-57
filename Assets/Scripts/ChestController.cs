using UnityEngine;
using System.Collections.Generic;
public class ChestController : MonoBehaviour
{
    private Animator animator;
    private UIController UI;
    private CircleCollider2D chestCollider;


    void Awake()
    {
        animator = GetComponent<Animator>();
        UI = FindAnyObjectByType<UIController>();
        chestCollider = GetComponent<CircleCollider2D>();
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Player")
        {
            animator.SetTrigger("OpenChest");
            chestCollider.enabled = false;
            UI.OpenChestPanel();
            Destroy(gameObject, 5f);
        }       
    }

}
