using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Temporary fix for a magenta Terrain: clears the dangling materialTemplate the
/// terrain points to (a material that was deleted) and uses Unity's built-in
/// Standard terrain material instead. Run once, then this file can be deleted.
/// Menu: Tools > Fix Magenta Terrain (Built-in)
/// </summary>
public static class FixMagentaTerrain
{
    [MenuItem("Tools/Fix Magenta Terrain (Built-in)")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Stop Play mode first.");
            return;
        }

        int n = 0;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

        Shader s = Shader.Find("Nature/Terrain/Standard");
        if (s == null)
        {
            Debug.LogError("Built-in terrain shader not found.");
            return;
        }

        string path = "Assets/Settings/Terrain_Builtin.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(s);
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.shader = s;
            EditorUtility.SetDirty(mat);
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Terrain t in root.GetComponentsInChildren<Terrain>(true))
            {
                if (t == null) continue;
                t.materialType = Terrain.MaterialType.Custom;
                t.materialTemplate = mat;
                n++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log(n > 0
            ? "Fixed " + n + " Terrain(s) to Built-in Standard material. Save the scene now."
            : "No Terrain found in the open scene.");
    }
}
