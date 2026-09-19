#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Sets the slime up with hold-to-charge magic built from the Hovl Studio effect pack.
    ///
    /// It adds a <see cref="SlimeMagic"/> to the player and a <see cref="MagicChargeHUD"/> in the
    /// scene, then fills the five charge levels with effects from
    /// "Assets/Hovl Studio/Magic effects pack". Every threshold is exactly two layers: one magic
    /// circle lying flat on the ground and one aura around the slime. Reaching the next threshold
    /// takes both away and blooms the new pair, so only one circle and one aura are ever out:
    ///
    ///   1  Gale Slash       wind    - Magic circle + Buff aura
    ///   2  Frost Edge       water   - Freeze circle + magic shield, splash on impact
    ///   3  Volt Arc         thunder - Magic circle 2 + Lightning aura, laser blast
    ///   4  Crystal Bloom    earth   - Healing circle + Healing aura, crystals on the ground
    ///   5  Void Nova        void    - Plexus AoE (violet) + Debuff aura, meteors and a nova
    ///
    /// The circles grow with the level and swell while the charge fills up, so a higher threshold
    /// is visibly bigger. It runs by itself the first time the project reloads with a slime player
    /// that has no magic, when the Mystic Map scene is opened, and on leaving Play mode. Anything
    /// can be re-run from MCP > Player > Slime magic (Hovl circles), and the circle size, the
    /// thresholds and the world reaction are edited in MCP > Player > Magic tuning.
    /// </summary>
    [InitializeOnLoad]
    public static class SlimeMagicSetup
    {
        /// <summary>The pack the magic effects come from.</summary>
        public const string PackRoot = "Assets/Hovl Studio/Magic effects pack/Prefabs";

        const string HudObjectName = "Magic HUD";

        static SlimeMagicSetup()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.delayCall += AutoMigrate;
        }

        // =====================================================================
        //  Entry points
        // =====================================================================
        [MenuItem("MCP/Player/Slime magic (Hovl circles)")]
        public static void InstallFromMenu() => Install(true, false);

        [MenuItem("MCP/Player/Rebuild the 5 magic levels from the Hovl pack")]
        public static void RebuildFromMenu() => Install(true, true);

        [MenuItem("MCP/Player/Magic tuning (circles, thresholds, world)...")]
        public static void OpenTuningWindow() => MagicTuningWindow.Open();

        [MenuItem("MCP/Player/Add magic test targets (3 dummies)")]
        public static void AddTestTargets()
        {
            if (!CanRun()) return;

            Scene scene = SceneManager.GetActiveScene();
            var magic = FindMagic(scene);
            if (magic == null)
            {
                Debug.LogWarning("[MysticMap] Add the slime magic first (MCP > Player > Slime magic).");
                return;
            }

            Transform anchor = magic.transform;
            Vector3 forward = anchor.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

            var created = new List<GameObject>();
            for (int i = 0; i < 3; i++)
            {
                Vector3 spot = anchor.position + forward * (10f + i * 3.5f) + Vector3.right * ((i - 1) * 2.5f);
                spot = Damage.SnapToGround(spot, ~0) + Vector3.up * 1.1f;   // a capsule stands on the ground

                var dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                dummy.name = "Magic Test Target " + (i + 1);
                dummy.transform.position = spot;
                Undo.RegisterCreatedObjectUndo(dummy, "Add a magic test target");

                var health = dummy.AddComponent<Health>();
                health.maxHealth = 300f;
                health.destroyOnDeath = false;
                health.reviveAfter = 6f;
                health.deathEffect = FindPrefab("Hits and explosions", "Explosion");
                created.Add(dummy);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.objects = created.ToArray();
            Debug.Log("[MysticMap] Added " + created.Count + " magic test targets in front of the slime " +
                      "(300 HP each, they come back 6 s after dying).");
        }

        [MenuItem("MCP/Player/Remove magic test targets")]
        public static void RemoveTestTargets()
        {
            if (!CanRun()) return;

            var doomed = new List<GameObject>();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root != null && root.name.StartsWith("Magic Test Target")) doomed.Add(root);

            foreach (GameObject go in doomed) Undo.DestroyObjectImmediate(go);

            Debug.Log("[MysticMap] Removed " + doomed.Count + " magic test target(s).");
        }

        // =====================================================================
        //  Installing
        // =====================================================================
        /// <summary>Adds the magic to the slime player of the open scene, and the bar that shows it.</summary>
        /// <param name="saveScene">Save the scene when it changed.</param>
        /// <param name="rebuildLevels">Throw the level table away and fill it from the pack again.</param>
        /// <returns>True when the scene was changed.</returns>
        public static bool Install(bool saveScene, bool rebuildLevels)
        {
            if (!CanRun())
            {
                Debug.LogWarning("[MysticMap] The slime magic can only be installed in edit mode " +
                                 "(stop Play mode or wait for the recompile to finish).");
                return false;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return false;

            SlimePlayer player = FindPlayer(scene);
            if (player == null)
            {
                Debug.LogWarning("[MysticMap] No SlimePlayer in this scene - install the slime player " +
                                 "first (MCP > Player > Slime as the player).");
                return false;
            }

            var changes = new StringBuilder();

            SlimeMagic magic = player.GetComponent<SlimeMagic>();
            bool created = false;
            if (magic == null)
            {
                magic = Undo.AddComponent<SlimeMagic>(player.gameObject);
                created = true;
                changes.Append("added SlimeMagic, ");
            }
            magic.player = player;

            if (created)
            {
                ApplyTuningDefaults(magic);
                changes.Append("reset the circle / threshold tuning, ");
            }

            // A scene built before the built-in levels changed look is upgraded once: the layout
            // (flat circles on the ground, smaller revolving rings, one aura at a time) is rebuilt
            // from the pack, while the thresholds and damage that were dialled in stay as they were.
            int wasLayout = magic.levelsLayout;
            bool outdatedLayout = magic.levels != null && magic.levels.Length > 0 &&
                                  wasLayout < SlimeMagic.LayoutVersion;

            if (rebuildLevels || outdatedLayout || magic.levels == null || magic.levels.Length == 0)
            {
                MagicSpellLevel[] previous = magic.levels;
                MagicSpellLevel[] levels = BuildLevels(changes);
                if (levels != null && levels.Length > 0)
                {
                    bool newLayout = wasLayout < SlimeMagic.LayoutVersion;
                    CarryOverTuning(previous, levels, changes, !newLayout);

                    magic.levels = levels;
                    magic.levelsLayout = SlimeMagic.LayoutVersion;
                    magic.RefreshGauge();

                    changes.Append("filled ").Append(levels.Length).Append(" magic levels, ");
                    if (newLayout && previous != null) changes.Append("laid the new flat circles, ");
                }
            }

            // The circles lie flat on the ground now, so a big global size multiplier would only
            // make them overlap again: it is pulled back when a scene predating that layout is
            // upgraded (never after that, so a size chosen in the tuning window stays).
            if (wasLayout < SlimeMagic.FlatLayout && magic.circleScale > 1.6f)
            {
                magic.circleScale = 1.6f;
                changes.Append("circle size multiplier back to 1.6, ");
            }

            if (magic.debrisPrefabs == null || magic.debrisPrefabs.Length == 0)
            {
                GameObject[] debris = FindDebrisPrefabs();
                if (debris != null && debris.Length > 0)
                {
                    magic.debrisPrefabs = debris;
                    changes.Append("linked ").Append(debris.Length).Append(" debris prefab(s), ");
                }
            }

            MagicChargeHUD hud = FindHud(scene);
            if (hud == null)
            {
                var go = new GameObject(HudObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Add the magic bar");
                hud = Undo.AddComponent<MagicChargeHUD>(go);
                changes.Append("added the magic bar, ");
            }
            hud.magic = magic;
            EditorUtility.SetDirty(hud);
            EditorUtility.SetDirty(magic);

            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene) EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = player.gameObject;
            SceneView.RepaintAll();

            Debug.Log("[MysticMap] Slime magic ready on '" + player.name + "': " +
                      (changes.Length > 0 ? changes.ToString().TrimEnd(' ', ',') : "already up to date") +
                      ". Hold left mouse / F: the circles build up level by level, release to cast. " +
                      "MCP > Player > Magic tuning opens the circle size / threshold settings.");

            return changes.Length > 0;
        }

        /// <summary>The magic component of the open scene (used by the tuning window).</summary>
        public static SlimeMagic ActiveMagic() => FindMagic(SceneManager.GetActiveScene());

        static SlimePlayer FindPlayer(Scene scene)
        {
            var players = Object.FindObjectsByType<SlimePlayer>(FindObjectsSortMode.None);
            foreach (SlimePlayer candidate in players)
                if (candidate != null && candidate.gameObject.scene == scene) return candidate;

            return players.Length > 0 ? players[0] : null;
        }

        static SlimeMagic FindMagic(Scene scene)
        {
            var magics = Object.FindObjectsByType<SlimeMagic>(FindObjectsSortMode.None);
            foreach (SlimeMagic candidate in magics)
                if (candidate != null && candidate.gameObject.scene == scene) return candidate;

            return magics.Length > 0 ? magics[0] : null;
        }

        static MagicChargeHUD FindHud(Scene scene)
        {
            var huds = Object.FindObjectsByType<MagicChargeHUD>(FindObjectsSortMode.None);
            foreach (MagicChargeHUD candidate in huds)
                if (candidate != null && candidate.gameObject.scene == scene) return candidate;

            return huds.Length > 0 ? huds[0] : null;
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

        /// <summary>
        /// Keeps the slime scene up to date: it installs the magic when the player has none, and it
        /// rebuilds the five levels when the scene still holds an older layout of them (the
        /// thresholds and damage that were tuned by hand are kept either way).
        /// </summary>
        static void AutoMigrate()
        {
            if (!CanRun()) return;
            if (BuildPipeline.isBuildingPlayer) return;

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;

            if (FindPlayer(scene) == null) return;              // not a slime scene

            SlimeMagic magic = MagicOnPlayer(scene);

            if (magic == null)
            {
                Install(true, false);                          // no magic yet: install it
                return;
            }

            // The magic is there, but the levels may still be an older layout (a bigger set of
            // circles per level, for instance): rebuild them once, keeping the tuning.
            if (magic.levels != null && magic.levels.Length > 0 &&
                magic.levelsLayout < SlimeMagic.LayoutVersion)
                Install(false, false);
        }

        /// <summary>The magic of the scene's slime player - the one the tools work on.</summary>
        static SlimeMagic MagicOnPlayer(Scene scene)
        {
            SlimePlayer player = FindPlayer(scene);
            return player != null ? player.GetComponent<SlimeMagic>() : null;
        }

        // =====================================================================
        //  The five charge levels (built from the Hovl pack)
        // =====================================================================
        static MagicSpellLevel[] BuildLevels(StringBuilder log)
        {
            var levels = new List<MagicSpellLevel>();

            // ---- 1) Gale Slash - the first circle blooms the moment the button goes down -----
            levels.Add(new MagicSpellLevel
            {
                name = "Gale Slash",
                school = "Wind",
                tint = new Color(0.62f, 0.88f, 1f, 1f),
                holdToReach = 0f,
                chargeMoveScale = 0.95f,
                cooldown = 0.35f,
                delivery = SpellDelivery.Projectile,
                damage = 14f,
                speed = 26f,
                radius = 0.75f,
                hugGround = true,
                hugHeight = 0.6f,
                castEffect = FindPrefab("AoE effects", "AoE slash blue"),
                impactEffect = FindPrefab("Hits and explosions", "Green hit"),
                levelBurst = FindPrefab("Sparks", "Sparks explode white"),
                shake = 0.04f,
                knockback = 0.5f,
                layers = new[]
                {
                    // One circle on the ground and one aura around the slime. The next threshold
                    // takes both away and blooms its own, so only ever one of each is out.
                    AsCircle(Layer("Magic circles", "Magic circle", 3.6f, 0.02f, 14f, 0f, 0f, 0f, 0.03f, 0.4f)),
                    AsAura(Layer("Character auras", "Buff", 2.6f, 0.8f, 0f, 0f, 0f, 0f, 0.05f, 0.35f))
                },
                environmentScale = 1f
            });

            // ---- 2) Frost Edge - a tilted ice ring above the first circle, splash on impact ---
            levels.Add(new MagicSpellLevel
            {
                name = "Frost Edge",
                school = "Water",
                tint = new Color(0.5f, 0.8f, 1f, 1f),
                holdToReach = 1.6f,
                chargeMoveScale = 0.8f,
                cooldown = 0.6f,
                delivery = SpellDelivery.Projectile,
                damage = 30f,
                speed = 22f,
                radius = 0.9f,
                hugGround = true,
                hugHeight = 0.5f,
                splash = 0.4f,
                splashRadius = 3.2f,
                castEffect = FindPrefab("Slash effects", "Snow slash"),
                impactEffect = FindPrefab("Hits and explosions", "Snow hit"),
                levelBurst = FindPrefab("Sparks", "Sparks explode blue"),
                shake = 0.06f,
                knockback = 0.8f,
                layers = new[]
                {
                    AsCircle(Layer("Magic circles", "Freeze circle", 4.2f, 0.02f, -18f, 0f, 0f, 0f, 0.04f, 0.4f)),
                    AsAura(Layer("Magic shields", "Magic shield blue", 3.2f, 0.8f, 22f, 0f, 0f, 0f, 0.04f, 0.4f))
                }
            });

            // ---- 3) Volt Arc - a ring that orbits the inner circles, then a laser on the ground
            levels.Add(new MagicSpellLevel
            {
                name = "Volt Arc",
                school = "Thunder",
                tint = new Color(0.72f, 0.7f, 1f, 1f),
                holdToReach = 3.4f,
                chargeMoveScale = 0.65f,
                cooldown = 0.9f,
                delivery = SpellDelivery.ForwardBlast,
                damage = 52f,
                castDistance = 7f,
                radius = 4.5f,
                impactDelay = 0.25f,
                castEffect = FindPrefab("AoE effects", "Laser AOE"),
                centerEffect = FindPrefab("Sparks", "Sparks explode blue"),
                impactEffect = FindPrefab("Hits and explosions", "Electro hit"),
                levelBurst = FindPrefab("Sparks", "Sparks explode yellow"),
                shake = 0.12f,
                knockback = 1.2f,
                layers = new[]
                {
                    AsCircle(Layer("Magic circles", "Magic circle 2", 4.8f, 0.02f, 22f, 0f, 0f, 0f, 0.05f, 0.35f)),
                    AsAura(Layer("Character auras", "Lightning aura", 3.4f, 0.9f, 0f, 0f, 0f, 0f, 0.05f, 0.35f))
                },
                maxChargeEffect = FindPrefab("AoE effects", "Ground AOE explosion"),
                maxChargeRadius = 5.5f,
                maxChargeDamage = 0.8f,
                maxChargeDebris = 16,
                maxChargeShake = 0.5f
            });

            // ---- 4) Crystal Bloom ----------------------------------------------------------
            levels.Add(CrystalBloom());

            // ---- 5) Void Nova --------------------------------------------------------------
            levels.Add(VoidNova());

            if (levels.Count == 0) log.Append("no effects found in the Hovl pack, ");
            return levels.ToArray();
        }

        // ---- 4) Crystal Bloom - nested aura rings, then crystals erupting on the ground ----
        static MagicSpellLevel CrystalBloom()
        {
            return new MagicSpellLevel
            {
                name = "Crystal Bloom",
                school = "Earth",
                tint = new Color(0.6f, 1f, 0.75f, 1f),
                holdToReach = 5.6f,
                chargeMoveScale = 0.5f,
                cooldown = 1.4f,
                delivery = SpellDelivery.ForwardBlast,
                damage = 82f,
                castDistance = 8f,
                radius = 6f,
                impactDelay = 0.45f,
                castEffect = FindPrefab("AoE effects", "Crystals front attack"),
                centerEffect = FindPrefab("Sparks", "Sparks explode green"),
                impactEffect = FindPrefab("Hits and explosions", "Holy hit"),
                levelBurst = FindPrefab("Sparks", "Sparks explode green"),
                shake = 0.2f,
                knockback = 1.6f,
                layers = new[]
                {
                    AsCircle(Layer("Magic circles", "Healing circle", 5.4f, 0.02f, -16f, 0f, 0f, 0f, 0.04f, 0.4f)),
                    AsAura(Layer("Character auras", "Healing", 3.6f, 0.9f, 0f, 0f, 0f, 0f, 0.05f, 0.4f))
                },
                maxChargeEffect = FindPrefab("AoE effects", "Smoke AOE explosion"),
                maxChargeRadius = 7f,
                maxChargeDamage = 0.85f,
                maxChargeDebris = 18,
                maxChargeShake = 0.55f
            };
        }

        // ---- 5) Void Nova - the whole cage of rings plus a shield, meteors and a nova -------
        static MagicSpellLevel VoidNova()
        {
            return new MagicSpellLevel
            {
                name = "Void Nova",
                school = "Void",
                tint = new Color(0.95f, 0.45f, 0.85f, 1f),
                holdToReach = 8.4f,
                chargeMoveScale = 0.35f,
                cooldown = 2.6f,
                delivery = SpellDelivery.Nova,
                damage = 140f,
                radius = 9f,
                impactDelay = 0.5f,
                castEffect = FindPrefab("AoE effects", "Meteors AOE"),
                centerEffect = FindPrefab("Sparks", "Sparks explode red"),
                impactEffect = FindPrefab("Hits and explosions", "Explosion"),
                levelBurst = FindPrefab("Sparks", "Sparks explode red"),
                shake = 0.35f,
                knockback = 2.5f,
                layers = new[]
                {
                    AsCircle(Layer("AoE effects", "Plexus AoE", 6.5f, 0.02f, -10f, 0f, 0f, 0f, 0.03f, 0.8f,
                                   new Color(1f, 0.75f, 1f, 1f))),
                    AsAura(Layer("Character auras", "Debuff", 4.2f, 1f, 0f, 0f, 0f, 0f, 0.06f, 0.4f))
                },
                maxChargeEffect = FindPrefab("AoE effects", "Red energy explosion"),
                maxChargeRadius = 10f,
                maxChargeDamage = 0.9f,
                maxChargeDebris = 24,
                maxChargeShake = 0.7f
            };
        }

        /// <summary>
        /// One effect layer of a level. The effect prefab is resolved as "folder/name" inside the
        /// pack's Prefabs folder, and the motion is written in the order it is easiest to reason
        /// about: size, height, spin, orbit (with its radius), tilt, pulse, bloom time.
        /// </summary>
        static MagicLayer Layer(string folder, string prefabName, float diameter, float height,
                                float spin, float orbit, float orbitRadius, float tilt,
                                float pulse, float growIn, Color tint = default)
        {
            return new MagicLayer
            {
                effect = FindPrefab(folder, prefabName),
                diameter = diameter,
                height = height,
                spin = spin,
                orbit = orbit,
                orbitRadius = orbitRadius,
                tilt = tilt,
                pulse = pulse,
                growIn = growIn,
                tint = tint.a <= 0f ? Color.white : tint
            };
        }

        /// <summary>
        /// Finds one effect prefab in the pack: first at the expected path, then by name anywhere
        /// inside "Assets/Hovl Studio" (so the levels survive the folder being reorganised).
        /// </summary>
        static GameObject FindPrefab(string folder, string prefabName)
        {
            GameObject found = AssetDatabase.LoadAssetAtPath<GameObject>(
                PackRoot + "/" + folder + "/" + prefabName + ".prefab");
            if (found != null) return found;

            string[] guids = AssetDatabase.FindAssets(prefabName + " t:Prefab",
                                                      new[] { "Assets/Hovl Studio" });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (System.IO.Path.GetFileNameWithoutExtension(path) != prefabName) continue;

                found = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (found != null) return found;
            }

            Debug.LogWarning("[MysticMap] Hovl effect '" + folder + "/" + prefabName +
                             "' was not found - that part of the magic will stay empty.");
            return null;
        }

        /// <summary>
        /// Turns a layer into a clock face: little copies of an effect travel around its rim, the
        /// way sparks and symbols walk around a magic circle in anime. Leave
        /// <paramref name="folder"/> / <paramref name="prefabName"/> empty to reuse the layer's own
        /// effect for the riders.
        ///
        /// The five built levels no longer use it - they are one circle and one aura each - but it
        /// is here for any layer that should get the pips back, and the tuning window can raise the
        /// rider count of a single circle instead.
        /// </summary>
        static MagicLayer Clock(MagicLayer layer, string folder, string prefabName, int riders,
                                float lap, float riderScale = 0.22f, float offset = 0.06f)
        {
            if (layer == null) return null;

            layer.rimRiders = Mathf.Clamp(riders, 0, 16);
            layer.riderLap = Mathf.Max(0f, lap);
            layer.riderScale = Mathf.Clamp(riderScale, 0.04f, 1f);
            layer.riderOffset = Mathf.Clamp(offset, 0f, 0.8f);
            layer.riderEffect = (!string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(prefabName))
                ? FindPrefab(folder, prefabName)
                : null;

            return layer;
        }

        /// <summary>
        /// Marks a layer as the magic circle of its level. Only one circle is ever out: when a
        /// later threshold brings its own, the one before it collapses first - so the magic
        /// changes shape instead of stacking circles on top of each other.
        /// </summary>
        static MagicLayer AsCircle(MagicLayer layer)
        {
            if (layer != null) layer.group = MagicLayerGroup.Circle;
            return layer;
        }

        /// <summary>Marks a layer as the aura of its level (only one aura is ever out).</summary>
        static MagicLayer AsAura(MagicLayer layer)
        {
            if (layer != null) layer.group = MagicLayerGroup.Aura;
            return layer;
        }

        /// <summary>
        /// The prefabs the magic throws around as debris: the small stones (and, when there are
        /// none, a few rocks) of the streamed world's own palette, so the debris matches the map.
        /// Anything too big is shrunk to pebble size when it is thrown, so this can never look wrong.
        /// </summary>
        static GameObject[] FindDebrisPrefabs()
        {
            var list = new List<GameObject>();

            var managers = Object.FindObjectsByType<World.ChunkManager>(FindObjectsSortMode.None);
            foreach (World.ChunkManager manager in managers)
            {
                World.WorldSettings settings = manager != null ? manager.settings : null;
                World.WorldPrefabPalette palette = settings != null ? settings.palette : null;
                if (palette == null) continue;

                AddDebris(list, palette.stones, 6);
                AddDebris(list, palette.rocks, 3);
                if (list.Count > 0) break;
            }

            return list.ToArray();
        }

        static void AddDebris(List<GameObject> list, GameObject[] source, int max)
        {
            if (source == null) return;

            int added = 0;
            foreach (GameObject prefab in source)
            {
                if (prefab == null || list.Contains(prefab) || added >= max) continue;
                list.Add(prefab);
                added++;
            }
        }

        /// <summary>
        /// The starting point of everything the tuning window edits: circles well over 4 m across,
        /// thresholds spread out over eight seconds, and a world that really reacts (grass torn
        /// away, rocks shattering at maximum charge).
        /// </summary>
        public static void ApplyTuningDefaults(SlimeMagic magic)
        {
            if (magic == null) return;

            magic.circleScale = 1.35f;
            magic.circleHeightScale = 1f;
            magic.chargeSwell = 0.22f;
            magic.chargeSpinUp = 1.2f;
            magic.swapCirclesEachLevel = true;
            magic.circlesBounceWithSlime = false;
            magic.groundLift = 0.03f;
            magic.groundMask = ~0;

            magic.thresholdScale = 1f;
            magic.thresholdBias = 0f;

            magic.environmentScale = 1f;
            magic.auraWindStrength = 0.6f;
            magic.auraWindScale = 1f;

            magic.rockBreakPower = 1.5f;
            magic.debrisPerHit = 6;
            magic.debrisPerExplosion = 16;

            magic.RefreshGauge();
            EditorUtility.SetDirty(magic);
        }

        /// <summary>
        /// Copies the numbers a designer dialled in by hand onto freshly built levels, so
        /// "Rebuild the 5 levels" swaps the effect layout without ever throwing away the tuning.
        ///
        /// The thresholds and the damage always come across. The circle sizes / heights / riders
        /// only come across when <paramref name="circles"/> is true - that is when the levels were
        /// already built with the current layout (see <see cref="SlimeMagic.LayoutVersion"/>); an
        /// upgrade to a new layout keeps the balance but brings the new look.
        /// </summary>
        static void CarryOverTuning(MagicSpellLevel[] previous, MagicSpellLevel[] fresh,
                                    StringBuilder log, bool circles)
        {
            if (previous == null || fresh == null || previous.Length == 0) return;

            int levels = Mathf.Min(previous.Length, fresh.Length);
            int carried = 0;

            for (int i = 0; i < levels; i++)
            {
                MagicSpellLevel was = previous[i];
                MagicSpellLevel now = fresh[i];
                if (was == null || now == null) continue;

                if (was.holdToReach > 0.001f) now.holdToReach = was.holdToReach;
                now.damage = was.damage;
                now.environmentScale = was.environmentScale;
                if (was.maxChargeRadius > 0.001f) now.maxChargeRadius = was.maxChargeRadius;

                if (!circles) continue;

                now.circleScale = was.circleScale;

                if (was.layers == null || now.layers == null) continue;

                int layerCount = Mathf.Min(was.layers.Length, now.layers.Length);

                for (int j = 0; j < layerCount; j++)
                {
                    MagicLayer layerWas = was.layers[j];
                    MagicLayer layerNow = now.layers[j];
                    if (layerWas == null || layerNow == null) continue;

                    layerNow.diameter = layerWas.diameter;
                    layerNow.height = layerWas.height;
                    layerNow.rimRiders = layerWas.rimRiders;
                    layerNow.riderScale = layerWas.riderScale;
                    layerNow.riderOffset = layerWas.riderOffset;
                    layerNow.riderLap = layerWas.riderLap;
                    carried++;
                }
            }

            if (log != null && levels > 0)
                log.Append(circles
                    ? "kept your tuning of " + levels + " levels / " + carried + " circles, "
                    : "kept your thresholds and damage, ");
        }

        static bool CanRun()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode
                && !EditorApplication.isCompiling
                && !EditorApplication.isUpdating;
        }
    }
}
#endif
