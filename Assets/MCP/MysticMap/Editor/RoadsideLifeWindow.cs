#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Settings for the roadside life (streamed flowers / rocks / cargo clusters). Everything
    /// is applied live to the open scene from the Editor - no need to close Unity.
    /// </summary>
    public class RoadsideLifeWindow : EditorWindow
    {
        const string KF = "RL.FlowerDensity";
        const string KR = "RL.RockDensity";
        const string KC = "RL.ClusterDensity";
        const string KVD = "RL.VergeDensity";
        const string KVS = "RL.VergeSize";
        const string KVO = "RL.VergeOffset";
        const string KVW = "RL.VergeWidth";

        float flowerDensity;
        float rockDensity;
        float clusterDensity;
        float vergeDensity;
        float vergeSize;
        float vergeOffset;
        float vergeWidth;
        Vector2 _scroll;

        [MenuItem("MCP/Roadside Life/Settings...")]
        public static void Open()
        {
            var w = GetWindow<RoadsideLifeWindow>(true, "Roadside Life");
            w.minSize = new Vector2(360, 380);
            w.Load();
        }

        void OnEnable() => Load();

        void Load()
        {
            flowerDensity = EditorPrefs.GetFloat(KF, 0.7f);
            rockDensity = EditorPrefs.GetFloat(KR, 0.5f);
            clusterDensity = EditorPrefs.GetFloat(KC, 0.6f);
            vergeDensity = EditorPrefs.GetFloat(KVD, 0.9f);
            vergeSize = EditorPrefs.GetFloat(KVS, 0.5f);
            vergeOffset = EditorPrefs.GetFloat(KVO, 0.3f);
            vergeWidth = EditorPrefs.GetFloat(KVW, 3.2f);
        }

        void Save()
        {
            EditorPrefs.SetFloat(KF, flowerDensity);
            EditorPrefs.SetFloat(KR, rockDensity);
            EditorPrefs.SetFloat(KC, clusterDensity);
            EditorPrefs.SetFloat(KVD, vergeDensity);
            EditorPrefs.SetFloat(KVS, vergeSize);
            EditorPrefs.SetFloat(KVO, vergeOffset);
            EditorPrefs.SetFloat(KVW, vergeWidth);
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("Roadside life (streamed near the player, nothing saved)", EditorStyles.wordWrappedLabel);

            GUILayout.Space(8);
            flowerDensity = EditorGUILayout.Slider("Flowers on the roadside", flowerDensity, 0f, 1f);
            rockDensity = EditorGUILayout.Slider("Small rocks on the roadside", rockDensity, 0f, 1f);
            clusterDensity = EditorGUILayout.Slider("Carts / sack piles frequency", clusterDensity, 0f, 1f);

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Road verge (dense border at the road edge)", EditorStyles.boldLabel);
            vergeDensity = EditorGUILayout.Slider("Verge density", vergeDensity, 0f, 1f);
            vergeSize = EditorGUILayout.Slider("Verge plant size", vergeSize, 0.2f, 2f);
            vergeOffset = EditorGUILayout.Slider("Start distance past road edge (m)", vergeOffset, 0f, 4f);
            vergeWidth = EditorGUILayout.Slider("Verge band width (m)", vergeWidth, 1f, 6f);

            EditorGUILayout.HelpBox(
                "The verge is a dense row of small grass + flowers right at the road's edge that " +
                "separates the road texture from the grass beyond. Rocks sit further out and sparser; " +
                "carts & sack piles appear in small groups beside the road. All stream near the player " +
                "and despawn as you leave.",
                MessageType.None);

            GUILayout.Space(10);
            if (GUILayout.Button("Add / Update in the open scene", GUILayout.Height(30)))
            {
                Save();
                MysticMapBuilder.AddRoadsideLifeToScene(flowerDensity, rockDensity, clusterDensity);
                EditorUtility.DisplayDialog("Roadside Life",
                    "Added/updated 'Roadside Props' in the open scene with the chosen amounts.\n" +
                    "Press Play and walk along a road to see it stream in.", "OK");
            }

            if (GUILayout.Button("Remove roadside life from the scene"))
            {
                MysticMapBuilder.RemoveRoadsideLifeFromScene();
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Tip: after adding it once you can just press Play. Re-open this window anytime " +
                "and hit Add/Update to change the amounts live.",
                MessageType.Info);
            EditorGUILayout.EndScrollView();

            if (GUI.changed) Save();
        }
    }
}
#endif
