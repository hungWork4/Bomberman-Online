using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerVisual : MonoBehaviour
{
    [Header("Correction")]
    [SerializeField] private float visualCorrectionReturnSpeed = 24f;
    [SerializeField] private float maxVisualOffset = 0.35f;

    private Animator anim;
    private SpriteRenderer spriteRenderer;
    [HideInInspector]public Vector2 lastDirection = new Vector2(0f, -1f);

    private readonly int animMoveX = Animator.StringToHash("MoveX");
    private readonly int animMoveY = Animator.StringToHash("MoveY");
    private readonly int animSpeed = Animator.StringToHash("Speed");
    private readonly int animLastMoveX = Animator.StringToHash("LastMoveX");
    private readonly int animLastMoveY = Animator.StringToHash("LastMoveY");

    private void Awake()
    {
        anim = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void LateUpdate()
    {
        if (transform.localPosition.sqrMagnitude <= 0.000001f)
        {
            transform.localPosition = Vector3.zero;
            return;
        }

        transform.localPosition = Vector3.MoveTowards(
            transform.localPosition,
            Vector3.zero,
            visualCorrectionReturnSpeed * Time.deltaTime);
    }

    public void SetMovement(Vector2 direction)
    {
        if (direction.sqrMagnitude > 0f)
        {
            lastDirection = direction;
        }

        anim.SetFloat(animMoveX, direction.x);
        anim.SetFloat(animMoveY, direction.y);
        anim.SetFloat(animSpeed, direction.sqrMagnitude);

        anim.SetFloat(animLastMoveX, lastDirection.x);
        anim.SetFloat(animLastMoveY, lastDirection.y);
    }

    public void ApplyCorrectionDelta(Vector2 worldDelta, bool forceResetVisual = false)
    {
        if (forceResetVisual)
        {
            transform.localPosition = Vector3.zero;
            return;
        }

        Vector3 nextOffset = transform.localPosition + (Vector3)worldDelta;
        float maxOffsetSqr = maxVisualOffset * maxVisualOffset;

        if (nextOffset.sqrMagnitude > maxOffsetSqr)
        {
            nextOffset = nextOffset.normalized * maxVisualOffset;
        }

        transform.localPosition = nextOffset;
    }

    public void SetDead(bool isDead)
    {
        anim.SetBool("IsDead", isDead);
    }

    public void SetSortingOrder(int sortingOrder)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.sortingOrder = sortingOrder;
        }
    }
}
