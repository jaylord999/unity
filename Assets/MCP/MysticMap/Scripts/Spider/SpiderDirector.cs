using System.Collections.Generic;
using UnityEngine;
using MysticMap.World;

namespace MysticMap
{
    /// <summary>
    /// Puts the fantasy spiders into the endless world, a couple at a time.
    ///
    /// Nothing is stored in the scene: while the player walks around, this spawns a small number
    /// of spiders on random spots near them (on ground that is really rendered), and removes any
    /// that the player has left behind. They therefore behave exactly like the streamed chunks and
    /// roadside life - they exist only while they are close enough to matter, and they are always
    /// somewhere new.
    ///
    /// It is deliberately small:
    ///   * at most <see cref="maxAlive"/> spiders at once (2 by default),
    ///   * a new one every <see cref="spawnInterval"/> seconds (plus a short delay at the start),
    ///   * placed in a ring between <see cref="minSpawnDistance"/> and <see cref="maxSpawnDistance"/>
    ///     metres from the player, so one never pops into view right on top of you,
    ///   * removed past <see cref="despawnDistance"/> metres (or, of course, when the player kills
    ///     it - the Health on the prefab destroys it),
    ///   * never inside the walled fortress town when <see cref="avoidTown"/> is on.
    ///
    /// The spiders themselves (hostility, bites, ranged magic) live in <see cref="SpiderEnemy"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MysticMap/Spider Director (streams 2 spiders near the player)")]
    public class SpiderDirector : MonoBehaviour
    {
        [Header("What it spawns")]
        [Tooltip("Spider prefabs to pick from (the prefab builder creates one).")]
        public GameObject[] spiders = new GameObject[0];

        [Header("Who it follows")]
        [Tooltip("The player these spiders are streamed around (auto-filled with the slime).")]
        public Transform target;
        [Tooltip("Look for the SlimePlayer automatically.")]
        public bool autoFindPlayer = true;

        [Header("How many")]
        [Tooltip("Most spiders alive at the same time.")]
        [Range(1, 12)] public int maxAlive = 2;
        [Tooltip("Seconds between two spawns.")]
        public float spawnInterval = 9f;
        [Tooltip("Seconds before the very first spider appears (lets the world finish streaming).")]
        public float firstSpawnDelay = 4f;

        [Header("Where")]
        [Tooltip("Closest (m) a spider may spawn to the player.")]
        public float minSpawnDistance = 22f;
        [Tooltip("Farthest (m) a spider may spawn from the player.")]
        public float maxSpawnDistance = 48f;
        [Tooltip("A spider further than this (m) from the player is removed again.")]
        public float despawnDistance = 95f;
        [Tooltip("Keep at least this much space (m) between two spiders.")]
        public float minSeparation = 7f;
        [Tooltip("Try this many random spots before giving up for this cycle.")]
        [Range(1, 40)] public int placementAttempts = 16;
        [Tooltip("Keep spiders out of the walled fortress town (nobody wants one in their market square).")]
        public bool avoidTown = true;
        [Tooltip("Also only spawn where the ground is really rendered (the chunk is streamed in).")]
        public bool requireRenderedGround = true;

        [Header("Look")]
        [Tooltip("Random scale range applied to each spider.")]
        public Vector2 scaleRange = new Vector2(0.9f, 1.25f);
        [Tooltip("Effect played where a spider appears (optional).")]
        public GameObject spawnEffect;

        [Header("Debug")]
        [Tooltip("Log every spawn and despawn.")]
        public bool logSpawns = false;

        // The walled fortress of the hand-built map (same numbers MysticMapBuilder / RoadsideLife
        // use: the town sits at 500,520 and its walls are 57 m / 47 m from the centre).
        const float TownX = 500f, TownZ = 520f;
        const float FortHalfX = 57f, FortHalfZ = 47f;
        const float SpawnClearance = 12f;

        readonly List<SpiderEnemy> _alive = new List<SpiderEnemy>(8);
        Transform _root;
        ChunkManager _chunks;
        float _timer;

        /// <summary>How many spiders the director currently keeps alive.</summary>
        public int AliveCount
        {
            get
            {
                Prune();
                return _alive.Count;
            }
        }

        // =====================================================================
        //  Lifecycle
        // =====================================================================
        void Awake()
        {
            _root = transform.Find("_spiders");
            if (_root == null)
            {
                var go = new GameObject("_spiders");
                go.transform.SetParent(transform, false);
                _root = go.transform;
            }

            _timer = Mathf.Max(0.1f, firstSpawnDelay);
        }

        void OnDisable() => ClearAll();

        void Update()
        {
            ResolveTarget();
            if (target == null) return;

            Prune();
            DespawnFar();

            if (_timer > 0f)
            {
                _timer -= Time.deltaTime;
                return;
            }

            if (_alive.Count >= Mathf.Max(1, maxAlive))
            {
                _timer = 0.5f;                 // full house: look again shortly
                return;
            }

            _timer = SpawnOne() ? Mathf.Max(0.5f, spawnInterval) : 1.5f;
        }

        void ResolveTarget()
        {
            if (target != null || !autoFindPlayer) return;

            var players = FindObjectsByType<SlimePlayer>();
            if (players != null && players.Length > 0) target = players[0].transform;
        }

