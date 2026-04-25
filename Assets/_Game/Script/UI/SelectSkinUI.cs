using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.UI;

public class SelectSkinUI : MonoBehaviour
{
    [SerializeField]private SkinUI skinItemPrefab; // Prefab default cho item skin trong UI
    [SerializeField]private Transform skinListContainer; // Container để chứa các item skin trong UI
    [SerializeField]private Color equippedStatusColor = new Color(0.2f, 0.9f, 0.45f, 1f);
    [SerializeField]private Color ownedStatusColor = Color.white;
    [SerializeField] private Button selectButton; // Button để mở UI chọn skin
    [SerializeField] private Color selectedStatusColor = new Color(1f, 0.85f, 0.3f, 1f);
    [SerializeField] private float closeFadeDuration = 0.2f;

    private bool isEquipping;
    private CanvasGroup canvasGroup;
    private List<Skin> ownedSkins = new List<Skin>();
    private long currentEquippedSkinId;
    private long? pendingSelectedSkinId;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();

        if (selectButton != null)
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(() =>
            {
                _ = HandleSelectButtonClickedAsync();
            });
        }
    }

    private async void OnEnable()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        pendingSelectedSkinId = null;
        await LoadOwnedSkinsAsync();
    }

    public async Task LoadOwnedSkinsAsync()
    {
        if (isEquipping)
        {
            return;
        }

        if (skinItemPrefab == null || skinListContainer == null)
        {
            Debug.LogError("[SelectSkinUI] Thiếu skinItemPrefab hoặc skinListContainer.");
            return;
        }

        await SupabaseManager.EnsureInitializedAsync();

        if (!SupabaseManager.TryGetCachedPlayerSnapshot(out SupabaseManager.PlayerSnapshot snapshot))
        {
            var refreshedSnapshot = await SupabaseManager.RefreshPlayerSnapshotAsync();
            if (!refreshedSnapshot.HasValue)
            {
                Debug.LogWarning("[SelectSkinUI] Không lấy được player snapshot.");
                ClearSpawnedItems();
                return;
            }

            snapshot = refreshedSnapshot.Value;
        }

        if (snapshot.OwnedSkinIds == null || snapshot.OwnedSkinIds.Count == 0)
        {
            Debug.Log("[SelectSkinUI] Người chơi chưa sở hữu skin nào.");
            ClearSpawnedItems();
            return;
        }

        var client = SupabaseManager.Client;
        if (client == null)
        {
            Debug.LogError("[SelectSkinUI] Supabase client chưa được khởi tạo.");
            return;
        }

        List<Skin> allSkins = (await client.From<Skin>().Get()).Models;
        ownedSkins = allSkins
            .Where(skin => snapshot.OwnedSkinIds.Contains(skin.Id))
            .OrderBy(skin => skin.Id)
            .ToList();

        currentEquippedSkinId = snapshot.CurrentSkinId;
        RenderOwnedSkins();
        UpdateSelectButtonState();
    }

    private void RenderOwnedSkins()
    {
        ClearSpawnedItems();

        foreach (Skin skin in ownedSkins)
        {
            SkinUI item = Instantiate(skinItemPrefab, skinListContainer);
            bool isEquipped = skin.Id == currentEquippedSkinId;
            bool isSelected = pendingSelectedSkinId.HasValue && skin.Id == pendingSelectedSkinId.Value;
            string status = isEquipped ? "EQUIPPED" : (isSelected ? "SELECTED" : "OWNED");
            item.SetSkinPlayerLobbyUI((int)skin.Id, skin.Name, status);
            ApplyHighlight(item, isEquipped, isSelected);

            if (item.buyButton != null)
            {
                item.buyButton.gameObject.SetActive(true);
                item.buyButton.onClick.RemoveAllListeners();
                item.buyButton.interactable = !isEquipping;

                long skinIdToSelect = skin.Id;
                item.buyButton.onClick.AddListener(() =>
                {
                    SelectSkin(skinIdToSelect);
                });
            }
        }
    }

    private void SelectSkin(long skinId)
    {
        if (isEquipping)
        {
            return;
        }

        pendingSelectedSkinId = skinId;
        RenderOwnedSkins();
        UpdateSelectButtonState();
    }

    private void UpdateSelectButtonState()
    {
        if (selectButton == null)
        {
            return;
        }

        selectButton.interactable = !isEquipping && pendingSelectedSkinId.HasValue;
    }

    private async Task HandleSelectButtonClickedAsync()
    {
        if (isEquipping || !pendingSelectedSkinId.HasValue)
        {
            return;
        }

        long selectedSkinId = pendingSelectedSkinId.Value;
        if (selectedSkinId != currentEquippedSkinId)
        {
            await EquipSkinAndRefreshAsync(selectedSkinId);
            if (selectedSkinId != currentEquippedSkinId)
            {
                return;
            }
        }

        await FadeOutAndCloseAsync();
    }

    private async Task EquipSkinAndRefreshAsync(long skinId)
    {
        if (isEquipping)
        {
            return;
        }

        isEquipping = true;
        UpdateSelectButtonState();
        RenderOwnedSkins();

        bool equipped = await SupabaseManager.UpdateCurrentSkinIdAsync(skinId);
        if (!equipped)
        {
            isEquipping = false;
            UpdateSelectButtonState();
            RenderOwnedSkins();
            return;
        }

        currentEquippedSkinId = skinId;
        pendingSelectedSkinId = skinId;
        if (SupabaseManager.TryGetCurrentUserId(out string playerId))
        {
            SupabaseManager.SetCachedCurrentSkinId(playerId, skinId);
        }

        if (GameLobby.Instance != null)
        {
            bool lobbyUpdated = await GameLobby.Instance.SetLocalPlayerSkinAsync((int)skinId);
            if (lobbyUpdated && LobbyUI.Instance != null)
            {
                LobbyUI.Instance.UpdateLocalPlayerSkinUI((int)skinId);
            }
        }

        var refreshed = await SupabaseManager.RefreshPlayerSnapshotAsync();
        if (!refreshed.HasValue)
        {
            Debug.LogWarning("[SelectSkinUI] Đã cập nhật current_skin_id, nhưng refresh snapshot thất bại.");
        }

        if (refreshed.HasValue && refreshed.Value.CurrentSkinId == skinId)
        {
            currentEquippedSkinId = refreshed.Value.CurrentSkinId;
            pendingSelectedSkinId = currentEquippedSkinId;
        }
        else if (refreshed.HasValue)
        {
            Debug.LogWarning("[SelectSkinUI] Snapshot vừa refresh chưa phản ánh skin mới, giữ state local để tránh nhảy về skin cũ.");
        }

        isEquipping = false;
        RenderOwnedSkins();
        UpdateSelectButtonState();

        string authPlayerId = AuthenticationService.Instance.PlayerId;
        if (!string.IsNullOrEmpty(authPlayerId))
        {
            Debug.Log("[SelectSkinUI] Đã chốt skin cho player=" + authPlayerId + ", skinId=" + skinId);
        }
    }

    private async Task FadeOutAndCloseAsync()
    {
        if (canvasGroup == null)
        {
            gameObject.SetActive(false);
            return;
        }

        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        float duration = Mathf.Max(0.01f, closeFadeDuration);
        float elapsed = 0f;
        float startAlpha = canvasGroup.alpha;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            await Task.Yield();
        }

        canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
    }

    private void ApplyHighlight(SkinUI item, bool isEquipped, bool isSelected)
    {
        if (item.statusText != null)
        {
            item.statusText.color = isEquipped
                ? equippedStatusColor
                : (isSelected ? selectedStatusColor : ownedStatusColor);
            item.statusText.fontStyle = (isEquipped || isSelected)
                ? TMPro.FontStyles.Bold
                : TMPro.FontStyles.Normal;
        }

        if (item.skinImage != null)
        {
            item.skinImage.transform.localScale = (isEquipped || isSelected)
                ? Vector3.one * 1.05f
                : Vector3.one;
        }

        Image rootImage = item.GetComponent<Image>();
        if (rootImage != null)
        {
            rootImage.color = (isEquipped || isSelected)
                ? new Color(1f, 1f, 1f, 1f)
                : new Color(1f, 1f, 1f, 0.85f);
        }
    }

    private void ClearSpawnedItems()
    {
        for (int i = skinListContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(skinListContainer.GetChild(i).gameObject);
        }
    }
}
