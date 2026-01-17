using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public class PlayerController : MonoBehaviour
{
    public Transform cameraPivot;
    public Camera playerCamera;

    public float mouseSensitivity = 0.12f;
    public float maxPitch = 89f;
    public bool lockCursor = true;

    public float walkSpeed = 5.0f;
    public float sprintSpeed = 8.5f;
    public float crouchSpeed = 2.6f;
    public float groundAcceleration = 55f;
    public float airAcceleration = 22f;
    public float maxAirSpeed = 6.0f;

    public float jumpHeight = 1.25f;
    public float jumpCooldown = 0.08f;
    public bool holdToAutoJump = true;

    public float coyoteTime = 0.12f;
    public float jumpBufferTime = 0.12f;

    public float extraGravity = 22f;
    public float groundedStickY = -0.3f;

    public float groundCheckDistance = 0.14f;
    public float skin = 0.04f;
    public LayerMask groundMask = ~0;

    public float crouchHeightMultiplier = 0.70f;
    public float crouchCameraDrop = 0.55f;
    public float crouchLerpSpeed = 14f;

    [Header("Sneak / Edge (Minecraft-like)")]
    public bool sneakPreventsFalling = true;

    [Tooltip("Talaj-ellenőrzés a sneakeléshez: vékony BoxCast a talp alatt. (0.15-0.30 jó)")]
    public float sneakSupportDistance = 0.22f;

    [Tooltip("A talp alatti BoxCast félmagassága. (0.02-0.05 jó)")]
    public float sneakSupportHalfHeight = 0.03f;

    [Tooltip("A talp lenyomatának XZ szűkítése, hogy ne legyen fals találat peremen/falon. (0.005-0.02)")]
    public float sneakSupportInset = 0.012f;

    [Tooltip("Ha igaz: sneak közben tényleg ne lehessen leesni a peremről (XZ-t clampeli).")]
    public bool sneakNeverFall = true;

    [Tooltip("Sneak clamp bináris keresés lépésszám")]
    public int sneakClampIterations = 14;

    [Tooltip("Minimum elmozdulás küszöb (nagyon kicsinél nulláz)")]
    public float sneakMinMove = 0.0004f;

    public bool enableStepUp = true;
    public float stepHeight = 0.55f;
    public float stepForward = 0.22f;
    public float stepUpSpeed = 7f;
    public LayerMask stepMask = ~0;

    public bool autoAssignNoFrictionMaterial = true;

    Rigidbody rb;
    BoxCollider box;

    float pitch;
    Vector2 moveInput;

    bool sprintHeld;
    bool crouchHeld;
    bool jumpHeld;
    bool jumpPressedThisFrame;

    bool isGrounded;
    float lastJumpTime;
    float lastGroundedTime;
    float lastJumpPressedTime;

    float standCamLocalY;
    float crouchCamLocalY;
    float standColliderHeight;
    Vector3 standColliderCenter;
    float crouchColliderHeight;
    Vector3 crouchColliderCenter;

    PhysicsMaterial noFrictionMat;

    // sneak “anchor” (utolsó biztos pozíció, amikor volt talaj alatta)
    Vector3 lastSneakSafePos;
    float lastSneakSafeTime;
    bool hasSneakSafe;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        box = GetComponent<BoxCollider>();

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.freezeRotation = true;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;

        if (cameraPivot == null)
        {
            Camera c = GetComponentInChildren<Camera>();
            if (c != null)
            {
                playerCamera = c;
                cameraPivot = c.transform;
            }
        }
        if (playerCamera == null && cameraPivot != null)
            playerCamera = cameraPivot.GetComponent<Camera>();

        if (cameraPivot != null)
        {
            standCamLocalY = cameraPivot.localPosition.y;
            crouchCamLocalY = standCamLocalY - crouchCameraDrop;
        }

        standColliderHeight = box.size.y;
        standColliderCenter = box.center;

        crouchColliderHeight = standColliderHeight * crouchHeightMultiplier;
        float heightDiff = standColliderHeight - crouchColliderHeight;
        crouchColliderCenter = standColliderCenter - new Vector3(0f, heightDiff * 0.5f, 0f);

        if (autoAssignNoFrictionMaterial)
            EnsureNoFrictionMaterial();
    }

    void EnsureNoFrictionMaterial()
    {
        if (box.sharedMaterial != null) return;

        noFrictionMat = new PhysicsMaterial("Player_NoFriction");
        noFrictionMat.dynamicFriction = 0f;
        noFrictionMat.staticFriction = 0f;
        noFrictionMat.frictionCombine = PhysicsMaterialCombine.Minimum;
        noFrictionMat.bounceCombine = PhysicsMaterialCombine.Minimum;
        noFrictionMat.bounciness = 0f;
        box.sharedMaterial = noFrictionMat;
    }

    void Start()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Update()
    {
        ReadInput();
        HandleLook();
        HandleCrouchVisuals();

        bool wantsJump = jumpPressedThisFrame || (holdToAutoJump && jumpHeld);
        if (wantsJump) lastJumpPressedTime = Time.time;
    }

    void FixedUpdate()
    {
        UpdateGrounded();
        HandleCrouchCollider();

        UpdateSneakSafeAnchor();

        HandleMovement();
        if (enableStepUp) HandleStepUp();
        HandleJumpAndGravity();
    }

    void ReadInput()
    {
        moveInput = Vector2.zero;
        sprintHeld = false;
        crouchHeld = false;
        jumpPressedThisFrame = false;

        if (Keyboard.current == null) return;

        if (Keyboard.current.wKey.isPressed) moveInput.y += 1;
        if (Keyboard.current.sKey.isPressed) moveInput.y -= 1;
        if (Keyboard.current.dKey.isPressed) moveInput.x += 1;
        if (Keyboard.current.aKey.isPressed) moveInput.x -= 1;

        if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();

        sprintHeld = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
        crouchHeld = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;

        jumpHeld = Keyboard.current.spaceKey.isPressed;
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
            jumpPressedThisFrame = true;
    }

    void HandleLook()
    {
        if (Mouse.current == null || cameraPivot == null) return;

        Vector2 delta = Mouse.current.delta.ReadValue();
        float mx = delta.x * mouseSensitivity;
        float my = delta.y * mouseSensitivity;

        transform.Rotate(Vector3.up, mx, Space.World);

        pitch -= my;
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void HandleCrouchVisuals()
    {
        if (cameraPivot == null) return;

        float targetY = crouchHeld ? crouchCamLocalY : standCamLocalY;
        Vector3 lp = cameraPivot.localPosition;
        lp.y = Mathf.Lerp(lp.y, targetY, 1f - Mathf.Exp(-crouchLerpSpeed * Time.deltaTime));
        cameraPivot.localPosition = lp;
    }

    void HandleCrouchCollider()
    {
        if (crouchHeld)
        {
            box.size = new Vector3(box.size.x, Mathf.Lerp(box.size.y, crouchColliderHeight, 0.35f), box.size.z);
            box.center = Vector3.Lerp(box.center, crouchColliderCenter, 0.35f);
        }
        else
        {
            if (CanStandUp())
            {
                box.size = new Vector3(box.size.x, Mathf.Lerp(box.size.y, standColliderHeight, 0.35f), box.size.z);
                box.center = Vector3.Lerp(box.center, standColliderCenter, 0.35f);
            }
            else
            {
                crouchHeld = true;
            }
        }
    }

    bool CanStandUp()
    {
        Bounds b = box.bounds;

        Vector3 half = b.extents;
        half.x = Mathf.Max(0.01f, half.x - skin);
        half.z = Mathf.Max(0.01f, half.z - skin);
        half.y = Mathf.Max(0.01f, half.y - skin);

        float currentHeight = b.size.y;
        float desiredHeight = standColliderHeight * transform.lossyScale.y;
        float grow = Mathf.Max(0f, desiredHeight - currentHeight);
        if (grow <= 0.001f) return true;

        Vector3 origin = b.center;
        int mask = groundMask & ~(1 << gameObject.layer);

        return !Physics.BoxCast(origin, half, Vector3.up, out _, transform.rotation, grow, mask, QueryTriggerInteraction.Ignore);
    }

    void HandleMovement()
    {
        float speed = walkSpeed;
        if (crouchHeld) speed = crouchSpeed;
        else if (sprintHeld) speed = sprintSpeed;

        Vector3 input = new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 wishDir = transform.TransformDirection(input);
        if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();

        Vector3 v = rb.linearVelocity;
        Vector3 vXZ = new Vector3(v.x, 0f, v.z);

        Vector3 targetXZ = wishDir * speed;

        float accel = isGrounded ? groundAcceleration : airAcceleration;
        Vector3 delta = targetXZ - vXZ;
        Vector3 accelVec = Vector3.ClampMagnitude(delta, accel * Time.fixedDeltaTime);

        if (!isGrounded)
        {
            Vector3 newXZ = vXZ + accelVec;
            if (newXZ.magnitude > maxAirSpeed) newXZ = newXZ.normalized * maxAirSpeed;
            accelVec = newXZ - vXZ;
        }

        Vector3 nextXZ = vXZ + accelVec;

        // >>> Minecraft-szerű sneak: ne groundedhez kösd, hanem "van-e talaj a talpad alatt?"
        bool supportNow = HasSupportAt(rb.position);

        if (sneakPreventsFalling && crouchHeld && nextXZ.sqrMagnitude > 0.0000001f)
        {
            Vector3 disp = nextXZ * Time.fixedDeltaTime;

            // ha van support most, akkor perem-clamp
            if (supportNow)
            {
                Vector3 allowed = SolveSneakDisplacement(rb.position, disp);
                Vector3 vel = allowed / Time.fixedDeltaTime;
                rb.linearVelocity = new Vector3(vel.x, v.y, vel.z);
                return;
            }

            // ha épp nincs support (perem-jitter / 1 frame drop), de volt nemrég, akkor "NE ESEK LE"
            if (sneakNeverFall && hasSneakSafe && (Time.time - lastSneakSafeTime) <= Mathf.Max(0.05f, coyoteTime))
            {
                // próbáljuk a mozgást az utolsó safe XZ-hez kötve: ne menjünk le a peremről
                Vector3 pos = rb.position;
                Vector3 safe = lastSneakSafePos;

                // engedjük a mozgást, amíg a safe-hoz képest nem megy "kifelé"
                // legegyszerűbb: csak nullázzuk az XZ-t ebben a frame-ben (ez szünteti a leesést)
                rb.linearVelocity = new Vector3(0f, v.y, 0f);
                return;
            }
        }

        rb.linearVelocity = new Vector3(nextXZ.x, v.y, nextXZ.z);
    }

    Vector3 SolveSneakDisplacement(Vector3 pos, Vector3 disp)
    {
        if (HasSupportAt(pos + disp)) return disp;

        Vector3 dispX = new Vector3(disp.x, 0f, 0f);
        Vector3 dispZ = new Vector3(0f, 0f, disp.z);

        Vector3 allowedX = ClampAlong(pos, dispX);
        Vector3 pos2 = pos + allowedX;
        Vector3 allowedZ = ClampAlong(pos2, dispZ);

        Vector3 total = allowedX + allowedZ;

        if (total.sqrMagnitude < sneakMinMove * sneakMinMove) return Vector3.zero;
        return total;
    }

    Vector3 ClampAlong(Vector3 pos, Vector3 dispAxis)
    {
        if (dispAxis.sqrMagnitude < 0.0000001f) return Vector3.zero;
        if (HasSupportAt(pos + dispAxis)) return dispAxis;

        float lo = 0f;
        float hi = 1f;

        for (int i = 0; i < Mathf.Max(1, sneakClampIterations); i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (HasSupportAt(pos + dispAxis * mid)) lo = mid;
            else hi = mid;
        }

        Vector3 r = dispAxis * lo;
        if (r.sqrMagnitude < sneakMinMove * sneakMinMove) return Vector3.zero;
        return r;
    }

    // >>> Stabil support check: vékony BoxCast a talp alatt (nem sarok-ray, nem fal)
    bool HasSupportAt(Vector3 worldPos)
    {
        // collider world center/half a megadott pos alapján
        Vector3 scale = transform.lossyScale;
        Vector3 halfFull = Vector3.Scale(box.size * 0.5f, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        Vector3 centerOffset = Vector3.Scale(box.center, new Vector3(scale.x, scale.y, scale.z));
        Vector3 center = worldPos + transform.rotation * centerOffset;

        // talp-lenyomat XZ (kicsit szűkítve)
        float hx = Mathf.Max(0.01f, halfFull.x - sneakSupportInset);
        float hz = Mathf.Max(0.01f, halfFull.z - sneakSupportInset);

        // nagyon vékony talp
        float hy = Mathf.Clamp(sneakSupportHalfHeight, 0.01f, 0.10f);

        // a cast origint a talp fölé tesszük
        float footY = center.y - halfFull.y;
        Vector3 origin = new Vector3(center.x, footY + 0.08f + hy, center.z);
        Vector3 half = new Vector3(hx, hy, hz);

        int mask = groundMask & ~(1 << gameObject.layer);

        return Physics.BoxCast(
            origin,
            half,
            Vector3.down,
            out _,
            transform.rotation,
            Mathf.Max(0.02f, sneakSupportDistance),
            mask,
            QueryTriggerInteraction.Ignore
        );
    }

    void UpdateSneakSafeAnchor()
    {
        if (!sneakPreventsFalling || !crouchHeld)
        {
            hasSneakSafe = false;
            return;
        }

        // Csak akkor frissítjük, ha tényleg van talaj ALATT (így falnál nem “ragad rá”)
        if (HasSupportAt(rb.position))
        {
            lastSneakSafePos = rb.position;
            lastSneakSafeTime = Time.time;
            hasSneakSafe = true;
        }
    }

    void HandleStepUp()
    {
        if (!isGrounded) return;
        if (moveInput.sqrMagnitude < 0.0001f) return;

        Vector3 v = rb.linearVelocity;
        Vector3 vXZ = new Vector3(v.x, 0f, v.z);
        if (vXZ.sqrMagnitude < 0.0001f) return;

        Vector3 dir = vXZ.normalized;
        Bounds b = box.bounds;

        float footY = b.min.y + 0.06f;
        Vector3 baseOrigin = new Vector3(b.center.x, footY, b.center.z);

        Vector3 halfLow = new Vector3(
            Mathf.Max(0.01f, b.extents.x - skin),
            0.05f,
            Mathf.Max(0.01f, b.extents.z - skin)
        );

        int mask = stepMask & ~(1 << gameObject.layer);

        bool hitLow = Physics.BoxCast(baseOrigin, halfLow, dir, out _, transform.rotation, stepForward, mask, QueryTriggerInteraction.Ignore);
        if (!hitLow) return;

        Vector3 highOrigin = baseOrigin + Vector3.up * stepHeight;
        bool hitHigh = Physics.BoxCast(highOrigin, halfLow, dir, out _, transform.rotation, stepForward, mask, QueryTriggerInteraction.Ignore);
        if (hitHigh) return;

        Vector3 p = rb.position;
        p.y += stepUpSpeed * Time.fixedDeltaTime;
        rb.MovePosition(p);
    }

    void HandleJumpAndGravity()
    {
        Vector3 v = rb.linearVelocity;

        bool canJumpByGround = isGrounded || (Time.time - lastGroundedTime) <= coyoteTime;
        bool bufferedJump = (Time.time - lastJumpPressedTime) <= jumpBufferTime;

        float effectiveG = Physics.gravity.magnitude + Mathf.Max(0f, extraGravity);

        if (bufferedJump && canJumpByGround && (Time.time - lastJumpTime) >= jumpCooldown)
        {
            lastJumpTime = Time.time;
            lastJumpPressedTime = -999f;

            float jumpVel = Mathf.Sqrt(2f * effectiveG * jumpHeight);
            v.y = jumpVel;
            rb.linearVelocity = v;
            isGrounded = false;
        }

        if (!isGrounded)
        {
            rb.AddForce(Vector3.down * extraGravity, ForceMode.Acceleration);
        }
        else
        {
            if (rb.linearVelocity.y < 0f)
            {
                v = rb.linearVelocity;
                v.y = groundedStickY;
                rb.linearVelocity = v;
            }
        }
    }

    void UpdateGrounded()
    {
        Bounds b = box.bounds;

        Vector3 half = b.extents;
        half.x = Mathf.Max(0.01f, half.x - skin);
        half.z = Mathf.Max(0.01f, half.z - skin);
        half.y = Mathf.Max(0.01f, half.y - skin);

        Vector3 origin = b.center;
        int mask = groundMask & ~(1 << gameObject.layer);

        bool g1 = Physics.BoxCast(origin, half, Vector3.down, out _, transform.rotation, groundCheckDistance, mask, QueryTriggerInteraction.Ignore);
        bool g2 = Physics.Raycast(new Vector3(origin.x, b.min.y + 0.06f, origin.z), Vector3.down, out _, groundCheckDistance + 0.10f, mask, QueryTriggerInteraction.Ignore);

        isGrounded = g1 || g2;
        if (isGrounded) lastGroundedTime = Time.time;
    }

    // >>> Lerakás: a régi Expand(0.02f) túl szigorú volt, peremen blokkol.
    public bool IntersectsBlock(Vector3Int blockPos)
    {
        Bounds playerB = box.bounds;

        float shrinkXZ = 0.02f;
        float shrinkTop = 0.01f;
        float liftBottom = 0.08f;

        Vector3 min = playerB.min;
        Vector3 max = playerB.max;

        min.x += shrinkXZ; min.z += shrinkXZ;
        max.x -= shrinkXZ; max.z -= shrinkXZ;

        max.y -= shrinkTop;
        min.y += liftBottom;

        if (max.x <= min.x || max.y <= min.y || max.z <= min.z)
            return false;

        Bounds playerForPlace = new Bounds();
        playerForPlace.SetMinMax(min, max);

        Bounds blockB = new Bounds(
            (Vector3)blockPos + new Vector3(0.5f, 0.5f, 0.5f),
            Vector3.one
        );

        return playerForPlace.Intersects(blockB);
    }

    public void PlaceAboveGround(Vector3 xzAnchor, float rayStartHeight, float extraUp)
    {
        float halfH = box.bounds.extents.y;
        Vector3 start = new Vector3(xzAnchor.x, rayStartHeight, xzAnchor.z);

        int mask = groundMask & ~(1 << gameObject.layer);

        if (Physics.Raycast(start, Vector3.down, out RaycastHit hit, rayStartHeight + 1000f, mask, QueryTriggerInteraction.Ignore))
        {
            Vector3 p = transform.position;
            p.x = xzAnchor.x;
            p.z = xzAnchor.z;
            p.y = hit.point.y + halfH + extraUp;
            rb.position = p;
            rb.linearVelocity = Vector3.zero;
        }
        else
        {
            Vector3 p = new Vector3(xzAnchor.x, rayStartHeight, xzAnchor.z);
            rb.position = p;
            rb.linearVelocity = Vector3.zero;
        }
    }
}
