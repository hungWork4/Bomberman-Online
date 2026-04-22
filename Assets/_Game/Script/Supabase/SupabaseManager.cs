using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Supabase;
using UnityEngine;
using UnityEngine.Networking;

public class SupabaseManager : MonoBehaviour
{
    public struct PlayerSnapshot
    {
        public string UserId;
        public string DisplayName;
        public long Gold;
        public long CurrentSkinId;
        public HashSet<long> OwnedSkinIds;
    }

    public static Client Client { get; private set; }
    public static string ProjectUrl { get; private set; }
    public static string AnonKey { get; private set; }
    public static string CurrentUserId { get; private set; }
    public static string CurrentAccessToken { get; private set; }
    public static bool HasPlayerSnapshot { get; private set; }
    public static PlayerSnapshot CurrentPlayerSnapshot { get; private set; }

    [SerializeField] private string supabaseUrl;
    [SerializeField] private string supabaseAnonKey;

    private static Task initializationTask;

    private async void Awake()
    {
        DontDestroyOnLoad(gameObject);
        await EnsureInitializedAsync(supabaseUrl, supabaseAnonKey);
    }

    public static bool TryGetCachedPlayerSnapshot(out PlayerSnapshot snapshot)
    {
        snapshot = CurrentPlayerSnapshot;
        return HasPlayerSnapshot;
    }

