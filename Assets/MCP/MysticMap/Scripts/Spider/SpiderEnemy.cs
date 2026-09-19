using System.Collections.Generic;
using UnityEngine;
using MysticMap.World;

namespace MysticMap
{
    /// <summary>
    /// A hostile fantasy spider that hunts the slime.
    ///
    /// It is built from the "Assets/fantasySpider" pack (the animated spider FBX), but nothing in
    /// here depends on that model: drop the component on any creature with a collider and it will
    /// walk, chase, bite and cast.
    ///
    /// Behaviour:
    ///   * it lives around a home spot and wanders there while nothing is in sight,
    ///   * the moment the slime comes within sight it turns hostile and runs at it,
    ///   * close up it bites (a short wind-up, then damage through <see cref="Damage"/>),
    ///   * from a distance it casts a long-range magic slash: a <see cref="MagicProjectile"/> fired
    ///     at the player, exactly the way the slime's own blade spells work, so it flies over the
    ///     ground, plays the Hovl effect and hurts whatever has an <see cref="IDamageable"/>
    ///     (the player, see <see cref="PlayerVitals"/>),
    ///   * it animates through a legacy Animation component (the pack's clips are legacy clips, so
    ///     it plays idle / walk / run / attack1 / attack2 / death2 by name) and simply keeps
    ///     moving if there is no animation at all.
    ///
    /// Movement uses a CharacterController and follows the streamed terrain through
    /// <see cref="ChunkManager.SampleHeight"/>, so it works anywhere in the procedural world.
    ///
    /// SIZE AND HIT BOX ARE NOT SCRIPT SETTINGS. The spider's mesh is a plain child object and the hit
    /// box is the CharacterController on the root, so both are adjusted by hand on the prefab like any
    /// other Unity object:
    ///   MCP &gt; Enemies &gt; 3. Open the spider to adjust the mesh and the hit box,
    ///   (or put one in the scene with MCP &gt; Enemies &gt; 4. Place a spider here to adjust it)
    /// and then move / scale the "Spider Model" child and edit Radius / Height / Centre on the
    /// CharacterController. Select the object to see the box drawn in the Scene view: green capsule =
    /// the hit box, yellow dot = the feet, red line = the box is not sitting on the feet.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MysticMap/Spider Enemy (bite + ranged magic)")]
    public class SpiderEnemy : MonoBehaviour
    {
        /// <summary>What the spider is doing right now.</summary>
        public enum SpiderState
        {
            /// <summary>Standing around, slowly looking about.</summary>
            Idle,
            /// <summary>Walking to a random spot near home.</summary>
            Wander,
            /// <summary>Running at the player.</summary>
            Chase,
            /// <summary>Standing still during a bite wind-up.</summary>
            Bite,
            /// <summary>Standing still during a spell wind-up.</summary>
            Cast,
            /// <summary>Dead (the Health component removes the object).</summary>
            Dead
        }

        [Header("Who it hunts")]
        [Tooltip("The victim (auto-filled with the slime when empty).")]
        public Transform target;
        [Tooltip("Look for the SlimePlayer automatically.")]
        public bool autoFindPlayer = true;
        [Tooltip("How far (m) the spider notices the player from.")]
        public float sightRange = 34f;
        [Tooltip("Once angry, how far (m) the player must get away before the spider calms down.")]
        public float loseRange = 62f;
        [Tooltip("What the bites and spells may hit.")]
        public LayerMask hitMask = ~0;

        [Header("Movement")]
        [Tooltip("Speed (m/s) while wandering (nothing to chase).")]
        public float walkSpeed = 1.9f;
        [Tooltip("Speed (m/s) while chasing the player.")]
        public float runSpeed = 4.6f;
        [Tooltip("How fast (degrees/s) the spider turns to face where it goes.")]
        public float turnSpeed = 280f;
        [Tooltip("Downward acceleration (m/s^2).")]
        public float gravity = 22f;
        [Tooltip("Do not come closer than this to the victim while chasing.")]
        public float stopDistance = 2.2f;

        [Header("Wander (idle life)")]
        [Tooltip("Radius (m) around its home spot the spider walks in when nothing is in sight.")]
        public float wanderRadius = 12f;
        [Tooltip("Seconds it rests at each wander point before picking the next one.")]
        public float wanderPause = 2.6f;
        [Tooltip("Degrees/s the spider slowly turns while resting (looking about).")]
        public float idleTurnSpeed = 25f;

