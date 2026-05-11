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
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;

public class GameLobby : NetworkBehaviour
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
    private const string RelayJoinCodeKey = "RelayJoinCode";
    private Lobby joinedLobby;
    public int JoinedLobbyPlayerCount => joinedLobby?.Players?.Count ?? 0;
    private bool monitorLobbyPlayers;
    private string lastPlayerSetSignature;
    private bool isQuittingLobby;
    private bool hasStartedGame;
    public static int maxPlayerAmount = 4;
    private List<GameObject> spawnedBotUIs = new List<GameObject>();
    // public int currentBotCount { get; private set; } = 0;
    public NetworkVariable<int> currentBotCount = new NetworkVariable<int>(0, 
    NetworkVariableReadPermission.Everyone, 
    NetworkVariableWritePermission.Server);
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // Xóa bản sao mới nếu đã có một Instance đang chạy
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeUnityAuthentication();
        // Đăng ký sự kiện: Khi Server bắt đầu chạy thành công thì mới Spawn
        NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
    }
    public override void OnNetworkSpawn()
    {
        // Khi giá trị thay đổi, UI sẽ tự cập nhật
        if (IsServer)
        {
            currentBotCount.Value = 0; 
            Debug.Log(">>> [Host] Đã reset syncBotCount về 0 an toàn.");
        }
        currentBotCount.OnValueChanged += (oldVal, newVal) => {
            UpdateBotUI(newVal);
        };
        
        // Cập nhật UI lần đầu khi vừa join
        UpdateBotUI(currentBotCount.Value);
    }

    private void UpdateBotUI(int totalBots)
    {
       Debug.Log($">>> [UI] Cập nhật danh sách Bot. Số lượng: {totalBots}");

        // 1. Xóa các UI Bot cũ đang tồn tại (để làm sạch danh sách)
        foreach (var bot in spawnedBotUIs)
        {
            if (bot != null) Destroy(bot);
        }
        spawnedBotUIs.Clear();

        // 2. Kiểm tra Singleton UI có tồn tại không
        if (LobbyUI.Instance == null || LobbyUI.Instance.AiUIPrefab == null) return;

        // 3. Tạo mới các UI Bot dựa trên số lượng totalBots
        for (int i = 0; i < totalBots; i++)
        {
            GameObject botUI = Instantiate(LobbyUI.Instance.AiUIPrefab, LobbyUI.Instance.LobbyContainer.transform);
            botUI.transform.localScale = Vector3.one * 1.65f;
            
            // Reset vị trí nếu cần (đảm bảo UI nằm đúng trong Layout Group)
            RectTransform rect = botUI.GetComponent<RectTransform>();
            if (rect != null) rect.anchoredPosition = Vector2.zero;

            // Lưu vào danh sách để quản lý xóa sau này
            spawnedBotUIs.Add(botUI);
        }
    }
    private void HandleServerStarted()
    {
        // Chỉ Host mới thực hiện việc Spawn NetworkObject
        if (IsHost) 
        {
            var networkObj = GetComponent<NetworkObject>();
            if (networkObj != null && !networkObj.IsSpawned)
            {
                networkObj.Spawn();
                Debug.Log(">>> [Success] GameLobby đã Spawn thành công trên Network!");
            }
        }
    }

    private void OnDisable() // Quan trọng: Hủy đăng ký khi object bị hủy
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
        }
    }
    private async void InitializeUnityAuthentication() {
        if (UnityServices.State != ServicesInitializationState.Initialized) {
            InitializationOptions initializationOptions = new InitializationOptions();
            initializationOptions.SetProfile(UnityEngine.Random.Range(0, 10000).ToString());

            await UnityServices.InitializeAsync(initializationOptions);

            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }
    public async Task SetBotCount(int count)
    {
        if (!IsLobbyHost()) return;
        currentBotCount.Value = count;
        await CheckAndLockLobbyIfNeeded();
    }
    public async Task AddBot()
    {
        if(currentBotCount.Value + joinedLobby.Players.Count >= maxPlayerAmount)
        {
            Debug.LogWarning("Không thể thêm bot: Số lượng người chơi đã đạt tối đa.");
            return;
        }
        if (!IsSpawned)
        {
            Debug.LogWarning("Chờ chút... NetworkObject đang được khởi tạo, hãy thử lại sau 1 giây.");
            return;
        }
            Debug.Log("Adding bot...");
        // if (!IsLobbyHost()) return;
        currentBotCount.Value += 1;
        // SpawnAIUIprefabServerRpc();
        await CheckAndLockLobbyIfNeeded();
    }
    private async Task CheckAndLockLobbyIfNeeded()
    {
        if (joinedLobby == null || !IsLobbyHost()) return;

        // Nếu tổng (người chơi thực + máy) >= maxPlayerAmount, tiến hành khoá phòng
        bool shouldLock = (joinedLobby.Players.Count + currentBotCount.Value) >= maxPlayerAmount;

        if (joinedLobby.IsLocked != shouldLock)
        {
            try
            {
                UpdateLobbyOptions options = new UpdateLobbyOptions { IsLocked = shouldLock };
                joinedLobby = await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, options);
            }
            catch (LobbyServiceException e)
            {
                Debug.LogWarning("Update lock state failed: " + e.Message);
            }
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

    public bool IsLobbyHost()
    {
        // Kiểm tra xem Lobby có tồn tại và người chơi hiện tại có phải Host không
        return joinedLobby != null && joinedLobby.HostId == AuthenticationService.Instance.PlayerId;
    }
    private async Task<Allocation> AllocateRelay()
    {
        try
        {
            Allocation allocation = await  RelayService.Instance.CreateAllocationAsync(maxPlayerAmount - 1);
            return allocation;
        }
        catch(RelayServiceException e)
        {
            Debug.LogError("Failed to create Relay allocation: " + e.Message);
            return default;
        }
    }
    private async Task<string> GetRelayJoinCode(Allocation allocation)
    {
        try
        {
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log("Relay Join Code: " + joinCode);
            return joinCode;
        }
        catch(RelayServiceException e)
        {
            Debug.LogError("Failed to get Relay join code: " + e.Message);
            return default;
        }
    }
    private async Task<JoinAllocation> JoinRelay(string joinCode) {
        try {
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            return joinAllocation;
        } catch (RelayServiceException e) {
            Debug.Log(e);
            return default;
        }
    }
    public async void CreateLobby(string lobbyName, bool isPrivate) {
        try {
            hasStartedGame = false;
            Player lobbyPlayer = await BuildLobbyPlayerAsync();
            joinedLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName,maxPlayerAmount, new CreateLobbyOptions {
                IsPrivate = isPrivate,
                Player = lobbyPlayer,
            });
                Allocation allocation = await AllocateRelay();
                string relayJoinCode = await GetRelayJoinCode(allocation);  
                
                await LobbyService.Instance.UpdateLobbyAsync(joinedLobby.Id, new UpdateLobbyOptions {
                    Data = new Dictionary<string, DataObject> {
                        { RelayJoinCodeKey, new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
                    }
                });
                NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(allocation, "dtls"));

                StartLobbyPlayersMonitor();
                NetworkManager.Singleton.StartHost();

                var networkObj = GetComponent<NetworkObject>();
                if (networkObj != null && !networkObj.IsSpawned)
                {
                    networkObj.Spawn(); 
                }

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
            string relayJoinCode = joinedLobby.Data[RelayJoinCodeKey].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(joinAllocation, "dtls"));

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
            string relayJoinCode = joinedLobby.Data[RelayJoinCodeKey].Value;
            JoinAllocation joinAllocation = await JoinRelay(relayJoinCode);

            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(new RelayServerData(joinAllocation, "dtls"));
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
                    
                    _ = CheckAndLockLobbyIfNeeded();
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
                string skinId = TryGetPlayerDataValue(player, "skin_id");
                playerStates.Add(player.Id + ":" + ready + ":" + skinId);
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

    public async Task<bool> SetLocalPlayerSkinAsync(int skinId)
    {
        if (joinedLobby == null)
        {
            return false;
        }

        string localPlayerId = AuthenticationService.Instance.PlayerId;
        try
        {
            UpdatePlayerOptions updatePlayerOptions = new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { "skin_id", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, skinId.ToString(CultureInfo.InvariantCulture)) }
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

                    player.Data["skin_id"] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, skinId.ToString(CultureInfo.InvariantCulture));
                    break;
                }
            }

            lastPlayerSetSignature = BuildPlayerSetSignature(joinedLobby);
            OnLobbyPlayersChanged?.Invoke();
            return true;
        }
        catch (LobbyServiceException e)
        {
            Debug.LogWarning("SetLocalPlayerSkinAsync failed: " + e.Message);
            return false;
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
