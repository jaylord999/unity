using System.Collections.Generic;
using UnityEngine;

namespace MysticMap.World
{
    /// <summary>
    /// Streams the procedural world around the player.
    ///
    /// Every frame (throttled) it works out which chunk the player is in, keeps a list of
    /// the chunks that should exist, and creates a small, fixed number of them per frame so
    /// the frame time never spikes. Chunks that fall far enough behind are released - mesh,
    /// collider and all props - which keeps the scene at a constant size no matter how far
    /// you walk.
    ///
    /// The hand-built town is never touched: chunks that overlap it are simply skipped, and
    /// the procedural ground/roads blend into its edges.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MysticMap/World/Chunk Manager")]
    public class ChunkManager : MonoBehaviour
    {
        [Tooltip("Every tunable of the generated world.")]
        public WorldSettings settings = new WorldSettings();

        [Tooltip("Describes the hand-built town map. Auto-created on this object when empty.")]
        public TownAnchor townAnchor;

        [Tooltip("Who the world is streamed around (usually the player). Auto-filled in play mode.")]
        public Transform target;

        [Tooltip("Look for the player / main camera automatically.")]
        public bool autoFindPlayer = true;

        [Tooltip("Chunks are generated and released while playing.")]
        public bool streamInPlayMode = true;

        [Tooltip("Also stream while the editor is not in Play mode (used by the preview tool).")]
        public bool buildInEditor = false;

        [Tooltip("Release chunks when the component is disabled.")]
        public bool releaseOnDisable = true;

        [Tooltip("Draw the streaming area and the frozen town rectangle in the scene view.")]
        public bool drawGizmos = true;

        [Tooltip("Log how many chunks are live every so often.")]
        public bool logStats = false;

        readonly Dictionary<Vector2Int, WorldChunk> _live = new Dictionary<Vector2Int, WorldChunk>();
        readonly List<Vector2Int> _queue = new List<Vector2Int>();
        readonly List<Vector2Int> _stale = new List<Vector2Int>();
        readonly List<float> _queueCost = new List<float>();

        WorldGenerator _generator;
        Transform _root;
        Vector2Int _center = new Vector2Int(int.MinValue, int.MinValue);
        bool _built;
        float _logTimer;
        int _legacySkipped;

        public WorldGenerator Generator => _generator;
        public int LiveChunks => _live.Count;
        public int QueuedChunks => _queue.Count;
        public int LegacyChunksSkipped => _legacySkipped;
        public Vector2Int CenterChunk => _center;

        // =====================================================================
        //  Lifecycle
        // =====================================================================
        void Awake() => EnsureBuilt();

        void OnEnable()
        {
            EnsureBuilt();
            if (Application.isPlaying || buildInEditor) Refresh(true);
        }

        void Start()
        {
            EnsureBuilt();
            ResolveTarget();
            Refresh(true);
            ProcessQueue(Mathf.Max(1, settings.chunksPerFrame) * 2);
        }

        void OnDisable()
        {
            if (releaseOnDisable && Application.isPlaying) ClearAll();
        }

        void Update()
        {
            if (!streamInPlayMode) return;
            if (!Application.isPlaying && !buildInEditor) return;

            EnsureBuilt();
            ResolveTarget();

            Refresh(false);
            ProcessQueue(settings.chunksPerFrame);

            if (logStats)
            {
                _logTimer -= Time.deltaTime;
                if (_logTimer <= 0f)
                {
                    _logTimer = 5f;
                    Debug.Log("[MysticMap.World] chunks=" + _live.Count + " queued=" + _queue.Count +
                              " center=" + _center.x + "," + _center.y + " skipped(town)=" + _legacySkipped);
                }
            }
        }

        /// <summary>Creates the generator (safe to call from the editor tools too).</summary>
        public void EnsureBuilt()
        {
            if (_built && _generator != null) return;

            if (settings == null) settings = new WorldSettings();
            settings.Validate();

            if (townAnchor == null) townAnchor = GetComponent<TownAnchor>();
            if (townAnchor == null) townAnchor = gameObject.AddComponent<TownAnchor>();
            townAnchor.EnsureReady();

            _generator = new WorldGenerator(settings, townAnchor);

            _built = true;
        }

