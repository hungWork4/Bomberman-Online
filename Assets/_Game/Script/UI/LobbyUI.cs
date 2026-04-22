using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [SerializeField] private GameObject LobbyContainer;
    [SerializeField] private SkinUI playerUIPrefab;
    [SerializeField] private Text LobbyInfoText;
    [SerializeField] private Button QuitLobbyButton;
    [SerializeField] private Button ReadyButton;

    private void Awake()
    {
        if (QuitLobbyButton != null)
        {
            QuitLobbyButton.onClick.AddListener(OnQuitLobbyClicked);
        }

        if (ReadyButton != null)
        {
            ReadyButton.onClick.AddListener(OnReadyClicked);
        }
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
        for (int i = containerTransform.childCount - 1; i >= 0; i--)
        {
            Destroy(containerTransform.GetChild(i).gameObject);
        }

        if (players == null || players.Count == 0)
        {
            return;
        }

        foreach (GameLobby.LobbyPlayerInfo player in players)
        {
            SkinUI playerUI = Instantiate(playerUIPrefab, containerTransform);
            playerUI.gameObject.transform.localScale = Vector3.one*1.65f;
            playerUI.SetSkinPlayerLobbyUI(player.SkinId, player.DisplayName, player.IsReady ? "READY" : "NONE");
        }
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
