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
        //  Bakes small grass tufts around the base of every town house by turning
        //  it into a shared grass-edged prefab (so memory stays low) while keeping
        //  every house exactly where it is. The amount of grass is adjustable.
        // ---------------------------------------------------------------------
        const string GrassAmountKey = "MM.HouseGrass";

        public static float HouseGrassAmount() => EditorPrefs.GetFloat(GrassAmountKey, 1f);
        static void SetGrassAmount(float a) => EditorPrefs.SetFloat(GrassAmountKey, Mathf.Clamp(a, 0.25f, 3f));

        [MenuItem("MCP/Town/House grass/Bake house-edge grass")]
        public static void BakeHouseGrassMenu() => BakeAll();

        [MenuItem("MCP/Town/House grass/Increase grass +")]
        public static void MoreGrassMenu()
        {
            SetGrassAmount(HouseGrassAmount() + 0.5f);
            Log("House grass amount set to " + HouseGrassAmount() + " -> re-baking.");
            BakeAll();
        }

        [MenuItem("MCP/Town/House grass/Decrease grass -")]
        public static void LessGrassMenu()
        {
            SetGrassAmount(HouseGrassAmount() - 0.5f);
            Log("House grass amount set to " + HouseGrassAmount() + " -> re-baking.");
            BakeAll();
        }

        // Batch-mode entry point.
        public static void GrassifyHouses() => BakeAll();
        class HouseEntry
        {
            public Transform grp;
            public string name;
            public Vector3 pos;
            public Quaternion rot;
            public Vector3 scl;
        }

        static void BakeAll()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var town = GameObject.Find("FortressTown");
            if (town == null)
            {
                Debug.LogWarning("[MysticMap] No FortressTown found.");
                return;
            }

            string dir = "Assets/MCP/MysticMap/Generated/TownGrass";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                string parent = "Assets/MCP/MysticMap/Generated";
                if (!AssetDatabase.IsValidFolder(parent))
                    AssetDatabase.CreateFolder("Assets/MCP/MysticMap", "Generated");
                AssetDatabase.CreateFolder(parent, "TownGrass");
            }

            var grass = new List<GameObject>();
            foreach (var gn in new[] { "Grass1", "Grass2", "Grass3", "Grass4", "Rye" })
            {
                var gp = AssetDatabase.LoadAssetAtPath<GameObject>(PropsDir + "/" + gn + ".prefab");
                if (gp != null) grass.Add(gp);
            }
            if (grass.Count == 0)
            {
                Debug.LogWarning("[MysticMap] No grass props found; aborting.");
                return;
            }

            // Record every house + its exact transform (windmills / doors / shutters ignored).
            var entries = new List<HouseEntry>();
            var used = new HashSet<string>();
            foreach (var groupName in new[] { "Houses", "TownCore" })
            {
                var grp = town.transform.Find(groupName);
                if (grp == null) continue;

                var items = new List<Transform>();
                foreach (Transform c in grp) items.Add(c);
                foreach (Transform child in items)
                {
                    string nm = child.name;
                    string baseName = nm.EndsWith("_Grassed") ? nm.Substring(0, nm.Length - 8) : nm;
                    if (!(baseName.Contains("House") || baseName.Contains("Tavern"))) continue; // skip windmills
                    if (baseName.Contains("shutter") || baseName.Contains("door")) continue;

                    entries.Add(new HouseEntry
                    {
                        grp = grp, name = baseName,
                        pos = child.position, rot = child.rotation, scl = child.localScale
                    });
                    used.Add(baseName);
                    Object.DestroyImmediate(child.gameObject);
                }
            }

            if (entries.Count == 0)
            {
                Log("No houses found to grass; nothing changed.");
                return;
            }

            // Remove old grassed prefab assets so they are rebuilt at the current amount.
            foreach (var name in used)
            {
                string p = dir + "/" + name + "_Grassed.prefab";
                if (System.IO.File.Exists(p)) AssetDatabase.DeleteAsset(p);
            }

            float amount = HouseGrassAmount();
            int placed = 0;
            foreach (var e in entries)
            {
                var g = GetOrCreateGrassed(e.name, dir, grass, amount);
                if (g == null) continue;
                var ng = (GameObject)PrefabUtility.InstantiatePrefab(g);
                if (ng == null) ng = (GameObject)Object.Instantiate(g);
                if (ng == null) continue;

                ng.transform.SetParent(e.grp, false);
                ng.transform.localPosition = e.grp.InverseTransformPoint(e.pos);
                ng.transform.localRotation = Quaternion.Inverse(e.grp.rotation) * e.rot;
                ng.transform.localScale = e.scl;
                placed++;
            }

            EditorSceneManager.SaveScene(scene, scene.path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log("Baked grass on " + placed + " houses (amount=" + amount + ") into shared prefabs.");
        }
        static GameObject GetOrCreateGrassed(string name, string dir, List<GameObject> grass, float amount)
        {
            string path = dir + "/" + name + "_Grassed.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var basePrefab = LoadPrefab(TownDir + "/" + name + ".prefab");
            if (basePrefab == null) return null;

            var root = (GameObject)Object.Instantiate(basePrefab);
            root.name = name + "_Grassed";
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            AddGrassSkirt(root, grass, amount);

            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        // Places grass tufts all around the base of the building. More tufts (and larger
        // ones) are added as the amount grows. Children of the root so they rotate with the
        // house and are stored once in the shared prefab.
        static void AddGrassSkirt(GameObject root, List<GameObject> grass, float amount)
        {
            Bounds b = CombinedBounds(root);
            float cx = b.center.x, cz = b.center.z;
            float hw = b.size.x * 0.5f, hd = b.size.z * 0.5f;
            float baseY = b.min.y;

            int perSide = Mathf.Clamp(Mathf.RoundToInt(2f * amount + 1f), 2, 6);
            float baseScale = 0.40f + 0.28f * amount;

            int k = 0;
            for (int i = 0; i < perSide; i++)
            {
                float f = perSide == 1 ? 0f : (i / (float)(perSide - 1));
                float t = -0.85f + 1.7f * f;
                AddTuft(root, grass, new Vector3(cx + hw * t, baseY, cz + hd), baseScale, ref k);
                AddTuft(root, grass, new Vector3(cx + hw * t, baseY, cz - hd), baseScale, ref k);
                AddTuft(root, grass, new Vector3(cx + hw, baseY, cz + hd * t), baseScale, ref k);
                AddTuft(root, grass, new Vector3(cx - hw, baseY, cz + hd * t), baseScale, ref k);
            }
        }

        static void AddTuft(GameObject root, List<GameObject> grass, Vector3 local, float baseScale, ref int k)
        {
            var gp = grass[k % grass.Count];
            var go = (GameObject)Object.Instantiate(gp);
            go.name = gp.name + "_g";
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, (k * 61f) % 360f, 0f);
            float scl = baseScale * (0.7f + 0.5f * ((k * 13) % 10) / 10f);
            go.transform.localScale = new Vector3(scl, scl, scl);
            k++;
        }
    }
}
#endif
