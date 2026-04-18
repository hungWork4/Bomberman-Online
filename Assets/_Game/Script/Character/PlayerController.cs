using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField]private NetworkVariable<float> _movespeed = new NetworkVariable<float>(5f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public float moveSpeed { get => _movespeed.Value; set => _movespeed.Value = value; }
    [Header("Remote Sync")]
    [SerializeField] private float remotePositionLerp = 16f;

    [Header("References")]
    [SerializeField] private PlayerVisual playerVisual;

    private Rigidbody2D rb;
    private Vector2 currentInput;

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

    // Sử dụng NetworkVariable để tránh Spam RPC và tự động đồng bộ cho người vào sau (Late-join)
    private NetworkVariable<StatePayload> serverState = new NetworkVariable<StatePayload>(
        new StatePayload(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private List<InputPayload> inputBuffer = new List<InputPayload>();
    private int localTick;
    private StatePayload lastProcessedState;
    private Bombit touchingBomb = null;
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.layer == 7 && collision.collider.isTrigger==false)
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
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        if (playerVisual == null)
        {
            playerVisual = GetComponentInChildren<PlayerVisual>(true);
        }

        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    public override void OnNetworkSpawn()
    {
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

        lastProcessedState = serverState.Value;
    }

    public void ForceServerSpawnState(Vector2 position, Quaternion rotation)
    {
        if (!IsServer)
        {
            return;
        }

        transform.SetPositionAndRotation(position, rotation);
        rb.position = position;
        rb.rotation = rotation.eulerAngles.z;
        rb.velocity = Vector2.zero;
        rb.angularVelocity = 0f;

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

    private void Update()
    {
        if (!IsSpawned) return;

        if (IsOwner)
        {
            currentInput = Vector2.zero;
            if (Input.GetKey(inputUp)) currentInput = Vector2.up;
            else if (Input.GetKey(inputDown)) currentInput = Vector2.down;
            else if (Input.GetKey(inputLeft)) currentInput = Vector2.left;
            else if (Input.GetKey(inputRight)) currentInput = Vector2.right;

            playerVisual?.SetMovement(currentInput);

            if (Input.GetKeyDown(KeyCode.LeftShift) && touchingBomb != null)
            {
                touchingBomb.RequestPushServerRpc(playerVisual.lastDirection);
            }
        }
        else
        {
            // 🔥 SMOOTHING CHO CÁC CLIENT KHÁC
            rb.position = Vector2.Lerp(rb.position, serverState.Value.position, remotePositionLerp * Time.deltaTime);

            Vector2 visualDirection = serverState.Value.velocity.sqrMagnitude > 0f ? serverState.Value.velocity.normalized : Vector2.zero;
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
                // LÀ HOST: Vừa là Server vừa là Client, xử lý luôn không cần dùng RPC
                SimulateLocal(inputPayload.direction);
                serverState.Value = new StatePayload
                {
                    tick = localTick,
                    position = rb.position,
                    velocity = inputPayload.direction * moveSpeed
                };
            }
            else
            {
                // LÀ CLIENT: Predict, Gửi Input, Reconcile
                inputBuffer.Add(inputPayload);
                SimulateLocal(inputPayload.direction);

                SubmitInputServerRpc(inputPayload);
                Reconcile();
            }
        }
    }

    private void SimulateLocal(Vector2 dir)
    {
        Vector2 vel = dir * moveSpeed;
        // Dùng cộng position thay vì rb.MovePosition để toán học đồng bộ chuẩn xác trong lúc Replay
        rb.position += vel * Time.fixedDeltaTime;
    }

    [ServerRpc]
    private void SubmitInputServerRpc(InputPayload input)
    {
        // Server xử lý input của Client
        Vector2 vel = input.direction * moveSpeed;
        rb.position += vel * Time.fixedDeltaTime;

        serverState.Value = new StatePayload
        {
            tick = input.tick,
            position = rb.position,
            velocity = vel
        };
    }

    // 🔥 CORE RECONCILE
    private void Reconcile()
    {
        StatePayload latestServerState = serverState.Value;

        // Chưa có dữ liệu mới từ server
        if (latestServerState.tick == 0 || latestServerState.tick == lastProcessedState.tick) return;

        lastProcessedState = latestServerState;

        // Xóa các input đã được server xử lý thành công
        inputBuffer.RemoveAll(i => i.tick <= latestServerState.tick);

        // Tính toán vị trí đáng lẽ phải ở đó theo góc nhìn xác thực từ server
        Vector2 expectedPos = latestServerState.position;

        // Replay lại các input mà server chưa kịp xử lý
        foreach (var input in inputBuffer)
        {
            expectedPos += (input.direction * moveSpeed) * Time.fixedDeltaTime;
        }

        float error = Vector2.Distance(rb.position, expectedPos);
        float snapThreshold = 1.0f;

        if (error > 0.05f) // Bỏ qua sai số siêu nhỏ để không giật cục
        {
            if (error > snapThreshold)
            {
                rb.position = expectedPos;
            }
            else
            {
                rb.position = Vector2.Lerp(rb.position, expectedPos, 0.5f);
            }
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer) return; // Chỉ Server mới được xử lý logic va chạm nguy hiểm để tránh client tự ý đoán sai

        if (other.gameObject.layer == LayerMask.NameToLayer("Explosion"))
        {
            DeathSequenceClientRpc();
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

        playerVisual?.SetDead(true);
        Invoke(nameof(OnDeathSequenceEnded), 1.25f);
    }

    private void OnDeathSequenceEnded()
    {
        Debug.Log("Player has died.");
    }
}