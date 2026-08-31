using UnityEngine;
using TMPro;

public class DamageNumber : MonoBehaviour
{
    public TMP_Text damageText;
    public float lifetime;
    private float lifeCounter;
    public float floatSpeed = 1f;


    // Update is called once per frame
    void Update()
    {
        if(lifeCounter > 0)
        {
            //Count down the damage number's lifetime
            lifeCounter -= Time.deltaTime;

            //When the lifetime expires, place the damage number back into the pool
            if(lifeCounter <= 0)
            {
                DamageNumberController.instance.PlaceInPool(this);
            }
        }

        //Make the damage number float upwards over time
        transform.position += Vector3.up * floatSpeed * Time.deltaTime;
    }

    public void Setup(int damageDisplay)
    {
        //Reset the life counter
        lifeCounter = lifetime;

        //Change the text to display the damage amount
        damageText.text = damageDisplay.ToString();
    }
}
