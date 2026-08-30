using UnityEngine;
using System.Collections;
using System.Collections.Generic;


public class EnemySpawner : MonoBehaviour
{
    public float spawnCounter;

    public Transform minSpawn, maxSpawn;
    private Transform target;

    private float despawnDistance;
    private List<GameObject> spawnedEnemies = new List<GameObject>();
    public int checkPerFrame;
    private int enemyToCheck;

    public List<WaveInfo> waves;
    private int currentWave;
    private float waveCounter;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // spawnCounter = spawnTime;
        target = PlayerHealth.instance.transform;
        despawnDistance = Vector3.Distance(transform.position, maxSpawn.position) + 5f;
        currentWave = -1;
        GoToNextWave();
    }

    // Update is called once per frame
    void Update()
    {

        if(PlayerHealth.instance.gameObject.activeSelf)
        {
            if(currentWave < waves.Count)
            {
                waveCounter -= Time.deltaTime;
                if(waveCounter <= 0)
                {
                    GoToNextWave();
                }

                spawnCounter -= Time.deltaTime;
                if(spawnCounter <= 0)
                {
                    //Set the spawn counter based on value stored in waves
                    spawnCounter = waves[currentWave].timeBetweenSpawns;
                    List<GameObject> enemies = waves[currentWave].enemiesToSpawn;
                    GameObject enemyToSpawn = enemies[Random.Range(0, enemies.Count)];
                    GameObject newEnemy  = Instantiate(enemyToSpawn, SelectSpawnPoint(), Quaternion.identity);
                    spawnedEnemies.Add(newEnemy);
                }
            }
        }



        //Make the spawner follow the player
        transform.position = target.position;

        //Enemy to check is the position, check per frame is the amount of enemies checked per frame
        int checkTarget = enemyToCheck + checkPerFrame;
        while (enemyToCheck < checkTarget)
        {
            //Ensure there is an enemy at this position of the list
            if (enemyToCheck < spawnedEnemies.Count)
            {
                //Check if it isn't empty
                if (spawnedEnemies[enemyToCheck] != null)
                {
                    //If the particular enemy is further than the set despawned distance
                    if(Vector3.Distance(transform.position, spawnedEnemies[enemyToCheck].transform.position) > despawnDistance)
                    {
                        //Destroy the enemy game object, then remove it from the list.
                        Destroy(spawnedEnemies[enemyToCheck]);
                        spawnedEnemies.RemoveAt(enemyToCheck);
                        checkTarget--;
                    }
                    else
                    {
                        //Enemy has been checked and not further than despawn dist, so move on to next enemy in list
                        enemyToCheck++;
                    }
                }
                else
                {
                    //Remove empty enemy from list
                    spawnedEnemies.RemoveAt(enemyToCheck);
                    checkTarget--;
                }
            }
            else
            {
                //Reset
                enemyToCheck = 0;
                checkTarget = 0;
            }
        }
    }

    public void GoToNextWave()
    {
        currentWave++;
        if(currentWave >= waves.Count)
        {
            currentWave = waves.Count - 1;
        }

        waveCounter = waves[currentWave].waveLength;
        spawnCounter = waves[currentWave].timeBetweenSpawns;
    }
    public Vector3 SelectSpawnPoint()
    {
        Vector3 spawnPoint = Vector3.zero;

        //Choose between spawning on top or bottom
        if (Random.Range(0f, 1f) > 0.5)
        {
            spawnPoint.y = Random.Range(minSpawn.position.y, maxSpawn.position.y);
            //Choose to spawn on left side or right side
            if (Random.Range(0f, 1f) > 0.5)
            {
                spawnPoint.x = maxSpawn.position.x;
            }
            else
            {
                spawnPoint.x = minSpawn.position.x;
            }
        }
        else
        {
            spawnPoint.x = Random.Range(minSpawn.position.x, maxSpawn.position.x);
            //Choose to spawn on left side or right side
            if (Random.Range(0f, 1f) > 0.5)
            {
                spawnPoint.y = maxSpawn.position.y;
            }
            else
            {
                spawnPoint.y = minSpawn.position.y;
            }
        }

        return spawnPoint;
    }
}

[System.Serializable]
public class WaveInfo
{
    public List<GameObject> enemiesToSpawn;
    public float waveLength;
    public float timeBetweenSpawns;
}
