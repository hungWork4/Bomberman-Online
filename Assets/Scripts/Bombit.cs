using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
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

    private Tilemap assignedDestructibleTiles;
    private int assignedExplosionRadius = 1;

    public void Initialize(Tilemap destructibleTiles, int explosionRadius)
    {
        assignedDestructibleTiles = destructibleTiles;
        assignedExplosionRadius = explosionRadius;
    }

    public void bombexplosion(Vector2 position, Tilemap destructibleTiles, int explosionRadius)
    {
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
            ClearDestructible(position, destructibleTiles);
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

        bombexplosion(position, assignedDestructibleTiles, assignedExplosionRadius + 1);
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
    private void ClearDestructible(Vector2 position, Tilemap destructibleTiles)
    {
        Vector3Int cell = destructibleTiles.WorldToCell(position);
        TileBase tile = destructibleTiles.GetTile(cell);

        if (tile != null)
        {
            Instantiate(destructiblePrefab, position, Quaternion.identity);
            destructibleTiles.SetTile(cell, null);
        }
        //if (tile != null)
        //{
        //    destructibleTiles.SetColliderType(cell, Tile.ColliderType.None);
        //    destructibleTiles.SetTile(cell, null);

        //    if (destructibleTiles.GetComponent<TilemapCollider2D>() != null)
        //    {
        //        destructibleTiles.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
        //    }

        //    Instantiate(destructiblePrefab, position, Quaternion.identity);
        //}
    }

}
