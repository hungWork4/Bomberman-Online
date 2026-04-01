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
    private void OnDestroy()
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

    //ConnectionApprovalResponse là “phiếu quyết định” của server cho mỗi request kết nối: có cho vào không, có spawn player không, và spawn ở đâu.
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

        int spawnIndex = nextSpawnPointIndex % spawnPoints.Count;
        nextSpawnPointIndex++;

        clientSpawnIndices[request.ClientNetworkId] = spawnIndex;

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
}
