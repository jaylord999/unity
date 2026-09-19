using System.Collections.Generic;
using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// Pure-ambience glowing flowers that are NOT stored in the scene. They "grow" in a ring
    /// around the player as they walk and vanish behind/far away. Every decision (does a flower
    /// exist here? which prefab? size? angle?) comes from a hash of the world cell, so the same
    /// spot always produces the same flowers - but nothing is saved, keeping the scene tiny.
    ///
    /// Add it from the editor menu:  MCP > Glow > Add runtime glow flowers (near player).
    /// Tune Radius / Density / Spacing on the component to control how much grows.
    /// </summary>
    public class RuntimeGlowFlowers : MonoBehaviour
    {
        [Tooltip("Who to follow (usually the main camera / player).")]
        public Transform target;

        [Tooltip("How far around the player flowers can grow (m).")]
        public float radius = 30f;

        [Tooltip("Distance between candidate grow-spots (m). Bigger = fewer, more spread out.")]
        public float spacing = 5f;

        [Tooltip("Chance a grow-spot actually has a flower (0.05 = sparse, 1 = every spot).")]
        [Range(0.02f, 1f)] public float density = 0.3f;

        [Tooltip("Random size range of the flowers (multiplies the prefab scale).")]
        public float minScale = 0.6f;
        public float maxScale = 1.4f;

        [Tooltip("Seed changes where flowers appear (keeps them consistent while it stays the same).")]
        public int seed = 12345;

        [Tooltip("Only keep flowers in FRONT of the target (cheaper); close ones always stay.")]
        public bool useViewCone = true;

        [Range(90f, 170f)] public float viewHalfAngle = 115f;
        public float keepCloseRadius = 8f;

        [Header("No-grow zone (e.g. the village)")]
        public float centerX = 500f;
        public float centerZ = 520f;
        public float noZoneRadius = 45f;

        [Header("Keep clear of roads & the walled town")]
        public bool keepRoadsClear = true;
        [Tooltip("Roads are kept bare this far out from the road centreline (m).")]
        public float roadClear = 6f;
        public bool keepTownClear = true;

        [Tooltip("Let grass/flowers grow inside the walled town too. When on, the 'no-grow zone' " +
                 "and the walled-town keep-clear band above are ignored so the town floor grows " +
                 "grass as well. Roads are still kept bare.")]
        public bool growInsideTown = true;

        public float groundOffset = 0.05f;

        public Terrain terrain;

        [Tooltip("Plain grass prefabs to grow (non-glowing). Assign the original Grass prefabs.")]
        public GameObject[] grassPrefabs;

        [Tooltip("Glowing flower/low-plant prefabs to grow (assign the *_Glow prefabs).")]
        public GameObject[] flowerPrefabs;

        [Tooltip("Chance a spot is a GLOWING plant instead of a plain one. Higher = more glows. 0 = none glow, 1 = everything glows.")]
        [Range(0f, 1f)] public float glowChance = 0.2f;

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
            if ((grassPrefabs == null || grassPrefabs.Length == 0) &&
                (flowerPrefabs == null || flowerPrefabs.Length == 0)) return;

            Vector3 tp = (target != null) ? target.position : _last;
            Vector3 fwd = (target != null) ? target.forward : transform.forward;
            float cell = Mathf.Max(1f, spacing);

            int cx = Mathf.RoundToInt(tp.x / cell);
            int cz = Mathf.RoundToInt(tp.z / cell);
            int reach = Mathf.CeilToInt((radius + cell) / cell);

            float keepSq = keepCloseRadius * keepCloseRadius;
            float cosHalf = Mathf.Cos(viewHalfAngle * Mathf.Deg2Rad);

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

                    float nx = x - centerX, nz = z - centerZ;
                    // The village no-grow zone only applies while the town must stay bare.
                    if (!growInsideTown && nx * nx + nz * nz < noZoneRadius * noZoneRadius) continue;

                    // Keep roads bare: no flowers/grass growing on or right beside a road.
                    if (keepRoadsClear && RoadDist(x, z) < roadClear) continue;
                    // Keep the walled town clear too (unless grass is allowed inside it).
                    if (!growInsideTown && keepTownClear && Mathf.Abs(nx) < 59f && Mathf.Abs(nz) < 49f) continue;

                    float presence = (rnd & 0xFFFF) / 65535f;
                    if (presence > density) { Release(key); continue; }      // empty spot

                    if (useViewCone)
                    {
                        float distSq = ox * ox + oz * oz;
                        if (distSq > keepSq)
                        {
                            float len = Mathf.Sqrt(distSq);
                            if (len > 0.0001f && (ox * fwd.x + oz * fwd.z) / len < cosHalf)
                            {
                                Release(key);
                                continue;                                    // behind the target
                            }
                        }
                    }

                    seen.Add(key);

                    float y = 0f;
                    if (terrain != null) y = terrain.SampleHeight(new Vector3(x, 0f, z)) + groundOffset;

                    var pf = PickPrefab(rnd);
                    if (pf == null) { Release(key); continue; }

                    if (!_live.TryGetValue(key, out GameObject go) || go == null)
                    {
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

            // Destroy anything that left our active set (moved past it or turned away).
            if (_live.Count > seen.Count)
            {
                var drop = new List<long>();
                foreach (var kv in _live)
                    if (!seen.Contains(kv.Key)) drop.Add(kv.Key);
                for (int i = 0; i < drop.Count; i++) Release(drop[i]);
            }
        }

        // The map's winding roads (shared with the terrain bake) so nothing grows on them.
        float RoadDist(float x, float z) => MapRoads.Distance(x, z);

        GameObject PickPrefab(uint rnd)
        {
            bool wantGlow = ((rnd & 0xFFFF) / 65535f) < glowChance;
            var arr = (wantGlow && flowerPrefabs != null && flowerPrefabs.Length > 0)
                ? flowerPrefabs
                : (grassPrefabs != null && grassPrefabs.Length > 0 ? grassPrefabs : flowerPrefabs);
            if (arr == null || arr.Length == 0) return null;
            int idx = (int)((rnd >> 24) % (uint)arr.Length);
            return arr[idx];
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
