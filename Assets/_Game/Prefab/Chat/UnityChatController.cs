using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Vivox;
using System.Threading.Tasks;

public class UnityChatController : MonoBehaviour
{
    [Header("UI Elements")]
    public TMP_InputField chatInputField;
    public Transform chatContentParent;
    public GameObject messagePrefab;
    public ScrollRect chatScrollRect;

    // [Header("Voice")]
    // public KeyCode pushToTalkKey = KeyCode.V; // Hold to talk
    // public bool micMutedByDefault = true;

    private string _roomName = "GlobalRoom";
    public static bool IsChatting = false;

    // Biến trạng thái để kiểm soát luồng
    private bool _isInitializing = false;
    private bool _isInRoom = false;

    // Internal state
    // private bool _micActive = false;

    async void Start()
    {
        // Đợi một chút để đảm bảo các dịch vụ hệ thống khác (như Authentication) đã ổn định
            await Task.Delay(1000); 
            
            if (chatInputField != null)
                chatInputField.onSubmit.AddListener(OnSubmit);

            DeactivateChat();

            // Chỉ chạy nếu Services đã được khởi tạo ở nơi khác (như GameLobby)
            if (UnityServices.State == ServicesInitializationState.Initialized)
            {
                await InitializeVivoxAsync();
            }
    }

    async Task InitializeVivoxAsync()
    {
        if (_isInitializing) return;
        _isInitializing = true;

        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            await VivoxService.Instance.InitializeAsync();

            // CHỈ Login nếu trạng thái hiện tại là LoggedOut
            if (!VivoxService.Instance.IsLoggedIn)
            {
                var loginOptions = new LoginOptions { DisplayName = "User_" + Random.Range(10, 99) };
                await VivoxService.Instance.LoginAsync(loginOptions);
            }

            VivoxService.Instance.ChannelMessageReceived += OnMessageReceived;

            // Tham gia phòng (bật cả text và audio)
            await VivoxService.Instance.JoinGroupChannelAsync(_roomName, ChatCapability.TextAndAudio);

            _isInRoom = true;

            // // Mute mic theo thiết lập mặc định
            // if (micMutedByDefault)
            // {
            //     VivoxService.Instance.MuteInputDevice();
            //     _micActive = false;
            // }
            // else
            // {
            //     VivoxService.Instance.UnmuteInputDevice();
            //     _micActive = true;
            // }

            AddMessageToUI("<color=yellow>Hệ thống: Đã kết nối!</color>");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Vivox Error]: {e.Message}");
        }
        finally
        {
            _isInitializing = false;
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (!IsChatting) ActivateChat();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (IsChatting) DeactivateChat();
        }

        // Push-to-talk: unmute while key held
        // if (_isInRoom)
        // {
        //     if (Input.GetKeyDown(pushToTalkKey))
        //     {
        //         try
        //         {
        //             VivoxService.Instance.UnmuteInputDevice();
        //             _micActive = true;
        //             AddMessageToUI("<color=green>Mic: On</color>");
        //         }
        //         catch (System.Exception e)
        //         {
        //             Debug.LogError("Vivox Unmute failed: " + e.Message);
        //         }
        //     }
        //     else if (Input.GetKeyUp(pushToTalkKey))
        //     {
        //         try
        //         {
        //             VivoxService.Instance.MuteInputDevice();
        //             _micActive = false;
        //             AddMessageToUI("<color=red>Mic: Off</color>");
        //         }
        //         catch (System.Exception e)
        //         {
        //             Debug.LogError("Vivox Mute failed: " + e.Message);
        //         }
        //     }
        // }
    }

    public void ActivateChat()
    {
        Debug.Log("Activating chat input field.");
        IsChatting = true;
        if (chatInputField != null) chatInputField.ActivateInputField();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void DeactivateChat()
    {
        IsChatting = false;
        if (chatInputField != null) chatInputField.DeactivateInputField();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public async void SendChatMessage()
    {
        // Kiểm tra xem đã vào phòng chưa và text có trống không
        if (!_isInRoom || chatInputField == null || string.IsNullOrEmpty(chatInputField.text))
        {
            DeactivateChat();
            return;
        }

        string messageToSend = chatInputField.text;
        chatInputField.text = ""; // Xóa text ngay lập tức để tránh nhấn Enter 2 lần

        try
        {
            await VivoxService.Instance.SendChannelTextMessageAsync(_roomName, messageToSend);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Gửi tin nhắn thất bại: " + e.Message);
        }

        DeactivateChat();
    }

    // Bridge for TMP_InputField.onSubmit so we can remove listener cleanly
    private void OnSubmit(string text)
    {
        SendChatMessage();
    }

    private void OnMessageReceived(VivoxMessage message)
    {
        AddMessageToUI($"<b>{message.SenderDisplayName}:</b> {message.MessageText}");
    }

    private void AddMessageToUI(string message)
    {
        if (messagePrefab == null || chatContentParent == null || chatScrollRect == null)
        {
            Debug.LogWarning("Chat UI references missing. Cannot display message.");
            return;
        }

        GameObject newMsg = Instantiate(messagePrefab, chatContentParent);
        var cm = newMsg.GetComponent<ChatMessage>();
        if (cm != null) cm.SetText(message);

        Canvas.ForceUpdateCanvases();
        chatScrollRect.verticalNormalizedPosition = 0f;
    }

    private async void OnDestroy()
    {
        if (chatInputField != null)
        {
            chatInputField.onSubmit.RemoveListener(OnSubmit);
        }

        try
        {
            if (VivoxService.Instance != null)
            {
                VivoxService.Instance.ChannelMessageReceived -= OnMessageReceived;

                if (_isInRoom)
                {
                    await VivoxService.Instance.LeaveChannelAsync(_roomName);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error during Vivox cleanup: " + e.Message);
        }
    }
}