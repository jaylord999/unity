using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Safely adds ONE Haon character to the open scene so you can see it under the
/// Built-in pipeline. It only instantiates the character and converts THAT
/// instance's own materials to a Built-in shader - it does NOT touch the map,
/// the render pipeline, or the pack's original material assets.
/// Reversible: just delete the "HaonCharacter (Built-in test)" object.
/// Menu: Tools > Add Built-in Character (test)
/// </summary>
public static class AddBuiltinCharacter
{
    const string PrefabPath = "Assets/Haons SD series Pack/Prefab/CharacterSet/prf_Set Costume02 UTC.prefab";

    [MenuItem("Tools/Add Built-in Character (test)")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Stop Play mode first.");
            return;
        }

        var po = AssetDatabase.LoadMainAssetAtPath(PrefabPath);
        if (!(po is GameObject pgo))
        {
            Debug.LogError("Could not load character prefab at:\n" + PrefabPath);
            return;
        }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(pgo);
        go.name = "HaonCharacter (Built-in test)";

        Vector3 pos = Vector3.zero;
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 f = cam.transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 0.001f) f = Vector3.forward;
            f.Normalize();
            pos = cam.transform.position + f * 5f;
            pos.y = cam.transform.position.y - 1.6f; // place feet roughly at ground
        }
        go.transform.position = pos;

        ConvertInstanceMaterials(go);

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("Added a Built-in test character at " + pos +
                  ".\nThis only added ONE object (delete 'HaonCharacter (Built-in test)') to undo.");
    }

    static void ConvertInstanceMaterials(GameObject go)
    {
        Shader standard = Shader.Find("Standard");
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                mats[i] = ToBuiltin(mats[i], standard);
            }
            r.sharedMaterials = mats;
        }
    }

    static Material ToBuiltin(Material src, Shader standard)
    {
        var m = new Material(standard);
        m.name = src.name + "_Builtin";

        Texture main = src.mainTexture;
        if (main == null && src.HasProperty("_BaseMap")) main = src.GetTexture("_BaseMap");
        Color col = Color.white;
        if (src.HasProperty("_Color")) col = src.GetColor("_Color");
        else if (src.HasProperty("_BaseColor")) col = src.GetColor("_BaseColor");

        m.mainTexture = main;
        m.color = col;
        return m;
    }
}
