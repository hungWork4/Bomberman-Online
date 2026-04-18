using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Tilemaps;
using Pathfinding.BehaviourTrees; 

[RequireComponent(typeof(Rigidbody2D))]
public class BombermanAI : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private NetworkVariable<float> _moveSpeed = new NetworkVariable<float>(5f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public float moveSpeed { get => _moveSpeed.Value; set => _moveSpeed.Value = value; }

    [Header("Sync & Layers")]
    [SerializeField] private float remotePositionLerp = 16f;
    [SerializeField] private float walkableCheckRadius = 0.42f;
    [Tooltip("Gán layer chứa Tường (Stage) vào đây")]
    public LayerMask obstacleLayer;
    [Tooltip("Gán layer chứa Quả Bomb vào đây")]
    public LayerMask bombLayer;
    [Tooltip("Gán layer chứa vùng nổ (Explosion) vào đây")]
    public LayerMask explosionLayer;

    [Header("Bomb Settings")]
    [SerializeField] private GameObject bombPrefab;
    [SerializeField] private float bombFuseTime = 3f;
    private float postExplosionMoveDelay = 1.5f;
    private NetworkVariable<int> bombsRemaining = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Header("Attack Settings")]
    [SerializeField] private float attackPlayerDetectionRange = 5f;
    [SerializeField] private float maxAttackTravelDistance = 2.5f;
    [SerializeField] private float attackCooldown = 2.5f;
    private float nextAttackAllowedTime;

    [SerializeField] private NetworkVariable<int> explosionRadius = new NetworkVariable<int>(1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public int ExplosionRadius { get => explosionRadius.Value; set => explosionRadius.Value = value; }

    private Rigidbody2D rb;
    private BehaviourTree tree;
    public BehaviourTree Tree => tree;
    private AISmartDodge dodgeStrategy;
    private AIAttackPlayer attackPlayerStrategy;
    private AIMoveToNearestItemPickup moveToItemPickupStrategy;
    private AIMoveToNearestDestructible moveToDestructibleStrategy;
    private bool isDead = false;
    private bool isWaitingBombExplosion;
    private bool isPlacingBomb;
    private bool hasActiveOwnBomb;
    private bool ownBombExploded;
    private float startRunTime;
    private Vector2 ownBombPosition;
    private float moveUnlockTime;
    private float ownBombExplodeTime;
    private string lastBehaviorLogged;
    private Tilemap destructibleTiles;
    private readonly Vector2Int[] cardinalDirs =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    // Trạng thái đồng bộ vị trí cho Client
    private NetworkVariable<Vector2> serverPosition = new NetworkVariable<Vector2>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    private void OnEnable()
    {
        // Tự động tìm Tilemap chứa các block có thể phá được
        var stage = GameObject.Find("Stage")?.transform;
        if (stage != null)
        {
            var destructibles = FindDeepChild(stage, "Destructibles");
            if (destructibles != null)
            {
                destructibleTiles = destructibles.GetComponent<Tilemap>();
            }
        }
    }

    public static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }

    public override void OnNetworkSpawn()
    {
        // Căn chỉnh vị trí AI khớp với tâm lưới (Grid) khi vừa xuất hiện
        rb.position = new Vector2(Mathf.Round(transform.position.x), Mathf.Round(transform.position.y));

        if (IsServer)
        {
            bombsRemaining.Value = 1; // Số lượng bomb mặc định
            serverPosition.Value = rb.position;
            startRunTime = Time.time + 2f;
            InitBehaviorTree();
        }
    }

    private void InitBehaviorTree()
    {
        tree = new BehaviourTree("Smart Bomberman AI");
        PrioritySelector root = new PrioritySelector("Root");

        dodgeStrategy = new AISmartDodge(this, rb);
        attackPlayerStrategy = new AIAttackPlayer(this, rb);
        moveToItemPickupStrategy = new AIMoveToNearestItemPickup(this, rb);
        moveToDestructibleStrategy = new AIMoveToNearestDestructible(this, rb);

        // Nhánh 1 (Ưu tiên 100): Né Bomb - Quét vùng nổ chữ thập và tìm góc khuất
        Sequence dodgeSequence = new Sequence("Dodge Bomb", 100);
        dodgeSequence.AddChild(new Leaf("Check Danger", new Condition(ShouldDodgeNow)));
        dodgeSequence.AddChild(new Leaf("Move To Safe Spot", dodgeStrategy));

        // Nhánh 2 (Ưu tiên 70): Tấn công người chơi - chọn điểm đặt bomb giảm đường thoát của đối thủ
        Sequence attackPlayerSequence = new Sequence("Attack Player", 70);
        attackPlayerSequence.AddChild(new Leaf("Can Attack Player?", new Condition(ShouldAttackPlayerNow)));
        attackPlayerSequence.AddChild(new Leaf("Attack Player Action", attackPlayerStrategy));

        // Nhánh 3 (Ưu tiên 90): Trong lúc chờ bomb nổ thì tiếp tục giữ vị trí an toàn
        Sequence waitBombSequence = new Sequence("Wait Bomb", 90);
        waitBombSequence.AddChild(new Leaf("Is Waiting Bomb?", new Condition(IsWaitingForBombExplosion)));
        waitBombSequence.AddChild(new Leaf("Wait Bomb Action", new ActionStrategy(WaitBombExplosionAction)));

        // Nhánh 4 (Ưu tiên 80): Nhặt item gần nhất
        Leaf moveToItemPickup = new Leaf("Move To Item Pickup", moveToItemPickupStrategy, 80);

        // Nhánh 5 (Ưu tiên 50): Phá Block - Đứng cạnh tường mềm thì đặt bomb
        Sequence breakBlockSequence = new Sequence("Break Block", 50);
        breakBlockSequence.AddChild(new Leaf("Can Plant Bomb?", new Condition(CanPlantBombNearDestructible)));
        breakBlockSequence.AddChild(new Leaf("Plant Bomb", new ActionStrategy(PlantBombAction)));

        // Nhánh 6 (Ưu tiên 20): Nếu chưa đứng cạnh block mềm, tìm block gần nhất và di chuyển tới
        Leaf moveToDestructible = new Leaf("Move To Nearest Destructible", moveToDestructibleStrategy, 20);

        // Nhánh 7 (Ưu tiên 10): Đứng yên - Chỉ khi không còn mục tiêu nào phù hợp
        root.AddChild(dodgeSequence);
        root.AddChild(waitBombSequence);
        root.AddChild(moveToItemPickup);
        root.AddChild(attackPlayerSequence);
        root.AddChild(breakBlockSequence);
        root.AddChild(moveToDestructible);
        root.AddChild(new Leaf("Idle", new ActionStrategy(IdleAction)));

        tree.AddChild(root);
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || isDead) return;

        if (IsServer)
        {
            if (Time.time < startRunTime)
            {
                serverPosition.Value = rb.position;
                return;
            }

            if (ShouldDodgeNow())
            {
                ResetNonDodgeStrategies();
            }

            tree?.Tick();
            serverPosition.Value = rb.position;
        }
        else
        {
            // Nội suy vị trí cho máy Client để chống giật
            rb.position = Vector2.Lerp(rb.position, serverPosition.Value, remotePositionLerp * Time.fixedDeltaTime);
        }
    }

    // ==========================================
    // LOGIC DÒ ĐƯỜNG VÀ KIỂM TRA (SERVER ONLY)
    // ==========================================

    public bool IsCurrentPosUnsafe() => IsPositionUnsafe(rb.position);
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        // 🔴 Vị trí hiện tại
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(rb.position, 0.2f);

        // 🔵 Ô hiện tại (snap grid)
        Vector2 snapped = new Vector2(Mathf.Round(rb.position.x), Mathf.Round(rb.position.y));
        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(snapped, Vector2.one);

        // 🔥 Vùng nguy hiểm (unsafe)
        Gizmos.color = Color.red;
        if (IsPositionUnsafe(rb.position))
        {
            Gizmos.DrawWireSphere(rb.position, 0.5f);
        }
    }
    public bool ShouldDodgeNow()
    {
        return IsPositionUnsafe(rb.position) || IsInsideOwnBombBlast(rb.position);
    }

    public bool HasPendingDodgeStep()
    {
        return dodgeStrategy != null && dodgeStrategy.HasPendingStep;
    }

    // Kiểm tra xem vị trí truyền vào có nằm trên tia nổ của bất kỳ quả bom nào không
    public bool IsPositionUnsafe(Vector2 pos)
    {
        Vector2 snappedPos = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));

        if (HasActiveExplosionAt(snappedPos))
        {
            return true;
        }

        // Quét tìm tất cả bom trong phạm vi 10 ô xung quanh
        Collider2D[] activeBombs = Physics2D.OverlapCircleAll(snappedPos, 10f, bombLayer);

        foreach (var bombCollider in activeBombs)
        {
            Vector2 bombPos = new Vector2(Mathf.Round(bombCollider.transform.position.x), Mathf.Round(bombCollider.transform.position.y));

            // Giả định bán kính của bomb (Lấy mặc định của AI, hoặc bạn có thể gọi bombit.radius)
            int radius = bombCollider.GetComponent<Bombit>().assignedExplosionRadius.Value;

            if (snappedPos == bombPos)
            {
                return true;
            }

            // Kiểm tra tia nổ theo 4 hướng, có xét vật cản chặn tia nổ.
            foreach (Vector2Int dir in cardinalDirs)
            {
                Vector2 current = bombPos;
                for (int i = 1; i <= radius; i++)
                {
                    current += (Vector2)dir;

                    if (IsObstacleBlockingExplosion(current))
                    {
                        break;
                    }

                    if (current == snappedPos)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private bool IsInsideOwnBombBlast(Vector2 pos)
    {
        if (!hasActiveOwnBomb)
        {
            return false;
        }

        return IsInsideBombBlast(pos, ownBombPosition, ExplosionRadius);
    }

    private bool IsInsideBombBlast(Vector2 pos, Vector2 bombPos, int radius)
    {
        Vector2 snappedPos = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
        Vector2 snappedBomb = new Vector2(Mathf.Round(bombPos.x), Mathf.Round(bombPos.y));

        if (snappedPos == snappedBomb)
        {
            return true;
        }

        foreach (Vector2Int dir in cardinalDirs)
        {
            Vector2 current = snappedBomb;
            for (int i = 1; i <= radius; i++)
            {
                current += (Vector2)dir;

                if (IsObstacleBlockingExplosion(current))
                {
                    break;
                }

                if (current == snappedPos)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool TryGetNextStepToSafeCell(out Vector2 nextStep)
    {
        // Ưu tiên đường né không đi qua lửa; nếu bí đường thì cho phép băng qua lửa để thoát thân.
        if (TryGetNextStepToSafeCellInternal(out nextStep, allowExplosionTraversal: false))
        {
            return true;
        }

        return TryGetNextStepToSafeCellInternal(out nextStep, allowExplosionTraversal: true);
    }

    private bool TryGetNextStepToSafeCellInternal(out Vector2 nextStep, bool allowExplosionTraversal)
    {
        nextStep = default;

        Vector2Int start = new Vector2Int(Mathf.RoundToInt(rb.position.x), Mathf.RoundToInt(rb.position.y));

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> parent = new Dictionary<Vector2Int, Vector2Int>();

        queue.Enqueue(start);
        visited.Add(start);

        int safetyCounter = 0;
        const int maxVisited = 512;

        while (queue.Count > 0 && safetyCounter < maxVisited)
        {
            safetyCounter++;

            Vector2Int cell = queue.Dequeue();
            Vector2 world = new Vector2(cell.x, cell.y);

            if (cell != start && IsCellWalkableInternal(world, allowExplosionTraversal) && !IsPositionUnsafe(world) && !IsInsideOwnBombBlast(world))
            {
                Vector2Int step = cell;
                while (parent.TryGetValue(step, out Vector2Int prev) && prev != start)
                {
                    step = prev;
                }

                nextStep = new Vector2(step.x, step.y);
                return true;
            }

            foreach (Vector2Int dir in cardinalDirs)
            {
                Vector2Int next = cell + dir;
                if (visited.Contains(next))
                {
                    continue;
                }

                Vector2 nextWorld = new Vector2(next.x, next.y);
                if (!IsCellWalkableInternal(nextWorld, allowExplosionTraversal))
                {
                    continue;
                }

                visited.Add(next);
                parent[next] = cell;
                queue.Enqueue(next);
            }
        }

        return false;
    }

    public bool TryGetNextStepToward(Vector2 targetCell, out Vector2 nextStep)
    {
        // Ưu tiên đổi đường né vùng nổ; chỉ băng qua vùng nguy hiểm khi bí đường.
        if (TryGetNextStepTowardInternal(targetCell, out nextStep, avoidUnsafeCells: true, allowExplosionTraversal: false))
        {
            return true;
        }

        return TryGetNextStepTowardInternal(targetCell, out nextStep, avoidUnsafeCells: true, allowExplosionTraversal: true);
    }

    private bool TryGetNextStepTowardInternal(Vector2 targetCell, out Vector2 nextStep, bool avoidUnsafeCells, bool allowExplosionTraversal)
    {
        nextStep = default;

        Vector2Int start = new Vector2Int(Mathf.RoundToInt(rb.position.x), Mathf.RoundToInt(rb.position.y));
        Vector2Int goal = new Vector2Int(Mathf.RoundToInt(targetCell.x), Mathf.RoundToInt(targetCell.y));

        if (start == goal)
        {
            return false;
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> parent = new Dictionary<Vector2Int, Vector2Int>();

        queue.Enqueue(start);
        visited.Add(start);

        int safetyCounter = 0;
        const int maxVisited = 1024;

        while (queue.Count > 0 && safetyCounter < maxVisited)
        {
            safetyCounter++;
            Vector2Int cell = queue.Dequeue();

            if (cell == goal)
            {
                Vector2Int step = goal;
                while (parent.TryGetValue(step, out Vector2Int prev) && prev != start)
                {
                    step = prev;
                }

                nextStep = new Vector2(step.x, step.y);
                return true;
            }

            foreach (Vector2Int dir in cardinalDirs)
            {
                Vector2Int next = cell + dir;
                if (visited.Contains(next))
                {
                    continue;
                }

                Vector2 nextWorld = new Vector2(next.x, next.y);
                if (!IsCellWalkableInternal(nextWorld, allowExplosionTraversal))
                {
                    continue;
                }

                if (avoidUnsafeCells && (IsPositionUnsafe(nextWorld) || IsInsideOwnBombBlast(nextWorld)))
                {
                    continue;
                }

                visited.Add(next);
                parent[next] = cell;
                queue.Enqueue(next);
            }
        }

        return false;
    }

    private bool IsObstacleBlockingExplosion(Vector2 worldPos)
    {
        return Physics2D.OverlapCircle(worldPos, 0.2f, obstacleLayer) != null;
    }

    //public bool TryGetNearestSafeCell(out Vector2 safeCell)
    //{
    //    safeCell = default;

    //    Vector2Int start = new Vector2Int(Mathf.RoundToInt(rb.position.x), Mathf.RoundToInt(rb.position.y));

    //    Queue<Vector2Int> queue = new Queue<Vector2Int>();
    //    HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

    //    queue.Enqueue(start);
    //    visited.Add(start);

    //    int safetyCounter = 0;
    //    const int maxVisited = 512;

    //    while (queue.Count > 0 && safetyCounter < maxVisited)
    //    {
    //        safetyCounter++;
    //        Vector2Int cell = queue.Dequeue();
    //        Vector2 world = new Vector2(cell.x, cell.y);

    //        if (IsCellWalkable(world) && !IsPositionUnsafe(world))
    //        {
    //            safeCell = world;
    //            return true;
    //        }

    //        foreach (Vector2Int dir in cardinalDirs)
    //        {
    //            Vector2Int next = cell + dir;
    //            if (visited.Contains(next))
    //            {
    //                continue;
    //            }

    //            Vector2 nextWorld = new Vector2(next.x, next.y);
    //            if (!IsCellWalkable(nextWorld))
    //            {
    //                continue;
    //            }

    //            visited.Add(next);
    //            queue.Enqueue(next);
    //        }
    //    }

    //    return false;
    //}

    // Kiểm tra xem ô Grid có thể bước vào không (Không đụng tường/bom)
    public bool IsCellWalkable(Vector2 pos)
    {
        return IsCellWalkableInternal(pos, allowExplosionTraversal: false);
    }

    private bool IsCellWalkableInternal(Vector2 pos, bool allowExplosionTraversal)
    {
        if (!allowExplosionTraversal && HasActiveExplosionAt(pos))
        {
            return false;
        }

        return Physics2D.OverlapCircle(pos, walkableCheckRadius, obstacleLayer) == null &&
               Physics2D.OverlapCircle(pos, walkableCheckRadius, bombLayer) == null;
    }

    private bool HasActiveExplosionAt(Vector2 pos)
    {
        if (explosionLayer.value != 0 && Physics2D.OverlapCircle(pos, walkableCheckRadius, explosionLayer) != null)
        {
            return true;
        }

        int explosionLayerIndex = LayerMask.NameToLayer("Explosion");
        if (explosionLayerIndex < 0)
        {
            return false;
        }

        int explosionMask = 1 << explosionLayerIndex;
        return Physics2D.OverlapCircle(pos, walkableCheckRadius, explosionMask) != null;
    }

    public Vector2 GetAxisLockedMovePosition(Vector2 currentPos, Vector2 targetPos, float moveStep)
    {
        Vector2 delta = targetPos - currentPos;
        float remainingStep = moveStep;

        // Khóa trục theo hướng di chuyển chính, nhưng luôn giữ tổng quãng đường mỗi tick <= moveStep.
        if (Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
        {
            float correctedY = Mathf.MoveTowards(currentPos.y, Mathf.Round(currentPos.y), remainingStep);
            float usedStep = Mathf.Abs(correctedY - currentPos.y);
            remainingStep -= usedStep;

            float movedX = Mathf.MoveTowards(currentPos.x, targetPos.x, remainingStep);
            return new Vector2(movedX, correctedY);
        }

        float correctedX = Mathf.MoveTowards(currentPos.x, Mathf.Round(currentPos.x), remainingStep);
        float used = Mathf.Abs(correctedX - currentPos.x);
        remainingStep -= used;

        float movedY = Mathf.MoveTowards(currentPos.y, targetPos.y, remainingStep);
        return new Vector2(correctedX, movedY);
    }

    // Kiểm tra 4 hướng xem có Tường Mềm (Tag "Destructible") không
    public bool IsNextToDestructible()
    {
        if (bombsRemaining.Value <= 0) return false;

        Vector2 currentSnap = new Vector2(Mathf.Round(rb.position.x), Mathf.Round(rb.position.y));
        return IsAdjacentToDestructible(currentSnap);
    }

    private bool CanPlantBombNearDestructible()
    {
        if (isWaitingBombExplosion || isPlacingBomb)
        {
            return false;
        }

        return IsNextToDestructible();
    }

    public bool CanPlantBombNow()
    {
        if (!IsServer)
        {
            return false;
        }

        if (isWaitingBombExplosion || isPlacingBomb)
        {
            return false;
        }

        return bombsRemaining.Value > 0;
    }

    private bool ShouldAttackPlayerNow()
    {
        if (Time.time < nextAttackAllowedTime)
        {
            return false;
        }

        if (!CanPlantBombNow())
        {
            return false;
        }

        return HasPlayerInAttackRange();
    }

    private bool HasPlayerInAttackRange()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        if (players == null || players.Length == 0)
        {
            return false;
        }

        Vector2 aiCell = new Vector2(Mathf.Round(rb.position.x), Mathf.Round(rb.position.y));
        float detectionRangeSqr = attackPlayerDetectionRange * attackPlayerDetectionRange;

        foreach (GameObject player in players)
        {
            if (player == null || !player.activeInHierarchy || player == gameObject)
            {
                continue;
            }

            Vector2 playerCell = new Vector2(Mathf.Round(player.transform.position.x), Mathf.Round(player.transform.position.y));
            if (Vector2.SqrMagnitude(playerCell - aiCell) <= detectionRangeSqr)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryGetBestPlayerAttackCell(out Vector2 playerCell, out Vector2 attackCell, out int predictedEscapeRoutes)
    {
        playerCell = default;
        attackCell = default;
        predictedEscapeRoutes = int.MaxValue;

        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        if (players == null || players.Length == 0)
        {
            return false;
        }

        Vector2 aiCell = new Vector2(Mathf.Round(rb.position.x), Mathf.Round(rb.position.y));
        float detectionRangeSqr = attackPlayerDetectionRange * attackPlayerDetectionRange;
        float maxTravelSqr = maxAttackTravelDistance * maxAttackTravelDistance;
        float bestDist = float.MaxValue;
        bool hasTarget = false;

        foreach (GameObject player in players)
        {
            if (player == null || !player.activeInHierarchy)
            {
                continue;
            }

            if (player == gameObject)
            {
                continue;
            }

            Vector2 targetPlayerCell = new Vector2(Mathf.Round(player.transform.position.x), Mathf.Round(player.transform.position.y));
            if (Vector2.SqrMagnitude(targetPlayerCell - aiCell) > detectionRangeSqr)
            {
                continue;
            }

            foreach (Vector2Int dir in cardinalDirs)
            {
                Vector2 candidate = targetPlayerCell + (Vector2)dir;
                float candidateDist = Vector2.SqrMagnitude(candidate - aiCell);

                bool candidateIsCurrentCell = Vector2.Distance(candidate, aiCell) < 0.05f;
                if (!candidateIsCurrentCell)
                {
                    if (candidateDist > maxTravelSqr)
                    {
                        continue;
                    }

                    if (!IsCellWalkable(candidate) || IsPositionUnsafe(candidate))
                    {
                        continue;
                    }

                    if (!TryGetNextStepToward(candidate, out _))
                    {
                        continue;
                    }
                }

                int escapeRoutes = CountEscapeRoutesForPlayer(targetPlayerCell, candidate, ExplosionRadius);
                float dist = candidateDist;

                if (!hasTarget || escapeRoutes < predictedEscapeRoutes ||
                    (escapeRoutes == predictedEscapeRoutes && dist < bestDist))
                {
                    hasTarget = true;
                    bestDist = dist;
                    predictedEscapeRoutes = escapeRoutes;
                    playerCell = targetPlayerCell;
                    attackCell = candidate;
                }
            }
        }

        return hasTarget;
    }

    public int CountEscapeRoutesForPlayer(Vector2 playerCell, Vector2 hypotheticalBombPos, int radius)
    {
        int routes = 0;

        foreach (Vector2Int dir in cardinalDirs)
        {
            Vector2 next = playerCell + (Vector2)dir;
            if (!IsCellWalkable(next))
            {
                continue;
            }

            if (IsInsideBombBlast(next, hypotheticalBombPos, radius) || IsPositionUnsafe(next))
            {
                continue;
            }

            routes++;
        }

        return routes;
    }

    public bool TryGetNearestDestructibleApproach(out Vector2 targetCell)
    {
        targetCell = default;

        if (destructibleTiles == null)
        {
            return false;
        }

        Vector2Int start = new Vector2Int(Mathf.RoundToInt(rb.position.x), Mathf.RoundToInt(rb.position.y));

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

        queue.Enqueue(start);
        visited.Add(start);

        int safetyCounter = 0;
        const int maxVisited = 1024;

        while (queue.Count > 0 && safetyCounter < maxVisited)
        {
            safetyCounter++;
            Vector2Int cell = queue.Dequeue();
            Vector2 cellWorldPos = new Vector2(cell.x, cell.y);

            if (IsAdjacentToDestructible(cellWorldPos))
            {
                targetCell = cellWorldPos;
                return true;
            }

            foreach (Vector2Int dir in cardinalDirs)
            {
                Vector2Int next = cell + dir;
                if (visited.Contains(next))
                {
                    continue;
                }

                if (!IsCellWalkable(new Vector2(next.x, next.y)))
                {
                    continue;
                }

                visited.Add(next);
                queue.Enqueue(next);
            }
        }

        return false;
    }

    public bool TryGetNearestItemPickup(out Vector2 targetCell)
    {
        targetCell = default;

        ItemPickup[] pickups = FindObjectsOfType<ItemPickup>();
        if (pickups == null || pickups.Length == 0)
        {
            return false;
        }

        Vector2 current = new Vector2(Mathf.Round(rb.position.x), Mathf.Round(rb.position.y));
        float bestDistance = float.MaxValue;
        bool hasTarget = false;

        foreach (ItemPickup pickup in pickups)
        {
            if (pickup == null || !pickup.gameObject.activeInHierarchy)
            {
                continue;
            }

            Vector2 pickupCell = new Vector2(Mathf.Round(pickup.transform.position.x), Mathf.Round(pickup.transform.position.y));

            if (IsPositionUnsafe(pickupCell) || IsInsideOwnBombBlast(pickupCell) || !IsCellWalkable(pickupCell))
            {
                continue;
            }

            float dist = Vector2.SqrMagnitude(pickupCell - current);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                targetCell = pickupCell;
                hasTarget = true;
            }
        }

        return hasTarget;
    }

    private bool IsAdjacentToDestructible(Vector2 worldPos)
    {
        if (destructibleTiles == null)
        {
            return false;
        }

        foreach (Vector2Int dir in cardinalDirs)
        {
            Vector3Int tileCell = destructibleTiles.WorldToCell(worldPos + (Vector2)dir);
            if (destructibleTiles.GetTile(tileCell) != null)
            {
                return true;
            }
        }

        return false;
    }

    // ==========================================
    // HÀNH ĐỘNG AI
    // ==========================================

    private void PlantBombAction()
    {
        if (isWaitingBombExplosion || isPlacingBomb)
        {
            return;
        }

        LogBehavior("Plant Bomb");

        StartCoroutine(PlaceBombRoutine());
    }

    private void ResetNonDodgeStrategies()
    {
        moveToItemPickupStrategy?.Reset();
        moveToDestructibleStrategy?.Reset();
        attackPlayerStrategy?.Reset();
    }

    public bool TryPlantBombNow()
    {
        if (!CanPlantBombNow())
        {
            return false;
        }

        PlantBombAction();
        return true;
    }

    public void NotifyAttackExecuted()
    {
        if (!IsServer)
        {
            return;
        }

        nextAttackAllowedTime = Time.time + Mathf.Max(0f, attackCooldown);
    }

    public void AddBomb()
    {
        if (!IsServer)
        {
            return;
        }

        bombsRemaining.Value++;
    }

    public void AddBlastRadius(int amount = 1)
    {
        if (!IsServer)
        {
            return;
        }

        explosionRadius.Value += Mathf.Max(0, amount);
    }

    public void AddMoveSpeed(float amount = 1f)
    {
        if (!IsServer)
        {
            return;
        }

        _moveSpeed.Value += Mathf.Max(0f, amount);
    }

    private bool IsWaitingForBombExplosion()
    {
        return isWaitingBombExplosion;
    }

    private void WaitBombExplosionAction()
    {
        if (ShouldDodgeNow())
        {
            Node.Status dodgeStatus = dodgeStrategy?.Process() ?? Node.Status.Failure;
            if (dodgeStatus == Node.Status.Running || dodgeStatus == Node.Status.Success)
            {
                LogBehavior("Wait Bomb Explosion");
            }
        }
        else
        {
            Node.Status pickupStatus = moveToItemPickupStrategy?.Process() ?? Node.Status.Failure;
            if (pickupStatus == Node.Status.Running || pickupStatus == Node.Status.Success)
            {
                LogBehavior("Wait Bomb + Pickup Item");
            }
            else
            {
                LogBehavior("Wait Bomb Explosion");
            }
        }

        if (Time.time >= moveUnlockTime)
        {
            isWaitingBombExplosion = false;
            hasActiveOwnBomb = false;
            dodgeStrategy?.Reset();
            moveToItemPickupStrategy?.Reset();
        }
    }

    private void IdleAction()
    {
        if (CanPlantBombNearDestructible())
        {
            PlantBombAction();
            return;
        }

        if (moveToDestructibleStrategy != null)
        {

            Node.Status moveStatus = moveToDestructibleStrategy.Process();
            if (moveStatus == Node.Status.Running || moveStatus == Node.Status.Success)
            {
                return;
            }

            moveToDestructibleStrategy.Reset();
        }

        LogBehavior("Idle");
    }

    public void LogBehavior(string behaviorName)
    {
        if (lastBehaviorLogged == behaviorName)
        {
            return;
        }

        lastBehaviorLogged = behaviorName;
        Debug.Log($"[AI {name}] Behavior: {behaviorName}");
    }

    private IEnumerator PlaceBombRoutine()
    {
        if (bombsRemaining.Value <= 0) yield break;

        isPlacingBomb = true;

        Vector2 plantPos = new Vector2(Mathf.Round(rb.position.x), Mathf.Round(rb.position.y));
        if (Physics2D.OverlapCircle(plantPos, 0.2f, bombLayer) != null)
        {
            isPlacingBomb = false;
            yield break;
        }

        isWaitingBombExplosion = true;
        moveUnlockTime = Time.time + bombFuseTime + postExplosionMoveDelay;
        ownBombExplodeTime = Time.time + bombFuseTime;
        ownBombExploded = false;
        hasActiveOwnBomb = true;
        ownBombPosition = plantPos;

        GameObject bomb = Instantiate(bombPrefab, plantPos, Quaternion.identity);
        bomb.GetComponent<NetworkObject>().Spawn();

        Bombit bombit = bomb.GetComponent<Bombit>();
        if (bombit != null)
        {
            bombit.Initialize(destructibleTiles, ExplosionRadius);
        }
        bombsRemaining.Value--;

        yield return new WaitForSeconds(bombFuseTime);

        // Kích nổ
        if (bomb != null)
        {
            plantPos = new Vector2(Mathf.Round(bomb.transform.position.x), Mathf.Round(bomb.transform.position.y));
            bomb.GetComponent<Bombit>()?.bombexplosion(plantPos, destructibleTiles, ExplosionRadius);
            bomb.GetComponent<NetworkObject>().Despawn();
            Destroy(bomb);
        }
        bombsRemaining.Value++;
        isPlacingBomb = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer) return;

        if (other.gameObject.layer == LayerMask.NameToLayer("Explosion"))
        {
            DeathSequenceClientRpc();
        }
    }

    [ClientRpc]
    private void DeathSequenceClientRpc()
    {
        //isDead = true;
        //enabled = false;

        //var col = GetComponent<Collider2D>();
        //if (col != null) col.enabled = false;

        Debug.Log("AI bị hạ gục bởi sức ép từ vụ nổ!");
    }
}

// ==========================================
// STRATEGY: Dò đường né bom 
// ==========================================
public class AISmartDodge : IStrategy
{
    BombermanAI ai;
    Rigidbody2D rb;
    Vector2? currentStep;
    Vector2 lastPosition;
    float stuckTimer;

    public bool HasPendingStep => currentStep.HasValue;

    public AISmartDodge(BombermanAI ai, Rigidbody2D rb)
    {
        this.ai = ai;
        this.rb = rb;
    }

    public Node.Status Process()
    {
        bool shouldDodge = ai.ShouldDodgeNow();
        if (!shouldDodge && currentStep == null)
        {
            return Node.Status.Success;
        }

        if (shouldDodge && (currentStep == null ||
            Vector2.Distance(rb.position, currentStep.Value) < 0.02f ||
            !ai.IsCellWalkable(currentStep.Value) ||
            ai.IsPositionUnsafe(currentStep.Value)))
        {
            if (!ai.TryGetNextStepToSafeCell(out Vector2 nextStep))
            {
                currentStep = null;
                return Node.Status.Failure;
            }

            currentStep = nextStep;
        }

        Vector2 currentPos = rb.position;
        float moveStep = ai.moveSpeed * Time.fixedDeltaTime;

        ai.LogBehavior("Dodge Bomb");
        if (Vector2.Distance(currentPos, currentStep.Value) <= moveStep + 0.001f)
        {
            rb.MovePosition(currentStep.Value);
            rb.position = currentStep.Value;
            currentStep = null;
            return Node.Status.Running;
        }

        Vector2 nextPos = ai.GetAxisLockedMovePosition(currentPos, currentStep.Value, moveStep);
        rb.MovePosition(nextPos);

        if (Vector2.Distance(currentPos, lastPosition) < 0.001f)
        {
            stuckTimer += Time.fixedDeltaTime;
            if (stuckTimer >= 0.25f)
            {
                currentStep = null;
                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = 0f;
        }

        lastPosition = currentPos;

        return Node.Status.Running;
    }
    public Vector2 SnapToGrid(Vector2 pos)
    {
        // Làm tròn vị trí đến mốc 0.5 gần nhất
        return new Vector2(Mathf.Round(pos.x * 2f) / 2f, Mathf.Round(pos.y * 2f) / 2f);
    }
    public void Reset()
    {
        currentStep = null;
        stuckTimer = 0f;
        lastPosition = rb.position;
    }
}

public class AIMoveToNearestDestructible : IStrategy
{
    private readonly BombermanAI ai;
    private readonly Rigidbody2D rb;
    private Vector2? targetPoint;
    private Vector2 lastPosition;
    private float stuckTimer;

    public AIMoveToNearestDestructible(BombermanAI ai, Rigidbody2D rb)
    {
        this.ai = ai;
        this.rb = rb;
    }

    public Node.Status Process()
    {
        if (ai.IsNextToDestructible())
        {
            targetPoint = null;
            stuckTimer = 0f;
            return Node.Status.Success;
        }

        if (targetPoint == null || ai.IsPositionUnsafe(targetPoint.Value) || !ai.IsCellWalkable(targetPoint.Value))
        {
            if (!ai.TryGetNearestDestructibleApproach(out Vector2 foundTarget))
            {
                return Node.Status.Failure;
            }

            targetPoint = foundTarget;
        }

        if (!ai.TryGetNextStepToward(targetPoint.Value, out Vector2 nextStep))
        {
            targetPoint = null;
            return Node.Status.Failure;
        }

        Vector2 currentPos = rb.position;
        float moveStep = ai.moveSpeed * Time.fixedDeltaTime;

        ai.LogBehavior("Move To Nearest Destructible");
        if (Vector2.Distance(currentPos, nextStep) <= moveStep + 0.001f)
        {
            rb.MovePosition(nextStep);
            rb.position = nextStep;
        }
        else
        {
            Vector2 nextPos = ai.GetAxisLockedMovePosition(currentPos, nextStep, moveStep);
            rb.MovePosition(nextPos);
        }

        // Nếu bị kẹt vật cản/bomb động, reset target để tìm đường lại.
        if (Vector2.Distance(currentPos, lastPosition) < 0.001f)
        {
            stuckTimer += Time.fixedDeltaTime;
            if (stuckTimer >= 0.35f)
            {
                targetPoint = null;
                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = 0f;
        }
        lastPosition = currentPos;

        if (Vector2.Distance(currentPos, nextStep) <= moveStep + 0.001f)
        {
            if (Vector2.Distance(nextStep, targetPoint.Value) < 0.05f)
            {
                targetPoint = null;
                stuckTimer = 0f;
            }
        }

        return Node.Status.Running;
    }

    public void Reset()
    {
        targetPoint = null;
        stuckTimer = 0f;
    }

}

public class AIAttackPlayer : IStrategy
{
    private readonly BombermanAI ai;
    private readonly Rigidbody2D rb;
    private Vector2? attackCell;
    private Vector2? playerCell;
    private int cachedEscapeRoutes;
    private Vector2 lastPosition;
    private float stuckTimer;

    public AIAttackPlayer(BombermanAI ai, Rigidbody2D rb)
    {
        this.ai = ai;
        this.rb = rb;
    }

    public Node.Status Process()
    {
        if (!ai.CanPlantBombNow())
        {
            Reset();
            return Node.Status.Failure;
        }

        if (!ai.TryGetBestPlayerAttackCell(out Vector2 foundPlayerCell, out Vector2 foundAttackCell, out int escapeRoutes))
        {
            Reset();
            return Node.Status.Failure;
        }

        playerCell = foundPlayerCell;
        attackCell = foundAttackCell;
        cachedEscapeRoutes = escapeRoutes;

        Vector2 currentPos = rb.position;
        float moveStep = ai.moveSpeed * Time.fixedDeltaTime;

        if (Vector2.Distance(currentPos, attackCell.Value) < 0.05f)
        {
            if (ai.TryPlantBombNow())
            {
                ai.NotifyAttackExecuted();
                ai.LogBehavior("Attack Player");
                Reset();
                return Node.Status.Success;
            }

            return Node.Status.Failure;
        }

        if (!ai.TryGetNextStepToward(attackCell.Value, out Vector2 nextStep))
        {
            Reset();
            return Node.Status.Failure;
        }

        ai.LogBehavior("Attack Player");
        if (Vector2.Distance(currentPos, nextStep) <= moveStep + 0.001f)
        {
            rb.MovePosition(nextStep);
            rb.position = nextStep;
        }
        else
        {
            Vector2 nextPos = ai.GetAxisLockedMovePosition(currentPos, nextStep, moveStep);
            rb.MovePosition(nextPos);
        }

        if (Vector2.Distance(currentPos, lastPosition) < 0.001f)
        {
            stuckTimer += Time.fixedDeltaTime;
            if (stuckTimer >= 0.35f)
            {
                Reset();
            }
        }
        else
        {
            stuckTimer = 0f;
        }
        lastPosition = currentPos;

        return Node.Status.Running;
    }

    public void Reset()
    {
        attackCell = null;
        playerCell = null;
        cachedEscapeRoutes = int.MaxValue;
        stuckTimer = 0f;
    }
}

public class AIMoveToNearestItemPickup : IStrategy
{
    private readonly BombermanAI ai;
    private readonly Rigidbody2D rb;
    private Vector2? targetPoint;
    private Vector2 lastPosition;
    private float stuckTimer;

    public AIMoveToNearestItemPickup(BombermanAI ai, Rigidbody2D rb)
    {
        this.ai = ai;
        this.rb = rb;
    }

    public Node.Status Process()
    {
        if (targetPoint == null || ai.IsPositionUnsafe(targetPoint.Value) || !ai.IsCellWalkable(targetPoint.Value))
        {
            if (!ai.TryGetNearestItemPickup(out Vector2 foundTarget))
            {
                return Node.Status.Failure;
            }

            targetPoint = foundTarget;
        }

        if (Vector2.Distance(rb.position, targetPoint.Value) < 0.05f)
        {
            targetPoint = null;
            stuckTimer = 0f;
            return Node.Status.Success;
        }

        if (!ai.TryGetNextStepToward(targetPoint.Value, out Vector2 nextStep))
        {
            targetPoint = null;
            return Node.Status.Failure;
        }

        Vector2 currentPos = rb.position;
        float moveStep = ai.moveSpeed * Time.fixedDeltaTime;

        ai.LogBehavior("Move To Item Pickup");
        if (Vector2.Distance(currentPos, nextStep) <= moveStep + 0.001f)
        {
            rb.MovePosition(nextStep);
            rb.position = nextStep;
        }
        else
        {
            Vector2 nextPos = ai.GetAxisLockedMovePosition(currentPos, nextStep, moveStep);
            rb.MovePosition(nextPos);
        }

        if (Vector2.Distance(currentPos, lastPosition) < 0.001f)
        {
            stuckTimer += Time.fixedDeltaTime;
            if (stuckTimer >= 0.35f)
            {
                targetPoint = null;
                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = 0f;
        }
        lastPosition = currentPos;

        if (Vector2.Distance(currentPos, nextStep) <= moveStep + 0.001f)
        {
            if (targetPoint.HasValue && Vector2.Distance(nextStep, targetPoint.Value) < 0.05f)
            {
                targetPoint = null;
                stuckTimer = 0f;
            }
        }

        return Node.Status.Running;
    }

    public void Reset()
    {
        targetPoint = null;
        stuckTimer = 0f;
    }
}