using UnityEngine;

public class ExpPickup : MonoBehaviour
{
    public int expValue;
    private bool isMoving;
    public float moveSpeed;
    public float timeBetweenChecks = .2f;
    private float checkCounter;

    private PlayerController player;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        player = PlayerHealth.instance.GetComponent<PlayerController>();
    }

    // Update is called once per frame
    void Update()
    {
        //Check if isMoving is true, if it is, make the orb move towards the player
        if(isMoving)
        {
            transform.position = Vector3.MoveTowards(transform.position, player.transform.position, moveSpeed * Time.deltaTime);
        } 
        else
        {
            //Countdown the checkCounter
            checkCounter -= Time.deltaTime;

            if(checkCounter <= 0)
            {
                //Reset counter
                checkCounter = timeBetweenChecks;

                //If the distance between the exp orb and the player is within the player's pickup range
                if(Vector3.Distance(transform.position, player.transform.position) < player.pickupRange)
                {
                    //Set isMoving to true
                    isMoving = true;

                    //Add the player's moveSpeed to exp orb, this is incase the player runs fast and ends up outrunning the orb
                    moveSpeed += player.moveSpeed;
                }
            }
        }
    }


    void OnTriggerEnter2D(Collider2D collision)
    {
        //When the orb collides with the player
        if(collision.tag == "Player")
        {
            //Call GetExp and destroy the orb
            ExperienceLevelController.instance.GetExp(expValue);
            Destroy(gameObject);
        }
    }
}
