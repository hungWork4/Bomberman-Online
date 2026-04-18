using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayersManager : NetworkBehaviour
{
    public static PlayersManager Instance { get; private set; }
    public List<PlayerController> players = new List<PlayerController>();
    public List<Transform> spawnPoints = new List<Transform>();

    private readonly HashSet<ulong> trackedClients = new HashSet<ulong>();
    private readonly Dictionary<ulong, int> clientSpawnIndices = new Dictionary<ulong, int>();
    private int nextSpawnPointIndex;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectionApprovalCallback == ApprovalCheck)
        {
            NetworkManager.Singleton.ConnectionApprovalCallback = null;
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            StartCoroutine(TrackPlayerWhenReady(client.ClientId));
        }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private int GetOrCreateSpawnIndex(ulong clientId)
    {
        if (clientSpawnIndices.TryGetValue(clientId, out int existingIndex))
        {
            return existingIndex;
        }

        int spawnIndex = nextSpawnPointIndex % spawnPoints.Count;
        nextSpawnPointIndex++;
        clientSpawnIndices[clientId] = spawnIndex;
        return spawnIndex;
    }

    public void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Pending = false;

        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            response.Position = Vector3.zero;
            response.Rotation = Quaternion.identity;
            return;
        }

        int spawnIndex = GetOrCreateSpawnIndex(request.ClientNetworkId);
        Transform spawnPoint = spawnPoints[spawnIndex];
        response.Position = spawnPoint.position;
        response.Rotation = spawnPoint.rotation;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }

        StartCoroutine(TrackPlayerWhenReady(clientId));
    }

    private void OnClientDisconnected(ulong clientId)
    {
        trackedClients.Remove(clientId);
        clientSpawnIndices.Remove(clientId);

        players.RemoveAll(p => p == null || (p.NetworkObject != null && p.NetworkObject.OwnerClientId == clientId));
    }

    private IEnumerator TrackPlayerWhenReady(ulong clientId)
    {
        if (trackedClients.Contains(clientId))
        {
            yield break;
        }

        NetworkObject playerObject = null;

        while (playerObject == null)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            {
                yield break;
            }

            playerObject = client.PlayerObject;
            if (playerObject == null)
            {
                yield return null;
            }
        }

        PlayerController playerController = playerObject.GetComponent<PlayerController>();
        if (playerController != null && !players.Contains(playerController))
        {
            players.Add(playerController);
        }

        trackedClients.Add(clientId);
    }

    public bool TryGetEmptySpawnPoint(out Transform emptySpawnPoint)
    {
        emptySpawnPoint = null;

        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < spawnPoints.Count; i++)
        {
            Transform spawnPoint = spawnPoints[i];
            if (spawnPoint == null)
            {
                continue;
            }

            bool occupied = false;
            for (int p = 0; p < players.Count; p++)
            {
                PlayerController player = players[p];
                if (player == null || player.NetworkObject == null || !player.NetworkObject.IsSpawned)
                {
                    continue;
                }

                Vector2 playerPos = player.transform.position;
                Vector2 spawnPos = spawnPoint.position;
                if (Mathf.RoundToInt(playerPos.x) == Mathf.RoundToInt(spawnPos.x) &&
                    Mathf.RoundToInt(playerPos.y) == Mathf.RoundToInt(spawnPos.y))
                {
                    occupied = true;
                    break;
                }
            }

            if (!occupied)
            {
                emptySpawnPoint = spawnPoint;
                return true;
            }
        }

        return false;
    }
}
