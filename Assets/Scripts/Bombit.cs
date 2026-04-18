using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Tilemaps;

public class Bombit : NetworkBehaviour
{
    [Header("Explosion")]
    public Explosion explosionPrefab;
    public LayerMask explosionLayerMask;
    public float explosionDuration = 1f;
    //public int explosionRadius = 1;
    public Destructible destructiblePrefab;
    [Header("Chain Reaction")]
    public LayerMask bombLayerMask;

    private bool hasExploded;
    public bool HasExploded => hasExploded;

    [Header("Push Settings")]
    public float pushDistance = 4f;
    public float pushDuration = 0.4f; // Thời gian lướt (thay vì speed)
    public LayerMask obstacleLayer;

    private Tween pushTween;

    private Tilemap assignedDestructibleTiles;
    //private int assignedExplosionRadius = 1;
    public NetworkVariable<int> assignedExplosionRadius = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public override void OnNetworkSpawn()
    {
        ResolveDestructibleTilemap();
    }
    private bool ResolveDestructibleTilemap()
    {
        if (assignedDestructibleTiles != null)
        {
            return true;
        }

        Transform stage = GameObject.Find("Stage")?.transform;
        if (stage == null)
        {
            return false;
        }

        Transform destructibles = FindDeepChild(stage, "Destructibles");
        if (destructibles == null)
        {
            return false;
        }

        assignedDestructibleTiles = destructibles.GetComponent<Tilemap>();
        return assignedDestructibleTiles != null;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
            {
                return child;
            }

            Transform result = FindDeepChild(child, name);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    public void Initialize(Tilemap destructibleTiles, int explosionRadius)
    {
        if (!IsServer) return;
        assignedDestructibleTiles = destructibleTiles;
        assignedExplosionRadius.Value = explosionRadius;
    }

    public void bombexplosion(Vector2 position, Tilemap destructibleTiles, int explosionRadius)
    {
        if (pushTween != null) pushTween.Kill();
        if (hasExploded)
        {
            return;
        }
        hasExploded = true;
        Explosion explosion = Instantiate(explosionPrefab, position, Quaternion.identity);
        explosion.GetComponent<NetworkObject>().Spawn();
        explosion.SetActiveRendererClientRpc(0);
        explosion.DestroyAfterClientRpc(explosionDuration);

        Explode(position, Vector2.up, explosionRadius, destructibleTiles);
        Explode(position, Vector2.down, explosionRadius, destructibleTiles);
        Explode(position, Vector2.left, explosionRadius, destructibleTiles);
        Explode(position, Vector2.right, explosionRadius, destructibleTiles);

    }

    private void Explode(Vector2 position, Vector2 direction, int length, Tilemap destructibleTiles)
    {
        if (length <= 0)
        {
            return;
        }

        position += direction;
        if (TryTriggerChainBomb(position, destructibleTiles, length))
        {
            return;
        }
        if (Physics2D.OverlapBox(position, Vector2.one / 2f, 0f, explosionLayerMask))
        {
            ClearDestructibleServerRpc(position);
            return;
        }

        Explosion explosion = Instantiate(explosionPrefab, position, Quaternion.identity);
        explosion.GetComponent<NetworkObject>().Spawn();

        explosion.SetActiveRendererClientRpc(length > 1 ? 1 : 2);
        explosion.SetDirectionClientRpc(direction);
        explosion.DestroyAfterClientRpc(explosionDuration);

        Explode(position, direction, length - 1, destructibleTiles);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasExploded)
        {
            return;
        }

        if (other.gameObject.layer != LayerMask.NameToLayer("Explosion"))
        {
            return;
        }

        if (assignedDestructibleTiles == null)
        {
            return;
        }

        Vector2 position = transform.position;
        position.x = Mathf.Round(position.x);
        position.y = Mathf.Round(position.y);

        bombexplosion(position, assignedDestructibleTiles, assignedExplosionRadius.Value + 1);
        this.GetComponent<NetworkObject>().Despawn();
        Destroy(gameObject);
    }

