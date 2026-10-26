using System.Collections.Generic;
using UnityEngine;

public class ClientPlayerController : MonoBehaviour
{
    private PlayerController _playerController;
    private Rigidbody2D _rb;

    private Vector2 _currentInput;
    private Vector2 _lastSentInput;
    private int _localTick;
    private readonly List<PlayerController.InputPayload> _inputBuffer = new List<PlayerController.InputPayload>();
    private PlayerController.StatePayload _lastProcessedState;

    private float _lastInputSentTime;
    private const float INPUT_SEND_RATE = 0.02f; // Send inputs up to 50 Hz (every 20ms) for low latency

    public int LocalTick => _localTick;
    public Vector2 CurrentInput => _currentInput;

    public void Initialize(PlayerController playerController)
    {
        _playerController = playerController;
        _rb = playerController.rb;
        _lastProcessedState = playerController.serverState.Value;
    }

    private void Update()
    {
        if (_playerController == null || !_playerController.IsSpawned) return;

        if (_playerController.IsOwner && _playerController.isPlayerLoadDone.Value)
        {
            _currentInput = Vector2.zero;
            if (!UnityChatController.IsChatting)
            {
                if (Input.GetKey(_playerController.inputUp)) _currentInput = Vector2.up;
                else if (Input.GetKey(_playerController.inputDown)) _currentInput = Vector2.down;
                else if (Input.GetKey(_playerController.inputLeft)) _currentInput = Vector2.left;
                else if (Input.GetKey(_playerController.inputRight)) _currentInput = Vector2.right;
            }

            _playerController.playerVisual?.SetMovement(_currentInput);

            if (Input.GetKeyDown(KeyCode.LeftShift) && _playerController.touchingBomb != null && !UnityChatController.IsChatting)
            {
                _playerController.touchingBomb.RequestPushServerRpc(_playerController.playerVisual.lastDirection);
            }
        }
        else
        {
            // For remote clients: update anim based on synced server velocity
            PlayerController.StatePayload targetState = _playerController.serverState.Value;
            Vector2 visualDirection = targetState.velocity.sqrMagnitude > 0f ? targetState.velocity.normalized : Vector2.zero;
            _playerController.playerVisual?.SetMovement(visualDirection);
        }
    }

    private Vector2 GetBufferedInputDir(int targetTick)
    {
        for (int i = _inputBuffer.Count - 1; i >= 0; i--)
        {
            if (_inputBuffer[i].tick == targetTick) return _inputBuffer[i].direction;
        }
        return Vector2.zero;
    }

    private void FixedUpdate()
    {
        if (_playerController == null || !_playerController.IsSpawned) return;

        if (_playerController.IsOwner)
        {
            _localTick++;

            Vector2 dir1 = GetBufferedInputDir(_localTick - 1);
            Vector2 dir2 = GetBufferedInputDir(_localTick - 2);
            Vector2 dir3 = GetBufferedInputDir(_localTick - 3);

            PlayerController.InputPayload inputPayload = new PlayerController.InputPayload
            {
                tick = _localTick,
                direction = _currentInput,
                prevDir1 = dir1,
                prevDir2 = dir2,
                prevDir3 = dir3
            };

            _inputBuffer.Add(inputPayload);

            // Clean up very old inputs from buffer
            if (_inputBuffer.Count > 120)
            {
                _inputBuffer.RemoveAt(0);
            }

            if (_playerController.IsServer)
            {
                // Host (Owner + Server): simulate locally
                _playerController.SimulateLocal(inputPayload.direction);
            }
            else
            {
                // Client Owner: simulate locally, send input with redundancy, and reconcile
                _playerController.SimulateLocal(inputPayload.direction);

                bool inputChanged = _currentInput != _lastSentInput;
                bool isMoving = _currentInput != Vector2.zero;
                bool timeToSend = (Time.time - _lastInputSentTime) >= (INPUT_SEND_RATE - 0.002f);

                // Send if input changed, or if moving, or periodically to maintain connection
                if (inputChanged || (isMoving && timeToSend) || timeToSend)
                {
                    _playerController.SubmitInputServerRpc(inputPayload);
                    _lastSentInput = _currentInput;
                    _lastInputSentTime = Time.time;
                }

                Reconcile();
            }
        }
        else
        {
            if (!_playerController.IsServer)
            {
                // Remote Client: Interpolate position smoothly with velocity extrapolation
                PlayerController.StatePayload targetState = _playerController.serverState.Value;
                Vector2 currentPos = transform.position;
                float distance = Vector2.Distance(currentPos, targetState.position);

                if (distance > 2.0f)
                {
                    // Snap on massive position jump (teleport)
                    transform.position = targetState.position;
                    if (_rb != null) _rb.position = targetState.position;
                }
                else
                {
                    Vector2 predictedPos = targetState.position + (targetState.velocity * Time.fixedDeltaTime);
                    float followSpeed = Mathf.Max(targetState.velocity.magnitude, distance * _playerController.remotePositionLerp);
                    Vector2 nextPos = Vector2.MoveTowards(currentPos, predictedPos, followSpeed * Time.fixedDeltaTime);
                    transform.position = nextPos;
                    if (_rb != null) _rb.position = nextPos;
                }
            }
        }
    }

