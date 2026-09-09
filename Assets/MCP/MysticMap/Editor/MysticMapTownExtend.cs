#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    public static partial class MysticMapBuilder
    {
        // ---------------------------------------------------------------------
        //  Additive "town" pass, run in-place on the already-built MysticMap scene.
        //  It does NOT regenerate the walls/towers/gates (those are left untouched),
        //  only: (1) the road network + ground splat, (2) trees/grass/plants get
        //  cleared from every road, and (3) the town interior is re-filled with a
        //  ring of houses hugging the walls plus significant landmark buildings in
        //  the centre.
        // ---------------------------------------------------------------------
        public static void ExtendTownAndRoads()
        {
            const string scenePath = "Assets/Scenes/MysticMap.unity";
            if (!System.IO.File.Exists(scenePath))
            {
                Debug.LogWarning("[MysticMap] Scene not found: " + scenePath);
                return;
            }
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var terrain = FindSceneTerrain();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogWarning("[MysticMap] No Terrain in the open scene. Build the map first.");
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

            SetExtendedRoads();

            Log("Repainting ground splat with the extended road network...");
            ApplySplatmap();
            Log("Regenerating grass + trees (clear of roads)...");
            ApplyGrassDetails();
            ApplyTrees();
            RemoveScatterFromRoads();
            EditorUtility.SetDirty(_td);

            RebuildTownHousing();

            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log("Town housing + extended road network complete.");
        }

        // Roads that run through the town and then reach out toward the edges of the map,
        // all painted with the road terrain layer. The two country roads per axis spread
        // far from town so the road network visibly expands to the outer map.
        static void SetExtendedRoads()
        {
            var roads = new List<Vector2[]>();
            float vx = Village.x, vz = Village.y;
            const float Lo = 30f, Hi = 1000f - 30f;

            // Main north-south road: runs through the fortress gates to both map edges.
            roads.Add(new[] { new Vector2(vx, Lo), new Vector2(vx, Hi) });

            // Countryside roads (north/south of town) reaching the east/west map edges.
            roads.Add(new[] { new Vector2(Lo, vz - 350f), new Vector2(Hi, vz - 350f) });
            roads.Add(new[] { new Vector2(Lo, vz + 350f), new Vector2(Hi, vz + 350f) });

            // Countryside roads (west/east of town) reaching the north/south map edges.
            roads.Add(new[] { new Vector2(vx - 315f, Lo), new Vector2(vx - 315f, Hi) });
            roads.Add(new[] { new Vector2(vx + 315f, Lo), new Vector2(vx + 315f, Hi) });

            // Interior east-west avenue inside the town (stops short of the solid side walls).
            roads.Add(new[] { new Vector2(vx - (FortHX - 6f), vz), new Vector2(vx + (FortHX - 6f), vz) });

            _roadPolys = roads;
        }

        // Any scattered nature prop that now sits on a road, or inside the town, is removed
        // so roads stay clean and the town interior reads as built-up ground.
        static void RemoveScatterFromRoads()
        {
            var root = GameObject.Find("Nature Props");
            if (root == null) return;

            var kill = new List<GameObject>();
            foreach (Transform c in root.transform)
            {
                float px = c.position.x, pz = c.position.z;
                bool onRoad = RoadDistance(px, pz) < RoadHalfWidth + 4f;

                float ax = Mathf.Abs(px - Village.x) - (FortHX + 6f);
                float az = Mathf.Abs(pz - Village.y) - (FortHZ + 6f);
                bool inTown = ax < 0f && az < 0f;

                if (onRoad || inTown) kill.Add(c.gameObject);
            }
            foreach (var g in kill)
                if (g != null) Object.DestroyImmediate(g);
            if (kill.Count > 0) Log("Removed " + kill.Count + " scatter props from roads/town.");
        }

        [MenuItem("MCP/Roads/Clear grass & plants from roads")]
        public static void ClearGrassFromRoadsMenu()
        {
            var scene = EditorSceneManager.GetActiveScene();
            CleanGrassFromRoads(scene);
        }

        public static void ClearGrassFromRoads()   // batch entry point
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MysticMap.unity", OpenSceneMode.Single);
            CleanGrassFromRoads(scene);
        }

        // Regenerates the terrain grass detail + trees and drops any leftover scatter props so
        // a comfortable grass/plant-free shoulder exists around every road.
        static void CleanGrassFromRoads(Scene scene)
        {
            var terrain = FindSceneTerrain();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogWarning("[MysticMap] No Terrain found.");
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

            SetExtendedRoads();
            ApplyGrassDetails();
            ApplyTrees();
            RemoveScatterFromRoads();
            EditorUtility.SetDirty(_td);
            EditorSceneManager.SaveScene(scene, scene.path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log("Regenerated grass + trees clear of all roads (clean shoulder).");
        }
        // ---- Town interior: central landmarks + wall-aligned houses ------------------
        static void RebuildTownHousing()
        {
            var root = GameObject.Find("FortressTown");
            if (root == null)
            {
                Log("No FortressTown found; leaving town interior as-is.");
                return;
            }
            RemoveGroupChild(root.transform, "Buildings");
            RemoveGroupChild(root.transform, "Houses");
            RemoveGroupChild(root.transform, "TownCore");

            // Derive the fortress rectangle from the existing tower positions so the house
            // rows hug whatever wall positions are in the scene now.
            float x0 = Village.x - FortHX, x1 = Village.x + FortHX;
            float z0 = Village.y - FortHZ, z1 = Village.y + FortHZ;
            var towers = root.transform.Find("Towers");
            if (towers != null && towers.childCount > 0)
            {
                bool first = true;
                foreach (Transform t in towers)
                {
                    if (first) { x0 = x1 = t.position.x; z0 = z1 = t.position.z; first = false; }
                    else
                    {
                        x0 = Mathf.Min(x0, t.position.x); x1 = Mathf.Max(x1, t.position.x);
                        z0 = Mathf.Min(z0, t.position.z); z1 = Mathf.Max(z1, t.position.z);
                    }
                }
            }
            float cx = (x0 + x1) * 0.5f;
            float cz = (z0 + z1) * 0.5f;

            var core = Child(root.transform, "TownCore");
            var houses = Child(root.transform, "Houses");

            var placed = new List<Bounds>();
            CollectFortressStructures(root.transform, placed); // keep new houses off every wall/tower/gate

            PlaceCoreLandmarks(core, cx, cz, placed);
            PlaceWallHouses(houses, placed, cx, cz, x0, x1, z0, z1);
            Log("Town interior re-filled: central landmarks + houses hugging the walls.");
        }

        static void RemoveGroupChild(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        static void CollectFortressStructures(Transform root, List<Bounds> placed)
        {
            foreach (var groupName in new[] { "Walls", "Towers", "Gates" })
            {
                var grp = root.Find(groupName);
                if (grp == null) continue;
                foreach (Transform c in grp)
                {
                    var go = c.gameObject;
                    if (go != null) placed.Add(CombinedBounds(go));
                }
            }
        }

        static void PlaceCoreLandmarks(Transform parent, float cx, float cz, List<Bounds> placed)
        {
            // The "significant" buildings live in the middle: an inn as the town hall, a
            // couple of tall windmills, and one large wooden townhouse. Order matters: each
            // later one is skipped if it would overlap an earlier one, the plaza or a road.
            TryBld(parent, "Tavern",        cx - 24f, cz + 26f, 0f,  1.0f, placed);
            TryBld(parent, "Windmill",      cx + 28f, cz + 28f, 0f,  0.9f, placed);
            TryBld(parent, "Windmill",      cx - 28f, cz - 28f, 0f,  0.9f, placed);
            TryBld(parent, "WoodenHouse_2", cx + 24f, cz - 28f, 90f, 1.1f, placed);
        }
        // Houses packed in rows that run parallel to each wall and hug the inside of it.
        // Each house is oriented so its long side runs along the wall; a second, slightly
        // inner row behind it fills the block towards the centre.
        static void PlaceWallHouses(Transform parent, List<Bounds> placed,
                                    float cx, float cz, float x0, float x1, float z0, float z1)
        {
            string[] pool = { "House1", "House2", "House3", "WoodenHouse_1", "House2", "House1" };
            int pi = 0;
            const float rowGap = 9f;

            // North (wall at z1) and South (wall at z0) walls run along X.
            foreach (var wall in new[] { z1, z0 })
            {
                int inward = wall > cz ? -1 : 1;
                foreach (var extra in new[] { 0f, rowGap })
                {
                    for (float x = x0 + 9f; x <= x1 - 9f;)
                    {
                        string nm = pool[pi++ % pool.Length];
                        Vector2 c = Footprint(nm);
                        float depth = Mathf.Min(c.x, c.y);
                        float width = Mathf.Max(c.x, c.y);
                        float lane = wall + inward * (depth * 0.5f + 2.6f + extra);
                        float yaw = (c.x >= c.y) ? 0f : 90f; // long side along the wall (X)
                        TryBld(parent, nm, x, lane, yaw, 1f, placed);
                        x += width + 1.4f;
                    }
                }
            }

            // East (wall at x1) and West (wall at x0) walls run along Z.
            foreach (var wall in new[] { x1, x0 })
            {
                int inward = wall > cx ? -1 : 1;
                foreach (var extra in new[] { 0f, rowGap })
                {
                    for (float z = z0 + 9f; z <= z1 - 9f;)
                    {
                        string nm = pool[pi++ % pool.Length];
                        Vector2 c = Footprint(nm);
                        float depth = Mathf.Min(c.x, c.y);
                        float width = Mathf.Max(c.x, c.y);
                        float lane = wall + inward * (depth * 0.5f + 2.6f + extra);
                        float yaw = (c.y >= c.x) ? 0f : 90f; // long side along the wall (Z)
                        TryBld(parent, nm, lane, z, yaw, 1f, placed);
                        z += width + 1.4f;
                    }
                }
            }
        }

        static readonly Dictionary<string, Vector2> _fpCache = new Dictionary<string, Vector2>();

        static Vector2 Footprint(string prefabName)
        {
            if (_fpCache.TryGetValue(prefabName, out var c)) return c;
            Vector2 r = new Vector2(8f, 8f);
            var p = LoadPrefab(TownDir + "/" + prefabName + ".prefab");
            if (p != null)
            {
                var g = (GameObject)Object.Instantiate(p);
                g.transform.position = Vector3.zero;
                g.transform.rotation = Quaternion.identity;
                g.transform.localScale = Vector3.one;
                var b = CombinedBounds(g);
                Object.DestroyImmediate(g);
                r = new Vector2(Mathf.Max(1f, b.size.x), Mathf.Max(1f, b.size.z));
            }
            _fpCache[prefabName] = r;
            return r;
        }
    }
}
#endif
