using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public Camera playerCamera;

    public float mouseSensitivity = 0.15f;
    public float maxLookAngle = 85f;

    public float walkSpeed = 6f;
    public float sprintSpeed = 9f;
    public float crouchSpeed = 3.2f;

    public float groundAcceleration = 30f;
    public float airAcceleration = 8f;
    public float groundFriction = 12f;

    public float jumpHeight = 1.2f;
    public float gravity = -20f;

    public float standingHeight = 1.8f;
    public float crouchHeight = 1.2f;
    public float crouchTransitionSpeed = 12f;

    public bool crouchEdgeSafety = true;
    public float maxDropWhileCrouching = 0.05f;

    public LayerMask worldMask = ~0;

    private CharacterController cc;
    private float pitch;

    private Vector3 velocity;
    private Vector3 horizVel;

    private bool isCrouching;

    private int EffectiveMask
    {
        get
        {
            int m = worldMask;
            m &= ~(1 << gameObject.layer);
            return m;
        }
    }

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();

        cc.height = standingHeight;
        cc.center = new Vector3(0, standingHeight * 0.5f, 0);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        Look();
        HandleCrouch();
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

    private void HandleCrouch()
    {
        bool wantCrouch = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
        isCrouching = wantCrouch;

        float targetH = isCrouching ? crouchHeight : standingHeight;

        if (!isCrouching && !HasHeadroomForStanding())
            targetH = cc.height;

        float newH = Mathf.Lerp(cc.height, targetH, Time.deltaTime * crouchTransitionSpeed);
        cc.height = newH;
        cc.center = new Vector3(0, cc.height * 0.5f, 0);

        if (playerCamera != null)
        {
            Vector3 camLocal = playerCamera.transform.localPosition;
            float targetCamY = cc.height - 0.1f;
            camLocal.y = Mathf.Lerp(camLocal.y, targetCamY, Time.deltaTime * crouchTransitionSpeed);
            playerCamera.transform.localPosition = camLocal;
        }
    }

    private bool HasHeadroomForStanding()
    {
        float radius = cc.radius * 0.95f;
        Vector3 center = transform.position + cc.center;

        Vector3 bottom = center + Vector3.down * (cc.height * 0.5f - radius);
        Vector3 topStanding = center + Vector3.up * (standingHeight * 0.5f - radius);

        return !Physics.CheckCapsule(bottom, topStanding, radius, EffectiveMask, QueryTriggerInteraction.Ignore);
    }

    private void Move()
    {
        bool grounded = cc.isGrounded;
        if (grounded && velocity.y < 0f) velocity.y = -2f;

        Vector2 move2 = ReadMoveInput();
        Vector3 wishDir = (transform.right * move2.x + transform.forward * move2.y);
        if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();

        bool sprinting = IsSprinting(move2);
        float targetSpeed = isCrouching ? crouchSpeed : (sprinting ? sprintSpeed : walkSpeed);

        Vector3 targetHoriz = wishDir * targetSpeed;

        float accel = grounded ? groundAcceleration : airAcceleration;
        horizVel = Vector3.MoveTowards(horizVel, targetHoriz, accel * Time.deltaTime);

        if (grounded && move2.sqrMagnitude < 0.01f)
            horizVel = Vector3.MoveTowards(horizVel, Vector3.zero, groundFriction * Time.deltaTime);

        Vector3 horizMove = horizVel * Time.deltaTime;

        if (grounded && isCrouching && crouchEdgeSafety && horizMove.sqrMagnitude > 0.0000001f)
        {
            if (!CanMoveWithoutDropping(horizMove))
                horizMove = Vector3.zero;
        }

        cc.Move(horizMove);

        bool jumpPressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        if (grounded && jumpPressed && !isCrouching)
        {
            velocity.y = Mathf.Sqrt(2f * jumpHeight * -gravity);
        }

        velocity.y += gravity * Time.deltaTime;
        cc.Move(Vector3.up * velocity.y * Time.deltaTime);
    }

    private bool CanMoveWithoutDropping(Vector3 proposedMove)
    {
        Vector3 pos = transform.position + proposedMove;

        float radius = cc.radius * 0.9f;
        Vector3 center = pos + cc.center;
        float footY = center.y - cc.height * 0.5f + radius;

        Vector3 probe = new Vector3(center.x, footY + 0.02f, center.z);

        if (!Physics.SphereCast(probe, radius, Vector3.down, out RaycastHit hit, 2f, EffectiveMask, QueryTriggerInteraction.Ignore))
            return false;

        return hit.distance <= (0.02f + maxDropWhileCrouching);
    }

    private Vector2 ReadMoveInput()
    {
        if (Keyboard.current == null) return Vector2.zero;

        float x = 0f;
        float y = 0f;
        if (Keyboard.current.aKey.isPressed) x -= 1f;
        if (Keyboard.current.dKey.isPressed) x += 1f;
        if (Keyboard.current.sKey.isPressed) y -= 1f;
        if (Keyboard.current.wKey.isPressed) y += 1f;

        Vector2 v = new Vector2(x, y);
        if (v.sqrMagnitude > 1f) v.Normalize();
        return v;
    }

    private bool IsSprinting(Vector2 move2)
    {
        if (Keyboard.current == null) return false;
        bool ctrl = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
        bool forward = move2.y > 0.5f;
        return ctrl && forward && !isCrouching;
    }

    public bool IntersectsBlock(Vector3Int blockPos)
    {
        Bounds pb = cc.bounds;
        Bounds bb = new Bounds((Vector3)blockPos + new Vector3(0.5f, 0.5f, 0.5f), Vector3.one);
        return pb.Intersects(bb);
    }
}