    private void Reconcile()
    {
        PlayerController.StatePayload latestServerState = _playerController.serverState.Value;

        if (latestServerState.tick == 0 || latestServerState.tick == _lastProcessedState.tick) return;

        _lastProcessedState = latestServerState;

        // Keep inputs that are strictly ahead of latestServerState.tick
        _inputBuffer.RemoveAll(i => i.tick <= latestServerState.tick);

        Vector2 expectedPos = latestServerState.position;

        for (int i = 0; i < _inputBuffer.Count; i++)
        {
            expectedPos = _playerController.SimulateStep(expectedPos, _inputBuffer[i].direction, Time.fixedDeltaTime);
        }

        Vector2 currentPos = transform.position;
        Vector2 error = currentPos - expectedPos;

        // 1. DEADZONE (< 8cm): Mạng dao động nhẹ (ping chập chờn 50-120ms) sẽ tạo ra vi sai số khoảng 2-7cm.
        // Bỏ qua sai số trong deadzone để triệt tiêu hoàn toàn giật con lắc khi ping nhảy
        const float DEADZONE_THRESHOLD_SQR = 0.0064f; // 0.08m = 8cm
        // 2. LARGE ERROR (> 1.2m): Lệch quá lớn do teleport / mất kết nối sâu => Cưỡng bức snap
        const float LARGE_ERROR_THRESHOLD_SQR = 1.44f; // 1.2m

        if (error.sqrMagnitude > LARGE_ERROR_THRESHOLD_SQR)
        {
            // Sai lệch quá lớn => Khôi phục vị trí lập tức
            transform.position = expectedPos;
            if (_rb != null) _rb.position = expectedPos;
            _playerController.playerVisual?.ResetVisualOffset();
        }
        else if (error.sqrMagnitude > DEADZONE_THRESHOLD_SQR)
        {
            // Sai lệch thực sự vượt ngưỡng Deadzone => Cập nhật vị trí chuẩn, bù sai số vào visual offset để mắt nhìn mượt mà
            transform.position = expectedPos;
            if (_rb != null) _rb.position = expectedPos;
            _playerController.playerVisual?.AddVisualOffset((Vector3)error);
        }
    }

    public void ResetPrediction(Vector2 position)
    {
        _localTick = 0;
        _inputBuffer.Clear();
        _lastSentInput = Vector2.zero;
        _currentInput = Vector2.zero;
        _lastInputSentTime = 0f;
        _lastProcessedState = new PlayerController.StatePayload
        {
            tick = 0,
            position = position,
            velocity = Vector2.zero
        };
    }
}
