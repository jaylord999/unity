using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MysticMap
{
    /// <summary>
    /// First-person controller with mouse look, WASD movement, jumping and gravity.
    /// Uses the active Input System (new Input System when enabled, legacy fallback otherwise).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonPlayer : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Normal walk speed in m/s.")]
        public float walkSpeed = 4f;
        [Tooltip("Sprint speed when Shift is held, in m/s.")]
        public float runSpeed = 8.5f;
        [Tooltip("Jump impulse (vertical speed on jump).")]
        public float jumpSpeed = 6.5f;
        [Tooltip("Gravity applied each second when airborne.")]
        public float gravity = 22f;

        [Header("Look")]
        public float lookSensitivity = 2f;
        public bool invertY = false;
        [Tooltip("Camera attached to this rig (used for the view direction).")]
        public Camera cam;

        [Header("Grounding")]
        [Tooltip("Auto step-off when not exactly grounded.")]
        public float stickToGroundForce = 4f;

        private CharacterController _cc;
        private Vector2 _look;
        private Vector3 _velocity;
        private float _yaw;
        private float _pitch;
        private bool _grounded;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (cam == null) cam = GetComponentInChildren<Camera>();
            if (cam == null) cam = Camera.main;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            _yaw = transform.eulerAngles.y;
        }

        void Update()
        {
            GatherInput(out Vector2 move, out Vector2 look, out bool jump, out bool sprint);

            // --- Look ---
            _yaw += look.x * lookSensitivity;
            _pitch += look.y * lookSensitivity * (invertY ? -1f : 1f);
            _pitch = Mathf.Clamp(_pitch, -85f, 85f);

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (cam != null) cam.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            // --- Movement ---
            Vector3 wish = (transform.right * move.x) + (transform.forward * move.y);
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float speed = sprint ? runSpeed : walkSpeed;
            Vector3 horizontal = wish * speed;

            // --- Gravity + jump ---
            _grounded = _cc.isGrounded;
            if (_grounded)
            {
                _velocity.y = -stickToGroundForce;
                if (jump)
                    _velocity.y = jumpSpeed;
            }
            else
            {
                _velocity.y -= gravity * Time.deltaTime;
            }

            _velocity.x = horizontal.x;
            _velocity.z = horizontal.z;

            _cc.Move(_velocity * Time.deltaTime);
        }

        private void GatherInput(out Vector2 move, out Vector2 look, out bool jump, out bool sprint)
        {
            move = Vector2.zero;
            look = Vector2.zero;
            jump = false;
            sprint = false;

#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            Mouse ms = Mouse.current;
            if (kb == null || ms == null) return;

            Vector2 axis = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) axis.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) axis.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) axis.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) axis.x -= 1f;
            move = axis;

            Vector2 md = ms.delta.ReadValue();
            look = new Vector2(md.x * 0.1f, md.y * 0.1f);

            jump = kb.spaceKey.isPressed;
            sprint = kb.leftShiftKey.isPressed;
#else
            move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            look = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            jump = Input.GetKey(KeyCode.Space);
            sprint = Input.GetKey(KeyCode.LeftShift);
#endif
        }

        /// <summary>Unlock the cursor (e.g. when opening menus).</summary>
        public void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
