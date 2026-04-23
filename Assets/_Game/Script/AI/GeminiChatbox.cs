using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

public class GeminiChatbox : MonoBehaviour
{
    private const string BombermanScopeInstruction =
        "Bạn là trợ lý AI cho game Bomberman Online. " +
        "Bạn chỉ được trả lời trong phạm vi luật chơi Bomberman sau: " +
        "Mỗi người chơi di chuyển 4 hướng bằng WASD, mục tiêu là sống sót cuối cùng. " +
        "Nhấn Space để đặt bom tại ô hiện tại (tọa độ làm tròn theo lưới). " +
        "Bom nổ sau khoảng 3 giây, vùng nổ lan hình dấu cộng (lên/xuống/trái/phải). " +
        "Nếu đứng trong vùng nổ thì nhân vật chết. " +
        "Phá block mềm có thể rơi item để tăng sức mạnh. " +
        "Dùng bom để mở đường, lấy item, và hạ đối thủ. " +
        "Chi tiết: " +
        "Luật đặt bom: mặc định mỗi người có 1 bom đặt đồng thời; bom nổ xong mới hồi lượt đặt bom; bán kính nổ mặc định 1 và tăng bằng item BlastRadius. " +
        "Luật nổ: nổ tâm + 4 hướng theo từng ô; gặp vật cản thuộc explosionLayerMask thì dừng; gặp bom khác thì nổ dây chuyền. " +
        "Luật phá map và rơi item: ô phá hủy được sẽ bị xóa tile; tỉ lệ rơi item mặc định 20%. " +
        "Luật item: ExtraBomb tăng số bom, BlastRadius tăng bán kính nổ, SpeedIncrease tăng tốc chạy. " +
        "Luật chết: chạm collider Explosion thì vào death sequence. " +
        "Luật thắng thua: nhánh classic còn <= 1 người sống thì sau 3 giây load round mới; nhánh netcode mới hiện chưa chốt winner đầy đủ như classic. " +
        "Nếu câu hỏi nằm ngoài phạm vi này, hãy trả lời ngắn gọn: 'Mình chỉ hỗ trợ chủ đề luật chơi Bomberman ở phòng chat này.'";

    [Header("UI References")]
    public TMP_InputField chatInputField;
    public Transform chatContentParent;
    public GameObject messagePrefab;
    public ScrollRect chatScrollRect;


    public static bool IsChatting = false;

    // Biến trạng thái để kiểm soát luồng
    private bool _isInitializing = false;
    private bool _isInRoom = false;

    [Header("Scroll Settings")]
    [Range(0f, 0.2f)]
    public float autoScrollThreshold = 0.03f;

    [Header("Message Bubble")]
    [Min(50)]
    public int maxCharactersPerMessagePrefab = 220;

    private bool shouldAutoScroll = true;
    private bool suppressScrollEvent;

    [Header("API Settings")]
    [Tooltip("Dán API Key của bạn vào đây")]
    public string apiKey = "YOUR_GEMINI_API_KEY"; 
    
    // Đã đổi sang gemini-1.5-flash-latest để khắc phục lỗi 404 (Model Not Found)
   private string apiUrl = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite-preview:generateContent?key=";

    private ChatMessage pendingBotMessage;

    void Start()
    {
        _isInitializing = true;
        if (chatScrollRect != null)
        {
            chatScrollRect.onValueChanged.AddListener(OnScrollValueChanged);
        }

        _isInRoom = true;
        _isInitializing = false;

        AddChatMessage("Hệ thống: Bắt đầu trò chuyện với AI!");
        StartCoroutine(ScrollToBottomNextFrame());
    }

    void OnDestroy()
    {
        if (chatScrollRect != null)
        {
            chatScrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
        }
    }

