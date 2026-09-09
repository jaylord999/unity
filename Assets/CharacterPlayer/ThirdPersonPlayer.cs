using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Third-person player controller for the Built-in Haon test character.
///
/// Responsibilities (see the separate PlayerCameraController for all camera work):
///   • Movement, gravity and jumping via CharacterController
///   • Camera-relative locomotion (asks PlayerCameraController for its flat axes;
///     it never reads the Camera transform or writes the camera itself)
///   • Smooth, frame-rate independent character rotation toward the travel direction
///     (the character only turns while it is moving — rotating the camera alone never
///     rotates the character)
///   • One-shot magic / death actions that *request* camera states (combat framing,
///     cinematic ability, shake) through PlayerCameraController so only the camera
///     controller ever owns the final camera transform.
///
/// The camera controller is created on the follow camera at runtime, so no scene
/// wiring is required. Controls: WASD move, mouse/right-stick/touch look (handled by
/// the camera), Left Shift run, Space jump, F cast spell (cinematic shot),
/// Left Mouse magic attack, K death (auto-revives).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ThirdPersonPlayer : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 3.2f;
    public float runSpeed = 7.2f;
    public float jumpSpeed = 6.5f;
    public float gravity = 22f;
    public float stickToGroundForce = 4f;

    [Header("Character Rotation")]
    [Tooltip("Degrees per second the character turns toward its movement direction.")]
    public float characterRotationSpeed = 540f;

    [Header("Camera (auto-wired)")]
    [Tooltip("Follow camera. Empty = Camera.main. A PlayerCameraController is created on it if missing.")]
    public Camera followCam;

    [Header("Magic & Action Movements (VFX)")]
    [Tooltip("Ground magic circle shown while casting (F).")]
    public GameObject castCirclePrefab;
    [Tooltip("Glowing aura that wraps the character while casting.")]
    public GameObject castAuraPrefab;
    [Tooltip("Small circle shown while doing a magic attack (Left Mouse).")]
    public GameObject attackCirclePrefab;
    [Tooltip("Projectile/burst fired in front during the magic attack.")]
    public GameObject attackBurstPrefab;
    [Tooltip("Ground effect under the player when dying (K).")]
    public GameObject deathGroundPrefab;
    [Tooltip("Scale applied to circles/ground effects.")]
    public float groundCircleScale = 2.2f;
    [Tooltip("Scale applied to effects that hug the character (auras).")]
    public float characterFxScale = 1.5f;
    [Tooltip("Fallback lock duration if a clip length can't be read.")]
    public float actionDefaultDuration = 1.6f;
    [Tooltip("Extra seconds the character stays down after the Death clip before reviving.")]
    public float deathReviveDelay = 2.5f;

    // Which non-locomotion action is currently playing (magic/death).
    private enum ActionState { None, CastSpell, MagicAttack, Death }

    private CharacterController _cc;
    private Animator _anim;
    private PlayerCameraController _camCtl;

    private bool _grounded;
    private float _verticalSpeed;
    private bool _wasJumping;
    private string _currentState = "";

    private ActionState _action = ActionState.None;
    private float _actionStarted;
    private float _actionDuration = -1f;   // real clip length, read at runtime
    private float _actionEndAt;
    private bool _impactSpawned;
    private GameObject _groundFx;
    private GameObject _auraFx;
    private GameObject _burstFx;

    // Auto-load the magic VFX from Resources if they are not assigned in the Inspector.
    private const string MagicFxRoot = "ThirdPersonMagicFX/";
    private const string MagicCircleRes = MagicFxRoot + "MagicCircle";
    private const string MagicAuraRes = MagicFxRoot + "AuraPlexus";
    private const string AttackCircleRes = MagicFxRoot + "AttackCircle";
    private const string AttackBurstRes = MagicFxRoot + "AttackBurst";
    private const string DeathGroundRes = MagicFxRoot + "DeathGround";

    // Runtime reference to the camera controller (exposed so other systems can submit requests).
    public PlayerCameraController CameraController => _camCtl;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _anim = GetComponentInChildren<Animator>();
        if (_anim != null) _anim.applyRootMotion = false; // controller moves the rig

        EnsureMagicFX(); // auto-load magic VFX prefabs (safe if missing)

        // Attach (or find) the single-authority camera controller on the follow camera.
        if (followCam == null) followCam = Camera.main;
        if (followCam != null)
        {
            _camCtl = followCam.GetComponent<PlayerCameraController>();
            if (_camCtl == null) _camCtl = followCam.gameObject.AddComponent<PlayerCameraController>();
            _camCtl.Initialize(transform);
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    /// <summary>Assigns any missing magic VFX from the Resources folder.</summary>
    private void EnsureMagicFX()
    {
        if (castCirclePrefab == null) castCirclePrefab = Resources.Load<GameObject>(MagicCircleRes);
        if (castAuraPrefab == null) castAuraPrefab = Resources.Load<GameObject>(MagicAuraRes);
        if (attackCirclePrefab == null) attackCirclePrefab = Resources.Load<GameObject>(AttackCircleRes);
        if (attackBurstPrefab == null) attackBurstPrefab = Resources.Load<GameObject>(AttackBurstRes);
        if (deathGroundPrefab == null) deathGroundPrefab = Resources.Load<GameObject>(DeathGroundRes);
    }

    void Update()
    {
        GatherInput(out Vector2 move, out bool jump, out bool sprint,
                    out bool cast, out bool attack, out bool death);

        _grounded = _cc.isGrounded;

        // Magic / death actions run on their own and lock locomotion for their duration.
        if (_action != ActionState.None)
        {
            UpdateStandingAction();
            FeedCameraState(false, false, Vector3.zero, 0f);
            return;
        }

        // Begin a magic/death action if the player asked for one (only when grounded).
        if (cast && _grounded) { StartCast(); FeedCameraState(false, false, Vector3.zero, 0f); return; }
        if (attack && _grounded) { StartAttack(); FeedCameraState(false, false, Vector3.zero, 0f); return; }
        if (death && _grounded) { StartDeath(); FeedCameraState(false, false, Vector3.zero, 0f); return; }

        // ---- Camera-relative locomotion reference frame ----
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (_camCtl != null)
        {
            forward = _camCtl.FlatForward;
            right = _camCtl.FlatRight;
        }

        // Normalize diagonal input so W+D is not faster than W.
        Vector2 normMove = move;
        if (normMove.sqrMagnitude > 1f) normMove.Normalize();
        bool moving = normMove.sqrMagnitude > 0.0001f;

        Vector3 wish = (right * normMove.x) + (forward * normMove.y);
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        float speed = sprint ? runSpeed : walkSpeed;
        Vector3 horizontal = wish * speed;

        // ---- Gravity + jump ----
        if (_grounded)
        {
            _verticalSpeed = -stickToGroundForce;
            if (jump)
            {
                _verticalSpeed = jumpSpeed;
                if (!_wasJumping)
                {
                    _wasJumping = true;
                    PlayState("Jump");
                }
            }
        }
        else
        {
            _verticalSpeed -= gravity * Time.deltaTime;
        }

        Vector3 velocity = new Vector3(horizontal.x, _verticalSpeed, horizontal.z);
        _cc.Move(velocity * Time.deltaTime);

        Vector3 hVel = new Vector3(velocity.x, 0f, velocity.z);
        float hSpeed = hVel.magnitude;

        // ---- Character facing: smoothly turn to face the travel direction. ----
        UpdateCharacterFacing(hVel);

        // ---- Locomotion animation state ----
        if (_wasJumping)
        {
            if (_grounded) { _wasJumping = false; PlayState("Idle"); }
        }
        else
        {
            PlayState(ResolveLocomotionState(hSpeed, sprint));
        }

        // ---- Report locomotion to the camera (recenter + look-ahead). ----
        float speedFactor = runSpeed > 0.01f ? Mathf.Clamp01(hSpeed / runSpeed) : 0f;
        FeedCameraState(moving && _grounded, false, hVel, speedFactor);
    }

    /// <summary>Rotate the character smoothly toward its movement direction (never a snap).</summary>
    private void UpdateCharacterFacing(Vector3 hVel)
    {
        // Standing still: never rotate just because the camera turns.
        if (hVel.sqrMagnitude <= 0.01f) return;

        float targetYaw = Mathf.Atan2(hVel.x, hVel.z) * Mathf.Rad2Deg;
        float maxDeg = characterRotationSpeed * Time.deltaTime;
        Quaternion desired = Quaternion.Euler(0f, targetYaw, 0f);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, maxDeg);
    }

    private string ResolveLocomotionState(float hSpeed, bool sprint)
    {
        if (_grounded && hSpeed <= 0.2f) return "Idle";
        return (sprint || hSpeed > runSpeed * 0.6f) ? "Run" : "Walk";
    }

    // ----------------------------------------------------------------- actions

    /// <summary>Advances the currently playing magic/death action and ends it on time.</summary>
    private void UpdateStandingAction()
    {
        if (_anim == null)
        {
            FinishAction();
            return;
        }

        // Keep the character planted but still obey gravity if they fell off a ledge.
        if (_grounded) _verticalSpeed = -stickToGroundForce;
        else _verticalSpeed -= gravity * Time.deltaTime;
        _cc.Move(new Vector3(0f, _verticalSpeed, 0f) * Time.deltaTime);

        // Fires the burst roughly halfway through the attack clip for a bit of rhythm.
        if (_action == ActionState.MagicAttack && !_impactSpawned && !_anim.IsInTransition(0))
        {
            AnimatorStateInfo st = _anim.GetCurrentAnimatorStateInfo(0);
            if (st.normalizedTime >= 0.45f)
            {
                SpawnAttackBurst();
                _impactSpawned = true;
                if (_camCtl != null) _camCtl.PlayShake(0.12f, 0.18f); // light impact shake
            }
        }

        // Read the real clip length once the cross-fade has finished so the lock duration
        // and VFX always match the actual downloaded animation duration.
        if (_actionDuration < 0f)
        {
            if (!_anim.IsInTransition(0))
            {
                AnimatorStateInfo st = _anim.GetCurrentAnimatorStateInfo(0);
                if (st.length > 0.01f)
                {
                    _actionDuration = st.length;
                    _actionEndAt = _actionStarted + Mathf.Max(0.05f, _actionDuration)
                                    + (_action == ActionState.Death ? deathReviveDelay : 0f);
                    if (_camCtl != null) _camCtl.SetCinematicDuration(_actionDuration);
                }
            }
            else if (Time.time - _actionStarted > 1.5f) // fallback if length can't be read
            {
                _actionDuration = actionDefaultDuration;
                _actionEndAt = _actionStarted + actionDefaultDuration
                               + (_action == ActionState.Death ? deathReviveDelay : 0f);
                if (_camCtl != null) _camCtl.SetCinematicDuration(actionDefaultDuration);
            }
        }
        else if (Time.time >= _actionEndAt)
        {
            FinishAction();
        }
    }

    /// <summary>Starts a one-shot standing action by name (CastSpell/MagicAttack/Death).</summary>
    private void StartAction(string state)
    {
        _action = ActionStateFor(state);
        _actionStarted = Time.time;
        _actionDuration = -1f;
        _actionEndAt = float.MaxValue;
        _impactSpawned = false;
        PlayState(state);
    }

    /// <summary>Cast: cinematic camera shot + ground circle + body aura.</summary>
    private void StartCast()
    {
        StartAction("CastSpell");
        _groundFx = SpawnAtFeet(castCirclePrefab, groundCircleScale);
        _auraFx = SpawnAround(castAuraPrefab, characterFxScale);
        if (_camCtl != null) _camCtl.BeginCinematic(null); // uses the camera's tuned ability
    }

    /// <summary>Magic attack: combat framing + a smaller circle under the player.</summary>
    private void StartAttack()
    {
        StartAction("MagicAttack");
        _groundFx = SpawnAtFeet(attackCirclePrefab, groundCircleScale * 0.8f);
        if (_camCtl != null) _camCtl.SetCombat(true);   // widen view during the attack
    }

    /// <summary>Death: plays the death clip with a ground effect, then revives after a delay.</summary>
    private void StartDeath()
    {
        StartAction("Death");
        _groundFx = SpawnAtFeet(deathGroundPrefab, groundCircleScale);
        if (_camCtl != null) _camCtl.SetCombat(false);  // leave combat framing when down
    }

    private void FinishAction()
    {
        bool wasAttack = _action == ActionState.MagicAttack;
        ClearEffects();
        _action = ActionState.None;
        _actionDuration = -1f;

        // Return the camera to normal gameplay framing (safe no-ops if no request is active).
        if (_camCtl != null)
        {
            _camCtl.CancelCinematic();
            if (wasAttack) _camCtl.SetCombat(false);
        }

        if (_anim != null && _grounded) PlayState("Idle");
    }

    /// <summary>Report locomotion to the camera so it can do look-ahead and auto recentering.</summary>
    private void FeedCameraState(bool moving, bool strafing, Vector3 hVel, float speedFactor)
    {
        if (_camCtl != null) _camCtl.FeedMovementState(moving, strafing, hVel, speedFactor);
    }

    // ----------------------------------------------------------------- VFX

    /// <summary>Spawns the projectile/energy burst in front of the character.</summary>
    private void SpawnAttackBurst()
    {
        if (_burstFx != null || attackBurstPrefab == null) return;
        Vector3 pos = transform.position + transform.forward * 1.6f + Vector3.up * 1.0f;
        _burstFx = Instantiate(attackBurstPrefab, pos,
            Quaternion.LookRotation(transform.forward, Vector3.up));
        _burstFx.transform.localScale = Vector3.one * (characterFxScale * 0.9f);
    }

    /// <summary>Places a flat effect on the ground just under the player's feet.</summary>
    private GameObject SpawnAtFeet(GameObject prefab, float scale)
    {
        if (prefab == null) return null;
        Vector3 ground;
        if (!RaycastGround(out ground))
            ground = transform.position - Vector3.up * (_cc.height * 0.5f + 0.05f);
        ground += Vector3.up * 0.05f;                    // lift to avoid z-fighting
        GameObject go = Instantiate(prefab, ground, Quaternion.identity);
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    /// <summary>Attaches an effect to the character (auras) so it follows the body.</summary>
    private GameObject SpawnAround(GameObject prefab, float scale)
    {
        if (prefab == null) return null;
        GameObject go = Instantiate(prefab,
            transform.position + Vector3.up * 0.9f, transform.rotation, transform);
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    private bool RaycastGround(out Vector3 point)
    {
        if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down,
                            out RaycastHit hit, 300f))
        {
            point = hit.point;
            return true;
        }
        point = transform.position;
        return false;
    }

    private void ClearEffects()
    {
        if (_groundFx != null) { Destroy(_groundFx); _groundFx = null; }
        if (_auraFx != null) { Destroy(_auraFx); _auraFx = null; }
        if (_burstFx != null) { Destroy(_burstFx); _burstFx = null; }
    }

    private ActionState ActionStateFor(string state)
    {
        switch (state)
        {
            case "CastSpell": return ActionState.CastSpell;
            case "MagicAttack": return ActionState.MagicAttack;
            case "Death": return ActionState.Death;
            default: return ActionState.None;
        }
    }

    private void PlayState(string state)
    {
        if (_anim == null || state == _currentState) return;
        if (_anim.HasState(0, Animator.StringToHash(state)))
        {
            _anim.CrossFadeInFixedTime(state, 0.18f);
            _currentState = state;
        }
    }

    // ----------------------------------------------------------------- input

    private void GatherInput(out Vector2 move, out bool jump, out bool sprint,
        out bool cast, out bool attack, out bool death)
    {
        move = Vector2.zero;
        jump = false;
        sprint = false;
        cast = false;
        attack = false;
        death = false;

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

        jump = kb.spaceKey.wasPressedThisFrame;
        sprint = kb.leftShiftKey.isPressed;
        cast = kb.fKey.wasPressedThisFrame;
        attack = ms.leftButton.wasPressedThisFrame;
        death = kb.kKey.wasPressedThisFrame;
#else
        move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        jump = Input.GetKeyDown(KeyCode.Space);
        sprint = Input.GetKey(KeyCode.LeftShift);
        cast = Input.GetKeyDown(KeyCode.F);
        attack = Input.GetMouseButtonDown(0);
        death = Input.GetKeyDown(KeyCode.K);
#endif
    }

    // Exposed so UI/menus can toggle the cursor (also respected by the camera look input).
    public void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // Exposed so a settings menu can read/write look sensitivity through the player component.
    public float LookSensitivity => _camCtl != null ? _camCtl.Sensitivity : 1f;

    public void SetLookSensitivity(float value)
    {
        if (_camCtl != null) _camCtl.SetSensitivity(value);
    }
}
