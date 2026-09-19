using UnityEngine;

namespace MysticMap.World
{
    /// <summary>
    /// Owns the procedural passes for one world and produces a chunk on demand. Everything
    /// that decides what a chunk looks like lives behind this class, so the streaming
    /// manager only has to ask for a coordinate.
    /// </summary>
    public class WorldGenerator
    {
        public readonly WorldSettings Settings;
        public readonly TownAnchor Town;
        public readonly TerrainGenerator Terrain;
        public readonly VegetationGenerator Vegetation;
        public readonly EnvironmentGenerator Environment;

        public WorldGenerator(WorldSettings settings, TownAnchor town)
        {
            Settings = settings;
            Town = town;
            Settings.Validate();
            Terrain = new TerrainGenerator(Settings, town);
            Vegetation = new VegetationGenerator(Settings, Terrain);
            Environment = new EnvironmentGenerator(Settings, Terrain);
        }

        public float ChunkSize => Settings.chunkSize;

        // =====================================================================
        //  Chunk placement
        // =====================================================================
        /// <summary>World position of a chunk's corner.</summary>
        public Vector3 OriginOf(int cx, int cz) => new Vector3(cx * ChunkSize, 0f, cz * ChunkSize);

        /// <summary>
        /// True when this chunk belongs to the hand-built map (the town) and must not be
        /// generated. The town keeps its terrain, trees, grass, roads and buildings exactly
        /// as they were authored.
        /// </summary>
        public bool IsLegacy(Vector2Int coord)
        {
            if (Town == null) return false;
            float s = ChunkSize;
            float x0 = coord.x * s, z0 = coord.y * s;
            return Town.OverlapsLegacy(x0, z0, x0 + s, z0 + s, -Settings.townSkipMargin);
        }

        public Vector2Int ChunkOf(Vector3 worldPos) => new Vector2Int(
            Mathf.FloorToInt(worldPos.x / ChunkSize),
            Mathf.FloorToInt(worldPos.z / ChunkSize));

        // =====================================================================
        //  Chunk content
        // =====================================================================
        /// <summary>
        /// Builds (or rebuilds) the terrain mesh of a chunk and returns it. The road field is
        /// handed back so the vegetation pass can reuse it instead of rebuilding it.
        /// </summary>
        public RoadField BuildTerrain(WorldChunk chunk, bool highLod)
        {
            float s = ChunkSize;
            float x0 = chunk.coord.x * s, z0 = chunk.coord.y * s;

            RoadField field = Terrain.Roads.BuildField(x0, z0, x0 + s, z0 + s, Settings.roadMaxApron);
            TerrainGenerator.ChunkMesh data = Terrain.BuildMesh(chunk.coord.x, chunk.coord.y, highLod, field);

            chunk.transform.position = new Vector3(x0, 0f, z0);
            chunk.Setup(chunk.coord, data, ChunkMaterials.For(data.dominant), highLod,
                        Settings.generateColliders && highLod && data.mesh != null);

            return field;
        }

        /// <summary>Scatters vegetation and landmarks. Only called for chunks near the player.</summary>
        public void Populate(WorldChunk chunk, RoadField field, float densityScale)
        {
            if (!Settings.generateEnvironment || densityScale <= 0.01f) return;

            Vegetation.Populate(chunk, field, densityScale);
            Environment.Populate(chunk, field, densityScale);
        }

        /// <summary>Full creation of a chunk object (terrain + content).</summary>
        public WorldChunk CreateChunk(Transform parent, Vector2Int coord, bool highLod, bool detail,
                                      float densityScale)
        {
            var go = new GameObject("Chunk_" + coord.x + "_" + coord.y);
            go.transform.SetParent(parent, false);

            var chunk = go.AddComponent<WorldChunk>();
            RoadField field = BuildTerrain(chunk, highLod);
            if (detail) Populate(chunk, field, densityScale);
            return chunk;
        }

        // =====================================================================
        //  Height / helpers used by the player and the tools
        // =====================================================================
        /// <summary>
        /// Ground height anywhere: the hand-built terrain inside its own rectangle, a live
        /// chunk when one exists, otherwise the procedural field.
        /// </summary>
        public float SampleHeight(float x, float z, WorldChunk liveChunk)
        {
            if (Terrain == null) return Settings.baseHeight;

            if (Town != null && Town.InsideLegacy(x, z) && Town.legacyTerrain != null)
                return Town.legacyTerrain.SampleHeight(new Vector3(x, 0f, z));

            if (liveChunk != null && liveChunk.Data != null)
            {
                Vector3 o = liveChunk.transform.position;
                return liveChunk.Data.SampleHeight(x - o.x, z - o.z);
            }

            return Terrain.Height(x, z);
        }

        /// <summary>Type of country at a world position (used by the tools and gizmos).</summary>
        public BiomeType BiomeAt(float x, float z) => Terrain.Biomes.Weights(x, z).Dominant;

        /// <summary>Distance to the nearest procedural road (metres).</summary>
        public float RoadDistance(float x, float z, out float halfWidth) =>
            Terrain.Roads.DistanceToRoad(x, z, out halfWidth);

        public float RoadElevation(float x, float z) => Terrain.Roads.RoadElevation(x, z);
    }
}
