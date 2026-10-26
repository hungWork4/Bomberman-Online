using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ServerPlayerController : MonoBehaviour
{
    private PlayerController _playerController;

    private int _lastSimulatedTick;
    private Vector2 _lastProcessedInput = Vector2.zero;
    private bool _hasInitializedTick;

    private readonly Dictionary<int, Vector2> _inputQueue = new Dictionary<int, Vector2>();

    private float _lastStateSentTime;
    private const float STATE_SEND_RATE = 0.033f; // ~30 updates per second for smooth state sync
    private Vector2 _lastServerPosition;

    public void Initialize(PlayerController playerController)
    {
        _playerController = playerController;
        _lastServerPosition = _playerController.transform.position;
        _inputQueue.Clear();
        _hasInitializedTick = false;
        _lastSimulatedTick = 0;
        _lastProcessedInput = Vector2.zero;
    }

    public void OnInputReceived(PlayerController.InputPayload input)
    {
        if (!_hasInitializedTick && input.tick > 0)
        {
            _hasInitializedTick = true;
            _lastSimulatedTick = input.tick - 1;
        }

        // Store history from redundant payload (tick, tick-1, tick-2, tick-3)
        StoreInput(input.tick, input.direction);
        StoreInput(input.tick - 1, input.prevDir1);
        StoreInput(input.tick - 2, input.prevDir2);
        StoreInput(input.tick - 3, input.prevDir3);
    }

    private void StoreInput(int tick, Vector2 direction)
    {
        if (!_hasInitializedTick) return;
        if (tick <= _lastSimulatedTick) return;
        if (!_inputQueue.ContainsKey(tick))
        {
            _inputQueue[tick] = direction;
        }
    }

    private void FixedUpdate()
    {
        if (_playerController == null || !_playerController.IsSpawned) return;

        if (!_playerController.IsOwner)
        {
            if (!_hasInitializedTick) return;

            // Xử lý đuổi kịp khi mạng chập chờn (Jitter Catch-up):
            // Cho phép server xử lý tối đa 4 ticks mỗi FixedUpdate khi hàng đợi bị đọng để lập tức bắt kịp thời gian thực
            int maxTicksToProcess = Mathf.Clamp(_inputQueue.Count, 1, 4);
            int ticksProcessed = 0;

            while (ticksProcessed < maxTicksToProcess)
            {
                int nextTick = _lastSimulatedTick + 1;

                if (_inputQueue.TryGetValue(nextTick, out Vector2 inputDir))
                {
                    _inputQueue.Remove(nextTick);
                    _lastSimulatedTick = nextTick;
                    _lastProcessedInput = inputDir;
                    _playerController.SimulateLocal(inputDir);
                    ticksProcessed++;
                }
                else
                {
                    // Nếu thiếu tick tiếp theo nhưng phía trước có tick mới hơn (rớt packet):
                    int minAvailableTick = int.MaxValue;
                    foreach (var k in _inputQueue.Keys)
                    {
                        if (k > nextTick && k < minAvailableTick)
                        {
                            minAvailableTick = k;
                        }
                    }

                    // Nếu chỉ cách tối đa 3 ticks, ngoại suy (extrapolate) bằng input gần nhất để không bị khựng vị trí
                    if (minAvailableTick != int.MaxValue && (minAvailableTick - _lastSimulatedTick) <= 3)
                    {
                        _lastSimulatedTick = nextTick;
                        _playerController.SimulateLocal(_lastProcessedInput);
                        ticksProcessed++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (ticksProcessed > 0 || _inputQueue.Count > 0)
            {
                UpdateServerState(_lastSimulatedTick, _lastProcessedInput);
            }
            // Dọn dẹp các input tick đã quá cũ
            if (_inputQueue.Count > 100)
            {
                List<int> keysToRemove = new List<int>();
                foreach (int k in _inputQueue.Keys)
                {
                    if (k <= _lastSimulatedTick) keysToRemove.Add(k);
                }
                for (int i = 0; i < keysToRemove.Count; i++)
                {
                    _inputQueue.Remove(keysToRemove[i]);
                }
            }
        }
        else
        {
            // For Host (Owner + Server):
            var clientController = GetComponent<ClientPlayerController>();
            if (clientController != null)
            {
                UpdateServerState(clientController.LocalTick, clientController.CurrentInput);
            }
        }
    }

    public void UpdateServerState(int tick, Vector2 direction)
    {
        if (!_playerController.IsServer) return;

        Vector2 currentPosition = _playerController.transform.position;
        Vector2 vel = direction * _playerController.moveSpeed;

        bool positionChanged = Vector2.Distance(currentPosition, _lastServerPosition) > 0.0005f;
        bool velocityChanged = _playerController.serverState.Value.velocity != vel;
        bool heartbeatDue = (Time.time - _lastStateSentTime) >= STATE_SEND_RATE;

        // Cập nhật state nếu:
        // 1. Vận tốc hoặc vị trí thay đổi (bắt đầu đi / dừng lại / đổi hướng)
        // 2. Hoặc đã đến hạn heartbeat (30Hz) VÀ tick mới hơn tick đã lưu (giúp xác nhận tick cho client ngay cả khi bị tường chặn)
        if (velocityChanged || positionChanged || (heartbeatDue && tick > _playerController.serverState.Value.tick))
        {
            _playerController.serverState.Value = new PlayerController.StatePayload
            {
                tick = tick,
                position = currentPosition,
                velocity = vel
            };
            _lastServerPosition = currentPosition;
            _lastStateSentTime = Time.time;
        }
    }

    public void ResetState(Vector2 position)
    {
        _lastSimulatedTick = 0;
        _hasInitializedTick = false;
        _lastProcessedInput = Vector2.zero;
        _inputQueue.Clear();
        _lastServerPosition = position;
        _lastStateSentTime = 0f;
    }
}