    public static async Task<PlayerSnapshot?> RefreshPlayerSnapshotAsync()
    {
        await EnsureInitializedAsync();

        if (!TryGetCurrentUserId(out string playerId))
        {
            Debug.LogWarning("[SupabaseManager] Chưa có userId để load player snapshot.");
            return null;
        }

        if (!TryGetAccessToken(out string accessToken))
        {
            Debug.LogWarning("[SupabaseManager] Chưa có access token để load player snapshot.");
            return null;
        }

        if (string.IsNullOrEmpty(ProjectUrl) || string.IsNullOrEmpty(AnonKey))
        {
            Debug.LogWarning("[SupabaseManager] Thiếu ProjectUrl/AnonKey để load player snapshot.");
            return null;
        }

        PlayerSnapshot snapshot = new PlayerSnapshot
        {
            UserId = playerId,
            DisplayName = string.Empty,
            Gold = 0,
            CurrentSkinId = 0,
            OwnedSkinIds = new HashSet<long>()
        };

        string encodedPlayerId = UnityWebRequest.EscapeURL(playerId);
        string baseUrl = ProjectUrl.TrimEnd('/');
        string profileUrl = baseUrl + "/rest/v1/player_profiles?select=display_name&id=eq." + encodedPlayerId + "&limit=1";
        string statsUrl = baseUrl + "/rest/v1/player_stats?select=gold,current_skin_id&id=eq." + encodedPlayerId + "&limit=1";
        string inventoryUrl = baseUrl + "/rest/v1/player_inventory?select=skin_id&player_id=eq." + encodedPlayerId;

        using (UnityWebRequest profileRequest = UnityWebRequest.Get(profileUrl))
        using (UnityWebRequest statsRequest = UnityWebRequest.Get(statsUrl))
        using (UnityWebRequest inventoryRequest = UnityWebRequest.Get(inventoryUrl))
        {
            SetupAuthHeaders(profileRequest, accessToken);
            SetupAuthHeaders(statsRequest, accessToken);
            SetupAuthHeaders(inventoryRequest, accessToken);

            var profileOperation = profileRequest.SendWebRequest();
            var statsOperation = statsRequest.SendWebRequest();
            var inventoryOperation = inventoryRequest.SendWebRequest();

            while (!profileOperation.isDone || !statsOperation.isDone || !inventoryOperation.isDone)
            {
                await Task.Yield();
            }

            if (!profileRequest.isNetworkError && !profileRequest.isHttpError)
            {
                Match nameMatch = Regex.Match(profileRequest.downloadHandler.text, "\"display_name\"\\s*:\\s*\"([^\"]*)\"");
                if (nameMatch.Success && nameMatch.Groups.Count > 1)
                {
                    snapshot.DisplayName = nameMatch.Groups[1].Value;
                }
            }

            if (!statsRequest.isNetworkError && !statsRequest.isHttpError)
            {
                Match goldMatch = Regex.Match(statsRequest.downloadHandler.text, "\"gold\"\\s*:\\s*(\\d+)");
                if (goldMatch.Success && long.TryParse(goldMatch.Groups[1].Value, out long g))
                {
                    snapshot.Gold = g;
                }

                Match skinIdMatch = Regex.Match(statsRequest.downloadHandler.text, "\"current_skin_id\"\\s*:\\s*(\\d+)");
                if (skinIdMatch.Success && long.TryParse(skinIdMatch.Groups[1].Value, out long s))
                {
                    snapshot.CurrentSkinId = s;
                }
            }

            if (!inventoryRequest.isNetworkError && !inventoryRequest.isHttpError)
            {
                MatchCollection matches = Regex.Matches(inventoryRequest.downloadHandler.text, "\"skin_id\"\\s*:\\s*(\\d+)");
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1 && long.TryParse(match.Groups[1].Value, out long skinId))
                    {
                        snapshot.OwnedSkinIds.Add(skinId);
                    }
                }
            }
        }

        CurrentPlayerSnapshot = snapshot;
        HasPlayerSnapshot = true;
        return snapshot;
    }

    private static void SetupAuthHeaders(UnityWebRequest request, string accessToken)
    {
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("apikey", AnonKey);
        request.SetRequestHeader("Authorization", "Bearer " + accessToken);
    }

    public static Task EnsureInitializedAsync(string url = null, string anonKey = null)
    {
        if (Client != null)
        {
            return Task.CompletedTask;
        }

        if (initializationTask != null)
        {
            return initializationTask;
        }

        initializationTask = InitializeInternalAsync(url, anonKey);
        return initializationTask;
    }

    private static async Task InitializeInternalAsync(string url, string anonKey)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(anonKey))
        {
            Debug.LogError("SupabaseManager thiếu supabaseUrl hoặc supabaseAnonKey trong Inspector.");
            return;
        }

        Client = new Client(url, anonKey);
        await Client.InitializeAsync();
        ProjectUrl = url;
        AnonKey = anonKey;
        Debug.Log("Supabase initialized.");
    }

    public static bool TryGetCurrentUserId(out string userId)
    {
        userId = null;

        if (!string.IsNullOrEmpty(CurrentUserId))
        {
            userId = CurrentUserId;
            return true;
        }

        if (Client != null)
        {
            object auth = GetPropertyValue(Client, "Auth");
            if (auth != null)
            {
                // Newer/older SDKs expose one of these paths.
                object currentUser = GetPropertyValue(auth, "CurrentUser") ?? GetPropertyValue(auth, "User");
                if (currentUser != null)
                {
                    string idFromUser = GetPropertyValue(currentUser, "Id") as string;
                    if (!string.IsNullOrEmpty(idFromUser))
                    {
                        userId = idFromUser;
                        return true;
                    }
                }

                object currentSession = GetPropertyValue(auth, "CurrentSession") ?? GetPropertyValue(auth, "Session");
                if (currentSession != null)
                {
                    object sessionUser = GetPropertyValue(currentSession, "User");
                    if (sessionUser != null)
                    {
                        string id = GetPropertyValue(sessionUser, "Id") as string;
                        if (!string.IsNullOrEmpty(id))
                        {
                            userId = id;
                            return true;
                        }
                    }
                }
            }
        }

        // Fallback for the login system that stores auth data in PlayerPrefs.
        string fallbackUserId = PlayerPrefs.GetString("SupabaseUserId", string.Empty);
        if (!string.IsNullOrEmpty(fallbackUserId))
        {
            CurrentUserId = fallbackUserId;
            userId = fallbackUserId;
            return true;
        }

        return false;
    }

    private static object GetPropertyValue(object instance, string propertyName)
    {
        if (instance == null || string.IsNullOrEmpty(propertyName))
        {
            return null;
        }

        var property = instance.GetType().GetProperty(propertyName);
        if (property == null)
        {
            return null;
        }

        return property.GetValue(instance, null);
    }

    public static bool TryGetAccessToken(out string accessToken)
    {
        accessToken = null;

        if (!string.IsNullOrEmpty(CurrentAccessToken))
        {
            accessToken = CurrentAccessToken;
            return true;
        }

        if (Client != null)
        {
            object auth = GetPropertyValue(Client, "Auth");
            if (auth != null)
            {
                object currentSession = GetPropertyValue(auth, "CurrentSession") ?? GetPropertyValue(auth, "Session");
                if (currentSession != null)
                {
                    string token = GetPropertyValue(currentSession, "AccessToken") as string;
                    if (!string.IsNullOrEmpty(token))
                    {
                        accessToken = token;
                        return true;
                    }
                }
            }
        }

        // Fallback for the login system that stores auth data in PlayerPrefs.
        string fallbackToken = PlayerPrefs.GetString("SupabaseAccessToken", string.Empty);
        if (!string.IsNullOrEmpty(fallbackToken))
        {
            CurrentAccessToken = fallbackToken;
            accessToken = fallbackToken;
            return true;
        }

        return false;
    }

    public static void SetAuthContext(string userId, string accessToken)
    {
        if (!string.IsNullOrEmpty(userId))
        {
            CurrentUserId = userId;
        }

        if (!string.IsNullOrEmpty(accessToken))
        {
            CurrentAccessToken = accessToken;
        }
    }

    public static void ClearAuthContext()
    {
        CurrentUserId = null;
        CurrentAccessToken = null;
        HasPlayerSnapshot = false;
        CurrentPlayerSnapshot = default;
    }
}
