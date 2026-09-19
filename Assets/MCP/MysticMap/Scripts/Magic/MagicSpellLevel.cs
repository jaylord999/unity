using UnityEngine;

namespace MysticMap
{
    /// <summary>Where a spell's magic comes out.</summary>
    public enum SpellDelivery
    {
        /// <summary>A slash / bolt that travels forward and hurts what it touches (splash optional).</summary>
        Projectile,
        /// <summary>Fireworks on a point a few metres in front of the slime (lasers, crystals, meteors).</summary>
        ForwardBlast,
        /// <summary>Everything around the slime is hit at once (a nova, for the top level).</summary>
        Nova
    }

    /// <summary>What "the front of the player" means when a spell is fired.</summary>
    public enum AimMode
    {
        /// <summary>Fire along the direction the slime is facing (its last travel direction).</summary>
        PlayerFacing,
        /// <summary>Turn the slime towards the camera while charging and fire where you look.</summary>
        CameraForward
    }

    /// <summary>
    /// What a magic-circle layer does when a later threshold brings its own.
    /// </summary>
    public enum MagicLayerGroup
    {
        /// <summary>Stays alongside everything else (it is added, never swapped).</summary>
        Alongside,
        /// <summary>The level's one magic circle: when a new circle blooms, this one collapses.</summary>
        Circle,
        /// <summary>The level's one aura: when a new aura blooms, this one collapses.</summary>
        Aura
    }

    /// <summary>
    /// One visual layer of a charging spell: a magic circle, an aura, a shield...
    ///
    /// The layers are what makes the Tensura-like look - every level reached adds another ring,
    /// each with its own height, tilt, spin and orbit, so the circles nest and revolve around
    /// each other instead of stacking on top of each other. A layer that belongs to the Circle or
    /// Aura group takes the place of the one before it, so a threshold swaps the magic out
    /// instead of piling it up.
    /// </summary>
    [System.Serializable]
    public class MagicLayer
    {
        [Tooltip("Effect prefab (a Hovl magic circle / aura / shield). What happens to the layers of " +
                 "the earlier levels is decided by the group below.")]
        public GameObject effect;

        [Header("Placement")]
        [Tooltip("Fit the effect's footprint to this diameter in metres (0 = keep the prefab scale). " +
                 "The magic tuning multiplies this, so a circle can be resized without editing layers.")]
        public float diameter = 4.6f;
        [Tooltip("Height above the slime's feet, in metres.")]
        public float height = 0.05f;
        [Tooltip("Metres off-centre: with orbit set too, the circle revolves around the slime. " +
                 "0 = it automatically rides the rim of the biggest circle that is already out, so " +
                 "the smaller circles turn around the main one like the hands of a clock.")]
        public float orbitRadius;
        [Tooltip("Degrees the circle leans over (a tilted ring reads as a second 3D layer).")]
        public float tilt;

        [Header("Motion")]
        [Tooltip("Degrees per second the effect turns on its own axis (negative = the other way).")]
        public float spin = 18f;
        [Tooltip("Degrees per second the circle travels around the slime.")]
        public float orbit;
        [Tooltip("Breathing scale, 0 = none.")]
        [Range(0f, 0.5f)] public float pulse;
        [Tooltip("Seconds the circle takes to bloom to full size when its level is reached.")]
        public float growIn = 0.35f;

        [Header("Clock riders (little elements circling the rim)")]
        [Tooltip("How many small copies of the effect travel around this circle's rim, like the " +
                 "hands and pips of a clock face. 0 = none.")]
        [Range(0, 16)] public int rimRiders;
        [Tooltip("Size of one rider, as a fraction of this circle's diameter.")]
        [Range(0.04f, 1f)] public float riderScale = 0.22f;
        [Tooltip("How far outside the rim the riders travel, as a fraction of the radius.")]
        [Range(0f, 0.8f)] public float riderOffset = 0.05f;
        [Tooltip("Seconds one lap around the rim takes (0 = the riders only spin on the spot).")]
        public float riderLap = 6f;
        [Tooltip("Extra degrees per second each rider turns on its own axis.")]
        public float riderSpin = 120f;
        [Tooltip("Effect the riders use (empty = the same effect as the circle itself).")]
        public GameObject riderEffect;

        [Header("One at a time")]
        [Tooltip("Circle = this is the level's magic circle, Aura = its aura. Only one layer of each " +
                 "group is ever out: when a later threshold brings its own, the one before it " +
                 "collapses, so the magic changes instead of piling up. Alongside = simply added.")]
        public MagicLayerGroup group = MagicLayerGroup.Alongside;

        /// <summary>True when this layer should put riders on its rim.</summary>
        public bool HasRiders => rimRiders > 0 && effect != null;

        [Header("Look")]
        [Tooltip("Multiplied into the effect's additive materials (white = leave the asset colours).")]
        public Color tint = Color.white;

        /// <summary>True when the layer should be auto-scaled to <see cref="diameter"/>.</summary>
        public bool FitsToDiameter => diameter > 0.01f;
    }

