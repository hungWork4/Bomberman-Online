using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Tilemaps;

public class BombController : NetworkBehaviour
{
    [Header("Bomb")]
    [SerializeField] private KeyCode inputKey = KeyCode.LeftShift;
    [SerializeField] private GameObject bombPrefab;
    [SerializeField] private float bombFuseTime = 3f;
    // [SerializeField] private int bombAmount = 1;
    private NetworkVariable<int> bombAmount = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    //[SerializeField] private int bombsRemaining;
    private NetworkVariable<int> bombsRemaining = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    //[SerializeField] private int explosionRadius = 1;
    [SerializeField] private NetworkVariable<int> explosionRadius = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public int ExplosionRadius { get => explosionRadius.Value; set => explosionRadius.Value = value; }
    public int BombAmount { get => bombAmount.Value; set => bombAmount.Value = value; }

    public int CurrentBombCount => bombsRemaining.Value;

    [Header("Destructible")]
    public Tilemap destructibleTiles;

    private void OnEnable()
    {
        Debug.Log("load Destructibles");
        if (destructibleTiles == null)
        {
            var stage = GameObject.Find("Stage")?.transform;

            if (stage != null)
            {
                var destructibles = FindDeepChild(stage, "Destructibles");
                if (destructibles != null)
                {
                    destructibleTiles = destructibles.GetComponent<Tilemap>();
                }
            }
        }
           
    }
    private void Start()
    {
        if(IsServer)
            bombsRemaining.Value = bombAmount.Value;
        bombAmount.OnValueChanged += (oldValue, newValue) => ScoreBoardController.Instance?.UpdatePlayerCard(this.GetComponent<PlayerController>());
        explosionRadius.OnValueChanged += (oldValue, newValue) => ScoreBoardController.Instance?.UpdatePlayerCard(this.GetComponent<PlayerController>());

    }
    public static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;

            var result = FindDeepChild(child, name);
            if (result != null)
                return result;
        }
        return null;
    }
    private static readonly Dictionary<Vector2, GameObject> _activeAnticipationBombs = new Dictionary<Vector2, GameObject>();

    private void Update()
    {
        if (bombsRemaining.Value > 0 && Input.GetKeyDown(inputKey) && IsOwner && UnityChatController.IsChatting == false)
        {
            Vector2 position = new Vector2(Mathf.Round(transform.position.x), Mathf.Round(transform.position.y));

            // Client-Side Action Anticipation: hiển thị quả bomb thử nghiệm tức thì trên màn hình Client
            if (IsClient && !IsServer)
            {
                AnticipatePlaceBomb(position);
            }

            PlaceBombServerRpc(position);
        }
    }

    private void AnticipatePlaceBomb(Vector2 position)
    {
        if (_activeAnticipationBombs.ContainsKey(position)) return;

        GameObject ghostBomb = Instantiate(bombPrefab, position, Quaternion.identity);

        var netObj = ghostBomb.GetComponent<NetworkObject>();
        if (netObj != null) Destroy(netObj);

        var bombit = ghostBomb.GetComponent<Bombit>();
        if (bombit != null) Destroy(bombit);

        var col = ghostBomb.GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;

        _activeAnticipationBombs[position] = ghostBomb;
        StartCoroutine(AnticipationSafeguardRoutine(position, ghostBomb, 0.8f));
    }

    private IEnumerator AnticipationSafeguardRoutine(Vector2 position, GameObject ghostBomb, float timeout)
    {
        yield return new WaitForSeconds(timeout);
        if (_activeAnticipationBombs.TryGetValue(position, out GameObject currentGhost) && currentGhost == ghostBomb)
        {
            _activeAnticipationBombs.Remove(position);
            if (ghostBomb != null) Destroy(ghostBomb);
        }
    }

    public static void NotifyBombSpawnedClient(Vector2 position)
    {
        Vector2 rounded = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
        if (_activeAnticipationBombs.TryGetValue(rounded, out GameObject ghost))
        {
            _activeAnticipationBombs.Remove(rounded);
            if (ghost != null) Destroy(ghost);
        }
    }

    [ServerRpc]
    private void PlaceBombServerRpc(Vector2 position)
    {
        StartCoroutine(PlaceBomb(position));
    }
    
    private IEnumerator PlaceBomb(Vector2 position)
    {
        position.x = Mathf.Round(position.x);
        position.y = Mathf.Round(position.y);

        GameObject bomb = Instantiate(bombPrefab, position, Quaternion.identity);
        bomb.GetComponent<NetworkObject>().Spawn();
        Bombit bombit = bomb.GetComponent<Bombit>();
        if (bombit != null)
        {
            bombit.Initialize(destructibleTiles, ExplosionRadius);
        }
        bombsRemaining.Value--;

        yield return new WaitForSeconds(bombFuseTime);
        if (bomb == null)
        {
            //phongf trg hoop bi kick no trc
            bombsRemaining.Value++;
            yield break;
        }
        position = bomb.transform.position;
        position.x = Mathf.Round(position.x);
        position.y = Mathf.Round(position.y);

        bomb.GetComponent<Bombit>().bombexplosion(position, destructibleTiles, ExplosionRadius);
        bomb.GetComponent<NetworkObject>().Despawn();
        Destroy(bomb);
        bombsRemaining.Value++;
    }
    public void AddBomb()
    {
        bombAmount.Value++;
        bombsRemaining.Value++;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.gameObject.layer == LayerMask.NameToLayer("Bomb"))
        {
            other.isTrigger = false;
        }
    }

}
