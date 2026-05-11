using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

public class NetworkManagerUI : MonoBehaviour
{
    public static NetworkManagerUI Instance { get; private set; }
    [SerializeField] private Button serverBtn;
    [SerializeField] private Button hostBtn;
    [SerializeField] private Button clientBtn;
    [SerializeField] private Button leaveMatchBtn;
    [SerializeField] private Button spawnAIBtn;
    [SerializeField] private GameObject bombermanAIPrefab;
    [SerializeField] private CanvasGroup LoadingScreen;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (leaveMatchBtn != null)
        {
            Navigation navigation = leaveMatchBtn.navigation;
            navigation.mode = Navigation.Mode.None;
            leaveMatchBtn.navigation = navigation;
            leaveMatchBtn.onClick.AddListener(LeaveMatch);
        }

        // serverBtn.onClick.AddListener(() =>
        // {
        //     if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening && PlayersManager.Instance != null)
        //     {
        //         NetworkManager.Singleton.ConnectionApprovalCallback = PlayersManager.Instance.ApprovalCheck;
        //         NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        //         NetworkManager.Singleton.StartServer();
        //     }
        // });

        // hostBtn.onClick.AddListener(() =>
        // {
        //     if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening && PlayersManager.Instance != null)
        //     {
        //         NetworkManager.Singleton.ConnectionApprovalCallback = PlayersManager.Instance.ApprovalCheck;
        //         NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        //         NetworkManager.Singleton.StartHost();
        //     }
        // });

        // clientBtn.onClick.AddListener(() =>
        // {
        //     if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsListening)
        //     {
        //         NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        //         NetworkManager.Singleton.StartClient();
        //     }
        // });

        // if (spawnAIBtn != null)
        // {
        //     spawnAIBtn.onClick.AddListener(() => SpawnAI());
        // }
    }

    private void OnDestroy()
    {
        if (leaveMatchBtn != null)
        {
            leaveMatchBtn.onClick.RemoveListener(LeaveMatch);
        }
    }

    public void LeaveMatch()
    {
        if (GameLobby.Instance != null)
        {
            // if (GameLobby.Instance.IsLobbyHost() && GameLobby.Instance.JoinedLobbyPlayerCount > 1)
            // {
            //     Debug.LogWarning("Host không thể rời trận khi vẫn còn người chơi khác trong phòng.");
            //     return;
            // }

            GameLobby.Instance.QuitLobby();
            return;
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
    }

    public void DisableLoadingScene()
    {
        if (LoadingScreen != null)
        {
            LoadingScreen.DOFade(0, 1f).OnComplete(() => {
                LoadingScreen.interactable = false;
                LoadingScreen.blocksRaycasts = false;
                LoadingScreen.gameObject.SetActive(false);
            });
        }
    }

    private void Update()
    {
        if (leaveMatchBtn == null || EventSystem.current == null)
        {
            return;
        }

        if (EventSystem.current.currentSelectedGameObject == leaveMatchBtn.gameObject)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    public IEnumerator botinit()
    {
        // Đợi player object được spawn đầy đủ để tránh bot lấy nhầm vị trí
        int waitFrames = 0;
        while (waitFrames < 300)
        {
            if (GameLobby.Instance != null && PlayersManager.Instance != null)
            {
                if (PlayersManager.Instance.players.Count >= GameLobby.Instance.JoinedLobbyPlayerCount)
                {
                    break;
                }
            }

            waitFrames++;
            yield return null;
        }

        if (GameLobby.Instance != null)
        {
            int totalBot =GameLobby.Instance.currentBotCount.Value;
            for (int i=0; i < totalBot; i++)
            {
                SpawnAI(i);
            }
        }
    }
    public bool TryGetSpawnPointByPlayerIndex(int index , out Transform spawnPoint)
    {
        var spawnPoints = PlayersManager.Instance.spawnPoints;
        spawnPoint = null;
        if (spawnPoints == null || spawnPoints.Count == 0)
        {
            return false;
        }

        int playerCount = PlayersManager.Instance.players.Count;
        if (GameLobby.Instance != null && GameLobby.Instance.JoinedLobbyPlayerCount > 0)
        {
            playerCount = GameLobby.Instance.JoinedLobbyPlayerCount;
        }

        int aiIndex = playerCount + index;
        if (aiIndex < 0 || aiIndex >= spawnPoints.Count)
        {
            return false;
        }

        spawnPoint = spawnPoints[aiIndex];
        return spawnPoint != null;
    }
    private void SpawnAI(int index =0)
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
            if (!TryGetSpawnPointByPlayerIndex(index, out Transform spawnPoint))
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