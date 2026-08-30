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
        if(isMoving)
        {
            transform.position = Vector3.MoveTowards(transform.position, player.transform.position, moveSpeed * Time.deltaTime);
        } else
        {
            checkCounter -= Time.deltaTime;
            if(checkCounter <= 0)
            {
                checkCounter = timeBetweenChecks;
                if(Vector3.Distance(transform.position, player.transform.position) < player.pickupRange)
                {
                    isMoving = true;
                    moveSpeed += player.moveSpeed;
                }
            }
        }
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Player")
        {
            ExperienceLevelController.instance.GetExp(expValue);
            Destroy(gameObject);
        }
    }
}
