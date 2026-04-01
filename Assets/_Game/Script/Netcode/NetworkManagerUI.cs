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
    }
}