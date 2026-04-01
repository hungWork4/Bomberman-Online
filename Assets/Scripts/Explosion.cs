using Unity.Netcode;
using UnityEngine;
using System.Collections;
public class Explosion : NetworkBehaviour
{
    public AnimatedSpriteRenderer start;//0
    public AnimatedSpriteRenderer middle;//1
    public AnimatedSpriteRenderer end;//2
    [ClientRpc]
    public void SetActiveRendererClientRpc(int type)
    {
        start.enabled = type == 0;
        middle.enabled = type == 1;
        end.enabled = type == 2;
    }
    [ClientRpc]
    public void SetDirectionClientRpc(Vector2 direction)
    {
        float angle = Mathf.Atan2(direction.y, direction.x);
        transform.rotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.forward);
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
            GetComponent<NetworkObject>().Despawn();
            Destroy(gameObject);
        }
    }
}
