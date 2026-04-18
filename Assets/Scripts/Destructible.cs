using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class Destructible : NetworkBehaviour
{
    public float destructionTime = 1f;
    [Range(0f, 1f)]
    public float itemSpawnChance = 0.2f;
    public GameObject[] spawnableItems;

    private void Start()
    {
        //Destroy(gameObject, destructionTime);
        DestroyAfterClientRpc(destructionTime);
    }
    [ClientRpc]
    public void DestroyAfterClientRpc(float seconds)
    {
        StartCoroutine(DeSpawnCoroutine(seconds));
    }
    private IEnumerator DeSpawnCoroutine(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (IsServer)
        {
            if (spawnableItems.Length > 0 && Random.value < itemSpawnChance && IsServer)
            {
                int randomIndex = Random.Range(0, spawnableItems.Length);
                var item = Instantiate(spawnableItems[randomIndex], transform.position, Quaternion.identity);
                Debug.Log("Spawned item: " );
                item.gameObject.GetComponent<NetworkObject>().Spawn();
            }
            GetComponent<NetworkObject>().Despawn();
            Destroy(gameObject);
        }
    }
    //public override void OnDestroy()
    //{

    //}

}
