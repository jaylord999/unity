using UnityEngine;

namespace MysticMap.World
{
    /// <summary>
    /// Scatters trees, ground cover and rocks over one chunk. Every candidate comes from a
    /// jittered grid whose randomness is derived only from (seed, chunk, cell), so the same
    /// spot always grows the same plants no matter when you walk past it, and nothing has to
    /// be stored in the scene.
    /// </summary>
    public class VegetationGenerator
    {
        readonly WorldSettings _s;
        readonly TerrainGenerator _terrain;

        public VegetationGenerator(WorldSettings settings, TerrainGenerator terrain)
        {
            _s = settings;
            _terrain = terrain;
        }

        /// <summary>Populates a chunk. <paramref name="densityScale"/> lets far chunks stay cheap.</summary>
        public void Populate(WorldChunk chunk, RoadField field, float densityScale)
        {
            WorldPrefabPalette p = _s.palette;
            if (p == null || p.IsEmpty || chunk.Data == null) return;

            float size = _s.chunkSize;
            float worldX = chunk.coord.x * size;
            float worldZ = chunk.coord.y * size;

            BiomeWeights w = _terrain.Biomes.Weights(worldX + size * 0.5f, worldZ + size * 0.5f);
            float moisture = _terrain.Biomes.Moisture(worldX + size * 0.5f, worldZ + size * 0.5f);

            uint salt = (uint)_s.seed * 2654435761u;
            float rockBias = Mathf.Clamp01(w.rocky + w.highlands * 0.5f);

            // ---- trees --------------------------------------------------------
            float treeDensity = (w.forest * 0.95f + w.meadow * 0.10f + w.highlands * 0.22f +
                                 w.labyrinth * 0.12f + w.ruins * 0.06f) * densityScale;
            Scatter(chunk, field, p.trees, _s.treeSpacing, treeDensity, _s.maxTreesPerChunk,
                    new DetRandom(chunk.coord.x, chunk.coord.y, salt + 31u),
                    0.85f, 1.3f, rockBias, true);

            // ---- bushes and ferns --------------------------------------------
            float bushDensity = (w.forest * 0.6f + w.meadow * 0.25f + w.ruins * 0.2f +
                                 w.labyrinth * 0.35f) * densityScale;
            Scatter(chunk, field, p.bushes, _s.groundSpacing * 1.8f, bushDensity,
                    Mathf.Max(1, _s.maxGroundPlantsPerChunk / 3),
                    new DetRandom(chunk.coord.x, chunk.coord.y, salt + 67u),
                    0.8f, 1.25f, rockBias, false);

            // ---- grass and flowers -------------------------------------------
            float grassDensity = (0.45f + moisture * 0.45f) * (1f - rockBias * 0.6f) * densityScale;
            Scatter(chunk, field, p.grass, _s.groundSpacing, grassDensity,
                    _s.maxGroundPlantsPerChunk,
                    new DetRandom(chunk.coord.x, chunk.coord.y, salt + 103u),
                    0.8f, 1.35f, rockBias, false);

            float flowerDensity = (w.meadow * 0.5f + w.ruins * 0.3f + w.forest * 0.18f +
                                   moisture * 0.2f) * densityScale;
            Scatter(chunk, field, p.flowers, _s.groundSpacing * 1.6f, flowerDensity,
                    Mathf.Max(1, _s.maxGroundPlantsPerChunk / 2),
                    new DetRandom(chunk.coord.x, chunk.coord.y, salt + 149u),
                    0.7f, 1.2f, rockBias, false);

            // ---- rocks --------------------------------------------------------
            float rockDensity = (w.rocky * 0.95f + w.highlands * 0.3f + w.ruins * 0.25f) * densityScale;
            Scatter(chunk, field, p.rocks, _s.rockSpacing, rockDensity, _s.maxRocksPerChunk,
                    new DetRandom(chunk.coord.x, chunk.coord.y, salt + 191u),
                    0.7f, 1.6f, 0f, true);

            // ---- a few stones where the ground is bare -------------------------
            float stoneDensity = (0.12f + rockBias * 0.4f) * densityScale;
            Scatter(chunk, field, p.stones, _s.groundSpacing * 2.2f, stoneDensity,
                    Mathf.Max(1, _s.maxRocksPerChunk),
                    new DetRandom(chunk.coord.x, chunk.coord.y, salt + 233u),
                    0.6f, 1.1f, 0f, false);
        }