        [Header("Bite (close range)")]
        [Tooltip("How close (m) the player has to be for a bite.")]
        public float biteRange = 3.2f;
        [Tooltip("Damage of one bite.")]
        public float biteDamage = 9f;
        [Tooltip("Seconds of wind-up before the bite lands (matches the attack animation).")]
        public float biteWindup = 0.4f;
        [Tooltip("Seconds between two bites.")]
        public float biteCooldown = 1.9f;
        [Tooltip("Knockback of the bite (m, 0 = none).")]
        public float biteKnockback = 2.5f;
        [Tooltip("Effect played on the victim of a bite (any impact effect).")]
        public GameObject biteImpactEffect;

        [Header("Ranged magic (the flying slash)")]
        [Tooltip("Let the spider cast its long-range spell.")]
        public bool useMagic = true;
        [Tooltip("The spell: its castEffect is the projectile prefab, the rest is damage / speed / " +
                 "radius / impact effect. Filled from the Hovl pack by the prefab builder.")]
        public MagicSpellLevel spell = new MagicSpellLevel();
        [Tooltip("Closest (m) the player can be while the spider prefers magic.")]
        public float castMinRange = 7f;
        [Tooltip("Farthest (m) the spider may cast at.")]
        public float castMaxRange = 33f;
        [Tooltip("Seconds of wind-up before the spell leaves the spider.")]
        public float castWindup = 0.65f;
        [Tooltip("Seconds between two casts.")]
        public float castCooldown = 5.5f;
        [Tooltip("Height (m) above the spider's feet the spell comes out of.")]
        public float muzzleHeight = 0.85f;
        [Tooltip("Height (m) above the player's feet the spell is aimed at.")]
        public float aimHeight = 0.7f;
        [Tooltip("Only cast when the player can actually be seen (no magic through hills).")]
        public bool requireLineOfSight = true;
        [Tooltip("Effect played around the spider while the spell charges (optional).")]
        public GameObject castChargeEffect;

        [Header("Animation (the pack's clips)")]
        [Tooltip("The Animation component on the spider model (auto-filled from the children). " +
                 "The fantasySpider pack is a LEGACY animation set, so its clips are played through " +
                 "this component - an Animator cannot use them.")]
        public Animation spiderAnimation;
        [Tooltip("Clip played while standing still.")]
        public string idleClip = "idle";
        [Tooltip("Clip played while wandering.")]
        public string walkClip = "walk";
        [Tooltip("Clip played while chasing the player.")]
        public string runClip = "run";
        [Tooltip("Clip played for a bite.")]
        public string biteClip = "attack1";
        [Tooltip("Clip played while casting the spell.")]
        public string spellClip = "attack2";
        [Tooltip("Clip played once when the spider dies.")]
        public string deathClip = "death2";
        [Tooltip("Seconds a change of clip takes to blend.")]
        [Range(0.02f, 0.6f)] public float clipFade = 0.15f;

        [Header("Death")]
        [Tooltip("Safety net: the spider is removed this long after dying even if Health does not do it.")]
        public float deathRemoveDelay = 4f;

        /// <summary>
        /// How far (m) a spider tries to stay from the other spiders, and how hard it steers away, so
        /// two of them cannot end up standing on top of each other. (No Inspector knobs on purpose:
        /// the mesh and the hit box are meant to be edited on the prefab by hand, not from sliders.)
        /// </summary>
        const float PersonalSpace = 2.5f;
        const float SeparationPush = 2.5f;

        /// <summary>Which prefab builder version produced this spider (used only by the editor tool).</summary>
        [HideInInspector] public int builtBy;

        // ---- state ----------------------------------------------------------
        SpiderState _state = SpiderState.Idle;
        Health _health;
        CharacterController _controller;
        ChunkManager _chunks;
        Collider _targetCollider;

        Vector3 _home;
        Vector3 _wanderPoint;

        float _wanderTimer;
        float _biteTimer;
        float _castTimer;
        float _windup;
        float _groundTimer;
        SpiderState _pending;
        bool _windingUp;
        bool _aggro;
        float _verticalVelocity;

        // ---- animation ------------------------------------------------------
        string _playingClip;

        /// <summary>Every spider alive, so they can keep out of each other's way.</summary>
        static readonly List<SpiderEnemy> All = new List<SpiderEnemy>(16);

        /// <summary>What the spider is doing (handy for debugging / tests).</summary>
        public SpiderState State => _state;
        /// <summary>True while the spider is hunting the player.</summary>
        public bool IsAggro => _aggro;
        /// <summary>The hit points component (null when the prefab has none).</summary>
        public Health Health => _health;

