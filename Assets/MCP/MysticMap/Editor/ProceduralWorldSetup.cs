#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MysticMap.World;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Puts the endless procedural world into the Mystic Map scene:
    ///   * adds a "Procedural World" object with the streamer + the town anchor,
    ///   * fills the prefab palette from the FantasyEnvironments folders,
    ///   * points the streamer at the player and makes sure the first-person controller is
    ///     switched on,
    ///   * offers an editor preview so the generated country can be inspected without
    ///     pressing Play.
    ///
    /// The town is never modified by this tool.
    /// </summary>
    public static class ProceduralWorldSetup
    {
        const string EnvDir = "Assets/FantasyEnvironments/Environments";
        const string TreeDir = EnvDir + "/Ambient-Occlusion-Trees/Prefabs";
        const string PropsDir = EnvDir + "/Prefabs";
        const string TownDir = EnvDir + "/Town/Prefabs";
        const string RootName = "Procedural World";

        // =====================================================================
        //  Menu
        // =====================================================================
        [MenuItem("MCP/Procedural World/2. Add the world to the scene")]
        public static void AddToSceneMenu()
        {
            var manager = Setup(true);
            if (manager == null) return;

            EditorUtility.DisplayDialog(
                "MysticMap",
                "The procedural world is in the scene.\n\n" +
                "Press Play and walk out of the north or south gate: the road continues into " +
                "endless procedural country. The town itself is untouched.\n\n" +
                "Tip: MCP > Procedural World > World Settings tunes the seed, the streaming " +
                "distance and the content.",
                "OK");
        }

        [MenuItem("MCP/Procedural World/3. Preview the world here")]
        public static void PreviewMenu()
        {
            var manager = Setup(false);
            if (manager == null) return;

            Vector3 pivot = SceneView.lastActiveSceneView != null
                ? SceneView.lastActiveSceneView.pivot
                : new Vector3(500f, 30f, 620f);

            manager.buildInEditor = true;
            manager.BuildAround(pivot, true);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            SceneView.RepaintAll();
            Debug.Log("[MysticMap.World] Preview built around " + pivot.ToString("0") +
                      " (" + manager.LiveChunks + " chunks, " + manager.LegacyChunksSkipped +
                      " skipped over the town).");
        }

        [MenuItem("MCP/Procedural World/Clear the preview")]
        public static void ClearPreviewMenu()
        {
            var manager = FindManager();
            if (manager == null) return;

            manager.ClearAll();
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            SceneView.RepaintAll();
            Debug.Log("[MysticMap.World] Preview cleared.");
        }

        // Builds a patch of the STREAMED world well OUTSIDE the 1 km hand-built map, where the
        // roads are the procedural (winding) ones, and points the Scene view at it. Nothing is
        // saved: the chunks are editor-only (DontSave) and the town is never touched.
        [MenuItem("MCP/Procedural World/4. Preview the curving roads (outside the map)")]
        public static void PreviewRoadsMenu()
        {
            var manager = Setup(false);
            if (manager == null) return;

            // North of the map (the map spans 0..1000 m). Past the forced straight gate corridor
            // and far enough from town that the roads are at full wander.
            Vector3 pivot = new Vector3(500f, 40f, 1400f);

            int oldRadius = manager.settings.viewRadiusChunks;
            manager.settings.viewRadiusChunks = Mathf.Max(oldRadius, 6);   // a bigger patch to look at
            manager.settings.Validate();
            manager.buildInEditor = true;
            try { manager.BuildAround(pivot, true); }
            finally { manager.settings.viewRadiusChunks = oldRadius; }

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

            var sv = SceneView.lastActiveSceneView;
            if (sv != null)
            {
                sv.pivot = pivot;
                sv.Frame(new Bounds(pivot, new Vector3(380f, 90f, 380f)), false);
                sv.Repaint();
            }

            Debug.Log("[MysticMap.World] Curving-road preview built around " + pivot.ToString("0") +
                      " (" + manager.LiveChunks + " chunks, " + manager.LegacyChunksSkipped +
                      " town chunks skipped). Move the Scene view to see more, then 'Clear the preview'.");
        }

        [MenuItem("MCP/Procedural World/Fill the prefab palette")]
        public static void FillPaletteMenu()
        {
            var manager = FindManager();
            if (manager == null)
            {
                Debug.LogWarning("[MysticMap.World] No world in the scene yet. Use menu 2 first.");
                return;
            }

            FillPalette(manager);
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Debug.Log("[MysticMap.World] Palette filled: " +
                      manager.settings.palette.trees.Length + " trees, " +
                      manager.settings.palette.grass.Length + " grass, " +
                      manager.settings.palette.flowers.Length + " flowers, " +
                      manager.settings.palette.rocks.Length + " rocks, " +
                      manager.settings.palette.walls.Length + " wall pieces.");
        }

        // =====================================================================
        //  Setup
        // =====================================================================
        static ChunkManager Setup(bool report)
        {
            var manager = FindManager();
            if (manager == null)
            {
                var go = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(go, "Add Procedural World");
                manager = go.AddComponent<ChunkManager>();
                go.transform.position = Vector3.zero;
            }

            var anchor = manager.GetComponent<TownAnchor>();
            if (anchor == null) anchor = manager.gameObject.AddComponent<TownAnchor>();

            // The hand-built map (never modified - only measured).
            Terrain terrain = null;
            var terrains = Object.FindObjectsByType<Terrain>();
            foreach (var t in terrains)
            {
                if (t == null) continue;
                if (t.gameObject.name == "Terrain") { terrain = t; break; }
                if (terrain == null) terrain = t;
            }
            anchor.legacyTerrain = terrain;

            // Road exits: front and back of the town plus the country roads.
            anchor.exitNorth = MysticMapBuilder.RoadExitNorth;
            anchor.exitSouth = MysticMapBuilder.RoadExitSouth;
            anchor.sideExits = MysticMapBuilder.RoadSideExits();
            anchor.Refresh();

            if (manager.settings.palette.IsEmpty) FillPalette(manager);

            // Point the streamer at the player and make sure the controller is enabled.
            var players = Object.FindObjectsByType<SlimePlayer>();
            if (players != null && players.Length > 0)
            {
                manager.target = players[0].transform;
                if (!players[0].enabled)
                {
                    players[0].enabled = true;
                    Debug.Log("[MysticMap.World] Re-enabled the SlimePlayer on '" +
                              players[0].name + "' (it was switched off).");
                }
            }

            manager.settings.Validate();
            manager.EnsureBuilt();

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(anchor);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

            if (report)
                Debug.Log("[MysticMap.World] Streamer ready on '" + manager.name + "', anchored to " +
                          (terrain != null ? terrain.name : "no terrain (using the default 1 km map)") + ".");
            return manager;
        }

        public static ChunkManager FindManager()
        {
            var found = Object.FindObjectsByType<ChunkManager>();
            return found != null && found.Length > 0 ? found[0] : null;
        }

        // =====================================================================
        //  Prefab palette
        // =====================================================================
        public static void FillPalette(ChunkManager manager)
        {
            if (manager == null || manager.settings == null) return;
            WorldPrefabPalette p = manager.settings.palette;

            // Trees: every ambient-occlusion tree (the stumps become scenery instead).
            p.trees = LoadAll(TreeDir, n => !n.ToLowerInvariant().Contains("stump"));

            p.bushes = LoadNames(PropsDir, "Bush1", "Fern1", "Fern2", "Fern3",
                                 "Plant1", "Plant2", "Plant3", "Plant4", "Plant5");
            p.grass = LoadNames(PropsDir, "Grass1", "Grass2", "Grass3", "Grass4", "Rye");
            p.flowers = LoadNames(PropsDir, "Flower1", "Flower2", "Flower3", "Flower4", "Flower5",
                                  "Flower6", "Flower7", "Flower8", "Flower9");
            p.rocks = LoadNames(PropsDir, "Rock1", "Rock2", "Rock3", "Stone2", "Stone3");
            p.stones = LoadNames(PropsDir, "Stone2", "Stone3", "Stone1", "Stone1_detail");

            // Ruins are built from the town pack's stone wall pieces.
            p.walls = LoadNames(TownDir, "Wall_part1", "Wall_part2", "Wall_corner", "Wall_entrance");
            p.props = LoadNames(TownDir, "cart1", "cart2", "cart3", "storage_barrel",
                                "storage_barrel_small", "storage_basket", "storage_bag");
        }

        static GameObject[] LoadNames(string dir, params string[] names)
        {
            var list = new List<GameObject>();
            foreach (string n in names)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/" + n + ".prefab");
                if (prefab != null) list.Add(prefab);
            }
            return list.ToArray();
        }

        static GameObject[] LoadAll(string dir, System.Func<string, bool> filter)
        {
            var list = new List<GameObject>();
            if (!Directory.Exists(dir)) return list.ToArray();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                if (filter != null && !filter(name)) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) list.Add(prefab);
            }
            return list.ToArray();
        }
    }
}
#endif
