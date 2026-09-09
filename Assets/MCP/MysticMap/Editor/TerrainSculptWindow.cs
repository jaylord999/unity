#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Brush tool to sculpt the terrain (raise / lower / flatten in a circle).
    /// Menu: MCP -> Terrain Sculpt Brush
    /// </summary>
    public class TerrainSculptWindow : EditorWindow
    {
        static readonly string[] Modes = { "Raise hills", "Lower (dig)", "Flatten" };

        bool painting;
        int modeIndex = 0;
        float brushRadius = 12f;
        float strength = 0.4f;

        Terrain _terrain;
        float _flatten01;

        [MenuItem("MCP/Terrain Sculpt Brush (raise/lower hills)")]
        public static void Open()
        {
            var w = GetWindow<TerrainSculptWindow>(false, "Terrain Sculpt");
            w.minSize = new Vector2(320, 240);
        }

        void OnEnable() { SceneView.duringSceneGui += OnSceneGUI; }
        void OnDisable() { SceneView.duringSceneGui -= OnSceneGUI; painting = false; }

        void OnGUI()
        {
            GUILayout.Space(6);
            painting = GUILayout.Toggle(painting, "  Sculpt (hold LEFT mouse and drag on the ground)");
            GUILayout.Space(4);
            if (Application.isPlaying) EditorGUILayout.HelpBox("Exit Play mode to sculpt.", MessageType.Warning);

            modeIndex = EditorGUILayout.Popup("Brush mode", modeIndex, Modes);
            brushRadius = EditorGUILayout.Slider("Brush radius (m)", brushRadius, 3f, 40f);
            strength = EditorGUILayout.Slider("Strength", strength, 0.05f, 3f);

            EditorGUILayout.HelpBox("Works in the Scene view. Hold Alt to orbit without sculpting.", MessageType.None);

            GUILayout.Space(6);
            if (GUILayout.Button("Flatten ENTIRE map (start fresh)"))
            {
                Defer(() =>
                {
                    if (EditorUtility.DisplayDialog("Terrain",
                        "Flatten the whole terrain to a clean level so you can sculpt your own hills?",
                        "Flatten", "Cancel"))
                        FlattenAll();
                });
            }
        }

        static void Defer(System.Action act)
        {
            EditorApplication.delayCall += () =>
            {
                if (act == null) return;
                try { act(); }
                catch (System.Exception e) { Debug.LogException(e); }
            };
        }
        void OnSceneGUI(SceneView sv)
        {
            if (!painting || Application.isPlaying) return;
            Event e = Event.current;
            if (e == null || e.button != 0) return;
            if (e.alt || e.control || e.shift) return;

            if (e.type == EventType.MouseDown) CaptureFlattenTarget(e);

            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) return;

            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 3000f)) return;

            Paint(hit.point);
            e.Use();
            sv.Repaint();
        }

        void CaptureFlattenTarget(Event e)
        {
            if (modeIndex != 2) return;
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 3000f)) return;
            if (_terrain == null) _terrain = FindTerrain();
            if (_terrain == null || _terrain.terrainData == null) return;
            TerrainData td = _terrain.terrainData;
            _flatten01 = Mathf.Clamp01(td.GetInterpolatedHeight(
                (hit.point.x - _terrain.transform.position.x) / td.size.x,
                (hit.point.z - _terrain.transform.position.z) / td.size.z) / td.size.y);
        }

        static Terrain FindTerrain()
        {
            return Object.FindFirstObjectByType<Terrain>();
        }

        void Paint(Vector3 center)
        {
            if (_terrain == null) _terrain = FindTerrain();
            if (_terrain == null || _terrain.terrainData == null)
            {
                Debug.LogWarning("No Terrain in the scene. Build the map first.");
                return;
            }
            TerrainData td = _terrain.terrainData;
            int hres = td.heightmapResolution;
            float spacing = td.size.x / (hres - 1);

            float fx = (center.x - _terrain.transform.position.x) / td.size.x * (hres - 1);
            float fz = (center.z - _terrain.transform.position.z) / td.size.z * (hres - 1);
            float radiusPx = Mathf.Max(1f, brushRadius / spacing);

            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx - radiusPx), 0, hres - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(fx + radiusPx), 0, hres - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(fz - radiusPx), 0, hres - 1);
            int z1 = Mathf.Clamp(Mathf.CeilToInt(fz + radiusPx), 0, hres - 1);

            int w = x1 - x0 + 1;
            int h = z1 - z0 + 1;

            float[,] hs = td.GetHeights(x0, z0, w, h);   // first index = z (row), second = x
            float delta = modeIndex == 0 ? strength : (modeIndex == 1 ? -strength : 0f);
            float delta01 = delta / td.size.y;
            float flattenAmt = Mathf.Min(1f, strength);

            for (int row = 0; row < h; row++)            // z
            {
                int cellZ = z0 + row;
                float dz = cellZ - fz;
                for (int col = 0; col < w; col++)        // x
                {
                    int cellX = x0 + col;
                    float dx = cellX - fx;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz) / radiusPx;
                    float fall = Mathf.Clamp01(1f - dist);   // soft circle
                    if (fall <= 0.001f) continue;

                    float oldVal = hs[row, col];
                    float newVal = oldVal;
                    if (modeIndex == 2)                        // flatten toward the clicked height
                        newVal = Mathf.Lerp(oldVal, _flatten01, fall * flattenAmt);
                    else                                       // raise / lower
                        newVal = oldVal + delta01 * fall;
                    hs[row, col] = Mathf.Clamp01(newVal);
                }
            }

            td.SetHeights(x0, z0, hs);
            EditorUtility.SetDirty(td);
            EditorUtility.SetDirty(_terrain);
        }

        void FlattenAll()
        {
            if (_terrain == null) _terrain = FindTerrain();
            if (_terrain == null || _terrain.terrainData == null) return;
            TerrainData td = _terrain.terrainData;
            int hres = td.heightmapResolution;
            float flat01 = Mathf.Clamp01(24f / td.size.y);   // clean level ground
            float[,] hs = new float[hres, hres];
            for (int z = 0; z < hres; z++)
                for (int x = 0; x < hres; x++)
                    hs[z, x] = flat01;
            td.SetHeights(0, 0, hs);
            EditorUtility.SetDirty(td);
            EditorUtility.SetDirty(_terrain);
            Repaint();
        }

    }
}
#endif
