#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MysticMap.EditorTools
{
    public static partial class MysticMapBuilder
    {
        // ---------------------------------------------------------------------
        //  Writes the map's WINDING road network (MysticMap.MapRoads) into the terrain.
        //  It only touches the ROAD layer of the splatmap and the grass detail - the town,
        //  buildings, props, colliders and the terrain HEIGHT are not touched at all, so it
        //  is safe to re-run whenever you want.
        // ---------------------------------------------------------------------
        [MenuItem("MCP/Roads/Paint winding roads into the map")]
        public static void PaintWindingRoadsMenu() => PaintWindingRoads(true);

        public static void PaintWindingRoads(bool ask)
        {
            var terrain = FindSceneTerrain();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogWarning("[MysticMap] No Terrain found in the open scene.");
                return;
            }

            if (ask && !EditorUtility.DisplayDialog("Mystic Map",
                    "Repaint the terrain roads as WINDING curves and re-bake the grass so it keeps " +
                    "off them?\n\nOnly the road layer of the splatmap and the grass detail change - " +
                    "the town, buildings, props and terrain height are NOT touched.",
                    "Paint", "Cancel"))
                return;

            _terrain = terrain;
            _td = terrain.terrainData;
            int hres = _td.heightmapResolution;
            _spacing = _td.size.x / (hres - 1);
            float[,] norm = _td.GetHeights(0, 0, hres, hres);
            _h2 = new float[hres, hres];
            for (int z = 0; z < hres; z++)
                for (int x = 0; x < hres; x++)
                    _h2[z, x] = norm[z, x] * _td.size.y;

            _roadPolys = MapRoads.Paths;      // the winding network

            EditorUtility.DisplayProgressBar("Mystic Map", "Painting winding roads...", 0.2f);
            try
            {
                ApplySplatmap();
                EditorUtility.DisplayProgressBar("Mystic Map", "Re-baking grass...", 0.7f);
                ApplyGrassDetails();
                RemoveScatterFromRoads();
            }
            finally { EditorUtility.ClearProgressBar(); }

            EditorUtility.SetDirty(_td);
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MysticMap] Winding roads painted into the map (" + MapRoads.Paths.Count +
                      " roads). They stay straight through the town and wind in the country.");
        }
    }
}
#endif
