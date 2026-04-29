using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkManagerUI : MonoBehaviour
{
    public static NetworkManagerUI Instance { get; private set; }
    [SerializeField] private Button serverBtn;
    [SerializeField] private Button hostBtn;
    [SerializeField] private Button clientBtn;
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
    public IEnumerator botinit()
    {
        yield return new WaitForSeconds(2f); // Đợi một chút để đảm bảo mọi thứ đã sẵn sàng
        if(GameLobby.Instance != null )
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
        var players = PlayersManager.Instance.players;

        // Tìm index của player trong danh sách quản lý

        int AIIndex = players.Count+index; // Giả sử AI sẽ được thêm vào sau cùng, nên index của AI sẽ là số lượng player hiện tại

        // Nếu không tìm thấy player trong list
        if (AIIndex == -1)
        {
            Debug.LogWarning("Player không tồn tại trong danh sách quản lý của PlayersManager.");
            return false;
        }

        // Ánh xạ index: Nếu playerIndex vượt quá số lượng spawnPoints, dùng phép chia lấy dư (loop)
        // Hoặc nếu bạn muốn giới hạn cứng thì chỉ cần: if (playerIndex < spawnPoints.Count)
        int spawnIndex = AIIndex % spawnPoints.Count;

        spawnPoint = spawnPoints[spawnIndex];
        
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