    /// <summary>
    /// One threshold of the charge: how long you have to hold the button, what starts playing
    /// around the player when you get there, and what the release fires.
    /// </summary>
    [System.Serializable]
    public class MagicSpellLevel
    {
        [Header("Identity")]
        [Tooltip("Shown on the magic bar.")]
        public string name = "Aether Slash";
        [Tooltip("Element / school, also shown on the magic bar.")]
        public string school = "Wind";
        [Tooltip("Colour of this level on the magic bar (and of its circles when they are tinted).")]
        public Color tint = new Color(0.55f, 0.85f, 1f, 1f);

        [Header("Charge")]
        [Tooltip("Seconds of holding the button needed before this level is reached.")]
        public float holdToReach = 0.35f;
        [Tooltip("How fast the slime may move while charging at this level (1 = no slow-down).")]
        [Range(0.05f, 1f)] public float chargeMoveScale = 0.9f;
        [Tooltip("Delay before the damage lands, so it matches a long wind-up effect.")]
        public float impactDelay = 0f;
        [Tooltip("Seconds before the same spell can be charged again.")]
        public float cooldown = 0.4f;

        [Header("Circles added at this level")]
        [Tooltip("Multiplies the size of this level's circles: a later, more powerful threshold can " +
                 "bloom visibly bigger circles, so the magic changes as each level is reached. " +
                 "1 = the diameters written on the layers below.")]
        [Range(0.25f, 3f)] public float circleScale = 1f;
        [Tooltip("Effect layers that start playing around the slime at this level and stay there " +
                 "while the button is held.")]
        public MagicLayer[] layers;
        [Tooltip("One-shot effect the moment this level is reached (a small burst of sparks).")]
        public GameObject levelBurst;

        [Header("World")]
        [Tooltip("How much this level disturbs the world it lands in (grass torn away, trees " +
                 "shaken, rocks kicked). 0 = it only hurts creatures.")]
        [Range(0f, 2f)] public float environmentScale = 1f;

        [Header("Maximum charge (the overcharge window is full)")]
        [Tooltip("Effect of the big explosion a maximum-charge release makes where it lands.")]
        public GameObject maxChargeEffect;
        [Tooltip("Radius of that explosion, in metres (0 = this level has no big explosion).")]
        public float maxChargeRadius;
        [Tooltip("Extra damage of that explosion, as a fraction of the spell's damage.")]
        [Range(0f, 3f)] public float maxChargeDamage = 0.75f;
        [Tooltip("Debris thrown by the big explosion.")]
        [Range(0, 40)] public int maxChargeDebris = 14;
        [Tooltip("Extra camera shake for the big explosion.")]
        public float maxChargeShake = 0.5f;
        [Tooltip("The big explosion shatters the rocks and heavy props around the impact point.")]
        public bool maxChargeBreaksWorld = true;

        [Header("Cast")]
        public SpellDelivery delivery = SpellDelivery.Projectile;
        public GameObject castEffect;
        [Tooltip("Extra one-shot played at the centre of a blast / nova.")]
        public GameObject centerEffect;
        [Tooltip("Played on every victim that is actually hit.")]
        public GameObject impactEffect;

        [Header("Power")]
        [Tooltip("Damage of the spell at this level (holding longer adds overcharge on top).")]
        public float damage = 14f;
        [Tooltip("How far in front of the slime a forward blast lands, in metres.")]
        public float castDistance = 6f;
        [Tooltip("Radius of a projectile / blast / nova, in metres.")]
        public float radius = 3f;
        [Tooltip("Projectile speed in m/s.")]
        public float speed = 22f;
        [Tooltip("Projectile keeps a fixed height above the ground (ground-skimming slashes).")]
        public bool hugGround = true;
        [Tooltip("Height above the ground a ground-hugging projectile flies at.")]
        public float hugHeight = 0.45f;
        [Tooltip("Keep going through victims (each one is still only hit once).")]
        public bool pierce;
        [Tooltip("Fraction of the damage that also lands around a projectile's impact point.")]
        [Range(0f, 1f)] public float splash;
        [Tooltip("Radius of that splash, in metres.")]
        public float splashRadius = 3f;
        [Tooltip("Scale of the cast effect (the pack effects are built at very different sizes).")]
        public float castScale = 1f;
        [Tooltip("Camera shake on release (0 = none).")]
        public float shake;
        [Tooltip("Knockback given to whatever is hit (metres, 0 = none).")]
        public float knockback;

        /// <summary>Damage a release right now would deal, overcharge included.</summary>
        public float DamageAfterHold(float heldSeconds, float overchargePerSecond, float overchargeCap)
        {
            float beyond = Mathf.Max(0f, heldSeconds - Mathf.Max(0f, holdToReach));
            float bonus = Mathf.Clamp(beyond * Mathf.Max(0f, overchargePerSecond), 0f, Mathf.Max(0f, overchargeCap));
            return Mathf.Max(0f, damage) * (1f + bonus);
        }
    }
}
