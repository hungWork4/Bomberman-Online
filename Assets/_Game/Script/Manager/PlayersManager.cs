using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayersManager : NetworkBehaviour
{
    public static PlayersManager Instance { get; private set; }
    public List<PlayerController> players = new List<PlayerController>();
    public List<Transform> spawnPoints = new List<Transform>();
    public GameObject PlayerPrefab;
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
        if (NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnSceneEvent += OnNetworkSceneEvent;
        }

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            StartCoroutine(TrackPlayerWhenReady(client.ClientId));
        }

        // if (SceneManager.GetActiveScene().name == "Bomberman")
        // {
        //     StartCoroutine(PlaceAllPlayersAtSpawnPointsWhenReady());
        // }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            if (NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.OnSceneEvent -= OnNetworkSceneEvent;
            }
        }
    }

    private void OnNetworkSceneEvent(SceneEvent sceneEvent)
    {
        if (!IsServer)
        {
            return;
        }

        if (sceneEvent.SceneEventType == SceneEventType.LoadEventCompleted && sceneEvent.SceneName == "Bomberman")
        {
            StartCoroutine(PlaceAllPlayersAtSpawnPointsWhenReady());
        }
    }

    private IEnumerator PlaceAllPlayersAtSpawnPointsWhenReady()
    {
        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            yield break;
        }

        int waitFrames = 0;
        while (waitFrames < 300)
        {
            bool allReady = true;
            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client == null || client.PlayerObject == null)
                {
                    allReady = false;
                    break;
                }
            }

            if (allReady)
            {
                break;
            }

            waitFrames++;
            yield return null;
        }

        clientSpawnIndices.Clear();
        nextSpawnPointIndex = 0;

        List<NetworkClient> sortedClients = new List<NetworkClient>(NetworkManager.Singleton.ConnectedClientsList);
        sortedClients.Sort((a, b) => a.ClientId.CompareTo(b.ClientId));

        for (int i = 0; i < sortedClients.Count; i++)
        {
            NetworkClient client = sortedClients[i];
            if (client == null)
            {
                continue;
            }

            if (client.PlayerObject == null)
            {
                if (PlayerPrefab == null)
                {
                    Debug.LogError("PlayersManager thiếu PlayerPrefab để spawn player object.");
                    continue;
                }

                GameObject playerObj = Instantiate(PlayerPrefab, Vector3.zero, Quaternion.identity);
                NetworkObject networkObject = playerObj.GetComponent<NetworkObject>();
                if (networkObject == null)
                {
                    Debug.LogError("PlayerPrefab phải có NetworkObject component.");
                    Destroy(playerObj);
                    continue;
                }

                networkObject.SpawnAsPlayerObject(client.ClientId, true);
                client.PlayerObject = networkObject;
            }

            if (client.PlayerObject == null)
            {
                continue;
            }

            int spawnIndex = i % spawnPoints.Count;
            Transform spawnPoint = spawnPoints[spawnIndex];
            if (spawnPoint == null)
            {
                continue;
            }

            clientSpawnIndices[client.ClientId] = spawnIndex;
            PlayerController controller = client.PlayerObject.GetComponent<PlayerController>();
            if (controller != null)
            {
                controller.ForceServerSpawnStateServerRpc(spawnPoint.position, spawnPoint.rotation);
            }
            else
            {
                client.PlayerObject.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
            }
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
