using System.Collections.Generic;
using UnityEngine;

namespace MysticMap.World
{
    /// <summary>
    /// Places the hand-made-feeling landmarks of the endless world: ruined sites built from
    /// the town pack's stone walls, stone labyrinths in the labyrinth biome, and the odd
    /// cart or stone marker beside a road. Everything is generated from (seed, chunk) so a
    /// ruin is always exactly where it was, and it disappears with its chunk.
    /// </summary>
    public class EnvironmentGenerator
    {
        readonly WorldSettings _s;
        readonly TerrainGenerator _terrain;

        const int MaxObjectsPerChunk = 90;

        public EnvironmentGenerator(WorldSettings settings, TerrainGenerator terrain)
        {
            _s = settings;
            _terrain = terrain;
        }

        public void Populate(WorldChunk chunk, RoadField field, float densityScale)
        {
            if (!_s.generateEnvironment || densityScale <= 0.01f || chunk.Data == null) return;

            WorldPrefabPalette p = _s.palette;
            if (p == null || p.IsEmpty) return;

            float size = _s.chunkSize;
            float cx = chunk.coord.x * size + size * 0.5f;
            float cz = chunk.coord.y * size + size * 0.5f;
            BiomeWeights w = _terrain.Biomes.Weights(cx, cz);

            uint salt = (uint)_s.seed * 40503u + 7u;
            var rng = new DetRandom(chunk.coord.x, chunk.coord.y, salt);

            // ---- ruined site --------------------------------------------------
            if (w.ruins > 0.30f && rng.Next01() < _s.ruinChance)
                BuildRuin(chunk, field, ref rng, p, cx, cz);

            // ---- stone labyrinth ----------------------------------------------
            if (w.labyrinth > 0.34f && rng.Next01() < _s.mazeChance)
                BuildMaze(chunk, field, ref rng, p, cx, cz);

            // ---- road-side landmarks -------------------------------------------
            if (field != null && !field.IsEmpty)
                RoadSideDetails(chunk, field, ref rng, p);

            // ---- a stone circle on lonely rocky ground --------------------------
            if (w.rocky > 0.55f && rng.Next01() < 0.16f)
                StoneCircle(chunk, field, ref rng, p, cx, cz);
        }

        // =====================================================================
        //  Landmarks
        // =====================================================================
        /// <summary>A broken rectangle of stone walls with rubble inside.</summary>
        void BuildRuin(WorldChunk chunk, RoadField field, ref DetRandom rng, WorldPrefabPalette p,
                       float cx, float cz)
        {
            GameObject wall = First(p.walls, p.stones, p.rocks);
            if (wall == null) return;

            float size = _s.chunkSize;
            float cell = 6.5f;
            int w = rng.Range(3, 5);
            int d = rng.Range(3, 5);

            // Centre the ruin in the chunk, then pull it inside a safe margin.
            float ox = Mathf.Clamp(cx - w * cell * 0.5f, chunk.coord.x * size + 16f,
                                   chunk.coord.x * size + size - 16f - w * cell);
            float oz = Mathf.Clamp(cz - d * cell * 0.5f, chunk.coord.y * size + 16f,
                                   chunk.coord.y * size + size - 16f - d * cell);

            int placed = 0;

            for (int i = 0; i < w; i++)
            {
                for (int j = 0; j < d; j++)
                {
                    bool edge = i == 0 || j == 0 || i == w - 1 || j == d - 1;
                    float keep = rng.Next01();
                    float jitter = rng.NextSigned() * 4f;
                    if (!edge || keep > 0.72f) continue;      // plenty of gaps: it is a ruin

                    var world = new Vector3(ox + i * cell, 0f, oz + j * cell);
                    world.y = _terrain.Height(world.x, world.z, field);
                    if (SkipSpot(chunk, field, world, p)) continue;

                    float rot = ((i + j) % 2 == 0 ? 0f : 90f) + jitter;
                    if (WorldSpawn.Spawn(wall, chunk.PropsRoot, world, rot, 0.75f) != null)
                    {
                        chunk.AddProp();
                        if (++placed >= 26) return;
                    }
                }
            }
        }

