using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MysticMap
{
    /// <summary>
    /// The Mystic Map player: a bouncing slime seen from a third-person camera.
    ///
    /// WASD / arrow keys (or the left stick) steer the slime, Shift runs and the mouse (or the
    /// right stick) orbits the camera. There is no jump - a slime hops, and it hops on its own
    /// while it is moving.
    ///
    /// The imported slime model has no animation clips (mesh + blend shapes only), so the
    /// bounce is generated procedurally instead of played back: every time the slime lands it
    /// squashes and launches itself up again, stretching as it rises. That is the whole
    /// locomotion - no Avatar, Animator or humanoid rig is involved anywhere.
    ///
    /// It drives an existing CharacterController (which it sizes to the model when
    /// "autoFitController" is on) so it follows the terrain and the streamed procedural chunks
    /// with gravity, and it places the third-person camera itself, so no camera rig is needed.
    ///
    /// The model keeps whatever rotation the importer gave it (a Z-up FBX root carries an axis
    /// conversion there), so the rest pose is captured on start-up and the squash is applied
    /// along the model's own up axis instead of assuming Y.
    ///
    /// Abilities can steer the slime while they charge: <see cref="aimLock"/> holds the facing
    /// steady, <see cref="FaceDirection"/> aims it, <see cref="speedScale"/> slows the hopping
    /// down and <see cref="ShakeCamera"/> punches the view (see <see cref="SlimeMagic"/>).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    [AddComponentMenu("MysticMap/Slime Player (3rd person)")]
    public class SlimePlayer : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Normal hopping speed in m/s.")]
        public float moveSpeed = 5f;
        [Tooltip("Speed while Shift / sprint is held.")]
        public float runSpeed = 8f;
        [Tooltip("How fast (degrees/s) the model turns to face where it is going.")]
        public float turnSpeed = 720f;

        [Header("Bounce")]
        [Tooltip("Upward speed of every hop. Higher = taller, floatier bounces.")]
        public float hopSpeed = 4.4f;
        [Tooltip("Downward acceleration (higher = snappier, heavier feel).")]
        public float gravity = 22f;
        [Tooltip("Minimum time on the ground between two hops (keeps a landing from double-firing).")]
        public float hopRest = 0.08f;
        [Tooltip("Also hop when standing still (idle bouncing).")]
        public bool bounceWhileIdle = false;
        [Tooltip("Gentle breathing squash while resting.")]
        public bool idleBreathe = true;
        [Tooltip("How flat the slime is when it lands (0 = rigid, 0.5 = very squishy).")]
        [Range(0f, 0.6f)] public float squash = 0.32f;
        [Tooltip("How tall the slime is at the top of a hop.")]
        [Range(0f, 0.6f)] public float stretch = 0.3f;
        [Tooltip("Vertical speed -> stretch factor.")]
        public float bounceStretch = 0.05f;
        [Tooltip("How quickly the squash/stretch follows the bounce. Higher = snappier.")]
        public float squashBlend = 18f;

        [Header("Model")]
        [Tooltip("The slime mesh (a child of this object). Auto-filled from the first renderer.")]
        public Transform model;
        [Tooltip("Yaw correction if the model does not look down its own +Z axis.")]
        public float modelYawOffset = 0f;
        [Tooltip("Scale the model so it is exactly 'slimeHeight' tall (the source FBX scale is ignored).")]
        public bool normalizeModelHeight = true;
        [Tooltip("World height of the slime in metres.")]
        public float slimeHeight = 1f;
        [Tooltip("Size the CharacterController to the model instead of using the values in the inspector.")]
        public bool autoFitController = true;

        [Header("Third-person camera")]
        [Tooltip("The camera that follows the slime. Auto-filled from a child or Camera.main.")]
        public Camera cam;
        [Tooltip("How far behind the slime the camera sits.")]
        public float cameraDistance = 4.5f;
        [Tooltip("Pivot height above the slime's feet (the camera looks at this point).")]
        public float cameraHeight = 0.9f;
        [Tooltip("Sideways offset of the camera (over-the-shoulder).")]
        [Range(-2f, 2f)] public float cameraSideOffset = 0f;
        [Tooltip("Mouse / stick look sensitivity.")]
        public float lookSensitivity = 2f;
        [Tooltip("Flip vertical look.")]
        public bool invertY = false;
        [Tooltip("How far down the camera may look, in degrees.")]
        public float minPitch = -35f;
        [Tooltip("How far up the camera may look, in degrees.")]
        public float maxPitch = 70f;
        [Tooltip("Follow smoothing in seconds (0 = rigid). Damps the bounce so the view stays calm.")]
        [Range(0f, 0.5f)] public float followSmoothing = 0.12f;
        [Tooltip("Pull the camera in when a wall would get between it and the slime.")]
        public bool cameraCollision = true;
        [Tooltip("What the camera collides with.")]
        public LayerMask collisionMask = ~0;
        [Tooltip("Thickness of the camera's collision probe.")]
        public float collisionRadius = 0.25f;

        [Header("Cursor")]
        [Tooltip("Capture the mouse cursor for look control while playing.")]
        public bool lockCursor = true;

        CharacterController _cc;
        float _yaw;
        float _pitch;
        float _verticalVelocity;
        float _hopTimer;
        bool _cursorLocked;

        // Hooks other components (the magic system) use: a speed multiplier, an aim lock and a
        // camera shake. They are runtime state only, so they are never saved into the scene.
        [System.NonSerialized] public float speedScale = 1f;
        [System.NonSerialized] public bool aimLock;
        float _shakeAmplitude;
        float _shakeTime;
        float _shakeFor;

        // Model rest pose, measured once so squash / stretch stays anchored to the ground.
        Vector3 _restScale = Vector3.one;
        float _restLocalY;
        float _bottomY;
        float _scaleY = 1f;

        // An FBX root may carry an axis-conversion rotation (a Z-up file gets a -90 degree X),
        // so the model is never forced upright: the rest orientation is remembered and both the
        // facing yaw and the squash are applied around it.
        Quaternion _restRotation = Quaternion.identity;
        int _upAxis = 1;              // the model's own axis that points up: 0 = X, 1 = Y, 2 = Z
        bool _restPoseCaptured;

        // Camera state.
        Vector3 _pivot;
        Vector3 _pivotVelocity;
        float _pivotHeight = 0.9f;
        readonly RaycastHit[] _hitBuffer = new RaycastHit[8];

        struct InputState
        {
            public Vector2 move;    // x = strafe, y = forward
            public Vector2 look;    // mouse delta in pixels (or stick * time)
            public bool sprint;
            public bool click;
        }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (cam == null) cam = GetComponentInChildren<Camera>(true);
            if (cam == null) cam = Camera.main;
            if (model == null) model = FindModel();

            // Keep whatever orientation the scene was saved with.
            _yaw = transform.eulerAngles.y;
            if (cam != null)
            {
                float p = cam.transform.eulerAngles.x;
                _pitch = (p > 180f) ? p - 360f : p;   // inspector values are 0..360
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }

            FitToModel();

            // Start facing away from the camera (around whatever orientation the model came in with).
            if (model != null)
                model.rotation = Quaternion.Euler(0f, _yaw + modelYawOffset, 0f) * _restRotation;

            _pivot = PivotPoint();
        }

        void OnEnable() => SetCursor(lockCursor);

        void OnDisable() => SetCursor(false);

        void Update()
        {
            InputState input = Poll();

            Look(input.look);
            Move(input);
            AnimateModel(input.move);
            HandleCursorToggle(input);
        }

        // The camera is placed after the movement so it never lags a frame behind.
        void LateUpdate() => PlaceCamera();

        /// <summary>
        /// Measures the slime model, optionally normalises its height to <see cref="slimeHeight"/>
        /// and sizes the CharacterController + camera pivot to it. The editor setup tool calls
        /// this too, so the player already looks right in the scene view before Play.
        /// </summary>
        public void FitToModel()
        {
            if (_cc == null) _cc = GetComponent<CharacterController>();

            if (model == null)
            {
                _pivotHeight = Mathf.Max(cameraHeight, 0.5f);
                return;
            }

            CaptureRestPose();

            _restScale = model.localScale;
            _restLocalY = model.localPosition.y;

            if (!TryGetModelBounds(out Bounds bounds)) return;

            if (normalizeModelHeight && bounds.size.y > 0.0001f)
            {
                model.localScale = _restScale * (Mathf.Max(0.01f, slimeHeight) / bounds.size.y);
                if (!TryGetModelBounds(out bounds)) return;
            }

            _bottomY = bounds.min.y;
            _restScale = model.localScale;
            _restLocalY = model.localPosition.y;

            float bodyHeight = Mathf.Max(0.05f, bounds.size.y);
            float bodyRadius = Mathf.Max(0.02f, Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f);

            if (autoFitController && _cc != null)
            {
                // A capsule whose bottom sits on the object's origin (= the slime's feet).
                float h = Mathf.Max(bodyRadius * 2f, bodyHeight);
                _cc.height = h;
                _cc.radius = Mathf.Min(bodyRadius, h * 0.5f - 0.001f);
                _cc.center = new Vector3(0f, h * 0.5f, 0f);
                _cc.stepOffset = Mathf.Min(0.3f, h * 0.25f);
                _cc.slopeLimit = 45f;
                _cc.skinWidth = 0.03f;
            }

            // Look at the middle of the slime, never below its feet.
            _pivotHeight = Mathf.Max(cameraHeight, bodyHeight * 0.5f);
        }

        /// <summary>
        /// Remembers how the model came out of the FBX importer, once per Play session: the rest
        /// rotation (a Z-up file arrives with a -90 degree X on its root) and which of the model's
        /// own axes points up. Scaling happens before that rotation, so the vertical half of the
        /// squash has to travel along <see cref="_upAxis"/> instead of always Y.
        /// </summary>
        void CaptureRestPose()
        {
            if (_restPoseCaptured || model == null) return;
            _restPoseCaptured = true;

            _restRotation = model.localRotation;

            Vector3 up = Quaternion.Inverse(_restRotation) * Vector3.up;
            float ax = Mathf.Abs(up.x), ay = Mathf.Abs(up.y), az = Mathf.Abs(up.z);
            _upAxis = (ax > ay && ax > az) ? 0 : (az > ay ? 2 : 1);
        }

        /// <summary>
        /// Snaps the follow camera to where the rig wants it right now, skipping the smoothing.
        /// The editor setup tool and <see cref="Teleport"/> use it so what you see in the scene
        /// view already matches the game view before Play.
        /// </summary>
        public void SnapCamera()
        {
            if (cam == null) cam = GetComponentInChildren<Camera>(true);
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            _pivot = PivotPoint();
            _pivotVelocity = Vector3.zero;
            PlaceCamera();
        }

        /// <summary>The first child that actually has geometry (skips cameras, lights and audio).</summary>
        Transform FindModel()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child == null) continue;
                if (child.GetComponent<Camera>() != null) continue;
                if (child.GetComponentInChildren<Renderer>(true) != null) return child;
            }
            return null;
        }

        /// <summary>Bounds of everything the model draws, expressed in this object's local space.</summary>
        bool TryGetModelBounds(out Bounds localBounds)
        {
            localBounds = new Bounds();
            if (model == null) return false;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;

                Bounds b = r.bounds;   // world space
                Vector3 c = b.center, e = b.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        c.x + ((i & 1) == 0 ? -e.x : e.x),
                        c.y + ((i & 2) == 0 ? -e.y : e.y),
                        c.z + ((i & 4) == 0 ? -e.z : e.z));
                    Vector3 local = transform.InverseTransformPoint(corner);
                    if (!any) { localBounds = new Bounds(local, Vector3.zero); any = true; }
                    else localBounds.Encapsulate(local);
                }
            }
            return any;
        }

        // =====================================================================
        //  Look
        // =====================================================================
        void Look(Vector2 look)
        {
            if (cam == null) return;

            float sens = Mathf.Max(0.01f, lookSensitivity) * 0.1f;
            _yaw += look.x * sens;
            _pitch += (invertY ? look.y : -look.y) * sens;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        // =====================================================================
        //  Move (steer + bounce)
        // =====================================================================
        void Move(InputState input)
        {
            // Steering is relative to where the camera looks, like any third-person game.
            Quaternion flat = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 dir = flat * new Vector3(input.move.x, 0f, input.move.y);
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            float speed = (input.sprint ? Mathf.Max(moveSpeed, runSpeed) : moveSpeed) * Mathf.Max(0.05f, speedScale);
            bool moving = dir.sqrMagnitude > 0.0001f;

            if (_cc.isGrounded)
            {
                // Small downward push so the controller hugs slopes and the ground stays detected.
                if (_verticalVelocity < 0f) _verticalVelocity = -2f;

                _hopTimer -= Time.deltaTime;
                if (_hopTimer <= 0f && (moving || bounceWhileIdle))
                {
                    _verticalVelocity = Mathf.Max(0.1f, hopSpeed);   // launch the next hop
                    _hopTimer = Mathf.Max(0f, hopRest);
                }
            }

            _verticalVelocity -= Mathf.Max(0.01f, gravity) * Time.deltaTime;

            Vector3 velocity = dir * speed + Vector3.up * _verticalVelocity;
            _cc.Move(velocity * Time.deltaTime);

            FaceTravelDirection(dir);
        }

        void FaceTravelDirection(Vector3 dir)
        {
            if (model == null || dir.sqrMagnitude < 0.0001f) return;
            if (aimLock) return;      // an ability (a charging spell) is holding the aim

            Quaternion want = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, modelYawOffset, 0f) * _restRotation;
            model.rotation = Quaternion.RotateTowards(model.rotation, want, Mathf.Max(0f, turnSpeed) * Time.deltaTime);
        }

        // =====================================================================
        //  Bounce visuals (squash / stretch)
        // =====================================================================
        void AnimateModel(Vector2 move)
        {
            if (model == null) return;

            float target;
            if (_cc.isGrounded)
            {
                bool moving = move.sqrMagnitude > 0.0001f;
                if (moving) target = 1f - squash;                                         // squashed on impact
                else if (idleBreathe) target = 1f + Mathf.Sin(Time.time * 2.2f) * 0.04f;  // gentle breathing
                else target = 1f;
            }
            else
            {
                target = 1f + Mathf.Clamp(_verticalVelocity * Mathf.Max(0f, bounceStretch), -squash, stretch);
            }

            float t = 1f - Mathf.Exp(-Mathf.Max(0.01f, squashBlend) * Time.deltaTime);
            _scaleY = Mathf.Lerp(_scaleY, Mathf.Clamp(target, 0.4f, 1.8f), t);

            float sxz = 1f / Mathf.Sqrt(_scaleY);   // keep the slime's volume roughly constant

            // Squash along the model's own "up" axis (Y for the usual FBX, but an imported file
            // may carry its up on X or Z), and widen the other two.
            Vector3 scale = new Vector3(_restScale.x * sxz, _restScale.y * sxz, _restScale.z * sxz);
            if (_upAxis == 0) scale.x = _restScale.x * _scaleY;
            else if (_upAxis == 1) scale.y = _restScale.y * _scaleY;
            else scale.z = _restScale.z * _scaleY;

            model.localScale = scale;

            // Keep the bottom of the slime planted while it squashes (the model's pivot may not
            // be at its feet, so the offset is measured instead of assumed).
            float y = _restLocalY + (_bottomY - _restLocalY) * (1f - _scaleY);
            model.localPosition = new Vector3(model.localPosition.x, y, model.localPosition.z);
        }

        // =====================================================================
        //  Third-person camera
        // =====================================================================
        Vector3 PivotPoint() => transform.position + Vector3.up * _pivotHeight;

        void PlaceCamera()
        {
            if (cam == null) return;

            Vector3 pivot = PivotPoint();
            if (followSmoothing > 0f)
                _pivot = Vector3.SmoothDamp(_pivot, pivot, ref _pivotVelocity, followSmoothing);
            else
                _pivot = pivot;

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 back = rot * Vector3.back;

            float dist = Mathf.Max(0.2f, cameraDistance);
            if (cameraCollision) dist = ClampCameraDistance(_pivot, back, dist);

            Vector3 pos = _pivot + back * dist + rot * (Vector3.right * cameraSideOffset);

            // A spell can kick the camera for a moment (see ShakeCamera).
            if (_shakeFor > 0f)
            {
                _shakeTime += Time.deltaTime;
                float falloff = 1f - Mathf.Clamp01(_shakeTime / _shakeFor);

                if (falloff <= 0f)
                {
                    _shakeFor = 0f;
                    _shakeAmplitude = 0f;
                }
                else
                {
                    pos += Random.insideUnitSphere * (_shakeAmplitude * falloff);
                }
            }

            cam.transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Shortens the boom when something solid sits between the pivot and the camera.</summary>
        float ClampCameraDistance(Vector3 pivot, Vector3 dir, float wanted)
        {
            int count = Physics.SphereCastNonAlloc(
                pivot, Mathf.Max(0.01f, collisionRadius), dir, _hitBuffer, wanted,
                collisionMask, QueryTriggerInteraction.Ignore);

            float best = wanted;
            for (int i = 0; i < count; i++)
            {
                Collider hit = _hitBuffer[i].collider;
                if (hit == null) continue;
                if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;  // never hit the slime itself
                if (_hitBuffer[i].distance < best) best = _hitBuffer[i].distance;
            }
            return Mathf.Max(0.2f, best);
        }

        // =====================================================================
        //  Cursor
        // =====================================================================
        void HandleCursorToggle(InputState input)
        {
            if (!lockCursor) return;

            if (_cursorLocked && WantReleaseCursor())
                SetCursor(false);
            else if (!_cursorLocked && input.click)
                SetCursor(true);
        }

        void SetCursor(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        // =====================================================================
        //  Input (mirrors the Unity Input System setup used by the project)
        // =====================================================================
        InputState Poll()
        {
            var s = new InputState();

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) s.move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) s.move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) s.move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) s.move.x -= 1f;

                s.sprint |= kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                // The delta is already in pixels for this frame, so it is frame-rate independent.
                s.look += mouse.delta.ReadValue();
                s.click |= mouse.leftButton.wasPressedThisFrame;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                s.move += pad.leftStick.ReadValue();
                s.look += pad.rightStick.ReadValue() * (180f * Time.deltaTime);
                s.sprint |= pad.leftStickButton.isPressed;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            s.move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            s.look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 8f;
            s.sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            s.click = Input.GetMouseButtonDown(0);
