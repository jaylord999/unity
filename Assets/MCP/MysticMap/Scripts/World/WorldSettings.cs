using UnityEngine;

namespace MysticMap.World
{
    /// <summary>
    /// The prefabs the procedural world may spawn. Kept in a plain serializable class so
    /// the editor setup tool can fill it from the FantasyEnvironments folders and the
    /// result is stored in the scene (exactly like the existing PropStreamer / RoadsideLife
    /// components store their pools).
    /// </summary>
    [System.Serializable]
    public class WorldPrefabPalette
    {
        [Tooltip("Tree prefabs (Ambient-Occlusion-Trees).")]
        public GameObject[] trees;
        [Tooltip("Bush / shrub prefabs.")]
        public GameObject[] bushes;
        [Tooltip("Plain grass prefabs (also used sparsely away from the town).")]
        public GameObject[] grass;
        [Tooltip("Flower / low-plant prefabs.")]
        public GameObject[] flowers;
        [Tooltip("Large rock formations.")]
        public GameObject[] rocks;
        [Tooltip("Small stones used in ruins and along roads.")]
        public GameObject[] stones;
        [Tooltip("Straight stone wall pieces (Town pack) used for ruins.")]
        public GameObject[] walls;
        [Tooltip("Odd props placed at road junctions (carts, barrels, ...).")]
        public GameObject[] props;

        public bool IsEmpty =>
            Count(trees) + Count(bushes) + Count(grass) + Count(flowers) +
            Count(rocks) + Count(stones) + Count(walls) + Count(props) == 0;

        static int Count(GameObject[] a) => a == null ? 0 : a.Length;
    }

    /// <summary>
    /// Every tunable of the streamed procedural world. Add it to the scene through
    /// MCP > Procedural World > World Settings (or the setup tool).
    /// </summary>
    [System.Serializable]
    public class WorldSettings
    {
        [Header("Determinism")]
        [Tooltip("Changing this regenerates the whole world. The town never changes.")]
        public int seed = 12345;

        [Header("Streaming")]
        [Tooltip("Size of one square chunk in metres. The town grid is aligned to this.")]
        public float chunkSize = 100f;
        [Tooltip("How many chunks of terrain are kept around the player (radius in chunks).")]
        public int viewRadiusChunks = 3;
        [Tooltip("How many chunks of trees/grass/rocks are kept around the player.")]
        public int detailRadiusChunks = 2;
        [Tooltip("Chunks generated per frame (1-2 keeps the frame time stable).")]
        public int chunksPerFrame = 1;
        [Tooltip("Chunks are removed once they are this many chunks beyond the view radius.")]
        public int unloadMarginChunks = 1;

        [Header("Terrain mesh")]
        [Tooltip("Vertices per side of a near chunk (33 -> 32 quads, ~3 m at 100 m chunks).")]
        public int meshResolution = 33;
        [Tooltip("Vertices per side of a far chunk (simpler mesh for the distance).")]
        public int farMeshResolution = 17;
        [Tooltip("Distance (in chunks) where a chunk switches to the far mesh.")]
        public float lodDistanceChunks = 2.5f;
        [Tooltip("Highest the procedural terrain may get (metres).")]
        public float maxHeight = 140f;
        [Tooltip("Ground level the rolling biomes start from (metres).")]
        public float baseHeight = 20f;
        [Tooltip("Blend band (metres) where procedural ground meets the hand-built map.")]
        public float seamBand = 180f;

        [Header("Roads")]
        [Tooltip("How much the road height rolls over long distances (metres).")]
        public float roadRelief = 16f;
        [Tooltip("Road half width near the town (metres).")]
        public float roadHalfWidthNear = 4.5f;
        [Tooltip("Road half width far from the town (metres).")]
        public float roadHalfWidthFar = 7f;
        [Tooltip("Widest cut/fill apron a road may carve when it crosses a hill (metres).")]
        public float roadMaxApron = 90f;
        [Tooltip("Distance from the town where roads are dead straight; beyond it they wind.")]
        public float roadStraightRange = 140f;
        [Tooltip("Distance from the town where roads reach their maximum wander.")]
        public float roadWindRange = 700f;
        [Tooltip("Maximum extra turn (degrees) a road node may add to the local direction. " +
                 "Higher = the roads sweep through much sharper curves.")]
        public float roadMaxTurnDegrees = 80f;
        [Tooltip("How far each road segment bows sideways. 0 = straight, higher = much curvier roads.")]
        public float roadCurveStrength = 0.60f;