        // =====================================================================
        //  Lifecycle
        // =====================================================================
        void Awake()
        {
            _health = GetComponent<Health>();
            _controller = GetComponent<CharacterController>();

            if (spiderAnimation == null) spiderAnimation = GetComponentInChildren<Animation>();
            if (spiderAnimation != null) spiderAnimation.playAutomatically = false;

            _home = transform.position;
            _wanderPoint = _home;
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);

            if (_health == null) return;
            _health.Died += OnDied;
            _health.Revived += OnRevived;
        }

        void OnDisable()
        {
            All.Remove(this);

            if (_health == null) return;
            _health.Died -= OnDied;
            _health.Revived -= OnRevived;
        }

        void Start()
        {
            ResolveTarget();
            SnapToGround();
            PickWanderPoint();
        }

        // =====================================================================
        //  Animation (legacy clips - that is what the spider pack ships)
        // =====================================================================
        /// <summary>Keeps a looping clip (idle / walk / run) on screen, blending into it.</summary>
        void PlayLoop(string clip)
        {
            if (spiderAnimation == null || string.IsNullOrEmpty(clip)) return;
            if (spiderAnimation.GetClip(clip) == null) return;                 // the pack renamed it
            if (_playingClip == clip && spiderAnimation.IsPlaying(clip)) return;

            _playingClip = clip;
            spiderAnimation.CrossFade(clip, Mathf.Max(0.02f, clipFade));
        }

        /// <summary>Plays a one-shot clip (a bite, a spell, the death) from its start.</summary>
        void PlayOnce(string clip, bool clamp)
        {
            if (spiderAnimation == null || string.IsNullOrEmpty(clip)) return;

            AnimationState state = spiderAnimation.GetClip(clip) != null ? spiderAnimation[clip] : null;
            if (state == null) return;

            state.wrapMode = clamp ? WrapMode.ClampForever : WrapMode.Once;
            state.time = 0f;
            state.speed = 1f;

            spiderAnimation.Play(clip, PlayMode.StopAll);
            _playingClip = clip;
        }

        /// <summary>Picks the clip that matches what the spider is doing right now.</summary>
        void AnimateLocomotion()
        {
            switch (_state)
            {
                case SpiderState.Chase: PlayLoop(runClip); break;
                case SpiderState.Wander: PlayLoop(walkClip); break;
                case SpiderState.Bite:
                case SpiderState.Cast:
                case SpiderState.Dead: break;                              // a one-shot has the stage
                default: PlayLoop(idleClip); break;
            }
        }

        // =====================================================================
        //  Main loop
        // =====================================================================
        void Update()
        {
            if (_state == SpiderState.Dead) return;

            float dt = Time.deltaTime;

            ResolveTarget();
            TickTimers(dt);

            if (_windingUp)
            {
                _windup -= dt;

                if (_state == SpiderState.Cast) FaceTarget(dt);
                Move(Vector3.zero, 0f, dt);
                AnimateLocomotion();

                if (_windup <= 0f) LandAttack();
                return;
            }

            float distance = TargetDistance();

            if (distance <= sightRange) _aggro = true;
            if (_aggro && (target == null || distance > loseRange)) _aggro = false;

            if (_aggro && target != null) Hunt(distance, dt);
            else Wander(dt);

            AnimateLocomotion();
        }

        void TickTimers(float dt)
        {
            if (_biteTimer > 0f) _biteTimer -= dt;
            if (_castTimer > 0f) _castTimer -= dt;
            if (_wanderTimer > 0f) _wanderTimer -= dt;
            if (_groundTimer > 0f) _groundTimer -= dt;
        }

        void ResolveTarget()
        {
            if (target != null || !autoFindPlayer) return;

            var players = FindObjectsByType<SlimePlayer>();
            if (players != null && players.Length > 0) target = players[0].transform;
        }

        // =====================================================================
        //  Hunting
        // =====================================================================
        void Hunt(float distance, float dt)
        {
            if (CanCast(distance)) { BeginCast(); return; }

            if (distance <= biteRange)
            {
                if (_biteTimer <= 0f) { BeginBite(); return; }

                Move(Vector3.zero, 0f, dt);      // recovering from the last bite
                FaceTarget(dt);
                return;
            }

            _state = SpiderState.Chase;
            FaceTarget(dt);

            Vector3 toTarget = Flat(target.position - transform.position);
            float travel = Mathf.Max(0f, distance - stopDistance);

            if (toTarget.sqrMagnitude > 0.0001f && travel > 0.05f)
            {
                // Steer around the other spiders instead of walking over them.
                Vector3 steer = toTarget.normalized + Separation() * SeparationPush;
                Move(steer, Mathf.Min(runSpeed, travel * 2f), dt);
            }
            else
            {
                Move(Vector3.zero, 0f, dt);
            }
        }

