using UnityEngine;

namespace MysticMap.World
{
    /// <summary>The kinds of country the endless world is made of.</summary>
    public enum BiomeType
    {
        Meadow = 0,
        Forest = 1,
        Highlands = 2,
        Rocky = 3,
        Ruins = 4,
        Labyrinth = 5,
    }

    /// <summary>
    /// How strongly each biome applies at a world position. Always normalized to 1, so
    /// several biomes can share the ground at a border and everything (height, ground
    /// colour, vegetation density) blends across the seam instead of snapping.
    /// </summary>
    public struct BiomeWeights
    {
        public float meadow, forest, highlands, rocky, ruins, labyrinth;

        public float this[BiomeType t]
        {
            get
            {
                switch (t)
                {
                    case BiomeType.Forest: return forest;
                    case BiomeType.Highlands: return highlands;
                    case BiomeType.Rocky: return rocky;
                    case BiomeType.Ruins: return ruins;
                    case BiomeType.Labyrinth: return labyrinth;
                    default: return meadow;
                }
            }
        }

        public static BiomeWeights Single(BiomeType t)
        {
            var b = new BiomeWeights();
            b.meadow = b.forest = b.highlands = b.rocky = b.ruins = b.labyrinth = 0f;
            switch (t)
            {
                case BiomeType.Forest: b.forest = 1f; break;
                case BiomeType.Highlands: b.highlands = 1f; break;
                case BiomeType.Rocky: b.rocky = 1f; break;
                case BiomeType.Ruins: b.ruins = 1f; break;
                case BiomeType.Labyrinth: b.labyrinth = 1f; break;
                default: b.meadow = 1f; break;
            }
            return b;
        }

        public float Total => meadow + forest + highlands + rocky + ruins + labyrinth;

        public BiomeWeights Normalized()
        {
            float t = Total;
            if (t <= 0.0001f) return Single(BiomeType.Meadow);
            meadow /= t; forest /= t; highlands /= t; rocky /= t; ruins /= t; labyrinth /= t;
            return this;
        }

        public BiomeType Dominant
        {
            get
            {
                BiomeType best = BiomeType.Meadow;
                float bestW = meadow;
                if (forest > bestW) { best = BiomeType.Forest; bestW = forest; }
                if (highlands > bestW) { best = BiomeType.Highlands; bestW = highlands; }
                if (rocky > bestW) { best = BiomeType.Rocky; bestW = rocky; }
                if (ruins > bestW) { best = BiomeType.Ruins; bestW = ruins; }
                if (labyrinth > bestW) { best = BiomeType.Labyrinth; bestW = labyrinth; }
                return best;
            }
        }

        /// <summary>0..1 "how wild is this spot" - used to keep the area near the town calm.</summary>
        public float Wildness => Mathf.Clamp01(highlands + rocky * 1.2f + labyrinth * 0.6f);
    }


    /// <summary>
    /// Decides which terrain type is where. Two slow, domain-warped noise fields
    /// (moisture and ruggedness) are mapped onto biome centroids with a smooth
    /// exponential falloff, which is what makes the borders melt into each other
    /// instead of forming straight or blobby edges.
    /// </summary>
    public class BiomeGenerator
    {
        readonly WorldSettings _s;
        readonly uint _salt;

        // Biome "ideal" positions in (rugged, moisture) space plus how much map they own.
        static readonly Vector2[] Centroid =
        {
            new Vector2(0.16f, 0.58f),   // Meadow
            new Vector2(0.32f, 0.86f),   // Forest
            new Vector2(0.58f, 0.52f),   // Highlands
            new Vector2(0.88f, 0.34f),   // Rocky
            new Vector2(0.46f, 0.16f),   // Ruins
            new Vector2(0.26f, 0.30f),   // Labyrinth
        };

        static readonly float[] Rarity = { 1f, 1f, 1f, 1f, 0.34f, 0.30f };

        const float Spread = 0.30f;

        public BiomeGenerator(WorldSettings settings)
        {
            _s = settings;
            _salt = (uint)settings.seed * 0x9E3779B1u;
        }

        public float Moisture(float x, float z)
        {
            Vector2 w = ProcNoise.Warp(x, z, 220f, _salt + 101u);
            return ProcNoise.Fbm(w.x * 0.0013f, w.y * 0.0013f, _salt + 11u, 4);
        }

        public float Ruggedness(float x, float z)
        {
            Vector2 w = ProcNoise.Warp(x, z, 260f, _salt + 505u);
            float f = ProcNoise.Fbm(w.x * 0.0011f, w.y * 0.0011f, _salt + 23u, 4);
            float r = ProcNoise.Ridged(w.x * 0.0021f, w.y * 0.0021f, _salt + 37u, 3);
            return Mathf.Clamp01(f * 0.65f + r * 0.35f);
        }

        /// <summary>Normalized biome mix at a world position (always sums to 1).</summary>
        public BiomeWeights Weights(float x, float z)
        {
            float moisture = Moisture(x, z);
            float rugged = Ruggedness(x, z);
            var p = new Vector2(rugged, moisture);

            float w0, w1, w2, w3, w4, w5;
            float total = 0f;

            w0 = Blend(p, 0, ref total);
            w1 = Blend(p, 1, ref total);
            w2 = Blend(p, 2, ref total);
            w3 = Blend(p, 3, ref total);
            w4 = Blend(p, 4, ref total);
            w5 = Blend(p, 5, ref total);

            if (total <= 0.00001f) return BiomeWeights.Single(BiomeType.Meadow);

            var bw = new BiomeWeights
            {
                meadow = w0,
                forest = w1,
                highlands = w2,
                rocky = w3,
                ruins = w4,
                labyrinth = w5,
            };
            bw = bw.Normalized();

            // Keep the country right around the town gentle and open, so the fortress sits
            // in a calm valley instead of on the edge of a stone maze.
            float dTown = Vector2.Distance(new Vector2(x, z), _townCenter);
            float calm = 1f - ProcNoise.Smoothstep(180f, 620f, dTown);
            if (calm > 0.001f)
            {
                var soft = new BiomeWeights
                {
                    meadow = bw.meadow * (1f - calm) + calm * 0.6f,
                    forest = bw.forest * (1f - calm) + calm * 0.4f,
                    highlands = bw.highlands * (1f - calm) + calm * 0.05f,
                    rocky = bw.rocky * (1f - calm) + calm * 0.02f,
                    ruins = bw.ruins * (1f - calm),
                    labyrinth = bw.labyrinth * (1f - calm),
                };
                bw = soft.Normalized();
            }

            return bw;
        }

        static float Blend(Vector2 p, int i, ref float total)
        {
            float d = Vector2.Distance(p, Centroid[i]);
            float w = Mathf.Exp(-(d * d) / (Spread * Spread)) * Rarity[i];
            total += w;
            return w;
        }

        // The fortress town centre (MysticMapBuilder.Village).
        static readonly Vector2 _townCenter = new Vector2(500f, 520f);
    }
}
