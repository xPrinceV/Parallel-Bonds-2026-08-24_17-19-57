using UnityEngine;

public class StateSwitchController : MonoBehaviour
{
    public bool isActive = true;
    public GameObject char1;
    public GameObject char2;
    private float timer = 15f;
    private float timerCounter;
    void Start()
    {
        timerCounter = timer;
        char1.SetActive(isActive);
        char2.SetActive(!isActive);
    }

    void Update()
    {
        timerCounter -= Time.deltaTime;
        if(timerCounter <= 0)
        {
            isActive = !isActive;
            char1.SetActive(isActive);
            char2.SetActive(!isActive);
            timerCounter = timer;
        }
    }
}
