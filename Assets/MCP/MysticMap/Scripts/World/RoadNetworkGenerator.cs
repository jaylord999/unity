using System.Collections.Generic;
using UnityEngine;

namespace MysticMap.World
{
    /// <summary>Identifies one lattice edge. Shared by both chunks that touch it.</summary>
    public struct RoadEdgeKey : System.IEquatable<RoadEdgeKey>
    {
        public readonly int i, j;
        public readonly bool horizontal;

        public RoadEdgeKey(int i, int j, bool horizontal)
        {
            this.i = i; this.j = j; this.horizontal = horizontal;
        }

        public bool Equals(RoadEdgeKey o) => i == o.i && j == o.j && horizontal == o.horizontal;

        public override bool Equals(object o) => o is RoadEdgeKey k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = i * 73856093 ^ j * 19349663 ^ (horizontal ? 0x51ED : 0x270B);
                return h;
            }
        }
    }

    /// <summary>One road segment between two lattice nodes, with its sampled centre line.</summary>
    public sealed class RoadEdge
    {
        public RoadEdgeKey key;
        public Vector2 a, b;              // lattice end points (world XZ)
        public Vector3[] points;          // sampled centre line (y unused = 0)
        public Vector3[] normals;         // per point, side direction (XZ) for placing props
        public Bounds bounds;
        public float halfWidth;

        public float Length => points == null || points.Length < 2
            ? 0f
            : Vector3.Distance(points[0], points[points.Length - 1]);
    }

    /// <summary>
    /// Builds and queries the procedural road network.
    ///
    /// The network is a deterministic graph on the chunk lattice: lattice nodes sit on
    /// multiples of <c>chunkSize</c>, and an edge between two nodes either exists or not
    /// depending only on (seed, edge). Because both chunks that share an edge evaluate the
    /// exact same canonical edge, roads cross chunk borders as one continuous curve, and
    /// no chunk ever has to talk to another one.
    ///
    /// The curve between two nodes is a cubic Bezier whose end tangents are the node
    /// tangents; a node tangent is derived from the (shared) set of open edges at that
    /// node plus a deterministic wander, so several roads meeting at a node form one
    /// smooth continuation instead of a kink.
    /// </summary>
    public class RoadNetworkGenerator
    {
        readonly WorldSettings _s;
        readonly ILegacyMap _town;
        readonly float _size;

        readonly Dictionary<RoadEdgeKey, RoadEdge> _cache = new Dictionary<RoadEdgeKey, RoadEdge>();
        readonly Dictionary<RoadEdgeKey, float> _openCache = new Dictionary<RoadEdgeKey, float>();
        readonly Dictionary<long, Vector2> _tangentCache = new Dictionary<long, Vector2>();

        HashSet<RoadEdgeKey> _forcedOpen;
        Dictionary<long, Vector2> _forcedDir;

        const int Segments = 10;          // samples per edge
        const int ForcedChainNodes = 5;   // how many nodes the town approach stays straight

        public RoadNetworkGenerator(WorldSettings settings, ILegacyMap town)
        {
            _s = settings;
            _town = town;
            _size = Mathf.Max(10f, settings.chunkSize);
        }


        // =====================================================================
        //  Lattice helpers
        // =====================================================================
        public float ChunkSize => _size;

        int NodeI(float x) => Mathf.RoundToInt(x / _size);
        int NodeJ(float z) => Mathf.RoundToInt(z / _size);

        public Vector2 NodePos(int i, int j) => new Vector2(i * _size, j * _size);

        static long NodeKey(int i, int j) => ((long)i << 32) ^ (uint)j;

        public static readonly Vector2 TownCenter = new Vector2(500f, 520f);

        uint Seed => (uint)_s.seed;

        // =====================================================================
        //  Forced connections (town approaches)
        // =====================================================================
        void EnsureForced()
        {
            if (_forcedOpen != null) return;

            _forcedOpen = new HashSet<RoadEdgeKey>();
            _forcedDir = new Dictionary<long, Vector2>();

            if (_town == null) return;
            _town.EnsureReady();
            if (!_town.HasLegacy) return;

            var exits = _town.AllExits();
            for (int e = 0; e < exits.Length; e++)
            {
                Vector2 exit = exits[e];
                Vector2 n = _town.OutsideNormal(exit);

                int i = NodeI(exit.x);
                int j = NodeJ(exit.y);

                // Does the lattice actually pass through this exit? If not the road would
                // meet the hand-built map slightly off centre, so let the user know.
                float off = Vector2.Distance(NodePos(i, j), exit);
                if (off > 0.5f && _town.LogAlignment)
                    Debug.LogWarning("[MysticMap] Road exit " + exit.ToString("0") +
                                     " is " + off.ToString("0.0") + " m off the chunk grid (" +
                                     _size.ToString("0") + " m). Move the road or change the chunk " +
                                     "size so the connection is exact.", _town as Object);

                int di = Mathf.Abs(n.x) > Mathf.Abs(n.y) ? (int)Mathf.Sign(n.x) : 0;
                int dj = di == 0 ? (int)Mathf.Sign(n.y) : 0;
                if (di == 0 && dj == 0) dj = 1;

                int ci = i, cj = j;
                var axis = new Vector2(di, dj);
                for (int k = 0; k < ForcedChainNodes; k++)
                {
                    // Every node of the approach keeps the exact axis tangent, so the whole
                    // corridor out of the town gate is one straight, predictable road.
                    _forcedDir[NodeKey(ci, cj)] = axis;

                    int ni = ci + di, nj = cj + dj;
                    var key = new RoadEdgeKey(Mathf.Min(ci, ni), Mathf.Min(cj, nj), di != 0);
                    if (!ClosedByLegacy(key)) _forcedOpen.Add(key);

                    ci = ni; cj = nj;
                    _forcedDir[NodeKey(ci, cj)] = axis;
                }
            }
        }

        /// <summary>True when the node is part of a forced (dead-straight) town approach.</summary>
        bool ForcedNode(int i, int j)
        {
            EnsureForced();
            return _forcedDir != null && _forcedDir.ContainsKey(NodeKey(i, j));
        }

        // =====================================================================
        //  Edge existence
        // =====================================================================
        /// <summary>
        /// True when the edge sits inside the hand-built map. Those edges are skipped
        /// because the town's own terrain and roads own that ground.
        /// </summary>
        bool ClosedByLegacy(RoadEdgeKey key)
        {
            if (_town == null || !_town.HasLegacy) return false;

            Vector2 ma = EdgeMidpoint(key);
            return _town.OutsideDistance(ma.x, ma.y) <= 0.5f;
        }

        Vector2 EdgeMidpoint(RoadEdgeKey key)
        {
            Vector2 a = NodePos(key.i, key.j);
            Vector2 b = key.horizontal ? NodePos(key.i + 1, key.j) : NodePos(key.i, key.j + 1);
            return (a + b) * 0.5f;
        }

        /// <summary>Is this lattice edge a road? Deterministic for the whole world.</summary>
        public bool EdgeOpen(RoadEdgeKey key)
        {
            if (_openCache.TryGetValue(key, out float cached))
                return cached > 0.5f;

            EnsureForced();

            bool open;
            if (_forcedOpen != null && _forcedOpen.Contains(key)) open = true;
            else if (ClosedByLegacy(key)) open = false;
            else
            {
                Vector2 mid = EdgeMidpoint(key);
                float dTown = Vector2.Distance(mid, TownCenter);
                // Near the town most edges exist (a settled road network); far away it thins
                // out into a windier web, but it always stays above the percolation
                // threshold of the square lattice so the network never falls apart.
                float p = Mathf.Lerp(0.88f, 0.60f,
                                     ProcNoise.Smoothstep(_s.roadStraightRange, _s.roadWindRange, dTown));
                uint h = DetHash.Hash(key.i, key.j, key.horizontal ? 0x1234 : 0x4321, Seed);
                open = DetHash.Chance(h, p);
            }

            _openCache[key] = open ? 1f : 0f;
            return open;
        }

        public bool EdgeOpen(int i, int j, bool horizontal) => EdgeOpen(new RoadEdgeKey(i, j, horizontal));

        // =====================================================================
        //  Geometry
        // =====================================================================
        /// <summary>
        /// Direction a road leaves a node in. It is the circular average of the open edges
        /// at that node (so all roads meeting there join smoothly) plus a deterministic
        /// wander that grows with the distance from the town - the approach stays straight
        /// and predictable, the far country winds.
        /// </summary>
        public Vector2 NodeTangent(int i, int j)
        {
            long key = NodeKey(i, j);
            if (_tangentCache.TryGetValue(key, out Vector2 cached)) return cached;

            EnsureForced();

            // A town approach is exactly axis aligned: the gate roads never bend until the
            // forced corridor ends, whatever other roads happen to meet them.
            if (_forcedDir != null && _forcedDir.TryGetValue(key, out Vector2 forced))
            {
                _tangentCache[key] = forced;
                return forced;
            }

            float sx = 0f, sy = 0f;
            int count = 0;
            AccumulateNodeDir(i, j, true, 1, ref sx, ref sy, ref count);
            AccumulateNodeDir(i, j, true, -1, ref sx, ref sy, ref count);
            AccumulateNodeDir(i, j, false, 1, ref sx, ref sy, ref count);
            AccumulateNodeDir(i, j, false, -1, ref sx, ref sy, ref count);

            // A node whose only open edges are opposite (a road running straight through) averages
            // to zero, and a node with a single axis open (a T-junction crossbar) would line its
            // tangent up with the edge - both make the Bezier dead straight. Use the axis for the
            // single-axis case so the tangent is at least consistent (the sideways bow below is
            // what actually curves the road).
            bool hOpen = EdgeOpen(new RoadEdgeKey(i - 1, j, true)) ||
                         EdgeOpen(new RoadEdgeKey(i, j, true));
            bool vOpen = EdgeOpen(new RoadEdgeKey(i, j - 1, false)) ||
                         EdgeOpen(new RoadEdgeKey(i, j, false));

            Vector2 dir;
            if (sx * sx + sy * sy > 0.0001f) dir = new Vector2(sx, sy).normalized;
            else if (hOpen && !vOpen) dir = Vector2.right;
            else if (vOpen && !hOpen) dir = Vector2.up;
            else
            {
                // Crossing / lone node: pick a heading from a SMOOTH spatial field so roads
                // bend gently instead of snapping to an axis, and neighbouring nodes agree.
                float a = (ProcNoise.Fbm(i * 0.08f, j * 0.08f, Seed + 0x0D1Au, 3) * 2f - 1f) * 1.0f;
                dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }

            // Far from the town the heading wanders. The offset comes from a SMOOTH spatial
            // field (not a per-node random value), so neighbouring nodes turn together and the
            // road sweeps through long, unpredictable curves instead of zig-zagging every edge.
            // The offset is clamped to one so "Max wander" really is the turn limit in degrees.
            float dTown = Vector2.Distance(NodePos(i, j), TownCenter);
            float wander = ProcNoise.Smoothstep(_s.roadStraightRange, _s.roadWindRange, dTown)
                           * _s.roadMaxTurnDegrees * Mathf.Deg2Rad;
            float sweep = ProcNoise.Fbm(i * 0.08f, j * 0.08f, Seed + 0x0A11u, 3) * 2f - 1f;
            float jitter = DetHash.Signed(DetHash.Hash(i, j, 0xA11E, Seed)) * 0.15f;
            dir = Rotate(dir, Mathf.Clamp(sweep + jitter, -1f, 1f) * wander);

            _tangentCache[key] = dir;
            return dir;
        }

        void AccumulateNodeDir(int i, int j, bool horizontal, int sign,
                               ref float sx, ref float sy, ref int count)
        {
            int ei = i, ej = j;
            if (sign < 0)
            {
                if (horizontal) ei = i - 1; else ej = j - 1;
            }

            if (!EdgeOpen(new RoadEdgeKey(ei, ej, horizontal))) return;

            sx += horizontal ? sign : 0f;
            sy += horizontal ? 0f : sign;
            count++;
        }

        static Vector2 Rotate(Vector2 v, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>The full, sampled centre line of one edge (built once per edge).</summary>
        public RoadEdge GetEdge(RoadEdgeKey key)
        {
            if (_cache.TryGetValue(key, out RoadEdge cached)) return cached;

            Vector2 a = NodePos(key.i, key.j);
            Vector2 b = key.horizontal ? NodePos(key.i + 1, key.j) : NodePos(key.i, key.j + 1);
            Vector2 ta = NodeTangent(key.i, key.j);
            Vector2 tb = key.horizontal ? NodeTangent(key.i + 1, key.j) : NodeTangent(key.i, key.j + 1);

            uint h1 = DetHash.Hash(key.i, key.j, key.horizontal ? 0xB0B0 : 0xC0C0, Seed);
            uint h2 = DetHash.Mix(h1 ^ 0x5BD1E995u);

            float len = _size;
            float k1 = 0.30f + 0.20f * DetHash.Float01(h1);
            float k2 = 0.30f + 0.20f * DetHash.Float01(h2);

            Vector2 c1 = a + Vector2.ClampMagnitude(ta * (len * k1), 0.62f * len);
            Vector2 c2 = b + Vector2.ClampMagnitude(-tb * (len * k2), 0.62f * len);

            float distFactor = ProcNoise.Smoothstep(_s.roadStraightRange, _s.roadWindRange,
                                                    Vector2.Distance((a + b) * 0.5f, TownCenter));

            // Guaranteed sideways bow. When an edge's node tangents line up with the edge, the
            // control points sit on the straight line and the Bezier is mathematically straight -
            // that is why the roads still looked straight. Offsetting BOTH control points along
            // the edge's side bows the curve by 0.75 * offset at its middle (the ends stay put),
            // and the offset comes from a smooth field so neighbouring edges arc together into
            // long sweeping curves. Forced gate approaches and the area right around the town are
            // left straight so the roads still line up with the town.
            Vector2 ab = b - a;
            float abLen = ab.magnitude;
            bool gateApproach = ForcedNode(key.i, key.j) ||
                                ForcedNode(key.horizontal ? key.i + 1 : key.i,
                                           key.horizontal ? key.j : key.j + 1);
            if (abLen > 0.0001f && distFactor > 0.001f && !gateApproach)
            {
                Vector2 side = new Vector2(-ab.y, ab.x) / abLen;
                float midX = (a.x + b.x) * 0.5f, midZ = (a.y + b.y) * 0.5f;
                float sweep = ProcNoise.Fbm(midX * 0.0016f, midZ * 0.0016f, Seed + 0x5EEDu, 3) * 2f - 1f;
                Vector2 bow = side * (sweep * _s.roadCurveStrength * len * distFactor);
                c1 += bow;
                c2 += bow;
            }

            float half = Mathf.Lerp(_s.roadHalfWidthNear, _s.roadHalfWidthFar,
                                    Mathf.Max(distFactor, DetHash.Float01(DetHash.Mix(h1 ^ 0x7777u)) * 0.5f));

            var pts = new Vector3[Segments + 1];
            var nrm = new Vector3[Segments + 1];
            Vector3 min = new Vector3(float.MaxValue, 0f, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, 0f, float.MinValue);

            for (int s = 0; s <= Segments; s++)
            {
                float t = s / (float)Segments;
                Vector2 p = Bezier(a, c1, c2, b, t);
                Vector2 d = BezierTangent(a, c1, c2, b, t);
                if (d.sqrMagnitude < 0.000001f) d = (b - a).normalized;
                else d = d.normalized;

                pts[s] = new Vector3(p.x, 0f, p.y);
                nrm[s] = new Vector3(-d.y, 0f, d.x);

                min = Vector3.Min(min, pts[s]);
                max = Vector3.Max(max, pts[s]);
            }

            var bnds = new Bounds();
            bnds.SetMinMax(min - new Vector3(half, 0f, half), max + new Vector3(half, 0f, half));

            var edge = new RoadEdge
            {
                key = key,
                a = a,
                b = b,
                points = pts,
                normals = nrm,
                bounds = bnds,
                halfWidth = half,
            };
            _cache[key] = edge;
            return edge;
        }

        static Vector2 Bezier(Vector2 a, Vector2 c1, Vector2 c2, Vector2 b, float t)
        {
            float u = 1f - t;
            float uu = u * u, tt = t * t;
            return uu * u * a + 3f * uu * t * c1 + 3f * u * tt * c2 + tt * t * b;
        }

        static Vector2 BezierTangent(Vector2 a, Vector2 c1, Vector2 c2, Vector2 b, float t)
        {
            float u = 1f - t;
            return 3f * u * u * (c1 - a) + 6f * u * t * (c2 - c1) + 3f * t * t * (b - c2);
        }

        /// <summary>
        /// Every road edge that can possibly reach the given rectangle. Built once per chunk
        /// so the terrain mesh, the vegetation pass and the road-side props all reuse it.
        /// </summary>
        public RoadField BuildField(float x0, float z0, float x1, float z1, float pad)
        {
            var field = new RoadField();
            float reach = _size + pad;

            int i0 = Mathf.FloorToInt((x0 - reach) / _size) - 1;
            int i1 = Mathf.CeilToInt((x1 + reach) / _size) + 1;
            int j0 = Mathf.FloorToInt((z0 - reach) / _size) - 1;
            int j1 = Mathf.CeilToInt((z1 + reach) / _size) + 1;

            var rect = new Bounds();
            rect.SetMinMax(new Vector3(x0 - pad, -1f, z0 - pad), new Vector3(x1 + pad, 1f, z1 + pad));

            for (int i = i0; i <= i1; i++)
            {
                for (int j = j0; j <= j1; j++)
                {
                    var h = new RoadEdgeKey(i, j, true);
                    if (EdgeOpen(h))
                    {
                        var e = GetEdge(h);
                        if (e.bounds.Intersects(rect) || rect.Contains(e.bounds.center)) field.Edges.Add(e);
                    }

                    var v = new RoadEdgeKey(i, j, false);
                    if (EdgeOpen(v))
                    {
                        var e = GetEdge(v);
                        if (e.bounds.Intersects(rect) || rect.Contains(e.bounds.center)) field.Edges.Add(e);
                    }
                }
            }

            field.Prepare();
            return field;
        }

        /// <summary>Distance from a point to the nearest road centre line (metres; large when none).</summary>
        public float DistanceToRoad(float x, float z, out float halfWidth)
        {
            var field = BuildField(x, z, x, z, _s.roadMaxApron);
            RoadHit hit = field.Query(x, z);
            halfWidth = hit.halfWidth;
            return hit.distance;
        }

        /// <summary>
        /// Height the roads are graded to. It varies very slowly so the roads keep a
        /// walkable grade no matter what the surrounding biome does.
        /// </summary>
        public float RoadElevation(float x, float z)
        {
            float big = ProcNoise.Fbm(x * 0.0016f, z * 0.0016f, Seed + 991u, 3) - 0.5f;
            float detail = ProcNoise.Fbm(x * 0.013f, z * 0.013f, Seed + 997u, 2) - 0.5f;
            float h = _s.baseHeight + 4f + big * 2f * _s.roadRelief + detail * 1.4f;
            return Mathf.Clamp(h, 6f, _s.maxHeight - 20f);
        }
    }
}