        /// <summary>A stone labyrinth built as a perfect maze on a 5 m grid.</summary>
        void BuildMaze(WorldChunk chunk, RoadField field, ref DetRandom rng, WorldPrefabPalette p,
                       float cx, float cz)
        {
            GameObject pillar = First(p.rocks, p.stones);
            if (pillar == null) return;

            float size = _s.chunkSize;
            int n = Mathf.Clamp(_s.mazeCells, 5, 17);
            const float cell = 5f;
            float extent = n * cell;

            float ox = Mathf.Clamp(cx - extent * 0.5f, chunk.coord.x * size + 8f,
                                   chunk.coord.x * size + size - 8f - extent);
            float oz = Mathf.Clamp(cz - extent * 0.5f, chunk.coord.y * size + 8f,
                                   chunk.coord.y * size + size - 8f - extent);

            var wall = new bool[n + 1, n + 1];
            var visited = new bool[n, n];

            // The border always stands.
            for (int i = 0; i <= n; i++)
            {
                wall[i, 0] = true; wall[i, n] = true;
                wall[0, i] = true; wall[n, i] = true;
            }

            // Carve the maze with a randomised depth-first walk.
            var stack = new Stack<Vector2Int>();
            int sx = rng.Range(0, n), sy = rng.Range(0, n);
            visited[sx, sy] = true;
            stack.Push(new Vector2Int(sx, sy));

            var options = new List<Vector2Int>(4);
            while (stack.Count > 0)
            {
                Vector2Int c = stack.Peek();
                options.Clear();
                if (c.x > 0 && !visited[c.x - 1, c.y]) options.Add(new Vector2Int(-1, 0));
                if (c.x < n - 1 && !visited[c.x + 1, c.y]) options.Add(new Vector2Int(1, 0));
                if (c.y > 0 && !visited[c.x, c.y - 1]) options.Add(new Vector2Int(0, -1));
                if (c.y < n - 1 && !visited[c.x, c.y + 1]) options.Add(new Vector2Int(0, 1));

                if (options.Count == 0) { stack.Pop(); continue; }

                Vector2Int dir = options[rng.Range(0, options.Count)];
                int nx = c.x + dir.x, ny = c.y + dir.y;

                // The pillar between the two cells comes down.
                if (dir.x != 0) wall[Mathf.Max(c.x, nx), c.y] = false;
                else wall[c.x, Mathf.Max(c.y, ny)] = false;

                visited[nx, ny] = true;
                stack.Push(new Vector2Int(nx, ny));
            }

            int placed = 0;
            for (int i = 0; i <= n; i++)
            {
                for (int j = 0; j <= n; j++)
                {
                    if (!wall[i, j]) continue;
                    float keep = rng.Next01();
                    float yaw = rng.Next01() * 360f;
                    if (keep > 0.92f) continue;               // a few collapsed walls

                    var world = new Vector3(ox + i * cell, 0f, oz + j * cell);
                    world.y = _terrain.Height(world.x, world.z, field);
                    if (SkipSpot(chunk, field, world, p)) continue;

                    if (WorldSpawn.Spawn(pillar, chunk.PropsRoot, world, yaw, 2.4f) != null)
                    {
                        chunk.AddProp();
                        if (++placed >= MaxObjectsPerChunk) return;
                    }
                }
            }
        }

