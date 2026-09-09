using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Makes the "HaonCharacter (Built-in test)" object a playable third-person
/// character: adds a CharacterController + ThirdPersonPlayer, assigns the
/// locomotion animator, and lets it drive the Main Camera. Disables the old
/// first-person player's input so it doesn't fight. Only touches the character.
/// Menu: Tools > Setup Character Controller & Camera
/// </summary>
public static class SetupCharacterPlayer
{
    const string CharName = "HaonCharacter (Built-in test)";
    const string ControllerPath = "Assets/CharacterPlayer/CharacterLocomotion.controller";

    [MenuItem("Tools/Setup Character Controller & Camera")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Stop Play mode first.");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        GameObject character = FindRootByName(scene, CharName);
        if (character == null)
        {
            Debug.LogError("Could not find '" + CharName + "'. Run 'Tools > Add Built-in Character (test)' first.");
            return;
        }

        // CharacterController
        var cc = character.GetComponent<CharacterController>();
        if (cc == null) cc = character.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.4f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.3f;
        cc.skinWidth = 0.08f;

        // Third-person controller
        var ctrl = character.GetComponent<ThirdPersonPlayer>();
        if (ctrl == null) ctrl = character.AddComponent<ThirdPersonPlayer>();

        // Locomotion animator
        Animator anim = character.GetComponentInChildren<Animator>();
        if (anim != null)
        {
            var loco = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            if (loco != null) { anim.runtimeAnimatorController = loco; anim.applyRootMotion = false; }
        }

        // Camera -> the existing Main Camera
        Camera cam = Camera.main;
        if (cam == null)
        {
            GameObject cgo = GameObject.FindGameObjectWithTag("MainCamera");
            if (cgo != null) cam = cgo.GetComponent<Camera>();
        }
        ctrl.followCam = cam;

        // Disable old first-person input so it doesn't fight the character camera.
        DisableFirstPersonInput();

        // Push character in front of where the (soon third-person) camera looks from.
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = character;

        Debug.Log("Character is now third-person. Press Play: WASD move, Shift run, Space jump, mouse orbits camera.");
    }

    static void DisableFirstPersonInput()
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                foreach (Component c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    var ty = c.GetType();
                    if (ty.Name == "FirstPersonPlayer" ||
                        (ty.FullName != null && ty.FullName.EndsWith(".FirstPersonPlayer")))
                    {
                        var b = c as Behaviour;
                        if (b != null && b.enabled) { b.enabled = false; Debug.Log("Disabled " + t.name + " (FirstPersonPlayer)."); }
                    }
                }
            }
        }
    }

    static GameObject FindRootByName(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == name) return root;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
        }
        return null;
    }
}
