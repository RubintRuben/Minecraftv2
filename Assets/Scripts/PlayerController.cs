using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Refs")]
    public Camera playerCamera;

    [Header("Look")]
    public float mouseSensitivity = 0.15f;
    public float maxLookAngle = 85f;

    [Header("Move")]
    public float moveSpeed = 6f;
    public float sprintMultiplier = 1.5f;
    public float jumpHeight = 1.2f;
    public float gravity = -20f;

    private CharacterController controller;
    private float pitch;
    private Vector3 velocity;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        Look();
        Move();
    }

    private void Look()
    {
        if (Mouse.current == null) return;

        Vector2 delta = Mouse.current.delta.ReadValue();
        float mx = delta.x * mouseSensitivity;
        float my = delta.y * mouseSensitivity;

        transform.Rotate(Vector3.up * mx);

        pitch -= my;
        pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

        if (playerCamera != null)
            playerCamera.transform.localEulerAngles = new Vector3(pitch, 0, 0);
    }

    private void Move()
    {
        bool grounded = controller.isGrounded;
        if (grounded && velocity.y < 0) velocity.y = -2f;

        Vector2 move2 = Vector2.zero;
        if (Keyboard.current != null)
        {
            float x = 0;
            float y = 0;
            if (Keyboard.current.aKey.isPressed) x -= 1;
            if (Keyboard.current.dKey.isPressed) x += 1;
            if (Keyboard.current.sKey.isPressed) y -= 1;
            if (Keyboard.current.wKey.isPressed) y += 1;
            move2 = new Vector2(x, y);
            if (move2.sqrMagnitude > 1f) move2.Normalize();
        }

        Vector3 move = (transform.right * move2.x + transform.forward * move2.y);

        float speed = moveSpeed;
        if (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed)
            speed *= sprintMultiplier;

        controller.Move(move * speed * Time.deltaTime);

        bool jumpPressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        if (grounded && jumpPressed)
        {
            velocity.y = Mathf.Sqrt(2f * jumpHeight * -gravity);
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
}
