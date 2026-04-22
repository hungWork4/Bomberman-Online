using System.Collections;
using System.Collections.Generic;
using Unity.Services.Core;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.PlayerLoop;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Globalization;
using System;
using System.Threading.Tasks;

public class GameLobby : MonoBehaviour
{
    public event Action OnLobbyPlayersChanged;
    public event Action OnLobbyClosed;
    
    public struct LobbyPlayerInfo
    {
        public string PlayerId;
        public string DisplayName;
        public int SkinId;
        public bool IsReady;
    }

    // public static Game
    public static GameLobby Instance { get; private set; }
    private Lobby joinedLobby;
    private bool monitorLobbyPlayers;
    private string lastPlayerSetSignature;
    private bool isQuittingLobby;
    private bool hasStartedGame;
    public static int maxPlayerAmount = 4;
    void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeUnityAuthentication();
    }
    private async void InitializeUnityAuthentication() {
        if (UnityServices.State != ServicesInitializationState.Initialized) {
            InitializationOptions initializationOptions = new InitializationOptions();
            initializationOptions.SetProfile(UnityEngine.Random.Range(0, 10000).ToString());

            await UnityServices.InitializeAsync(initializationOptions);

            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }
    private void Update()
    {
        HandleHeartbeat();
    }
    private float heartbeatTimer;
    private void HandleHeartbeat()
    {
        if (IsLobbyHost())
        {
            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer <= 0f)
            {
                float heartbeatTimerMax = 15f;
                heartbeatTimer = heartbeatTimerMax;

                // Gửi tín hiệu Heartbeat để duy trì phòng
                LobbyService.Instance.SendHeartbeatPingAsync(joinedLobby.Id);
            }
        }
    }

    private bool IsLobbyHost()
    {
        // Kiểm tra xem Lobby có tồn tại và người chơi hiện tại có phải Host không
        return joinedLobby != null && joinedLobby.HostId == AuthenticationService.Instance.PlayerId;
    }
    public async void CreateLobby(string lobbyName, bool isPrivate) {
        try {
            hasStartedGame = false;
            Player lobbyPlayer = await BuildLobbyPlayerAsync();
            joinedLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName,maxPlayerAmount, new CreateLobbyOptions {
                IsPrivate = isPrivate,
                Player = lobbyPlayer,
            });
                StartLobbyPlayersMonitor();
                NetworkManager.Singleton.StartHost();
                NetworkManager.Singleton.SceneManager.LoadScene("Lobby", LoadSceneMode.Single);
        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }
    public async void QuickJoin() {
        try {
            hasStartedGame = false;
            Player lobbyPlayer = await BuildLobbyPlayerAsync();
            joinedLobby = await LobbyService.Instance.QuickJoinLobbyAsync(new QuickJoinLobbyOptions {
                Player = lobbyPlayer,
            });
            StartLobbyPlayersMonitor();
            NetworkManager.Singleton.StartClient();

        } catch (LobbyServiceException e) {
            Debug.Log(e);
        }
    }
    public async void JoinWithCode(string lobbyCode)
    {
        try
        {
            hasStartedGame = false;
            Player lobbyPlayer = await BuildLobbyPlayerAsync();
            joinedLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, new JoinLobbyByCodeOptions
            {
                Player = lobbyPlayer,
            });
            StartLobbyPlayersMonitor();
            NetworkManager.Singleton.StartClient();
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }
    private async void StartLobbyPlayersMonitor()
    {
        if (monitorLobbyPlayers || joinedLobby == null)
        {
            return;
        }

        monitorLobbyPlayers = true;
        lastPlayerSetSignature = BuildPlayerSetSignature(joinedLobby);
        OnLobbyPlayersChanged?.Invoke();

        while (monitorLobbyPlayers && joinedLobby != null)
        {
            try
            {
                Lobby updatedLobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);
                if (updatedLobby == null)
                {
                    break;
                }

                joinedLobby = updatedLobby;
                string newSignature = BuildPlayerSetSignature(joinedLobby);
                if (!string.Equals(newSignature, lastPlayerSetSignature, StringComparison.Ordinal))
                {
                    lastPlayerSetSignature = newSignature;
                    OnLobbyPlayersChanged?.Invoke();
                }

                if (!hasStartedGame && IsLobbyHost() && AreAllPlayersReady())
                {
                    hasStartedGame = true;
                    monitorLobbyPlayers = false;
                    NetworkManager.Singleton.SceneManager.LoadScene("Bomberman", LoadSceneMode.Single);
                    break;
                }
            }
            catch (LobbyServiceException)
            {
                HandleLobbyClosed();
                break;
            }

            await Task.Delay(1000);
        }

        monitorLobbyPlayers = false;
    }

    private string BuildPlayerSetSignature(Lobby lobby)
    {
        if (lobby == null || lobby.Players == null || lobby.Players.Count == 0)
        {
            return string.Empty;
        }

        List<string> playerStates = new List<string>();
        foreach (Player player in lobby.Players)
        {
            if (!string.IsNullOrEmpty(player?.Id))
            {
                string ready = TryGetPlayerDataValue(player, "is_ready");
                playerStates.Add(player.Id + ":" + ready);
            }
        }

        playerStates.Sort(StringComparer.Ordinal);
        return string.Join("|", playerStates);
    }

    private void OnDestroy()
    {
        monitorLobbyPlayers = false;
    }

    public async void QuitLobby()
    {
        if (isQuittingLobby)
        {
            return;
        }

        isQuittingLobby = true;
        monitorLobbyPlayers = false;

        try
        {
            if (joinedLobby != null)
            {
                if (IsLobbyHost())
                {
                    await LobbyService.Instance.DeleteLobbyAsync(joinedLobby.Id);
                }
                else
                {
                    await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId);
                }
            }
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning("QuitLobby warning: " + e.Message);
        }

        HandleLobbyClosed();
    }

    private void HandleLobbyClosed()
    {
        if (joinedLobby == null && !isQuittingLobby)
        {
            return;
        }

        monitorLobbyPlayers = false;
        joinedLobby = null;
        lastPlayerSetSignature = string.Empty;

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        OnLobbyClosed?.Invoke();
        SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
        isQuittingLobby = false;
    }

    public List<LobbyPlayerInfo> GetJoinedLobbyPlayers()
    {
        List<LobbyPlayerInfo> players = new List<LobbyPlayerInfo>();
        if (joinedLobby == null || joinedLobby.Players == null)
        {
            return players;
        }

        foreach (Player player in joinedLobby.Players)
        {
            string displayName = TryGetPlayerDataValue(player, "display_name");
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = player.Id;
            }

            int skinId = 0;
            string skinIdRaw = TryGetPlayerDataValue(player, "skin_id");
            if (!string.IsNullOrEmpty(skinIdRaw))
            {
                int.TryParse(skinIdRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out skinId);
            }

            bool isReady = string.Equals(TryGetPlayerDataValue(player, "is_ready"), "1", StringComparison.Ordinal);

            players.Add(new LobbyPlayerInfo
            {
                PlayerId = player.Id,
                DisplayName = displayName,
                SkinId = skinId,
                IsReady = isReady
            });
        }

        return players;
    }

    public string GetLobbyInfoText()
    {
        if (joinedLobby == null)
        {
            return "Lobby: --- / CODE:---";
        }

        string lobbyName = string.IsNullOrWhiteSpace(joinedLobby.Name) ? "---" : joinedLobby.Name;
        string lobbyCode = string.IsNullOrWhiteSpace(joinedLobby.LobbyCode) ? "---" : joinedLobby.LobbyCode;
        return "Lobby: " + lobbyName + " / CODE: " + lobbyCode;
    }

    public bool IsLocalPlayerReady()
    {
        if (joinedLobby == null || joinedLobby.Players == null)
        {
            return false;
        }

        string localPlayerId = AuthenticationService.Instance.PlayerId;
        foreach (Player player in joinedLobby.Players)
        {
            if (player != null && player.Id == localPlayerId)
            {
                return string.Equals(TryGetPlayerDataValue(player, "is_ready"), "1", StringComparison.Ordinal);
            }
        }

        return false;
    }

    public async void SetLocalPlayerReady()
    {
        if (joinedLobby == null)
        {
            return;
        }

        string localPlayerId = AuthenticationService.Instance.PlayerId;
        try
        {
            UpdatePlayerOptions updatePlayerOptions = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { "is_ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "1") }
                }
            };

            await LobbyService.Instance.UpdatePlayerAsync(joinedLobby.Id, localPlayerId, updatePlayerOptions);

            foreach (Player player in joinedLobby.Players)
            {
                if (player != null && player.Id == localPlayerId)
                {
                    if (player.Data == null)
                    {
                        player.Data = new Dictionary<string, PlayerDataObject>();
                    }

                    player.Data["is_ready"] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "1");
                    break;
                }
            }

            lastPlayerSetSignature = BuildPlayerSetSignature(joinedLobby);
            OnLobbyPlayersChanged?.Invoke();
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning("SetLocalPlayerReady failed: " + e.Message);
        }
    }

    private bool AreAllPlayersReady()
    {
        if (joinedLobby == null || joinedLobby.Players == null || joinedLobby.Players.Count == 0)
        {
            return false;
        }

        foreach (Player player in joinedLobby.Players)
        {
            if (!string.Equals(TryGetPlayerDataValue(player, "is_ready"), "1", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private async System.Threading.Tasks.Task<Player> BuildLobbyPlayerAsync()
    {
        await SupabaseManager.EnsureInitializedAsync();

        string displayName = "NONE";
        int skinId = -1;
        string supabaseUserId = string.Empty;

        SupabaseManager.PlayerSnapshot snapshotValue;
        bool hasSnapshot = SupabaseManager.TryGetCachedPlayerSnapshot(out snapshotValue);
        if (!hasSnapshot)
        {
            var refreshedSnapshot = await SupabaseManager.RefreshPlayerSnapshotAsync();
            if (refreshedSnapshot.HasValue)
            {
                snapshotValue = refreshedSnapshot.Value;
                hasSnapshot = true;
            }
        }

        if (hasSnapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshotValue.DisplayName))
            {
                displayName = snapshotValue.DisplayName;
            }

            skinId = (int)snapshotValue.CurrentSkinId;
            supabaseUserId = snapshotValue.UserId ?? string.Empty;
        }

        return new Player(id: AuthenticationService.Instance.PlayerId, data: new Dictionary<string, PlayerDataObject>
        {
            { "display_name", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, displayName) },
            { "skin_id", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, skinId.ToString(CultureInfo.InvariantCulture)) },
            { "supabase_user_id", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, supabaseUserId) },
            { "is_ready", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, "0") },
        });
    }

    private string TryGetPlayerDataValue(Player player, string key)
    {
        if (player == null || player.Data == null || string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (player.Data.TryGetValue(key, out PlayerDataObject value) && value != null)
        {
            return value.Value;
        }

        return string.Empty;
    }
}