        /// <summary>How strongly the other spiders push this one away (0 = nobody nearby).</summary>
        Vector3 Separation()
        {
            Vector3 push = Vector3.zero;
            float space = Mathf.Max(0.5f, PersonalSpace);

            for (int i = 0; i < All.Count; i++)
            {
                SpiderEnemy other = All[i];
                if (other == null || other == this) continue;

                Vector3 away = Flat(transform.position - other.transform.position);
                float distance = away.magnitude;
                if (distance > space) continue;

                if (distance < 0.01f)
                    away = new Vector3(Random.value - 0.5f, 0f, Random.value - 0.5f);

                push += away.normalized * (1f - distance / space);
            }

            return push;
        }

        bool CanCast(float distance)
        {
            if (!useMagic || spell == null || spell.castEffect == null) return false;
            if (_castTimer > 0f) return false;
            if (target == null) return false;
            if (distance < castMinRange || distance > castMaxRange) return false;
            if (requireLineOfSight && !HasLineOfSight()) return false;

            return true;
        }

        void BeginBite()
        {
            _state = SpiderState.Bite;
            _pending = SpiderState.Bite;
            _windingUp = true;
            _windup = Mathf.Max(0.05f, biteWindup);
            _biteTimer = Mathf.Max(biteWindup, biteCooldown);

            PlayOnce(biteClip, false);
        }

        void BeginCast()
        {
            _state = SpiderState.Cast;
            _pending = SpiderState.Cast;
            _windingUp = true;
            _windup = Mathf.Max(0.05f, castWindup);
            _castTimer = Mathf.Max(castWindup, castCooldown);

            PlayOnce(spellClip, false);

            if (castChargeEffect != null)
            {
                GameObject fx = MagicEffects.Spawn(castChargeEffect, Muzzle(), transform.rotation, transform,
                                                   Mathf.Max(0.05f, spell.castScale));
                MagicEffects.DestroyLater(fx, _windup + 0.25f);
            }
        }

        /// <summary>Runs the bite / spell the wind-up was for.</summary>
        void LandAttack()
        {
            bool bite = _pending == SpiderState.Bite;
            _windingUp = false;

            if (bite) DoBite();
            else LaunchSpell();

            _state = SpiderState.Idle;
        }

        void DoBite()
        {
            Collider victim = TargetCollider();
            if (victim == null) return;

            Vector3 point = victim.bounds.center;
            Vector3 direction = point - (transform.position + Vector3.up * muzzleHeight);
            if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;

            var info = new DamageInfo(biteDamage * Random.Range(0.9f, 1.1f), point, direction,
                                      biteKnockback, gameObject);

            Damage.Apply(victim, info, transform, biteImpactEffect);
            MagicEnvironment.Impact(point, 2.6f, 0.35f);
        }

        /// <summary>Fires the ranged slash at the player through the shared projectile code.</summary>
        void LaunchSpell()
        {
            if (!useMagic || spell == null || spell.castEffect == null) return;
            if (target == null) return;

            Vector3 origin = Muzzle();
            Vector3 aim = target.position + Vector3.up * aimHeight;
            Vector3 direction = Flat(aim - origin);
            if (direction.sqrMagnitude < 0.0001f) direction = Flat(transform.forward);

            var spec = new MagicCastSpec
            {
                damage = spell.damage,
                power = 0.45f,
                environmentScale = Mathf.Max(0f, spell.environmentScale),
                direction = direction.normalized,
                debrisPerHit = 0
            };

            MagicProjectile.Launch(spell.castEffect, origin, direction, spell, spec, gameObject, hitMask);
        }

        /// <summary>Where its magic comes out: in front of the body, at chest height.</summary>
        Vector3 Muzzle() => transform.position + Vector3.up * muzzleHeight + Flat(transform.forward) * 0.7f;

        /// <summary>True when nothing solid stands between the spider's muzzle and the player.</summary>
        bool HasLineOfSight()
        {
            if (target == null) return false;

            Vector3 from = Muzzle() + Vector3.up * 0.15f;
            Vector3 to = target.position + Vector3.up * aimHeight;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.05f) return true;

