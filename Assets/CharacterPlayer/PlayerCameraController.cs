using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Single-authority, third-person action-RPG orbital camera inspired by the feel of
/// modern action RPGs (Genshin Impact style).
///
/// The player GameObject and the camera are treated as two *independent* rotating
/// bodies that are gently coupled:
///   - Yaw, pitch and distance are controlled by this controller only.
///   - The player asks this controller for its flattened forward/right so that its
///     movement stays camera-relative (it never reads the Camera transform directly).
///   - Other systems never write the camera transform. They only submit *requests*
///     (combat, aiming, lock-on target, cinematic ability, shake).
///
/// Responsibilities (single cohesive script, no over-engineering):
///   • Free orbit (mouse / right stick / touch) with weighted smoothing
///   • Pitch limiting
///   • Subtle, delayed, manual-input-suppressed auto-recentering
///   • Pivot smoothing + look-ahead / movement offset
///   • desired vs actual camera distance
///   • Padded, player-self-excluding spherecast collision that smoothly restores
///   • State machine: Exploration < Combat < Aiming < Ability/Cinematic
///   • Temporary additive camera shake
///   • Cinematic ability orbit (sine shaped, returns to the exact gameplay state)
///   • Teleport / respawn snap protection
///
/// Lives on the Camera GameObject. Call Initialize() once from the player controller.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class PlayerCameraController : MonoBehaviour
{
    // ------------------------------------------------------- nested types

    /// <summary>Camera states ordered by gameplay priority (higher overrides lower).</summary>
    public enum CamState { Exploration, Combat, Aiming, Ability }

    /// <summary>Data-driven request for a temporary cinematic ability shot.</summary>
    [System.Serializable]
    public class CinematicAbility
    {
        [Tooltip("Total length of the cinematic in seconds.")]
        public float duration = 1.8f;
        [Tooltip("Sideways yaw swing of the orbit (degrees). Applied as sin(pi*u), peaking mid-shot.")]
        public float orbitDegrees = 220f;
        [Tooltip("Camera pitch pulled toward at the shot's peak (degrees).")]
        public float pitch = 18f;
        [Tooltip("Distance pulled toward at the shot's peak (metres).")]
        public float distance = 3.4f;
        [Tooltip("Normalized time 0..1 where the impact/shake fires (0.55 ~ just past mid-cast).")]
        public float impactAt = 0.55f;
        [Tooltip("Shake strength fired at impact.")]
        public float shakeStrength = 0.22f;
        [Tooltip("Shake duration fired at impact.")]
        public float shakeDuration = 0.3f;
    }

    // ------------------------------------------------------------ target/pivot

    [Header("Target & Pivot")]
    [Tooltip("Root transform that is orbited (the player). Assigned via Initialize().")]
    public Transform player;
    [Tooltip("Camera orbit centre height above the player's feet.")]
    public float pivotHeight = 1.35f;
    [Tooltip("Height above the feet the camera aims at (upper body framing).")]
    public float lookHeight = 1.55f;

    // -------------------------------------------------------------- distance

    [Header("Distance & Zoom")]
    [Tooltip("Exploration camera distance.")]
    public float normalDistance = 5f;
    [Tooltip("Combat camera distance (slightly wider battlefield view).")]
    public float combatDistance = 6f;
    [Tooltip("Hard minimum distance (zoom / collision lower bound).")]
    public float minDistance = 2f;
    [Tooltip("Hard maximum distance the player can zoom out to.")]
    public float maxDistance = 8f;
    [Tooltip("Metres of zoom per mouse-wheel notch.")]
    public float zoomStep = 0.6f;

    // -------------------------------------------------------------- look/pitch

    [Header("Look & Pitch")]
    [Range(0.05f, 3f)]
    [Tooltip("Master look sensitivity. 1 = tuned default for mouse/stick/touch. Lower = slower camera.")]
    public float lookSensitivity = 1f;
    [Tooltip("Invert vertical look (mouse up = look down).")]
    public bool invertY = false;
    [Tooltip("Lowest camera pitch: negative = looks up (camera lower).")]
    public float minPitch = -22f;
    [Tooltip("Highest camera pitch: positive = looks down (camera overhead).")]
    public float maxPitch = 68f;
    [Tooltip("How fast the camera eases toward the look target (rotation smoothing). Higher = more responsive. 80+ is near-instant like a classic follow camera; lower values add a weighty drift.")]
    public float rotationSmoothing = 80f;
    [Tooltip("How fast the orbit pivot follows the player (position smoothing). Higher = less lag behind the player.")]
    public float positionSmoothing = 12f;

    // ------------------------------------------------------------ auto recenter

    [Header("Auto Recenter")]
    [Tooltip("Seconds of sustained, roughly-constant movement before the camera gently recenters.")]
    public float recenterDelay = 1.1f;
    [Tooltip("Max degrees/sec the camera drifts toward the movement heading during auto recenter.")]
    public float recenterSpeed = 28f;
    [Tooltip("If true, lateral (strafe) movement never auto-recenters.")]
    public bool disableRecenterWhileStrafing = true;

    // ------------------------------------------------------------ framing

    [Header("Framing")]
    [Tooltip("Rig look-ahead shift toward travel at full speed (metres).")]
    public float movementCameraOffset = 0.45f;
    [Tooltip("Blend (0..1) of the target toward the lock-on target. 0 = ignore targeting.")]
    [Range(0f, 1f)]
    public float combatTargetBias = 0.35f;

    // ------------------------------------------------------------ collision

    [Header("Collision")]
    [Tooltip("Layers the camera should collide against.")]
    public LayerMask collisionMask = ~0;
    [Tooltip("Collider radius used for the camera spherecast.")]
    public float collisionRadius = 0.18f;
    [Tooltip("Extra space kept between the camera and geometry.")]
    public float collisionPadding = 0.2f;
    [Tooltip("Smoothing while geometry pushes the camera in.")]
    public float collisionInSpeed = 24f;
    [Tooltip("Smoothing while the camera restores after geometry clears.")]
    public float collisionRestoreSpeed = 6f;

    // ------------------------------------------------------------ ability/shake

    [Header("Ability & Shake")]
    [Tooltip("Fallback cinematic ability used when none is supplied.")]
    public CinematicAbility defaultAbility = new CinematicAbility();

    [Header("Debug")]
    [Tooltip("Draw editor gizmos for target, desired position and collision cast.")]
    public bool showDebugGizmos = false;

    // --------------------------------------------------------- state diagnostics

    [Header("State (read-only diagnostics)")]
    [SerializeField, HideInInspector] private CamState _state = CamState.Exploration;
    /// <summary>The currently composed camera state (by priority).</summary>
    public CamState State => _state;

    // ---------------------------------------------------------------- private

    private Camera _cam;
    private Transform _playerRoot;              // root used to ignore self-collisions

    // Look target (raw) vs smoothed displayed look.
    private float _lookYaw;                     // raw target yaw (deg)
    private float _lookPitch;                   // raw target pitch (deg)
    private float _gameplayYaw;                 // smoothed yaw shown / used for movement
    private float _gameplayPitch;               // smoothed pitch shown

    // Smooth position pivot (feet) & damped look-ahead bias.
    private Vector3 _feet = Vector3.zero;
    private Vector3 _moveBias = Vector3.zero;

    // Distance handling.
    private float _zoomOffset;
    private float _actualDistance = 5f;
    private bool _camInitialized;

    // Recenter bookkeeping.
    private float _lastManualLookTime = -100f;
    private float _recenterStartTime = -100f;
    private float _lastHeading;
    private bool _sustainedHeading;

    // Player-supplied movement state (recenter + look-ahead).
    private bool _isMoving;
    private bool _isStrafing;
    private Vector3 _flatVelocity;
    private float _speedFactor;

    // Higher-priority state requests (never direct camera writes).
    private bool _requestCombat;
    private bool _requestAiming;
    private Transform _lockTarget;

    // Cinematic ability.
    private bool _cinActive;
    private CinematicAbility _cin;
    private float _cinStart;
    private bool _cinImpactFired;

    // Additive shake.
    private float _shakeStrength;
    private float _shakeDecay;
    private float _shakeSeed;

    // Non-alloc collision buffer.
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[24];

    // Mobile touch bookkeeping.
    private readonly Dictionary<int, Vector2> _touchLast = new Dictionary<int, Vector2>();
    private readonly List<Rect> _uiExclusionRects = new List<Rect>();

    // ------------------------------------------------------------------ tuning

    // Per-input-device base scales (degrees). `lookSensitivity` multiplies these so the
    // tuned feel at sensitivity = 1 is comfortable, and a settings screen can scale it.
    private const float MouseDegreesPerPixel = 0.25f;     // mouse: responsive but not twitchy
    private const float TouchDegreesPerPixel = 0.18f;     // touch drag
    private const float GamepadDegreesPerSecond = 160f;   // full right-stick deflection
    private const float LegacyMouseScale = 0.3f;          // Input Manager fallback (raw -1..1 axis)

    // ------------------------------------------------------------------ setup

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        _shakeSeed = Random.value * 100f;
    }

    /// <summary>Wire the camera up to orbit a player root. Call once from the player controller.</summary>
    public void Initialize(Transform playerRoot)
    {
        player = playerRoot;
        _playerRoot = playerRoot;

        if (_cam == null) _cam = GetComponent<Camera>();
        if (_cam == null && Camera.main != null) _cam = Camera.main;

        // Seed the look from the current camera orientation so there is no first-frame snap.
        float y = _cam != null ? _cam.transform.eulerAngles.y : 0f;
        _lookYaw = _gameplayYaw = y;
        _lookPitch = _gameplayPitch = 0f;

        // Start smoothed state on the player so we never interpolate from the world origin.
        if (playerRoot != null)
        {
            _feet = playerRoot.position;
            Vector3 toCam = _cam != null ? _cam.transform.position - _feet : Vector3.zero;
            float dist = toCam.magnitude;
            _actualDistance = dist > 0.05f ? Mathf.Clamp(dist, minDistance, maxDistance) : normalDistance;
        }
        else
        {
            _actualDistance = normalDistance;
        }

        _camInitialized = true;
    }

    // ------------------------------------------------------------- public API

    /// <summary>Flattened camera forward on the horizontal plane (movement reference).</summary>
    public Vector3 FlatForward
    {
        get
        {
            Quaternion yRot = Quaternion.Euler(0f, _gameplayYaw, 0f);
            return Vector3.ProjectOnPlane(yRot * Vector3.forward, Vector3.up).normalized;
        }
    }

    /// <summary>Flattened camera right on the horizontal plane (movement reference).</summary>
    public Vector3 FlatRight
    {
        get
        {
            Quaternion yRot = Quaternion.Euler(0f, _gameplayYaw, 0f);
            return Vector3.ProjectOnPlane(yRot * Vector3.right, Vector3.up).normalized;
        }
    }

    /// <summary>The smoothed horizontal yaw currently looked at (degrees, 0 = +Z).</summary>
    public float Yaw => _gameplayYaw;

    /// <summary>Current effective look sensitivity (the master multiplier, 0.05..3).</summary>
    public float Sensitivity => lookSensitivity;

    /// <summary>Runtime control so a settings menu can change look sensitivity without restarting.</summary>
    public void SetSensitivity(float value)
    {
        lookSensitivity = Mathf.Clamp(value, 0.05f, 3f);
    }

    /// <summary>Tell the camera about the player's locomotion (look-ahead + auto recenter).</summary>
    public void FeedMovementState(bool moving, bool strafing, Vector3 flatVelocity, float speedFactor)
    {
        _isMoving = moving;
        _isStrafing = strafing;
        _flatVelocity = flatVelocity;
        _speedFactor = speedFactor;
    }

    /// <summary>Toggle combat framing (higher than exploration, lower than aiming/ability).</summary>
    public void SetCombat(bool active)
    {
        _requestCombat = active;
        UpdateComposedState();
    }

    /// <summary>Toggle aiming (suppresses auto recenter, keeps manual look control).</summary>
    public void SetAiming(bool active)
    {
        _requestAiming = active;
        UpdateComposedState();
    }

    /// <summary>Optional soft target used for subtle combat framing (never a hard lock-on).</summary>
    public void SetLockOnTarget(Transform target)
    {
        _lockTarget = target;
    }

    /// <summary>Request a temporary cinematic ability shot. Manual look + recenter suspend; ends by duration.</summary>
    public void BeginCinematic(CinematicAbility ability)
    {
        _cin = ability != null ? ability : defaultAbility;
        _cinActive = true;
        _cinStart = Time.time;
        _cinImpactFired = false;
        UpdateComposedState();
    }

    /// <summary>Update the active cinematic's total duration once the real clip length is known.</summary>
    public void SetCinematicDuration(float duration)
    {
        if (_cin != null && duration > 0.05f) _cin.duration = duration;
    }

    /// <summary>Abort an active cinematic and smoothly return to gameplay framing.</summary>
    public void CancelCinematic()
    {
        _cinActive = false;
        _cinImpactFired = false;
        UpdateComposedState();
    }

    /// <summary>Additive, temporary shake. Does not mutate the underlying look rotation.</summary>
    public void PlayShake(float strength, float duration)
    {
        if (strength <= 0f || duration <= 0f) return;
        _shakeStrength = strength;
        _shakeDecay = duration > 0f ? Mathf.Log(2f) / duration : 1f;   // half-life ~ duration
    }

    /// <summary>Feed zoom input (mouse wheel / pinch). Positive = zoom in (closer).</summary>
    public void FeedZoom(float delta)
    {
        _zoomOffset = Mathf.Clamp(_zoomOffset - delta * zoomStep,
            minDistance - normalDistance, maxDistance - normalDistance);
    }

    /// <summary>Register a UI rect that must never be treated as a camera drag (mobile buttons).</summary>
    public void RegisterUiExclusionRect(Rect rect)
    {
        _uiExclusionRects.Add(rect);
    }

    /// <summary>Instantly snap the rig to the player (spawn / teleport / respawn).</summary>
    public void SnapToTargetNow()
    {
        if (player != null) _feet = player.position;
        _actualDistance = Mathf.Clamp(_actualDistance, minDistance, maxDistance);
    }

    // ------------------------------------------------------------- state logic

    /// <summary>Compose the effective state from individual requests by priority.</summary>
    private void UpdateComposedState()
    {
        if (_cinActive) _state = CamState.Ability;
        else if (_requestAiming) _state = CamState.Aiming;
        else if (_requestCombat) _state = CamState.Combat;
        else _state = CamState.Exploration;
    }

    // ------------------------------------------------------------ per-frame

    private void LateUpdate()
    {
        if (!_camInitialized || player == null) return;

        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        float now = Time.time;

        bool cinematic = _cinActive;
        bool aiming = _requestAiming;
        bool combat = _requestCombat;

        // ---- 1. Manual look + auto recenter (suspended while a cinematic owns the shot). ----
        if (!cinematic)
        {
            bool manual = ReadManualLook();
            if (manual) _lastManualLookTime = now;
            UpdateRecenter(now, dt, aiming);
        }

        // ---- 2. Weighted, frame-rate independent smoothing of yaw/pitch toward targets. ----
        float kRot = 1f - Mathf.Exp(-rotationSmoothing * dt);
        _gameplayYaw = DampAngleTo(_gameplayYaw, _lookYaw, kRot);
        _gameplayPitch = Mathf.Lerp(_gameplayPitch, _lookPitch, kRot);
        _gameplayPitch = Mathf.Clamp(_gameplayPitch, minPitch, maxPitch);

        // ---- 3. Cinematic ability shaping (sine: starts & ends exactly on the gameplay look). ----
        float cinShape = 0f;
        if (cinematic && _cin != null)
        {
            float u = Mathf.Clamp01((now - _cinStart) / Mathf.Max(0.05f, _cin.duration));
            cinShape = Mathf.Sin(Mathf.PI * u);

            if (!_cinImpactFired && u >= _cin.impactAt)     // fire shake exactly once
            {
                _cinImpactFired = true;
                PlayShake(_cin.shakeStrength, _cin.shakeDuration);
            }
            if (u >= 1f)
            {
                _cinActive = false;                          // shape(1) == 0 -> seamless return
                UpdateComposedState();
            }
        }

        // ---- 4. Smooth pivot follow with teleport protection (never swing across a map). ----
        Vector3 targetFeet = player.position;
        if ((_feet - targetFeet).sqrMagnitude > 3f * 3f) _feet = targetFeet;
        else
        {
            float kPos = 1f - Mathf.Exp(-positionSmoothing * dt);
            _feet = Vector3.Lerp(_feet, targetFeet, kPos);
        }

        // ---- 5. Damped look-ahead bias toward travel. ----
        Vector3 biasTarget = Vector3.zero;
        if (_isMoving && _flatVelocity.sqrMagnitude > 0.01f)
            biasTarget = _flatVelocity.normalized * (movementCameraOffset * _speedFactor);
        float kBias = 1f - Mathf.Exp(-(positionSmoothing * 0.5f) * dt);
        _moveBias = Vector3.Lerp(_moveBias, biasTarget, kBias);

        // ---- 6. Subtle combat framing toward the lock target (never a hard lock-on). ----
        Vector3 combatBias = Vector3.zero;
        if (combat && _lockTarget != null && combatTargetBias > 0f)
        {
            Vector3 to = _lockTarget.position - player.position;
            float d = to.magnitude;
            if (d > 0.05f && d < 20f)
            {
                Vector3 mid = player.position + to * 0.5f;
                Vector3 horiz = mid - _feet; horiz.y = 0f;
                if (horiz.sqrMagnitude > 0.01f)
                    combatBias = horiz.normalized * Mathf.Min(d, 8f) * 0.15f * combatTargetBias;
            }
        }

        // ---- 7. Anchor & look points (orbit centre near chest, aim near upper body). ----
        Vector3 anchor = _feet + Vector3.up * pivotHeight + _moveBias + combatBias;
        Vector3 lookPoint = _feet + Vector3.up * lookHeight + _moveBias * 0.6f + combatBias;

        // ---- 8. Resolve distance (base -> state/zoom -> cinematic -> collision). ----
        float baseDist = combat ? combatDistance : normalDistance;
        float desired = Mathf.Clamp(baseDist + _zoomOffset, minDistance, maxDistance);

        float effYaw = _gameplayYaw;
        float effPitch = _gameplayPitch;
        if (cinematic && _cin != null)
        {
            effYaw = _gameplayYaw + _cin.orbitDegrees * cinShape;
            effPitch = Mathf.Clamp(Mathf.Lerp(_gameplayPitch, _cin.pitch, cinShape), minPitch, maxPitch);
            desired = Mathf.Lerp(desired, _cin.distance, cinShape);
        }

        float collideLimit = ComputeCollisionDistance(anchor, effYaw, effPitch, desired);
        float distGoal = Mathf.Max(minDistance, Mathf.Min(desired, collideLimit));

        bool restoring = distGoal >= _actualDistance;             // in fast, restore slow
        float kDist = 1f - Mathf.Exp(-(restoring ? collisionRestoreSpeed : collisionInSpeed) * dt);
        _actualDistance = Mathf.Lerp(_actualDistance, distGoal, kDist);

        // ---- 9. Place camera: anchor + back direction (carries pitch) * distance. ----
        Quaternion lookRot = Quaternion.Euler(effPitch, effYaw, 0f);
        Vector3 camPos = anchor + (lookRot * Vector3.back) * _actualDistance;

        // ---- 10. Additive, temporary shake (never writes the permanent look rotation). ----
        camPos += ComputeShakeOffset(dt);

        // ---- 11. Write the camera transform (single authority). ----
        Vector3 dir = lookPoint - camPos;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.position = camPos;
    }

    // --------------------------------------------------------------- look input

    /// <summary>Reads mouse / right stick / touch and returns true if any manual look occurred.</summary>
    private bool ReadManualLook()
    {
        Vector2 look = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        // Mouse (desktop): only meaningful while the cursor is locked.
        if (Cursor.lockState == CursorLockMode.Locked && Mouse.current != null)
        {
            look += Mouse.current.delta.ReadValue() * MouseDegreesPerPixel;
            Vector2 scroll = Mouse.current.scroll.ReadValue();
            if (Mathf.Abs(scroll.y) > 0.01f) FeedZoom(scroll.y);
        }
        // Gamepad right stick.
        if (Gamepad.current != null)
        {
            Vector2 stick = Gamepad.current.rightStick.ReadValue();
            if (stick.sqrMagnitude > 0.001f)
                look += stick * (GamepadDegreesPerSecond * Time.deltaTime);
        }
        // Touch drag (mobile) — UI-excluded zones never rotate the camera.
        ReadTouchLook(ref look);
#else
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            look += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * LegacyMouseScale;
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f) FeedZoom(scroll);
        }