    private bool TryTriggerChainBomb(Vector2 position, Tilemap destructibleTiles, int chainExplosionRadius)
    {
        Collider2D bombCollider = Physics2D.OverlapBox(position, Vector2.one / 2f, 0f, bombLayerMask);

        if (bombCollider == null)
        {
            return false;
        }

        Bombit otherBomb = bombCollider.GetComponent<Bombit>();

        if (otherBomb == null || otherBomb == this || otherBomb.HasExploded)
        {
            return false;
        }
        Vector2 otherBombPosition = otherBomb.transform.position;
        otherBombPosition.x = Mathf.Round(otherBombPosition.x);
        otherBombPosition.y = Mathf.Round(otherBombPosition.y);

        otherBomb.bombexplosion(otherBombPosition, destructibleTiles, chainExplosionRadius + 1);
        Destroy(otherBomb.gameObject);

        return true;
    }
    [ServerRpc]
    private void ClearDestructibleServerRpc(Vector2 position)
    {
        if (!ResolveDestructibleTilemap())
        {
            return;
        }

        Vector3Int cell = assignedDestructibleTiles.WorldToCell(position);
        TileBase tile = assignedDestructibleTiles.GetTile(cell);

        if (tile == null)
        {
            return;
        }

        // Server cũng phải clear tile
        assignedDestructibleTiles.SetTile(cell, null);
        assignedDestructibleTiles.RefreshTile(cell);

        Destructible spawned = Instantiate(destructiblePrefab, position, Quaternion.identity);
        spawned.GetComponent<NetworkObject>().Spawn();

        ClearTileVisualClientRpc(position);
    }

    [ClientRpc]
    private void ClearTileVisualClientRpc(Vector2 position)
    {
        if (!ResolveDestructibleTilemap())
        {
            return;
        }

        Vector3Int cell = assignedDestructibleTiles.WorldToCell(position);
        assignedDestructibleTiles.SetTile(cell, null);
        assignedDestructibleTiles.RefreshTile(cell);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestPushServerRpc(Vector2 direction)
    {
        if ((pushTween != null && pushTween.IsActive()) || hasExploded) return;

        Vector2 startPos = transform.position;
        GetComponent<Collider2D>().enabled = false;
        RaycastHit2D hit = Physics2D.BoxCast(startPos, Vector2.one * 0.6f, 0f, direction, pushDistance, obstacleLayer);
        GetComponent<Collider2D>().enabled = true; //tắt tạm thời tránh quét phải chính bomb bản thân
        string objectTag = "";
        if (hit.collider!=null)
            objectTag = hit.collider.tag;
        //float actualDist =((hit.collider != null && hit.collider.gameObject.layer != 6) || (objectTag == "Destructible" 
        //    && hit.collider.gameObject.layer == 6)) ? Mathf.Floor(hit.distance) : pushDistance;
        float actualDist = hit.collider != null  ? Mathf.Floor(hit.distance) : pushDistance;
        Vector2 targetPos = startPos + direction * actualDist;

        // Snap tọa độ để khớp Grid
        targetPos = new Vector2(Mathf.Round(targetPos.x), Mathf.Round(targetPos.y));

        // Gọi Client để tất cả cùng thấy mượt
        StartPushClientRpc(targetPos);
    }

    [ClientRpc]
    private void StartPushClientRpc(Vector2 target)
    {
        // Dùng DOTween để di chuyển
        // LƯU Ý: Dùng DOMove thay vì DOAnchorPos cho World Space
        pushTween = transform.DOMove(target, pushDuration)
            .SetEase(Ease.OutQuad) // Tạo hiệu ứng lướt nhanh rồi chậm lại một chút cho tự nhiên
            .OnComplete(() => {
                transform.position = target; // Đảm bảo vị trí chính xác tuyệt đối khi xong
            });
    }

}
