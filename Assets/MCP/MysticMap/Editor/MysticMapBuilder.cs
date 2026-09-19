#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// One-click generator for a large (1km x 1km) mysterious fantasy map from the
    /// "FantasyEnvironments" asset. Menu: MCP -> Build Mystic Map
    ///
    /// Builds: rolling terrain, splat-painted ground (grass-dominant with roads),
    /// a network of roads, terrain trees (auto LOD/culled), lots of grass as terrain
    /// detail, scattered ground-cover props that are streamed in only around the
    /// player, a small village, fog + a short camera far plane so only nearby
    /// content is actually rendered, and a bouncing slime player seen from a third-person camera.
    /// </summary>
    public static partial class MysticMapBuilder
    {
        // ---- Where generated assets are stored --------------------------------
        const string GeneratedDir = "Assets/MCP/MysticMap/Generated";
        const string TerrainDataPath = GeneratedDir + "/MysticTerrain.asset";
        const string ScenePath = "Assets/Scenes/MysticMap.unity";

        // ---- Source asset folders (FantasyEnvironments) -----------------------
        const string EnvDir = "Assets/FantasyEnvironments/Environments";
        const string LayerDir = EnvDir + "/TerrainLayers";
        const string TreeDir = EnvDir + "/Ambient-Occlusion-Trees/Prefabs";
        const string PropsDir = EnvDir + "/Prefabs";
        const string TownDir = EnvDir + "/Town/Prefabs";

        // ---- Map size / resolution --------------------------------------------
        const float MapSize = 1000f;      // world metres (X and Z)
        const float MaxHeight = 90f;      // highest the terrain can be
        const int HeightRes = 513;        // heightmap samples (513 -> 512 cells)
        const int SplatRes = 512;         // splatmap (alphamap) resolution
        const int DetailRes = 512;        // grass detail resolution
        const int DetailPerPatch = 8;

        // Terrain colours. Layers must be added in this order (<=4 for safe splatting).
        const string LAYER_GRASS = "layer_ground_grass";
        const string LAYER_SOIL = "layer_ground_soil";
        const string LAYER_ROCK = "layer_ground_rock";
        const string LAYER_ROAD = "layer_ground_road";

        // Town / fortress (in the middle of the map)
        static readonly Vector2 Village = new Vector2(500f, 520f);
        const float PlazaRadius = 13f;    // paved market-square radius (m)
        const float RoadHalfWidth = 3.6f;
        const float RoadFloor = 24f;   // height the town + roads are flattened to (m)

        // Fortress footprint: world metres from the town centre to the wall centre line.
        const float FortHX = 57f;      // east / west walls
        const float FortHZ = 47f;      // north / south walls
        const float WallScale = 1.45f; // scale applied to the stone wall/tower pieces

        // Grass is allowed to grow inside the fortress town too. It is thinned out by this
        // factor so the compacted town floor still reads as a lived-in yard, not open meadow.
        const float TownGrassScale = 0.6f;

        // How far the approach roads run out of the two main (north / south) gates.
        const float RoadLen = 175f;

        // Player spawn: on the approach road just outside the north gate.
        static readonly Vector2 Spawn = new Vector2(500f, 520f + FortHZ + 9f);

        static Terrain _terrain;
        static TerrainData _td;
        static float _spacing;   // world metres per heightmap cell

        // Hill controls (read from Map Settings; default = current look).
        static float _hillHeight = 30f;
        static float _hillScale = 1f;

        // Large "rim" hills / mountains that ring the edges of the map.
        static bool _borderOn = true;      // on by default
        static float _borderHeight = 88f;  // tallest border crest (m)
        static float _borderRim = 235f;    // how far the mountains reach inward (m)

        public static bool DebugMode = true;

        // =========================================================================
        [MenuItem("MCP/Build Mystic Map (1km)")]
        public static void BuildFromMenu()
        {
            Build();
        }

        [MenuItem("MCP/Open Mystic Map scene")]
        public static void OpenSceneFromMenu()
        {
            if (System.IO.File.Exists(ScenePath))
                EditorSceneManager.OpenScene(ScenePath);
            else
                Debug.LogWarning("MysticMap scene not built yet. Use 'Build Mystic Map' first.");
        }

        /// <summary>Public entry point (also callable by scripts / batch mode).</summary>
        public static void Build()
        {
            Log("MCP Map Builder starting...");
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            try
            {
                GenerateTerrainAsset();
                CreateTerrainGameObject();
                CreateLightingAndFog();
                CreatePlayer();
                CreateFortressTown();
                CreateScatter();
                Log("Terrain + content done.");
            }
            catch (System.Exception e)
            {
                Debug.LogError("MCP Map Builder failed: " + e);
            }

            if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(ScenePath)))
                AssetDatabase.CreateFolder("Assets", "Scenes");

            EditorSceneManager.SaveScene(newScene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = _terrain != null ? _terrain.gameObject : null;
            SceneView.FrameLastActiveSceneView();
            Log("Mystic Map saved to " + ScenePath);
            Log("Tip: press Play and walk with WASD (Shift to run). Use the MCP/Open menu to reopen.");
        }
        // =========================================================================
        //  Terrain data generation
        // =========================================================================
        static void GenerateTerrainAsset()
        {
            if (System.IO.File.Exists(TerrainDataPath))
                AssetDatabase.DeleteAsset(TerrainDataPath);

            _td = new TerrainData();
            _td.heightmapResolution = HeightRes;
            _td.size = new Vector3(MapSize, MaxHeight, MapSize);
            _td.alphamapResolution = SplatRes;
            _td.baseMapResolution = 512;
            _td.SetDetailResolution(DetailRes, DetailPerPatch);
            _spacing = MapSize / (HeightRes - 1);

            _td.terrainLayers = new[]
            {
                LoadLayer(LAYER_GRASS),
                LoadLayer(LAYER_SOIL),
                LoadLayer(LAYER_ROCK),
                LoadLayer(LAYER_ROAD)
            };

            ApplyHeights();
            ApplySplatmap();
            ApplyGrassDetails();
            ClearStoredTrees();

            AssetDatabase.CreateAsset(_td, TerrainDataPath);
            AssetDatabase.SaveAssets();
        }

        static float[,] _h2; // world-metre heights indexed [z, x]

        static void ApplyHeights()
        {
            _hillHeight = EditorPrefs.GetFloat("MM.HillHeight", 30f);
            _hillScale = Mathf.Max(0.2f, EditorPrefs.GetFloat("MM.HillScale", 1f));
            _borderOn = EditorPrefs.GetBool("MM.BorderHills", true);
            _borderHeight = Mathf.Clamp(EditorPrefs.GetFloat("MM.BorderHeight", 88f), 20f, 90f);
            _h2 = new float[HeightRes, HeightRes];
            float[,] hs = new float[HeightRes, HeightRes];
            for (int z = 0; z < HeightRes; z++)
            {
                float wz = z * _spacing;
                for (int x = 0; x < HeightRes; x++)
                {
                    float wx = x * _spacing;
                    float h = HeightWorld(wx, wz);
                    _h2[z, x] = h;
                    hs[z, x] = Mathf.Clamp01(h / MaxHeight);
                }
            }
            _td.SetHeights(0, 0, hs);
        }

        // Height at a world position, in metres:
        // a flat village centre, gentle rolling countryside around it, and a ring
        // of large border hills/mountains that rise up near the edges of the map so
        // the whole scene sits in a natural valley framed by an interesting skyline.
        static float HeightWorld(float x, float z)
        {
            float h = RollingField(x, z);            // gentle interior hills

            if (_borderOn)
            {
                // Border mountains gradually take over near an edge of the map.
                float a = BorderAmount(x, z);
                if (a > 0f) h = Mathf.Lerp(h, MountainHeight(x, z), a);
            }

            // Flatten a level plateau across the whole fortress so its walls, towers and
            // buildings sit on even ground, and flatten corridors along every road so the
            // long approach roads to the gates run level across the rolling countryside.
            {
                float ax = Mathf.Abs(x - Village.x) - (FortHX + 10f);
                float az = Mathf.Abs(z - Village.y) - (FortHZ + 10f);
                float dRect = Mathf.Max(ax, az);                  // <= 0 inside the plateau
                float rectBlend = Mathf.Clamp01(1f - dRect / 26f);

                float rd = RoadDistance(x, z);
                float rdBlend = Mathf.Clamp01(1f - Mathf.Max(0f, rd - (RoadHalfWidth + 5f)) / 22f);

                float flatBlend = Mathf.Max(rectBlend, rdBlend);
                if (flatBlend > 0.01f)
                    h = Mathf.Lerp(h, RoadFloor, Mathf.Clamp01(flatBlend));
            }

            return Mathf.Clamp(h, 12f, MaxHeight - 1f);
        }

        // Gentle rolling countryside that covers the middle of the map.
        static float RollingField(float x, float z)
        {
            float sx = x * 0.0024f * _hillScale + 31f;
            float sy = z * 0.0024f * _hillScale + 9f;
            float mx = x * 0.0080f * _hillScale + 7f;
            float my = z * 0.0080f * _hillScale + 19f;
            float hx = x * 0.0200f * _hillScale + 53f;
            float hy = z * 0.0200f * _hillScale + 3f;
            float s =
                Mathf.PerlinNoise(sx, sy) * 0.55f +
                Mathf.PerlinNoise(mx, my) * 0.30f +
                Mathf.PerlinNoise(hx, hy) * 0.15f;
            s = Mathf.Clamp01(s);
            return 20f + _hillHeight * Mathf.Pow(s, 1.1f);   // gentle rolling hills
        }

        // How strongly the border hills should appear at (x, z): 1 right against an
        // edge, fading smoothly to 0 once we are far enough into the map. Each side
        // has its own jittered reach so the ring around the map is irregular.
        static float BorderAmount(float x, float z)
        {
            float aL = BorderSide(x,           z, 2.3f, 0.6f);  // near x = 0
            float aR = BorderSide(MapSize - x, z, 9.7f, 3.1f);  // near x = MapSize
            float aB = BorderSide(z,           x, 5.5f, 1.7f);  // near z = 0
            float aT = BorderSide(MapSize - z, x, 13.1f, 4.9f);  // near z = MapSize
            return Mathf.Max(Mathf.Max(aL, aR), Mathf.Max(aB, aT));
        }

        // d = distance in metres from one straight edge of the map. 'along' runs
        // along that edge and jitters the reach, giving every side its own shape.
        static float BorderSide(float d, float along, float seedA, float seedB)
        {
            float reach = _borderRim * (0.6f + 0.8f * Mathf.PerlinNoise(along * 0.004f + seedA, seedB));
            float a = 1f - d / Mathf.Max(20f, reach);
            a = Mathf.Clamp01(a);
            return a * a * (3f - 2f * a);          // smooth falloff from the edge
        }

        // Large hills/mountains ringing the map: tall craggy peaks with lower passes
        // between them, so the border is an interesting skyline, not a flat wall.
        static float MountainHeight(float x, float z)
        {
            // Broad bumps decide where tall peaks and open passes sit.
            float broad = Mathf.Clamp01(
                Mathf.PerlinNoise(x * 0.0042f + 17f, z * 0.0042f + 27f) * 0.55f +
                Mathf.PerlinNoise(x * 0.0085f + 91f, z * 0.0085f + 55f) * 0.45f);

            // Ridged component sharpens the crests (instead of smooth dunes).
            float fine = Mathf.PerlinNoise(x * 0.024f + 3f, z * 0.024f + 8f);
            float ridge = Mathf.Pow(Mathf.Abs(2f * fine - 1f), 1.5f);

            // Small rocky detail so large slopes aren't perfectly smooth.
            float micro = Mathf.PerlinNoise(x * 0.052f + 60f, z * 0.052f + 41f);

            float t = Mathf.Clamp01(broad * 0.60f + ridge * 0.32f + micro * 0.08f);
            return Mathf.Lerp(38f, _borderHeight, t);
        }

        static float SlopeAt(int hx, int hz)
        {
            int x0 = Mathf.Max(0, hx - 1), x1 = Mathf.Min(HeightRes - 1, hx + 1);
            int z0 = Mathf.Max(0, hz - 1), z1 = Mathf.Min(HeightRes - 1, hz + 1);
            float riseX = Mathf.Abs(_h2[hz, x1] - _h2[hz, x0]);
            float riseZ = Mathf.Abs(_h2[z1, hx] - _h2[z0, hx]);
            float rise = Mathf.Max(riseX, riseZ);
            float run = 2f * _spacing;
            return Mathf.Atan2(rise, Mathf.Max(run, 0.01f)) * Mathf.Rad2Deg;
        }
        static void ApplySplatmap()
        {
            int res = SplatRes;
            float cell = MapSize / res;
            float[,,] amap = new float[res, res, 4];

            for (int z = 0; z < res; z++)
            {
                float wz = (z + 0.5f) * cell;
                int hz = Mathf.Clamp(Mathf.RoundToInt(wz / _spacing), 0, HeightRes - 1);
                for (int x = 0; x < res; x++)
                {
                    float wx = (x + 0.5f) * cell;
                    int hx = Mathf.Clamp(Mathf.RoundToInt(wx / _spacing), 0, HeightRes - 1);
                    float rd = RoadDistance(wx, wz);
                    float slope = SlopeAt(hx, hz);

                    float road = Mathf.Clamp01((RoadHalfWidth + 1.5f - rd) / 1.5f);

                    float steep = Mathf.Clamp01((slope - 16f) / 42f);
                    float g = 1f - steep;
                    float soil = 0.4f * steep;
                    float rock = 0.6f * steep;

                    float blot = Mathf.PerlinNoise(wx * 0.012f + 3f, wz * 0.012f + 9f);
                    if (blot > 0.82f) { soil += 0.5f; g -= 0.5f; }

                    // Inside the fortress the ground reads as compacted, lived-in earth
                    // (mostly soil with patches of worn grass) instead of open meadow.
                    float tax = Mathf.Abs(wx - Village.x) - (FortHX + 2f);
                    float taz = Mathf.Abs(wz - Village.y) - (FortHZ + 2f);
                    if (tax < 0f && taz < 0f)
                    {
                        g = 0.16f + 0.10f * Mathf.PerlinNoise(wx * 0.03f + 5f, wz * 0.03f + 11f);
                        soil = 0.70f + 0.12f * Mathf.PerlinNoise(wx * 0.017f + 2f, wz * 0.017f + 8f);
                        rock = 0.05f;
                    }

                    float remain = Mathf.Clamp01(1f - road);
                    amap[z, x, 0] = Mathf.Clamp01(g * remain);
                    amap[z, x, 1] = Mathf.Clamp01(soil * remain);
                    amap[z, x, 2] = Mathf.Clamp01(rock * remain);
                    amap[z, x, 3] = road;

                    float sum = amap[z, x, 0] + amap[z, x, 1] + amap[z, x, 2] + amap[z, x, 3];
                    if (sum > 0.0001f)
                    {
                        amap[z, x, 0] /= sum; amap[z, x, 1] /= sum;
                        amap[z, x, 2] /= sum; amap[z, x, 3] /= sum;
                    }
                }
            }
            _td.SetAlphamaps(0, 0, amap);
        }

        // ---- Road network -------------------------------------------------------
        static List<Vector2[]> _roadPolys;

        static void EnsureRoads()
        {
            if (_roadPolys != null) return;
            _roadPolys = MapRoads.Paths;
        }

        static float RoadDistance(float x, float z)
        {
            EnsureRoads();
            Vector2 p = new Vector2(x, z);

            float toCenter = Vector2.Distance(p, Village);
            if (toCenter < PlazaRadius) return 0f;
            float best = toCenter - PlazaRadius;

            for (int k = 0; k < _roadPolys.Count; k++)
            {
                Vector2[] pts = _roadPolys[k];
                for (int i = 1; i < pts.Length; i++)
                    best = Mathf.Min(best, DistToSeg(pts[i - 1], pts[i], p));
            }
            return best;
        }

        static float DistToSeg(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float len = ab.sqrMagnitude;
            float t = len > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len) : 0f;
            return Vector2.Distance(a + ab * t, p);
        }
        // ---- Grass detail (dense ground grass rendered by the Terrain, cheap + auto-culled)
        static void ApplyGrassDetails()
        {
            float density = EditorPrefs.GetFloat("MM.GrassDensity", 1f);
            float scale = EditorPrefs.GetFloat("MM.GrassScale", 1f);

            Texture2D shortTex = MakeGrassTexAsset("grass_short", 0.5f, 41);
            Texture2D midTex = MakeGrassTexAsset("grass_med", 0.75f, 87);
            Texture2D tallTex = MakeGrassTexAsset("grass_tall", 1.05f, 199);

            _td.detailPrototypes = new[]
            {
                MakeGrassProto(shortTex, 0.2f * scale, 0.5f * scale, 0.35f, 0.6f),
                MakeGrassProto(midTex, 0.5f * scale, 0.9f * scale, 0.45f, 0.7f),
                MakeGrassProto(tallTex, 0.9f * scale, 1.6f * scale, 0.5f, 0.9f)
            };

            int res = DetailRes;
            float cell = MapSize / res;
            long total0 = 0, total1 = 0, total2 = 0;

            int[,] c0 = new int[res, res];
            int[,] c1 = new int[res, res];
            int[,] c2 = new int[res, res];
            for (int z = 0; z < res; z++)
            {
                float wz = (z + 0.5f) * cell;
                int hz = Mathf.Clamp(Mathf.RoundToInt(wz / _spacing), 0, HeightRes - 1);
                for (int x = 0; x < res; x++)
                {
                    float wx = (x + 0.5f) * cell;
                    float f = GrassFactor(wx, wz, hz);

                    // Dense field everywhere (short grass is the base layer).
                    int n0 = Mathf.RoundToInt(f * density * 13f);
                    // Medium blades, mostly in lush zones.
                    int n1 = Mathf.RoundToInt(Mathf.Max(0f, f - 0.25f) * density * 10f);
                    // Tall rye only in clumped patches -> natural variation in height.
                    float patch = Mathf.PerlinNoise(wx * 0.005f + 51f, wz * 0.005f + 12f);
                    int n2 = patch > 0.52f ? Mathf.RoundToInt(f * density * 9f) : 0;

                    c0[z, x] = n0; c1[z, x] = n1; c2[z, x] = n2;
                    total0 += n0; total1 += n1; total2 += n2;
                }
            }

            _td.SetDetailLayer(0, 0, 0, c0);
            _td.SetDetailLayer(0, 0, 1, c1);
            _td.SetDetailLayer(0, 0, 2, c2);
            Log(string.Format("Grass detail: short={0:N0}  med={1:N0}  tall={2:N0}", total0, total1, total2));
        }

        static float DistanceToSpawn(float x, float z) => Vector2.Distance(new Vector2(x, z), Spawn);

        // Returns a grass-field density factor for a world position (0 = no grass).
        static float GrassFactor(float x, float z, int hz)
        {
            // Roads stay clear of grass (plus a clean shoulder).
            float rd = RoadDistance(x, z);
            if (rd < RoadHalfWidth + 4f) return 0f;

            // The paved market square stays bare.
            if (Vector2.Distance(new Vector2(x, z), Village) < PlazaRadius + 2f) return 0f;

            // Grass grows inside the fortress town too; it is thinned with TownGrassScale
            // further down so the compacted town floor still reads as a lived-in yard.
            bool inFortress = Mathf.Abs(x - Village.x) < FortHX + 4f &&
                              Mathf.Abs(z - Village.y) < FortHZ + 4f;

            // Broad lushness: some big areas are thick meadows, others thinner.
            float lush = Mathf.PerlinNoise(x * 0.0011f + 3f, z * 0.0011f + 7f);
            // Fine clumping so the field reads as natural tufts, not a grid.
            float clump = Mathf.PerlinNoise(x * 0.006f + 40f, z * 0.006f + 20f);

            float f = Mathf.Lerp(0.55f, 1.15f, lush);
            f *= Mathf.Lerp(0.55f, 1.35f, clump);
            if (inFortress) f *= TownGrassScale;                 // town floor: thinner, tidier grass

            // Still grow under/around trees (the field now covers the whole map, town included).
            int hx = Mathf.Clamp(Mathf.RoundToInt(x / _spacing), 0, HeightRes - 1);
            if (_h2 != null)
            {
                float slope = SlopeAt(hx, hz);
                f *= Mathf.Lerp(1f, 0.25f, Mathf.Clamp01((slope - 28f) / 45f)); // less on very steep cliffs
            }

            // Dithering noise so individual cells vary (some dense, some lighter).
            float dith = Mathf.PerlinNoise(x * 0.05f + 90f, z * 0.05f + 61f);
            f *= 0.45f + 0.8f * dith;

            return Mathf.Clamp(f, 0f, 2.2f);
        }


        static DetailPrototype MakeGrassProto(Texture2D tex, float minH, float maxH, float minW, float maxW)
        {
            return new DetailPrototype
            {
                renderMode = DetailRenderMode.Grass,
                prototypeTexture = tex,
                healthyColor = new Color(0.42f, 0.72f, 0.26f, 1f),
                dryColor = new Color(0.55f, 0.72f, 0.30f, 1f),
                noiseSpread = 0.12f,
                minHeight = minH, maxHeight = maxH,
                minWidth = minW, maxWidth = maxW,
                useInstancing = true
            };
        }

        // Generates a simple soft tuft-of-blades grass texture and stores it as an asset.
        static Texture2D MakeGrassTexAsset(string name, float bladeH, int rngSeed)
        {
            int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var cols = new Color[size * size];
            var rand = new System.Random(rngSeed);

            int blades = 26;
            for (int b = 0; b < blades; b++)
            {
                float bx = 8f + (float)(rand.NextDouble()) * (size - 16f);
                float hgt = bladeH * size * (0.6f + (float)rand.NextDouble() * 0.8f);
                float bend = ((float)rand.NextDouble() - 0.5f) * 6f;
                float startW = 1.4f + (float)rand.NextDouble() * 1.6f;
                for (int y = 0; y < (int)hgt; y++)
                {
                    int iy = size - 1 - y;
                    if (iy < 0 || iy >= size) continue;
                    float t = y / hgt;
                    float cx = bx + bend * t;
                    float halfW = Mathf.Lerp(startW, 0.3f, t);
                    int x0 = Mathf.Clamp((int)(cx - halfW), 0, size - 1);
                    int x1 = Mathf.Clamp((int)(cx + halfW), 0, size - 1);
                    for (int px = x0; px <= x1; px++)
                    {
                        int idx = iy * size + px;
                        cols[idx].r = cols[idx].g = cols[idx].b = 1f;
                        cols[idx].a = Mathf.Max(cols[idx].a, 1f);
                    }
                }
            }
            tex.SetPixels(cols);
            tex.Apply(false, true);

            string path = GeneratedDir + "/" + name + ".png";
            try { System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); }
            catch (System.Exception e) { Debug.LogWarning("Could not write " + path + ": " + e); }
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        // ---- Trees (NO LONGER baked) ------------------------------------------------
        // Trees used to be stored as Terrain tree instances, which bloated the terrain asset
        // with ~1600 entries. They now grow at runtime around the player instead - see
        // RuntimeTrees and the "MCP > Trees" menu - keeping clear of the roads and the walled
        // town. This list is still used to pick the runtime tree prefabs.
        static readonly string[] TreeNames =
        {
            "Oak_tree1", "Oak_tree3", "Deciduous_tree1", "Deciduous_tree4",
            "Pine_tree1", "Pine_tree2", "Birch_tree1", "Willow_tree1"
        };

        // Strips any trees an older build stored in the terrain, so nothing tree-related stays
        // saved in the terrain asset / scene.
        static void ClearStoredTrees()
        {
            if (_td == null) return;
            int had = _td.treeInstances != null ? _td.treeInstances.Length : 0;
            _td.treeInstances = new TreeInstance[0];
            _td.treePrototypes = new TreePrototype[0];
            Log("Stored terrain trees cleared: " + had + " (trees grow at runtime now).");
        }
        // =========================================================================
        //  Scene setup
        // =========================================================================
        // Render distance in metres picked in Map Settings (= the camera far plane,
        // so nothing is drawn past it). Everything around the player uses this.
        static float RenderDistance()
        {
            return Mathf.Max(30f, EditorPrefs.GetFloat("MM.RenderDist", 130f));
        }

        // How far the streamed nature props are kept active around the player.
        static float PropRadius()
        {
            return Mathf.Max(40f, RenderDistance() * 0.8f);
        }

        // Push the chosen render distance into a terrain's tree / grass / basemap draws.
        static void ApplyTerrainRenderDistances(Terrain terrain, float rd)
        {
            if (terrain == null) return;
            terrain.treeDistance = Mathf.Max(40f, rd);              // trees pop in up to the far plane
            terrain.detailObjectDistance = Mathf.Max(25f, rd * 0.6f); // grass/details closer for speed
            terrain.basemapDistance = Mathf.Max(60f, rd);           // far ground falls back to baked basemap
        }

        static void CreateTerrainGameObject()
        {
            var go = new GameObject("Terrain");
            _terrain = go.AddComponent<Terrain>();
            _terrain.terrainData = _td;
            var col = go.AddComponent<TerrainCollider>();
            col.terrainData = _td;

            Shader s = Shader.Find("Nature/Terrain/Standard");
            if (s != null) _terrain.materialTemplate = new Material(s);
            else Debug.LogWarning("Terrain shader not found (are you on Built-in pipeline?).");

            // Performance: only content near the camera is rendered. These distances
            // follow the "Render distance" set in Map Settings (a fresh build already
            // respects it, and ApplyAtmosphereToScene can retune it without rebuilding).
            _terrain.heightmapPixelError = 4f;
            _terrain.detailObjectDensity = 1f;
            ApplyTerrainRenderDistances(_terrain, RenderDistance());
            go.transform.position = Vector3.zero;

            // Bake the basemap texture so distant terrain is cheap.
            EditorUtility.SetDirty(_td);
            Log("Terrain created (1000m x 1000m).");
        }

        static void CreateLightingAndFog()
        {
            var sunGo = new GameObject("Directional Light");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.05f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.52f, 0.56f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.34f, 0.31f);
            RenderSettings.ambientGroundColor = new Color(0.13f, 0.16f, 0.13f);

            // Mysterious fog - hides distant content so only what's close reads clearly.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = EditorPrefs.GetFloat("MM.FogDensity", 0.008f);
            RenderSettings.fogColor = new Color(0.58f, 0.68f, 0.70f);
            RenderSettings.skybox = null;
        }

        static float WorldSampleHeight(float x, float z)
        {
            if (_terrain != null && _terrain.terrainData != null)
                return _terrain.SampleHeight(new Vector3(x, 0f, z));
            if (_h2 != null)
            {
                int hx = Mathf.Clamp(Mathf.RoundToInt(x / _spacing), 0, HeightRes - 1);
                int hz = Mathf.Clamp(Mathf.RoundToInt(z / _spacing), 0, HeightRes - 1);
                return _h2[hz, hx];
            }
            return RoadFloor;
        }

        static GameObject LoadPrefab(string path)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (p == null) Debug.LogWarning("Missing prefab: " + path);
            return p;
        }

        static TerrainLayer LoadLayer(string name)
        {
            var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerDir + "/" + name + ".terrainlayer");
            if (l == null) Debug.LogError("Missing terrain layer: " + name);
            return l;
        }

        static void Log(string msg)
        {
            if (DebugMode) Debug.Log("[MysticMap] " + msg);
        }
        static void CreatePlayer()
        {
            var pGo = new GameObject("Player");
            pGo.transform.position = new Vector3(Spawn.x, WorldSampleHeight(Spawn.x, Spawn.y) + 0.2f, Spawn.y);

            // The (Mystic Map) player is a slime: the CharacterController is auto-sized to the
            // imported model by SlimePlayer.FitToModel(), so these are just safe defaults.
            var cc = pGo.AddComponent<CharacterController>();
            cc.height = 1.2f; cc.radius = 0.5f; cc.center = new Vector3(0f, 0.6f, 0f);

            var slime = pGo.AddComponent<SlimePlayer>();

            var slimeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SlimePlayerSetup.SlimeModelPath);
            if (slimeAsset != null)
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(slimeAsset, pGo.transform);
                model.name = slimeAsset.name;
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                slime.model = model.transform;
            }
            else
            {
                Debug.LogWarning("[MysticMap] Slime model not found at " + SlimePlayerSetup.SlimeModelPath +
                                 " - the player was created without a visible body.");
            }

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(pGo.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, -4.5f);

            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 75f;
            cam.nearClipPlane = 0.05f;
            // Render distance ~1/4 of the original 500 m, heavy fog beyond it.
            cam.farClipPlane = EditorPrefs.GetFloat("MM.RenderDist", 130f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            camGo.AddComponent<AudioListener>();

            slime.cam = cam;
            slime.FitToModel();   // size the controller + camera pivot to the slime mesh

            Log("Slime player created at " + pGo.transform.position.ToString("0.0"));
        }

        // Buildings are deliberately the low-priority part: a handful around the plaza.
        static void CreateVillage()
        {
            var root = new GameObject("Village");
            root.transform.position = Vector3.zero;

            // name, offset direction (angle degrees), distance from center, scale
            var plan = new[]
            {
                new { name = "Tavern",        ang = 215f, dist = 33f, s = 1f  },
                new { name = "Windmill",      ang = 20f,  dist = 40f, s = 0.9f },
                new { name = "House1",        ang = 120f, dist = 30f, s = 0.9f },
                new { name = "House2",        ang = 300f, dist = 31f, s = 0.9f },
                new { name = "House3",        ang = 80f,  dist = 42f, s = 0.85f },
                new { name = "WoodenHouse_1", ang = 255f, dist = 30f, s = 0.9f },
                new { name = "WoodenHouse_2", ang = 170f, dist = 38f, s = 0.8f }
            };

            int placed = 0;
            foreach (var b in plan)
            {
                var prefab = LoadPrefab(TownDir + "/" + b.name + ".prefab");
                if (prefab == null) continue;

                float rad = b.ang * Mathf.Deg2Rad;
                float ox = Mathf.Sin(rad) * b.dist;
                float oz = Mathf.Cos(rad) * b.dist;
                float wx = Village.x + ox;
                float wz = Village.y + oz;
                if (wx < 5f || wx > MapSize - 5f || wz < 5f || wz > MapSize - 5f) continue;

                float h = WorldSampleHeight(wx, wz);
                var go = SpawnAt(root.transform, prefab, new Vector3(wx, h, wz),
                                 -b.ang + 180f, b.s);
                if (go != null) placed++;
            }

            // A few village props / storage barrels to give the square some life.
            var props = new[] { "cart1", "storage_barrel", "Furniture_table" };
            for (int i = 0; i < props.Length; i++)
            {
                var prefab = LoadPrefab(TownDir + "/" + props[i] + ".prefab");
                if (prefab == null) continue;
                float rad = (float)(i) / props.Length * 360f * Mathf.Deg2Rad;
                float rr = 10f + 6f * i;
                float wx = Village.x + Mathf.Sin(rad) * rr;
                float wz = Village.y + Mathf.Cos(rad) * rr;
                float h = WorldSampleHeight(wx, wz);
                SpawnAt(root.transform, prefab, new Vector3(wx, h, wz), rad * Mathf.Rad2Deg, 1f);
            }
            Log("Village placed (" + placed + " buildings).");
        }

        static GameObject SpawnAt(Transform parent, GameObject prefab, Vector3 pos, float yaw, float scale)
        {
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) go = (GameObject)Object.Instantiate(prefab);
            if (go == null) return null;

            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        static List<GameObject> LoadMany(string dir, string[] names)
        {
            var list = new List<GameObject>();
            foreach (var n in names)
            {
                var p = LoadPrefab(dir + "/" + n + ".prefab");
                if (p != null) list.Add(p);
            }
            return list;
        }
        // =========================================================================
        //  Scattered ground cover (grass tufts, ferns, flowers, rocks...)
        // =========================================================================
        static readonly System.Random R = new System.Random(20240601);

        static void CreateScatter()
        {
            var root = new GameObject("Nature Props");
            root.transform.position = Vector3.zero;

            // Preload prefab pools once (avoids thousands of disk loads).
            var tall = LoadMany(PropsDir, new[] { "Grass1", "Grass2", "Grass3", "Grass4", "Rye" });
            var lowPlants = LoadMany(PropsDir, new[] { "Fern1", "Fern2", "Fern3", "Plant1", "Plant2", "Plant3", "Sunflower" });
            var flowers = LoadMany(PropsDir, new[] { "Flower1", "Flower2", "Flower3", "Flower4", "Flower5", "Flower6", "Flower7", "Flower8", "Flower9" });
            var bushes = LoadMany(PropsDir, new[] { "Bush1" });
            var mushrooms = LoadMany(PropsDir, new[] { "Mushroom1", "Mushroom2", "Mushroom3", "Mushroom4", "Mushroom5" });
            var rocks = LoadMany(PropsDir, new[] { "Rock1", "Rock2", "Rock3", "Stone1", "Stone2", "Stone3" });

            int total = 0;
            total += ScatterPlants(root.transform, tall, 900, 0.7f, 1.6f,
                                   (x, z) => AllowField(x, z, 32f, false));
            total += ScatterPlants(root.transform, lowPlants, 700, 0.8f, 1.5f,
                                   (x, z) => AllowField(x, z, 30f, false));
            total += ScatterPlants(root.transform, flowers, 550, 0.7f, 1.4f,
                                   (x, z) => AllowField(x, z, 28f, true));
            total += ScatterPlants(root.transform, bushes, 160, 0.8f, 1.6f,
                                   (x, z) => AllowField(x, z, 36f, false));
            total += ScatterPlants(root.transform, mushrooms, 220, 0.8f, 1.5f,
                                   (x, z) => AllowForest(x, z, 30f));
            total += ScatterPlants(root.transform, rocks, 320, 0.6f, 2.2f,
                                   (x, z) => AllowRock(x, z));

            // Streamed activation: only props within the chosen render distance are alive.
            var streamer = root.AddComponent<PropStreamer>();
            streamer.radius = PropRadius();
            var cam = GameObject.FindGameObjectWithTag("MainCamera");
            if (cam != null) streamer.target = cam.transform;

            // Deactivate far props right away so the editor and Game view stay light.
            var spawn3 = new Vector3(Spawn.x, 0f, Spawn.y);
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var ch = root.transform.GetChild(i);
                bool near = (ch.position - spawn3).sqrMagnitude <= (streamer.radius * streamer.radius);
                if (!near) ch.gameObject.SetActive(false);
            }

            Log("Scattered props total: " + total);
        }
        static int ScatterPlants(Transform parent, List<GameObject> pool, int count,
                                 float sMin, float sMax, System.Func<float, float, bool> allow)
        {
            if (pool == null || pool.Count == 0) return 0;
            const int margin = 15;
            int placed = 0, guard = 0;
            while (placed < count && guard < count * 12)
            {
                guard++;
                float x = margin + (float)R.NextDouble() * (MapSize - margin * 2f);
                float z = margin + (float)R.NextDouble() * (MapSize - margin * 2f);
                if (!allow(x, z)) continue;

                var prefab = pool[(int)(R.NextDouble() * pool.Count)];
                float h = WorldSampleHeight(x, z);
                float yaw = (float)(R.NextDouble() * 360f);
                float s = sMin + (float)R.NextDouble() * (sMax - sMin);
                if (SpawnAt(parent, prefab, new Vector3(x, h, z), yaw, s) != null) placed++;
            }
            return placed;
        }

        static float SlopeAtWorld(float x, float z)
        {
            int hx = Mathf.Clamp(Mathf.RoundToInt(x / _spacing), 0, HeightRes - 1);
            int hz = Mathf.Clamp(Mathf.RoundToInt(z / _spacing), 0, HeightRes - 1);
            return SlopeAt(hx, hz);
        }

        static bool AllowField(float x, float z, float maxSlope, bool openOnly)
        {
            float rd = RoadDistance(x, z);
            if (rd < RoadHalfWidth + 4f) return false;
            if (Vector2.Distance(new Vector2(x, z), Village) < PlazaRadius + 3f) return false;
            if (DistanceToSpawn(x, z) < 8f) return false;
            if (SlopeAtWorld(x, z) > maxSlope) return false;
            if (openOnly)
            {
                float forest = Mathf.PerlinNoise(x * 0.0009f + 31f, z * 0.0009f + 6f);
                if (forest > 0.62f) return false;      // flowers prefer open clearings
            }
            return true;
        }

        static bool AllowForest(float x, float z, float maxSlope)
        {
            float rd = RoadDistance(x, z);
            if (rd < RoadHalfWidth + 4f) return false;
            if (Vector2.Distance(new Vector2(x, z), Village) < PlazaRadius + 3f) return false;
            if (DistanceToSpawn(x, z) < 8f) return false;
            if (SlopeAtWorld(x, z) > maxSlope) return false;
            float forest = Mathf.PerlinNoise(x * 0.0009f + 31f, z * 0.0009f + 6f);
            return forest > 0.58f;                       // mushrooms like the woods
        }

        static bool AllowRock(float x, float z)
        {
            float rd = RoadDistance(x, z);
            if (rd < RoadHalfWidth + 4f) return false;
            if (Vector2.Distance(new Vector2(x, z), Village) < PlazaRadius + 3f) return false;
            if (DistanceToSpawn(x, z) < 10f) return false;
            if (x < 15f || x > MapSize - 15f || z < 15f || z > MapSize - 15f) return false;
            return true;                                  // rocks live anywhere, esp. slopes
        }

        // =========================================================================
        //  Live tools used by the "Map Settings" window (apply to the open scene)
        // =========================================================================
        public static Terrain FindSceneTerrain()
        {
            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            foreach (var t in terrains)
                if (t.gameObject.name == "Terrain") return t;
            return terrains.Length > 0 ? terrains[0] : null;
        }

        public static void ApplyAtmosphereToScene(float farClip, float fogDensity)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = Mathf.Max(0.0005f, fogDensity);
            RenderSettings.fogColor = new Color(0.58f, 0.68f, 0.70f);
            RenderSettings.skybox = null;

            var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var c in cams)
            {
                c.farClipPlane = Mathf.Max(30f, farClip);
                c.clearFlags = CameraClearFlags.SolidColor;
                c.backgroundColor = RenderSettings.fogColor;
            }

            var terrain = FindSceneTerrain();
            if (terrain != null) ApplyTerrainRenderDistances(terrain, farClip);

            // Match the prop-streaming radius so scenery appears up to the chosen range.
            var streamers = Object.FindObjectsByType<PropStreamer>(FindObjectsSortMode.None);
            foreach (var s in streamers) s.radius = Mathf.Max(40f, farClip * 0.8f);

            EditorPrefs.SetFloat("MM.RenderDist", farClip);
            EditorPrefs.SetFloat("MM.FogDensity", fogDensity);
            Log("Applied atmosphere: far=" + farClip + " fog=" + fogDensity);
        }

        public static void BakeGrassInScene(float density, float scale)
        {
            var terrain = FindSceneTerrain();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogWarning("No Terrain in the open scene. Build the map first.");
                return;
            }
            _terrain = terrain;
            _td = terrain.terrainData;

            int hres = _td.heightmapResolution;
            _spacing = _td.size.x / (hres - 1);
            float[,] norm = _td.GetHeights(0, 0, hres, hres);
            _h2 = new float[hres, hres];
            for (int z = 0; z < hres; z++)
                for (int x = 0; x < hres; x++)
                    _h2[z, x] = norm[z, x] * _td.size.y;

            EditorPrefs.SetFloat("MM.GrassDensity", density);
            EditorPrefs.SetFloat("MM.GrassScale", scale);
            ApplyGrassDetails();
            EditorUtility.SetDirty(_td);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log("Grass re-baked: density=" + density + " scale=" + scale);
        }
        public static int AddPropsToScene(string group, int count)
        {
            var terrain = FindSceneTerrain();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogWarning("No Terrain in the open scene. Build the map first.");
                return 0;
            }
            _terrain = terrain;

            string[] names;
            float sMin, sMax;
            if (group == "stones" || group == "rocks")
            {
                names = new[] { "Rock1", "Rock2", "Rock3", "Stone1", "Stone2", "Stone3" };
                sMin = 0.6f; sMax = 2.2f;
            }
            else if (group == "flowers")
            {
                names = new[] { "Flower1", "Flower2", "Flower3", "Flower4", "Flower5", "Flower6", "Flower7", "Flower8", "Flower9" };
                sMin = 0.7f; sMax = 1.4f;
            }
            else if (group == "plants")
            {
                names = new[] { "Fern1", "Fern2", "Fern3", "Plant1", "Plant2", "Plant3", "Sunflower", "Bush1" };
                sMin = 0.8f; sMax = 1.6f;
            }
            else // grass tufts
            {
                names = new[] { "Grass1", "Grass2", "Grass3", "Grass4", "Rye" };
                sMin = 0.9f; sMax = 2.2f;
            }

            var pool = LoadMany(PropsDir, names);
            if (pool.Count == 0)
            {
                Debug.LogWarning("No prefabs found for group '" + group + "'.");
                return 0;
            }

            var root = GameObject.Find("Nature Props");
            if (root == null)
            {
                root = new GameObject("Nature Props");
                root.transform.position = Vector3.zero;
                var streamer = root.AddComponent<PropStreamer>();
                streamer.radius = PropRadius();
                var cam = GameObject.FindGameObjectWithTag("MainCamera");
                if (cam != null) root.GetComponent<PropStreamer>().target = cam.transform;
            }

            const int margin = 20;
            int placed = 0, guard = 0;
            while (placed < count && guard < count * 15)
            {
                guard++;
                float x = margin + (float)R.NextDouble() * (MapSize - margin * 2f);
                float z = margin + (float)R.NextDouble() * (MapSize - margin * 2f);

                float rd = RoadDistance(x, z);
                if (rd < RoadHalfWidth + 4f) continue;
                if (Vector2.Distance(new Vector2(x, z), Village) < PlazaRadius + 3f) continue;

                float h = _terrain.SampleHeight(new Vector3(x, 0f, z));
                float yaw = (float)(R.NextDouble() * 360f);
                float s = sMin + (float)R.NextDouble() * (sMax - sMin);
                var prefab = pool[(int)(R.NextDouble() * pool.Count)];
                if (SpawnAt(root.transform, prefab, new Vector3(x, h, z), yaw, s) != null) placed++;
            }
            EditorUtility.SetDirty(root);
            Log("Added " + placed + " '" + group + "' props.");
            return placed;
        }

        // ---- Magic aura + grass color ------------------------------------------
        public static void CreateMagicAuraInScene(Color color, float intensity, float radius,
                                                   float softness, bool followPlayer, bool enabled)
        {
            string auraName = "Magic Aura";
            var go = GameObject.Find(auraName);
            if (go == null)
            {
                go = new GameObject(auraName);
                go.transform.position = new Vector3(Village.x, 0f, Village.y); // over the village
            }

            var mc = go.GetComponent<MagicAura>();
            if (mc == null) mc = go.AddComponent<MagicAura>();
            mc.color = color;
            mc.intensity = intensity;
            mc.radius = radius;
            mc.softness = softness;
            mc.centerHeight = 1.5f;
            mc.followPlayer = followPlayer;

            // Make sure the glow disc + material exist so it shows in the Scene view.
            Material mat = LoadOrCreateAuraMaterial();
            Transform disc = go.transform.Find("GlowDisc");
            if (disc == null)
            {
                var quadGo = CreateAuraQuad();
                quadGo.transform.SetParent(go.transform, false);
                quadGo.name = "GlowDisc";
                disc = quadGo.transform;
            }
            var r = disc.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = mat;
            if (mat != null)
            {
                mat.SetColor("_Color", color);
                mat.SetFloat("_Intensity", Mathf.Max(0f, intensity));
                mat.SetFloat("_Falloff", Mathf.Max(0.05f, softness));
            }
            disc.localScale = new Vector3(radius * 2f, radius * 2f, 1f);

            go.SetActive(enabled);
            EditorUtility.SetDirty(go);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            Log("Magic aura " + (enabled ? "created/updated" : "hidden") + ".");
        }

        static Material LoadOrCreateAuraMaterial()
        {
            string path = GeneratedDir + "/MagicAuraMat.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader sh = Shader.Find("MysticMap/MagicAura");
            if (sh == null) return null;
            var mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static GameObject CreateAuraQuad()
        {
            var go = new GameObject("GlowDisc");
            var mf = go.AddComponent<MeshFilter>();
            var mesh = new Mesh();
            mesh.name = "AuraQuad";
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mf.mesh = mesh;
            go.AddComponent<MeshRenderer>();
            return go;
        }

        public static void SetGrassVisualInScene(Color baseColor, float brightness)
        {
            var terrain = FindSceneTerrain();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogWarning("No Terrain in the open scene. Build the map first.");
                return;
            }
            var td = terrain.terrainData;
            var protos = td.detailPrototypes;
            Color healthy = Bright(baseColor, brightness);
            Color dry = Bright(new Color(baseColor.r * 0.72f, baseColor.g * 0.85f, baseColor.b * 0.65f), brightness * 0.9f);
            for (int i = 0; i < protos.Length; i++)
            {
                protos[i].healthyColor = healthy;
                protos[i].dryColor = dry;
            }
            td.detailPrototypes = protos;
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
            Log("Grass color applied: " + baseColor + " brightness=" + brightness);
        }

        static Color Bright(Color c, float mul)
        {
            return new Color(
                Mathf.Clamp01(c.r * mul),
                Mathf.Clamp01(c.g * mul),
                Mathf.Clamp01(c.b * mul),
                1f);
        }

        // Glow applied to the grass sprites only (not roads/bare terrain). Removes the old ground blob.
        public static void SetGrassGlowInScene(Color glowColor, float strength, bool on)
        {
            var oldBlob = GameObject.Find("Magic Aura");
            if (oldBlob != null) Object.DestroyImmediate(oldBlob);

            var terrain = FindSceneTerrain();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogWarning("No Terrain in the open scene. Build the map first.");
                return;
            }
            var td = terrain.terrainData;

            Color baseColor = PrefColor("MM.GrassColor", new Color(0.35f, 0.9f, 0.3f, 1f));
            float bright = EditorPrefs.GetFloat("MM.GrassBright", 1.3f);

            Color normal = Bright(baseColor, bright);
            Color normalDry = Bright(new Color(baseColor.r * 0.72f, baseColor.g * 0.85f, baseColor.b * 0.65f), bright * 0.9f);

            float t = on ? Mathf.Clamp01(strength) : 0f;
            Color glow = Bright(glowColor, 1f + strength);
            Color glowDry = Bright(new Color(glowColor.r * 0.55f, glowColor.g * 0.7f, glowColor.b * 0.55f), 1f + strength * 0.7f);

            var protos = td.detailPrototypes;
            for (int i = 0; i < protos.Length; i++)
            {
                protos[i].healthyColor = Color.Lerp(normal, glow, t);
                protos[i].dryColor = Color.Lerp(normalDry, glowDry, t);
            }
            td.detailPrototypes = protos;
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();
            Log("Grass glow " + (on ? "applied (" + glowColor + " strength=" + strength + ")" : "off"));
        }

        static Color PrefColor(string key, Color def)
        {
            if (!EditorPrefs.HasKey(key + ".R")) return def;
            return new Color(
                EditorPrefs.GetFloat(key + ".R", def.r),
                EditorPrefs.GetFloat(key + ".G", def.g),
                EditorPrefs.GetFloat(key + ".B", def.b),
                EditorPrefs.GetFloat(key + ".A", def.a));
        }

        // ---- Grass field glow (soft ground glows, only where grass grows) ------
        public static void CreateGrassGlowFieldInScene(Color color, float strength, float spacing, bool enabled)
        {
            RemoveGrassGlowField();
            if (!enabled) return;

            var terrain = FindSceneTerrain();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogWarning("No Terrain in the open scene. Build the map first.");
                return;
            }
            _terrain = terrain;

            Material mat = LoadOrCreateAuraMaterial();
            if (mat == null)
            {
                Debug.LogWarning("MagicAura shader not found.");
                return;
            }

            var root = new GameObject("Grass Glow Field");
            var orbs = root.AddComponent<GlowOrbs>();
            orbs.radius = Mathf.Max(60f, EditorPrefs.GetFloat("MM.RenderDist", 130f) * 0.7f);
            orbs.pulseSpeed = 1.1f;
            orbs.pulseAmount = 0.06f;
            var cam = GameObject.FindGameObjectWithTag("MainCamera");
            if (cam != null) orbs.target = cam.transform;

            float step = Mathf.Max(8f, spacing);
            int placed = 0;
            for (float x = 15f; x < MapSize - 15f; x += step)
            {
                for (float z = 15f; z < MapSize - 15f; z += step)
                {
                    // Jitter each point so glows never line up into a visible grid.
                    float jx = x + ((float)R.NextDouble() - 0.5f) * step * 0.95f;
                    float jz = z + ((float)R.NextDouble() - 0.5f) * step * 0.95f;
                    if (!IsGrassRegion(jx, jz)) continue;

                    float ground = terrain.SampleHeight(new Vector3(jx, 0f, jz));
                    float variation = 0.6f + (float)R.NextDouble() * 0.8f;   // per-orb strength variation
                    float radius = step * 0.18f * (0.9f + (float)R.NextDouble() * 0.6f);

                    var child = CreateAuraQuad();
                    child.name = "O";
                    child.transform.SetParent(root.transform, false);
                    float cy = ground + radius * 0.6f;       // low, sitting on the grass base
                    child.transform.position = new Vector3(jx, cy, jz);
                    child.transform.rotation = Quaternion.identity;   // GlowOrbs billboards it every frame
                    float d = radius * 2f;
                    child.transform.localScale = new Vector3(d, d * 0.6f, 1f);   // a low, rounded glow

                    var rend = child.GetComponent<MeshRenderer>();
                    if (rend != null)
                    {
                        rend.sharedMaterial = mat;
                        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        rend.receiveShadows = false;
                        var pb = new MaterialPropertyBlock();
                        pb.SetColor("_Color", color);
                        pb.SetFloat("_Intensity", Mathf.Max(0.001f, strength * variation));
                        pb.SetFloat("_Falloff", 3f);
                        rend.SetPropertyBlock(pb);
                    }
                    placed++;
                }
            }

            // Keep the editor light: leave only the glows near spawn active; the streamer
            // wakes the rest up as the player approaches during Play.
            Vector3 spawnPt = new Vector3(Spawn.x, 0f, Spawn.y);
            float nearR = orbs.radius;
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var ch = root.transform.GetChild(i);
                if ((ch.position - spawnPt).sqrMagnitude > nearR * nearR)
                    ch.gameObject.SetActive(false);
            }

            EditorUtility.SetDirty(root);
            Log("Grass glow field placed: " + placed + " patches (strength=" + strength + " spacing=" + spacing + ")");
        }

        public static void RemoveGrassGlowField()
        {
            var old = GameObject.Find("Grass Glow Field");
            if (old != null) Object.DestroyImmediate(old);
            var oldBlob = GameObject.Find("Magic Aura");   // remove any leftover old glow blob too
            if (oldBlob != null) Object.DestroyImmediate(oldBlob);
        }

        // ---- Glow attached to the actual grass sprites (goes with the grass) ------
        static Mesh _auraQuadMesh;
        static Mesh AuraQuadMesh()
        {
            if (_auraQuadMesh != null) return _auraQuadMesh;
            _auraQuadMesh = new Mesh();
            _auraQuadMesh.name = "AuraQuad";
            _auraQuadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            };
            _auraQuadMesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            _auraQuadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            _auraQuadMesh.RecalculateNormals();
            return _auraQuadMesh;
        }

        static Material LoadOrCreateGlowMaterial()
        {
            string path = GeneratedDir + "/MagicGlowMat.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            Shader sh = Shader.Find("MysticMap/BillboardGlow");
            if (sh == null) return null;
            var mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        public static void CreateGrassSpriteGlowsInScene(Color color, float strength, float size, float height, bool enabled)
        {
            RemoveGrassGlowField();   // drop the old floating/orb glow first
            if (!enabled) return;

            var terrain = FindSceneTerrain();
            Material mat = LoadOrCreateGlowMaterial();
            if (mat == null)
            {
                Debug.LogWarning("BillboardGlow shader not found.");
                return;
            }

            string[] roots = { "Nature Props", "Painted Props" };
            int attached = 0;
            foreach (var rootName in roots)
            {
                var root = GameObject.Find(rootName);
                if (root == null) continue;
                int n = root.transform.childCount;
                for (int i = 0; i < n; i++)
                {
                    var prop = root.transform.GetChild(i);
                    if (prop == null) continue;

                    // Remove any glow already attached to this prop (so re-applying is clean).
                    var oldGlow = prop.Find("GlowSprite");
                    if (oldGlow != null) Object.DestroyImmediate(oldGlow.gameObject);

                    float gx = prop.position.x, gz = prop.position.z;
                    float ground = 0f;
                    if (terrain != null) ground = terrain.SampleHeight(new Vector3(gx, 0f, gz));

                    float variation = 0.7f + (float)R.NextDouble() * 0.7f;  // only brightness varies per sprite
                    // Glow width follows this grass's painted size (bigger grass -> wider glow).
                    // The "height above grass" stays uniform because the billboard shader
                    // ignores object scale, so we compute the width from the prop's scale.
                    float scl = Mathf.Max(0.3f, Mathf.Max(prop.lossyScale.x, prop.lossyScale.y));
                    float dia = Mathf.Max(0.15f, size * scl);              // world diameter, matches grass

                    var glow = new GameObject("GlowSprite");
                    glow.transform.SetParent(prop, false);
                    float gy = Mathf.Max(0.02f, height) + ground;           // height above the ground
                    glow.transform.position = new Vector3(gx, gy, gz);

                    var mf = glow.AddComponent<MeshFilter>();
                    mf.sharedMesh = AuraQuadMesh();
                    var mr = glow.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;

                    var pb = new MaterialPropertyBlock();
                    pb.SetColor("_Color", color);
                    pb.SetFloat("_Intensity", Mathf.Max(0.001f, strength * variation));
                    pb.SetFloat("_Falloff", 3f);
                    pb.SetFloat("_Size", dia);
                    pb.SetFloat("_Aspect", 0.7f);
                    mr.SetPropertyBlock(pb);
                    attached++;
                }
                if (root != null) EditorUtility.SetDirty(root);
            }
            Log("Grass glow attached to " + attached + " sprites (strength=" + strength + " size=" + size + " height=" + height + ").");
        }

        /// <summary>
        /// Deletes the per-prop "GlowSprite" children that were added to every grass tuft /
        /// plant / stone on the map. This is what actually removes the tens of thousands of
        /// glow quads and shrinks the (huge) scene file. The grass-glow FEATURE is kept - the
        /// settings and toggle remain in Map Settings, and it is set to OFF so auto-restore
        /// does not instantly re-add the glows the next time you leave Play mode.
        /// </summary>
        public static void RemoveGrassSpriteGlows()
        {
            string[] roots = { "Nature Props", "Painted Props" };
            int removed = 0;
            foreach (var rootName in roots)
            {
                var root = GameObject.Find(rootName);
                if (root == null) continue;
                Transform props = root.transform;
                for (int i = props.childCount - 1; i >= 0; i--)
                {
                    var prop = props.GetChild(i);
                    if (prop == null) continue;
                    var glow = prop.Find("GlowSprite");
                    if (glow == null) continue;
                    Object.DestroyImmediate(glow.gameObject);
                    removed++;
                }
                EditorUtility.SetDirty(root);
            }

            // Don't let auto-restore instantly re-add them next time you leave Play.
            EditorPrefs.SetBool("MM.GlowOn", false);

            // Drop the old orb-field root too if it lingers.
            RemoveGrassGlowField();

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Log("Removed " + removed + " grass glow sprites (feature kept, ready to re-enable).");
        }

        [MenuItem("MCP/Remove grass glows from the map (keep feature)")]
        static void RemoveGrassGlowsMenu()
        {
            RemoveGrassSpriteGlows();
        }

        // ---- Floating magical dust (rises from the ground near the player) ---------
        public static void CreateMagicalDustInScene(Color color, bool enabled, int count, float radius,
                                                    float size, float speed, float glow,
                                                    float pulseAmount, float pulseSpeed,
                                                    bool illuminate, float lightStrength)
        {
            RemoveMagicalDust();
            if (!enabled) return;

            var root = new GameObject("Magical Dust");
            root.transform.position = new Vector3(Village.x, 0f, Village.y);

            var dust = root.AddComponent<MagicalDust>();
            dust.color = color;
            dust.glowStrength = Mathf.Max(0f, glow);
            dust.pulseAmount = Mathf.Clamp01(pulseAmount);
            dust.pulseSpeed = Mathf.Max(0f, pulseSpeed);
            dust.count = Mathf.Clamp(count, 1, 300);
            dust.radius = Mathf.Max(2f, radius);
            dust.particleSize = Mathf.Max(0.03f, size);
            dust.riseSpeed = Mathf.Max(0.05f, speed);
            dust.illuminate = illuminate;
            dust.lightStrength = Mathf.Clamp(lightStrength, 0f, 2f);

            var cam = GameObject.FindGameObjectWithTag("MainCamera");
            if (cam != null) dust.target = cam.transform;

            EditorUtility.SetDirty(root);
            Log("Magical dust added: count=" + count + " radius=" + radius + " size=" + size +
                " speed=" + speed + " glow=" + glow + " pulse=" + pulseAmount + " pulseSpeed=" + pulseSpeed);
        }

        public static void RemoveMagicalDust()
        {
            var old = GameObject.Find("Magical Dust");
            if (old != null) Object.DestroyImmediate(old);
        }

        // Cap how far away we render grass and the glow under it (power saving).
        public static void ApplyGrassDistancesToScene(float grassDist, float glowDist)
        {
            grassDist = Mathf.Clamp(grassDist, 5f, 600f);
            glowDist = Mathf.Clamp(glowDist, 5f, 600f);

            // 1) Stop drawing far terrain grass - the big, expensive "grass field".
            var terrain = FindSceneTerrain();
            if (terrain != null)
            {
                terrain.detailObjectDistance = grassDist;
                EditorUtility.SetDirty(terrain);
            }

            // 2) Grass / ground-cover props + their glow under them.
            string[] roots = { "Nature Props", "Painted Props" };
            var cam = GameObject.FindGameObjectWithTag("MainCamera");
            Vector3 camPos = (cam != null) ? cam.transform.position : new Vector3(Spawn.x, 0f, Spawn.y);

            foreach (var rn in roots)
            {
                var root = GameObject.Find(rn);
                if (root == null) continue;

                var ps = root.GetComponent<PropStreamer>();
                if (ps == null) ps = root.AddComponent<PropStreamer>();
                ps.radius = grassDist;
                if (cam != null) ps.target = cam.transform;

                var gc = root.GetComponent<GlowDistanceCuller>();
                if (gc == null) gc = root.AddComponent<GlowDistanceCuller>();
                gc.radius = glowDist;
                if (cam != null) gc.target = cam.transform;

                // Reflect the change immediately (even in the Scene view), not only after
                // pressing Play. PropStreamer keeps this updated as you move in Play mode.
                float sqrG = grassDist * grassDist;
                float sqrGl = glowDist * glowDist;
                int n = root.transform.childCount;
                for (int i = 0; i < n; i++)
                {
                    var ch = root.transform.GetChild(i);
                    if (ch == null) continue;
                    float sqr = (ch.position - camPos).sqrMagnitude;
                    bool on = sqr <= sqrG;
                    if (ch.gameObject.activeSelf != on) ch.gameObject.SetActive(on);
                    if (on)
                    {
                        var glow = ch.Find("GlowSprite");
                        if (glow != null)
                            glow.gameObject.SetActive(sqr <= sqrGl);
                    }
                }
            }

            EditorPrefs.SetFloat("MM.GrassDist", grassDist);
            EditorPrefs.SetFloat("MM.GlowDist", glowDist);
            Log("Applied grass distances: grass=" + grassDist + "m glow=" + glowDist + "m.");
        }

        // Where is grass allowed to glow? Everywhere the grass grows - including inside the
        // town - except the roads and the paved market square.
        static bool IsGrassRegion(float x, float z)
        {
            float rd = RoadDistance(x, z);
            if (rd < RoadHalfWidth + 3f) return false;
            if (Vector2.Distance(new Vector2(x, z), Village) < PlazaRadius + 2f) return false;
            if (x < 12f || x > MapSize - 12f || z < 12f || z > MapSize - 12f) return false;
            return true;
        }

    }
}
#endif
