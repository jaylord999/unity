#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using MysticMap.World;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Turns the Mystic Map player into the bouncing slime (third person).
    ///
    /// The scene used to hold a first-person humanoid ("Player" carrying a FirstPersonPlayer)
    /// plus a leftover third-person humanoid rig ("CharacterPlayer" whose ThirdPersonPlayer
    /// script is gone). This tool replaces all of that with SlimePlayer + the imported
    /// anime_slime mesh:
    ///   * removes every humanoid controller (even one whose script was already deleted, which
    ///     Unity otherwise keeps as a "missing script" component) and deletes the empty
    ///     CharacterPlayer husk,
    ///   * drops "anime_slime" under the Player object and wires it to SlimePlayer,
    ///   * sizes the CharacterController / camera pivot to the slime,
    ///   * un-parents the camera (it is a third-person boom now, not an eye on the head) and
    ///     parks it where the follow rig will keep it,
    ///   * points the world streamers (ChunkManager / PropStreamer) at the new player.
    ///
    /// It runs by itself the first time the project reloads after the humanoid scripts were
    /// deleted, when the Mystic Map scene is opened, and on leaving Play mode. It can be re-run
    /// any time from MCP -> Player -> Slime as the player (third person), and it does nothing
    /// once the scene is already slime-based.
    /// </summary>
    [InitializeOnLoad]
    public static class SlimePlayerSetup
    {
        /// <summary>The imported slime mesh: a static mesh (+ blend shapes), so there is no rig
        /// and no animation clip - SlimePlayer bounces it procedurally instead.</summary>
        public const string SlimeModelPath = "Assets/3dcharacters/anime_slime.fbx";

        // ---- Names used by the migration -------------------------------------
        const string PlayerName = "Player";
        const string ModelChildName = "anime_slime";
        const string HumanoidHuskName = "CharacterPlayer";
        const string MainCameraName = "Main Camera";

        /// <summary>Humanoid controllers that are replaced. Matched by class name so it also
        /// catches components whose script asset has been deleted.</summary>
        static readonly string[] HumanoidTypes = { "FirstPersonPlayer", "ThirdPersonPlayer" };

        static SlimePlayerSetup()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.delayCall += AutoMigrate;
        }

        // =====================================================================
        //  Entry points
        // =====================================================================
        [MenuItem("MCP/Player/Slime as the player (third person)")]
        public static void InstallFromMenu()
        {
            Install(true);
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += AutoMigrate;
        }

        static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (mode == OpenSceneMode.Single)
                EditorApplication.delayCall += AutoMigrate;
        }

        /// <summary>Runs the migration only when the open scene still has a humanoid player in it.</summary>
        static void AutoMigrate()
        {
            if (!CanRun()) return;
            if (BuildPipeline.isBuildingPlayer) return;

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            if (!HasHumanoidPlayer(scene)) return;

            Install(true);
        }

        static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating;
        }

        // =====================================================================
        //  The migration
        // =====================================================================
        /// <summary>Replaces the humanoid player in the open scene with the slime.</summary>
        /// <param name="saveScene">Save the scene when it changed (the menu item and the
        /// automatic migration both do, so the result survives Play / a Unity restart).</param>
        /// <returns>True when the scene was changed.</returns>
        public static bool Install(bool saveScene)
        {
            if (!CanRun())
            {
                Debug.LogWarning("[MysticMap] The slime player can only be installed in edit mode " +
                                 "(stop Play mode or wait for the recompile to finish).");
                return false;
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return false;

            var slimeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SlimeModelPath);
            if (slimeAsset == null)
            {
                Debug.LogError("[MysticMap] Slime model not found at " + SlimeModelPath +
                               " - the player was left untouched.");
                return false;
            }

            var changes = new StringBuilder();

            // ---- 1) The object that becomes the player -----------------------
            GameObject player = FindPlayerObject(scene);
            if (player == null)
            {
                player = new GameObject(PlayerName);
                Undo.RegisterCreatedObjectUndo(player, "Create Player");
                player.transform.position = SceneViewPivot();
                changes.Append("created '" + PlayerName + "', ");
            }

            Undo.RegisterFullObjectHierarchyUndo(player, "Use the slime as the player");

            // ---- 2) Strip the humanoid rig (including dead / missing scripts)--
            foreach (GameObject go in FindHumanoidObjects(scene, player))
            {
                if (go == player) continue;

                if (HasVisibleChildren(go))
                {
                    // Never throw away an object that still draws something: just clean it.
                    StripHumanoidControllers(go, changes);
                    changes.Append("cleaned '" + go.name + "', ");
                }
                else
                {
                    changes.Append("deleted '" + go.name + "' (" + Describe(go) + "), ");
                    Undo.DestroyObjectImmediate(go);
                }
            }
            StripHumanoidControllers(player, changes);

            // ---- 3) The slime body -------------------------------------------
            Transform model = player.transform.Find(ModelChildName);
            if (model == null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(slimeAsset, player.transform);
                go.name = ModelChildName;
                Undo.RegisterCreatedObjectUndo(go, "Add the slime model");
                go.transform.localPosition = Vector3.zero;
                // The rotation is left exactly as the importer made it: a Z-up FBX root carries an
                // axis conversion there, and SlimePlayer works around whatever it finds.
                model = go.transform;
                changes.Append("added the slime model, ");
            }

            // Existing models get cleaned too (the file used to be imported with an Animator).
            CleanupModelInstance(model.gameObject, changes);

            // ---- 4) Controller + the SlimePlayer itself ----------------------
            var cc = player.GetComponent<CharacterController>();
            if (cc == null)
            {
                Undo.AddComponent<CharacterController>(player);
                changes.Append("added a CharacterController, ");
            }

            var slime = player.GetComponent<SlimePlayer>();
            if (slime == null)
            {
                slime = Undo.AddComponent<SlimePlayer>(player);
                changes.Append("added SlimePlayer, ");
            }
            slime.model = model;

            // ---- 5) The camera becomes a third-person boom -------------------
            Camera cam = FindCamera(player);
            if (cam == null)
            {
                var camGo = new GameObject(MainCameraName);
                camGo.tag = "MainCamera";
                Undo.RegisterCreatedObjectUndo(camGo, "Create Main Camera");
                cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                cam.fieldOfView = 75f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = EditorPrefs.GetFloat("MM.RenderDist", 130f);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = RenderSettings.fogColor;
                changes.Append("created '" + MainCameraName + "', ");
            }
            else if (cam.transform.parent == player.transform)
            {
                // SlimePlayer writes the camera's world position every frame, so the parent is
                // only a scale/height trap (and in the editor it would leave the camera sitting
                // at eye height inside the slime).
                Undo.SetTransformParent(cam.transform, null, "Detach the camera");
                changes.Append("took the camera off the player, ");
            }
            slime.cam = cam;

            // ---- 6) Stream the procedural world around the new player --------
            foreach (var mgr in Object.FindObjectsByType<ChunkManager>(FindObjectsSortMode.None))
            {
                if (mgr == null || mgr.target == player.transform) continue;
                mgr.target = player.transform;
                mgr.autoFindPlayer = true;
                EditorUtility.SetDirty(mgr);
            }
            foreach (var streamer in Object.FindObjectsByType<PropStreamer>(FindObjectsSortMode.None))
            {
                // PropStreamer culls props that are behind whatever it tracks, and it is meant to
                // follow the camera (in third person the slime can face away from it).
                if (streamer == null || streamer.target == cam.transform) continue;
                streamer.target = cam.transform;
                EditorUtility.SetDirty(streamer);
            }

            // ---- 7) Size everything to the slime, then park the camera -------
            slime.FitToModel();

            // Show in the scene view the pose Play starts from: the slime faces away from the camera.
            model.rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y + slime.modelYawOffset, 0f)
                             * model.localRotation;

            slime.SnapCamera();
            EditorUtility.SetDirty(slime);
            EditorUtility.SetDirty(model.gameObject);

            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene) EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = player;
            SceneView.RepaintAll();

            Debug.Log("[MysticMap] Slime player ready on '" + player.name + "': " +
                      (changes.Length > 0 ? changes.ToString().TrimEnd(' ', ',') : "already up to date") +
                      ". Mouse orbits the camera, WASD hops, Shift hops faster.");
            return true;
        }

        // =====================================================================
        //  Finding things
        // =====================================================================
        /// <summary>The object that should become the slime player.</summary>
        static GameObject FindPlayerObject(Scene scene)
        {
            // 1) Already migrated.
            var slimes = Object.FindObjectsByType<SlimePlayer>(FindObjectsSortMode.None);
            if (slimes.Length > 0 && slimes[0] != null) return slimes[0].gameObject;

            // 2) Whoever still carries a humanoid controller. The husk scores lowest, a live
            //    controller on the object called "Player" scores highest.
            GameObject best = null;
            int bestScore = int.MinValue;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!IsHumanoid(mb)) continue;

                    GameObject go = mb.gameObject;
                    int score = IsMissingScript(mb) ? 1 : 4;
                    if (go.name == PlayerName) score += 2;
                    if (go.name == HumanoidHuskName) score -= 3;

                    if (best == null || score > bestScore) { best = go; bestScore = score; }
                }
            }
            if (best != null) return best;

            // 3) The old first-person player object, else the only thing in this scene that
            //    walks around on a CharacterController.
            GameObject named = GameObject.Find(PlayerName);
            if (named != null) return named;

            var controllers = Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None);
            return controllers.Length > 0 && controllers[0] != null ? controllers[0].gameObject : null;
        }

        /// <summary>Root objects (other than the player) that carry a humanoid controller.</summary>
        static List<GameObject> FindHumanoidObjects(Scene scene, GameObject player)
        {
            var found = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null || root == player) continue;

                bool humanoid = false;
                foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!IsHumanoid(mb)) continue;
                    humanoid = true;
                    break;
                }

                // The old third-person rig can also show up as nothing but a missing script.
                if (!humanoid && root.name == HumanoidHuskName) humanoid = true;

                if (humanoid) found.Add(root);
            }
            return found;
        }

        /// <summary>True while the open scene still has a humanoid player in it.</summary>
        static bool HasHumanoidPlayer(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null) continue;

                foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (IsHumanoid(mb)) return true;

                // A humanoid whose script asset is gone is only visible as a "missing script"
                // component, so the two objects that used to carry one are checked by name.
                if ((root.name == HumanoidHuskName || root.name == PlayerName) &&
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root) > 0)
                    return true;
            }
            return false;
        }

        /// <summary>The camera that becomes the third-person camera: the old eye camera of the
        /// player if there is one, else the main camera.</summary>
        static Camera FindCamera(GameObject player)
        {
            Camera child = player.GetComponentInChildren<Camera>(true);
            if (child != null) return child;

            if (Camera.main != null) return Camera.main;

            GameObject named = GameObject.Find(MainCameraName);
            if (named != null && named.TryGetComponent(out Camera cam)) return cam;

            var cams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            return cams.Length > 0 ? cams[0] : null;
        }

        static Vector3 SceneViewPivot()
        {
            SceneView view = SceneView.lastActiveSceneView;
            return view != null ? view.pivot : Vector3.zero;
        }

        // =====================================================================
        //  Editing
        // =====================================================================
        /// <summary>Removes every humanoid controller from the object (and from its children).</summary>
        static void StripHumanoidControllers(GameObject go, StringBuilder log)
        {
            foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || !IsHumanoid(mb)) continue;
                log.Append(mb.GetType().Name + " off '" + go.name + "', ");
                Undo.DestroyObjectImmediate(mb);
            }

            // A component whose script asset has been deleted is not always reachable through
            // GetComponentsInChildren, so Unity's own helper finishes the job. This is not
            // undoable, but it only ever runs on the player / the old humanoid husk, right
            // before the scene is saved.
            if (go.name != PlayerName && go.name != HumanoidHuskName) return;

            int dead = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (dead <= 0) return;

            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            log.Append(dead + " dead script(s) off '" + go.name + "', ");
        }

        // =====================================================================
        //  Component inspection (works even when the script asset is gone)
        // =====================================================================
        static bool IsHumanoid(MonoBehaviour mb)
        {
            if (mb == null) return false;

            string type = mb.GetType().Name;
            foreach (string humanoid in HumanoidTypes)
                if (type == humanoid) return true;

            // A deleted script still remembers the class it was serialised with.
            string id = EditorClassIdentifier(mb);
            foreach (string humanoid in HumanoidTypes)
                if (id.Contains(humanoid)) return true;

            return false;
        }

        /// <summary>True for a component whose script asset no longer exists.</summary>
        static bool IsMissingScript(Object component)
        {
            try
            {
                var so = new SerializedObject(component);
                SerializedProperty script = so.FindProperty("m_Script");
                return script == null || script.objectReferenceValue == null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The class name a component was serialised with.</summary>
        static string EditorClassIdentifier(Object component)
        {
            try
            {
                var so = new SerializedObject(component);
                SerializedProperty id = so.FindProperty("m_EditorClassIdentifier");
                return id != null && id.propertyType == SerializedPropertyType.String ? id.stringValue : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Drops the helper components an FBX import can bring along: an Animator (the slime file is
        /// imported as Generic, so Unity may add one) and any camera / light baked into the file -
        /// the last thing we want is the setup tool adopting a stray camera inside the model.
        /// </summary>
        static void CleanupModelInstance(GameObject model, StringBuilder log)
        {
            if (model == null) return;

            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
            {
                if (animator == null) continue;
                log.Append("no Animator, ");
                Undo.DestroyObjectImmediate(animator);
            }

            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child.gameObject == model) continue;
                if (child.GetComponent<Renderer>() != null) continue;                  // never hide the slime
                if (child.GetComponent<Camera>() == null && child.GetComponent<Light>() == null) continue;

                log.Append("dropped '" + child.name + "', ");
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        static bool HasVisibleChildren(GameObject go)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                if (r != null) return true;
            return false;
        }

        /// <summary>Short list of what an object is made of, for the log message.</summary>
        static string Describe(GameObject go)
        {
            var parts = new List<string>();
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;

                if (IsMissingScript(c))
                {
                    string id = EditorClassIdentifier(c);
                    int split = id.LastIndexOf(':');
                    parts.Add("missing " + (split >= 0 && split + 1 < id.Length ? id.Substring(split + 1) : "script"));
                }
                else
                {
                    parts.Add(c.GetType().Name);
                }
            }
            return parts.Count > 0 ? string.Join(" + ", parts) : "empty object";
        }
    }
}
#endif
