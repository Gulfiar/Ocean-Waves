// General-Purpose Free Camera Controller
// Compatible with both Legacy Input Manager and New Input System.
// No external dependencies — just attach to any Camera GameObject.

using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// A general-purpose free-look camera controller (fly camera).
/// Supports WASD movement, mouse look, scroll-wheel zoom, sprint,
/// vertical movement (Q/E), and optional cursor locking.
/// Works with both Legacy Input Manager and New Input System.
/// </summary>
public class CamController : MonoBehaviour
{
    // ───────────────────────── Movement ─────────────────────────
    [Header("Movement")]
    [Tooltip("Base movement speed in units per second.")]
    public float moveSpeed = 10f;

    [Tooltip("Sprint speed multiplier when holding Shift.")]
    public float sprintMultiplier = 3f;

    [Tooltip("Smoothing factor for movement (0 = instant, higher = smoother). Set to 0 to disable.")]
    [Range(0f, 0.5f)]
    public float moveSmoothTime = 0.1f;

    // ───────────────────────── Rotation ─────────────────────────
    [Header("Rotation")]
    [Tooltip("Mouse look sensitivity.")]
    public float lookSensitivity = 2f;

    [Tooltip("Smoothing factor for rotation (0 = instant, higher = smoother). Set to 0 to disable.")]
    [Range(0f, 0.5f)]
    public float lookSmoothTime = 0.02f;

    [Tooltip("Clamp the vertical look angle. Set to 90 for full range.")]
    [Range(0f, 90f)]
    public float maxVerticalAngle = 89f;

    [Tooltip("Allow the camera to roll on the Z axis.")]
    public bool allowRoll = false;

    // ────────────────────────── Zoom ────────────────────────────
    [Header("Scroll Zoom")]
    [Tooltip("Speed of scroll-wheel zoom (affects FOV or dolly). Set to 0 to disable.")]
    public float scrollZoomSpeed = 10f;

    [Tooltip("Use FOV zoom instead of dolly (move forward/backward).")]
    public bool useFovZoom = false;

    [Tooltip("Min / Max FOV when using FOV zoom.")]
    public Vector2 fovClamp = new Vector2(15f, 90f);

    // ───────────────────────── Cursor ───────────────────────────
    [Header("Cursor")]
    [Tooltip("Lock and hide the cursor on play.")]
    public bool lockCursorOnStart = false;

    [Tooltip("If true, mouse look only works while holding the right mouse button.")]
    public bool requireRightMouseToLook = true;

    // ──────────────────────── Boundary ──────────────────────────
    [Header("Boundary (Optional)")]
    [Tooltip("Enable position clamping.")]
    public bool useBoundary = false;

    [Tooltip("Minimum boundary position (world space).")]
    public Vector3 boundaryMin = new Vector3(-500, 0, -500);

    [Tooltip("Maximum boundary position (world space).")]
    public Vector3 boundaryMax = new Vector3(500, 200, 500);

    // ───────────────────── Internal State ───────────────────────
    Camera _camera;
    float _yaw;
    float _pitch;
    Vector3 _currentVelocity;
    Vector2 _currentLookVelocity;
    Vector2 _targetLookDelta;

    void Awake()
    {
        _camera = GetComponent<Camera>();

        // Initialize rotation from current transform
        Vector3 euler = transform.eulerAngles;
        _yaw = euler.y;
        _pitch = euler.x;
        // Normalize pitch to [-180, 180] so clamping works correctly
        if (_pitch > 180f) _pitch -= 360f;
    }

    void Start()
    {
        if (lockCursorOnStart)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Update()
    {
        if (!Application.isFocused) return;

        float dt = Time.deltaTime;

        HandleCursorToggle();
        HandleLook(dt);
        HandleMovement(dt);
        HandleScroll(dt);
        ClampPosition();
        KillRoll();
    }

    // ═══════════════════════════════════════════════════════════
    //  INPUT HELPERS — abstract away Legacy vs New Input System
    // ═══════════════════════════════════════════════════════════

    #region Input Helpers

    static bool GetKey(KeyCode legacy
#if ENABLE_INPUT_SYSTEM
        , Key newKey
#endif
    )
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current[newKey].isPressed;
#else
        return Input.GetKey(legacy);
#endif
    }

    static bool GetKeyDown(KeyCode legacy
#if ENABLE_INPUT_SYSTEM
        , Key newKey
#endif
    )
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current[newKey].wasPressedThisFrame;
#else
        return Input.GetKeyDown(legacy);
#endif
    }

    static bool GetMouseButton(int button)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null) return false;
        return button switch
        {
            0 => Mouse.current.leftButton.isPressed,
            1 => Mouse.current.rightButton.isPressed,
            2 => Mouse.current.middleButton.isPressed,
            _ => false,
        };
#else
        return Input.GetMouseButton(button);
