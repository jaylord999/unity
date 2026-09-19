using System.Collections.Generic;
using UnityEngine;

namespace MysticMap.World
{
    /// <summary>Result of asking the road network where the nearest road is.</summary>
    public struct RoadHit
    {
        public float distance;      // to the road centre line (large when there is none)
        public float halfWidth;     // half width of that road
        public Vector3 closest;     // closest point on the centre line
        public Vector3 side;        // unit side direction there (points to one verge)
        public Vector3 forward;     // unit direction the road runs in
        public RoadEdge edge;       // null when no road was found

        public bool valid => edge != null;

        public static RoadHit None => new RoadHit
        {
            distance = 1e9f,
            halfWidth = 0f,
            closest = Vector3.zero,
            side = Vector3.right,
            forward = Vector3.forward,
            edge = null,
        };
    }

    /// <summary>
    /// The roads that can reach one region (usually a chunk). Built once per chunk from
    /// <see cref="RoadNetworkGenerator.BuildField"/> and then reused for every terrain
    /// vertex, every vegetation candidate and every road-side prop, which keeps the
    /// streaming cost predictable.
    /// </summary>
    public sealed class RoadField
    {
        public readonly List<RoadEdge> Edges = new List<RoadEdge>();

        public int Count => Edges.Count;

        public bool IsEmpty => Edges.Count == 0;

        /// <summary>Reserved so a spatial index could be added later without touching callers.</summary>
        public void Prepare()
        {
            // Edges are already bounds-filtered by the builder; nothing to do for now.
        }

        public bool TryQuery(float x, float z, float maxDistance, out RoadHit hit)
        {
            hit = Query(x, z);
            return hit.valid && hit.distance <= maxDistance;
        }

        public RoadHit Query(float x, float z)
        {
            var best = RoadHit.None;

            if (Edges.Count == 0) return best;

            var p = new Vector2(x, z);
            for (int e = 0; e < Edges.Count; e++)
            {
                RoadEdge edge = Edges[e];
                if (edge.points == null || edge.points.Length < 2) continue;

                // Bounds early-out: the centre line can never be closer than this.
                if (edge.bounds.SqrDistance(new Vector3(x, 0f, z)) > best.distance * best.distance) continue;

                Vector3[] pts = edge.points;
                for (int s = 0; s < pts.Length - 1; s++)
                {
                    Vector2 a = new Vector2(pts[s].x, pts[s].z);
                    Vector2 b = new Vector2(pts[s + 1].x, pts[s + 1].z);

                    float d = DistToSegment(a, b, p, out float t);
                    if (d >= best.distance) continue;

                    best.distance = d;
                    best.halfWidth = edge.halfWidth;
                    best.edge = edge;

                    Vector3 pa = pts[s], pb = pts[s + 1];
                    Vector3 na = edge.normals[s], nb = edge.normals[s + 1];

                    best.closest = Vector3.Lerp(pa, pb, t);
                    best.side = Vector3.Slerp(na, nb, t).normalized;
                    if (best.side.sqrMagnitude < 0.5f) best.side = Vector3.right;
                    Vector3 fwd = pb - pa;
                    best.forward = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
                }
            }

            return best;
        }

        static float DistToSegment(Vector2 a, Vector2 b, Vector2 p, out float t)
        {
            Vector2 ab = b - a;
            float len = ab.sqrMagnitude;
            t = len > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len) : 0f;
            return Vector2.Distance(a + ab * t, p);
        }
    }
}
