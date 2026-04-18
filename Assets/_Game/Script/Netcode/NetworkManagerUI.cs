using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkManagerUI : MonoBehaviour
{
    [SerializeField] private Button serverBtn;
    [SerializeField] private Button hostBtn;
    [SerializeField] private Button clientBtn;
    [SerializeField] private Button spawnAIBtn;
    [SerializeField] private GameObject bombermanAIPrefab;

    private void Awake()
    {
        serverBtn.onClick.AddListener(() =>
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening && PlayersManager.Instance != null)
            {
                NetworkManager.Singleton.ConnectionApprovalCallback = PlayersManager.Instance.ApprovalCheck;
                NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
                NetworkManager.Singleton.StartServer();
            }
        });

        hostBtn.onClick.AddListener(() =>
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening && PlayersManager.Instance != null)
            {
                NetworkManager.Singleton.ConnectionApprovalCallback = PlayersManager.Instance.ApprovalCheck;
                NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
                NetworkManager.Singleton.StartHost();
            }
        });

        clientBtn.onClick.AddListener(() =>
        {
            if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
                NetworkManager.Singleton.StartClient();
            }
        });

        if (spawnAIBtn != null)
        {
            spawnAIBtn.onClick.AddListener(SpawnAI);
        }
    }

    private void SpawnAI()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (bombermanAIPrefab == null)
        {
            Debug.LogWarning("Missing Bomberman AI prefab reference on NetworkManagerUI.");
            return;
        }

        Vector3 spawnPosition = Vector3.zero;
        Quaternion spawnRotation = Quaternion.identity;

        if (PlayersManager.Instance != null)
        {
            if (!PlayersManager.Instance.TryGetEmptySpawnPoint(out Transform spawnPoint))
            {
                Debug.LogWarning("No empty spawn point available for AI.");
                return;
            }

            if (spawnPoint != null)
            {
                spawnPosition = spawnPoint.position;
                spawnRotation = spawnPoint.rotation;
            }
        }

        GameObject aiObject = Instantiate(bombermanAIPrefab, spawnPosition, spawnRotation);
        NetworkObject networkObject = aiObject.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError("Bomberman AI prefab must include a NetworkObject component.");
            Destroy(aiObject);
            return;
        }

        networkObject.Spawn();
    }
}