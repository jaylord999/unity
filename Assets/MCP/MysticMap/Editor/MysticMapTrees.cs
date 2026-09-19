#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    public static partial class MysticMapBuilder
    {
        // ---------------------------------------------------------------------
        //  Trees are no longer baked into the terrain (that stored ~1600 tree
        //  instances in the terrain asset and bloated the scene). Instead they
        //  grow at runtime around the player - see RuntimeTrees - exactly like
        //  the ambient grass, and they keep clear of the roads and the walled
        //  town (plus a green belt around it).
        // ---------------------------------------------------------------------
        const string RuntimeTreesName = "Ambient Trees (Runtime)";

        [MenuItem("MCP/Trees/Add runtime tree spawner (nothing stored)")]
        public static void MenuAddRuntimeTrees() => AddRuntimeTrees();

        public static RuntimeTrees AddRuntimeTrees()
        {
            var go = GameObject.Find(RuntimeTreesName);
            if (go == null) go = new GameObject(RuntimeTreesName);

            var comp = go.GetComponent<RuntimeTrees>();
            if (comp == null) comp = go.AddComponent<RuntimeTrees>();

            var list = new List<GameObject>();
            foreach (var n in TreeNames)
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(TreeDir + "/" + n + ".prefab");
                if (p != null) list.Add(p);
                else Debug.LogWarning("Missing tree prefab: " + n);
            }
            comp.treePrefabs = list.ToArray();

            var cam = GameObject.FindGameObjectWithTag("MainCamera");
            if (cam != null) comp.target = cam.transform;
            var terr = FindSceneTerrain();
            if (terr != null) comp.terrain = terr;

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Selection.activeGameObject = go;
            Debug.Log("[MysticMap] Runtime trees ready (" + comp.treePrefabs.Length +
                      " prefabs). They grow near the player and are removed when out of range.");
            return comp;
        }

        [MenuItem("MCP/Trees/Remove stored trees (grow them at runtime instead)")]
        public static void MenuRemoveStoredTrees() => RemoveStoredTrees();

        // Clears the trees baked into the terrain AND any tree prefab instances left in the
        // scene, so nothing tree-related is stored any more.
        public static int RemoveStoredTrees()
        {
            int removed = 0;

            var terrain = FindSceneTerrain();
            if (terrain != null && terrain.terrainData != null)
            {
                var td = terrain.terrainData;
                removed += td.treeInstances != null ? td.treeInstances.Length : 0;
                td.treeInstances = new TreeInstance[0];
                td.treePrototypes = new TreePrototype[0];
                EditorUtility.SetDirty(td);
            }

            // Tree prefab instances sitting in the scene (roots or cloned into prop roots).
            var hits = new List<GameObject>();
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None))
            {
                if (go == null) continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (string.IsNullOrEmpty(path)) continue;
                if (path.Replace('\\', '/').StartsWith(TreeDir)) hits.Add(go);
            }
            foreach (var go in hits)
            {
                if (go == null) continue;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) continue;
                Object.DestroyImmediate(go);
                removed++;
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[MysticMap] Removed " + removed + " stored trees. Trees now grow at runtime.");
            return removed;
        }
    }
}
#endif
