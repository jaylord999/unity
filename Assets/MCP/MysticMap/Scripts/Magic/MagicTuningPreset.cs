using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// A saved copy of every number the magic tuning window can change.
    ///
    /// The tuning normally lives on the SlimeMagic component, so it is part of the scene - but the
    /// scene is not where those numbers always survive: rebuilding the five levels, reloading the
    /// scene or tuning while the game plays (play-mode changes are discarded on stop) all used to
    /// throw hand-tuned numbers away. This asset is the little save file the editor tools write to
    /// and read back, so "my settings did not stick" cannot happen again.
    /// </summary>
    [CreateAssetMenu(menuName = "MysticMap/Magic tuning preset", fileName = "MagicTuningPreset")]
    public class MagicTuningPreset : ScriptableObject
    {
        /// <summary>The size / motion numbers of one circle: one layer of one level.</summary>
        [System.Serializable]
        public class LayerValues
        {
            public string name = "Magic circle";
            public float diameter = 4.6f;
            public float height = 0.05f;
            public float orbitRadius;
            public float tilt;
            public float spin = 18f;
            public float orbit;
            public float pulse;
            public float growIn = 0.35f;
            public int rimRiders;
            public float riderScale = 0.22f;
            public float riderOffset = 0.05f;
            public float riderLap = 6f;
            public float riderSpin = 120f;
            public MagicLayerGroup group = MagicLayerGroup.Alongside;
        }

        /// <summary>The numbers of one threshold: when it is reached and what it does.</summary>
        [System.Serializable]
        public class LevelValues
        {
            public string name = "Aether Slash";
            public float holdToReach;
            public float damage = 14f;
            public float circleScale = 1f;
            public float environmentScale = 1f;
            public float maxChargeRadius;
            public LayerValues[] layers;
        }

        [Header("Circles")]
        public float circleScale = 1.35f;
        public float circleHeightScale = 1f;
        public float chargeSwell = 0.22f;
        public float chargeSpinUp = 1.2f;
        public bool swapCirclesEachLevel = true;
        public bool circlesBounceWithSlime;
        public float groundLift = 0.03f;
        public LayerMask groundMask = ~0;

        [Header("Thresholds")]
        public float thresholdScale = 1f;
        public float thresholdBias;

        [Header("World")]
        public float environmentScale = 1f;
        public float auraWindStrength = 0.6f;
        public float auraWindScale = 1f;
        public float rockBreakPower = 1.5f;
        public int debrisPerHit = 6;
        public int debrisPerExplosion = 16;
        public GameObject[] debrisPrefabs;

        [Header("Cast")]
        public float overchargeWindow = 2.5f;
        public float overchargePerSecond = 0.25f;
        public float overchargeCap = 1f;
        public float minimumHoldToCast = 0.12f;
        public float releaseCollapse = 0.18f;

        [Header("Every level and every circle")]
        [Tooltip("Which layout of the built-in levels this copy was taken from (see " +
                 "SlimeMagic.LayoutVersion). An older copy keeps its thresholds but never puts the " +
                 "old circle sizes back on top of a newer layout.")]
        public int layout;
        public LevelValues[] levels;

        /// <summary>Copies everything the magic component holds into this preset.</summary>
        public void Capture(SlimeMagic magic) => Capture(magic, true);

        /// <summary>
        /// Copies the tuning into this preset. <paramref name="includePrefabs"/> false leaves the
        /// debris prefab list behind, which is what a snapshot that travels through JSON needs
        /// (object references cannot be restored from a string).
        /// </summary>
        public void Capture(SlimeMagic magic, bool includePrefabs)
        {
            if (magic == null) return;

            circleScale = magic.circleScale;
            circleHeightScale = magic.circleHeightScale;
            chargeSwell = magic.chargeSwell;
            chargeSpinUp = magic.chargeSpinUp;
            swapCirclesEachLevel = magic.swapCirclesEachLevel;
            circlesBounceWithSlime = magic.circlesBounceWithSlime;
            groundLift = magic.groundLift;
            groundMask = magic.groundMask;

            thresholdScale = magic.thresholdScale;
            thresholdBias = magic.thresholdBias;

            environmentScale = magic.environmentScale;
            auraWindStrength = magic.auraWindStrength;
            auraWindScale = magic.auraWindScale;
            rockBreakPower = magic.rockBreakPower;
            debrisPerHit = magic.debrisPerHit;
            debrisPerExplosion = magic.debrisPerExplosion;
            if (includePrefabs) debrisPrefabs = magic.debrisPrefabs;

            overchargeWindow = magic.overchargeWindow;
            overchargePerSecond = magic.overchargePerSecond;
            overchargeCap = magic.overchargeCap;
            minimumHoldToCast = magic.minimumHoldToCast;
            releaseCollapse = magic.releaseCollapse;

            layout = magic.levelsLayout;
            levels = CaptureLevels(magic.levels);
        }

        /// <summary>Writes this preset back onto a magic component (the bar is refreshed too).</summary>
        public void Apply(SlimeMagic magic)
        {
            if (magic == null) return;

            // A copy taken before the levels changed layout must not push the old (big, tall)
            // circles back on top of the new flat ones, so its sizes stay where they are.
            bool currentLayout = layout >= SlimeMagic.LayoutVersion;

            magic.circleScale = Mathf.Clamp(circleScale, 0.25f, currentLayout ? 4f : 1.6f);
            magic.circleHeightScale = Mathf.Clamp(circleHeightScale, 0.25f, 4f);
            magic.chargeSwell = Mathf.Clamp(chargeSwell, 0f, 1f);
            magic.chargeSpinUp = Mathf.Clamp(chargeSpinUp, 0f, 3f);
            magic.swapCirclesEachLevel = swapCirclesEachLevel;
            magic.circlesBounceWithSlime = circlesBounceWithSlime;
            magic.groundLift = Mathf.Clamp(groundLift, 0f, 1f);
            magic.groundMask = groundMask;

            magic.thresholdScale = Mathf.Clamp(thresholdScale, 0.25f, 4f);
            magic.thresholdBias = Mathf.Clamp(thresholdBias, -1f, 4f);

            magic.environmentScale = Mathf.Clamp(environmentScale, 0f, 3f);
            magic.auraWindStrength = Mathf.Clamp(auraWindStrength, 0f, 2f);
            magic.auraWindScale = Mathf.Clamp(auraWindScale, 0f, 3f);
            magic.rockBreakPower = Mathf.Clamp(rockBreakPower, 0.5f, 3f);
            magic.debrisPerHit = Mathf.Clamp(debrisPerHit, 0, 24);
            magic.debrisPerExplosion = Mathf.Clamp(debrisPerExplosion, 0, 40);
            if (debrisPrefabs != null && debrisPrefabs.Length > 0) magic.debrisPrefabs = debrisPrefabs;

            magic.overchargeWindow = Mathf.Clamp(overchargeWindow, 0.25f, 8f);
            magic.overchargePerSecond = Mathf.Clamp(overchargePerSecond, 0f, 1f);
            magic.overchargeCap = Mathf.Clamp(overchargeCap, 0f, 3f);
            magic.minimumHoldToCast = Mathf.Clamp(minimumHoldToCast, 0f, 0.6f);
            magic.releaseCollapse = Mathf.Clamp(releaseCollapse, 0.02f, 1f);

            ApplyLevels(magic.levels, currentLayout);

            magic.RefreshGauge();
        }

        // =====================================================================
        //  Copying every level and every circle in and out
        // =====================================================================
        static LevelValues[] CaptureLevels(MagicSpellLevel[] source)
        {
            if (source == null) return new LevelValues[0];

            var copy = new LevelValues[source.Length];

            for (int i = 0; i < source.Length; i++)
            {
                MagicSpellLevel level = source[i];
                if (level == null) { copy[i] = new LevelValues(); continue; }

                copy[i] = new LevelValues
                {
                    name = level.name,
                    holdToReach = level.holdToReach,
                    damage = level.damage,
                    circleScale = level.circleScale,
                    environmentScale = level.environmentScale,
                    maxChargeRadius = level.maxChargeRadius,
                    layers = CaptureLayers(level.layers)
                };
            }

            return copy;
        }

        static LayerValues[] CaptureLayers(MagicLayer[] source)
        {
            if (source == null) return new LayerValues[0];

            var copy = new LayerValues[source.Length];

            for (int i = 0; i < source.Length; i++)
            {
                MagicLayer layer = source[i];
                if (layer == null) { copy[i] = new LayerValues(); continue; }

                copy[i] = new LayerValues
                {
                    name = layer.effect != null ? layer.effect.name : "circle",
                    diameter = layer.diameter,
                    height = layer.height,
                    orbitRadius = layer.orbitRadius,
                    tilt = layer.tilt,
                    spin = layer.spin,
                    orbit = layer.orbit,
                    pulse = layer.pulse,
                    growIn = layer.growIn,
                    rimRiders = layer.rimRiders,
                    riderScale = layer.riderScale,
                    riderOffset = layer.riderOffset,
                    riderLap = layer.riderLap,
                    riderSpin = layer.riderSpin,
                    group = layer.group
                };
            }

            return copy;
        }

        /// <summary>
        /// Writes the level numbers onto the component's levels. <paramref name="sizes"/> false keeps
        /// the thresholds and damage but leaves the circle sizes alone, which is what a copy taken
        /// before the levels changed layout has to do.
        /// </summary>
        void ApplyLevels(MagicSpellLevel[] target, bool sizes)
        {
            if (target == null || levels == null) return;

            int count = Mathf.Min(target.Length, levels.Length);

            for (int i = 0; i < count; i++)
            {
                MagicSpellLevel level = target[i];
                LevelValues values = levels[i];
                if (level == null || values == null) continue;

                level.holdToReach = Mathf.Max(0f, values.holdToReach);
                level.damage = Mathf.Max(0f, values.damage);
                level.environmentScale = Mathf.Clamp(values.environmentScale, 0f, 2f);
                level.maxChargeRadius = Mathf.Max(0f, values.maxChargeRadius);

                if (!sizes) continue;

                level.circleScale = Mathf.Clamp(values.circleScale, 0.25f, 3f);

                if (level.layers == null || values.layers == null) continue;

                int layers = Mathf.Min(level.layers.Length, values.layers.Length);

                for (int j = 0; j < layers; j++)
                {
                    MagicLayer layer = level.layers[j];
                    LayerValues layerValues = values.layers[j];
                    if (layer == null || layerValues == null) continue;

                    layer.diameter = Mathf.Max(0f, layerValues.diameter);
                    layer.height = layerValues.height;
                    layer.orbitRadius = Mathf.Max(0f, layerValues.orbitRadius);
                    layer.tilt = layerValues.tilt;
                    layer.spin = layerValues.spin;
                    layer.orbit = layerValues.orbit;
                    layer.pulse = Mathf.Clamp(layerValues.pulse, 0f, 0.5f);
                    layer.growIn = Mathf.Max(0f, layerValues.growIn);
                    layer.rimRiders = Mathf.Clamp(layerValues.rimRiders, 0, 16);
                    layer.riderScale = Mathf.Clamp(layerValues.riderScale, 0.04f, 1f);
                    layer.riderOffset = Mathf.Clamp(layerValues.riderOffset, 0f, 0.8f);
                    layer.riderLap = Mathf.Max(0f, layerValues.riderLap);
                    layer.riderSpin = layerValues.riderSpin;
                    layer.group = layerValues.group;
                }
            }
        }
    }
}
