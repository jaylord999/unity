using UnityEngine;

namespace MysticMap.World
{
    /// <summary>
    /// Deterministic, allocation-free value noise. Unity's Mathf.PerlinNoise is not
    /// seed-controllable, so the world uses this instead: every layer is derived purely
    /// from integer lattice hashes, which makes the terrain field reproducible on any
    /// machine and lets two streams compare identical worlds.
    /// </summary>
    public static class ProcNoise
    {
        // ---- single octave (smooth value noise, two rotated lattices to hide the grid) --

        static float Value(float x, float y, uint salt)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;

            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);

            float a = DetHash.Float01(DetHash.Hash(xi, yi, salt));
            float b = DetHash.Float01(DetHash.Hash(xi + 1, yi, salt));
            float c = DetHash.Float01(DetHash.Hash(xi, yi + 1, salt));
            float d = DetHash.Float01(DetHash.Hash(xi + 1, yi + 1, salt));

            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        /// <summary>Smooth noise in [0, 1] with a second, rotated lattice mixed in.</summary>
        public static float Noise(float x, float y, uint salt)
        {
            float n = Value(x, y, salt);
            // Rotated copy (45 degrees, slightly different scale) breaks the axis alignment
            // that plain value noise shows on large flat areas.
            float rx = (x * 0.7071f - y * 0.7071f + 137.13f) * 1.03f;
            float ry = (x * 0.7071f + y * 0.7071f - 71.91f) * 1.03f;
            n += Value(rx, ry, salt ^ 0x9E3779B9u);
            return n * 0.5f;
        }

        /// <summary>Fractal noise in [0, 1].</summary>
        public static float Fbm(float x, float y, uint salt, int octaves = 4,
                                float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            float fx = x, fy = y;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(fx, fy, salt + (uint)o * 1013u) * amp;
                norm += amp;
                amp *= gain;
                fx *= lacunarity;
                fy *= lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>Ridged fractal noise in [0, 1] - sharp crests, good for rock and mountains.</summary>
        public static float Ridged(float x, float y, uint salt, int octaves = 4,
                                   float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            float fx = x, fy = y;
            for (int o = 0; o < octaves; o++)
            {
                float n = Noise(fx, fy, salt + (uint)o * 7919u);
                float r = 1f - Mathf.Abs(2f * n - 1f);
                sum += r * r * amp;
                norm += amp;
                amp *= gain;
                fx *= lacunarity;
                fy *= lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>
        /// Domain warp: nudges the sample point by another noise field. This is what makes
        /// biome borders interlock naturally instead of reading as smooth blobs.
        /// </summary>
        public static Vector2 Warp(float x, float y, float strength, uint salt)
        {
            float wx = Fbm(x * 0.0016f, y * 0.0016f, salt + 17u, 3) * 2f - 1f;
            float wy = Fbm(x * 0.0016f + 41.7f, y * 0.0016f - 13.3f, salt + 71u, 3) * 2f - 1f;
            return new Vector2(x + wx * strength, y + wy * strength);
        }

        /// <summary>Smooth Hermite step, handy for falloffs.</summary>
        public static float Smoothstep(float edge0, float edge1, float v)
        {
            if (Mathf.Approximately(edge0, edge1)) return v < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((v - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