    void Update()
    {
        IsChatting = chatInputField != null && chatInputField.isFocused;

        if (!_isInRoom || _isInitializing)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            OnSendClick();
        }
    }

    public void OnSendClick()
    {
        if (chatInputField == null)
        {
            Debug.LogError("chatInputField is not assigned.");
            return;
        }

        string message = chatInputField.text.Trim();
        if (string.IsNullOrEmpty(message)) return;

        AddChatMessage("<color=#00FF00>Bạn:</color> " + message);
        chatInputField.text = "";

        StartCoroutine(SendToGemini(message));
    }

    ChatMessage AddChatMessage(string message, bool splitIfLong = true)
    {
        if (splitIfLong && !string.IsNullOrEmpty(message))
        {
            ChatMessage lastMessage = null;
            foreach (string line in SplitByNewLine(message))
            {
                // Keep empty lines as a visible spacer while still creating a prefab per newline.
                string normalizedLine = string.IsNullOrEmpty(line) ? " " : line;

                if (normalizedLine.Length > maxCharactersPerMessagePrefab)
                {
                    foreach (string chunk in SplitMessageIntoChunks(normalizedLine, maxCharactersPerMessagePrefab))
                    {
                        lastMessage = CreateMessageItem(chunk);
                    }
                }
                else
                {
                    lastMessage = CreateMessageItem(normalizedLine);
                }
            }

            return lastMessage;
        }

        return CreateMessageItem(message);
    }

    IEnumerable<string> SplitByNewLine(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            yield return message;
            yield break;
        }

        string normalized = message.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            yield return lines[i];
        }
    }

    ChatMessage CreateMessageItem(string message)
    {
        if (messagePrefab == null || chatContentParent == null)
        {
            Debug.LogError("messagePrefab or chatContentParent is not assigned.");
            return null;
        }

        GameObject messageObj = Instantiate(messagePrefab, chatContentParent);
        ChatMessage chatMessage = messageObj.GetComponent<ChatMessage>();
        if (chatMessage == null)
        {
            Debug.LogError("messagePrefab does not contain ChatMessage component.");
            Destroy(messageObj);
            return null;
        }

        chatMessage.SetText(message);
        Canvas.ForceUpdateCanvases();
        if (chatContentParent is RectTransform parentRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
        }

        if (shouldAutoScroll)
        {
            StartCoroutine(ScrollToBottomNextFrame());
        }

        return chatMessage;
    }

    IEnumerable<string> SplitMessageIntoChunks(string message, int maxLength)
    {
        if (string.IsNullOrEmpty(message) || maxLength <= 0)
        {
            yield return message;
            yield break;
        }

        int start = 0;
        while (start < message.Length)
        {
            int remaining = message.Length - start;
            if (remaining <= maxLength)
            {
                yield return message.Substring(start).Trim();
                yield break;
            }

            int splitAt = start + maxLength;
            int lastSpace = message.LastIndexOf(' ', splitAt, maxLength);
            if (lastSpace > start + (maxLength / 2))
            {
                splitAt = lastSpace;
            }

            string chunk = message.Substring(start, splitAt - start).Trim();
            if (!string.IsNullOrEmpty(chunk))
            {
                yield return chunk;
            }

            start = splitAt;
            while (start < message.Length && message[start] == ' ')
            {
                start++;
            }
        }
    }

    void OnScrollValueChanged(Vector2 _)
    {
        if (suppressScrollEvent || chatScrollRect == null)
        {
            return;
        }

        if (!IsScrollContentLargerThanViewport())
        {
            shouldAutoScroll = true;
            return;
        }

        // In ScrollRect, verticalNormalizedPosition ~= 0 means bottom.
        shouldAutoScroll = chatScrollRect.verticalNormalizedPosition <= autoScrollThreshold;
    }

    bool IsScrollContentLargerThanViewport()
    {
        if (chatScrollRect == null || chatScrollRect.content == null || chatScrollRect.viewport == null)
        {
            return false;
        }

        return chatScrollRect.content.rect.height > chatScrollRect.viewport.rect.height + 1f;
    }

    IEnumerator ScrollToBottomNextFrame()
    {
        if (chatScrollRect == null)
        {
            yield break;
        }

        yield return null;
        Canvas.ForceUpdateCanvases();

        suppressScrollEvent = true;
        chatScrollRect.verticalNormalizedPosition = 0f;
        suppressScrollEvent = false;
    }

    IEnumerator SendToGemini(string prompt)
    {
        pendingBotMessage = AddChatMessage("<color=#999999>ADMIN đang gõ...</color>", false);

        string requestUrl = apiUrl + apiKey.Trim();

        GeminiRequestData requestData = new GeminiRequestData
        {
            contents = new ContentData[]
            {
                new ContentData
                {
                    parts = new PartData[]
                    {
                        new PartData { text = BuildScopedPrompt(prompt) }
                    }
                }
            }
        };
        
        string jsonPayload = JsonUtility.ToJson(requestData);

        using (UnityWebRequest request = new UnityWebRequest(requestUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (pendingBotMessage != null)
            {
                Destroy(pendingBotMessage.gameObject);
                pendingBotMessage = null;
            }

            if (request.result == UnityWebRequest.Result.Success)
            {
                string jsonResponse = request.downloadHandler.text;
                string replyText = ExtractTextFromJson(jsonResponse);
                AddChatMessage("<color=#00BFFF>ADMIN:</color> " + replyText);
            }
            else
            {
                // BẮT LỖI CHI TIẾT TỪ GOOGLE
                // Khi Google báo lỗi 404/400, họ luôn trả về một JSON giải thích nguyên nhân nằm trong downloadHandler
                string errorDetail = request.downloadHandler != null ? request.downloadHandler.text : "Không có dữ liệu phản hồi từ server";
                
                AddChatMessage("<color=#FF0000>Lỗi:</color> Không thể kết nối. (Mã lỗi: " + request.responseCode + ")");
                
                // In ra Console để phân tích chính xác lý do
                Debug.LogError("Lỗi API: " + request.error + 
                               "\nURL Đã gọi: " + requestUrl + 
                               "\nChi tiết từ Google: " + errorDetail);
            }
        }
    }

    private string BuildScopedPrompt(string userPrompt)
    {
        return BombermanScopeInstruction + "\n\nNgười chơi hỏi: " + userPrompt;
    }

    private string ExtractTextFromJson(string json)
    {
        try
        {
            GeminiResponse response = JsonUtility.FromJson<GeminiResponse>(json);
            return response.candidates[0].content.parts[0].text;
        }
        catch (System.Exception e)
        {
            Debug.LogError("Lỗi Parse JSON: " + e.Message + "\nJSON nhận được: " + json);
            return "Không thể đọc dữ liệu phản hồi từ AI.";
        }
    }
}

// --- CÁC CLASS ĐỂ XỬ LÝ JSON ---
[System.Serializable]
public class GeminiRequestData { public ContentData[] contents; }

[System.Serializable]
public class GeminiResponse { public Candidate[] candidates; }

[System.Serializable]
public class Candidate { public ContentData content; }

[System.Serializable]
public class ContentData { public PartData[] parts; }

[System.Serializable]
public class PartData { public string text; }