#endif

        if (look.sqrMagnitude <= 0.0001f) return false;

        float sens = Sensitivity;
        _lookYaw = Mathf.Repeat(_lookYaw + look.x * sens, 360f);
        _lookPitch = Mathf.Clamp(_lookPitch + look.y * sens * (invertY ? -1f : 1f),
            minPitch, maxPitch);
        return true;
    }

    private void ReadTouchLook(ref Vector2 look)
    {
#if ENABLE_INPUT_SYSTEM
        Touchscreen ts = Touchscreen.current;
        if (ts == null) return;
        float rightThreshold = Screen.width * 0.35f;          // left area = movement joystick
        foreach (UnityEngine.InputSystem.Controls.TouchControl t in ts.touches)
        {
            if (!t.press.isPressed) continue;
            Vector2 pos = t.position.ReadValue();
            if (pos.x < rightThreshold) continue;
            if (PointInExcludedRect(pos)) continue;

            int id = t.touchId.ReadValue();
            if (_touchLast.TryGetValue(id, out Vector2 prev))
                look += (pos - prev) * TouchDegreesPerPixel;
            _touchLast[id] = pos;
        }
#else
        if (Input.touchCount <= 0) return;
        float rightThreshold = Screen.width * 0.35f;
        for (int i = 0; i < Input.touchCount; i++)
        {
            UnityEngine.Touch t = Input.GetTouch(i);
            if (t.phase != UnityEngine.TouchPhase.Moved) continue;
            if (t.position.x < rightThreshold) continue;
            if (PointInExcludedRect(t.position)) continue;
            int id = t.fingerId;
            if (_touchLast.TryGetValue(id, out Vector2 prev))
                look += (t.position - prev) * TouchDegreesPerPixel;
            _touchLast[id] = t.position;
        }
#endif
    }

    private bool PointInExcludedRect(Vector2 screenPos)
    {
        for (int i = 0; i < _uiExclusionRects.Count; i++)
            if (_uiExclusionRects[i].Contains(screenPos)) return true;
        return false;
    }

    // ------------------------------------------------------------- auto recenter

    /// <summary>Gently drifts the look target behind the travel heading after consistent motion + delay.</summary>
    private void UpdateRecenter(float now, float dt, bool aiming)
    {
        if (aiming) return;                                   // aiming suppresses auto recenter

        bool trackable = _isMoving && !(disableRecenterWhileStrafing && _isStrafing);
        if (!trackable)
        {
            _sustainedHeading = false;
            _recenterStartTime = now;
            return;
        }

        float heading = Mathf.Atan2(_flatVelocity.x, _flatVelocity.z) * Mathf.Rad2Deg;
        float change = Mathf.Abs(Mathf.DeltaAngle(_lastHeading, heading));
        _lastHeading = heading;

        if (change > 35f || !_sustainedHeading)
        {
            _sustainedHeading = change <= 35f;
            _recenterStartTime = now;                        // restart count on direction change
        }

        if (!_sustainedHeading) return;
        if (now - _recenterStartTime < recenterDelay) return;
        if (now - _lastManualLookTime < recenterDelay) return; // manual input has priority

        _lookYaw = Mathf.Repeat(MoveTowardAngle(_lookYaw, heading, recenterSpeed * dt), 360f);
    }

    // --------------------------------------------------------------- collision

    /// <summary>Farthest usable distance toward the desired one, honoring geometry (self-excluding).</summary>
    private float ComputeCollisionDistance(Vector3 anchor, float yaw, float pitch, float desired)
    {
        Vector3 dir = Quaternion.Euler(pitch, yaw, 0f) * Vector3.back;

        int count = Physics.SphereCastNonAlloc(anchor, collisionRadius, dir, _hitBuffer,
            desired + collisionPadding, collisionMask, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (IsPlayerCollider(_hitBuffer[i].collider)) continue;
            best = Mathf.Min(best, _hitBuffer[i].distance);
        }

        if (best >= float.MaxValue) return desired;
        return Mathf.Max(minDistance, Mathf.Min(desired, best - collisionPadding));
    }

    private bool IsPlayerCollider(Collider c)
    {
        if (c == null || _playerRoot == null) return false;
        return c.transform == _playerRoot || c.transform.IsChildOf(_playerRoot);
    }

    // ------------------------------------------------------------------ shake

    private Vector3 ComputeShakeOffset(float dt)
    {
        if (_shakeStrength <= 0.0001f) return Vector3.zero;

        _shakeStrength *= Mathf.Exp(-_shakeDecay * dt);
        if (_shakeStrength < 0.001f)
        {
            _shakeStrength = 0f;
            return Vector3.zero;
        }

        float t = Time.time;
        Vector3 n = new Vector3(
            Mathf.PerlinNoise(_shakeSeed, t * 23f) - 0.5f,
            Mathf.PerlinNoise(_shakeSeed + 7f, t * 29f) - 0.5f,
            Mathf.PerlinNoise(_shakeSeed + 13f, t * 17f) - 0.5f);

        return transform.TransformDirection(n * _shakeStrength);
    }

    // ---------------------------------------------------------------- utilities

    private static float MoveTowardAngle(float from, float to, float maxDelta)
    {
        float d = Mathf.DeltaAngle(from, to);
        if (Mathf.Abs(d) <= maxDelta) return to;
        return from + Mathf.Sign(d) * maxDelta;
    }

    private static float DampAngleTo(float from, float to, float t)
    {
        return from + Mathf.DeltaAngle(from, to) * Mathf.Clamp01(t);
    }

    // ------------------------------------------------------------------ gizmos

    private void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos || player == null) return;

        Vector3 anchor = player.position + Vector3.up * pivotHeight;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(anchor + _moveBias, 0.25f);

        Gizmos.color = Color.yellow;
        Vector3 cam = anchor + _moveBias
            + (Quaternion.Euler(_gameplayPitch, _gameplayYaw, 0f) * Vector3.back) * _actualDistance;
        Gizmos.DrawWireSphere(cam, collisionRadius);

        Gizmos.color = new Color(1f, 0.6f, 0f, 0.6f);
        Gizmos.DrawLine(anchor + _moveBias, cam);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(player.position + Vector3.up * lookHeight + _moveBias, 0.12f);
    }
}
