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
        try
        {
            var bombController = player.GetComponent<BombController>();
            var playerController = player.GetComponent<PlayerController>();
            var aiController = player.GetComponent<BombermanAI>();

            switch (type)
            {
                case ItemType.ExtraBomb:
                    if (bombController != null)
                    {
                        bombController.AddBomb();
                    }
                    else if (aiController != null)
                    {
                        aiController.AddBomb();
                    }
                    break;

                case ItemType.BlastRadius:
                    if (bombController != null)
                    {
                        bombController.ExplosionRadius++;
                    }
                    else if (aiController != null)
                    {
                        aiController.AddBlastRadius();
                    }
                    break;

                case ItemType.SpeedIncrease:
                    if (playerController != null)
                    {
                        playerController.moveSpeed++;
                    }
                    else if (aiController != null)
                    {
                        aiController.AddMoveSpeed();
                    }
                    break;
            }
            this.GetComponent<NetworkObject>().Despawn();
            Destroy(gameObject);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error applying item effect: {e.Message}");
        }
        
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer) return;

        bool canReceiveItem = other.CompareTag("Player") ||
                              other.GetComponent<BombermanAI>() != null;

        if (canReceiveItem)
        {
            if (other.TryGetComponent<NetworkObject>(out var playerNetworkObj))
            {
                OnItemPickup(other.gameObject);
                //GetComponent<NetworkObject>().Despawn();
            }
        }
    }

}
