using System.Collections.Generic;
using UnityEngine;
using MysticMap.World;

namespace MysticMap
{
    /// <summary>
    /// The winding road network of the hand-built 1 km map: ONE shared source of truth used by
    /// the terrain splat bake, the grass/tree clearance, the roadside props and the runtime
    /// spawners. Deterministic (fixed seed) so it never changes between runs.
    ///
    /// Roads run dead straight through the town and for the last stretch at the map border (so
    /// they still meet the gates and the procedural roads at the edges) and wind hard in the
    /// open country - that is what makes the roads visibly curve.
    /// </summary>
    public static class MapRoads
    {
        public const float MapSize = 1000f;
        public const float HalfWidth = 3.6f;
        public const float FortHX = 57f;
        public const float FortHZ = 47f;
        public static readonly Vector2 TownCenter = new Vector2(500f, 520f);

        const float SampleStep = 10f;    // centre-line sample spacing (m)
        const float SnakeAmp = 95f;      // main serpentine swing (m) - the hard S-bends
        const float SnakeWave = 230f;    // one full left-right S covers this distance (m)
        const float Wander = 35f;        // smooth noise wander on top (m)
        const float Detail = 16f;        // fine wrinkle (m)
        const uint Seed = 0x4D4953u;

        static List<Vector2[]> _paths;
        static List<Rect> _bounds;

        /// <summary>The winding centre lines (cached, deterministic).</summary>
        public static List<Vector2[]> Paths
        {
            get { if (_paths == null) Build(); return _paths; }
        }

        /// <summary>Distance from a point to the nearest road centre line (metres; large if none).</summary>
        public static float Distance(float x, float z)
        {
            if (_paths == null) Build();
            var p = new Vector2(x, z);
            float best = float.MaxValue;
            for (int k = 0; k < _paths.Count; k++)
            {
                Rect r = _bounds[k];
                if (x < r.xMin - best || x > r.xMax + best ||
                    z < r.yMin - best || z > r.yMax + best) continue;

                Vector2[] pts = _paths[k];
                for (int i = 1; i < pts.Length; i++)
                {
                    float d = DistToSeg(pts[i - 1], pts[i], p);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        /// <summary>Every road split into consecutive 2-point segments (prop placement along roads).</summary>
        public static List<Vector2[]> Segments()
        {
            if (_paths == null) Build();
            var segs = new List<Vector2[]>();
            for (int k = 0; k < _paths.Count; k++)
            {
                Vector2[] pts = _paths[k];
                for (int i = 1; i < pts.Length; i++) segs.Add(new[] { pts[i - 1], pts[i] });
            }
            return segs;
        }

        static void Build()
        {
            _paths = new List<Vector2[]>();
            _bounds = new List<Rect>();
            float vx = TownCenter.x, vz = TownCenter.y;
            const float Lo = 2f, Hi = MapSize - 2f;

            Add(Wind(new Vector2(vx, Lo), new Vector2(vx, Hi), Seed + 11u));                 // main N-S
            Add(Wind(new Vector2(Lo, vz + 350f), new Vector2(Hi, vz + 350f), Seed + 23u));   // E-W north
            Add(Wind(new Vector2(Lo, vz - 350f), new Vector2(Hi, vz - 350f), Seed + 37u));   // E-W south
            Add(Wind(new Vector2(vx - 315f, Lo), new Vector2(vx - 315f, Hi), Seed + 53u));   // N-S west
            Add(Wind(new Vector2(vx + 315f, Lo), new Vector2(vx + 315f, Hi), Seed + 71u));   // N-S east

            // Interior east-west avenue: inside the town, so it stays straight.
            Add(new[] { new Vector2(vx - (FortHX - 6f), vz), new Vector2(vx + (FortHX - 6f), vz) });
        }

        static void Add(Vector2[] path)
        {
            _paths.Add(path);
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < path.Length; i++)
            {
                minX = Mathf.Min(minX, path[i].x); maxX = Mathf.Max(maxX, path[i].x);
                minY = Mathf.Min(minY, path[i].y); maxY = Mathf.Max(maxY, path[i].y);
            }
            _bounds.Add(Rect.MinMaxRect(minX, minY, maxX, maxY));
        }

        static Vector2[] Wind(Vector2 a, Vector2 b, uint salt)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1f) return new[] { a, b };
            Vector2 dir = d / len;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            int n = Mathf.Max(3, Mathf.RoundToInt(len / SampleStep) + 1);
            var pts = new Vector2[n];

            // A deterministic phase per road so the S-bends of parallel roads do not line up.
            float phase = DetHash.Float01(DetHash.Mix(salt)) * Mathf.PI * 2f;
            float k = Mathf.PI * 2f / SnakeWave;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Vector2 p = a + d * t;

                // The hard S-bends: a strong serpentine along the run of the road ...
                float snake = Mathf.Sin(len * t * k + phase) * SnakeAmp;
                // ... plus smooth noise so the bends are not perfectly regular.
                float big = (ProcNoise.Fbm(p.x * 0.0032f, p.y * 0.0032f, salt, 3) * 2f - 1f) * Wander;
                float fine = (ProcNoise.Fbm(p.x * 0.0125f + 7.3f, p.y * 0.0125f - 3.1f, salt + 101u, 2) * 2f - 1f) * Detail;

                p += perp * ((snake + big + fine) * Fade(p.x, p.y));
                p.x = Mathf.Clamp(p.x, 8f, MapSize - 8f);
                p.y = Mathf.Clamp(p.y, 8f, MapSize - 8f);
                pts[i] = p;
            }
            pts[0] = a;                    // keep the exact gate / edge connections
            pts[n - 1] = b;
            return pts;
        }

        // 0 inside the town and at the map border (roads stay straight there), 1 in the open.
        static float Fade(float x, float z)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(x - TownCenter.x) - (FortHX + 10f));
            float dz = Mathf.Max(0f, Mathf.Abs(z - TownCenter.y) - (FortHZ + 10f));
            float townFade = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dz * dz) - 15f) / 200f);

            float edge = Mathf.Min(Mathf.Min(x, z), Mathf.Min(MapSize - x, MapSize - z));
            float edgeFade = Mathf.Clamp01((edge - 20f) / 70f);

            float f = Mathf.Min(townFade, edgeFade);
            return f * f * (3f - 2f * f);
        }

        static float DistToSeg(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            float t = l2 > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
            return Vector2.Distance(a + ab * t, p);
        }
    }
}
