using UnityEngine;
using UnityEngine.Windows;
using Input = UnityEngine.Input;

[RequireComponent(typeof(Rigidbody2D), typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;

    private Rigidbody2D rb;
    private Animator anim;
    private Vector2 direction;
    private Vector2 lastDirection;

    // Sử dụng StringToHash để tối ưu hiệu suất thay vì dùng string trực tiếp
    private readonly int animMoveX = Animator.StringToHash("MoveX");
    private readonly int animMoveY = Animator.StringToHash("MoveY");
    private readonly int animSpeed = Animator.StringToHash("Speed");
    private readonly int animLastMoveX = Animator.StringToHash("LastMoveX");
    private readonly int animLastMoveY = Animator.StringToHash("LastMoveY");

    [Header("Input")]
    public KeyCode inputUp = KeyCode.W;
    public KeyCode inputDown = KeyCode.S;
    public KeyCode inputLeft = KeyCode.A;
    public KeyCode inputRight = KeyCode.D;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();

        // Setup Rigidbody2D để phù hợp với top-down
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        // Mặc định nhân vật hướng xuống dưới
        lastDirection = new Vector2(0, -1);
    }

    private void Update()
    {
        if (Input.GetKey(inputUp))
        {
            direction =Vector2.up;
            // 2. Lưu lại hướng cuối cùng để chạy animation Idle chính xác
            if (direction.sqrMagnitude > 0)
            {
                lastDirection = direction;
            }
        }
        else if (Input.GetKey(inputDown))
        {
            direction = Vector2.down;
            // 2. Lưu lại hướng cuối cùng để chạy animation Idle chính xác
            if (direction.sqrMagnitude > 0)
            {
                lastDirection = direction;
            }
        }
        else if (Input.GetKey(inputLeft))
        {
            direction = Vector2.left;
            // 2. Lưu lại hướng cuối cùng để chạy animation Idle chính xác
            if (direction.sqrMagnitude > 0)
            {
                lastDirection = direction;
            }
        }
        else if (Input.GetKey(inputRight))
        {
            direction = Vector2.right;
            // 2. Lưu lại hướng cuối cùng để chạy animation Idle chính xác
            if (direction.sqrMagnitude > 0)
            {
                lastDirection = direction;
            }
        }
        else
        {
            direction = Vector2.zero;
        }



        // 3. Cập nhật các tham số cho Animator
        UpdateAnimator();
    }

    private void FixedUpdate()
    {
        // 4. Xử lý di chuyển trong FixedUpdate để vật lý mượt mà hơn
        Vector2 position = rb.position;
        Vector2 translation = moveSpeed * Time.fixedDeltaTime * direction;

        rb.MovePosition(position + translation);
        //rb.MovePosition(rb.position + movement * (moveSpeed * Time.fixedDeltaTime));
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.gameObject.layer == LayerMask.NameToLayer("Explosion"))
        {
            DeathSequence();
            anim.SetBool("IsDead", true);
        }
    }
    private void DeathSequence()
    {
        enabled = false;
        GetComponent<BombController>().enabled = false;
        GetComponent<SpriteRenderer>().sortingOrder = 1;
        GetComponent<Collider2D>().enabled = false;
        Invoke(nameof(OnDeathSequenceEnded), 1.25f);
    }
    private void OnDeathSequenceEnded()
    {
        //gameObject.SetActive(false);
        //GameManager.Instance.CheckWinState();
        Debug.Log("Player has died. Implement death handling logic here.");
    }
    private void UpdateAnimator()
    {
        // Truyền giá trị đang di chuyển
        anim.SetFloat(animMoveX, direction.x);
        anim.SetFloat(animMoveY, direction.y);
        anim.SetFloat(animSpeed, direction.sqrMagnitude);

        // Truyền giá trị hướng mặt cuối cùng (dùng cho Blend Tree Idle)
        anim.SetFloat(animLastMoveX, lastDirection.x);
        anim.SetFloat(animLastMoveY, lastDirection.y);
    }
}