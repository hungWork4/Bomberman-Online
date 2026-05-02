using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.AddressableAssets;
using Unity.Collections;
using System.Threading.Tasks;
using UnityEngine.SocialPlatforms.Impl;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private NetworkVariable<float> _movespeed = new NetworkVariable<float>(5f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public float moveSpeed { get => _movespeed.Value; set => _movespeed.Value = value; }
    [SerializeField] private NetworkVariable<int> _life = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public int Life { get => _life.Value; set => _life.Value = value; }

    [Header("Remote Sync")]
    [SerializeField] private float remotePositionLerp = 15f;

    [Header("References")]
    private PlayerVisual playerVisual;
    private Rigidbody2D rb;
    private Vector2 currentInput;
    private Vector2 lastSentInput;// client
    private Vector2 hostLastInput;
    [Header("Input")]
    public KeyCode inputUp = KeyCode.W;
    public KeyCode inputDown = KeyCode.S;
    public KeyCode inputLeft = KeyCode.A;
    public KeyCode inputRight = KeyCode.D;

    // =========================
    // 🔥 OPTIMIZED SYNC VARIABLES
    // =========================
    private struct InputPayload : INetworkSerializable
    {
        public int tick;
        public Vector2 direction;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref tick);
            serializer.SerializeValue(ref direction);
        }
    }

    private struct StatePayload : INetworkSerializable, System.IEquatable<StatePayload>
    {
        public int tick;
        public Vector2 position;
        public Vector2 velocity;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref tick);
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref velocity);
        }

        public bool Equals(StatePayload other)
        {
            return tick == other.tick && position == other.position && velocity == other.velocity;
        }
    }

    private NetworkVariable<StatePayload> serverState = new NetworkVariable<StatePayload>(
        new StatePayload(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private List<InputPayload> inputBuffer = new List<InputPayload>();
    private int localTick;
    private StatePayload lastProcessedState;
    private Bombit touchingBomb = null;
    
    private Vector2 _lastServerPosition;

    private NetworkVariable<FixedString64Bytes> _currentSkinKey = new NetworkVariable<FixedString64Bytes>(
        "", 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );
    public NetworkVariable<long> _currentSkinID = new NetworkVariable<long>(
        -1, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Server
    );
    public NetworkVariable<FixedString32Bytes> _CurrentPlayerName = new NetworkVariable<FixedString32Bytes>(
        "", 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Owner
    );
    public NetworkVariable<bool> isPlayerLoadDone = new NetworkVariable<bool>(
        false, 
        NetworkVariableReadPermission.Everyone, 
        NetworkVariableWritePermission.Owner
    );
    private AsyncOperationHandle<GameObject> _currentSkinHandle;
    private string _lastSkinKey = "";

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        // Đảm bảo Rigidbody là Dynamic để va chạm hoạt động tốt
        rb.bodyType = RigidbodyType2D.Dynamic;
    }
    // private void Start() {
    //     if (NetworkManager.Singleton != null) {
    //         NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    //     }
    // }

    // private void OnClientDisconnected(ulong clientId) {
    //     Debug.Log($"Client {clientId} đã thoát hoặc mất kết nối.");
    // }
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            _CurrentPlayerName.Value = getPlayerName();
        }
        isPlayerLoadDone.OnValueChanged += OnPlayerLoadDoneChanged;
        _currentSkinKey.OnValueChanged += OnSkinKeyChanged;
        _currentSkinID.OnValueChanged += OnSkinIDChanged;
        if (IsOwner)
        {
            _ = SubmitInitialSkinForOwnerAsync();
        }
        string initialSkinKey = _currentSkinKey.Value.ToString();
        if (!string.IsNullOrEmpty(initialSkinKey))
        {
            ChangeSkinByKey(initialSkinKey);
        }
        // ScoreBoardController.Instance?.SpawnPlayerCard(this);
        // ScoreBoardController.Instance?.UpdatePlayerCard(this);
        if (_currentSkinID.Value != -1)
        {
            InitializeScoreboard();
        }
        _life.OnValueChanged += (oldValue, newValue) => ScoreBoardController.Instance?.UpdatePlayerCard(this);
        _movespeed.OnValueChanged += (oldValue, newValue) => ScoreBoardController.Instance?.UpdatePlayerCard(this);
        _CurrentPlayerName.OnValueChanged += (oldValue, newValue) => ScoreBoardController.Instance?.UpdatePlayerCard(this);
        Vector2 spawnedPosition = transform.position;
        rb.position = spawnedPosition;

        if (IsServer)
        {
            serverState.Value = new StatePayload
            {
                tick = 0,
                position = spawnedPosition,
                velocity = Vector2.zero
            };
            _lastServerPosition = spawnedPosition;
        }
        lastProcessedState = serverState.Value;
        // if(IsOwner)
        // Debug.Log("ten nguoi choi: " + SupabaseManager.CurrentPlayerSnapshot.DisplayName);
    }
    private void OnSkinIDChanged(long oldValue, long newValue)
    {
        if (newValue != -1)
        {
            InitializeScoreboard();
        }
    }

    private void InitializeScoreboard()
    {
        // Đảm bảo ScoreBoard đã sẵn sàng
        if (ScoreBoardController.Instance != null)
        {
            ScoreBoardController.Instance.SpawnPlayerCard(this);
            ScoreBoardController.Instance.UpdatePlayerCard(this);
        }
    }
    // private void OnSkinIDChanged(long oldValue, long newValue)
    // {
    //     // Mỗi khi ID thay đổi, đảm bảo Card đã tồn tại và cập nhật
    //     ScoreBoardController.Instance?.SpawnPlayerCard(this);
    //     ScoreBoardController.Instance?.UpdatePlayerCard(this);
    // }
    public string getPlayerName()
    {
        return SupabaseManager.TryGetCachedPlayerSnapshot(out SupabaseManager.PlayerSnapshot snapshot) ? snapshot.DisplayName : "Player";
    }
    private async Task SubmitInitialSkinForOwnerAsync()
    {
        long currentSkinId = 0;

        if (SupabaseManager.TryGetCachedPlayerSnapshot(out SupabaseManager.PlayerSnapshot snapshot))
        {
            currentSkinId = snapshot.CurrentSkinId;
        }
        else
        {
            var refreshedSnapshot = await SupabaseManager.RefreshPlayerSnapshotAsync();
            if (refreshedSnapshot.HasValue)
            {
                currentSkinId = refreshedSnapshot.Value.CurrentSkinId;
            }
        }
        // _currentSkinID.Value = currentSkinId;
        SubmitCurrentSkinServerRpc(currentSkinId);
    }

    [ServerRpc]
    private void SubmitCurrentSkinServerRpc(long skinId)
    {
        _currentSkinID.Value = skinId;
        _ = SubmitCurrentSkinServerAsync(skinId);
    }

    private async Task SubmitCurrentSkinServerAsync(long skinId)
    {
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        if (skinId < 0)
        {
            skinId = 0;
        }

        await InitializeSkinFromSupabaseAsync(skinId);
    }

    private async Task InitializeSkinFromSupabaseAsync(long currentSkinId)
    {
        string skinKey = await SupabaseManager.GetSkinKeyByIdAsync(currentSkinId);
        if (!IsServer || !IsSpawned)
        {
            return;
        }

        _currentSkinKey.Value = skinKey;
    }

    public override void OnNetworkDespawn()
    {
        Debug.Log($"PlayerController for client {_CurrentPlayerName.Value} is despawning. Cleaning up resources.");
        _currentSkinKey.OnValueChanged -= OnSkinKeyChanged;
        _currentSkinKey.OnValueChanged -= OnSkinKeyChanged;
        _currentSkinID.OnValueChanged -= OnSkinIDChanged;
        isPlayerLoadDone.OnValueChanged -= OnPlayerLoadDoneChanged;

        _life.OnValueChanged -= (oldValue, newValue) => ScoreBoardController.Instance?.UpdatePlayerCard(this);
        _movespeed.OnValueChanged -= (oldValue, newValue) => ScoreBoardController.Instance?.UpdatePlayerCard(this);
        _CurrentPlayerName.OnValueChanged -= (oldValue, newValue) => ScoreBoardController.Instance?.UpdatePlayerCard(this);
        if (_currentSkinHandle.IsValid())
        {
            Addressables.ReleaseInstance(_currentSkinHandle);
        }
    }
    public void OnPlayerLoadDoneChanged(bool oldValue, bool newValue)
    {
        PlayersManager.Instance?.CheckAllPlayersLoadDoneServerRpc();
    }
    private void Update()
    {
        if (!IsSpawned) return;

        if (IsOwner && isPlayerLoadDone.Value == true)
        {
            currentInput = Vector2.zero;
            if(UnityChatController.IsChatting == false)
            {
                if (Input.GetKey(inputUp)) currentInput = Vector2.up;
                else if (Input.GetKey(inputDown)) currentInput = Vector2.down;
                else if (Input.GetKey(inputLeft)) currentInput = Vector2.left;
                else if (Input.GetKey(inputRight)) currentInput = Vector2.right;
            }
            if (IsServer)
                hostLastInput = currentInput;
            
            playerVisual?.SetMovement(currentInput);

            if (Input.GetKeyDown(KeyCode.LeftShift) && touchingBomb != null&& UnityChatController.IsChatting == false)
            {
                touchingBomb.RequestPushServerRpc(playerVisual.lastDirection);
            }

        }
        else
        {
            // Chỉ cập nhật visual (Animation) trong Update, việc di chuyển nhường cho FixedUpdate
            StatePayload targetState = serverState.Value;
            Vector2 visualDirection = targetState.velocity.sqrMagnitude > 0f ? targetState.velocity.normalized : Vector2.zero;
            playerVisual?.SetMovement(visualDirection);
        }
    }

    private void FixedUpdate()
    {
        if (!IsSpawned) return;

        if (IsOwner)
        {
            localTick++;
            InputPayload inputPayload = new InputPayload { tick = localTick, direction = currentInput };

            if (IsServer)
            {
                // LÀ HOST
                SimulateLocal(inputPayload.direction);
                // UpdateServerState(localTick, inputPayload.direction);
                UpdateServerState(localTick, hostLastInput);

            }
            else
            {
                // LÀ CLIENT
                inputBuffer.Add(inputPayload);
                SimulateLocal(inputPayload.direction);

                if (currentInput != lastSentInput)
                {
                    SubmitInputServerRpc(inputPayload);
                    lastSentInput = currentInput;
                }
                
                Reconcile();
            }
        }
        else 
        {
            if (IsServer)
            {
                // Server tiếp tục mô phỏng hành động cuối cùng của Client
                SimulateLocal(lastSentInput); 
                UpdateServerState(lastProcessedState.tick, lastSentInput);
            }
            else
            {
                // 🔥 [FIX QUAN TRỌNG] Các Client khác nội suy vị trí bằng vật lý (MovePosition)
                StatePayload targetState = serverState.Value;
                float distance = Vector2.Distance(rb.position, targetState.position);

                if (targetState.velocity.sqrMagnitude < 0.0001f && distance < 0.02f)
                {
                    // Đã đến nơi thì Snap để tránh trượt băng
                    rb.MovePosition(targetState.position);
                }
                else
                {
                    // Dự đoán trước 1 tick + bám theo tốc độ động để giảm cảm giác delay khi xem host từ client
                    Vector2 predictedPos = targetState.position + (targetState.velocity * Time.fixedDeltaTime);
                    float followSpeed = Mathf.Max(targetState.velocity.magnitude, distance * remotePositionLerp);
                    Vector2 nextPos = Vector2.MoveTowards(rb.position, predictedPos, followSpeed * Time.fixedDeltaTime);
                    rb.MovePosition(nextPos);
                }
            }
        }
    }

    private void UpdateServerState(int tick, Vector2 direction)
    {
        if (!IsServer) return;

        Vector2 vel = direction * moveSpeed;

        // Always push state when velocity changes (especially moving -> idle)
        // so remote clients can stop run animation immediately.
        bool positionChanged = Vector2.Distance(rb.position, _lastServerPosition) > 0.01f;
        bool velocityChanged = serverState.Value.velocity != vel;

        if (positionChanged || velocityChanged)
        {
            serverState.Value = new StatePayload
            {
                tick = tick,
                position = rb.position,
                velocity = vel
            };
            _lastServerPosition = rb.position;
        }
    }

    private void SimulateLocal(Vector2 dir)
    {
        Vector2 vel = dir * moveSpeed;
        // Dùng rb.MovePosition để chạy mô phỏng di chuyển đúng chuẩn vật lý của Unity
        rb.MovePosition(rb.position + vel * Time.fixedDeltaTime);
    }

    [ServerRpc]
    private void SubmitInputServerRpc(InputPayload input)
    {
        lastSentInput = input.direction;
        lastProcessedState.tick = input.tick;
    }

    private void Reconcile()
    {
        StatePayload latestServerState = serverState.Value;

        if (latestServerState.tick == 0 || latestServerState.tick == lastProcessedState.tick) return;

        lastProcessedState = latestServerState;
        inputBuffer.RemoveAll(i => i.tick <= latestServerState.tick);

        Vector2 expectedPos = latestServerState.position;

        foreach (var input in inputBuffer)
        {
            expectedPos += (input.direction * moveSpeed) * Time.fixedDeltaTime;
        }

        float error = Vector2.Distance(rb.position, expectedPos);
        float snapThreshold = 1.0f;

        if (error > 0.05f) 
        {
            if (error > snapThreshold)
            {
                rb.position = expectedPos; // Sai số quá lớn -> Dịch chuyển thẳng
            }
            else
            {
                // Kéo mượt về vị trí đúng nhưng vẫn thông qua Rigidbody
                rb.MovePosition(Vector2.Lerp(rb.position, expectedPos, 0.5f));
            }
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.layer == 7 && collision.collider.isTrigger == false)
        {
            touchingBomb = collision.gameObject.GetComponent<Bombit>();
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.layer == 7 && collision.collider.isTrigger == false)
        {
            touchingBomb = null;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer) return; 

        if (other.gameObject.layer == LayerMask.NameToLayer("Explosion"))
        {
            if (_life.Value <= 0)
            {
                return;
            }

            _life.Value = 0;
            DeathSequenceClientRpc();
            PlayersManager.Instance?.CheckWinStateServer();
        }
    }

    [ClientRpc]
    private void DeathSequenceClientRpc()
    {
        enabled = false;
        var bombController = GetComponent<BombController>();
        if (bombController != null) bombController.enabled = false;

        playerVisual?.SetSortingOrder(1);
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;
        ScoreBoardController.Instance?.playerCardsDict[OwnerClientId]?.SetPlayerLeave("DEAD...");
        // ScoreBoardController.Instance?.playerCardsDict.Remove(OwnerClientId);
        playerVisual?.SetDead(true);
        Invoke(nameof(OnDeathSequenceEnded), 1.25f);
    }

    private void OnDeathSequenceEnded()
    {
        Debug.Log("Player has died.");
    }

    private async void ChangeSkinByKey(string skinKey)
    {
        if (string.IsNullOrEmpty(skinKey) || skinKey == _lastSkinKey) return;

        if (_currentSkinHandle.IsValid())
        {
            Addressables.ReleaseInstance(_currentSkinHandle);
        }

        _lastSkinKey = skinKey;
        _currentSkinHandle = Addressables.InstantiateAsync(skinKey, this.transform);

        await _currentSkinHandle.Task;

        if (_currentSkinHandle.Status == AsyncOperationStatus.Succeeded)
        {
            GameObject skinObj = _currentSkinHandle.Result;
            skinObj.transform.localPosition = Vector3.zero;
            skinObj.transform.localRotation = Quaternion.identity;

            playerVisual = skinObj.GetComponentInChildren<PlayerVisual>(true);
            if(IsOwner)
            {
                isPlayerLoadDone.Value = true;
                Debug.Log($"Skin changed to {skinKey} for player {OwnerClientId}");
            }
        }
        else
        {
            Debug.LogError($"Lỗi load Addressable: {skinKey}");
            _lastSkinKey = "";
        }
    }

    public void RequestChangeSkin(string newKey)
    {
        if (!IsServer) return;
        _currentSkinKey.Value = newKey;
    }

    private void OnSkinKeyChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
    {
        ChangeSkinByKey(newValue.ToString());
    }

    [ServerRpc(RequireOwnership =false)]
    public void ForceServerSpawnStateServerRpc(Vector2 position, Quaternion rotation)
    {
        if (!IsServer) return;
        ForceServerSpawnStateClientRpc(position, rotation);
        currentInput = Vector2.zero;
        inputBuffer.Clear();

        StatePayload spawnState = new StatePayload
        {
            tick = localTick,
            position = position,
            velocity = Vector2.zero
        };

        serverState.Value = spawnState;
        lastProcessedState = spawnState;
    }
    [ClientRpc]
    public void ForceServerSpawnStateClientRpc(Vector2 position, Quaternion rotation){
        
        transform.SetPositionAndRotation(position, rotation);
        rb.position = position;
        rb.rotation = rotation.eulerAngles.z;
        rb.velocity = Vector2.zero;
        rb.angularVelocity = 0f;


    }
}