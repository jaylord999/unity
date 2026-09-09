#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Bakes the grass/flower glow INTO prop prefabs (instead of bolting a glow child onto
    /// each placed instance afterwards - which is what bloated the scene to 513 MB).
    ///
    /// For grass, low plants and flowers it builds derived prefabs named  "<name>_Glow.prefab"
    /// in a Generated folder. Each derived prefab is the original prop PLUS one baked
    /// "GlowSprite" quad that shares a single quad-mesh asset and the MagicGlow material.
    /// Rocks / mushrooms / bushes stay plain (no glow) as requested.
    ///
    /// You can then spread/keep those glowing prefabs on the map AND tweak each prop group
    /// (Grass / Low plants / Flowers) - strength & size - and it updates EVERY glowing
    /// instance of that type at once, because they all reference the same prefab assets.
    ///
    /// Run from the menu:  MCP > Glow > ...
    ///   1. Bake glowing prop prefabs
    ///   2. Apply glowing props to the map (strips old per-instance glows, swaps ground cover)
    ///   3. Edit glow per type (strength / size)  -> then "Apply glow look to props"
    ///   4. Revert to plain props (undo #2)
    /// </summary>
    public static class PropGlowBaker
    {
        const string PropsDir = "Assets/FantasyEnvironments/Environments/Prefabs";
        const string GeneratedRoot = "Assets/MCP/MysticMap/Generated";
        const string GlowFolder = GeneratedRoot + "/GlowProps";
        const string MeshPath = GlowFolder + "/GlowQuadMesh.asset";
        const string MatPath = GeneratedRoot + "/MagicGlowMat.mat";

        const string ShaderName = "MysticMap/BillboardGlow";

        // Which source prop prefabs belong to which glow type. ("name without .prefab")
        static readonly Dictionary<string, string[]> Groups = new Dictionary<string, string[]>
        {
            { "Grass",     new[] { "Grass1", "Grass2", "Grass3", "Grass4", "Rye" } },
            { "LowPlants", new[] { "Fern1", "Fern2", "Fern3", "Plant1", "Plant2", "Plant3", "Sunflower" } },
            { "Flowers",   new[] { "Flower1", "Flower2", "Flower3", "Flower4", "Flower5", "Flower6", "Flower7", "Flower8", "Flower9" } }
        };

        static readonly string[] PropRoots = { "Nature Props", "Painted Props" };

        // ---- Per-type settings (stored in EditorPrefs so they survive) --------------
        static string StrengthKey(string type)  => "MM.PG." + type + ".Strength";
        static string SizeKey(string type)      => "MM.PG." + type + ".Size";
        static string HeightKey(string type)    => "MM.PG." + type + ".Height";

        public static float GetStrength(string type)
        {
            float d = type == "Flowers" ? 0.75f : 0.6f;
            return EditorPrefs.GetFloat(StrengthKey(type), d);
        }

        public static float GetSize(string type)
        {
            return EditorPrefs.GetFloat(SizeKey(type), 1.25f);
        }

        public static float GetHeight(string type)
        {
            return EditorPrefs.GetFloat(HeightKey(type), 0.15f);
        }

        public static void SetStrength(string type, float v) => EditorPrefs.SetFloat(StrengthKey(type), v);
        public static void SetSize(string type, float v)     => EditorPrefs.SetFloat(SizeKey(type), v);
        public static void SetHeight(string type, float v)   => EditorPrefs.SetFloat(HeightKey(type), v);

        static Color GetGlowColor()
        {
            return new Color(
                EditorPrefs.GetFloat("MM.GlowColor.R", 0.75f),
                EditorPrefs.GetFloat("MM.GlowColor.G", 1f),
                EditorPrefs.GetFloat("MM.GlowColor.B", 0.6f),
                1f);
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(GlowFolder))
            {
                string parent = Path.GetDirectoryName(GlowFolder).Replace('\\', '/');
                string leaf = Path.GetFileName(GlowFolder);
                if (AssetDatabase.IsValidFolder(parent))
                    AssetDatabase.CreateFolder(parent, leaf);
            }
        }

        // ---- Glow quad mesh + material (saved once as real assets) ------------------
        static Mesh GetOrCreateQuadMesh()
        {
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (m != null) return m;

            EnsureFolder();
            m = new Mesh();
            m.name = "GlowQuad";
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateNormals();
            AssetDatabase.CreateAsset(m, MeshPath);
            return m;
        }

        static Material GetOrCreateGlowMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat != null) return mat;
            Shader sh = Shader.Find(ShaderName);
            if (sh == null) return null;
            mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, MatPath);
            return mat;
        }

        // Each type gets its OWN glow material asset. The shader values (color / strength /
        // size) live ON the material - unlike a MaterialPropertyBlock, material properties
        // ARE saved to disk, so changing a type updates every instance for good.
        static Material GetTypeMaterial(string type)
        {
            EnsureFolder();
            string path = GlowFolder + "/MagicGlow_" + type + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader sh = Shader.Find(ShaderName);
                if (sh == null) return null;
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, path);
            }
            ApplyTypeToMaterial(mat, type);
            return mat;
        }

        static void ApplyTypeToMaterial(Material mat, string type)
        {
            if (mat == null) return;
            mat.SetColor("_Color", GetGlowColor());
            mat.SetFloat("_Intensity", Mathf.Max(0.001f, GetStrength(type)));
            mat.SetFloat("_Falloff", 3f);
            mat.SetFloat("_Size", Mathf.Max(0.05f, GetSize(type)));
            mat.SetFloat("_Aspect", 0.7f);
            EditorUtility.SetDirty(mat);
        }

        // Points a glow renderer at its type's material (which holds the colour/size/strength).
        static void ApplyGlowToRenderer(MeshRenderer mr, string type)
        {
            mr.sharedMaterial = GetTypeMaterial(type);
        }

        // Adds the baked glow quad as a child of a prop root.
        static GameObject CreateGlowChild(GameObject parentRoot, string type)
        {
            var glow = new GameObject("GlowSprite");
            glow.transform.SetParent(parentRoot.transform, false);
            glow.transform.localPosition = new Vector3(0f, GetHeight(type), 0f);

            var mf = glow.AddComponent<MeshFilter>();
            mf.sharedMesh = GetOrCreateQuadMesh();

            var mr = glow.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            ApplyGlowToRenderer(mr, type);
            return glow;
        }

        // ---- Build derived "<name>_Glow.prefab" assets ------------------------------
        static string TypeOf(string name)
        {
            foreach (var kv in Groups)
                for (int i = 0; i < kv.Value.Length; i++)
                    if (kv.Value[i] == name) return kv.Key;
            return null;
        }

        static void BuildOne(string name, string type, bool force)
        {
            EnsureFolder();
            string path = GlowFolder + "/" + name + "_Glow.prefab";
            if (!force && File.Exists(path)) return;

            var src = AssetDatabase.LoadAssetAtPath<GameObject>(PropsDir + "/" + name + ".prefab");
            if (src == null) { Debug.LogWarning("GlowBaker: missing prop prefab " + name); return; }

            var tmp = (GameObject)PrefabUtility.InstantiatePrefab(src);
            if (tmp == null) return;
            // Break the link to the original so the glow prefab is self-contained.
            PrefabUtility.UnpackPrefabInstance(tmp, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);

            CreateGlowChild(tmp, type);

            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            var saved = PrefabUtility.SaveAsPrefabAsset(tmp, path);
            Object.DestroyImmediate(tmp);
            Debug.Log("GlowBaker: " + (saved != null ? "saved " : "FAILED ") + path);
        }

        public static void BakeAll(bool force)
        {
            EnsureFolder();
            foreach (var kv in Groups)
                foreach (var name in kv.Value)
                    BuildOne(name, kv.Key, force);
            AssetDatabase.SaveAssets();
            Debug.Log("GlowBaker: baking finished. Glowing prefabs live in " + GlowFolder);
        }

        // Returns the baked "_Glow" prefab path for a prop name (e.g. "Grass1" -> ".../Grass1_Glow.prefab")
        // or null if that prop has no baked glow version (rocks, mushrooms, bush, ...).
        public static string GetGlowVariantPath(string sourceName)
        {
            string path = GlowFolder + "/" + sourceName + "_Glow.prefab";
            return File.Exists(path) ? path : null;
        }

        // Re-apply current per-type strength/size/color to every glowing prefab (affects
        // every instance on the map that references them).
        public static void ReapplyLook()
        {
            foreach (var kv in Groups)
            {
                string type = kv.Key;
                foreach (var name in kv.Value)
                {
                    string path = GlowFolder + "/" + name + "_Glow.prefab";
                    if (!File.Exists(path)) continue;
                    GameObject content = PrefabUtility.LoadPrefabContents(path);
                    if (content != null)
                    {
                        var child = content.transform.Find("GlowSprite");
                        if (child != null)
                        {
                            child.localPosition = new Vector3(0f, GetHeight(type), 0f);
                            var mr = child.GetComponent<MeshRenderer>();
                            if (mr != null) ApplyGlowToRenderer(mr, type);
                            PrefabUtility.SaveAsPrefabAsset(content, path);
                        }
                        PrefabUtility.UnloadPrefabContents(content);
                    }
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("GlowBaker: glow look applied to all glowing prop prefabs.");
        }

        // ---- Replace one placed prop with a different prefab, keeping its transform ---
        static GameObject ReplacePrefab(GameObject old, string newPath)
        {
            if (old == null || string.IsNullOrEmpty(newPath) || !File.Exists(newPath)) return old;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(newPath);
            if (prefab == null) return old;

            Transform t = old.transform;
            Vector3 pos = t.position;
            Quaternion rot = t.rotation;
            Vector3 scl = t.localScale;
            Transform parent = t.parent;
            int index = t.GetSiblingIndex();

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go != null)
            {
                go.transform.SetParent(parent, true);
                go.transform.SetSiblingIndex(index);
                go.transform.position = pos;
                go.transform.rotation = rot;
                go.transform.localScale = scl;
            }
            Object.DestroyImmediate(old);
            return go;
        }
        // Swaps the map's grass / low-plant / flower instances over to the glowing prefabs.
        // Rocks, mushrooms, bushes and anything non-glow are left untouched.
        public static void ApplyToScene()
        {
            // Remove the old per-instance glows (and old orb field) first so we don't end
            // up with double glows.
            try { MysticMapBuilder.RemoveGrassSpriteGlows(); }
            catch (System.Exception e) { Debug.LogWarning("GlowBaker: remove old glows skipped: " + e.Message); }

            BakeAll(false);

            var list = new List<Transform>();
            foreach (var rootName in PropRoots)
            {
                var root = GameObject.Find(rootName);
                if (root == null) continue;
                for (int i = 0; i < root.transform.childCount; i++)
                {
                    var ch = root.transform.GetChild(i);
                    if (ch != null) list.Add(ch);
                }
            }

            int swapped = 0, skipped = 0;
            foreach (var child in list)
            {
                if (child == null) continue;
                string curPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                if (string.IsNullOrEmpty(curPath)) continue;

                string folder = Path.GetDirectoryName(curPath).Replace('\\', '/');
                if (folder != PropsDir) continue;                       // only original scattered props

                string baseName = Path.GetFileNameWithoutExtension(curPath);
                if (TypeOf(baseName) == null) continue;                 // not a glow group (rocks etc.)

                string glowPath = GlowFolder + "/" + baseName + "_Glow.prefab";
                if (!File.Exists(glowPath)) { skipped++; continue; }
                if (ReplacePrefab(child.gameObject, glowPath) != null) swapped++;
            }

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("GlowBaker: swapped " + swapped + " props to glowing prefabs (skipped " + skipped + ").");
        }

        // Undo: turn glowing props back into the plain originals.
        public static void RevertFromScene()
        {
            var list = new List<Transform>();
            foreach (var rootName in PropRoots)
            {
                var root = GameObject.Find(rootName);
                if (root == null) continue;
                for (int i = 0; i < root.transform.childCount; i++)
                {
                    var ch = root.transform.GetChild(i);
                    if (ch != null) list.Add(ch);
                }
            }

            int reverted = 0;
            foreach (var child in list)
            {
                if (child == null) continue;
                string curPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
                if (string.IsNullOrEmpty(curPath)) continue;

                string folder = Path.GetDirectoryName(curPath).Replace('\\', '/');
                if (folder != GlowFolder) continue;

                string baseName = Path.GetFileNameWithoutExtension(curPath);   // e.g. "Flower3_Glow"
                if (!baseName.EndsWith("_Glow")) continue;
                string origName = baseName.Substring(0, baseName.Length - 5);
                string origPath = PropsDir + "/" + origName + ".prefab";
                if (!File.Exists(origPath)) continue;

                if (ReplacePrefab(child.gameObject, origPath) != null) reverted++;
            }

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("GlowBaker: reverted " + reverted + " props to plain originals.");
        }

        // ---- Ambient (non-stored) ground cover --------------------------------------
        static readonly System.Collections.Generic.HashSet<string> AmbientNames = BuildAmbientSet();

        static System.Collections.Generic.HashSet<string> BuildAmbientSet()
        {
            var set = new System.Collections.Generic.HashSet<string>();
            foreach (var key in new[] { "Grass", "LowPlants", "Flowers" })
                if (Groups.TryGetValue(key, out var arr))
                    for (int i = 0; i < arr.Length; i++) set.Add(arr[i]);
            return set;
        }

        // Map a prefab path back to the original prop name (handles both plain + _Glow).
        static string SourceNameOfPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            if (folder == GlowFolder)
            {
                string b = Path.GetFileNameWithoutExtension(path);
                return b.EndsWith("_Glow") ? b.Substring(0, b.Length - 5) : b;
            }
            if (folder == PropsDir) return Path.GetFileNameWithoutExtension(path);
            return null;
        }

        // Removes the STORED field grass/flowers (pure ambiance) from the Nature/Painted
        // roots, keeping rocks, stones, mushrooms, bushes and trees.
        public static int RemoveStoredFieldProps()
        {
            var list = new List<Transform>();
            foreach (var rn in PropRoots)
            {
                var root = GameObject.Find(rn);
                if (root == null) continue;
                Transform rt = root.transform;
                for (int i = rt.childCount - 1; i >= 0; i--)
                {
                    var ch = rt.GetChild(i);
                    if (ch != null) list.Add(ch);
                }
            }

            int removed = 0;
            foreach (var child in list)
            {
                if (child == null) continue;
                string src = SourceNameOfPath(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject));
                if (src != null && AmbientNames.Contains(src))
                {
                    Object.DestroyImmediate(child.gameObject);
                    removed++;
                }
            }

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("GlowBaker: removed " + removed + " stored field grass/flowers (kept rocks/trees).");
            return removed;
        }

        // Adds the runtime "grass + flowers grow near the player" component, pre-loaded with
        // plain grass AND glowing flower prefabs. Nothing is stored.
        public static void AddRuntimeFlowers()
        {
            var go = GameObject.Find("Ambient Grass & Flowers (Runtime)");
            if (go == null) go = new GameObject("Ambient Grass & Flowers (Runtime)");

            var comp = go.GetComponent<RuntimeGlowFlowers>();
            if (comp == null) comp = go.AddComponent<RuntimeGlowFlowers>();

            // Dense by default, but MOST spots are plain plants - only ~20% glow. Adjust the
            // "Glow chance" slider on the object to make more or fewer glow.
            comp.radius = 38f;
            comp.spacing = 2.5f;
            comp.density = 0.8f;
            comp.glowChance = 0.2f;

            // PLAIN (non-glow) pool: plain grass + plain low plants.
            var plainList = new List<GameObject>();
            foreach (var key in new[] { "Grass", "LowPlants" })
            {
                if (!Groups.TryGetValue(key, out var arr)) continue;
                for (int i = 0; i < arr.Length; i++)
                {
                    var pr = AssetDatabase.LoadAssetAtPath<GameObject>(PropsDir + "/" + arr[i] + ".prefab");
                    if (pr != null) plainList.Add(pr);
                }
            }
            comp.grassPrefabs = plainList.ToArray();

            var flowerList = new List<GameObject>();
            foreach (var key in new[] { "Flowers", "LowPlants" })
            {
                if (!Groups.TryGetValue(key, out var arr)) continue;
                for (int i = 0; i < arr.Length; i++)
                {
                    string p = GlowFolder + "/" + arr[i] + "_Glow.prefab";
                    if (!File.Exists(p)) continue;
                    var pr = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (pr != null) flowerList.Add(pr);
                }
            }
            comp.flowerPrefabs = flowerList.ToArray();

            var cam = GameObject.FindGameObjectWithTag("MainCamera");
            if (cam != null) comp.target = cam.transform;
            var terr = Object.FindFirstObjectByType<Terrain>();
            if (terr != null) comp.terrain = terr;

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Selection.activeGameObject = go;
            Debug.Log("Runtime ambient grass+flowers ready (" + comp.grassPrefabs.Length + " grass, " +
                      comp.flowerPrefabs.Length + " glow). They grow near the player and are removed when not looked at.");
        }

        [MenuItem("MCP/Ambient/Add runtime grass + flowers (near player, nothing stored)")]
        public static void MenuAddRuntimeFlowers() { AddRuntimeFlowers(); }

        [MenuItem("MCP/Ambient/Remove stored field grass & flowers (keep rocks/trees)")]
        public static void MenuRemoveStoredField() { RemoveStoredFieldProps(); }

        // ---- Menu items --------------------------------------------------------------
        [MenuItem("MCP/Glow/1. Bake glowing prop prefabs")]
        public static void MenuBake() { BakeAll(false); }

        [MenuItem("MCP/Glow/2. Apply glowing props to the map")]
        public static void MenuApply() { ApplyToScene(); }

        [MenuItem("MCP/Glow/3. Apply glow look now (after editing type settings)")]
        public static void MenuReapply() { ReapplyLook(); }

        [MenuItem("MCP/Glow/4. Revert to plain props")]
        public static void MenuRevert() { RevertFromScene(); }

        [MenuItem("MCP/Glow/Edit glow per type (window)")]
        public static void MenuOpenWindow()
        {
            EditorWindow.GetWindow<GlowTypeWindow>(true, "Glow per type");
        }
    }

    /// <summary>A tiny panel to tune glow strength/size per prop type (affects all of them).</summary>
    public class GlowTypeWindow : EditorWindow
    {
        static readonly string[] Types = { "Grass", "LowPlants", "Flowers" };

        void OnGUI()
        {
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Tune glow STRENGTH, SIZE and HEIGHT separately for grass, low plants and flowers.\n" +
                "Because the glow is baked into the prefab, changing a type here updates EVERY " +
                "glowing instance of that type on the map at once.",
                MessageType.Info);

            foreach (var t in Types)
                DrawType(t);

            GUILayout.Space(8);
            if (GUILayout.Button("Apply glow look to glowing props now"))
                PropGlowBaker.ReapplyLook();

            if (GUILayout.Button("Bake missing prefabs + apply to map"))
                PropGlowBaker.ApplyToScene();

            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Bake = create the glow prefab assets (do once).\n" +
                "Apply to map = swap the current map's grass/flowers/low plants to the glowing versions.",
                MessageType.None);
        }

        static void DrawType(string type)
        {
            GUILayout.Space(6);
            GUILayout.Label(type, EditorStyles.boldLabel);

            float strength = PropGlowBaker.GetStrength(type);
            float size = PropGlowBaker.GetSize(type);
            float height = PropGlowBaker.GetHeight(type);
            EditorGUI.BeginChangeCheck();
            strength = EditorGUILayout.Slider("Strength", strength, 0f, 2f);
            size = EditorGUILayout.Slider("Size (m)", size, 0.1f, 3f);
            height = EditorGUILayout.Slider("Height (m)", height, -0.5f, 2f);
            if (EditorGUI.EndChangeCheck())
            {
                PropGlowBaker.SetStrength(type, strength);
                PropGlowBaker.SetSize(type, size);
                PropGlowBaker.SetHeight(type, height);
            }
        }
    }
}
#endif


