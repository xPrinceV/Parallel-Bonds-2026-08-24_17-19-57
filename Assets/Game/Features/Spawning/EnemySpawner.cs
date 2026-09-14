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
    private int currentWave = -1;
    private bool externalStages;
    private bool isSpawning;
    public bool UsesExternalStages => externalStages;
    private float waveCounter;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        RefreshTarget();
        despawnDistance = maxSpawn != null
            ? Vector3.Distance(transform.position, maxSpawn.position) + 5f : 0f;
        if (!externalStages)
            GoToNextWave();
    }

    private void RefreshTarget()
    {
        World world = World.GetFor(this);
        PlayerController player = world != null ? world.InteractionPlayer : null;
        PlayerHealth playerHealth = world != null
            ? (player != null ? player.GetComponent<PlayerHealth>() : null)
            : PlayerHealth.instance;
        target = playerHealth != null ? playerHealth.transform : null;
    }

    // Update is called once per frame
    void Update()
    {
        if (!isSpawning || Time.deltaTime <= 0f)
            return;

        RefreshTarget();
        if (target == null || !target.gameObject.activeInHierarchy)
            return;

        //Make the spawner follow the player
        transform.position = target.position;

        if(target.gameObject.activeInHierarchy)
        {
            if(currentWave >= 0 && currentWave < waves.Count)
            {
                if (!externalStages)
                    waveCounter -= Time.deltaTime;
                if(!externalStages && waveCounter <= 0)
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
                    GameObject newEnemy  = Instantiate(enemyToSpawn, SelectSpawnPoint(), Quaternion.identity, World.GetContentRoot(this));
                    spawnedEnemies.Add(newEnemy);
                }
            }
        }



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

    // May be called before Start, including while the source world sleeps.
    public void ConfigureExternalStages()
    {
        externalStages = true;
        StopSpawning(false);
    }

    public bool CanStartWave(int index)
    {
        if (minSpawn == null || maxSpawn == null || waves == null
            || index < 0 || index >= waves.Count || waves[index] == null)
            return false;

        WaveInfo wave = waves[index];
        if (wave.timeBetweenSpawns <= 0f || float.IsNaN(wave.timeBetweenSpawns)
            || float.IsInfinity(wave.timeBetweenSpawns)
            || wave.enemiesToSpawn == null || wave.enemiesToSpawn.Count == 0)
            return false;
        foreach (GameObject prefab in wave.enemiesToSpawn)
            if (prefab == null || prefab.GetComponent<EnemyController>() == null)
                return false;
        return true;
    }

    public bool TryStartWave(int index)
    {
        if (!CanStartWave(index) || (isSpawning && currentWave == index))
            return false;

        currentWave = index;
        waveCounter = waves[index].waveLength;
        spawnCounter = waves[index].timeBetweenSpawns;
        isSpawning = true;
        return true;
    }

    public void StopSpawning(bool clearSpawned)
    {
        isSpawning = false;
        if (!clearSpawned)
            return;

        foreach (GameObject enemy in spawnedEnemies)
        {
            if (enemy == null)
                continue;
            enemy.SetActive(false);
            Destroy(enemy);
        }
        spawnedEnemies.Clear();
        enemyToCheck = 0;
    }

    public void GoToNextWave()
    {
        if (externalStages || waves == null || waves.Count == 0)
            return;

        // Legacy mode repeats its final wave indefinitely.
        currentWave = Mathf.Min(currentWave + 1, waves.Count - 1);
        waveCounter = waves[currentWave].waveLength;
        spawnCounter = waves[currentWave].timeBetweenSpawns;
        isSpawning = true;
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
