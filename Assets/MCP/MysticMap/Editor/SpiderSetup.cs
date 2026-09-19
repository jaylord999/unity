#if UNITY_EDITOR
using System.Collections.Generic;
using MysticMap;
using MysticMap.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Puts the "Assets/fantasySpider" spider into the Mystic Map as a hostile creature.
    ///
    /// It does the two halves of the job:
    ///   1. Builds a ready-to-use spider prefab from the pack:
    ///        * the animated FBX, scaled to a real-world size and stood on the ground,
    ///        * its own LEGACY animation clips (idle / walk / run / attack1 / attack2 / death2)
    ///          wired to the Animation component <see cref="SpiderEnemy"/> plays them through
    ///          (the pack's clips are legacy clips, so they cannot go into an AnimatorController),
    ///        * a CharacterController so it walks the streamed terrain,
    ///        * a Health so the slime's magic can kill it,
    ///        * a <see cref="SpiderEnemy"/> with its long-range spell filled from the Hovl
    ///          "Magic effects pack" (a venom slash that flies at the player).
    ///   2. Adds the spawner to the scene:
    ///        * a "Spider Director" that keeps two spiders alive around the player,
    ///        * a <see cref="PlayerVitals"/> on the slime so the spiders can actually hurt it.
    ///
    /// It runs by itself when the Mystic Map scene is opened (like the slime / magic setup tools)
    /// and can be re-run any time from MCP -> Enemies.
    /// </summary>
    [InitializeOnLoad]
    public static class SpiderSetup
    {
        /// <summary>The imported spider model from the pack.</summary>
        public const string ModelPath = "Assets/fantasySpider/spider_myOldOne.FBX";

        // ---- Where the generated assets live --------------------------------
        const string GeneratedDir = "Assets/MCP/MysticMap/Generated";
        const string SpiderDir = GeneratedDir + "/Spider";

        /// <summary>The spider prefab this tool builds (used by the scene installer and the tuning window).</summary>
        public const string PrefabPath = SpiderDir + "/FantasySpider.prefab";

        // ---- Effect pack ----------------------------------------------------
        const string HovlRoot = "Assets/Hovl Studio/Magic effects pack/Prefabs";
        const string PropsDir = "Assets/FantasyEnvironments/Environments/Prefabs";

        // ---- Scene ----------------------------------------------------------
        const string SceneName = "MysticMap";
        const string DirectorName = "Spider Director";

        /// <summary>
        /// Bumped whenever the prefab builder changes what it writes into the spider. A prefab built by
        /// an older version is rebuilt once, so the scene always works with a current spider (3 = the
        /// one that leaves the mesh and the hit box as plain prefab objects, edited by hand).
        /// </summary>
        public const int BuildVersion = 3;

        // ---- Sizing (a starting point - scale the mesh child on the prefab to taste) ----
        const float TargetSpan = 1.6f;      // metres across the legs
        const float TargetHeight = 1.0f;    // metres from the ground to the top of the body

        static SpiderSetup()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.delayCall += AutoInstall;
        }

        // =====================================================================
        //  Menu
        // =====================================================================
        [MenuItem("MCP/Enemies/1. Build the spider prefab (from fantasySpider)")]
        public static void BuildPrefabMenu()
        {
            GameObject prefab = BuildPrefab(true);
            if (prefab != null) Selection.activeObject = prefab;
        }

        [MenuItem("MCP/Enemies/2. Add spiders to the scene")]
        public static void AddToSceneMenu()
        {
            OpenGameScene();       // make sure we are editing the game scene, not some other one
            Install(true);
        }

        /// <summary>Opens the Mystic Map scene when it is not the active one.</summary>
        static void OpenGameScene()
        {
            const string scenePath = "Assets/Scenes/MysticMap.unity";

            if (SceneManager.GetActiveScene().name == SceneName) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null) return;

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        /// <summary>
        /// Opens the spider prefab on its own (the Unity "prefab stage") so the mesh and the hit box can
        /// be adjusted by hand: scale / move the "Spider Model" child and edit Radius, Height and Centre
        /// on the CharacterController. Select it and the box is drawn in the Scene view.
        /// </summary>
        [MenuItem("MCP/Enemies/3. Open the spider to adjust the mesh and the hit box")]
        public static void OpenPrefabForEditing()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[MysticMap.Spiders] No spider prefab yet - build it first " +
                                 "(MCP > Enemies > 1.).");
                return;
            }

            Selection.activeObject = prefab;
            AssetDatabase.OpenAsset(prefab);        // opens the prefab stage
            SceneView.RepaintAll();

            Debug.Log("[MysticMap.Spiders] Editing " + PrefabPath + ".\n" +
                      "  * Mesh size / position: select the \"Spider Model\" child (or the model root) and use " +
                      "the transform tools. Keep its feet on the prefab's origin (y = 0) so it stands on the ground.\n" +
                      "  * Hit box: select 'FantasySpider' and edit Radius / Height / Centre on the " +
                      "CharacterController. Keep Centre Y = Height / 2 so the box bottom sits on the feet, " +
                      "otherwise the spider floats (or sinks in).\n" +
                      "  * The box is drawn in the Scene view (green capsule, yellow dot = feet, red line = the " +
                      "box is not on the feet).\n" +
                      "  * Save the prefab (Ctrl+S) when it looks right - this IS the spider that spawns, " +
                      "so nothing else has to be done.");
        }

        /// <summary>
        /// Drops a spider at the scene view camera as a normal prefab instance, so the fit can be judged
        /// against the real terrain. Tweak it, then apply the overrides to the prefab - or remove it.
        /// </summary>
        [MenuItem("MCP/Enemies/4. Place a spider here to adjust it")]
        public static void PlaceSpiderForAdjusting()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning("[MysticMap.Spiders] No spider prefab yet - build it first " +
                                 "(MCP > Enemies > 1.).");
                return;
            }

            SceneView view = SceneView.lastActiveSceneView;
            Vector3 pivot = view != null ? view.pivot : new Vector3(500f, 30f, 620f);
            float height = GroundHeightAt(pivot.x, pivot.z);

            var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            placed.name = "Spider (adjust me)";
            placed.transform.SetPositionAndRotation(new Vector3(pivot.x, height, pivot.z),
                                                   Quaternion.Euler(0f, 180f, 0f));

            Undo.RegisterCreatedObjectUndo(placed, "Place a spider to adjust");
            EditorSceneManager.MarkSceneDirty(placed.scene);
            Selection.activeGameObject = placed;
            SceneView.RepaintAll();

            Debug.Log("[MysticMap.Spiders] Placed '" + placed.name + "' next to the scene camera. Adjust the " +
                      "mesh child and the CharacterController on it, then press\n" +
                      "  MCP > Enemies > 5. Use the selected spider as the spawning spider\n" +
                      "so the spiders that SPAWN get the same look and hit box. (Saving the scene is not " +
                      "enough: spawns come from the prefab, not from this copy.)");
        }

        /// <summary>
        /// THE ONE TO PRESS AFTER ADJUSTING A SPIDER IN THE SCENE. Spawns come from the spider PREFAB,
        /// so editing a spider in the scene only changes that one object (saving the scene saves the
        /// scene, not the prefab). This copies the fit of the selected spider - the mesh child's
        /// position / rotation / scale, the root scale and the CharacterController - onto the prefab,
        /// so every spider the director spawns looks and collides the same.
        /// </summary>
        [MenuItem("MCP/Enemies/5. Use the selected spider as the spawning spider")]
        public static void UseSelectedSpiderAsSpawning()
        {
            GameObject source = FindSelectedSpider();
            if (source == null) return;

            if (!SaveFitToPrefab(source)) return;

            Debug.Log("[MysticMap.Spiders] Every spider that spawns now uses the fit of '" + source.name +
                      "'. (You can delete the adjusted copy with MCP > Enemies > Remove the placed spiders.)");
        }

        /// <summary>
        /// Grounds the selected spider for you: the mesh is moved so its lowest point sits on the
        /// object's origin, and the hit box is moved so its bottom is on the same spot - the combination
        /// that stops a spider floating. A prefab instance is saved to the prefab as well, so the fix
        /// reaches the spiders that spawn.
        /// </summary>
        [MenuItem("MCP/Enemies/6. Ground the selected spider (mesh + hit box)")]
        public static void GroundSelectedSpider()
        {
            GameObject source = FindSelectedSpider();
            if (source == null) return;

            GroundFeet(source);

            bool saved = PrefabUtility.IsPartOfPrefabInstance(source) && SaveFitToPrefab(source);

            EditorSceneManager.MarkSceneDirty(source.scene);
            SceneView.RepaintAll();

            Debug.Log("[MysticMap.Spiders] Grounded '" + source.name + "': mesh feet on the origin, box bottom " +
                      "on the feet." + (saved
                          ? " Saved to the prefab too, so spawned spiders match."
                          : " It is not a prefab instance - press '5. Use the selected spider as the spawning " +
                            "spider' to make the spawns match, or adjust the prefab itself (MCP > Enemies > 3.)."));
        }

        /// <summary>The spider root of the current selection (the object the SpiderEnemy sits on).</summary>
        static GameObject FindSelectedSpider()
        {
            GameObject selected = Selection.activeGameObject;

            if (selected == null)
            {
                Debug.LogWarning("[MysticMap.Spiders] Select a spider in the scene (or the open spider prefab) " +
                                 "first, then run this again.");
                return null;
            }

            var enemy = selected.GetComponentInChildren<SpiderEnemy>(true);
            if (enemy == null)
            {
                Debug.LogWarning("[MysticMap.Spiders] '" + selected.name + "' is not a spider - there is no " +
                                 "SpiderEnemy on it or below it. Place one with MCP > Enemies > 4. and adjust that.");
                return null;
            }

            return enemy.gameObject;      // SpiderEnemy always sits on the spider root
        }

        [MenuItem("MCP/Enemies/Remove the placed spiders")]
        public static void RemovePlacedSpiders()
        {
            var doomed = new List<GameObject>();

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root != null && root.name.StartsWith("Spider (adjust me")) doomed.Add(root);

            foreach (GameObject go in doomed) Undo.DestroyObjectImmediate(go);

            Debug.Log("[MysticMap.Spiders] Removed " + doomed.Count + " placed spider(s).");
        }

        /// <summary>
        /// Reports whether the spider prefab (the one that SPAWNS) is standing on the ground: where the
        /// mesh's lowest point is relative to the spider's origin, and where the hit box's bottom is.
        /// Both should be 0 - anything else is why spawned spiders float.
        /// </summary>
        [MenuItem("MCP/Enemies/7. Check the spawning spider (is it standing on the ground?)")]
        public static void CheckSpawningSpider()
        {
            string path = PrefabPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning("[MysticMap.Spiders] No spider prefab yet - build it first (MCP > Enemies > 1.).");
                return;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var enemy = contents.GetComponentInChildren<SpiderEnemy>(true);
                GameObject root = enemy != null ? enemy.gameObject : contents;

                Transform model = FindModel(root);
                float meshFeet = model != null ? LowestWorldY(model) - root.transform.position.y : 0f;

                var box = root.GetComponent<CharacterController>();
                float boxBottom = box != null ? box.center.y - box.height * 0.5f : 0f;

                bool good = Mathf.Abs(meshFeet) < 0.02f && Mathf.Abs(boxBottom) < 0.02f;

                Debug.Log("[MysticMap.Spiders] " + path + "\n" +
                          "  mesh lowest point: " + meshFeet.ToString("0.###") +
                          " m from the spider's origin (positive = the mesh floats, negative = buried)\n" +
                          "  box bottom:        " + boxBottom.ToString("0.###") +
                          " m from the feet (positive = the box lifts the spider, negative = it sinks it)\n" +
                          "  " + (good
                              ? "OK - it is standing on the ground."
                              : "NOT standing right - place one with MCP > Enemies > 4., adjust it, then " +
                                "MCP > Enemies > 5. (or 6.) to make the spawns match."));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Copies the hand-made fit of a spider (mesh transform + root scale + the CharacterController)
        /// onto the spider prefab, so the spiders that spawn use it. The prefab keeps everything else -
        /// animation, Health, the spell - and keeps its identity, so the scene reference stays valid.
        /// </summary>
        static bool SaveFitToPrefab(GameObject source)
        {
            string path = PrefabPath;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning("[MysticMap.Spiders] No spider prefab yet - build it first (MCP > Enemies > 1.).");
                return false;
            }

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var targetEnemy = contents.GetComponentInChildren<SpiderEnemy>(true);
                GameObject target = targetEnemy != null ? targetEnemy.gameObject : contents;

                Vector3 modelPosition = Vector3.zero, modelScale = Vector3.one;
                float boxRadius = 0f, boxHeight = 0f, boxCentreY = 0f;

                Transform srcModel = FindModel(source);
                Transform dstModel = FindModel(target);

                if (srcModel != null && dstModel != null)
                {
                    dstModel.localPosition = srcModel.localPosition;
                    dstModel.localRotation = srcModel.localRotation;
                    dstModel.localScale = srcModel.localScale;

                    modelPosition = srcModel.localPosition;
                    modelScale = srcModel.localScale;
                }

                // Some people scale the whole spider instead of the mesh - take that as well.
                target.transform.localScale = source.transform.localScale;

                var srcBox = source.GetComponent<CharacterController>();
                var dstBox = target.GetComponent<CharacterController>();

                if (srcBox != null && dstBox != null)
                {
                    dstBox.radius = srcBox.radius;
                    dstBox.height = srcBox.height;
                    dstBox.center = srcBox.center;
                    dstBox.slopeLimit = srcBox.slopeLimit;
                    dstBox.stepOffset = srcBox.stepOffset;
                    dstBox.skinWidth = srcBox.skinWidth;

                    boxRadius = srcBox.radius;
                    boxHeight = srcBox.height;
                    boxCentreY = srcBox.center.y;
                }

                PrefabUtility.SaveAsPrefabAsset(contents, path);

                Debug.Log("[MysticMap.Spiders] Saved '" + source.name + "' to " + path + ":\n" +
                          "  mesh  position " + modelPosition.ToString("0.###") +
                          ", scale " + modelScale.ToString("0.###") + "\n" +
                          "  box   radius " + boxRadius.ToString("0.###") + " m, height " +
                          boxHeight.ToString("0.###") + " m, centre Y " + boxCentreY.ToString("0.###") +
                          " m  (box bottom " + (boxCentreY - boxHeight * 0.5f).ToString("0.###") + " m from the feet)");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>The mesh object of a spider (the object the pack's Animation lives on).</summary>
        static Transform FindModel(GameObject root)
        {
            var enemy = root.GetComponent<SpiderEnemy>();
            if (enemy != null && enemy.spiderAnimation != null) return enemy.spiderAnimation.transform;

            foreach (Transform child in root.transform)
                if (child.GetComponentInChildren<Renderer>() != null) return child;

            return null;
        }

        /// <summary>
        /// Puts the feet where they belong: the mesh is moved so its lowest point sits on the spider's
        /// origin, and the hit box is moved so its bottom is on the same spot.
        /// </summary>
        static void GroundFeet(GameObject root)
        {
            Transform model = FindModel(root);

            if (model != null)
            {
                float lowest = LowestWorldY(model);
                model.position += new Vector3(0f, root.transform.position.y - lowest, 0f);
            }

            var box = root.GetComponent<CharacterController>();
            if (box != null) box.center = new Vector3(box.center.x, box.height * 0.5f, box.center.z);
        }

        /// <summary>World height of the lowest point a transform draws (the legs included).</summary>
        static float LowestWorldY(Transform root)
        {
            bool any = false;
            float lowest = root.position.y;

            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;

                if (!any || r.bounds.min.y < lowest) { lowest = r.bounds.min.y; any = true; }
            }

            return lowest;
        }

        /// <summary>Ground height under a world position (the streamed world, or a ray).</summary>
        static float GroundHeightAt(float x, float z)
        {
            var manager = Object.FindFirstObjectByType<ChunkManager>();
            if (manager != null) return manager.SampleHeight(x, z);

            if (Physics.Raycast(new Vector3(x, 600f, z), Vector3.down, out RaycastHit hit, 1200f,
                                ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y;

            return 25f;
        }

        [MenuItem("MCP/Enemies/Remove spiders from the scene")]
        public static void RemoveFromSceneMenu()
        {
            var director = GameObject.Find(DirectorName);
            if (director != null) Undo.DestroyObjectImmediate(director);

            var vitals = Object.FindObjectsByType<PlayerVitals>();
            foreach (PlayerVitals v in vitals)
            {
                if (v == null) continue;
                Health health = v.GetComponent<Health>();
                Undo.DestroyObjectImmediate(v);
                if (health != null) Undo.DestroyObjectImmediate(health);
            }

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[MysticMap.Spiders] Removed the spider director and the player health.");
        }

        static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (mode == OpenSceneMode.Single) EditorApplication.delayCall += AutoInstall;
        }

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += AutoInstall;
        }

        static void AutoInstall()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (SceneManager.GetActiveScene().name != SceneName) return;

            Install(false);
        }

        // =====================================================================
        //  The spider prefab
        // =====================================================================
        /// <summary>Builds (or rebuilds) the spider prefab from the pack at the default size.</summary>
        public static GameObject BuildPrefab(bool report) => BuildPrefab(report, 0f);

        /// <summary>Builds (or rebuilds) the spider prefab from the pack.</summary>
        /// <param name="legSpan">Metres across the legs (0 = the default size from the tuning window).</param>
        public static GameObject BuildPrefab(bool report, float legSpan)
        {
            EnsureFolders();

            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                if (report) Debug.LogWarning("[MysticMap.Spiders] No model at " + ModelPath + ".");
                return null;
            }

            // Scratch objects: built, measured, saved as a prefab, then thrown away.
            var root = new GameObject("FantasySpider");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var model = (GameObject)Object.Instantiate(modelAsset, root.transform);
            model.name = "Spider Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            StripBakedHelpers(model);

            float targetSpan = legSpan > 0.05f ? legSpan : TargetSpan;
            Vector3 size = FitModel(model.transform, targetSpan);

            Animation animation = BuildAnimation(model);

            BuildCharacterController(root, size);
            BuildHealth(root);
            BuildEnemy(root, animation);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();

            if (report)
            {
                Debug.Log(saved != null
                    ? "[MysticMap.Spiders] Built " + PrefabPath + " (spider " + size.x.ToString("0.0") + " x " +
                      size.z.ToString("0.0") + " m across, " + size.y.ToString("0.0") + " m tall, with the " +
                      "pack's legacy Animation clips: idle / walk / run / attack1 / attack2 / death2)."
                    : "[MysticMap.Spiders] Could not save the spider prefab.");
            }

            return saved;
        }

        /// <summary>
        /// Wires the pack's LEGACY clips to a legacy Animation component on the model and drops any
        /// Animator (legacy clips cannot live in an AnimatorController, which is exactly how the
        /// spider pack ships).
        /// </summary>
        static Animation BuildAnimation(GameObject model)
        {
            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
                if (animator != null) Object.DestroyImmediate(animator);

            var animation = model.GetComponent<Animation>();
            if (animation == null) animation = model.AddComponent<Animation>();

            animation.playAutomatically = false;
            animation.cullingType = AnimationCullingType.BasedOnRenderers;

            // The FBX import already puts its clips on this component (importAnimation is on for the
            // spider pack), so the prefab keeps them. The AssetDatabase only hands the clip list over
            // while the model happens to be fully loaded, so this top-up is best-effort; SpiderEnemy
            // plays the clips by name and copes with whatever is really there.
            foreach (AnimationClip clip in LoadClips())
            {
                if (clip == null) continue;
                if (animation.GetClip(clip.name) != null) continue;

                animation.AddClip(clip, clip.name);
            }

            return animation;
        }

        /// <summary>Scales the model to a real-world size and stands it on its feet (y = 0).</summary>
        static Vector3 FitModel(Transform model, float targetSpan)
        {
            Vector3 size = Vector3.one * TargetHeight;

            if (!TryBounds(model.gameObject, out Bounds bounds)) return size;

            float span = Mathf.Max(bounds.size.x, bounds.size.z);
            float scale = span > 0.001f ? Mathf.Clamp(targetSpan / span, 0.0001f, 10000f) : 1f;
            model.localScale = Vector3.one * scale;

            if (!TryBounds(model.gameObject, out Bounds scaled)) return bounds.size * scale;

            Vector3 p = model.localPosition;
            p.y -= scaled.min.y;
            model.localPosition = p;

            return scaled.size;
        }

        /// <summary>The world-space bounds of everything the object draws (identity parents assumed).</summary>
        static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;

                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }

            return any;
        }

        /// <summary>Drops any camera / light child the FBX import may have brought along.</summary>
        static void StripBakedHelpers(GameObject model)
        {
            var doomed = new List<GameObject>();

            foreach (Transform child in model.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child.gameObject == model) continue;
                if (child.GetComponent<Renderer>() != null) continue;
                if (child.GetComponent<Camera>() == null && child.GetComponent<Light>() == null) continue;

                doomed.Add(child.gameObject);
            }

            foreach (GameObject go in doomed) Object.DestroyImmediate(go);
        }

        /// <summary>
        /// The hit box a spider of this size gets: a slim capsule that follows the BODY, not the leg
        /// span (the legs stick out 3-4x further than the body, and a collider that wide would make
        /// the spider shove the whole world around).
        /// </summary>
        static void ColliderFit(Vector3 size, out float radius, out float height, out float centerY)
        {
            float span = Mathf.Max(size.x, size.z);
            radius = Mathf.Clamp(span * 0.22f, 0.25f, 0.9f);
            height = Mathf.Max(Mathf.Max(size.y * 0.9f, TargetHeight * 0.7f), radius * 2.05f);
            centerY = height * 0.5f;
        }

        static void BuildCharacterController(GameObject root, Vector3 size)
        {
            var cc = root.AddComponent<CharacterController>();

            ColliderFit(size, out float radius, out float height, out float centerY);

            cc.radius = radius;
            cc.height = height;
            cc.center = new Vector3(0f, centerY, 0f);
            cc.slopeLimit = 55f;
            // A LOW step: enough for a pebble, too low for a spider to climb onto another spider.
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.05f;
            cc.minMoveDistance = 0f;
        }

        static void BuildHealth(GameObject root)
        {
            var health = root.AddComponent<Health>();
            health.maxHealth = 140f;
            health.destroyOnDeath = true;
            health.destroyDelay = 1.5f;
            health.hitEffect = LoadEffect("Hits and explosions", "Green hit");
            health.deathEffect = LoadEffect("Hits and explosions", "Explosion");
            health.flashColor = new Color(0.7f, 1f, 0.75f, 1f);
            health.stirEnvironment = true;
            health.environmentRadius = 3.5f;
            health.debrisOnDeath = 10;
            health.debrisPrefabs = LoadDebris();
        }

        static void BuildEnemy(GameObject root, Animation animation)
        {
            var enemy = root.AddComponent<SpiderEnemy>();
            enemy.spiderAnimation = animation;
            enemy.hitMask = ~0;
            enemy.biteImpactEffect = LoadEffect("Hits and explosions", "Green hit");
            enemy.castChargeEffect = LoadEffect("Sparks", "Sparks flashing green");
            enemy.spell = BuildSpell();

            enemy.builtBy = BuildVersion;
        }

        /// <summary>
        /// The long-range spell: a green venom slash that flies at the player.
        /// It uses the same pack prefabs the slime's own travelling spells use ("AoE slash green"),
        /// so it is a proven projectile and behaves exactly like the player's blade.
        /// </summary>
        static MagicSpellLevel BuildSpell()
        {
            return new MagicSpellLevel
            {
                name = "Venom Slash",
                school = "Venom",
                delivery = SpellDelivery.Projectile,
                holdToReach = 0f,
                cooldown = 5f,
                damage = 8f,
                castDistance = 3f,
                radius = 0.6f,
                speed = 19f,
                hugGround = true,
                hugHeight = 0.8f,
                pierce = false,
                splash = 0f,
                splashRadius = 1.5f,
                castScale = 0.55f,
                shake = 0f,
                knockback = 2f,
                environmentScale = 0.7f,
                castEffect = LoadEffect("AoE effects", "AoE slash green"),
                impactEffect = LoadEffect("Hits and explosions", "Green hit"),
                maxChargeRadius = 0f
            };
        }

        // =====================================================================
        //  Asset helpers
        // =====================================================================
        /// <summary>Every animation clip that lives inside the spider FBX (idle, walk, run, ...).</summary>
        static AnimationClip[] LoadClips()
        {
            var clips = new List<AnimationClip>();

            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                var clip = asset as AnimationClip;
                if (clip != null) clips.Add(clip);
            }

            return clips.ToArray();
        }

        static GameObject LoadEffect(string folder, string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(HovlRoot + "/" + folder + "/" + name + ".prefab");

        /// <summary>A few small stones the spider throws when it dies.</summary>
        static GameObject[] LoadDebris()
        {
            string[] names = { "Stone1", "Stone2", "Rock1", "Rock2" };
            var list = new List<GameObject>();

            foreach (string n in names)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(PropsDir + "/" + n + ".prefab");
                if (go != null) list.Add(go);
            }

            return list.ToArray();
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedDir))
                AssetDatabase.CreateFolder("Assets/MCP/MysticMap", "Generated");

            if (!AssetDatabase.IsValidFolder(SpiderDir))
                AssetDatabase.CreateFolder(GeneratedDir, "Spider");
        }

        // =====================================================================
        //  Installing
        // =====================================================================
        static void Install(bool report)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            {
                if (report)
                    Debug.LogWarning("[MysticMap.Spiders] The spider model is missing at " + ModelPath + ".");
                return;
            }

            var players = Object.FindObjectsByType<SlimePlayer>();
            if (players == null || players.Length == 0)
            {
                if (report)
                    Debug.LogWarning("[MysticMap.Spiders] No SlimePlayer in the scene - " +
                                     "spiders need someone to hunt.");
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            bool rebuilt = false;

            if (prefab == null || NeedsRebuild(prefab))
            {
                prefab = BuildPrefab(false);
                rebuilt = true;
            }

            if (prefab == null) return;

            if (rebuilt && report)
                Debug.Log("[MysticMap.Spiders] Rebuilt the spider prefab. Size and hit box are edited on the " +
                          "prefab itself (MCP > Enemies > 3.).");

            bool changed = EnsurePlayerVitals(players[0].gameObject);

            var director = GameObject.Find(DirectorName);
            if (director == null)
            {
                director = new GameObject(DirectorName);
                Undo.RegisterCreatedObjectUndo(director, "Add the spider director");
                changed = true;
            }

            var spawner = director.GetComponent<SpiderDirector>();
            if (spawner == null) spawner = director.AddComponent<SpiderDirector>();

            spawner.spiders = new[] { prefab };
            spawner.target = players[0].transform;

            EditorUtility.SetDirty(director);
            EditorUtility.SetDirty(spawner);

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            }

            if (report)
                Debug.Log("[MysticMap.Spiders] Ready: '" + DirectorName + "' streams up to " +
                          spawner.maxAlive + " spiders around '" + players[0].name + "' using " +
                          PrefabPath + ".");
        }

        /// <summary>True when this prefab was made before the current builder (or by hand).</summary>
        static bool NeedsRebuild(GameObject prefab)
        {
            var enemy = prefab.GetComponentInChildren<SpiderEnemy>(true);
            return enemy == null || enemy.builtBy != BuildVersion;
        }

        /// <summary>Makes the slime hurtable (Health) and adds the feedback component.</summary>
        static bool EnsurePlayerVitals(GameObject player)
        {
            bool changed = false;

            var health = player.GetComponent<Health>();
            if (health == null)
            {
                health = Undo.AddComponent<Health>(player);
                changed = true;
            }

            var vitals = player.GetComponent<PlayerVitals>();
            if (vitals == null)
            {
                vitals = Undo.AddComponent<PlayerVitals>(player);
                changed = true;
            }

            health.maxHealth = Mathf.Max(1f, vitals.maxHealth);
            health.destroyOnDeath = false;
            health.reviveAfter = 0f;
            health.deathEffect = null;
            health.stirEnvironment = false;     // the slime is knocked out, not blown to pieces
            vitals.health = health;

            EditorUtility.SetDirty(health);
            EditorUtility.SetDirty(vitals);

            return changed;
        }
    }
}
#endif
