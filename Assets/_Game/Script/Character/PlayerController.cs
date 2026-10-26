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
    [SerializeField] public float remotePositionLerp = 15f;

    [Header("References")]
    [HideInInspector] public PlayerVisual playerVisual;
    [HideInInspector] public Rigidbody2D rb;
    [HideInInspector] public Bombit touchingBomb = null;

    [Header("Input")]
    public KeyCode inputUp = KeyCode.W;
    public KeyCode inputDown = KeyCode.S;
    public KeyCode inputLeft = KeyCode.A;
    public KeyCode inputRight = KeyCode.D;

    // =========================
    // 🔥 OPTIMIZED SYNC VARIABLES
    // =========================
    public struct InputPayload : INetworkSerializable
    {
        public int tick;
        public Vector2 direction;
        public Vector2 prevDir1;
        public Vector2 prevDir2;
        public Vector2 prevDir3;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref tick);
            serializer.SerializeValue(ref direction);
            serializer.SerializeValue(ref prevDir1);
            serializer.SerializeValue(ref prevDir2);
            serializer.SerializeValue(ref prevDir3);
        }
    }


    public struct StatePayload : INetworkSerializable, System.IEquatable<StatePayload>
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

    public NetworkVariable<StatePayload> serverState = new NetworkVariable<StatePayload>(
        new StatePayload(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

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

    private CircleCollider2D _circleCollider;
    private float _colliderRadius = 0.35f;
    private Vector2 _colliderOffset = Vector2.zero;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        // Sử dụng Kinematic hoàn toàn để code tự xử lý di chuyển và va chạm tất định
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.useFullKinematicContacts = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        _circleCollider = GetComponent<CircleCollider2D>();
        if (_circleCollider != null)
        {
            _circleCollider.radius *= 0.9f;
            _circleCollider.offset += new Vector2(0f, 0.07f);
            _colliderRadius = _circleCollider.radius;
            _colliderOffset = _circleCollider.offset;
        }
        else
        {
            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                _colliderRadius = Mathf.Min(col.bounds.extents.x, col.bounds.extents.y);
                _colliderOffset = col.offset;
            }
        }
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
        }

        if (IsServer)
        {
            var serverController = gameObject.AddComponent<ServerPlayerController>();
            serverController.Initialize(this);
        }

        var clientController = gameObject.AddComponent<ClientPlayerController>();
        clientController.Initialize(this);
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

        var clientController = GetComponent<ClientPlayerController>();
        if (clientController != null) Destroy(clientController);
        var serverController = GetComponent<ServerPlayerController>();
        if (serverController != null) Destroy(serverController);
    }
    public void OnPlayerLoadDoneChanged(bool oldValue, bool newValue)
    {
        PlayersManager.Instance?.CheckAllPlayersLoadDoneServerRpc();
    }
    private void Update()
    {
        CheckTouchingBomb();
    }

    private static readonly RaycastHit2D[] _circleCastHitBuffer = new RaycastHit2D[16];
    private static readonly Collider2D[] _overlapColliderBuffer = new Collider2D[8];
    private static readonly Collider2D[] _depenetrateColliderBuffer = new Collider2D[8];

    private void CheckTouchingBomb()
    {
        int bombLayerMask = LayerMask.GetMask("Bomb", "Default");
        int mask = bombLayerMask != 0 ? bombLayerMask : ~0;
        int hitCount = Physics2D.OverlapCircleNonAlloc(transform.position, 0.6f, _overlapColliderBuffer, mask);
        
        touchingBomb = null;
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D bombCol = _overlapColliderBuffer[i];
            if (bombCol != null && bombCol.gameObject.layer == 7 && !bombCol.isTrigger)
            {
                touchingBomb = bombCol.GetComponent<Bombit>();
                break;
            }
        }
    }

    public float GetPlayerColliderRadius()
    {
        if (_colliderRadius > 0f) return _colliderRadius;
        var circle = GetComponent<CircleCollider2D>();
        if (circle != null) return circle.radius;
        return 0.35f;
    }

    public Vector2 GetPlayerColliderOffset()
    {
        if (_colliderOffset != Vector2.zero) return _colliderOffset;
        var col = GetComponent<Collider2D>();
        return col != null ? col.offset : Vector2.zero;
    }

    /// <summary>
    /// Chống kẹt: Tự động phát hiện và đẩy tâm nhân vật ra ngoài nếu bị lún/chạm collider mép tường/bom
    /// </summary>
    private Vector2 ResolveDepenetration(Vector2 center, float radius, int mask)
    {
        int count = Physics2D.OverlapCircleNonAlloc(center, radius, _depenetrateColliderBuffer, mask);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = _depenetrateColliderBuffer[i];
            if (col == null || col.isTrigger) continue;
            if (col.transform == transform || col.transform.IsChildOf(transform)) continue;

            Vector2 closestPoint = col.ClosestPoint(center);
            Vector2 diff = center - closestPoint;
            float distSqr = diff.sqrMagnitude;

            // Nếu tâm nhân vật nằm ngoài nhưng hình tròn bị lún vào collider
            if (distSqr > 0.000001f && distSqr < radius * radius)
            {
                float dist = Mathf.Sqrt(distSqr);
                float penetration = radius - dist;
                Vector2 pushDir = diff / dist;
                center += pushDir * (penetration + 0.002f);
            }
            // Trường hợp tâm nhân vật bị lọt hẳn vào trong collider
            else if (distSqr <= 0.000001f)
            {
                Vector2 colCenter = col.bounds.center;
                Vector2 escapeDir = (center - colCenter).normalized;
                if (escapeDir == Vector2.zero) escapeDir = Vector2.up;
                center += escapeDir * (radius + 0.01f);
            }
        }
        return center;
    }

    private float CastCircleInDirection(Vector2 origin, float radius, Vector2 direction, float distance, int mask)
    {
        int hitCount = Physics2D.CircleCastNonAlloc(origin, radius, direction, _circleCastHitBuffer, distance, mask);
        float minDistance = distance;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit2D hit = _circleCastHitBuffer[i];
            if (hit.collider == null || hit.collider.isTrigger) continue;

            // Bỏ qua collider của bản thân nhân vật và các object con
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

            // CHỐNG KẸT: Nếu cast bắt đầu khi đang chạm bề mặt (distance <= 0)
            if (hit.distance <= 0f)
            {
                Vector2 outwardNormal = hit.normal != Vector2.zero ? hit.normal : (origin - hit.point).normalized;
                // Nếu đang di chuyển theo hướng THOÁT RA KHỎI collider (hướng di chuyển cùng chiều với normal)
                // => BỎ QUA hit này, cho phép nhân vật quay đầu thoát ra tự do!
                if (Vector2.Dot(direction, outwardNormal) > 0.01f)
                {
                    continue;
                }
            }

            if (hit.distance < minDistance)
            {
                minDistance = hit.distance;
            }
        }

        return minDistance;
    }

    public Vector2 SimulateStep(Vector2 currentPos, Vector2 dir, float deltaTime)
    {
        if (dir == Vector2.zero) return currentPos;

        Vector2 moveDirection = dir.normalized;
        float targetDistance = moveSpeed * deltaTime;
        if (targetDistance <= 0f) return currentPos;

        int obstacleLayerMask = LayerMask.GetMask("Stage", "Bomb", "Default");
        int mask = obstacleLayerMask != 0 ? obstacleLayerMask : ~0;

        float radius = GetPlayerColliderRadius();
        Vector2 offset = GetPlayerColliderOffset();
        Vector2 center = currentPos + offset;

        // 0. Giải phóng lún collider trước khi di chuyển (chống kẹt khi đứng sát mép)
        center = ResolveDepenetration(center, radius, mask);

        const float skinWidth = 0.005f;

        // 1. Thử di chuyển theo hướng chính (Primary direction)
        float hitDist = CastCircleInDirection(center, radius, moveDirection, targetDistance, mask);
        float allowedDist = Mathf.Max(0f, hitDist - skinWidth);
        Vector2 newCenter = center + moveDirection * allowedDist;
        float remainingDist = targetDistance - allowedDist;

        // 2. Nếu bị chặn và còn khoảng cách chưa đi (va chạm mép tường) => Kích hoạt Corner Slip (trượt góc)
        if (remainingDist > 0.001f)
        {
            Vector2 perpDir1 = Vector2.zero;
            Vector2 perpDir2 = Vector2.zero;

            // Xác định trục vuông góc dựa trên hướng di chuyển chính
            if (Mathf.Abs(moveDirection.y) > Mathf.Abs(moveDirection.x))
            {
                perpDir1 = Vector2.left;
                perpDir2 = Vector2.right;
            }
            else
            {
                perpDir1 = Vector2.down;
                perpDir2 = Vector2.up;
            }

            float probeOffset = radius * 0.75f;
            float probeCheckDistance = targetDistance + 0.05f;

            // Thăm dò xem 2 bên mép: bên nào thông thoáng theo hướng chính
            float clearDist1 = CastCircleInDirection(newCenter + perpDir1 * probeOffset, radius * 0.85f, moveDirection, probeCheckDistance, mask);
            float clearDist2 = CastCircleInDirection(newCenter + perpDir2 * probeOffset, radius * 0.85f, moveDirection, probeCheckDistance, mask);

            Vector2 chosenSlipDir = Vector2.zero;
            if (clearDist1 >= probeCheckDistance && clearDist2 < probeCheckDistance)
            {
                chosenSlipDir = perpDir1;
            }
            else if (clearDist2 >= probeCheckDistance && clearDist1 < probeCheckDistance)
            {
                chosenSlipDir = perpDir2;
            }

            if (chosenSlipDir != Vector2.zero)
            {
                // Di chuyển trượt theo hướng đã chọn
                float slipHitDist = CastCircleInDirection(newCenter, radius, chosenSlipDir, remainingDist, mask);
                float allowedSlipDist = Mathf.Max(0f, slipHitDist - skinWidth);
                newCenter += chosenSlipDir * allowedSlipDist;
            }
        }

        // 3. Đảm bảo vị trí cuối cùng không bị lún vào bất kỳ vật cản nào
        newCenter = ResolveDepenetration(newCenter, radius, mask);

        return newCenter - offset;
    }

    public void SimulateLocal(Vector2 dir)
    {
        if (dir == Vector2.zero) return;

        transform.position = SimulateStep(transform.position, dir, Time.fixedDeltaTime);
        if (rb != null)
        {
            rb.position = transform.position;
        }
    }

    [ServerRpc(Delivery = RpcDelivery.Unreliable)]
    public void SubmitInputServerRpc(InputPayload input)
    {
        var serverController = GetComponent<ServerPlayerController>();
        if (serverController != null)
        {
            serverController.OnInputReceived(input);
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
        var clientController = GetComponent<ClientPlayerController>();
        if (clientController != null) clientController.enabled = false;
        var serverController = GetComponent<ServerPlayerController>();
        if (serverController != null) serverController.enabled = false;

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

    [ServerRpc(RequireOwnership = false)]
    public void ForceServerSpawnStateServerRpc(Vector2 position, Quaternion rotation)
    {
        if (!IsServer) return;
        ForceServerSpawnStateClientRpc(position, rotation);

        var serverController = GetComponent<ServerPlayerController>();
        if (serverController != null)
        {
            serverController.ResetState(position);
        }

        StatePayload spawnState = new StatePayload
        {
            tick = 0,
            position = position,
            velocity = Vector2.zero
        };

        serverState.Value = spawnState;
    }
    [ClientRpc]
    public void ForceServerSpawnStateClientRpc(Vector2 position, Quaternion rotation){
        
        transform.SetPositionAndRotation(position, rotation);
        rb.position = position;
        rb.rotation = rotation.eulerAngles.z;
        rb.velocity = Vector2.zero;
        rb.angularVelocity = 0f;

        var clientController = GetComponent<ClientPlayerController>();
        if (clientController != null)
        {
            clientController.ResetPrediction(position);
        }
    }
}