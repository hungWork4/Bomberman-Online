using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerVisual : MonoBehaviour
{
    [Header("Correction")]
    [SerializeField] private float visualCorrectionReturnSpeed = 12f;
    [SerializeField] private float maxVisualOffset = 0.6f;

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

        // Smoothly decay visual offset to local zero as physics root catches up
        Vector3 target = Vector3.zero;
        Vector3 nextLocalPos = Vector3.Lerp(transform.localPosition, target, visualCorrectionReturnSpeed * Time.deltaTime);
        nextLocalPos = Vector3.MoveTowards(nextLocalPos, target, 0.05f * Time.deltaTime);
        transform.localPosition = nextLocalPos;
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

    public void AddVisualOffset(Vector3 delta)
    {
        Vector3 nextOffset = transform.localPosition + delta;
        float maxOffsetSqr = maxVisualOffset * maxVisualOffset;

        if (nextOffset.sqrMagnitude > maxOffsetSqr)
        {
            nextOffset = nextOffset.normalized * maxVisualOffset;
        }

        transform.localPosition = nextOffset;
    }

    public void ResetVisualOffset()
    {
        transform.localPosition = Vector3.zero;
    }

    // Retained for backward compatibility
    public void AdvanceVisualInput(Vector2 direction, float speed, float deltaTime) { }
    public void OffsetVisualLocal(Vector3 delta) => AddVisualOffset(delta);
    public void ApplyCorrectionDelta(Vector2 worldDelta, bool forceResetVisual = false)
    {
        if (forceResetVisual) ResetVisualOffset();
        else AddVisualOffset((Vector3)worldDelta);
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

    public Sprite GetCurrentSprite()
    {
        return spriteRenderer != null ? spriteRenderer.sprite : null;
    }
}
