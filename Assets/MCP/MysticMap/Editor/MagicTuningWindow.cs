#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// The magic tuning window (MCP > Player > Magic tuning): every number that decides how the
    /// slime's hold-to-charge magic looks and feels, applied live to the open scene.
    ///
    ///   * Circles  - how large the magic circles are (the global multiplier, one level's own
    ///                multiplier, or the radius of a single circle) and how they charge up.
    ///   * Every circle - the size, height, spin, orbit (the clockwork around the main circle) and
    ///                clock riders of each layer, one by one.
    ///   * Thresholds - how long each level takes to charge, both with a global multiplier and per
    ///                level, with the effective seconds shown next to every level.
    ///   * World    - how much the magic disturbs the ground (grass torn away, trees shaken, rocks
    ///                shattered at maximum charge) and how much debris it throws.
    ///   * Saving   - the numbers live on the SlimeMagic component and are also snapshotted into
    ///                MagicTuningPreset.asset, so nothing is lost when the levels are rebuilt or
    ///                when tuning is done while the game plays.
    ///
    /// While the game is playing, a change is applied to the circles that are already charging, so
    /// a slider can be dragged and the result watched in the game view.
    /// </summary>
    public class MagicTuningWindow : EditorWindow
    {
        SlimeMagic _magic;
        Vector2 _scroll;

        bool _circlesOpen = true;
        bool _eachCircleOpen;
        bool _thresholdsOpen = true;
        bool _worldOpen = true;
        bool _savingOpen;
        bool _castOpen;

        [MenuItem("MCP/Player/Magic tuning (circles, thresholds, world)")]
        public static void Open()
        {
            var window = GetWindow<MagicTuningWindow>(true, "Magic tuning");
            window.minSize = new Vector2(430f, 540f);
            window.Find();
            window.Repaint();
        }

        void OnEnable() => Find();

        void OnInspectorUpdate() => Repaint();       // keeps the live read-out moving in play mode

        void Find() => _magic = SlimeMagicSetup.ActiveMagic();

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Slime magic tuning", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The numbers are stored on the SlimeMagic component (so they are part of the scene) " +
                "and snapshotted into MagicTuningPreset.asset, which also catches anything you tune " +
                "while the game plays. While charging, a change is applied to the circles already out.",
                MessageType.None);

            GUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            _magic = (SlimeMagic)EditorGUILayout.ObjectField("Slime Magic", _magic, typeof(SlimeMagic), true);
            if (EditorGUI.EndChangeCheck() && _magic != null) SceneView.RepaintAll();

            if (_magic == null)
            {
                EditorGUILayout.HelpBox("No SlimeMagic in the open scene. Run " +
                                        "MCP > Player > Slime magic (Hovl circles) first.",
                                        MessageType.Info);

                if (GUILayout.Button("Look for it again")) Find();
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawCircles();
            DrawEachCircle();
            DrawThresholds();
            DrawWorld();
            DrawCast();
            DrawPersistence();
            DrawBottom();

            EditorGUILayout.EndScrollView();
        }

        // =====================================================================
        //  Circles
        // =====================================================================
        void DrawCircles()
        {
            _circlesOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_circlesOpen, "Magic circles - size");

            if (_circlesOpen)
            {
                EditorGUI.BeginChangeCheck();

                _magic.circleScale = EditorGUILayout.Slider(
                    new GUIContent("Circle size", "Multiplies every circle's diameter and its orbit radius."),
                    _magic.circleScale, 0.25f, 4f);

                _magic.circleHeightScale = EditorGUILayout.Slider(
                    new GUIContent("Circle height", "Multiplies how high above the slime the circles sit."),
                    _magic.circleHeightScale, 0.25f, 4f);

                _magic.chargeSwell = EditorGUILayout.Slider(
                    new GUIContent("Swell while charging",
                                   "How much the circles that are out grow as the bar fills toward the " +
                                   "next threshold (0 = they keep their size)."),
                    _magic.chargeSwell, 0f, 1f);

                _magic.chargeSpinUp = EditorGUILayout.Slider(
                    new GUIContent("Spin up while charging",
                                   "How much faster the circles and their riders turn at the top of the bar."),
                    _magic.chargeSpinUp, 0f, 3f);

                if (EditorGUI.EndChangeCheck()) Touch();

                // Circles drawn on the floor: the rig snaps down to the terrain instead of bouncing
                // up and down with the slime.
                EditorGUI.BeginChangeCheck();

                _magic.swapCirclesEachLevel = EditorGUILayout.Toggle(
                    new GUIContent("Only the current circle and aura",
                                   "On = reaching a threshold takes the previous magic circle and " +
                                   "aura away and blooms the new pair, so charging never fills the " +
                                   "screen with circles."),
                    _magic.swapCirclesEachLevel);

                _magic.circlesBounceWithSlime = EditorGUILayout.Toggle(
                    new GUIContent("Bounce with the slime",
                                   "Off = the circles keep lying flat on the ground and the slime " +
                                   "bounces through them."),
                    _magic.circlesBounceWithSlime);

                using (new EditorGUI.DisabledScope(_magic.circlesBounceWithSlime))
                {
                    _magic.groundLift = EditorGUILayout.Slider(
                        new GUIContent("Ground lift", "How far above the ground the circles lie."),
                        _magic.groundLift, 0f, 1f);
                }

                if (EditorGUI.EndChangeCheck()) Touch();

                var groundProperty = new SerializedObject(_magic);
                groundProperty.Update();
                EditorGUILayout.PropertyField(groundProperty.FindProperty("groundMask"),
                    new GUIContent("Ground is", "What the flat circles are laid on. Nothing = everything."));
                groundProperty.ApplyModifiedProperties();

                if (GUILayout.Button("Lay every circle flat on the ground")) FlattenCircles();

                // What the numbers actually mean, so the sliders never feel abstract.
                MagicSpellLevel biggest = BiggestLevel(out float biggestDiameter);
                EditorGUILayout.LabelField("Biggest circle now",
                    (biggestDiameter * _magic.circleScale).ToString("0.0") + " m" +
                    (biggest != null ? "  (" + biggest.name + ")" : string.Empty), EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Levels with riders on the rim", CountClockLevels() + " of " +
                                           _magic.LevelCount, EditorStyles.miniLabel);

                if (Application.isPlaying)
                    EditorGUILayout.HelpBox("Charging now: level " + _magic.ReachedLevel + " - " +
                                            _magic.ChargeTime.ToString("0.00") + "s of " +
                                            _magic.GaugeLength.ToString("0.0") + "s, " +
                                            _magic.DamageNow.ToString("0") + " damage" +
                                            (_magic.OverchargeFraction >= 0.999f ? "  (MAX CHARGE)" : string.Empty) +
                                            "\nTowards the next circle: " +
                                            (_magic.ChargeFill() * 100f).ToString("0") + "%",
                                            MessageType.None);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Every circle x1.2")) ScaleAllCircles(1.2f);
                if (GUILayout.Button("x0.8")) ScaleAllCircles(0.8f);
                if (GUILayout.Button("Rebuild the circles now")) Touch();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("Easier per circle", "open \"Every circle\" below",
                                           EditorStyles.miniLabel);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // =====================================================================
        //  Every single circle
        // =====================================================================
        void DrawEachCircle()
        {
            _eachCircleOpen = EditorGUILayout.BeginFoldoutHeaderGroup(
                _eachCircleOpen, "Every circle - size, height, spin, orbit, riders");

            if (_eachCircleOpen)
            {
                if (_magic.levels == null || _magic.levels.Length == 0)
                {
                    EditorGUILayout.HelpBox("No levels yet - rebuild them from the Hovl pack below.",
                                            MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "Orbit 0 = the circle rides the rim of the biggest one (clockwork hands); any " +
                        "other number is degrees per second around the slime.", EditorStyles.miniLabel);

                    for (int i = 0; i < _magic.levels.Length; i++)
                    {
                        MagicSpellLevel level = _magic.levels[i];
                        if (level == null) continue;

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField((i + 1) + ". " + level.name, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(_magic.EffectiveHold(level).ToString("0.00") + "s",
                                                   EditorStyles.miniLabel, GUILayout.Width(56f));
                        EditorGUILayout.EndHorizontal();

                        EditorGUI.indentLevel++;

                        EditorGUI.BeginChangeCheck();
                        level.circleScale = EditorGUILayout.Slider(
                            new GUIContent("This level's circles",
                                           "Multiplies every diameter below: a later threshold can bloom " +
                                           "visibly bigger circles."),
                            level.circleScale, 0.25f, 3f);
                        if (EditorGUI.EndChangeCheck()) Touch(true);

                        DrawLayers(level);

                        EditorGUI.indentLevel--;
                        GUILayout.Space(3);
                    }
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        /// <summary>The circles of one level, one row of sliders each.</summary>
        void DrawLayers(MagicSpellLevel level)
        {
            if (level.layers == null) return;

            float levelScale = Mathf.Max(0.25f, level.circleScale);

            for (int i = 0; i < level.layers.Length; i++)
            {
                MagicLayer layer = level.layers[i];
                if (layer == null) continue;

                float shown = layer.diameter * levelScale * Mathf.Max(0.05f, _magic.circleScale);

                EditorGUILayout.LabelField((layer.effect != null ? layer.effect.name : "empty") +
                                           "  ->  " + shown.ToString("0.0") + " m",
                                           EditorStyles.miniBoldLabel);

                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();

                layer.diameter = EditorGUILayout.Slider(
                    new GUIContent("Size (m)", "The layer's own diameter, before the multipliers."),
                    layer.diameter, 0.2f, 30f);

                layer.height = EditorGUILayout.Slider(
                    new GUIContent("Height (m)", "Height above the slime's feet."),
                    layer.height, -1f, 6f);

                layer.spin = EditorGUILayout.Slider(
                    new GUIContent("Spin", "Degrees per second on the circle's own axis."),
                    layer.spin, -360f, 360f);

                layer.orbit = EditorGUILayout.Slider(
                    new GUIContent("Orbit", "Degrees per second around the slime (0 = it stays put)."),
                    layer.orbit, -360f, 360f);

                layer.orbitRadius = EditorGUILayout.Slider(
                    new GUIContent("Orbit radius (m)", "0 = ride the rim of the biggest circle."),
                    layer.orbitRadius, 0f, 12f);

                layer.rimRiders = EditorGUILayout.IntSlider(
                    new GUIContent("Clock riders", "Little copies of the effect walking the rim."),
                    layer.rimRiders, 0, 16);

                if (layer.rimRiders > 0)
                {
                    layer.riderScale = EditorGUILayout.Slider(new GUIContent("Rider size"),
                                                              layer.riderScale, 0.04f, 1f);
                    layer.riderLap = EditorGUILayout.Slider(new GUIContent("Rider lap (s)"),
                                                            layer.riderLap, 0f, 30f);
                }

                layer.growIn = EditorGUILayout.Slider(new GUIContent("Bloom time (s)"),
                                                      layer.growIn, 0f, 2f);

                layer.pulse = EditorGUILayout.Slider(new GUIContent("Breathing"),
                                                     layer.pulse, 0f, 0.5f);

                layer.tilt = EditorGUILayout.Slider(
                    new GUIContent("Tilt", "0 = flat on the ground, 90 = standing upright."),
                    layer.tilt, -90f, 90f);

                layer.group = (MagicLayerGroup)EditorGUILayout.EnumPopup(
                    new GUIContent("One at a time",
                                   "Circle = this is the level's magic circle, Aura = its aura. Only " +
                                   "one of each is ever out: the next threshold's circle / aura takes " +
                                   "this one's place. Alongside = it is simply added."),
                    layer.group);

                bool changed = EditorGUI.EndChangeCheck();
                bool layFlat = GUILayout.Button("Lay this circle flat on the ground");

                if (layFlat)
                {
                    layer.tilt = 0f;
                    layer.height = Mathf.Min(layer.height, 0.08f);
                }

                if (changed || layFlat) Touch(true);
                EditorGUI.indentLevel--;
                GUILayout.Space(2);
            }
        }

        /// <summary>Multiplies every layer's diameter on every level (one undo step).</summary>
        void ScaleAllCircles(float factor)
        {
            if (_magic == null || _magic.levels == null) return;

            Undo.RecordObject(_magic, "Scale the magic circles");

            foreach (MagicSpellLevel level in _magic.levels)
            {
                if (level == null || level.layers == null) continue;

                foreach (MagicLayer layer in level.layers)
                    if (layer != null) layer.diameter = Mathf.Clamp(layer.diameter * factor, 0.2f, 30f);
            }

            Touch(true);
        }

        /// <summary>
        /// Turns every circle of every level into a flat ring lying just above the ground (auras,
        /// which sit around the body, are left alone). One undo step.
        /// </summary>
        void FlattenCircles()
        {
            if (_magic == null || _magic.levels == null) return;

            Undo.RecordObject(_magic, "Lay the magic circles flat");

            foreach (MagicSpellLevel level in _magic.levels)
            {
                if (level == null || level.layers == null) continue;

                foreach (MagicLayer layer in level.layers)
                {
                    if (layer == null || layer.group == MagicLayerGroup.Aura) continue;   // auras sit on the body

                    layer.tilt = 0f;
                    layer.height = Mathf.Clamp(layer.height, 0.02f, 0.12f);
                }
            }

            Touch(true);
        }

        // =====================================================================
        //  Thresholds
        // =====================================================================
        void DrawThresholds()
        {
            _thresholdsOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_thresholdsOpen, "Charge thresholds");

            if (_thresholdsOpen)
            {
                EditorGUI.BeginChangeCheck();

                _magic.thresholdScale = EditorGUILayout.Slider(
                    new GUIContent("Spread", "Multiplies every level's hold-to-reach time."),
                    _magic.thresholdScale, 0.25f, 4f);

                _magic.thresholdBias = EditorGUILayout.Slider(
                    new GUIContent("Extra seconds", "Added to every threshold except the first."),
                    _magic.thresholdBias, -1f, 4f);

                if (EditorGUI.EndChangeCheck()) Touch();

                GUILayout.Space(4);
                EditorGUILayout.LabelField("Per level", EditorStyles.boldLabel);

                if (_magic.levels == null || _magic.levels.Length == 0)
                {
                    EditorGUILayout.HelpBox("No levels yet - rebuild them from the Hovl pack below.",
                                            MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Level", EditorStyles.miniBoldLabel, GUILayout.Width(118f));
                    EditorGUILayout.LabelField("Hold", EditorStyles.miniBoldLabel, GUILayout.Width(48f));
                    EditorGUILayout.LabelField("Damage", EditorStyles.miniBoldLabel, GUILayout.Width(48f));
                    EditorGUILayout.LabelField("Really", EditorStyles.miniBoldLabel);
                    EditorGUILayout.EndHorizontal();

                    for (int i = 0; i < _magic.levels.Length; i++)
                    {
                        MagicSpellLevel level = _magic.levels[i];
                        if (level == null) continue;

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField((i + 1) + ". " + level.name, GUILayout.Width(118f));

                        EditorGUI.BeginChangeCheck();
                        level.holdToReach = EditorGUILayout.FloatField(Mathf.Max(0f, level.holdToReach),
                                                                       GUILayout.Width(48f));
                        level.damage = EditorGUILayout.FloatField(Mathf.Max(0f, level.damage), GUILayout.Width(48f));
                        if (EditorGUI.EndChangeCheck()) Touch(true);

                        string extra = level.holdToReach <= 0.01f ? "instant" : level.holdToReach.ToString("0.0") + " x";
                        EditorGUILayout.LabelField(_magic.EffectiveHold(level).ToString("0.00") + "s (" + extra + ")",
                                                   EditorStyles.miniLabel);
                        EditorGUILayout.EndHorizontal();
                    }
                }

                GUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Spread out (x1.3)")) { _magic.thresholdScale = Mathf.Clamp(_magic.thresholdScale * 1.3f, 0.25f, 4f); Touch(); }
                if (GUILayout.Button("Compact (x0.75)")) { _magic.thresholdScale = Mathf.Clamp(_magic.thresholdScale * 0.75f, 0.25f, 4f); Touch(); }
                if (GUILayout.Button("Reset")) { _magic.thresholdScale = 1f; _magic.thresholdBias = 0f; Touch(); }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("Whole bar", _magic.GaugeLength.ToString("0.00") + " s",
                                           EditorStyles.miniLabel);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // =====================================================================
        //  The world's reaction
        // =====================================================================
        void DrawWorld()
        {
            _worldOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_worldOpen, "World reaction & destruction");

            if (_worldOpen)
            {
                EditorGUI.BeginChangeCheck();

                _magic.environmentScale = EditorGUILayout.Slider(
                    new GUIContent("World radius", "Multiplies every radius the magic applies to the world."),
                    _magic.environmentScale, 0f, 3f);

                _magic.auraWindStrength = EditorGUILayout.Slider(
                    new GUIContent("Charging aura", "How hard the charging aura bends the grass (0 = none)."),
                    _magic.auraWindStrength, 0f, 2f);

                _magic.auraWindScale = EditorGUILayout.Slider(
                    new GUIContent("Aura radius", "Aura radius as a multiple of the biggest circle."),
                    _magic.auraWindScale, 0f, 3f);

                _magic.rockBreakPower = EditorGUILayout.Slider(
                    new GUIContent("Rock break power", "1.5 = only a maximum-charge level 4 / 5 shatters rocks."),
                    _magic.rockBreakPower, 0.5f, 3f);

                _magic.debrisPerHit = EditorGUILayout.IntSlider(
                    new GUIContent("Debris per hit"), _magic.debrisPerHit, 0, 24);

                _magic.debrisPerExplosion = EditorGUILayout.IntSlider(
                    new GUIContent("Debris per explosion"), _magic.debrisPerExplosion, 0, 40);

                if (EditorGUI.EndChangeCheck()) Touch();

                EditorGUILayout.LabelField("Debris prefabs (empty = pebbles are built on the fly)",
                                           EditorStyles.miniLabel);
                EditorGUI.indentLevel++;
                var serialized = new SerializedObject(_magic);
                serialized.Update();
                EditorGUILayout.PropertyField(serialized.FindProperty("debrisPrefabs"), GUIContent.none, true);
                serialized.ApplyModifiedProperties();
                EditorGUI.indentLevel--;

                if (Application.isPlaying)
                {
                    MagicEnvironment environment = MagicEnvironment.Active;
                    EditorGUILayout.LabelField("Live",
                        environment == null
                            ? "no environment yet (the first chunk creates it)"
                            : environment.FlyingCount + " flying, " + environment.SwayingCount + " leaning, " +
                              MagicDebris.Alive + " debris pieces",
                        EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // =====================================================================
        //  Cast feel
        // =====================================================================
        void DrawCast()
        {
            _castOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_castOpen, "Cast & overcharge");

            if (_castOpen)
            {
                EditorGUI.BeginChangeCheck();

                _magic.overchargeWindow = EditorGUILayout.Slider(
                    new GUIContent("Overcharge window", "Seconds of extra holding that still add damage."),
                    _magic.overchargeWindow, 0.25f, 8f);

                _magic.overchargePerSecond = EditorGUILayout.Slider(
                    new GUIContent("Overcharge / second", "Extra damage per second, as a fraction of the level's."),
                    _magic.overchargePerSecond, 0f, 1f);

                _magic.overchargeCap = EditorGUILayout.Slider(
                    new GUIContent("Overcharge cap", "Most extra damage overcharge may add (1 = double)."),
                    _magic.overchargeCap, 0f, 3f);

                _magic.minimumHoldToCast = EditorGUILayout.Slider(
                    new GUIContent("Minimum hold", "Releasing sooner than this is a fizzle."),
                    _magic.minimumHoldToCast, 0f, 0.6f);

                _magic.releaseCollapse = EditorGUILayout.Slider(
                    new GUIContent("Collapse time", "Seconds the circles take to collapse on release."),
                    _magic.releaseCollapse, 0.02f, 1f);

                if (EditorGUI.EndChangeCheck()) Touch(true);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        // =====================================================================
        //  Saving the tuning
        // =====================================================================
        void DrawPersistence()
        {
            _savingOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_savingOpen, "Saving the tuning");

            if (_savingOpen)
            {
                EditorGUILayout.HelpBox(
                    "The numbers live on the SlimeMagic component (so they are part of the scene) and a " +
                    "copy is kept in " + MagicTuningPresetStore.AssetPath + ". Rebuilding the levels keeps " +
                    "your thresholds and circle sizes, and anything tuned while the game plays is " +
                    "snapshotted the moment the game stops (" + MagicTuningPresetStore.BackupPath + ") " +
                    "and put back right after.", MessageType.None);

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Save this tuning to the preset"))
                {
                    if (MagicTuningPresetStore.Save(_magic))
                        Debug.Log("[MysticMap] Magic tuning saved to " + MagicTuningPresetStore.AssetPath + ".");
                    else
                        Debug.LogWarning("[MysticMap] The magic tuning could not be saved.");
                }

                using (new EditorGUI.DisabledScope(!MagicTuningPresetStore.HasSaved))
                {
                    if (GUILayout.Button("Put the saved tuning back") &&
                        MagicTuningPresetStore.Apply(_magic))
                        Debug.Log("[MysticMap] Magic tuning loaded from " + MagicTuningPresetStore.AssetPath + ".");
                }

                EditorGUILayout.EndHorizontal();

                using (new EditorGUI.DisabledScope(!MagicTuningPresetStore.HasBackup))
                {
                    if (GUILayout.Button("Put back the tuning I changed while playing") &&
                        MagicTuningPresetStore.ApplyBackup(_magic))
                        Debug.Log("[MysticMap] Magic tuning restored from " +
                                  MagicTuningPresetStore.BackupPath + ".");
                }

                MagicTuningPresetStore.RestoreAfterPlay = EditorGUILayout.Toggle(
                    new GUIContent("Put the play-mode tuning back when the game stops",
                                   "Off = the snapshot is still written, you decide when to load it."),
                    MagicTuningPresetStore.RestoreAfterPlay);

                if (GUILayout.Button("Save the scene now")) SaveSceneNow();

                EditorGUILayout.LabelField("Preset asset",
                                           MagicTuningPresetStore.HasSaved ? "saved" : "not saved yet",
                                           EditorStyles.miniLabel);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        /// <summary>Writes the open scene (and any dirty assets) to disk right now.</summary>
        void SaveSceneNow()
        {
            if (_magic == null) return;

            Scene scene = _magic.gameObject.scene;
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.SaveScene(scene);

            AssetDatabase.SaveAssets();
        }

        // =====================================================================
        //  Buttons and helpers
        // =====================================================================
        void DrawBottom()
        {
            GUILayout.Space(6);

            if (GUILayout.Button("Reset the circles / thresholds / world to the defaults"))
            {
                Undo.RecordObject(_magic, "Magic tuning defaults");
                SlimeMagicSetup.ApplyTuningDefaults(_magic);
                Touch(true);
            }

            if (GUILayout.Button("Rebuild the 5 levels from the Hovl pack (keeps your tuning)"))
                SlimeMagicSetup.RebuildFromMenu();
            if (GUILayout.Button("Add magic test targets (3 dummies)")) SlimeMagicSetup.AddTestTargets();

            EditorGUILayout.LabelField(
                "The numbers live on the SlimeMagic component and are snapshotted into " +
                MagicTuningPresetStore.AssetPath + ", so they survive a rebuild and play mode.",
                EditorStyles.miniLabel);
        }

        /// <summary>Pushes an edited value everywhere it matters (bar length, live circles, scene).</summary>
        void Touch(bool structural = false)
        {
            if (_magic == null) return;

            Undo.RecordObject(_magic, "Magic tuning");
            _magic.RefreshGauge();
            _magic.ApplyTuning();

            // Marks the component, the scene (or the prefab) and the preview dirty in one go, so
            // what the window writes really is what gets saved.
            MagicTuningPresetStore.MarkDirty(_magic);

            if (structural) SceneView.RepaintAll();
        }

        /// <summary>The level with the biggest circle, and that diameter (before the tuning).</summary>
        MagicSpellLevel BiggestLevel(out float diameter)
        {
            MagicSpellLevel found = null;
            diameter = 0f;

            if (_magic == null || _magic.levels == null) return null;

            foreach (MagicSpellLevel level in _magic.levels)
            {
                if (level == null || level.layers == null) continue;

                foreach (MagicLayer layer in level.layers)
                {
                    if (layer == null || !layer.FitsToDiameter) continue;
                    if (layer.diameter <= diameter) continue;

                    diameter = layer.diameter;
                    found = level;
                }
            }

            return found;
        }

        /// <summary>How many levels have at least one layer with riders walking its rim.</summary>
        int CountClockLevels()
        {
            int count = 0;
            if (_magic == null || _magic.levels == null) return 0;

            foreach (MagicSpellLevel level in _magic.levels)
            {
                if (level == null || level.layers == null) continue;

                foreach (MagicLayer layer in level.layers)
                {
                    if (layer == null || !layer.HasRiders) continue;
                    count++;
                    break;
                }
            }

            return count;
        }
    }
}
#endif
