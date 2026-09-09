#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Batch tool: open the built MysticMap scene, count the fortress objects and render a
    /// few screenshots of the town so it can be reviewed.
    /// </summary>
    public static class MysticMapShot
    {
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/MysticMap.unity", OpenSceneMode.Single);
            RenderSettings.fog = false;   // clear view of the layout for the overview shots

            var town = GameObject.Find("FortressTown");
            int walls = 0, towers = 0, gates = 0, bld = 0, houses = 0, core = 0;
            if (town != null)
            {
                Count(town.transform.Find("Walls"), ref walls);
                Count(town.transform.Find("Towers"), ref towers);
                Count(town.transform.Find("Gates"), ref gates);
                Count(town.transform.Find("Buildings"), ref bld);
                Count(town.transform.Find("Houses"), ref houses);
                Count(town.transform.Find("TownCore"), ref core);
            }
            Debug.Log("SHOT walls=" + walls + " towers=" + towers + " gates=" + gates +
                      " buildings=" + bld + " houses=" + houses + " townCore=" + core);

            Vector3 c = new Vector3(500f, 0f, 520f);
            TownRenderTool.Shot("town2_overview",
                                new Vector3(500f, 210f, 375f), new Vector3(500f, 0f, 535f), 48f);
            TownRenderTool.Shot("town2_top",
                                new Vector3(500f, 470f, 520f), new Vector3(500f, 0f, 520f), 52f);
            TownRenderTool.Shot("town2_wallrow",
                                new Vector3(500f, 30f, 476f), new Vector3(500f, 0f, 508f), 62f);
            TownRenderTool.Shot("town2_gate",
                                new Vector3(495f, 26f, 585f), new Vector3(500f, 6f, 540f), 60f);
            TownRenderTool.Shot("town2_square",
                                new Vector3(500f, 26f, 490f), new Vector3(500f, 6f, 525f), 70f);
            Debug.Log("SHOT DONE");
            AssetDatabase.SaveAssets();
        }

        static void Count(Transform t, ref int n)
        {
            if (t == null) return;
            n = t.childCount;
        }
    }
}
#endif
