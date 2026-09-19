#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using MysticMap.World;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Tuning panel for the streamed procedural world (menu: MCP > Procedural World > World
    /// Settings). It edits the settings that live on the scene's ChunkManager, so a change is
    /// stored with the scene and takes effect on the next chunk.
    /// </summary>
    public class WorldSettingsWindow : EditorWindow
    {
        ChunkManager _manager;
        Vector2 _scroll;
        bool _showRoads = true;
        bool _showContent = true;

        [MenuItem("MCP/Procedural World/World Settings")]
        public static void Open()
        {
            var w = GetWindow<WorldSettingsWindow>(true, "Procedural World");
            w.minSize = new Vector2(380, 520);
            w.Refresh();
        }

        void OnEnable() => Refresh();

        void OnFocus() => Refresh();

        void Refresh()
        {
            _manager = ProceduralWorldSetup.FindManager();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            if (_manager == null)
            {
                EditorGUILayout.HelpBox(
                    "There is no procedural world in this scene yet.\n\n" +
                    "1. Run  MCP > Procedural World > 1. Open the town road passes\n" +
                    "2. Then  MCP > Procedural World > 2. Add the world to the scene",
                    MessageType.Info);

                if (GUILayout.Button("Add the world to the scene")) ProceduralWorldSetup.AddToSceneMenu();
                EditorGUILayout.EndScrollView();
                return;
            }

            var settings = _manager.settings;
            if (settings == null)
            {
                EditorGUILayout.HelpBox("The streamer has no settings object.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.LabelField("World", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();

            settings.seed = EditorGUILayout.IntField(
                new GUIContent("Seed", "Changing this regenerates the whole country. The town never changes."),
                settings.seed);

            settings.chunkSize = EditorGUILayout.FloatField(
                new GUIContent("Chunk size (m)", "Must divide 100 so the town roads line up."),
                settings.chunkSize);

            settings.viewRadiusChunks = EditorGUILayout.IntSlider(
                new GUIContent("View radius (chunks)", "How far the ground is generated around the player."),
                settings.viewRadiusChunks, 1, 10);

            settings.detailRadiusChunks = EditorGUILayout.IntSlider(
                new GUIContent("Detail radius (chunks)", "How far trees, grass, rocks and ruins are generated."),
                settings.detailRadiusChunks, 0, settings.viewRadiusChunks);

            settings.chunksPerFrame = EditorGUILayout.IntSlider(
                new GUIContent("Chunks per frame", "Lower = smoother, but the world fills in more slowly."),
                settings.chunksPerFrame, 1, 4);

            settings.unloadMarginChunks = EditorGUILayout.IntSlider(
                new GUIContent("Keep-alive margin", "Chunks are released this many chunks past the view radius."),
                settings.unloadMarginChunks, 0, 3);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Ground", EditorStyles.boldLabel);

            settings.meshResolution = EditorGUILayout.IntSlider("Near mesh resolution", settings.meshResolution, 9, 65);
            settings.farMeshResolution = EditorGUILayout.IntSlider("Far mesh resolution", settings.farMeshResolution, 5, 33);
            settings.lodDistanceChunks = EditorGUILayout.Slider("LOD distance (chunks)", settings.lodDistanceChunks, 1f, 6f);
            settings.baseHeight = EditorGUILayout.Slider("Base height (m)", settings.baseHeight, 0f, 60f);
            settings.maxHeight = EditorGUILayout.Slider("Max height (m)", settings.maxHeight, 60f, 300f);
            settings.seamBand = EditorGUILayout.Slider(
                new GUIContent("Seam blend (m)", "Distance over which the new ground blends into the hand-built map."),
                settings.seamBand, 40f, 400f);

            _showRoads = EditorGUILayout.Foldout(_showRoads, "Roads", true);
            if (_showRoads)
            {
                EditorGUI.indentLevel++;
                settings.roadHalfWidthNear = EditorGUILayout.Slider("Half width near town (m)", settings.roadHalfWidthNear, 2f, 12f);
                settings.roadHalfWidthFar = EditorGUILayout.Slider("Half width far away (m)", settings.roadHalfWidthFar, 2f, 16f);
                settings.roadRelief = EditorGUILayout.Slider("Height variation (m)", settings.roadRelief, 0f, 40f);
                settings.roadMaxApron = EditorGUILayout.Slider("Max cut apron (m)", settings.roadMaxApron, 20f, 160f);
                settings.roadStraightRange = EditorGUILayout.Slider(
                    new GUIContent("Straight for (m)", "Roads leave the town dead straight for this distance. Lower = they start bending sooner."),
                    settings.roadStraightRange, 20f, 600f);
                settings.roadWindRange = EditorGUILayout.Slider("Winds by (m)", settings.roadWindRange, 200f, 2000f);
                settings.roadMaxTurnDegrees = EditorGUILayout.Slider(
                    new GUIContent("Max wander (deg)", "How far a road node may turn. Higher = curvier, more winding roads."),
                    settings.roadMaxTurnDegrees, 0f, 85f);
                settings.roadCurveStrength = EditorGUILayout.Slider(
                    new GUIContent("Curve strength", "How far each road bends sideways. 0 = straight, 1.5 = very curvy."),
                    settings.roadCurveStrength, 0f, 1.5f);
                EditorGUI.indentLevel--;
            }

            _showContent = EditorGUILayout.Foldout(_showContent, "Content", true);
            if (_showContent)
            {
                EditorGUI.indentLevel++;
                settings.generateEnvironment = EditorGUILayout.Toggle("Trees / grass / ruins", settings.generateEnvironment);
                settings.maxTreesPerChunk = EditorGUILayout.IntSlider("Max trees per chunk", settings.maxTreesPerChunk, 0, 60);
                settings.maxGroundPlantsPerChunk = EditorGUILayout.IntSlider("Max plants per chunk", settings.maxGroundPlantsPerChunk, 0, 300);
                settings.maxRocksPerChunk = EditorGUILayout.IntSlider("Max rocks per chunk", settings.maxRocksPerChunk, 0, 40);
                settings.roadClearance = EditorGUILayout.Slider("Road clearance (m)", settings.roadClearance, 0f, 25f);
                settings.ruinChance = EditorGUILayout.Slider("Ruin chance", settings.ruinChance, 0f, 1f);
                settings.mazeChance = EditorGUILayout.Slider("Maze chance", settings.mazeChance, 0f, 1f);
                settings.mazeCells = EditorGUILayout.IntSlider("Maze size (cells)", settings.mazeCells, 5, 15);
                EditorGUI.indentLevel--;
            }

            if (EditorGUI.EndChangeCheck())
            {
                settings.Validate();
                EditorUtility.SetDirty(_manager);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Scene", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh palette")) ProceduralWorldSetup.FillPaletteMenu();
                if (GUILayout.Button("Preview here")) ProceduralWorldSetup.PreviewMenu();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear preview")) ProceduralWorldSetup.ClearPreviewMenu();
                if (GUILayout.Button("Open road passes")) MysticMapBuilder.OpenRoadPassesMenu();
            }

            WorldPrefabPalette palette = settings.palette;
            EditorGUILayout.HelpBox(
                "Palette: " + Count(palette.trees) + " trees, " + Count(palette.bushes) + " bushes, " +
                Count(palette.grass) + " grass, " + Count(palette.flowers) + " flowers, " +
                Count(palette.rocks) + " rocks, " + Count(palette.stones) + " stones, " +
                Count(palette.walls) + " walls, " + Count(palette.props) + " props.\n\n" +
                "Live chunks: " + _manager.LiveChunks + "   queued: " + _manager.QueuedChunks +
                "   town chunks skipped: " + _manager.LegacyChunksSkipped,
                MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "The hand-built town is never regenerated: chunks that overlap it are skipped and " +
                "the procedural ground and roads blend into its edges. Walk out of the north or " +
                "south gate to reach the endless country.",
                MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        static int Count(GameObject[] a) => a == null ? 0 : a.Length;
    }
}
#endif
