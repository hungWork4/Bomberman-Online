using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic; // 🔥 ADD
using Input = UnityEngine.Input;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;

    [Header("Remote Sync")]
    [SerializeField] private float remotePositionLerp = 16f;

    [Header("References")]
    [SerializeField] private PlayerVisual playerVisual;

    private Rigidbody2D rb;
    private Vector2 direction;

    private Vector2 remoteTargetPosition;
    private Vector2 remoteVelocity;
    private bool hasRemoteState;

    [Header("Input")]
    public KeyCode inputUp = KeyCode.W;
    public KeyCode inputDown = KeyCode.S;
    public KeyCode inputLeft = KeyCode.A;
    public KeyCode inputRight = KeyCode.D;

    // =========================
    // 🔥 PREDICTION VARIABLES
    // =========================
    private struct InputPayload
    {
        public int tick;
        public Vector2 direction;
    }

    private struct StatePayload
    {
        public int tick;
        public Vector2 position;
    }

    private List<InputPayload> inputBuffer = new List<InputPayload>();
    private List<StatePayload> stateBuffer = new List<StatePayload>();

    private int currentTick;
    private const int BUFFER_SIZE = 1024;

    // server tick nhận về
    private int lastServerTick; // 🔥 ADD

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

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (Input.GetKey(inputUp))
        {
            direction = Vector2.up;
        }
        else if (Input.GetKey(inputDown))
        {
            direction = Vector2.down;
        }
        else if (Input.GetKey(inputLeft))
        {
            direction = Vector2.left;
        }
        else if (Input.GetKey(inputRight))
        {
            direction = Vector2.right;
        }
        else
        {
            direction = Vector2.zero;
        }

        playerVisual?.SetMovement(direction);
    }

    private void FixedUpdate()
    {
        if (!IsSpawned)
        {
            return;
        }

        if (IsOwner)
        {
            currentTick++; // 🔥 ADD

            if (IsServer)
            {
                Vector2 position;
                Vector2 velocity;

                // 🔥 PREDICT LOCAL
                Simulate(direction, out position, out velocity);

                SaveState(position); // 🔥 ADD

                BroadcastStateClientRpc(position, velocity, currentTick); // 🔥 ADD tick
            }
            else
            {
                // 🔥 CLIENT PREDICTION
                Vector2 position;
                Vector2 velocity;

                Simulate(direction, out position, out velocity);
                SaveState(position);

                SubmitInputServerRpc(direction, currentTick); // 🔥 gửi tick
            }

            // 🔥 RECONCILIATION
            Reconcile(); // 🔥 ADD
        }
        else if (!IsServer && hasRemoteState)
        {
            Vector2 smoothedPosition = Vector2.Lerp(rb.position, remoteTargetPosition, remotePositionLerp * Time.fixedDeltaTime);
            rb.MovePosition(smoothedPosition);
        }
    }

    // 🔥 SAVE STATE
    private void SaveState(Vector2 position)
    {
        inputBuffer.Add(new InputPayload
        {
            tick = currentTick,
            direction = direction
        });

        stateBuffer.Add(new StatePayload
        {
            tick = currentTick,
            position = position
        });

        if (inputBuffer.Count > BUFFER_SIZE)
        {
            inputBuffer.RemoveAt(0);
            stateBuffer.RemoveAt(0);
        }
    }

    private void Simulate(Vector2 inputDirection, out Vector2 position, out Vector2 velocity)
    {
        Vector2 currentPosition = rb.position;
        velocity = inputDirection * moveSpeed;
        position = currentPosition + velocity * Time.fixedDeltaTime;
        rb.MovePosition(position);
    }

    [ServerRpc]
    private void SubmitInputServerRpc(Vector2 inputDirection, int tick, ServerRpcParams serverRpcParams = default) // 🔥 ADD tick
    {
        if (!IsServer)
        {
            return;
        }

        Vector2 position;
        Vector2 velocity;

        Simulate(inputDirection, out position, out velocity);

        BroadcastStateClientRpc(position, velocity, tick); // 🔥 gửi tick
    }

    [ClientRpc]
    private void BroadcastStateClientRpc(Vector2 position, Vector2 velocity, int tick) // 🔥 ADD tick
    {
        if (IsOwner && !IsServer)
        {
            lastServerTick = tick; // 🔥 ADD

            rb.position = position;

            Vector2 visualDirection = velocity.sqrMagnitude > 0f ? velocity.normalized : Vector2.zero;
            playerVisual?.SetMovement(visualDirection);
            return;
        }

        if (!IsOwner)
        {
            remoteTargetPosition = position;
            remoteVelocity = velocity;

            if (!hasRemoteState)
            {
                rb.position = remoteTargetPosition;
            }

            hasRemoteState = true;

            Vector2 visualDirection = remoteVelocity.sqrMagnitude > 0f ? remoteVelocity.normalized : Vector2.zero;
            playerVisual?.SetMovement(visualDirection);
        }
    }

    // 🔥 CORE RECONCILE
    private void Reconcile()
    {
        if (lastServerTick == 0) return;

        int index = stateBuffer.FindIndex(s => s.tick == lastServerTick);
        if (index == -1) return;

        Vector2 serverPos = rb.position;
        Vector2 localPos = stateBuffer[index].position;

        float error = Vector2.Distance(localPos, serverPos);

        if (error < 0.01f) return;

        // rollback
        rb.position = serverPos;

        // remove old
        inputBuffer.RemoveRange(0, index + 1);
        stateBuffer.RemoveRange(0, index + 1);

        // replay
        foreach (var input in inputBuffer)
        {
            Vector2 pos;
            Vector2 vel;
            Simulate(input.direction, out pos, out vel);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.layer == LayerMask.NameToLayer("Explosion"))
        {
            DeathSequence();
            playerVisual?.SetDead(true);
        }
    }

    private void DeathSequence()
    {
        enabled = false;
        GetComponent<BombController>().enabled = false;
        playerVisual?.SetSortingOrder(1);
        GetComponent<Collider2D>().enabled = false;
        Invoke(nameof(OnDeathSequenceEnded), 1.25f);
    }

    private void OnDeathSequenceEnded()
    {
        Debug.Log("Player has died. Implement death handling logic here.");
    }
}