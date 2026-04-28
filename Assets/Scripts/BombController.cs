using System.Collections;
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
    private void Update()
    {
        if (bombsRemaining.Value > 0 && Input.GetKeyDown(inputKey) && IsOwner && UnityChatController.IsChatting == false)
        {
            PlaceBombServerRpc();
        }
    }
    [ServerRpc]
    private void PlaceBombServerRpc()
    {
        StartCoroutine(PlaceBomb());
    }
    
    private IEnumerator PlaceBomb()
    {
        Vector2 position = transform.position;
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
