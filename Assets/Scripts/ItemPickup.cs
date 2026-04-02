using Unity.Netcode;
using UnityEngine;

public class ItemPickup : NetworkBehaviour
{
    public enum ItemType
    {
        ExtraBomb,
        BlastRadius,
        SpeedIncrease,
    }

    public ItemType type;

    private void OnItemPickup(GameObject player)
    {
        switch (type)
        {
            case ItemType.ExtraBomb:
                player.GetComponent<BombController>().AddBomb();
                break;

            case ItemType.BlastRadius:
                player.GetComponent<BombController>().ExplosionRadius++;
                break;

            case ItemType.SpeedIncrease:
                player.GetComponent<PlayerController>().moveSpeed++;
                break;
        }
        this.GetComponent<NetworkObject>().Despawn();
        Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer) return;
        if (other.CompareTag("Player"))
        {
            if (other.TryGetComponent<NetworkObject>(out var playerNetworkObj))
            {
                OnItemPickup(other.gameObject);
                //GetComponent<NetworkObject>().Despawn();
            }
        }
    }

}
