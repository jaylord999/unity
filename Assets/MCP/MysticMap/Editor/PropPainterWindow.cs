#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Brush tool to hand-place ground-cover prefabs (grass, stones, flowers, ...)
    /// onto the terrain in the Scene view. Menu: MCP -> Prop Painter
    /// </summary>
    public class MysticPropPainterWindow : EditorWindow
    {
        const string PropsDir = "Assets/FantasyEnvironments/Environments/Prefabs";
        const string RootName = "Painted Props";

        static readonly string[] Groups =
        {
            "Grass tufts", "Stones / rocks", "Flowers",
            "Plants / bushes", "Mushrooms"
        };
        static readonly string[][] GroupNames =
        {
            new[] { "Grass1", "Grass2", "Grass3", "Grass4", "Rye" },
            new[] { "Rock1", "Rock2", "Rock3", "Stone1", "Stone2", "Stone3" },
            new[] { "Flower1", "Flower2", "Flower3", "Flower4", "Flower5", "Flower6", "Flower7", "Flower8", "Flower9" },
            new[] { "Fern1", "Fern2", "Fern3", "Plant1", "Plant2", "Plant3", "Sunflower", "Bush1" },
            new[] { "Mushroom1", "Mushroom2", "Mushroom3", "Mushroom4", "Mushroom5" }
        };
        // min/max base scale per group
        static readonly Vector2[] GroupScale =
        {
            new Vector2(0.9f, 2.2f),
            new Vector2(0.5f, 2.2f),
            new Vector2(0.6f, 1.5f),
            new Vector2(0.7f, 1.8f),
            new Vector2(0.7f, 1.6f)
        };

        bool painting;
        int groupIndex = 0;
        float brushRadius = 20f;
        int perStroke = 8;
        float scaleMul = 1f;

        GameObject root;

        [MenuItem("MCP/Prop Painter (paint grass, stones...)")]
        public static void Open()
        {
            var w = GetWindow<MysticPropPainterWindow>(false, "Prop Painter");
            w.minSize = new Vector2(320, 300);
        }

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            painting = false;
        }

        void OnGUI()
        {
            GUILayout.Space(6);
            painting = GUILayout.Toggle(painting, "  Paint with brush (hold LEFT mouse in Scene view)");
            GUILayout.Space(4);

            if (Application.isPlaying)
                EditorGUILayout.HelpBox("Exit Play mode to paint.", MessageType.Warning);

            groupIndex = EditorGUILayout.Popup("Object type", groupIndex, Groups);
            brushRadius = EditorGUILayout.Slider("Brush radius (m)", brushRadius, 1f, 100f);
            perStroke = Mathf.RoundToInt(EditorGUILayout.Slider("Objects per click", perStroke, 1f, 40f));
            scaleMul = EditorGUILayout.Slider("Size", scaleMul, 0.4f, 3f);

            GUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Works in the Scene view. Press and drag the left mouse over the ground to paint.\n" +
                "Hold Alt to orbit without painting. Objects are hidden far from the player to save FPS.",
                MessageType.None);

            if (GUILayout.Button("Remove all my painted props"))
            {
                Defer(() =>
                {
                    var r = GameObject.Find(RootName);
                    if (r != null) Object.DestroyImmediate(r);
                    root = null;
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
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag) return;

            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 3000f)) return;

            PlaceStroke(hit.point);
            e.Use();
            sv.Repaint();
        }

        int PlaceStroke(Vector3 center)
        {
            var terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain == null) return 0;

            EnsureRoot();
            if (root == null) return 0;

            var prefabs = LoadPool();
            if (prefabs.Count == 0) return 0;

            Vector2 sRange = GroupScale[groupIndex];
            int placed = 0;
            for (int i = 0; i < perStroke; i++)
            {
                double ang = (double)(Random.value) * Mathf.PI * 2.0;
                double rr = (double)brushRadius * System.Math.Sqrt(Random.value);
                float x = center.x + (float)(System.Math.Cos(ang) * rr);
                float z = center.z + (float)(System.Math.Sin(ang) * rr);

                if (x < 15f || x > 985f || z < 15f || z > 985f) continue;
                // keep a little distance from the village buildings
                if (Vector2.Distance(new Vector2(x, z), new Vector2(500f, 520f)) < 15f) continue;

                float h = terrain.SampleHeight(new Vector3(x, 0f, z));
                float yaw = Random.value * 360f;
                float s = scaleMul * Mathf.Lerp(sRange.x, sRange.y, Random.value);

                var prefab = prefabs[Random.Range(0, prefabs.Count)];
                if (SpawnAt(prefab, new Vector3(x, h, z), yaw, s) != null) placed++;
            }
            EditorUtility.SetDirty(root);
            return placed;
        }

        void EnsureRoot()
        {
            if (root != null) return;
            root = GameObject.Find(RootName);
            if (root != null) return;

            root = new GameObject(RootName);
            root.transform.position = Vector3.zero;
            var ps = root.AddComponent<PropStreamer>();
            ps.radius = Mathf.Max(90f, brushRadius * 8f);
            var cam = GameObject.FindGameObjectWithTag("MainCamera");
            if (cam != null) ps.target = cam.transform;
        }

        System.Collections.Generic.List<GameObject> LoadPool()
        {
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var name in GroupNames[groupIndex])
            {
                // Use the baked "_Glow" prefab when one exists so newly painted
                // grass/flowers/plants glow too (rocks/mushrooms/bush stay plain).
                string glowPath = PropGlowBaker.GetGlowVariantPath(name);
                var p = glowPath != null
                    ? AssetDatabase.LoadAssetAtPath<GameObject>(glowPath)
                    : AssetDatabase.LoadAssetAtPath<GameObject>(PropsDir + "/" + name + ".prefab");
                if (p != null) list.Add(p);
            }
            return list;
        }

        GameObject SpawnAt(GameObject prefab, Vector3 pos, float yaw, float scale)
        {
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) go = (GameObject)Object.Instantiate(prefab);
            if (go == null) return null;
            go.transform.SetParent(root.transform, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

    }
}
#endif
