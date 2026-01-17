using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public Camera playerCamera;
    public string playerLayerName = "Player";

    [Header("Look")]
    public float mouseSensitivity = 0.15f;
    public float maxLookAngle = 85f;

    [Header("Minecraft-like Speeds (units = blocks)")]
    [Tooltip("Minecraft walk kb. 4.317 b/s")]
    public float walkSpeed = 4.317f;

    [Tooltip("Minecraft sprint kb. 5.612 b/s")]
    public float sprintSpeed = 5.612f;

    [Tooltip("Minecraft sneak kb. ~1.31 b/s (ízlés szerint)")]
    public float crouchSpeed = 1.31f;

    [Header("Horizontal Control (Minecraft-ish feel)")]
    [Tooltip("Földön gyorsulás (b/s^2 jelleg).")]
    public float groundAcceleration = 40f;

    [Tooltip("Levegőben gyorsulás (kisebb = Minecraftosabb).")]
    public float airAcceleration = 12f;

    [Tooltip("Földi csúszás/ellenállás. Nagyobb = gyorsabban megáll.")]
    public float groundFriction = 18f;

    [Header("Minecraft Vertical Physics (tick based)")]
    [Tooltip("Minecraft 20 tick/s")]
    public float mcTicksPerSecond = 20f;

    [Tooltip("Minecraft gravity per tick (v = v - 0.08) blocks/tick")]
    public float mcGravityPerTick = 0.08f;

    [Tooltip("Minecraft air drag per tick (v *= 0.98)")]
    [Range(0.5f, 1.0f)]
    public float mcAirDragPerTick = 0.98f;

    [Tooltip("Minecraft jump velocity per tick (0.42 blocks/tick)")]
    public float mcJumpVelocityPerTick = 0.42f;

    [Tooltip("Terminal fall speed in blocks/tick (Minecraft kb. ~3.92).")]
    public float mcTerminalFallPerTick = 3.92f;

    [Header("Crouch (NO CharacterController resize!)")]
    public float crouchCameraDrop = 0.6f;
    public float crouchTransitionSpeed = 12f;
    public bool crouchHold = true;
    public Key crouchKey = Key.LeftShift;

    public Transform visualRoot;
    public float crouchVisualScaleY = 0.75f;

    [Header("Sneak Edge Lock (Minecraft-like)")]
    public bool crouchEdgeSafety = true;

    [Tooltip("Sneak közben csak akkor enged tovább, ha a láb alatt ennyi távolságon belül van talaj.")]
    public float sneakCheckDistance = 0.35f;

    [Range(0.5f, 0.99f)]
    public float sneakProbeInset = 0.93f;

    public float sneakProbeUp = 0.08f;
    public float footEpsilon = 0.02f;

    [Header("Custom Ground Check")]
    public float groundCheckDistance = 0.2f;
    [Range(0.3f, 1.0f)]
    public float groundCheckRadiusFactor = 0.9f;

    [Header("Physics")]
    public LayerMask worldMask = ~0;

    private CharacterController cc;
    private float pitch;

    private Vector3 horizVel;    // world-space horizontal velocity (x,z)
    private float vertVel;       // world-space vertical velocity (y)

    // Crouch state
    private bool crouchTarget;
    private bool isCrouching;
    private float crouchT;
    private bool crouchToggleLatched;

    private float camStandY;
    private float camCrouchY;
    private Vector3 visualStandScale;

    private bool jumpQueued;           // space lenyomás jelleg (tickhez)
    private bool jumpHeldConsumed;     // ha tartod space-t, csak egyszer ugorjon landolás után

    private float tickAccumulator;

    private int playerLayer = -1;

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

        if (playerCamera != null)
        {
            camStandY = playerCamera.transform.localPosition.y;
            camCrouchY = camStandY - crouchCameraDrop;
        }

        if (visualRoot != null)
            visualStandScale = visualRoot.localScale;

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

        crouchTarget = false;
        isCrouching = false;
        crouchT = 0f;
        crouchToggleLatched = false;

        horizVel = Vector3.zero;
        vertVel = 0f;
        tickAccumulator = 0f;

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

    private void Update()
    {
        Look();
        HandleCrouchShift();
        ReadJumpInput();
        MoveMinecraftLike();
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

    // ===== New crouch/shift =====
    private void HandleCrouchShift()
    {
        bool keyDown = IsKeyPressed(crouchKey);

        if (crouchHold)
        {
            crouchTarget = keyDown;
        }
        else
        {
            if (keyDown && !crouchToggleLatched)
            {
                crouchTarget = !crouchTarget;
                crouchToggleLatched = true;
            }
            if (!keyDown) crouchToggleLatched = false;
        }

        float targetT = crouchTarget ? 1f : 0f;
        crouchT = Mathf.MoveTowards(crouchT, targetT, Time.deltaTime * crouchTransitionSpeed);
        isCrouching = crouchT > 0.5f;

        if (playerCamera != null)
        {
            float y = Mathf.Lerp(camStandY, camCrouchY, crouchT);
            Vector3 lp = playerCamera.transform.localPosition;
            lp.y = y;
            playerCamera.transform.localPosition = lp;
        }

        if (visualRoot != null)
        {
            Vector3 crouchScale = new Vector3(
                visualStandScale.x,
                visualStandScale.y * crouchVisualScaleY,
                visualStandScale.z
            );
            visualRoot.localScale = Vector3.Lerp(visualStandScale, crouchScale, crouchT);
        }
    }

    private bool IsKeyPressed(Key key)
    {
        if (Keyboard.current == null) return false;

        switch (key)
        {
            case Key.LeftShift: return Keyboard.current.leftShiftKey.isPressed;
            case Key.RightShift: return Keyboard.current.rightShiftKey.isPressed;
            case Key.LeftCtrl: return Keyboard.current.leftCtrlKey.isPressed;
            case Key.RightCtrl: return Keyboard.current.rightCtrlKey.isPressed;
            case Key.C: return Keyboard.current.cKey.isPressed;
            case Key.X: return Keyboard.current.xKey.isPressed;
            case Key.Z: return Keyboard.current.zKey.isPressed;
            default: return false;
        }
    }

    private void ReadJumpInput()
    {
        if (Keyboard.current == null) return;

        bool spaceHeld = Keyboard.current.spaceKey.isPressed;

        // "space-hold autojump" jelleg: csak egyszer ugorjon, amíg nem engeded fel / nem esel el a talajról.
        if (!spaceHeld) jumpHeldConsumed = false;

        if (spaceHeld && !jumpHeldConsumed)
        {
            // queue-zzuk, a tick rendszer majd elintézi, ha grounded
            jumpQueued = true;
        }
    }

    // ===== Minecraft-like movement =====
    private void MoveMinecraftLike()
    {
        // Grounded logika: CC + custom
        bool groundedForLogic = cc.isGrounded || IsGroundedCustom();

        // Horizontal input
        Vector2 move2 = ReadMoveInput();
        Vector3 wishDir = (transform.right * move2.x + transform.forward * move2.y);
        wishDir.y = 0f;
        if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();

        bool sprinting = IsSprinting(move2);
        float targetSpeed = isCrouching ? crouchSpeed : (sprinting ? sprintSpeed : walkSpeed);

        Vector3 targetHorizVel = wishDir * targetSpeed;

        float accel = groundedForLogic ? groundAcceleration : airAcceleration;
        horizVel = Vector3.MoveTowards(horizVel, targetHorizVel, accel * Time.deltaTime);

        if (groundedForLogic && move2.sqrMagnitude < 0.01f)
            horizVel = Vector3.MoveTowards(horizVel, Vector3.zero, groundFriction * Time.deltaTime);

        // Proposed horizontal move
        Vector3 horizMove = horizVel * Time.deltaTime;

        // ===== Fix: Minecraft sneak edge-lock (diagonálisan is) =====
        if (isCrouching && crouchEdgeSafety && groundedForLogic && horizMove.sqrMagnitude > 0.0000001f)
            horizMove = ClampMoveOnEdge_Minecraft_LocalAxes(horizMove);

        // Apply horizontal
        cc.Move(horizMove);

        // ===== Vertical: tick-based MC physics =====
        float tickDt = (mcTicksPerSecond <= 0.01f) ? 0.05f : (1f / mcTicksPerSecond);
        tickAccumulator += Time.deltaTime;

        // Ha nagyon lagos frame, akkor se pörögjön végtelen sok tick:
        int safety = 0;
        int maxTicksPerFrame = 5;

        while (tickAccumulator >= tickDt && safety < maxTicksPerFrame)
        {
            safety++;
            tickAccumulator -= tickDt;

            // Tick elején friss grounded (CC mozgás után)
            groundedForLogic = cc.isGrounded || IsGroundedCustom();

            if (groundedForLogic)
            {
                // talajon: kicsi tapadás, ne “lebegjen”
                if (vertVel < 0f) vertVel = 0f;

                // ugrás tick-ben
                if (jumpQueued)
                {
                    float jumpVelPerSec = mcJumpVelocityPerTick / tickDt; // (blocks/tick) -> (blocks/s)
                    vertVel = jumpVelPerSec;
                    jumpQueued = false;
                    jumpHeldConsumed = true;
                }
                else
                {
                    // ha talajon vagy és nem ugrunk, ne maradjon beragadt queue
                    jumpQueued = false;
                }
            }

            // Gravity + drag (Minecraft: v = (v - 0.08) * 0.98)
            float gravityImpulsePerSec = (mcGravityPerTick / tickDt); // (blocks/tick) -> (blocks/s^2 jelleg)
            vertVel = (vertVel - gravityImpulsePerSec) * mcAirDragPerTick;

            // Terminal velocity clamp (Minecraft kb. 3.92 blocks/tick)
            float terminalPerSec = mcTerminalFallPerTick / tickDt;
            if (vertVel < -terminalPerSec) vertVel = -terminalPerSec;
        }

        // Apply vertical per-frame (CC Move)
        cc.Move(Vector3.up * (vertVel * Time.deltaTime));
    }

    // Diagonál javítás: nem world X/Z szerint bontunk, hanem a PLAYER right/forward tengelyei szerint
    private Vector3 ClampMoveOnEdge_Minecraft_LocalAxes(Vector3 proposedMove)
    {
        // csak XZ
        proposedMove.y = 0f;

        Vector3 right = transform.right; right.y = 0f; right.Normalize();
        Vector3 forward = transform.forward; forward.y = 0f; forward.Normalize();

        float rAmt = Vector3.Dot(proposedMove, right);
        float fAmt = Vector3.Dot(proposedMove, forward);

        Vector3 rMove = right * rAmt;
        Vector3 fMove = forward * fAmt;

        Vector3 candidate = transform.position + proposedMove;

        // teljes mozgás ok?
        if (HasNearSupportAt(candidate)) return proposedMove;

        bool rOk = rMove.sqrMagnitude > 0.0000001f && HasNearSupportAt(transform.position + rMove);
        bool fOk = fMove.sqrMagnitude > 0.0000001f && HasNearSupportAt(transform.position + fMove);

        if (rOk && fOk) return proposedMove;
        if (rOk) return rMove;
        if (fOk) return fMove;
        return Vector3.zero;
    }

    // ==== Ground / support checks ====
    private bool IsGroundedCustom()
    {
        Bounds b = cc.bounds;

        float r = Mathf.Max(0.01f, cc.radius * groundCheckRadiusFactor);
        float startY = b.min.y + r + 0.02f;
        Vector3 origin = new Vector3(b.center.x, startY, b.center.z);

        float dist = groundCheckDistance;
        int mask = EffectiveMask;

        if (Physics.SphereCast(origin, r, Vector3.down, out RaycastHit hit, dist, mask, QueryTriggerInteraction.Ignore))
            return hit.normal.y > 0.2f;

        return false;
    }

    // “Közeli” alátámasztás: több pontból lefelé raycast, és csak közelit fogad el
    private bool HasNearSupportAt(Vector3 candidatePos)
    {
        Bounds bNow = cc.bounds;
        Vector3 delta = candidatePos - transform.position;

        Vector3 c = bNow.center + delta;
        Vector3 e = bNow.extents;

        float footY = (bNow.min.y + delta.y) + footEpsilon;
        float startY = footY + sneakProbeUp;

        float r = Mathf.Min(e.x, e.z) * sneakProbeInset;

        Vector3[] points =
        {
            new Vector3(c.x, startY, c.z),
            new Vector3(c.x + r, startY, c.z),
            new Vector3(c.x - r, startY, c.z),
            new Vector3(c.x, startY, c.z + r),
            new Vector3(c.x, startY, c.z - r)
        };

        float dist = sneakProbeUp + sneakCheckDistance;
        int mask = EffectiveMask;

        for (int i = 0; i < points.Length; i++)
        {
            if (Physics.Raycast(points[i], Vector3.down, out RaycastHit hit, dist, mask, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y > 0.2f) return true;
            }
        }

        return false;
    }

    // ==== Input helpers ====
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

    // ==== Utility (unchanged) ====
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
            float y = hit.point.y + (cc.height * 0.5f) + extraUp;

            bool we = cc.enabled;
            cc.enabled = false;
            transform.position = new Vector3(xzWorld.x, y, xzWorld.z);
            cc.enabled = we;
            Physics.SyncTransforms();
        }

        horizVel = Vector3.zero;
        vertVel = 0f;
        tickAccumulator = 0f;
        jumpQueued = false;
        jumpHeldConsumed = false;
    }

    public bool IntersectsBlock(Vector3Int blockPos)
    {
        Bounds pb = cc.bounds;
        Bounds bb = new Bounds((Vector3)blockPos + new Vector3(0.5f, 0.5f, 0.5f), Vector3.one);
        return pb.Intersects(bb);
    }
}