#endif
    }

    static Vector2 GetMouseDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#else
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#endif
    }

    static float GetScrollDelta()
    {
#if ENABLE_INPUT_SYSTEM
        return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
        return Input.GetAxis("Mouse ScrollWheel") * 10f;
#endif
    }

    #endregion

    // ═══════════════════════════════════════════════════════════
    //  CURSOR
    // ═══════════════════════════════════════════════════════════

    void HandleCursorToggle()
    {
        // Press Escape to toggle cursor lock
        if (GetKeyDown(KeyCode.Escape
#if ENABLE_INPUT_SYSTEM
            , Key.Escape
#endif
        ))
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  LOOK
    // ═══════════════════════════════════════════════════════════

    void HandleLook(float dt)
    {
        bool canLook = !requireRightMouseToLook || GetMouseButton(1)
                       || Cursor.lockState == CursorLockMode.Locked;

        if (!canLook)
        {
            _targetLookDelta = Vector2.zero;
            return;
        }

        Vector2 mouseDelta = GetMouseDelta();

        // Scale sensitivity — Legacy Input axes are already frame-rate compensated,
        // but the New Input System gives raw pixel deltas, so no dt multiplication needed.
        _targetLookDelta = mouseDelta * lookSensitivity;

        // Optional smoothing
        Vector2 smoothed;
        if (lookSmoothTime > 0f)
        {
            smoothed = Vector2.SmoothDamp(_currentLookVelocity, _targetLookDelta,
                ref _currentLookVelocity, lookSmoothTime, Mathf.Infinity, dt);
        }
        else
        {
            smoothed = _targetLookDelta;
            _currentLookVelocity = smoothed;
        }

        _yaw += smoothed.x;
        _pitch -= smoothed.y;
        _pitch = Mathf.Clamp(_pitch, -maxVerticalAngle, maxVerticalAngle);

        transform.eulerAngles = new Vector3(_pitch, _yaw, 0f);
    }

    // ═══════════════════════════════════════════════════════════
    //  MOVEMENT
    // ═══════════════════════════════════════════════════════════

    void HandleMovement(float dt)
    {
        // Gather directional input
        float forward = 0f, right = 0f, up = 0f;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            forward += Keyboard.current.wKey.isPressed ? 1f : 0f;
            forward -= Keyboard.current.sKey.isPressed ? 1f : 0f;
            right   += Keyboard.current.dKey.isPressed ? 1f : 0f;
            right   -= Keyboard.current.aKey.isPressed ? 1f : 0f;
            up      += Keyboard.current.eKey.isPressed ? 1f : 0f;
            up      -= Keyboard.current.qKey.isPressed ? 1f : 0f;
        }
#else
        forward += Input.GetKey(KeyCode.W) ? 1f : 0f;
        forward -= Input.GetKey(KeyCode.S) ? 1f : 0f;
        right   += Input.GetKey(KeyCode.D) ? 1f : 0f;
        right   -= Input.GetKey(KeyCode.A) ? 1f : 0f;
        up      += Input.GetKey(KeyCode.E) ? 1f : 0f;
        up      -= Input.GetKey(KeyCode.Q) ? 1f : 0f;
#endif

        Vector3 inputDir = new Vector3(right, up, forward).normalized;

        // World-space desired velocity
        Vector3 desiredMove = transform.TransformDirection(inputDir);

        // Sprint
        float speed = moveSpeed;
        if (GetKey(KeyCode.LeftShift
#if ENABLE_INPUT_SYSTEM
            , Key.LeftShift
#endif
        ))
        {
            speed *= sprintMultiplier;
        }

        Vector3 targetVelocity = desiredMove * speed;

        // Optional smoothing
        if (moveSmoothTime > 0f)
        {
            transform.position += Vector3.SmoothDamp(Vector3.zero, targetVelocity,
                ref _currentVelocity, moveSmoothTime, Mathf.Infinity, dt) * dt;
        }
        else
        {
            transform.position += targetVelocity * dt;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  SCROLL ZOOM
    // ═══════════════════════════════════════════════════════════

    void HandleScroll(float dt)
    {
        if (scrollZoomSpeed <= 0f) return;

        float scroll = GetScrollDelta();
        if (Mathf.Approximately(scroll, 0f)) return;

        if (useFovZoom && _camera != null)
        {
            _camera.fieldOfView = Mathf.Clamp(
                _camera.fieldOfView - scroll * scrollZoomSpeed * dt * 60f,
                fovClamp.x, fovClamp.y);
        }
        else
        {
            // Dolly zoom — move camera forward/backward
            transform.position += transform.forward * scroll * scrollZoomSpeed * dt * 10f;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  BOUNDARY
    // ═══════════════════════════════════════════════════════════

    void ClampPosition()
    {
        if (!useBoundary) return;

        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, boundaryMin.x, boundaryMax.x);
        pos.y = Mathf.Clamp(pos.y, boundaryMin.y, boundaryMax.y);
        pos.z = Mathf.Clamp(pos.z, boundaryMin.z, boundaryMax.z);
        transform.position = pos;
    }

    // ═══════════════════════════════════════════════════════════
    //  KILL ROLL
    // ═══════════════════════════════════════════════════════════

    void KillRoll()
    {
        if (allowRoll) return;

        Vector3 ea = transform.eulerAngles;
        ea.z = 0f;
        transform.eulerAngles = ea;
    }
}
