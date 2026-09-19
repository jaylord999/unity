using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace MysticMap
{
    /// <summary>
    /// Hold-to-charge magic for the slime, using the Hovl Studio effect pack.
    ///
    /// How it plays:
    ///   * hold the skill button (left mouse by default, F on the keyboard, right trigger on a
    ///     pad) and the first magic circle blooms around the slime while it charges,
    ///   * keep holding and every threshold reached adds another circle - each one at its own
    ///     height, tilt, spin and orbit, so the rings nest and revolve around the ones already
    ///     playing (the way a stack of magic circles builds up in Tensura),
    ///   * the damage of a release grows with the time held: inside a level it builds up as an
    ///     overcharge on that level's damage, and reaching the next threshold switches to a
    ///     completely different (stronger) spell with its own circles and its own attack,
    ///   * let go and the circles collapse while the spell fires out of the front of the player:
    ///     a slash that travels and cuts what it meets, a blast on the ground a few metres
    ///     ahead, or a nova around the slime on the top level.
    ///
    /// The five levels shipped by the editor tool are: wind slash, frost slash (with a splash),
    /// lightning arc (ground blast), crystal bloom (wide ground blast) and the void nova.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MysticMap/Slime Magic (charge + levels)")]
    public class SlimeMagic : MonoBehaviour
    {
        /// <summary>Name of the (runtime-created) object the magic circles live under.</summary>
        public const string RigName = "Magic Charge Rig";

        [Header("Input")]
        [Tooltip("Read the skill button at all (turn off to drive the magic from a script or an AI).")]
        public bool allowInput = true;
        [Tooltip("Left mouse button charges the magic.")]
        public bool chargeWithLeftMouse = true;
        [Tooltip("F on the keyboard charges the magic.")]
        public bool chargeWithKey = true;
        [Tooltip("Right trigger on a gamepad charges the magic.")]
        public bool chargeWithGamepad = true;
        [Tooltip("Only charge while the cursor is locked (so clicking a menu never fires a spell).")]
        public bool requireCursorLock = true;

        /// <summary>
        /// Which layout of the five built-in levels a component holds. The setup tool bumps this
        /// whenever the levels get a new look (one circle and one aura at a time, lying flat on the
        /// ground, no doubled impact effects), so an older scene is rebuilt exactly once - keeping
        /// the thresholds and damage it was tuned with.
        /// </summary>
        public const int LayoutVersion = 4;

        /// <summary>The layout where the circles first lay flat on the ground, and where an
        /// oversized global size multiplier is pulled back once.</summary>
        public const int FlatLayout = 3;

        [Header("Charge levels (the more you hold, the higher the level)")]
        [Tooltip("Ordered by 'hold to reach'. Leave empty and use " +
                 "MCP > Player > Slime magic (Hovl circles) to fill them from the effect pack.")]
        public MagicSpellLevel[] levels;
        [Tooltip("Which layout of the built-in levels this component holds (see LayoutVersion). A " +
                 "scene built before the levels were changed is upgraded once, and the thresholds " +
                 "and damage that were tuned by hand are kept.")]
        [HideInInspector] public int levelsLayout;
        [Tooltip("Seconds of extra holding after the last level that still add damage.")]
        public float overchargeWindow = 2.5f;
        [Tooltip("Extra damage per second of overcharge, as a fraction of the level's damage.")]
        public float overchargePerSecond = 0.25f;
        [Tooltip("Most extra damage overcharge can add (1 = at most double).")]
        public float overchargeCap = 1f;
        [Tooltip("Releasing sooner than this is a fizzle, not a spell.")]
        public float minimumHoldToCast = 0.12f;
        [Tooltip("Small puff played when the charge is released too early to cast.")]
        public GameObject fizzleEffect;
        public float fizzleCooldown = 0.25f;

        [Header("Aim")]
        [Tooltip("PlayerFacing fires where the slime looks, CameraForward turns the slime to the " +
                 "camera while charging and fires where you are looking.")]
        public AimMode aim = AimMode.CameraForward;

        [Header("Magic circles")]
        [Tooltip("How quickly the circles glide after the player (0 = glued to it).")]
        [Range(0f, 0.4f)] public float rigFollowSmoothing = 0.06f;
        [Tooltip("Keep the lights the pack bakes into some circles. Off = one light per circle " +
                 "less, which is much cheaper.")]
        public bool spawnEffectLights = false;
        [Tooltip("Seconds the circles take to collapse when the magic is released.")]
        public float releaseCollapse = 0.18f;

        [Header("Magic tuning - circle size (applied live)")]
        [Tooltip("Multiplies every circle's diameter and its orbit radius: the knob for how large " +
                 "the magic looks around the slime.")]
        [Range(0.25f, 4f)] public float circleScale = 1.35f;
        [Tooltip("Multiplies how high above the slime the circles sit.")]
        [Range(0.25f, 4f)] public float circleHeightScale = 1f;
        [Tooltip("How much the circles that are already out swell while the bar fills toward the next " +
                 "threshold (0 = they keep their size, 0.25 = they grow by up to a quarter). This is " +
                 "what makes the magic visibly charge up between levels.")]
        [Range(0f, 1f)] public float chargeSwell = 0.22f;
        [Tooltip("How much faster the circles and their clock riders turn at the top of the bar " +
                 "(0 = constant, 1 = twice as fast).")]
        [Range(0f, 3f)] public float chargeSpinUp = 1.2f;
        [Tooltip("Show only the circles of the threshold that is charged right now: reaching a new " +
                 "level takes the previous magic circle and aura away and blooms the new pair, so " +
                 "charging never fills the screen with circles. Off = every level's circles stay out.")]
        public bool swapCirclesEachLevel = true;
        [Tooltip("Let the circles bounce up and down with the slime. Off (the default) keeps them " +
                 "lying flat on the ground, the way a magic circle drawn on the floor should.")]
        public bool circlesBounceWithSlime;
        [Tooltip("How far above the ground the flat circles lie, in metres.")]
        [Range(0f, 1f)] public float groundLift = 0.03f;
        [Tooltip("What counts as ground under the slime when the circles are laid on it. " +
                 "Nothing = everything is ground.")]
        public LayerMask groundMask = ~0;

        [Header("Magic tuning - thresholds (how long each level takes)")]
        [Tooltip("Multiplies every level's 'hold to reach' time. 1 = the numbers written on the levels.")]
        [Range(0.25f, 4f)] public float thresholdScale = 1f;
        [Tooltip("Seconds added to every threshold except the first (the first circle always " +
                 "blooms the moment the button goes down).")]
        [Range(-1f, 4f)] public float thresholdBias;

        [Header("Magic tuning - the world reacts")]
        [Tooltip("Multiplies every radius the magic applies to the world (grass, trees, rocks, props).")]
        [Range(0f, 3f)] public float environmentScale = 1f;
        [Tooltip("How hard the charging aura bends the grass around the slime (0 = none).")]
        [Range(0f, 2f)] public float auraWindStrength = 0.6f;
        [Tooltip("The aura's radius, as a multiple of the biggest circle that is playing.")]
        [Range(0f, 3f)] public float auraWindScale = 1f;

        [Header("World destruction")]
        [Tooltip("Power needed to shatter rocks and heavy props: 1.5 means only a maximum-charge " +
                 "level 4 / level 5 release can do it.")]
        [Range(0.5f, 3f)] public float rockBreakPower = 1.5f;
        [Tooltip("Prefabs the debris is built from (small stones / rocks). Empty = pebbles are " +
                 "built on the fly. The setup tool fills this from the map's own palette.")]
        public GameObject[] debrisPrefabs;
        [Tooltip("Pieces of debris thrown per hit.")]
        [Range(0, 24)] public int debrisPerHit = 6;
        [Tooltip("Pieces of debris thrown by a maximum-charge explosion.")]
        [Range(0, 40)] public int debrisPerExplosion = 16;

        [Header("Cast")]
        [Tooltip("How far in front of the slime a travelling spell starts.")]
        public float castForwardOffset = 0.9f;
        [Tooltip("Height of the cast origin above the slime's feet.")]
        public float castHeight = 0.55f;
        [Tooltip("What the magic can hit.")]
        public LayerMask hitMask = ~0;

        [Header("Player")]
        [Tooltip("The bouncing slime these circles belong to (auto-filled).")]
        public SlimePlayer player;

        [Header("Feedback")]
        [Tooltip("Log every level reached and every cast.")]
        public bool logCasts = true;

        // ---- events the HUD (or a game mode) can listen to -------------------
        public event Action<int> LevelReached;
        public event Action<int, float> Cast;
        public event Action Fizzled;

        // ---- state ----------------------------------------------------------
        bool _charging;
        float _chargeTime;
        int _level;                       // 1..levels.Length, 0 = nothing reached yet
        float _cooldown;
        float _cooldownLength = 1f;
        float _gauge = 1f;                // length of the whole bar in seconds
        float _auraTimer;                 // throttle for the aura that bends the grass
        float _tuningTimer;               // throttle for re-applying the tuning while charging
        Transform _rig;
        Vector3 _rigVelocity;
        readonly RaycastHit[] _groundHits = new RaycastHit[8];
        readonly List<MagicLayerVisual> _layers = new List<MagicLayerVisual>(8);
        readonly List<PendingBlast> _pending = new List<PendingBlast>(4);
        readonly List<Vector3> _points = new List<Vector3>(16);
        readonly List<Vector3> _shown = new List<Vector3>(24);      // where an impact effect was played
        static readonly List<Vector3> _explosionShown = new List<Vector3>(24);

        int _lastCastLevel;
        float _lastCastDamage;
        float _lastCastTime = -99f;

        struct PendingBlast
        {
            public float delay;          // seconds still to wait before the damage lands
            public Vector3 center;
            public Vector3 direction;
            public MagicSpellLevel spell;
            public float damage;
            public MagicCastSpec spec;
        }

        // =====================================================================
        //  What the magic bar reads
        // =====================================================================
        /// <summary>True while the button is held and magic is building up.</summary>
        public bool IsCharging => _charging;
        /// <summary>How many levels this component was set up with.</summary>
        public int LevelCount => levels == null ? 0 : levels.Length;
        /// <summary>Level currently reached (1..<see cref="LevelCount"/>, 0 = none yet).</summary>
        public int ReachedLevel => _level;
        /// <summary>Seconds the button has been held.</summary>
        public float ChargeTime => _chargeTime;
        /// <summary>Length of the whole magic bar, in seconds (last threshold + overcharge).</summary>
        public float GaugeLength => Mathf.Max(0.05f, _gauge);
        /// <summary>Charge position on the bar, 0..1.</summary>
        public float ChargeNormalized => Mathf.Clamp01(_chargeTime / GaugeLength);
        /// <summary>Seconds of cooldown left.</summary>
        public float CooldownLeft => _cooldown;
        /// <summary>Cooldown left as 0..1 of the last spell's cooldown (0 = ready).</summary>
        public float CooldownNormalized => _cooldownLength > 0.01f ? Mathf.Clamp01(_cooldown / _cooldownLength) : 0f;
        /// <summary>The level the last release fired (0 = nothing fired yet).</summary>
        public int LastCastLevel => _lastCastLevel;
        /// <summary>The damage the last release dealt.</summary>
        public float LastCastDamage => _lastCastDamage;
        /// <summary>Seconds since the last release (used to flash the result on the bar).</summary>
        public float LastCastAge => Time.time - _lastCastTime;

        /// <summary>The level being charged (falls back to the first one when idle).</summary>
        public MagicSpellLevel CurrentLevel => LevelAt(_level > 0 ? _level : 1);

        public MagicSpellLevel LevelAt(int level)
        {
            if (levels == null || levels.Length == 0) return null;
            int index = Mathf.Clamp(level - 1, 0, levels.Length - 1);
            return levels[index];
        }

        /// <summary>Name of the level being charged ("-" while idle).</summary>
        public string CurrentName
        {
            get { MagicSpellLevel lv = CurrentLevel; return lv != null ? lv.name : "-"; }
        }

        /// <summary>School of the level being charged.</summary>
        public string CurrentSchool
        {
            get { MagicSpellLevel lv = CurrentLevel; return lv != null ? lv.school : string.Empty; }
        }

        /// <summary>Bar colour of the level being charged.</summary>
        public Color CurrentTint
        {
            get { MagicSpellLevel lv = CurrentLevel; return lv != null ? lv.tint : Color.white; }
        }

        /// <summary>Damage a release right now would deal (overcharge included).</summary>
        public float DamageNow => DamageFor(CurrentLevel, _chargeTime);

        /// <summary>Where a level's threshold sits on the bar (0..1), for the tick marks.</summary>
        public float ThresholdRatio(int level)
        {
            MagicSpellLevel lv = LevelAt(level);
            if (lv == null) return 1f;
            return Mathf.Clamp01(EffectiveHold(lv) / GaugeLength);
        }

        /// <summary>How far the current level is into its overcharge window (0..1).</summary>
        public float OverchargeFraction
        {
            get
            {
                if (!_charging || _level < 1 || overchargeWindow <= 0.01f) return 0f;
                float beyond = Mathf.Max(0f, _chargeTime - EffectiveHold(CurrentLevel));
                return Mathf.Clamp01(beyond / overchargeWindow);
            }
        }

        // =====================================================================
        //  Tuning: the settings the tuning window / the inspector drive
        // =====================================================================
        /// <summary>
        /// The seconds of holding the button this level really needs, with the global threshold
        /// tuning (<see cref="thresholdScale"/>, <see cref="thresholdBias"/>) applied. The first
        /// level always stays instant, so the first circle blooms as soon as you press.
        /// </summary>
        public float EffectiveHold(MagicSpellLevel level)
        {
            if (level == null) return 0f;
            if (level.holdToReach <= 0.01f) return 0f;

            return Mathf.Max(0.05f,
                level.holdToReach * Mathf.Max(0.05f, thresholdScale) + thresholdBias);
        }

        /// <summary>Seconds of holding the button this level needs (0 when there is no such level).</summary>
        public float EffectiveHold(int level) => EffectiveHold(LevelAt(level));

        /// <summary>Damage a release right now, of a given level, with the hold time given.</summary>
        public float DamageFor(MagicSpellLevel level, float heldSeconds)
        {
            if (level == null) return 0f;

            float beyond = Mathf.Max(0f, heldSeconds - EffectiveHold(level));
            float bonus = Mathf.Clamp(beyond * Mathf.Max(0f, overchargePerSecond), 0f, Mathf.Max(0f, overchargeCap));
            return Mathf.Max(0f, level.damage) * (1f + bonus);
        }

        /// <summary>
        /// How much power a release has built up: ~0.2 for a tap of the first level and 2 for a
        /// fully overcharged top level. This is what decides whether rocks are shattered.
        /// </summary>
        public float PowerOf(int level, float heldSeconds)
        {
            MagicSpellLevel lv = LevelAt(level);
            if (lv == null) return 0f;

            float tier = Mathf.Clamp01((float)level / Mathf.Max(1, LevelCount)) * 1.2f;
            float beyond = Mathf.Max(0f, heldSeconds - EffectiveHold(lv));
            float over = Mathf.Clamp01(beyond / Mathf.Max(0.01f, overchargeWindow));

            return tier + over * 0.8f;
        }

        /// <summary>True when the release is fully overcharged (the window is full).</summary>
        public bool IsMaxChargeNow(int level, float heldSeconds)
        {
            MagicSpellLevel lv = LevelAt(level);
            if (lv == null) return false;

            float beyond = Mathf.Max(0f, heldSeconds - EffectiveHold(lv));
            return beyond >= Mathf.Max(0.05f, overchargeWindow) - 0.001f;
        }

        /// <summary>
        /// Everything the spell needs to know about the world around it (power, maximum charge,
        /// debris). The projectiles and blasts then react to the environment with it.
        /// </summary>
        public MagicCastSpec BuildSpec(MagicSpellLevel level, int levelIndex, float heldSeconds, Vector3 direction)
        {
            return new MagicCastSpec
            {
                damage = DamageFor(level, heldSeconds),
                power = PowerOf(levelIndex, heldSeconds),
                maxCharge = level != null && level.maxChargeBreaksWorld && IsMaxChargeNow(levelIndex, heldSeconds),
                environmentScale = Mathf.Max(0f, environmentScale) *
                                   (level != null ? Mathf.Max(0f, level.environmentScale) : 1f),
                rockBreakPower = Mathf.Max(0.5f, rockBreakPower),
                debris = debrisPrefabs,
                debrisPerHit = Mathf.Max(0, debrisPerHit),
                debrisPerExplosion = Mathf.Max(0, debrisPerExplosion),
                caster = player,
                direction = direction
            };
        }

        /// <summary>
        /// Radius of the aura that bends the grass while charging: the biggest circle that is
        /// playing right now.
        /// </summary>
        float AuraRadius()
        {
            float diameter = 3f;
            MagicSpellLevel lv = CurrentLevel;

            if (lv != null && lv.layers != null)
            {
                float levelScale = Mathf.Max(0.25f, lv.circleScale);

                foreach (MagicLayer layer in lv.layers)
                    if (layer != null && layer.FitsToDiameter)
                        diameter = Mathf.Max(diameter, layer.diameter * levelScale *
                                                         Mathf.Max(0.05f, circleScale));
            }

            return Mathf.Max(1.2f, diameter * 0.5f * Mathf.Max(0.2f, auraWindScale) *
                                    Mathf.Max(0.2f, environmentScale));
        }

        /// <summary>
        /// Re-applies the size / threshold settings to the circles that are already out, so the
        /// tuning window can be dragged while the game plays and the change is seen immediately.
        /// </summary>
        public void ApplyTuning()
        {
            RefreshGauge();

            if (!_charging || _rig == null) return;
            if (Time.time - _tuningTimer < 0.1f) return;      // never rebuild twice in one blink
            _tuningTimer = Time.time;

            RetireLayers(0.02f);

            if (swapCirclesEachLevel)
            {
                // Only the circles of the level that is charged right now are put back.
                if (_level >= 1)
                {
                    MagicSpellLevel only = LevelAt(_level);
                    if (only != null) SpawnLayers(only, _level - 1);
                }
            }
            else
            {
                for (int i = 0; i < _level && i < LevelCount; i++) SpawnLayers(levels[i], i);
            }
        }

        // =====================================================================
        //  Life cycle
        // =====================================================================
        void Awake()
        {
            if (player == null) player = GetComponent<SlimePlayer>();
            if (player == null) player = GetComponentInParent<SlimePlayer>();
            RefreshGauge();
        }

        void OnValidate() => RefreshGauge();       // thresholds edited in the Inspector update the bar

        void OnDisable()
        {
            if (_charging) Cancel();
            if (_rig != null)
            {
                Destroy(_rig.gameObject);
                _rig = null;
            }
            _layers.Clear();
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (_cooldown > 0f) _cooldown = Mathf.Max(0f, _cooldown - dt);
            TickPendingBlasts(dt);

            bool held = allowInput && ReadSkillButton();

            if (_charging)
            {
                if (!allowInput || !held) Release();
                else HoldCharge(dt);
            }
            else if (held && _cooldown <= 0f && LevelCount > 0)
            {
                BeginCharge();
            }

            FollowRig();
        }

        /// <summary>Recalculates how long the bar is: the last threshold plus the overcharge window.</summary>
        public void RefreshGauge()
        {
            float last = 0f;
            if (levels != null)
                foreach (MagicSpellLevel lv in levels)
                    if (lv != null)
                    {
                        float hold = EffectiveHold(lv);
                        if (hold > last) last = hold;
                    }

            _gauge = Mathf.Max(0.25f, last + Mathf.Max(0.1f, overchargeWindow));
        }

        // =====================================================================
        //  Charging
        // =====================================================================
        /// <summary>Starts a charge (also reachable from a script / an AI).</summary>
        public void BeginCharge()
        {
            if (_charging || _cooldown > 0f || LevelCount == 0) return;

            _charging = true;
            _chargeTime = 0f;
            _level = 0;
            _auraTimer = 0f;
            _layers.Clear();

            if (_rig == null)
            {
                var go = new GameObject(RigName);
                go.transform.position = transform.position;
                _rig = go.transform;
            }

            if (player != null)
            {
                // Charging holds the facing, and (with CameraForward) aims the slime where the
                // camera looks so "the front of the player" is where you are pointing.
                player.aimLock = aim == AimMode.CameraForward;
                player.speedScale = 1f;
            }

            SetLevel(1);        // the first circle blooms the moment the button goes down
        }

        /// <summary>Ends the charge and fires (or fizzles). Also reachable from a script.</summary>
        public void ReleaseCharge() => Release();

        /// <summary>Abandons a charge without firing anything.</summary>
        public void CancelCharge() => Cancel();

        void HoldCharge(float dt)
        {
            _chargeTime += dt;

            int reached = LevelForTime(_chargeTime);
            if (reached > _level) SetLevel(reached);

            if (player != null)
            {
                MagicSpellLevel lv = CurrentLevel;
                player.speedScale = lv != null ? Mathf.Clamp(lv.chargeMoveScale, 0.05f, 1f) : 1f;
                if (aim == AimMode.CameraForward) player.FaceDirection(AimDirection());
            }

            TickAuraWind(dt);
            TickChargeFeel();
        }

        /// <summary>
        /// The aura that bends the world while charging: the grass around the slime leans and
        /// swirls away from it, and at the higher levels a few blades are pulled off the ground.
        /// The aura grows with the level, so a level 5 charge visibly tears at the ground.
        /// </summary>
        void TickAuraWind(float dt)
        {
            if (auraWindStrength <= 0.01f || environmentScale <= 0.01f) return;

            _auraTimer -= dt;
            if (_auraTimer > 0f) return;
            _auraTimer = 0.12f;

            Vector3 center = _rig != null ? _rig.position : transform.position;

            float tier = LevelCount > 1 ? (_level - 1) / (float)(LevelCount - 1) : 1f;
            float strength = Mathf.Lerp(14f, 50f, Mathf.Clamp01(tier)) * auraWindStrength;

            MagicEnvironment.Wind(center, AuraRadius(), strength, 0.4f);
        }

        /// <summary>
        /// Feeds "how far the bar has filled toward the next threshold" into the circles that are
        /// already out, so the magic visibly charges up between levels: the circles swell, turn
        /// faster and their clock riders walk the rim quicker the closer the next one is.
        /// </summary>
        void TickChargeFeel()
        {
            float fill = ChargeFill();

            for (int i = 0; i < _layers.Count; i++)
                if (_layers[i] != null) _layers[i].SetCharge(fill, chargeSwell, chargeSpinUp);
        }

        /// <summary>
        /// How far the hold is toward the next threshold (1 = the next circle is due, 1 = the
        /// overcharge window is full once the last level is reached).
        /// </summary>
        public float ChargeFill()
        {
            if (!_charging) return 0f;

            float from = _level > 0 ? EffectiveHold(LevelAt(_level)) : 0f;

            if (_level < LevelCount)
            {
                float to = EffectiveHold(LevelAt(_level + 1));
                return to > from + 0.001f ? Mathf.Clamp01((_chargeTime - from) / (to - from)) : 0f;
            }

            // Top level reached: the overcharge window is the last step of the bar.
            return Mathf.Clamp01((_chargeTime - from) / Mathf.Max(0.1f, overchargeWindow));
        }

        /// <summary>How many thresholds the given hold time has passed.</summary>
        int LevelForTime(float held)
        {
            int reached = 0;
            for (int i = 0; i < LevelCount; i++)
            {
                MagicSpellLevel lv = levels[i];
                if (lv == null) continue;
                if (held + 0.0001f >= EffectiveHold(lv)) reached = i + 1;
                else break;                                    // levels are ordered, so stop here
            }
            return reached;
        }

        /// <summary>
        /// Adds everything a new level brings: its circles and its burst.
        ///
        /// With <see cref="swapCirclesEachLevel"/> on (the default) the circles of the previous
        /// threshold are taken away first, so charging shows one magic circle and one aura, and the
        /// new threshold swaps them for its own pair.
        /// </summary>
        void SetLevel(int level)
        {
            if (level <= _level) return;

            int first = swapCirclesEachLevel ? level - 1 : _level;

            if (swapCirclesEachLevel) RetireLayers(Mathf.Max(0.12f, releaseCollapse));

            for (int i = first; i < level && i < LevelCount; i++)
            {
                MagicSpellLevel lv = levels[i];
                if (lv == null) continue;

                SpawnLayers(lv, i);

                if (lv.levelBurst != null)
                    MagicEffects.DestroyLater(
                        MagicEffects.Spawn(lv.levelBurst, PlayerCenter(), Quaternion.identity), 0f);
            }

            _level = level;

            if (logCasts)
            {
                MagicSpellLevel reached = LevelAt(level);
                if (reached != null)
                    Debug.Log("[MysticMap] Magic level " + level + " - " + reached.name + " (" +
                              reached.school + "), release now for " + DamageNow.ToString("0.#") + " damage.",
                              this);
            }

            LevelReached?.Invoke(level);
        }

        void SpawnLayers(MagicSpellLevel lv, int levelIndex)
        {
            if (_rig == null || lv.layers == null) return;

            // A level can scale its own circles, so the magic changes shape as it grows stronger.
            float sizeScale = Mathf.Max(0.05f, circleScale) * Mathf.Max(0.25f, lv.circleScale);
            float rim = RimDiameter(levelIndex);

            for (int i = 0; i < lv.layers.Length; i++)
            {
                MagicLayer layer = lv.layers[i];
                if (layer == null || layer.effect == null) continue;

                // A circle or an aura takes the place of the one the earlier level brought, so the
                // magic swaps at every threshold instead of piling circles on top of each other.
                if (layer.group != MagicLayerGroup.Alongside) RetireGroup(layer.group);

                // Staggered phases keep the circles of the same level from looking like clones.
                float phase = levelIndex * 0.35f + i * 0.5f;
                MagicLayerVisual visual = MagicLayerVisual.Add(_rig, layer, spawnEffectLights, phase,
                                                               sizeScale, circleHeightScale, rim);
                if (visual != null) _layers.Add(visual);
            }
        }

        /// <summary>Collapses the layers of one group that are still out (the previous circle / aura).</summary>
        void RetireGroup(MagicLayerGroup group)
        {
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                MagicLayerVisual visual = _layers[i];
                if (visual == null) { _layers.RemoveAt(i); continue; }
                if (visual.Group != group) continue;

                visual.Retire(Mathf.Max(0.18f, releaseCollapse));
                _layers.RemoveAt(i);
            }
        }

        /// <summary>
        /// The diameter of the biggest circle that is out once the given level is reached, in
        /// metres and before the tuning. A layer with no offset of its own orbits this rim, which
        /// is what makes the smaller circles travel around the edge of the main one.
        /// </summary>
        float RimDiameter(int levelIndex)
        {
            float biggest = 0f;

            for (int i = 0; i <= levelIndex && i < LevelCount; i++)
            {
                MagicSpellLevel lv = LevelAt(i + 1);
                if (lv == null || lv.layers == null) continue;

                float scale = Mathf.Max(0.25f, lv.circleScale);

                foreach (MagicLayer layer in lv.layers)
                    if (layer != null && layer.FitsToDiameter)
                        biggest = Mathf.Max(biggest, layer.diameter * scale);
            }

            return biggest;
        }

        void RetireLayers(float seconds)
        {
            for (int i = 0; i < _layers.Count; i++)
                if (_layers[i] != null) _layers[i].Retire(seconds);

            _layers.Clear();
        }

        void Release()
        {
            _charging = false;

            float held = _chargeTime;
            int level = _level;
            _chargeTime = 0f;
            _level = 0;

            RetireLayers(releaseCollapse);
            RestorePlayer();

            if (level >= 1 && held >= Mathf.Max(0f, minimumHoldToCast)) Fire(level, held);
            else Fizzle();
        }

        void Fizzle()
        {
            if (fizzleEffect != null)
                MagicEffects.DestroyLater(
                    MagicEffects.Spawn(fizzleEffect, PlayerCenter(), Quaternion.identity), 0f);

            Cooldown(fizzleCooldown);

            if (logCasts) Debug.Log("[MysticMap] Charge released too early - fizzle.", this);
            Fizzled?.Invoke();
        }

        void Cancel()
        {
            _charging = false;
            _chargeTime = 0f;
            _level = 0;
            RetireLayers(releaseCollapse);
            RestorePlayer();
        }

        void RestorePlayer()
        {
            if (player == null) return;
            player.speedScale = 1f;
            player.aimLock = false;
        }

        void Cooldown(float seconds)
        {
            _cooldownLength = Mathf.Max(0.05f, seconds);
            _cooldown = _cooldownLength;
        }

        // =====================================================================
        //  Casting
        // =====================================================================
        /// <summary>Fires the spell of the given level with the damage the hold time earned.</summary>
        void Fire(int level, float held)
        {
            MagicSpellLevel lv = LevelAt(level);
            if (lv == null) return;

            Vector3 aimDir = AimDirection();
            Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.05f, castHeight) +
                             aimDir * Mathf.Max(0f, castForwardOffset);

            MagicCastSpec spec = BuildSpec(lv, level, held, aimDir);
            float damage = spec.damage;

            _lastCastLevel = level;
            _lastCastDamage = damage;
            _lastCastTime = Time.time;

            switch (lv.delivery)
            {
                case SpellDelivery.Projectile:
                    MagicProjectile.Launch(lv.castEffect, origin, aimDir, lv, spec, gameObject, hitMask);
                    break;

                case SpellDelivery.ForwardBlast:
                {
                    // The blast lands on the ground in front of the slime, so the AoE effect sits
                    // on the terrain instead of floating at the height of the cast origin.
                    Vector3 center = transform.position + aimDir * Mathf.Max(0.5f, lv.castDistance);
                    center = Damage.SnapToGround(center, hitMask);
                    SpawnBlastVisuals(lv, center, aimDir, spec.maxCharge ? CraterEffect(lv) : null);
                    ScheduleBlast(center, aimDir, lv, spec);
                    break;
                }

                case SpellDelivery.Nova:
                {
                    Vector3 center = Damage.SnapToGround(transform.position, hitMask);
                    SpawnBlastVisuals(lv, center, aimDir, spec.maxCharge ? CraterEffect(lv) : null);
                    ScheduleBlast(center, aimDir, lv, spec);
                    break;
                }
            }

            Cooldown(lv.cooldown);

            if (player != null && lv.shake > 0f) player.ShakeCamera(lv.shake, 0.3f);

            if (logCasts)
                Debug.Log("[MysticMap] Cast " + lv.name + " (level " + level + ") - " +
                          damage.ToString("0.#") + " damage after " + held.ToString("0.00") + "s of charge" +
                          (spec.maxCharge ? " at MAXIMUM charge!" : "."));

            Cast?.Invoke(level, damage);
        }

        /// <summary>
        /// The effect that will sit on the impact point: the crater of a maximum-charge release, or
        /// the centre flourish of an ordinary one.
        /// </summary>
        static GameObject CraterEffect(MagicSpellLevel lv) =>
            lv == null ? null : lv.maxChargeEffect != null ? lv.maxChargeEffect : lv.centerEffect;

        /// <summary>
        /// Plays the effects of a blast / nova on the spot they land on.
        /// <paramref name="alsoPlayedByCrater"/> is the effect a maximum-charge release is going to
        /// play right there as well, so the same prefab is never spawned twice on one point.
        /// </summary>
        void SpawnBlastVisuals(MagicSpellLevel lv, Vector3 center, Vector3 aimDir,
                               GameObject alsoPlayedByCrater = null)
        {
            Quaternion rotation = Quaternion.LookRotation(aimDir, Vector3.up);
            float scale = Mathf.Max(0.01f, lv.castScale);

            if (lv.castEffect != null && lv.castEffect != alsoPlayedByCrater)
                MagicEffects.DestroyLater(MagicEffects.Spawn(lv.castEffect, center, rotation, null, scale));

            if (lv.centerEffect != null && lv.centerEffect != lv.castEffect &&
                lv.centerEffect != alsoPlayedByCrater)
                MagicEffects.DestroyLater(MagicEffects.Spawn(lv.centerEffect, center, rotation, null, scale));
        }

        void ScheduleBlast(Vector3 center, Vector3 direction, MagicSpellLevel lv, MagicCastSpec spec)
        {
            var blast = new PendingBlast
            {
                delay = Mathf.Max(0f, lv.impactDelay),
                center = center,
                direction = direction,
                spell = lv,
                damage = spec.damage,
                spec = spec
            };

            if (blast.delay <= 0.01f) ResolveBlast(blast);
            else _pending.Add(blast);
        }

        void TickPendingBlasts(float dt)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingBlast blast = _pending[i];
                blast.delay -= dt;

                if (blast.delay > 0f) { _pending[i] = blast; continue; }

                _pending.RemoveAt(i);
                ResolveBlast(blast);
            }
        }

        /// <summary>Lands a blast: damage everything in the radius, then let the world react.</summary>
        void ResolveBlast(PendingBlast blast)
        {
            MagicSpellLevel lv = blast.spell;
            if (lv == null) return;

            var info = new DamageInfo(blast.damage, blast.center, blast.direction, lv.knockback, gameObject);

            _points.Clear();
            int hits = Damage.ApplyRadial(blast.center, Mathf.Max(0.1f, lv.radius), info, transform, hitMask, _points);

            // One impact effect per victim, never twice on the same spot (the big explosion below
            // hovers over the same victims, and it used to show the same hit again).
            _shown.Clear();
            for (int i = 0; i < _points.Count; i++)
            {
                Vector3 spot = _points[i];
                if (!MagicEffects.Fresh(_shown, spot)) continue;

                _shown.Add(spot);
                MagicEffects.DestroyLater(MagicEffects.Spawn(lv.impactEffect, spot, Quaternion.identity), 0f);
            }

            // The ground itself reacts: grass is blown away, trees bend, stones are kicked.
            float envRadius = Mathf.Max(0.5f, lv.radius) * Mathf.Max(0f, blast.spec.environmentScale);
            if (envRadius > 0.01f)
            {
                MagicEnvironment.Impact(blast.center, envRadius, blast.spec.EnvironmentPower,
                                        blast.spec.debris, blast.spec.debrisPerHit);
                blast.spec.ThrowDebris(blast.center, blast.spec.debrisPerHit, 1.1f);
            }

            if (blast.spec.maxCharge)
                MaxChargeExplosion(lv, blast.center, blast.direction, blast.spec, transform, hitMask, _shown);

            if (logCasts)
                Debug.Log("[MysticMap] " + lv.name + " hit " + hits + " target(s) for " +
                          blast.damage.ToString("0.#") + " damage within " + lv.radius.ToString("0.#") + " m" +
                          (blast.spec.maxCharge ? " and exploded at maximum charge." : "."));
        }

        /// <summary>
        /// The big explosion of a maximum-charge release: a large effect on the impact point, extra
        /// damage over a wide radius, a shower of debris, camera shake - and, with a strong enough
        /// spell, the rocks and heavy props around it are shattered as well.
        ///
        /// Shared by the travelling spells and the ground blasts, so a fully charged level 5 nova
        /// and a fully charged level 4 eruption leave the same kind of crater behind.
        /// </summary>
        public static void MaxChargeExplosion(MagicSpellLevel lv, Vector3 center, Vector3 direction,
                                              MagicCastSpec spec, Transform ignore, LayerMask mask,
                                              List<Vector3> alreadyShown = null)
        {
            if (lv == null) return;

            float radius = Mathf.Max(0f, lv.maxChargeRadius) * Mathf.Max(0.05f, spec.environmentScale);

            GameObject effect = lv.maxChargeEffect != null ? lv.maxChargeEffect : lv.centerEffect;
            if (effect != null)
                MagicEffects.DestroyLater(MagicEffects.Spawn(effect, center,
                    Quaternion.LookRotation(direction.sqrMagnitude > 0.0001f ? direction : Vector3.forward, Vector3.up),
                    null, Mathf.Max(0.01f, lv.castScale)));

            if (radius > 0.1f)
            {
                // The world around the crater: rocks break, trees bend, props are blown apart.
                MagicEnvironment.Break(center, radius, Mathf.Max(2f, spec.EnvironmentPower + 0.6f),
                                       spec.debris, spec.debrisPerExplosion);
                spec.ThrowDebris(center, lv.maxChargeDebris, 1.3f);

                if (lv.maxChargeDamage > 0.01f)
                {
                    var info = new DamageInfo(spec.damage * lv.maxChargeDamage, center, direction,
                                              Mathf.Max(1f, spec.power),
                                              spec.caster != null ? spec.caster.gameObject : null);

                    var points = new List<Vector3>(16);
                    Damage.ApplyRadial(center, radius, info, ignore, mask, points);

                    // The crater effect is already on the centre and the primary hit already showed
                    // its impact on the victim, so only the fresh victims get one here.
                    _explosionShown.Clear();
                    _explosionShown.Add(center);
                    if (alreadyShown != null) _explosionShown.AddRange(alreadyShown);

                    for (int i = 0; i < points.Count; i++)
                    {
                        if (!MagicEffects.Fresh(_explosionShown, points[i])) continue;

                        _explosionShown.Add(points[i]);
                        MagicEffects.DestroyLater(
                            MagicEffects.Spawn(lv.impactEffect, points[i], Quaternion.identity), 0f);
                    }
                }
            }

            spec.Shake(lv.shake + lv.maxChargeShake, 0.45f);
        }

        // =====================================================================
        //  The circles follow the slime
        // =====================================================================
        void FollowRig()
        {
            if (_rig == null) return;

            // The layers destroy themselves when their collapse is over, and that is also what
            // ends the rig's life - so there is no bookkeeping to get wrong here.
            if (!_charging && _rig.childCount == 0)
            {
                Destroy(_rig.gameObject);
                _rig = null;
                return;
            }

            Vector3 want = transform.position + Vector3.up * 0.02f;

            // Flat on the ground: the slime bounces through its circles instead of carrying them
            // up and down with it, which is what makes them read as drawn on the floor.
            if (!circlesBounceWithSlime && GroundHeight(transform.position, out float ground))
                want.y = ground + Mathf.Max(0f, groundLift);

            if (rigFollowSmoothing > 0f)
                _rig.position = Vector3.SmoothDamp(_rig.position, want, ref _rigVelocity, rigFollowSmoothing);
            else
                _rig.position = want;
        }

        /// <summary>
        /// The height of the ground under a point, so the circles can lie flat on it. The slime's
        /// own colliders are skipped, and a mask of "nothing" counts everything as ground.
        /// </summary>
        bool GroundHeight(Vector3 from, out float y)
        {
            y = from.y;

            int mask = groundMask.value == 0 ? ~0 : groundMask.value;
            Vector3 origin = from + Vector3.up * 2f;

            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, 6f, mask,
                                                QueryTriggerInteraction.Ignore);

            bool found = false;
            float ground = 0f;

            for (int i = 0; i < count; i++)
            {
                Transform hit = _groundHits[i].transform;
                if (hit == null) continue;
                if (hit == transform || hit.IsChildOf(transform)) continue;    // never the slime itself
                if (found && _groundHits[i].point.y <= ground) continue;

                ground = _groundHits[i].point.y;
                found = true;
            }

            if (found) y = ground;
            return found;
        }

        /// <summary>The world position a burst effect leaves from.</summary>
        Vector3 PlayerCenter() => transform.position + Vector3.up * Mathf.Max(0.2f, castHeight);

        /// <summary>
        /// The direction the spell travels: where the slime looks, or where the camera looks.
        /// Always flattened, so a spell never dives into the ground.
        /// </summary>
        public Vector3 AimDirection()
        {
            Vector3 dir;
            if (aim == AimMode.PlayerFacing || player == null)
            {
                dir = player != null ? player.FacingDirection : transform.forward;
            }
            else
            {
                Camera cam = player.cam != null ? player.cam : Camera.main;
                dir = cam != null ? cam.transform.forward : player.FacingDirection;
            }

            dir.y = 0f;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        }

        // =====================================================================
        //  Input (mirrors the Unity Input System setup used by the project)
        // =====================================================================
        /// <summary>True while the skill button is held (left mouse, F, or the pad trigger).</summary>
        public bool ReadSkillButton()
        {
#if ENABLE_INPUT_SYSTEM
            if (requireCursorLock && Cursor.lockState != CursorLockMode.Locked) return false;

            if (chargeWithLeftMouse)
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.isPressed) return true;
            }

            if (chargeWithKey)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.fKey.isPressed) return true;
            }

            if (chargeWithGamepad)
            {
                var pad = Gamepad.current;
                if (pad != null && pad.rightTrigger.isPressed) return true;
            }

            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (requireCursorLock && Cursor.lockState != CursorLockMode.Locked) return false;

            if (chargeWithLeftMouse && Input.GetMouseButton(0)) return true;
            if (chargeWithKey && Input.GetKey(KeyCode.F)) return true;
            if (chargeWithGamepad && Input.GetKey(KeyCode.JoystickButton5)) return true;   // right trigger
            return false;
#else
            return false;
#endif
        }

        void OnDrawGizmosSelected()
        {
            Vector3 aimDir = player != null ? player.FacingDirection : transform.forward;
            Vector3 origin = transform.position + Vector3.up * Mathf.Max(0.05f, castHeight);

            Gizmos.color = new Color(0.7f, 0.6f, 1f, 0.9f);
            Gizmos.DrawLine(origin, origin + aimDir * 5f);

            // How far in front of the slime a blast would land, and how wide it would be.
            Gizmos.color = new Color(1f, 0.5f, 0.9f, 0.45f);
            for (int i = 1; i <= LevelCount; i++)
            {
                MagicSpellLevel lv = LevelAt(i);
                if (lv == null) continue;

                Vector3 center = lv.delivery == SpellDelivery.Nova
                    ? transform.position
                    : transform.position + aimDir * Mathf.Max(0.5f, lv.castDistance);

                Gizmos.DrawWireSphere(center, Mathf.Max(0.2f, lv.radius));
            }
        }
    }
}
