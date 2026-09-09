#if UNITY_EDITOR
using System.Collections.Generic;
using MysticMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    public static partial class MysticMapBuilder
    {
        const string RLFlowerKey = "RL.FlowerDensity";
        const string RLRockKey = "RL.RockDensity";
        const string RLClusterKey = "RL.ClusterDensity";
        const string RLVergeKey = "RL.VergeDensity";
        const string RLVergeSizeKey = "RL.VergeSize";
        const string RLVergeOffKey = "RL.VergeOffset";
        const string RLVergeWidKey = "RL.VergeWidth";

        static readonly string[] FlowerNames =
        {
            "Flower1", "Flower2", "Flower3", "Flower4", "Flower5",
            "Flower6", "Flower7", "Flower8", "Flower9"
        };
        static readonly string[] RockNames =
        {
            "Rock1", "Rock2", "Rock3", "Stone1", "Stone2", "Stone3"
        };
        static readonly string[] VergeNames =
        {
            "Grass1", "Grass2", "Grass3", "Grass4", "Rye",
            "Flower1", "Flower2", "Flower3", "Flower4"
        };

        [MenuItem("MCP/Roadside Life/Add to scene (current settings)")]
        public static void AddRoadsideLifeMenu()
        {
            AddRoadsideLifeToScene(EditorPrefs.GetFloat(RLFlowerKey, 0.7f),
                                   EditorPrefs.GetFloat(RLRockKey, 0.5f),
                                   EditorPrefs.GetFloat(RLClusterKey, 0.6f));
        }

        [MenuItem("MCP/Roadside Life/Remove from scene")]
        public static void RemoveRoadsideLifeFromScene()
        {
            var go = GameObject.Find("Roadside Props");
            if (go != null) Object.DestroyImmediate(go);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Log("Removed roadside life.");
        }

        public static void AddRoadsideLifeToScene(float flowerDensity, float rockDensity, float clusterDensity)
        {
            var flowers = LoadList(PropsDir, FlowerNames);
            var rocks = LoadList(PropsDir, RockNames);
            var clusters = EnsureClusterPrefabs();
            var verge = LoadList(PropsDir, VergeNames);

            var go = GameObject.Find("Roadside Props");
            if (go == null) go = new GameObject("Roadside Props");

            var rl = go.GetComponent<RoadsideLife>();
            if (rl == null) rl = go.AddComponent<RoadsideLife>();

            rl.flowerPrefabs = flowers.ToArray();
            rl.rockPrefabs = rocks.ToArray();
            rl.clusterPrefabs = clusters.ToArray();
            rl.vergePrefabs = verge.ToArray();
            rl.flowerDensity = Mathf.Clamp01(flowerDensity);
            rl.rockDensity = Mathf.Clamp01(rockDensity);
            rl.clusterDensity = Mathf.Clamp01(clusterDensity);

            rl.vergeOn = true;
            rl.vergeDensity = Mathf.Clamp01(EditorPrefs.GetFloat(RLVergeKey, 0.9f));
            rl.vergeSize = Mathf.Clamp(EditorPrefs.GetFloat(RLVergeSizeKey, 0.5f), 0.2f, 2f);
            rl.vergeOffset = Mathf.Clamp(EditorPrefs.GetFloat(RLVergeOffKey, 0.3f), 0f, 4f);
            rl.vergeWidth = Mathf.Clamp(EditorPrefs.GetFloat(RLVergeWidKey, 3.2f), 1f, 6f);

            // Keep the roadside bands off the actual road and reset any stale values.
            rl.flowerBand = 9f;
            rl.rockBand = 13f;
            rl.flowerScale = 1f;

            EditorPrefs.SetFloat(RLFlowerKey, rl.flowerDensity);
            EditorPrefs.SetFloat(RLRockKey, rl.rockDensity);
            EditorPrefs.SetFloat(RLClusterKey, rl.clusterDensity);
            EditorPrefs.SetFloat(RLVergeKey, rl.vergeDensity);
            EditorPrefs.SetFloat(RLVergeSizeKey, rl.vergeSize);
            EditorPrefs.SetFloat(RLVergeOffKey, rl.vergeOffset);
            EditorPrefs.SetFloat(RLVergeWidKey, rl.vergeWidth);

            EditorUtility.SetDirty(go);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log("Roadside life added: flowers=" + flowers.Count +
                " rocks=" + rocks.Count + " clusterPrefabs=" + clusters.Count +
                "  (flowers " + rl.flowerDensity.ToString("0.00") +
                ", rocks " + rl.rockDensity.ToString("0.00") +
                ", cargo " + rl.clusterDensity.ToString("0.00") + ").");
        }
        static List<GameObject> LoadList(string dir, string[] names)
        {
            var list = new List<GameObject>();
            foreach (var n in names)
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(dir + "/" + n + ".prefab");
                if (p != null) list.Add(p);
            }
            return list;
        }

        // ---- Clustered cargo prefabs (parked carts with barrels/bags piled around them) ----
        static List<GameObject> EnsureClusterPrefabs()
        {
            string rootDir = GeneratedDir;                 // Assets/MCP/MysticMap/Generated
            string dir = rootDir + "/Roadside";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                if (!AssetDatabase.IsValidFolder(rootDir)) AssetDatabase.CreateFolder("Assets/MCP/MysticMap", "Generated");
                AssetDatabase.CreateFolder(rootDir, "Roadside");
            }

            var result = new List<GameObject>();
            string[] names = { "RoadsideCargo_A", "RoadsideCargo_B", "RoadsideCargo_Pile" };
            foreach (var name in names)
            {
                string path = dir + "/" + name + ".prefab";
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) asset = BuildCargo(name, path);
                if (asset != null) result.Add(asset);
            }
            return result;
        }

        static GameObject BuildCargo(string name, string path)
        {
            GameObject root;
            if (name.EndsWith("_A"))
                root = Compose("cart1", new[] {
                    Item("storage_barrel", new Vector3(2.0f, 0f, -0.3f)),
                    Item("storage_barrel", new Vector3(-1.9f, 0f, 0.6f)),
                    Item("storage_bag",   new Vector3(0.3f, 0f, 2.1f))
                });
            else if (name.EndsWith("_B"))
                root = Compose("cart2", new[] {
                    Item("storage_basket", new Vector3(1.9f, 0f, 0.7f)),
                    Item("storage_jug",    new Vector3(-1.8f, 0f, -0.6f)),
                    Item("storage_barrel", new Vector3(0.2f, 0f, 1.9f))
                });
            else // pile of sacks/barrels, no cart
                root = Compose(null, new[] {
                    Item("storage_barrel", Vector3.zero),
                    Item("storage_barrel", new Vector3(1.4f, 0f, 0.3f)),
                    Item("storage_bag",    new Vector3(-1.3f, 0f, -0.2f)),
                    Item("storage_basket", new Vector3(0.4f, 0f, 1.8f))
                });

            if (root == null) return null;
            root.name = name;
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        static GameObject Compose(string baseCartName, GameObject[] parts)
        {
            GameObject root;
            if (baseCartName != null)
            {
                var cart = LoadPrefab(TownDir + "/" + baseCartName + ".prefab");
                if (cart == null) return null;
                root = (GameObject)Object.Instantiate(cart);
            }
            else root = new GameObject("cargo");

            if (parts != null)
                foreach (var part in parts)
                    if (part != null) part.transform.SetParent(root.transform, false);
            return root;
        }

        static GameObject Item(string prefabName, Vector3 localPos)
        {
            var p = LoadPrefab(TownDir + "/" + prefabName + ".prefab");
            if (p == null) return null;
            var go = (GameObject)Object.Instantiate(p);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 60f), 0f);
            return go;
        }
    }
}
#endif