        [Header("Vegetation")]
        [Tooltip("Grid spacing (metres) used to scatter trees.")]
        public float treeSpacing = 11f;
        [Tooltip("Hard cap of trees per chunk.")]
        public int maxTreesPerChunk = 14;
        [Tooltip("Grid spacing (metres) used to scatter grass/flowers/bushes.")]
        public float groundSpacing = 4.5f;
        [Tooltip("Hard cap of ground plants per chunk.")]
        public int maxGroundPlantsPerChunk = 90;
        [Tooltip("Grid spacing (metres) used to scatter rocks.")]
        public float rockSpacing = 16f;
        [Tooltip("Hard cap of rocks per chunk.")]
        public int maxRocksPerChunk = 10;
        [Tooltip("Terrain steeper than this gets no vegetation (degrees).")]
        public float maxVegetationSlope = 32f;
        [Tooltip("Minimum distance from a road centreline for vegetation (metres).")]
        public float roadClearance = 7f;

        [Header("Landmarks (ruins / stone mazes)")]
        [Tooltip("Chance that a ruined-site biome chunk actually builds a ruin.")]
        public float ruinChance = 0.35f;
        [Tooltip("Chance that a labyrinth biome chunk actually builds a stone maze.")]
        public float mazeChance = 0.30f;
        [Tooltip("Extent of a stone maze in cells (each cell is 5 m).")]
        public int mazeCells = 9;

        [Header("Content")]
        [Tooltip("Generate trees/grass/rocks/landmarks at all.")]
        public bool generateEnvironment = true;
        [Tooltip("Give near chunks a mesh collider so the player can walk on them.")]
        public bool generateColliders = true;
        [Tooltip("Inflate the hand-built map rectangle by this much (metres) when skipping chunks.")]
        public float townSkipMargin = 1f;

        [Header("Palette")]
        public WorldPrefabPalette palette = new WorldPrefabPalette();

        /// <summary>Keeps every value inside a sane range (called on load and by the tools).</summary>
        public void Validate()
        {
            chunkSize = Mathf.Clamp(chunkSize, 40f, 400f);
            viewRadiusChunks = Mathf.Clamp(viewRadiusChunks, 1, 12);
            detailRadiusChunks = Mathf.Clamp(detailRadiusChunks, 0, viewRadiusChunks);
            chunksPerFrame = Mathf.Clamp(chunksPerFrame, 1, 8);
            unloadMarginChunks = Mathf.Clamp(unloadMarginChunks, 0, 4);

            meshResolution = Mathf.Clamp(meshResolution, 9, 129);
            farMeshResolution = Mathf.Clamp(farMeshResolution, 5, meshResolution);
            if (meshResolution % 2 == 1) meshResolution++;   // even counts keep LOD edges aligned
            if (farMeshResolution % 2 == 1) farMeshResolution++;
            lodDistanceChunks = Mathf.Clamp(lodDistanceChunks, 1f, viewRadiusChunks);

            maxHeight = Mathf.Clamp(maxHeight, 60f, 400f);
            baseHeight = Mathf.Clamp(baseHeight, 0f, maxHeight - 20f);
            seamBand = Mathf.Clamp(seamBand, 40f, 600f);

            roadRelief = Mathf.Clamp(roadRelief, 0f, 60f);
            roadHalfWidthNear = Mathf.Clamp(roadHalfWidthNear, 2f, 12f);
            roadHalfWidthFar = Mathf.Clamp(roadHalfWidthFar, roadHalfWidthNear, 20f);
            roadMaxApron = Mathf.Clamp(roadMaxApron, 20f, 200f);
            roadStraightRange = Mathf.Clamp(roadStraightRange, 20f, 500f);
            roadWindRange = Mathf.Clamp(roadWindRange, roadStraightRange + 100f, 4000f);
            roadMaxTurnDegrees = Mathf.Clamp(roadMaxTurnDegrees, 0f, 85f);
            roadCurveStrength = Mathf.Clamp(roadCurveStrength, 0f, 1.5f);

            treeSpacing = Mathf.Clamp(treeSpacing, 4f, 40f);
            maxTreesPerChunk = Mathf.Clamp(maxTreesPerChunk, 0, 200);
            groundSpacing = Mathf.Clamp(groundSpacing, 2f, 30f);
            maxGroundPlantsPerChunk = Mathf.Clamp(maxGroundPlantsPerChunk, 0, 600);
            rockSpacing = Mathf.Clamp(rockSpacing, 4f, 60f);
            maxRocksPerChunk = Mathf.Clamp(maxRocksPerChunk, 0, 100);
            maxVegetationSlope = Mathf.Clamp(maxVegetationSlope, 5f, 70f);
            roadClearance = Mathf.Clamp(roadClearance, 0f, 40f);

            ruinChance = Mathf.Clamp01(ruinChance);
            mazeChance = Mathf.Clamp01(mazeChance);
            mazeCells = Mathf.Clamp(mazeCells, 5, 17);
            townSkipMargin = Mathf.Clamp(townSkipMargin, 0f, 50f);

            if (palette == null) palette = new WorldPrefabPalette();
        }
    }
}