        /// <summary>
        /// Container for the streamed chunks. Created on demand so loading the scene in the
        /// editor never adds an empty object to it.
        /// </summary>
        Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var go = new GameObject("Chunks");
                    go.transform.SetParent(transform, false);
                    if (!Application.isPlaying) go.hideFlags = HideFlags.DontSave;
                    _root = go.transform;
                }
                return _root;
            }
        }

        void ResolveTarget()
        {
            if (target != null || !autoFindPlayer) return;

            var player = FindObjectsByType<SlimePlayer>();
            if (player != null && player.Length > 0)
            {
                target = player[0].transform;
                return;
            }

            var cam = Camera.main;
            if (cam != null) target = cam.transform;
        }

        // =====================================================================
        //  Streaming
        // =====================================================================
        /// <summary>Recomputes what should exist around the target.</summary>
        public void Refresh(bool force)
        {
            if (_generator == null) return;

            if (target == null)
            {
                ProcessQueue(settings.chunksPerFrame);
                return;
            }

            Vector2Int center = _generator.ChunkOf(target.position);
            if (force || center != _center)
            {
                _center = center;
                RefreshQueue();
                UpdateLiveChunks();
            }

            ProcessQueue(settings.chunksPerFrame);
        }

        /// <summary>Builds the "to do" list, nearest chunk first.</summary>
        void RefreshQueue()
        {
            _queue.Clear();
            _legacySkipped = 0;
            _queueCost.Clear();

            int r = settings.viewRadiusChunks;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist > r + 0.35f) continue;

                    var coord = new Vector2Int(_center.x + dx, _center.y + dz);
                    if (_live.ContainsKey(coord)) continue;
                    if (_generator.IsLegacy(coord)) { _legacySkipped++; continue; }

                    _queue.Add(coord);
                    _queueCost.Add(dist);
                }
            }

            // Nearest first: the world fills in from the player outwards.
            for (int i = 1; i < _queue.Count; i++)
            {
                Vector2Int coord = _queue[i];
                float cost = _queueCost[i];
                int j = i - 1;
                while (j >= 0 && _queueCost[j] > cost)
                {
                    _queue[j + 1] = _queue[j];
                    _queueCost[j + 1] = _queueCost[j];
                    j--;
                }
                _queue[j + 1] = coord;
                _queueCost[j + 1] = cost;
            }
        }

        /// <summary>Creates up to <paramref name="budget"/> chunks from the queue.</summary>
        public void ProcessQueue(int budget)
        {
            if (_generator == null) return;

            int done = 0;
            while (done < budget && _queue.Count > 0)
            {
                Vector2Int coord = _queue[0];
                _queue.RemoveAt(0);
                if (_live.ContainsKey(coord)) continue;
                if (_generator.IsLegacy(coord)) continue;

                CreateChunk(coord);
                done++;
            }

            if (done > 0) ReleaseFarChunks();
        }

        WorldChunk CreateChunk(Vector2Int coord)
        {
            float dist = ChunkDistance(coord, _center);
            bool highLod = dist <= settings.lodDistanceChunks;
            bool detail = dist <= settings.detailRadiusChunks;
            float density = 1f;

            WorldChunk chunk = _generator.CreateChunk(Root, coord, highLod, detail, density);
            if (chunk == null) return null;

            if (!Application.isPlaying) chunk.gameObject.hideFlags = HideFlags.DontSave;

            _live[coord] = chunk;
            return chunk;
        }

        /// <summary>Keeps the LOD and the prop detail of existing chunks in sync with the player.</summary>
        void UpdateLiveChunks()
        {
            _stale.Clear();
            foreach (var pair in _live) _stale.Add(pair.Key);

            for (int i = 0; i < _stale.Count; i++)
            {
                if (!_live.TryGetValue(_stale[i], out WorldChunk chunk) || chunk == null) continue;

                float dist = ChunkDistance(_stale[i], _center);
                bool highLod = dist <= settings.lodDistanceChunks;
                bool detail = dist <= settings.detailRadiusChunks;

                if (chunk.highLod != highLod)
                {
                    RoadField field = _generator.BuildTerrain(chunk, highLod);
                    if (detail) _generator.Populate(chunk, field, 1f);
                }
                else if (detail && chunk.PropCount == 0)
                {
                    _generator.Populate(chunk, BuildField(chunk), 1f);
                }
                else if (!detail && chunk.PropCount > 0)
                {
                    chunk.ClearProps();
                }
            }
        }

        RoadField BuildField(WorldChunk chunk)
        {
            float s = settings.chunkSize;
            return _generator.Terrain.Roads.BuildField(
                chunk.coord.x * s, chunk.coord.y * s,
                (chunk.coord.x + 1) * s, (chunk.coord.y + 1) * s, settings.roadMaxApron);
        }

        /// <summary>Destroys every chunk that is not wanted any more.</summary>
        public void ReleaseFarChunks()
        {
            _stale.Clear();
            int limit = settings.viewRadiusChunks + settings.unloadMarginChunks + 1;

            foreach (var pair in _live)
                if (ChunkDistance(pair.Key, _center) > limit) _stale.Add(pair.Key);

            for (int i = 0; i < _stale.Count; i++) DestroyChunk(_stale[i]);
        }

        void DestroyChunk(Vector2Int coord)
        {
            if (!_live.TryGetValue(coord, out WorldChunk chunk)) return;
            _live.Remove(coord);
            if (chunk == null) return;

            chunk.Release();
            WorldSpawn.Destroy(chunk.gameObject);
        }

        /// <summary>Removes every streamed chunk (the hand-built town stays exactly as it is).</summary>
        public void ClearAll()
        {
            _stale.Clear();
            foreach (var pair in _live) _stale.Add(pair.Key);
            for (int i = 0; i < _stale.Count; i++) DestroyChunk(_stale[i]);
            _queue.Clear();
            _center = new Vector2Int(int.MinValue, int.MinValue);
        }

        // =====================================================================
        //  Queries used by the player and the tools
        // =====================================================================
        /// <summary>Ground height anywhere in the world (town, live chunk or procedural field).</summary>
        public float SampleHeight(float x, float z)
        {
            EnsureBuilt();
            var coord = _generator.ChunkOf(new Vector3(x, 0f, z));
            if (_live.TryGetValue(coord, out WorldChunk chunk) && chunk != null)
                return _generator.SampleHeight(x, z, chunk);
            return _generator.SampleHeight(x, z, null);
        }

        /// <summary>Puts a transform on the ground (used when the world is created at runtime).</summary>
        public void PlaceOnGround(Transform t, float extraHeight = 0.2f)
        {
            if (t == null) return;
            EnsureBuilt();

            float h = SampleHeight(t.position.x, t.position.z) + extraHeight;
            var player = t.GetComponent<SlimePlayer>();
            if (player != null) player.Teleport(new Vector3(t.position.x, h, t.position.z));
            else t.position = new Vector3(t.position.x, h, t.position.z);
        }

        /// <summary>Distance between two chunk coordinates, in chunks.</summary>
        static float ChunkDistance(Vector2Int a, Vector2Int b)
        {
            float dx = a.x - b.x, dz = a.y - b.y;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // =====================================================================
        //  Editor preview / tools
        // =====================================================================
        /// <summary>
        /// Builds the world around a position immediately (used by the editor preview so the
        /// world can be inspected without pressing Play). Safe to call from a menu item.
        /// </summary>
        public void BuildAround(Vector3 position, bool detail)
        {
            EnsureBuilt();
            ClearAll();

            _center = _generator.ChunkOf(position);
            int r = settings.viewRadiusChunks;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Mathf.Sqrt(dx * dx + dz * dz) > r + 0.35f) continue;

                    var coord = new Vector2Int(_center.x + dx, _center.y + dz);
                    if (_generator.IsLegacy(coord)) { _legacySkipped++; continue; }

                    float dist = ChunkDistance(coord, _center);
                    bool highLod = dist <= settings.lodDistanceChunks;
                    bool pop = detail && dist <= settings.detailRadiusChunks;
                    CreateChunkAt(coord, highLod, pop);
                }
            }
        }

        WorldChunk CreateChunkAt(Vector2Int coord, bool highLod, bool detail)
        {
            WorldChunk chunk = _generator.CreateChunk(Root, coord, highLod, detail, 1f);
            if (chunk == null) return null;
            if (!Application.isPlaying) chunk.gameObject.hideFlags = HideFlags.DontSave;
            _live[coord] = chunk;
            return chunk;
        }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos || settings == null) return;

            float s = Mathf.Max(10f, settings.chunkSize);
            int r = settings.viewRadiusChunks;

            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.35f);
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Mathf.Sqrt(dx * dx + dz * dz) > r + 0.35f) continue;
                    var center = new Vector3((_center.x + dx + 0.5f) * s, 0f, (_center.y + dz + 0.5f) * s);
                    center.y = SampleHeightSafe(center.x, center.z);
                    Gizmos.DrawWireCube(center, new Vector3(s, 1f, s));
                }
            }

            var town = townAnchor != null ? townAnchor : GetComponent<TownAnchor>();
            if (town != null)
            {
                town.EnsureReady();
                Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
                Vector2 o = town.Origin, size = town.Size;
                for (float x = o.x; x <= o.x + size.x + 0.1f; x += s)
                    Gizmos.DrawLine(new Vector3(x, 45f, o.y), new Vector3(x, 45f, o.y + size.y));
                for (float z = o.y; z <= o.y + size.y + 0.1f; z += s)
                    Gizmos.DrawLine(new Vector3(o.x, 45f, z), new Vector3(o.x + size.x, 45f, z));
            }
        }

        float SampleHeightSafe(float x, float z)
        {
            if (_generator == null) return 45f;
            return Mathf.Clamp(SampleHeight(x, z), 1f, settings.maxHeight);
        }
    }
}