        // =====================================================================
        //  Spawning
        // =====================================================================
        /// <summary>Tries to place one spider on a random spot around the player.</summary>
        public bool SpawnOne()
        {
            if (spiders == null || spiders.Length == 0 || target == null) return false;

            for (int attempt = 0; attempt < Mathf.Max(1, placementAttempts); attempt++)
            {
                if (!PickSpot(out Vector3 spot)) continue;
                if (TooCloseToAnother(spot)) continue;

                GameObject prefab = spiders[Random.Range(0, spiders.Length)];
                if (prefab == null) continue;

                GameObject go = Instantiate(prefab, spot,
                                            Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), _root);
                if (go == null) continue;
                go.name = prefab.name;

                float scale = Random.Range(Mathf.Min(scaleRange.x, scaleRange.y),
                                           Mathf.Max(scaleRange.x, scaleRange.y));
                if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;

                var enemy = go.GetComponent<SpiderEnemy>();
                if (enemy == null) enemy = go.AddComponent<SpiderEnemy>();
                enemy.Activate(spot, target, aggro: true);

                _alive.Add(enemy);

                if (spawnEffect != null)
                    MagicEffects.DestroyLater(MagicEffects.Spawn(spawnEffect, spot, Quaternion.identity), 0f);

                if (logSpawns)
                    Debug.Log("[MysticMap.Spiders] spawned '" + prefab.name + "' at " + spot.ToString("0") +
                              " (" + _alive.Count + "/" + maxAlive + " alive, " +
                              Vector3.Distance(spot, target.position).ToString("0") + " m away).");

                return true;
            }

            return false;
        }

        /// <summary>A random point on the ring around the player, snapped onto rendered ground.</summary>
        bool PickSpot(out Vector3 spot)
        {
            spot = Vector3.zero;
            if (target == null) return false;

            float angle = Random.value * Mathf.PI * 2f;
            float radius = Random.Range(Mathf.Min(minSpawnDistance, maxSpawnDistance),
                                        Mathf.Max(minSpawnDistance, maxSpawnDistance));

            var flat = new Vector3(target.position.x + Mathf.Cos(angle) * radius, 0f,
                                   target.position.z + Mathf.Sin(angle) * radius);

            if (avoidTown && InsideFortress(flat.x, flat.z)) return false;

            float ground = GroundHeight(flat.x, flat.z, out bool fromMesh);
            if (requireRenderedGround && !fromMesh) return false;

            spot = new Vector3(flat.x, ground, flat.z);
            return true;
        }

        bool TooCloseToAnother(Vector3 spot)
        {
            Prune();

            float min = Mathf.Max(0.5f, minSeparation);
            for (int i = 0; i < _alive.Count; i++)
            {
                if (_alive[i] == null) continue;
                if (Vector3.Distance(_alive[i].transform.position, spot) < min) return true;
            }

            return false;
        }

        // =====================================================================
        //  Removing
        // =====================================================================
        void DespawnFar()
        {
            if (target == null) return;

            float limit = Mathf.Max(despawnDistance, maxSpawnDistance + 5f);
            limit *= limit;

            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                SpiderEnemy enemy = _alive[i];
                if (enemy == null) { _alive.RemoveAt(i); continue; }

                if (FlatSqr(enemy.transform.position - target.position) <= limit) continue;

                if (logSpawns) Debug.Log("[MysticMap.Spiders] despawned '" + enemy.name + "' (player left).");

                Destroy(enemy.gameObject);
                _alive.RemoveAt(i);
            }
        }

        void Prune()
        {
            for (int i = _alive.Count - 1; i >= 0; i--)
                if (_alive[i] == null) _alive.RemoveAt(i);
        }

        /// <summary>Removes every spider this director spawned.</summary>
        public void ClearAll()
        {
            for (int i = 0; i < _alive.Count; i++)
                if (_alive[i] != null) Destroy(_alive[i].gameObject);

            _alive.Clear();

            if (_root != null)
            {
                for (int i = _root.childCount - 1; i >= 0; i--)
                    Destroy(_root.GetChild(i).gameObject);
            }
        }

        // =====================================================================
        //  World queries
        // =====================================================================
        /// <summary>
        /// Ground height at a spot, plus whether something is really rendered there.
        /// The height itself always comes from the world field (the same value the chunk meshes
        /// are built from), so a spider is never placed on top of a tree; the ray only answers
        /// "is the ground streamed in yet".
        /// </summary>
        float GroundHeight(float x, float z, out bool fromMesh)
        {
            fromMesh = false;

            if (_chunks == null) _chunks = FindFirstObjectByType<ChunkManager>();

            float guess = _chunks != null ? _chunks.SampleHeight(x, z) : 30f;

            if (Physics.Raycast(new Vector3(x, guess + 60f, z), Vector3.down, out RaycastHit hit, 140f,
                                ~0, QueryTriggerInteraction.Ignore))
                fromMesh = true;

            return guess;
        }

        /// <summary>
        /// True inside the walled fortress town of the hand-built map. The centre and the half
        /// extents match MysticMapBuilder (the same numbers RoadsideLife uses to keep its flowers
        /// out of the town).
        /// </summary>
        static bool InsideFortress(float x, float z)
        {
            float dx = Mathf.Abs(x - TownX);
            float dz = Mathf.Abs(z - TownZ);
            return dx < FortHalfX + SpawnClearance && dz < FortHalfZ + SpawnClearance;
        }

        static float FlatSqr(Vector3 v) { v.y = 0f; return v.sqrMagnitude; }

        void OnDrawGizmosSelected()
        {
            Vector3 p = target != null ? target.position : transform.position;

            Gizmos.color = new Color(1f, 0.35f, 0.65f, 0.5f);
            Gizmos.DrawWireSphere(p, minSpawnDistance);

            Gizmos.color = new Color(1f, 0.15f, 0.4f, 0.25f);
            Gizmos.DrawWireSphere(p, maxSpawnDistance);

            Gizmos.color = new Color(0.6f, 0.1f, 0.1f, 0.18f);
            Gizmos.DrawWireSphere(p, despawnDistance);
        }
    }
}