        /// <summary>Carts, barrels and marker stones where a road crosses the chunk.</summary>
        void RoadSideDetails(WorldChunk chunk, RoadField field, ref DetRandom rng, WorldPrefabPalette p)
        {
            float size = _s.chunkSize;
            float x0 = chunk.coord.x * size, z0 = chunk.coord.y * size;

            for (int e = 0; e < field.Edges.Count; e++)
            {
                RoadEdge edge = field.Edges[e];
                if (edge.points == null || edge.points.Length < 3) continue;

                // One landmark per road edge, and only when the middle of that edge really
                // lies in this chunk (so the neighbour does not place a second one).
                int midIndex = edge.points.Length / 2;
                Vector3 mid = edge.points[midIndex];
                if (mid.x < x0 || mid.x > x0 + size || mid.z < z0 || mid.z > z0 + size) continue;

                float keep = rng.Next01();
                float sideSign = rng.Next01() < 0.5f ? -1f : 1f;
                float yaw = rng.Next01() * 360f;
                float extra = rng.Range(2.5f, 6f);
                float scale = rng.Range(0.8f, 1.15f);
                if (keep > 0.55f) continue;

                GameObject prefab = First(p.props, p.stones, p.bushes);
                if (prefab == null) return;

                Vector3 side = edge.normals[midIndex] * sideSign;
                Vector3 pos = mid + side * (edge.halfWidth + extra);
                pos.y = _terrain.Height(pos.x, pos.z, field);

                if (WorldSpawn.Spawn(prefab, chunk.PropsRoot, pos, yaw, scale) != null)
                    chunk.AddProp();
            }
        }

        /// <summary>A ring of standing stones.</summary>
        void StoneCircle(WorldChunk chunk, RoadField field, ref DetRandom rng, WorldPrefabPalette p,
                         float cx, float cz)
        {
            GameObject stone = First(p.stones, p.rocks);
            if (stone == null) return;

            float radius = rng.Range(7f, 12f);
            int count = rng.Range(6, 10);
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f + rng.NextSigned() * 0.15f;
                float scale = rng.Range(1.8f, 3.2f);
                float yaw = rng.Next01() * 360f;

                var pos = new Vector3(cx + Mathf.Cos(a) * radius, 0f, cz + Mathf.Sin(a) * radius);
                pos.y = _terrain.Height(pos.x, pos.z, field);

                if (WorldSpawn.Spawn(stone, chunk.PropsRoot, pos, yaw, scale) != null)
                    chunk.AddProp();
            }
        }

        // =====================================================================
        //  Helpers
        // =====================================================================
        /// <summary>
        /// True when a landmark must not be placed here (it is on a road or on a cliff).
        /// It also drops a little rubble at spots it rejects, which is what makes a ruin look
        /// like it has been weathering for a few centuries.
        /// </summary>
        bool SkipSpot(WorldChunk chunk, RoadField field, Vector3 world, WorldPrefabPalette p)
        {
            if (field != null && !field.IsEmpty)
            {
                RoadHit hit = field.Query(world.x, world.z);
                if (hit.valid && hit.distance < hit.halfWidth + 4f) return true;
            }

            if (_terrain.SlopeDegrees(world.x, world.z, field) > 26f) return true;

            if (p.stones != null && p.stones.Length > 0)
            {
                float roll = DetHash.Float01(DetHash.Hash(Mathf.RoundToInt(world.x), Mathf.RoundToInt(world.z), 0x5A17u));
                if (roll < 0.22f)
                {
                    float yaw = roll * 1600f;
                    var pos = new Vector3(world.x + (roll - 0.11f) * 40f, _terrain.Height(world.x, world.z, field),
                                          world.z - (roll - 0.11f) * 40f);
                    pos.y = _terrain.Height(pos.x, pos.z, field);
                    if (WorldSpawn.Spawn(p.stones[(int)(roll * 977f) % p.stones.Length], chunk.PropsRoot,
                                         pos, yaw, 0.8f) != null)
                        chunk.AddProp();
                }
            }

            return false;
        }

        static GameObject First(params GameObject[][] pools)
        {
            for (int i = 0; i < pools.Length; i++)
            {
                var pool = pools[i];
                if (pool != null && pool.Length > 0 && pool[0] != null) return pool[0];
            }
            return null;
        }
    }
}