            // Only a *blocked* ray matters: whatever we hit, if it belongs to the player (or to
            // another creature) the shot is clear. Anything else is a hill or a wall in the way.
            if (Physics.Raycast(from, delta / distance, out RaycastHit hit, distance, hitMask,
                                QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == target || hit.transform.IsChildOf(target)) return true;
                return Damage.Find(hit.collider) != null;
            }

            return true;
        }

        // =====================================================================
        //  Wandering (nothing in sight)
        // =====================================================================
        void Wander(float dt)
        {
            Vector3 toPoint = Flat(_wanderPoint - transform.position);

            if (_wanderTimer > 0f && toPoint.sqrMagnitude <= 0.25f)
            {
                _state = SpiderState.Idle;
                transform.Rotate(0f, idleTurnSpeed * dt, 0f, Space.World);
                Move(Vector3.zero, 0f, dt);
                return;
            }

            if (toPoint.sqrMagnitude <= 0.25f) { PickWanderPoint(); Move(Vector3.zero, 0f, dt); return; }

            _state = SpiderState.Wander;
            Move(toPoint.normalized, walkSpeed, dt);
        }

        void PickWanderPoint()
        {
            _wanderTimer = Mathf.Max(0.5f, wanderPause);

            Vector2 offset = Random.insideUnitCircle * Mathf.Max(1f, wanderRadius);
            var spot = new Vector3(_home.x + offset.x, 0f, _home.z + offset.y);
            spot.y = GroundHeight(spot.x, spot.z, transform.position.y);
            _wanderPoint = spot;
        }

        // =====================================================================
        //  Movement / grounding
        // =====================================================================
        /// <summary>Walks a flat direction, turns to face it and applies gravity.</summary>
        void Move(Vector3 direction, float speed, float dt)
        {
            direction = Flat(direction);

            if (direction.sqrMagnitude > 0.0001f)
            {
                direction.Normalize();
                Face(direction, dt * (_state == SpiderState.Chase ? 1.6f : 1f));
            }

            if (_controller != null && _controller.enabled)
            {
                if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
                _verticalVelocity -= gravity * dt;

                Vector3 motion = direction * speed;
                motion.y = _verticalVelocity;
                _controller.Move(motion * dt);
                return;
            }

            // No controller (a plain creature object): move the transform and follow the ground.
            if (speed > 0.01f) transform.position += direction * speed * dt;

            if (_groundTimer <= 0f)
            {
                _groundTimer = 0.2f;

                Vector3 p = transform.position;
                p.y = GroundHeight(p.x, p.z, p.y);
                transform.position = p;
            }
        }

        /// <summary>Turns towards a flat direction at the configured turn speed.</summary>
        void Face(Vector3 direction, float dt)
        {
            direction = Flat(direction);
            if (direction.sqrMagnitude < 0.0001f) return;

            Quaternion want = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want,
                                                          Mathf.Max(30f, turnSpeed) * dt);
        }

        void FaceTarget(float dt)
        {
            if (target == null) return;

            Vector3 to = Flat(target.position - transform.position);
            if (to.sqrMagnitude > 0.0001f) Face(to, dt);
        }

        void SnapToGround()
        {
            Vector3 p = transform.position;
            Vector3 want = new Vector3(p.x, GroundHeight(p.x, p.z, p.y), p.z);

            if (_controller != null && _controller.enabled)
            {
                Vector3 delta = want - p;
                if (delta.sqrMagnitude > 0.0001f) _controller.Move(delta);
            }
            else transform.position = want;

            _verticalVelocity = 0f;
        }

        /// <summary>Ground height anywhere: the streamed world when it is there, a ray otherwise.</summary>
        float GroundHeight(float x, float z, float fallbackY)
        {
            if (_chunks == null) _chunks = FindFirstObjectByType<ChunkManager>();

            if (_chunks != null && _chunks.Generator != null) return _chunks.SampleHeight(x, z);

            Vector3 from = new Vector3(x, fallbackY + 8f, z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 90f, hitMask,
                                QueryTriggerInteraction.Ignore))
                return hit.point.y;

            return fallbackY;
        }

        // =====================================================================
        //  Death / revival
        // =====================================================================
        void OnDied()
        {
            _state = SpiderState.Dead;
            _aggro = false;
            _windingUp = false;

            if (_controller != null) _controller.enabled = false;
            PlayOnce(deathClip, true);        // held on its last frame while the body is removed

            // Health normally removes the spider; this is only the safety net (a delayed Destroy on
            // an object that is already gone simply never runs).
            Destroy(gameObject, Mathf.Max(0.5f, deathRemoveDelay));
        }

        void OnRevived()
        {
            _state = SpiderState.Idle;
            _aggro = false;
            _windingUp = false;
            _verticalVelocity = 0f;
            _playingClip = null;

            if (_controller != null) _controller.enabled = true;
        }

        // =====================================================================
        //  Helpers / gizmos
        // =====================================================================
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        float TargetDistance()
        {
            if (target == null) return float.MaxValue;
            return Flat(target.position - transform.position).magnitude;
        }

        Collider TargetCollider()
        {
            if (_targetCollider != null) return _targetCollider;
            if (target == null) return null;

            if (target.TryGetComponent(out Collider own)) _targetCollider = own;
            else _targetCollider = target.GetComponentInChildren<Collider>();

            return _targetCollider;
        }

        /// <summary>Places the spider at its new home and points it at a hunter (used by the spawner).</summary>
        public void Activate(Vector3 home, Transform hunter, bool aggro)
        {
            _home = home;
            _wanderPoint = home;
            _wanderTimer = 0f;
            _biteTimer = 0f;
            _castTimer = 0f;
            _windingUp = false;
            _state = SpiderState.Idle;
            _playingClip = null;

            if (spiderAnimation == null) spiderAnimation = GetComponentInChildren<Animation>();
            if (spiderAnimation != null) spiderAnimation.playAutomatically = false;

            target = hunter;
            _aggro = aggro;
        }

        void OnDrawGizmosSelected()
        {
            Vector3 p = transform.position;

            DrawColliderGizmo(p);

            Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.85f);
            Gizmos.DrawWireSphere(p, sightRange);

            Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.5f);
            Gizmos.DrawWireSphere(p, biteRange);

            if (useMagic)
            {
                Gizmos.color = new Color(0.55f, 0.4f, 1f, 0.6f);
                Gizmos.DrawWireSphere(p, castMinRange);
                Gizmos.color = new Color(0.35f, 0.7f, 1f, 0.35f);
                Gizmos.DrawWireSphere(p, castMaxRange);
            }

            Vector3 home = Application.isPlaying ? _home : transform.position;
            Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.35f);
            Gizmos.DrawWireSphere(home, wanderRadius);
        }

        /// <summary>
        /// Draws the hit box ("the box") and the feet, so it is obvious whether the box is sitting on
        /// the ground or lifting the spider up: the yellow dot is the feet, the green capsule is the
        /// collider, and the line between them turns RED when the box bottom is not on the feet.
        /// </summary>
        void DrawColliderGizmo(Vector3 feet)
        {
            var cc = _controller != null ? _controller : GetComponent<CharacterController>();
            if (cc == null || !cc.enabled) return;

            float radius = cc.radius;
            float height = Mathf.Max(cc.height, radius * 2f);
            Vector3 centre = feet + Vector3.up * cc.center.y;
            Vector3 top = centre + Vector3.up * (height * 0.5f - radius);
            Vector3 bottom = centre - Vector3.up * (height * 0.5f - radius);

            // Capsule.
            Gizmos.color = new Color(0.3f, 1f, 0.45f, 0.9f);
            Gizmos.DrawWireSphere(top, radius);
            Gizmos.DrawWireSphere(bottom, radius);

            Vector3 side = Vector3.right * radius;
            Vector3 side2 = Vector3.forward * radius;
            Gizmos.DrawLine(top + side, bottom + side);
            Gizmos.DrawLine(top - side, bottom - side);
            Gizmos.DrawLine(top + side2, bottom + side2);
            Gizmos.DrawLine(top - side2, bottom - side2);

            // The feet (the object's own origin = where the mesh was fitted to stand).
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 1f);
            Gizmos.DrawWireSphere(feet, 0.08f);

            // How far the box bottom is from the feet: red = the box lifts (or buries) the spider.
            float gap = cc.center.y - height * 0.5f;
            Gizmos.color = Mathf.Abs(gap) < 0.02f ? new Color(0.3f, 1f, 0.45f, 1f)
                                                  : new Color(1f, 0.25f, 0.2f, 1f);
            Gizmos.DrawLine(feet, feet + Vector3.up * gap);

            // Ground circle under the box so it reads as "this is what stands on the terrain".
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(feet + Vector3.up * gap, radius);
        }
    }
}
