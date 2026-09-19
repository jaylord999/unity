using System.Collections.Generic;
using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// Growing ground clutter - rocks, stones, mushrooms, bushes - that is NOT stored in the
    /// scene, exactly like the runtime grass and trees: it streams in a ring around the player
    /// and disappears again when it leaves the range. Every decision (does a prop exist here?
    /// which prefab? size? angle?) comes from a hash of the world cell, so the same spot always
    /// grows the same prop.
    ///
    /// The map's winding roads are kept clear (MysticMap.MapRoads). Like the grass it may also
    /// grow inside the town - untick "keepTownClear" to stop that.
    ///
    /// Add it from the menu:  MCP > Ambient > Add runtime rocks, stones & mushrooms.
    /// </summary>
    public class RuntimeScatter : MonoBehaviour
    {
        [Tooltip("Who to follow (usually the main camera / player).")]
        public Transform target;
        [Tooltip("How far around the player props can grow (m).")]
        public float radius = 48f;
        [Tooltip("Distance between candidate grow-spots (m).")]
        public float spacing = 6.5f;
        [Tooltip("Chance a grow-spot actually has a prop.")]
        [Range(0.02f, 1f)] public float density = 0.5f;
        public float minScale = 0.7f;
        public float maxScale = 1.45f;
        public int seed = 4242;

        [Tooltip("Only keep props in FRONT of the target (cheaper); close ones always stay.")]
        public bool useViewCone = false;
        [Range(90f, 170f)] public float viewHalfAngle = 120f;
        public float keepCloseRadius = 14f;

        [Header("Keep clear of roads & the walled town")]
        public bool keepRoadsClear = true;
        [Tooltip("Roads are kept bare this far out from the road centreline (m).")]
        public float roadClear = 5.5f;
        [Tooltip("Keep props out of the walled town. Unticked = they grow inside it too (like the grass).")]
        public bool keepTownClear = false;
        [Tooltip("Extra belt around the town kept clear (m).")]
        public float townBelt = 0f;

        [Header("Town rectangle (world XZ)")]
        public float townX = 500f;
        public float townZ = 520f;
        public float townHalfX = 57f;
        public float townHalfZ = 47f;

        [Header("Terrain")]
        [Tooltip("Steepest ground props may grow on (degrees).")]
        public float maxSlope = 48f;
        public float groundOffset = 0.02f;
        public float playerClearRadius = 3f;
        public Terrain terrain;

        [Tooltip("Prop prefabs to grow (rocks, stones, mushrooms, bushes...).")]
        public GameObject[] prefabs;

        readonly Dictionary<long, GameObject> _live = new Dictionary<long, GameObject>();
        Vector3 _last;

        void Start()
        {
            if (terrain == null) terrain = Terrain.activeTerrain;
            if (target == null)
            {
                Camera c = Camera.main;
                if (c != null) target = c.transform;
            }
            _last = (target != null) ? target.position : transform.position;
            Refresh();
        }

        void Update()
        {
            if (target == null) return;
            Vector3 p = target.position;
            Vector3 d = p - _last;
            float step = Mathf.Max(0.5f, spacing * 0.5f);
            if (d.x * d.x + d.z * d.z <= step * step) return;
            _last = p;
            Refresh();
        }

        void Refresh()
        {
            if (prefabs == null || prefabs.Length == 0) return;

            Vector3 tp = (target != null) ? target.position : _last;
            Vector3 fwd = (target != null) ? target.forward : transform.forward;
            float cell = Mathf.Max(1f, spacing);

            int cx = Mathf.RoundToInt(tp.x / cell);
            int cz = Mathf.RoundToInt(tp.z / cell);
            int reach = Mathf.CeilToInt((radius + cell) / cell);

            float keepSq = keepCloseRadius * keepCloseRadius;
            float cosHalf = Mathf.Cos(viewHalfAngle * Mathf.Deg2Rad);
            float playerSq = playerClearRadius * playerClearRadius;

            var seen = new HashSet<long>();

            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int gx = cx + dx, gz = cz + dz;
                    long key = Key(gx, gz);

                    uint rnd = Hash(gx, gz, seed);
                    float x = gx * cell + (((rnd >> 8) & 0xFF) / 255f - 0.5f) * cell * 0.7f;
                    float z = gz * cell + (((rnd >> 16) & 0xFF) / 255f - 0.5f) * cell * 0.7f;

                    float ox = x - tp.x, oz = z - tp.z;
                    if (ox * ox + oz * oz > radius * radius) continue;      // too far
                    if (ox * ox + oz * oz < playerSq) continue;             // on the player

                    // Keep the winding map roads bare.
                    if (keepRoadsClear && MapRoads.Distance(x, z) < roadClear) continue;
                    if (keepTownClear &&
                        Mathf.Abs(x - townX) < townHalfX + townBelt &&
                        Mathf.Abs(z - townZ) < townHalfZ + townBelt) continue;

                    float presence = (rnd & 0xFFFF) / 65535f;
                    if (presence > density) { Release(key); continue; }     // empty spot

                    if (useViewCone)
                    {
                        float distSq = ox * ox + oz * oz;
                        if (distSq > keepSq)
                        {
                            float len = Mathf.Sqrt(distSq);
                            if (len > 0.0001f && (ox * fwd.x + oz * fwd.z) / len < cosHalf)
                            {
                                Release(key);
                                continue;                                   // behind the target
                            }
                        }
                    }

                    if (!SlopeOk(x, z)) { Release(key); continue; }

                    seen.Add(key);

                    float y = 0f;
                    if (terrain != null) y = terrain.SampleHeight(new Vector3(x, 0f, z)) + groundOffset;

                    if (!_live.TryGetValue(key, out GameObject go) || go == null)
                    {
                        GameObject pf = prefabs[(int)((rnd >> 24) % (uint)prefabs.Length)];
                        if (pf == null) { Release(key); continue; }
                        go = Instantiate(pf, transform);
                        _live[key] = go;
                    }

                    float yaw = ((rnd >> 4) & 0xFF) / 255f * 360f;
                    float s = minScale + ((rnd >> 20) & 0xFF) / 255f * (maxScale - minScale);
                    go.transform.position = new Vector3(x, y, z);
                    go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    go.transform.localScale = Vector3.one * s;
                }
            }

            // Drop anything that left the active set.
            if (_live.Count > seen.Count)
            {
                var drop = new List<long>();
                foreach (var kv in _live)
                    if (!seen.Contains(kv.Key)) drop.Add(kv.Key);
                for (int i = 0; i < drop.Count; i++) Release(drop[i]);
            }
        }

        void OnDisable()
        {
            var keys = new List<long>(_live.Keys);
            for (int i = 0; i < keys.Count; i++) Release(keys[i]);
        }

        // Props avoid cliffs (slope sampled from the terrain).
        bool SlopeOk(float x, float z)
        {
            if (terrain == null) return true;
            const float d = 2f;
            float hL = terrain.SampleHeight(new Vector3(x - d, 0f, z));
            float hR = terrain.SampleHeight(new Vector3(x + d, 0f, z));
            float hD = terrain.SampleHeight(new Vector3(x, 0f, z - d));
            float hU = terrain.SampleHeight(new Vector3(x, 0f, z + d));
            float rise = Mathf.Max(Mathf.Abs(hR - hL), Mathf.Abs(hU - hD));
            return Mathf.Atan2(rise, d * 2f) * Mathf.Rad2Deg <= maxSlope;
        }

        void Release(long key)
        {
            if (_live.TryGetValue(key, out GameObject go))
            {
                if (go != null) Destroy(go);
                _live.Remove(key);
            }
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        static uint Hash(int x, int z, int seed)
        {
            uint h = (uint)(x * 374761393 + z * 668265263 + seed * 974634211);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }
}
