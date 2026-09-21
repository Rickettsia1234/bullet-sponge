using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : NetworkBehaviour
{
    public float moveSpeed = 50f;
    public float maxSpeedX = 5f;
    public float xDeceleration = 25f;
    public float yDrag = 0.5f;
    public float jumpForce = 7f;
    public LayerMask groundLayer;

    private Rigidbody2D rb;
    private float moveX;
    private bool jumpRequested;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            CinemachineCamera cinemachineCamera = FindAnyObjectByType<CinemachineCamera>();
            cinemachineCamera.Target.TrackingTarget = transform;
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        if (!IsOwner) return;

        moveX = 0f;
        bool jumpThisFrame = false;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) moveX -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) moveX += 1f;

            bool isGrounded = Physics2D.Raycast(transform.position, Vector2.down, 0.6f, groundLayer);

            if (Keyboard.current.spaceKey.wasPressedThisFrame && isGrounded)
            {
                jumpThisFrame = true;
            }
        }

        SubmitInputServerRpc(moveX, jumpThisFrame);
    }

    [ServerRpc]
    private void SubmitInputServerRpc(float inputX, bool jump)
    {
        moveX = inputX;
        if (jump)
        {
            jumpRequested = true;
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        if (moveX != 0f)
        {
            rb.AddForce(new Vector2(moveX * moveSpeed, 0f), ForceMode2D.Force);
            float clampedX = Mathf.Clamp(rb.linearVelocity.x, -maxSpeedX, maxSpeedX);
            rb.linearVelocity = new Vector2(clampedX, rb.linearVelocity.y);
        }
        else
        {
            float newX = Mathf.MoveTowards(rb.linearVelocity.x, 0f, xDeceleration * Time.fixedDeltaTime);
            rb.linearVelocity = new Vector2(newX, rb.linearVelocity.y);
        }

        if (jumpRequested)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            jumpRequested = false;
        }

        if (rb.linearVelocity.y != 0f)
        {
            rb.AddForce(new Vector2(0f, -rb.linearVelocity.y * yDrag), ForceMode2D.Force);
        }
    }
}