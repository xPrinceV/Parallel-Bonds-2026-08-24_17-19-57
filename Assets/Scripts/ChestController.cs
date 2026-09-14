using UnityEngine;
using System.Collections.Generic;
public class ChestController : MonoBehaviour
{
    private Animator animator;
    private UIController UI;


    void Awake()
    {
        animator = GetComponent<Animator>();
        UI = FindAnyObjectByType<UIController>();
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Player")
        {
            animator.SetTrigger("OpenChest");
            UI.OpenChestPanel();
        }       
    }

}
