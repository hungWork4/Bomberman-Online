using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using DG.Tweening;
using TMPro;
using Unity.Netcode;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    public static LobbyUI Instance { get; private set; }
    [SerializeField]private SelectSkinUI selectSkinUI;
    public GameObject LobbyContainer;
    [SerializeField] public SkinUI playerUIPrefab;
    public GameObject AiUIPrefab;

    [SerializeField] private Text LobbyInfoText;
    [SerializeField] private Button QuitLobbyButton;
    [SerializeField] private Button ReadyButton;
    [SerializeField] private Button SkinButton;
    public Button AddBotButton;

    private readonly Dictionary<string, SkinUI> playerUIMap = new Dictionary<string, SkinUI>();
    private readonly Dictionary<string, GameLobby.LobbyPlayerInfo> playerInfoMap = new Dictionary<string, GameLobby.LobbyPlayerInfo>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (QuitLobbyButton != null)
        {
            QuitLobbyButton.onClick.AddListener(OnQuitLobbyClicked);
        }

        if (ReadyButton != null)
        {
            ReadyButton.onClick.AddListener(OnReadyClicked);
        }

        if (SkinButton != null)
        {
            SkinButton.onClick.AddListener(() =>
            {
                if (selectSkinUI != null)
                {
                    selectSkinUI.gameObject.SetActive(true);
                    selectSkinUI.GetComponent<CanvasGroup>().DOFade(1,1f);
                }
            });
        }
        if (AddBotButton != null )
        {
            AddBotButton.onClick.AddListener(() =>
            {
                _ = GameLobby.Instance.AddBot();
            });
        }
        // else
        // {
        //     if (AddBotButton != null)
        //     {
        //         AddBotButton.gameObject.SetActive(false);
        //     }
        // }
    }

    private void OnEnable()
    {
        if (GameLobby.Instance != null)
        {
            GameLobby.Instance.OnLobbyPlayersChanged += HandleLobbyPlayersChanged;
        }
    }

    private void OnDisable()
    {
        if (GameLobby.Instance != null)
        {
            GameLobby.Instance.OnLobbyPlayersChanged -= HandleLobbyPlayersChanged;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        if (QuitLobbyButton != null)
        {
            QuitLobbyButton.onClick.RemoveListener(OnQuitLobbyClicked);
        }

        if (ReadyButton != null)
        {
            ReadyButton.onClick.RemoveListener(OnReadyClicked);
        }
    }

    private async void Start()
    {

        await PopulateLobbyPlayersAsync();
        if (GameLobby.Instance != null)
        {
            if (!GameLobby.Instance.IsLobbyHost())
            {
                if (AddBotButton != null)
                {
                    AddBotButton.gameObject.SetActive(false);
                }
            }else
            {
                if (AddBotButton != null)
                {
                    AddBotButton.gameObject.SetActive(true);
                }
            }
        }
        else
        {
            Debug.LogError("GameLobby.Instance is null in LobbyUI.Start");
        }
    }

    private async void HandleLobbyPlayersChanged()
    {
        await PopulateLobbyPlayersAsync();
    }

    private async Task PopulateLobbyPlayersAsync()
    {
        if (LobbyContainer == null || playerUIPrefab == null)
        {
            Debug.LogError("LobbyUI thiếu LobbyContainer hoặc playerUIPrefab.");
            return;
        }

        int retryCount = 0;
        while (retryCount < 20 && GameLobby.Instance == null)
        {
            retryCount++;
            await Task.Delay(100);
        }

        if (GameLobby.Instance == null)
        {
            return;
        }

        UpdateLobbyInfoText();
        UpdateReadyButtonState();

        List<GameLobby.LobbyPlayerInfo> players = GameLobby.Instance.GetJoinedLobbyPlayers();

        Transform containerTransform = LobbyContainer.transform;
        if (players == null || players.Count == 0)
        {
            for (int i = containerTransform.childCount - 1; i >= 0; i--)
            {
                Destroy(containerTransform.GetChild(i).gameObject);
            }

            playerUIMap.Clear();
            playerInfoMap.Clear();
            return;
        }

        HashSet<string> activePlayerIds = new HashSet<string>();
        foreach (GameLobby.LobbyPlayerInfo player in players)
        {
            activePlayerIds.Add(player.PlayerId);
        }

        List<string> removedPlayerIds = new List<string>();
        foreach (KeyValuePair<string, SkinUI> entry in playerUIMap)
        {
            if (!activePlayerIds.Contains(entry.Key))
            {
                if (entry.Value != null)
                {
                    Destroy(entry.Value.gameObject);
                }

                removedPlayerIds.Add(entry.Key);
            }
        }

        foreach (string removedPlayerId in removedPlayerIds)
        {
            playerUIMap.Remove(removedPlayerId);
            playerInfoMap.Remove(removedPlayerId);
        }

        foreach (GameLobby.LobbyPlayerInfo player in players)
        {
            SkinUI playerUI;
            if (!playerUIMap.TryGetValue(player.PlayerId, out playerUI) || playerUI == null)
            {
                playerUI = Instantiate(playerUIPrefab, containerTransform);
                playerUI.gameObject.transform.localScale = Vector3.one * 1.65f;
                playerUIMap[player.PlayerId] = playerUI;
            }

            playerInfoMap[player.PlayerId] = player;
            playerUI.SetSkinPlayerLobbyUI(player.SkinId, player.DisplayName, player.IsReady ? "SẴN SÀNG" : "CHƯA");
        }
    }

    public void UpdateLocalPlayerSkinUI(int skinId)
    {
        string localPlayerId = AuthenticationService.Instance.PlayerId;
        if (string.IsNullOrEmpty(localPlayerId))
        {
            return;
        }

        if (!playerUIMap.TryGetValue(localPlayerId, out SkinUI playerUI) || playerUI == null)
        {
            return;
        }

        if (!playerInfoMap.TryGetValue(localPlayerId, out GameLobby.LobbyPlayerInfo playerInfo))
        {
            return;
        }

        playerInfo.SkinId = skinId;
        playerInfoMap[localPlayerId] = playerInfo;
        playerUI.SetSkinPlayerLobbyUI(skinId, playerInfo.DisplayName, playerInfo.IsReady ? "SẴN SÀNG" : "CHƯA");
    }

    private void UpdateLobbyInfoText()
    {
        if (LobbyInfoText == null || GameLobby.Instance == null)
        {
            return;
        }

        LobbyInfoText.text = GameLobby.Instance.GetLobbyInfoText();
    }

    private void OnQuitLobbyClicked()
    {
        if (GameLobby.Instance == null)
        {
            return;
        }

        if (QuitLobbyButton != null)
        {
            QuitLobbyButton.interactable = false;
        }

        GameLobby.Instance.QuitLobby();
    }

    private void OnReadyClicked()
    {
        if (GameLobby.Instance == null)
        {
            return;
        }

        if (ReadyButton != null)
        {
            ReadyButton.interactable = false;
        }

        GameLobby.Instance.SetLocalPlayerReady();
    }

    private void UpdateReadyButtonState()
    {
        if (ReadyButton == null || GameLobby.Instance == null)
        {
            return;
        }

        ReadyButton.interactable = !GameLobby.Instance.IsLocalPlayerReady();
    }
}
