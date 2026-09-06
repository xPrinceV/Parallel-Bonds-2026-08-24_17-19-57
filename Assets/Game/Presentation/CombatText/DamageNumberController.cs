using UnityEngine;
using System.Collections;
using System.Collections.Generic;
public class DamageNumberController : MonoBehaviour
{
    //This is to allow other scripts to easily access the DamageNumberController
    public static DamageNumberController instance;
    public DamageNumber numberToSpawn;
    public Transform numberCanvas;
    private List<DamageNumber> numberPool = new List<DamageNumber>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        instance = this;
    }
    
    public void SpawnDamage(float damageAmount, Vector3 location)
    {
        //Round damage, display whole number
        int rounded = Mathf.RoundToInt(damageAmount);

        //Get an unused damage number from the pool, or create a new one
        DamageNumber newDamage = GetFromPool();

        //Activate damage number and place it at the location where the damage occurred 
        newDamage.Setup(rounded);
        newDamage.gameObject.SetActive(true);
        newDamage.transform.position = location;
    }

    public DamageNumber GetFromPool()
    {
        DamageNumber numberToOutput = null;

        //If there are no unused damage numbers, create a new one
        if(numberPool.Count == 0)
        {
            //Instantiate and parent it to the numberCanvas
            numberToOutput = Instantiate(numberToSpawn, numberCanvas);
        }
        else
        {
            //Reuse the first available damage number from the pool
            numberToOutput = numberPool[0];
            numberPool.RemoveAt(0);
        }

        return numberToOutput;
    }

    public void PlaceInPool(DamageNumber numberToPlace)
    {
        numberToPlace.gameObject.SetActive(false);
        numberPool.Add(numberToPlace);
    }
}
