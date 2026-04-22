using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public class StoreController : MonoBehaviour
{
    public static StoreController Instance { get; private set; }
    [SerializeField]private long playerGold;
    [SerializeField]private  TextMeshPro goldText;
    [SerializeField]private  TextMeshPro playerNameText;

    [SerializeField]private List<Skin> availableSkins;
    [SerializeField]private SkinUI skinItemPrefab; // Prefab default cho item skin trong UI
    [SerializeField]private Transform skinListContainer; // Container để chứa các item skin trong UI

    [SerializeField] private string purchaseSkinRpc = "purchase_skin_atomic";

    [System.Serializable]
    private class PurchaseSkinRequest
    {
        public long p_skin_id;
    }

    [System.Serializable]
    private class PurchaseSkinResponse
    {
        public bool success;
        public string message;
        public long remaining_gold;
        public long owned_skin_id;
    }

    private struct StorePlayerData
    {
        public long Gold;
        public string DisplayName;
        public HashSet<long> OwnedSkinIds;
    }
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    private string currentPlayerId;

    private void UpdateGoldUI()
    {
        if (goldText != null)
        {
            goldText.text = playerGold.ToString();
        }
    }

    private void UpdatePlayerNameUI(string displayName)
    {
        if (playerNameText != null)
        {
            playerNameText.text ="Hello: "+ (string.IsNullOrEmpty(displayName) ? "Player" : displayName);
        }
    }

    private async Task<StorePlayerData> LoadStorePlayerDataAsync(string playerId)
    {
        StorePlayerData data = new StorePlayerData
        {
            Gold = 0,
            DisplayName = string.Empty,
            OwnedSkinIds = new HashSet<long>()
        };

        if (!SupabaseManager.TryGetAccessToken(out string accessToken))
        {
            Debug.LogWarning("[Store] Không có access token để lấy dữ liệu người chơi.");
            return data;
        }

        if (string.IsNullOrEmpty(SupabaseManager.ProjectUrl) || string.IsNullOrEmpty(SupabaseManager.AnonKey))
        {
            Debug.LogWarning("[Store] Thiếu cấu hình Supabase URL/AnonKey cho store query.");
            return data;
        }

        string encodedPlayerId = UnityWebRequest.EscapeURL(playerId);
        string baseUrl = SupabaseManager.ProjectUrl.TrimEnd('/');
        string statsUrl = baseUrl + "/rest/v1/player_stats?select=gold&id=eq." + encodedPlayerId + "&limit=1";
        string profileUrl = baseUrl + "/rest/v1/player_profiles?select=display_name&id=eq." + encodedPlayerId + "&limit=1";
        string inventoryUrl = baseUrl + "/rest/v1/player_inventory?select=skin_id&player_id=eq." + encodedPlayerId;

        using (UnityWebRequest statsRequest = UnityWebRequest.Get(statsUrl))
        using (UnityWebRequest profileRequest = UnityWebRequest.Get(profileUrl))
        using (UnityWebRequest inventoryRequest = UnityWebRequest.Get(inventoryUrl))
        {
            statsRequest.downloadHandler = new DownloadHandlerBuffer();
            profileRequest.downloadHandler = new DownloadHandlerBuffer();
            inventoryRequest.downloadHandler = new DownloadHandlerBuffer();

            statsRequest.SetRequestHeader("Content-Type", "application/json");
            statsRequest.SetRequestHeader("apikey", SupabaseManager.AnonKey);
            statsRequest.SetRequestHeader("Authorization", "Bearer " + accessToken);

            profileRequest.SetRequestHeader("Content-Type", "application/json");
            profileRequest.SetRequestHeader("apikey", SupabaseManager.AnonKey);
            profileRequest.SetRequestHeader("Authorization", "Bearer " + accessToken);

            inventoryRequest.SetRequestHeader("Content-Type", "application/json");
            inventoryRequest.SetRequestHeader("apikey", SupabaseManager.AnonKey);
            inventoryRequest.SetRequestHeader("Authorization", "Bearer " + accessToken);

            var statsOperation = statsRequest.SendWebRequest();
            var profileOperation = profileRequest.SendWebRequest();
            var inventoryOperation = inventoryRequest.SendWebRequest();

            while (!statsOperation.isDone || !profileOperation.isDone || !inventoryOperation.isDone)
            {
                await Task.Yield();
            }

            if (statsRequest.isNetworkError || statsRequest.isHttpError)
            {
                Debug.LogWarning("[Store] Không lấy được player_stats: " + statsRequest.error + " " + statsRequest.downloadHandler.text);
            }
            else
            {
                string statsResponse = statsRequest.downloadHandler.text;
                var goldMatch = Regex.Match(statsResponse, "\"gold\"\\s*:\\s*(\\d+)");
                data.Gold = (goldMatch.Success && long.TryParse(goldMatch.Groups[1].Value, out var g)) ? g : 0;
            }

            if (profileRequest.isNetworkError || profileRequest.isHttpError)
            {
                Debug.LogWarning("[Store] Không lấy được player_profiles: " + profileRequest.error + " " + profileRequest.downloadHandler.text);
            }
            else
            {
                string profileResponse = profileRequest.downloadHandler.text;
                var nameMatch = Regex.Match(profileResponse, "\"display_name\"\\s*:\\s*\"([^\"]*)\"");
                if (nameMatch.Success && nameMatch.Groups.Count > 1)
                {
                    data.DisplayName = nameMatch.Groups[1].Value;
                }
            }

            if (inventoryRequest.isNetworkError || inventoryRequest.isHttpError)
            {
                Debug.LogWarning("[Store] Không lấy được inventory qua REST: " + inventoryRequest.error + " " + inventoryRequest.downloadHandler.text);
                return data;
            }

            string response = inventoryRequest.downloadHandler.text;
            MatchCollection matches = Regex.Matches(response, "\"skin_id\"\\s*:\\s*(\\d+)");
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1 && long.TryParse(match.Groups[1].Value, out long skinId))
                {
                    data.OwnedSkinIds.Add(skinId);
                }
            }
        }

        Debug.Log($"[Store] Player {playerId} name={data.DisplayName}, gold={data.Gold}, owned_skins={data.OwnedSkinIds.Count}");
        return data;
    }

    /// <summary>
    /// Lấy danh sách tất cả skin và đánh dấu skin nào người chơi hiện tại đã có.
    /// </summary>
    /// <param name="playerId">UUID của người chơi hiện tại</param>
    public async Task<List<Skin>> GetSkinsForStore(string playerId)
    {
        try
        {
            await SupabaseManager.EnsureInitializedAsync();
            var client = SupabaseManager.Client;
            if (client == null)
            {
                Debug.LogError("Supabase client chưa được khởi tạo.");
                return new List<Skin>();
            }

            // Luôn ưu tiên load danh sách skin trước.
            List<Skin> allSkins = (await client
                .From<Skin>()
                .Get()).Models;

            // Load player data (gold + owned skins) in one flow.
            StorePlayerData playerData = await LoadStorePlayerDataAsync(playerId);
            playerGold = playerData.Gold;
            UpdateGoldUI();
            UpdatePlayerNameUI(playerData.DisplayName);
            HashSet<long> ownedSkinIds = playerData.OwnedSkinIds;

            // 3. Duyệt danh sách skin tổng để đánh dấu
            foreach (var skin in allSkins)
            {
                // Nếu ID nằm trong inventory HOẶC giá = 0 (skin mặc định) thì coi như đã có
                if (ownedSkinIds.Contains(skin.Id) || skin.Price == 0)
                {
                    skin.IsOwned = true;
                }
                else
                {
                    skin.IsOwned = false;
                }
            }

            Debug.Log($"[Store] Loaded skins={allSkins.Count}, owned={ownedSkinIds.Count}, playerId={playerId}");

            return allSkins;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Lỗi khi lấy dữ liệu Store: {ex.Message}");
            return new List<Skin>();
        }
    }
    private void Start()
    {
        InitializeStore();
    }
    /// <summary>
    /// Hàm mẫu để gọi và hiển thị lên UI Store
    /// </summary>
    public async void InitializeStore()
    {
        await SupabaseManager.EnsureInitializedAsync();
        if (!SupabaseManager.TryGetCurrentUserId(out string myId))
        {
            Debug.LogError("Không tìm thấy session đăng nhập Supabase hiện tại. Hãy đăng nhập trước khi mở Store.");
            return;
        }

        currentPlayerId = myId;
        Debug.Log($"[Store] Khởi tạo Store cho playerId={myId}");
        
        List<Skin> displaySkins = await GetSkinsForStore(myId);
        availableSkins = displaySkins;

        if (displaySkins == null || displaySkins.Count == 0)
        {
            Debug.LogWarning("[Store] Danh sách skin rỗng. Kiểm tra dữ liệu bảng skins hoặc RLS policy select.");
        }

        RenderStoreItems(displaySkins);

        // foreach (var skin in displaySkins)
        // {
        //     if (skin.IsOwned)
        //     {
        //         Debug.Log($"[Store] {skin.Name}: Đã sở hữu (Hiện nút EQUIP)");
        //     }
        //     else
        //     {
        //         Debug.Log($"[Store] {skin.Name}: Giá {skin.Price} (Hiện nút BUY)");
        //     }
        // }
    }

    private void RenderStoreItems(List<Skin> skins)
    {
        if (skinItemPrefab == null || skinListContainer == null)
        {
            Debug.LogError("Thiếu skinItemPrefab hoặc skinListContainer để render Store.");
            return;
        }

        for (int i = skinListContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(skinListContainer.GetChild(i).gameObject);
        }

        foreach (var skin in skins)
        {
            SkinUI item = Instantiate(skinItemPrefab, skinListContainer);
            string status = skin.IsOwned ? "OWNED" : "BUY";
            item.SetSkinUI((int)skin.Id, skin.Name, skin.Price, status);

            if (item.buyButton != null)
            {
                item.buyButton.gameObject.SetActive(true);
                item.buyButton.onClick.RemoveAllListeners();
                item.buyButton.interactable = !skin.IsOwned;

                long skinId = skin.Id;
                if (!skin.IsOwned)
                {
                    item.buyButton.onClick.AddListener(() =>
                    {
                        _ = HandleBuyClicked(skinId);
                    });
                }
            }
        }
    }

    private async Task HandleBuyClicked(long skinId)
    {
        bool purchased = await PurchaseSkinAsync(skinId);
        if (!purchased)
        {
            return;
        }

        if (string.IsNullOrEmpty(currentPlayerId))
        {
            return;
        }

        List<Skin> updatedSkins = await GetSkinsForStore(currentPlayerId);
        availableSkins = updatedSkins;
        RenderStoreItems(updatedSkins);
    }

    public async Task<bool> PurchaseSkinAsync(long skinId)
    {
        await SupabaseManager.EnsureInitializedAsync();

        if (!SupabaseManager.TryGetAccessToken(out string accessToken))
        {
            Debug.LogError("Không có access token. Hãy đăng nhập trước khi mua skin.");
            return false;
        }

        if (string.IsNullOrEmpty(SupabaseManager.ProjectUrl) || string.IsNullOrEmpty(SupabaseManager.AnonKey))
        {
            Debug.LogError("Thiếu cấu hình Supabase URL/AnonKey.");
            return false;
        }

        string rpcUrl = SupabaseManager.ProjectUrl.TrimEnd('/') + "/rest/v1/rpc/" + purchaseSkinRpc;
        PurchaseSkinRequest requestPayload = new PurchaseSkinRequest { p_skin_id = skinId };
        string json = JsonUtility.ToJson(requestPayload);

        using (UnityWebRequest request = new UnityWebRequest(rpcUrl, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("apikey", SupabaseManager.AnonKey);
            request.SetRequestHeader("Authorization", "Bearer " + accessToken);

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.isNetworkError || request.isHttpError)
            {
                Debug.LogError($"[Store] Mua skin thất bại: {request.error} {request.downloadHandler.text}");
                return false;
            }

            string responseText = request.downloadHandler.text;
            PurchaseSkinResponse response = JsonUtility.FromJson<PurchaseSkinResponse>(responseText);

            if (response == null)
            {
                Debug.LogError("[Store] Không parse được phản hồi mua skin.");
                return false;
            }

            if (!response.success)
            {
                Debug.LogWarning("[Store] Mua skin không thành công: " + response.message);
                return false;
            }

            Skin purchased = availableSkins.FirstOrDefault(s => s.Id == response.owned_skin_id);
            if (purchased != null)
            {
                purchased.IsOwned = true;
            }

            playerGold = response.remaining_gold;
            UpdateGoldUI();

            // Keep cross-scene player snapshot in sync right after successful purchase.
            await SupabaseManager.RefreshPlayerSnapshotAsync();

            Debug.Log($"[Store] Mua skin thành công. Gold còn lại: {response.remaining_gold}");
            return true;
        }
    }
}