        /// <summary>
        /// One jittered-grid pass. The grid guarantees a minimum spacing; the hash decides
        /// whether each cell actually holds something - which is what keeps a forest a
        /// forest and a meadow open, without any neighbour lookups or stored data.
        /// </summary>
        void Scatter(WorldChunk chunk, RoadField field, GameObject[] pool, float spacing,
                     float density, int maxCount, DetRandom rng, float minScale, float maxScale,
                     float rockBias, bool trees)
        {
            if (pool == null || pool.Length == 0 || density <= 0.001f || maxCount <= 0) return;

            float size = _s.chunkSize;
            int perSide = Mathf.Max(1, Mathf.RoundToInt(size / Mathf.Max(1f, spacing)));
            float cell = size / perSide;
            float maxSlope = trees ? _s.maxVegetationSlope : _s.maxVegetationSlope + 12f;
            int placed = 0;

            for (int j = 0; j < perSide && placed < maxCount; j++)
            {
                for (int i = 0; i < perSide && placed < maxCount; i++)
                {
                    // Fixed draw order keeps the whole chunk deterministic.
                    float jx = rng.Next01();
                    float jz = rng.Next01();
                    float pick = rng.Next01();
                    float scaleT = rng.Next01();
                    float yaw = rng.Next01() * 360f;
                    float roll = rng.Next01();

                    float chance = density;
                    if (trees && rockBias > 0.45f) chance *= 0.55f;   // rocky ground keeps few trees
                    if (roll >= chance) continue;

                    float localX = (i + 0.15f + jx * 0.7f) * cell;
                    float localZ = (j + 0.15f + jz * 0.7f) * cell;

                    if (!Allowed(chunk, field, localX, localZ, maxSlope, trees)) continue;

                    GameObject prefab = pool[Mathf.Min(pool.Length - 1, (int)(pick * pool.Length))];
                    float h = chunk.HeightAtLocal(localX, localZ);
                    var world = new Vector3(chunk.coord.x * size + localX, h,
                                            chunk.coord.y * size + localZ);

                    if (WorldSpawn.Spawn(prefab, chunk.PropsRoot, world, yaw,
                                         Mathf.Lerp(minScale, maxScale, scaleT)) != null)
                    {
                        chunk.AddProp();
                        placed++;
                    }
                }
            }
        }

        bool Allowed(WorldChunk chunk, RoadField field, float localX, float localZ,
                     float maxSlope, bool trees)
        {
            TerrainGenerator.ChunkMesh d = chunk.Data;
            float size = _s.chunkSize;

            // Never in the middle of a road (kept clear for walking).
            if (field != null && !field.IsEmpty)
            {
                RoadHit hit = field.Query(chunk.coord.x * size + localX, chunk.coord.y * size + localZ);
                float allowFrom = _s.roadClearance + (trees ? 1.5f : 0f);
                if (hit.valid && hit.distance < allowFrom + hit.halfWidth) return false;
            }

            // Not on cliffs.
            float hL = d.SampleHeight(localX - 2f, localZ);
            float hR = d.SampleHeight(localX + 2f, localZ);
            float hD = d.SampleHeight(localX, localZ - 2f);
            float hU = d.SampleHeight(localX, localZ + 2f);
            float rise = Mathf.Max(Mathf.Abs(hR - hL), Mathf.Abs(hU - hD));
            float slope = Mathf.Atan2(rise, 4f) * Mathf.Rad2Deg;
            return slope <= maxSlope;
        }
    }
}
