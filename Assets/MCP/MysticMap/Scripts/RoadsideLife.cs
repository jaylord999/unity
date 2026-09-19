using System.Collections.Generic;
using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// Adds life along the road network without storing anything: while the player is near a
    /// road it streams in a dense band of small flowers and rocks on the roadside, plus
    /// occasional clusters of parked carts / piled sacks. Everything despawns as you leave
    /// and nothing is saved in the scene - positions are generated on the fly around the player.
    /// </summary>
    public class RoadsideLife : MonoBehaviour
    {
        [Header("Tracking")]
        public Transform target;
        [Range(20f, 150f)] public float radius = 78f;
        [Range(0.1f, 1f)] public float checkInterval = 0.35f;

        [Header("Roadside flowers (thick band right at the road edge)")]
        public bool flowersOn = true;
        [Range(0f, 1f)] public float flowerDensity = 0.7f;
        public float flowerBand = 9f;
        [Range(0.5f, 1.6f)] public float flowerScale = 1f;
        public GameObject[] flowerPrefabs = new GameObject[0];

        [Header("Road verge (dense small grass + flowers right at the edge, like a border)")]
        public bool vergeOn = true;
        [Range(0f, 1f)] public float vergeDensity = 0.9f;
        [Range(0.2f, 2f)] public float vergeSize = 0.5f;
        [Tooltip("Extra distance past the road edge where the verge starts (m).")]
        [Range(0f, 4f)] public float vergeOffset = 0.3f;
        [Tooltip("Width of the verge band (m).")]
        [Range(1f, 6f)] public float vergeWidth = 3.2f;
        public GameObject[] vergePrefabs = new GameObject[0];

        [Header("Small roadside rocks (sparser, slightly further out)")]
        public bool rocksOn = true;
        [Range(0f, 1f)] public float rockDensity = 0.5f;
        public float rockBand = 13f;
        public GameObject[] rockPrefabs = new GameObject[0];

        [Header("Cargo clusters (carts / sacks piling by the road)")]
        public bool clustersOn = true;
        [Range(0f, 1f)] public float clusterDensity = 0.6f;
        public GameObject[] clusterPrefabs = new GameObject[0];

        [Header("Walled town")]
        [Tooltip("Let the roadside grass verge, flowers and small rocks grow inside the town too. " +
                 "The bulky cargo clusters (carts / sacks) always stay clear of the houses and square.")]
        public bool allowGrassInTown = true;

        const float FlowerMax = 260f;
        const float RockMax = 55f;
        const float ClusterMax = 12f;
        const float VergeMax = 340f;
        const float RoadEdge = 4f;                 // approx road half-width (m)
        const float FortX = 57f, FortZ = 47f;      // fortress footprint (half extents)
        static readonly Vector2 Town = new Vector2(500f, 520f);

        List<Vector2[]> _roads;
        float _despawnDist;
        float _timer;
        bool _built;

        class Deco { public GameObject go; public Vector3 pos; }
        readonly List<Deco> _flowers = new List<Deco>();
        readonly List<Deco> _verge = new List<Deco>();
        readonly List<Deco> _rocks = new List<Deco>();
        readonly List<Deco> _clusters = new List<Deco>();
        readonly List<GameObject> _poolFlower = new List<GameObject>();
        readonly List<GameObject> _poolVerge = new List<GameObject>();
        readonly List<GameObject> _poolRock = new List<GameObject>();
        readonly List<GameObject> _poolCluster = new List<GameObject>();

        void BuildRoads()
        {
            if (_built) return;
            _built = true;
            _roads = MapRoads.Segments();   // the map's winding roads (shared with the bake)
            _despawnDist = radius + 14f;
        }

        void OnEnable()
        {
            BuildRoads();
            if (target == null) ResolveTarget();

            // Let the slime's magic tear up the roadside flowers / rocks / carts as well.
            MagicEnvironment.Register(transform, MagicEnvMode.Hide, radius + 16f);
        }

        void Update()
        {
            if (target == null) { ResolveTarget(); if (target == null) return; }

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = checkInterval;

            Vector3 c = target.position;
            float rsq = _despawnDist * _despawnDist;

            Cull(_flowers, _poolFlower, c, rsq);
            Cull(_verge, _poolVerge, c, rsq);
            Cull(_rocks, _poolRock, c, rsq);
            Cull(_clusters, _poolCluster, c, rsq);

            Manage(_flowers, _poolFlower, flowerPrefabs, flowersOn, FlowerMax * flowerDensity,
                   5f, flowerBand, flowerScale, c, false);
            Manage(_verge, _poolVerge, vergePrefabs, vergeOn, VergeMax * vergeDensity,
                   RoadEdge + vergeOffset, RoadEdge + vergeOffset + vergeWidth, vergeSize, c, false);
            Manage(_rocks, _poolRock, rockPrefabs, rocksOn, RockMax * rockDensity,
                   6f, rockBand, 0.3f, c, false);
            Manage(_clusters, _poolCluster, clusterPrefabs, clustersOn, ClusterMax * clusterDensity,
                   5.5f, 11f, 1f, c, true);
        }
        void ResolveTarget()
        {
            Camera cam = Camera.main;
            if (cam != null) target = cam.transform;
            else
            {
                var go = GameObject.Find("Main Camera");
                if (go != null) target = go.transform;
            }
        }

        // Keeps a category filled to its target, spawning new roadside items as needed and
        // trimming any excess.
        void Manage(List<Deco> act, List<GameObject> pool, GameObject[] pre, bool on,
                    float targetF, float bandMin, float bandMax, float scale,
                    Vector3 c, bool cluster)
        {
            if (!on || pre.Length == 0) { RecycleAll(act, pool); return; }

            int target = Mathf.Clamp(Mathf.RoundToInt(targetF), 0, 600);

            int need = target - act.Count;
            if (need > 0)
            {
                int tries = 0;
                int limit = target * 6 + 40;
                while (need > 0 && tries++ < limit)
                {
                    if (TryRoadPoint(out Vector3 p, bandMin, bandMax, c, cluster))
                    {
                        if (Spawn(act, pool, pre, p, scale, cluster)) need--;
                    }
                }
            }

            // Trim anything above the target so lowering the amount actually thins it out.
            while (act.Count > target)
                RecycleAt(act, pool, act.Count - 1);
        }

        bool TryRoadPoint(out Vector3 p, float bandMin, float bandMax, Vector3 c, bool cluster)
        {
            for (int a = 0; a < 30; a++)
            {
                var seg = _roads[Random.Range(0, _roads.Count)];
                Vector2 A = seg[0], B = seg[1];
                Vector2 d = B - A;
                float len = d.magnitude;
                if (len < 1f) continue;
                Vector2 dir = d / len;
                Vector2 perp = new Vector2(-dir.y, dir.x);

                Vector2 cc = new Vector2(c.x, c.z);
                float along = Mathf.Clamp(Vector2.Dot(cc - A, dir), 0f, len);
                float lo = Mathf.Max(0f, along - radius);
                float hi = Mathf.Min(len, along + radius);
                if (hi - lo < 0.01f) continue;

                float t = Random.Range(lo, hi);
                float off = (Random.value < 0.5f ? -1f : 1f) * Random.Range(bandMin, bandMax);
                Vector2 p2 = A + dir * t + perp * off;

                // The grass verge, flowers and small rocks may grow inside the walled town now,
                // but the bulky cargo clusters (carts / sacks) must stay clear of the houses
                // and the square, so only those keep the town exclusion.
                if (cluster || !allowGrassInTown)
                {
                    float dx = Mathf.Abs(p2.x - Town.x), dz = Mathf.Abs(p2.y - Town.y);
                    if (dx < FortX + 2f && dz < FortZ + 2f) continue;
                }

                float y = SampleGround(p2.x, p2.y, c.y);
                p = new Vector3(p2.x, y, p2.y);
                return true;
            }
            p = Vector3.zero;
            return false;
        }

        bool Spawn(List<Deco> act, List<GameObject> pool, GameObject[] pre, Vector3 p,
                   float scale, bool cluster)
        {
            GameObject go = pool.Count > 0 ? pool[0] : null;
            if (go == null)
            {
                var prefab = pre[Random.Range(0, pre.Length)];
                go = Instantiate(prefab, transform);
                if (go == null) return false;
            }
            else { pool.RemoveAt(0); go.SetActive(true); }

            go.transform.position = p;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            float s = cluster ? Random.Range(0.9f, 1.15f) : scale * Random.Range(0.7f, 1.3f);
            go.transform.localScale = new Vector3(s, s, s);
            act.Add(new Deco { go = go, pos = p });
            return true;
        }

        void Cull(List<Deco> act, List<GameObject> pool, Vector3 c, float rsq)
        {
            for (int i = act.Count - 1; i >= 0; i--)
            {
                float dx = act[i].pos.x - c.x, dz = act[i].pos.z - c.z;
                if (dx * dx + dz * dz > rsq) RecycleAt(act, pool, i);
            }
        }

        void RecycleAll(List<Deco> act, List<GameObject> pool)
        {
            while (act.Count > 0) RecycleAt(act, pool, act.Count - 1);
        }

        void RecycleAt(List<Deco> act, List<GameObject> pool, int i)
        {
            Deco d = act[i];
            act.RemoveAt(i);
            if (d.go != null) { d.go.SetActive(false); pool.Add(d.go); }
        }

        static float SampleGround(float x, float z, float fallbackY)
        {
            var t = Terrain.activeTerrain;
            if (t != null) return t.SampleHeight(new Vector3(x, 0f, z));
            if (Physics.Raycast(new Vector3(x, 200f, z), Vector3.down, out RaycastHit hit, 400f))
                return hit.point.y;
            return fallbackY;
        }

        void OnDisable()
        {
            MagicEnvironment.Unregister(transform);

            for (int i = 0; i < transform.childCount; i++)
            {
                var ch = transform.GetChild(i);
                if (ch != null) ch.gameObject.SetActive(false);
            }
            _flowers.Clear(); _verge.Clear(); _rocks.Clear(); _clusters.Clear();
            _poolFlower.Clear(); _poolVerge.Clear(); _poolRock.Clear(); _poolCluster.Clear();
        }
    }
}