#endif

            s.move = Vector2.ClampMagnitude(s.move, 1f);
            return s;
        }

        bool WantReleaseCursor()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }

        // =====================================================================
        //  Tools
        // =====================================================================
        /// <summary>True while the cursor is captured (in the game view, not in a menu).</summary>
        public bool CursorLocked => _cursorLocked;

        /// <summary>
        /// The flat direction the slime is facing: its model's forward (the model is what turns
        /// towards the travel direction), falling back to this object's forward.
        /// </summary>
        public Vector3 FacingDirection
        {
            get
            {
                Transform source = model != null ? model : transform;
                Vector3 forward = source.forward;
                forward.y = 0f;
                return forward.sqrMagnitude > 0.0001f ? forward.normalized : transform.forward;
            }
        }

        /// <summary>
        /// Turns the model towards a direction (an ability aiming the slime, e.g. while a spell
        /// is charging). Combine it with <see cref="aimLock"/> so travelling does not fight it.
        /// </summary>
        public void FaceDirection(Vector3 direction)
        {
            if (model == null) return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            Quaternion want = Quaternion.LookRotation(direction.normalized, Vector3.up) *
                              Quaternion.Euler(0f, modelYawOffset, 0f) * _restRotation;

            model.rotation = Quaternion.RotateTowards(model.rotation, want,
                                                      Mathf.Max(180f, turnSpeed) * Time.deltaTime);
        }

        /// <summary>Kicks the follow camera for a moment (spell impacts, a nova going off).</summary>
        public void ShakeCamera(float amplitude, float duration)
        {
            if (amplitude <= 0f || duration <= 0f) return;

            _shakeAmplitude = Mathf.Max(_shakeAmplitude, amplitude);
            _shakeFor = Mathf.Max(_shakeFor, duration);
            _shakeTime = 0f;
        }

        /// <summary>Teleport helper (used by the world streamer / editor tools).</summary>
        public void Teleport(Vector3 position)
        {
            if (_cc == null) _cc = GetComponent<CharacterController>();
            bool wasEnabled = _cc != null && _cc.enabled;
            if (wasEnabled) _cc.enabled = false;      // the CharacterController fights direct writes
            transform.position = position;
            if (wasEnabled) _cc.enabled = true;
            _verticalVelocity = 0f;
            _pivot = PivotPoint();
        }

        void OnDrawGizmosSelected()
        {
            Vector3 p = transform.position;
            Vector3 pivot = p + Vector3.up * _pivotHeight;

            Gizmos.color = new Color(0.4f, 1f, 0.7f, 0.8f);
            Gizmos.DrawWireSphere(pivot, 0.12f);
            Gizmos.DrawLine(p, pivot);

            if (cam != null)
            {
                Gizmos.color = new Color(0.6f, 0.8f, 1f, 0.6f);
                Gizmos.DrawLine(pivot, cam.transform.position);
            }
        }
    }
}
