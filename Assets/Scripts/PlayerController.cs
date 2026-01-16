using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public Camera playerCamera;

    public string playerLayerName = "Player";

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
    public float maxDropWhileCrouching = 0.02f;

    public LayerMask worldMask = ~0;

    private CharacterController cc;
    private float pitch;

    private Vector3 velocity;
    private Vector3 horizVel;

    private bool isCrouching;

    private int playerLayer = -1;

    private float camStandY;
    private float camCrouchY;

    // Space-hold autojump: egyszer ugrik egy "grounded szakaszban",
    // és csak akkor enged újra, ha elhagyta a talajt (vagy elengedte a space-t).
    private bool jumpHeldConsumed;

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

        if (playerCamera != null)
        {
            camStandY = playerCamera.transform.localPosition.y;
            camCrouchY = camStandY - (standingHeight - crouchHeight);
        }

        playerLayer = LayerMask.NameToLayer(playerLayerName);
        if (playerLayer >= 0)
        {
            ApplyLayerToVisuals(playerLayer);

            if (playerCamera != null)
            {
                playerCamera.cullingMask &= ~(1 << playerLayer);
                playerCamera.gameObject.layer = 0;
            }
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void ApplyLayerToVisuals(int layer)
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            if (playerCamera != null && renderers[i].transform.IsChildOf(playerCamera.transform)) continue;
            renderers[i].gameObject.layer = layer;
        }
    }

    public void PlaceAboveGround(Vector3 xzWorld, float castUp, float extraUp)
    {
        Vector3 start = new Vector3(xzWorld.x, castUp, xzWorld.z);

        bool wasEnabled = cc.enabled;
        cc.enabled = false;
        transform.position = start;
        cc.enabled = wasEnabled;

        Physics.SyncTransforms();

        int mask = EffectiveMask;
        if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, castUp + 2000f, mask, QueryTriggerInteraction.Ignore))
        {
            // Megjegyzés: ha a Player transform skálázva van, a CC valós mérete is skálázódik.
            // A legjobb: a Player root scale = (1,1,1).
            float y = hit.point.y + (cc.height * 0.5f) + extraUp;

            bool we = cc.enabled;
            cc.enabled = false;
            transform.position = new Vector3(xzWorld.x, y, xzWorld.z);
            cc.enabled = we;
            Physics.SyncTransforms();
        }

        velocity = Vector3.zero;
        horizVel = Vector3.zero;
        jumpHeldConsumed = false;
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
            float targetCamY = isCrouching ? camCrouchY : camStandY;
            Vector3 lp = playerCamera.transform.localPosition;
            lp.y = Mathf.Lerp(lp.y, targetCamY, Time.deltaTime * crouchTransitionSpeed);
            playerCamera.transform.localPosition = lp;
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
            horizMove = ClampMoveOnEdge(horizMove);

        cc.Move(horizMove);

        // ---- JUMP LOGIC (Shift közben is + space-hold autojump) ----
        bool spaceHeld = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;

        // ha a levegőben vagyunk, újra engedjük a "hold ugrást"
        if (!grounded)
            jumpHeldConsumed = false;

        // ha talajon vagyunk és nincs nyomva a space, szintén engedjük
        if (grounded && !spaceHeld)
            jumpHeldConsumed = false;

        // Autojump: ha talajon vagyunk és space nyomva van -> egy ugrás / grounded szakasz
        if (grounded && spaceHeld && !jumpHeldConsumed)
        {
            velocity.y = Mathf.Sqrt(2f * jumpHeight * -gravity);
            jumpHeldConsumed = true;
        }

        velocity.y += gravity * Time.deltaTime;
        cc.Move(Vector3.up * velocity.y * Time.deltaTime);
    }

    private Vector3 ClampMoveOnEdge(Vector3 proposedMove)
    {
        if (CanStepWithoutDropping(proposedMove)) return proposedMove;

        Vector3 xOnly = new Vector3(proposedMove.x, 0f, 0f);
        Vector3 zOnly = new Vector3(0f, 0f, proposedMove.z);

        bool xOk = xOnly.sqrMagnitude > 0.0000001f && CanStepWithoutDropping(xOnly);
        bool zOk = zOnly.sqrMagnitude > 0.0000001f && CanStepWithoutDropping(zOnly);

        if (xOk && zOk) return proposedMove;
        if (xOk) return xOnly;
        if (zOk) return zOnly;
        return Vector3.zero;
    }

    private bool CanStepWithoutDropping(Vector3 proposedMove)
    {
        if (!GetGroundYAt(transform.position, out float yNow)) return false;
        if (!GetGroundYAt(transform.position + proposedMove, out float yNext)) return false;
        return (yNow - yNext) <= maxDropWhileCrouching;
    }

    private bool GetGroundYAt(Vector3 pos, out float groundY)
    {
        Vector3 center = pos + cc.center;
        float radius = cc.radius * 0.95f;
        float footY = center.y - cc.height * 0.5f + radius;

        Vector3 origin = new Vector3(center.x, footY + 0.25f, center.z);

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 2.0f, EffectiveMask, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
            return true;
        }

        groundY = 0f;
        return false;
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
        if (isCrouching) return false;
        bool ctrl = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
        bool forward = move2.y > 0.5f;
        return ctrl && forward;
    }

    public bool IntersectsBlock(Vector3Int blockPos)
    {
        Bounds pb = cc.bounds;
        Bounds bb = new Bounds((Vector3)blockPos + new Vector3(0.5f, 0.5f, 0.5f), Vector3.one);
        return pb.Intersects(bb);
    }
}
