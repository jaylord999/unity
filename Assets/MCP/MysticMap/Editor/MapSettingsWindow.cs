#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// A settings panel (menu: MCP -> Map Settings) to tune the Mystic Map:
    /// grass density/size, render distance and fog, plus quick buttons to add
    /// more stones/plants/flowers/grass into the open scene.
    /// </summary>
    public class MysticMapSettingsWindow : EditorWindow
    {
        float grassDensity = 1f;
        float grassScale = 1f;
        float grassDist = 60f;   // how far grass still renders (m)
        float glowDist = 40f;    // how far the glow under grass shows (m)
        float renderDist = 130f;
        float fogDensity = 0.008f;

        // Terrain hills
        float hillHeight = 30f;
        float hillScale = 1f;

        // Large border hills around the edge of the map
        bool borderOn = true;
        float borderHeight = 88f;

        // Magical floating dust (rises from the ground near the player)
        bool dustOn = true;
        Color dustColor = new Color(0.7f, 0.95f, 1f, 1f);
        int dustCount = 60;
        float dustRadius = 16f;
        float dustSize = 0.22f;
        float dustSpeed = 0.9f;
        float dustGlow = 1.2f;
        float dustPulse = 0.55f;
        float dustPulseSpeed = 1.6f;
        bool dustLight = true;
        float dustLightStrength = 0.6f;

        // Grass color / brightness
        float grassBrightness = 1.3f;
        Color grassColor = new Color(0.35f, 0.9f, 0.3f, 1f);

        // Grass glow (under each grass sprite)
        bool glowOn = true;
        Color glowColor = new Color(0.75f, 1f, 0.6f, 1f);
        float glowStrength = 0.6f;
        float glowSize = 1.2f;
        float glowHeight = 0.25f;
        float glowSpacing = 14f;

        Vector2 _scroll;

        [MenuItem("MCP/Map Settings (grass, fog, distance)")]
        public static void Open()
        {
            var w = GetWindow<MysticMapSettingsWindow>(true, "Mystic Map Settings");
            w.minSize = new Vector2(360, 460);
            w.Load();
        }

        void OnEnable() => Load();

        void Load()
        {
            grassDensity = EditorPrefs.GetFloat("MM.GrassDensity", 1f);
            grassScale = EditorPrefs.GetFloat("MM.GrassScale", 1f);
            grassDist = EditorPrefs.GetFloat("MM.GrassDist", 60f);
            glowDist = EditorPrefs.GetFloat("MM.GlowDist", 40f);
            renderDist = EditorPrefs.GetFloat("MM.RenderDist", 130f);
            fogDensity = EditorPrefs.GetFloat("MM.FogDensity", 0.008f);

            hillHeight = EditorPrefs.GetFloat("MM.HillHeight", 30f);
            hillScale = EditorPrefs.GetFloat("MM.HillScale", 1f);

            borderOn = EditorPrefs.GetBool("MM.BorderHills", true);
            borderHeight = EditorPrefs.GetFloat("MM.BorderHeight", 88f);

            dustOn = EditorPrefs.GetBool("MM.DustOn", true);
            dustColor = LoadColor("MM.DustColor", new Color(0.7f, 0.95f, 1f, 1f));
            dustCount = EditorPrefs.GetInt("MM.DustCount", 60);
            dustRadius = EditorPrefs.GetFloat("MM.DustRadius", 16f);
            dustSize = EditorPrefs.GetFloat("MM.DustSize", 0.22f);
            dustSpeed = EditorPrefs.GetFloat("MM.DustSpeed", 0.9f);
            dustGlow = EditorPrefs.GetFloat("MM.DustGlow", 1.2f);
            dustPulse = EditorPrefs.GetFloat("MM.DustPulse", 0.55f);
            dustPulseSpeed = EditorPrefs.GetFloat("MM.DustPulseSpeed", 1.6f);
            dustLight = EditorPrefs.GetBool("MM.DustLight", true);
            dustLightStrength = EditorPrefs.GetFloat("MM.DustLightStrength", 0.6f);

            grassBrightness = EditorPrefs.GetFloat("MM.GrassBright", 1.3f);
            grassColor = LoadColor("MM.GrassColor", new Color(0.35f, 0.9f, 0.3f, 1f));

            glowOn = EditorPrefs.GetBool("MM.GlowOn", true);
            glowColor = LoadColor("MM.GlowColor", new Color(0.75f, 1f, 0.6f, 1f));
            glowStrength = EditorPrefs.GetFloat("MM.GlowStrength", 0.6f);
            glowSize = EditorPrefs.GetFloat("MM.GlowSize", 1.2f);
            glowHeight = EditorPrefs.GetFloat("MM.GlowHeight", 0.25f);
            glowSpacing = EditorPrefs.GetFloat("MM.GlowSpacing", 14f);
        }

        void Save()
        {
            EditorPrefs.SetFloat("MM.GrassDensity", grassDensity);
            EditorPrefs.SetFloat("MM.GrassScale", grassScale);
            EditorPrefs.SetFloat("MM.GrassDist", grassDist);
            EditorPrefs.SetFloat("MM.GlowDist", glowDist);
            EditorPrefs.SetFloat("MM.RenderDist", renderDist);
            EditorPrefs.SetFloat("MM.FogDensity", fogDensity);
            EditorPrefs.SetFloat("MM.HillHeight", hillHeight);
            EditorPrefs.SetFloat("MM.HillScale", hillScale);
            EditorPrefs.SetBool("MM.BorderHills", borderOn);
            EditorPrefs.SetFloat("MM.BorderHeight", borderHeight);
            EditorPrefs.SetBool("MM.DustOn", dustOn);
            SaveColor("MM.DustColor", dustColor);
            EditorPrefs.SetInt("MM.DustCount", dustCount);
            EditorPrefs.SetFloat("MM.DustRadius", dustRadius);
            EditorPrefs.SetFloat("MM.DustSize", dustSize);
            EditorPrefs.SetFloat("MM.DustSpeed", dustSpeed);
            EditorPrefs.SetFloat("MM.DustGlow", dustGlow);
            EditorPrefs.SetFloat("MM.DustPulse", dustPulse);
            EditorPrefs.SetFloat("MM.DustPulseSpeed", dustPulseSpeed);
            EditorPrefs.SetBool("MM.DustLight", dustLight);
            EditorPrefs.SetFloat("MM.DustLightStrength", dustLightStrength);
            EditorPrefs.SetFloat("MM.GrassBright", grassBrightness);
            SaveColor("MM.GrassColor", grassColor);
            EditorPrefs.SetBool("MM.GlowOn", glowOn);
            SaveColor("MM.GlowColor", glowColor);
            EditorPrefs.SetFloat("MM.GlowStrength", glowStrength);
            EditorPrefs.SetFloat("MM.GlowSize", glowSize);
            EditorPrefs.SetFloat("MM.GlowHeight", glowHeight);
            EditorPrefs.SetFloat("MM.GlowSpacing", glowSpacing);
        }

        static void Defer(System.Action act)
        {
            EditorApplication.delayCall += () =>
            {
                if (act == null) return;
                try { act(); }
                catch (System.Exception e) { Debug.LogException(e); }
            };
        }

        static Color LoadColor(string key, Color def)
        {
            if (!EditorPrefs.HasKey(key + ".R")) return def;
            return new Color(
                EditorPrefs.GetFloat(key + ".R", def.r),
                EditorPrefs.GetFloat(key + ".G", def.g),
                EditorPrefs.GetFloat(key + ".B", def.b),
                EditorPrefs.GetFloat(key + ".A", def.a));
        }

        static void SaveColor(string key, Color c)
        {
            EditorPrefs.SetFloat(key + ".R", c.r);
            EditorPrefs.SetFloat(key + ".G", c.g);
            EditorPrefs.SetFloat(key + ".B", c.b);
            EditorPrefs.SetFloat(key + ".A", c.a);
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandWidth(true));
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Open the MysticMap scene, press Play if you like, then tweak below.\n" +
                "'Grass density' needs 'Apply grass' to re-bake the field (takes a few seconds).\n" +
                "'Render distance + fog' applies to the open scene instantly.",
                MessageType.None);

            // ---- Render distance (performance) ----------------------------------
            GUILayout.Space(6);
            GUILayout.Label("RENDER DISTANCE FROM THE PLAYER", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            renderDist = EditorGUILayout.Slider("Render distance (m)", renderDist, 40f, 400f);
            fogDensity = EditorGUILayout.Slider("Fog thickness (hides the cut-off)", fogDensity, 0.001f, 0.04f);
            if (EditorGUI.EndChangeCheck()) Save();
            EditorGUILayout.HelpBox(
                "How far around the player the trees, grass, props and the camera draw.  Lower = much faster (fog hides where things end); higher = you can see further.  Applies to the open scene instantly.",
                MessageType.None);
            if (GUILayout.Button("Apply render distance now"))
                Defer(() => MysticMapBuilder.ApplyAtmosphereToScene(renderDist, fogDensity));

            // ---- Terrain hills ---------------------------------------------------
            GUILayout.Space(6);
            GUILayout.Label("TERRAIN HILLS", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            hillHeight = EditorGUILayout.Slider("Hill height", hillHeight, 0f, 70f);
            hillScale = EditorGUILayout.Slider("Hill size (lower = bigger/gentler)", hillScale, 0.3f, 2.5f);
            borderOn = EditorGUILayout.Toggle("Big hills around the map edge", borderOn);
            if (borderOn)
                borderHeight = EditorGUILayout.Slider("Border hill height (m)", borderHeight, 30f, 90f);
            if (EditorGUI.EndChangeCheck()) Save();
            EditorGUILayout.HelpBox(
                "Hill height = how tall the hills are. Hill size = how broad/steep they are.\n" +
                "This rebuilds the terrain so these apply. The big edge hills ring the\n" +
                "map with tall peaks and lower passes, so the town sits in a sheltered,\n" +
                "interesting valley to explore.",
                MessageType.None);
            if (GUILayout.Button("Apply hills (rebuild the whole map)"))
            {
                Defer(() =>
                {
                    if (EditorUtility.DisplayDialog("Mystic Map",
                        "Regenerate the whole 1km map with these hill settings?", "Rebuild", "Cancel"))
                        MysticMapBuilder.Build();
                });
            }

            // ---- Grass -----------------------------------------------------------
            GUILayout.Space(8);
            GUILayout.Label("GRASS FIELD", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            grassDensity = EditorGUILayout.Slider("Density", grassDensity, 0.2f, 500f);
            grassScale = EditorGUILayout.Slider("Size / height", grassScale, 0.5f, 2.5f);
            if (EditorGUI.EndChangeCheck()) Save();

            EditorGUILayout.HelpBox(
                "Dense fields everywhere - inside the town too (thinned a little there so the town floor still reads as a lived-in yard); medium grass in lush zones and\n" +
                "taller clumps grouped in patches, so the meadow varies naturally; roads and the paved market square stay bare.",
                MessageType.None);

            if (GUILayout.Button("Apply grass (re-bake the field)"))
            {
                Defer(() =>
                {
                    if (!EditorUtility.DisplayDialog("Mystic Map", "Re-bake the grass field at the current density?", "Apply", "Cancel"))
                        return;
                    EditorUtility.DisplayProgressBar("Grass", "Baking grass field...", 0.3f);
                    try { MysticMapBuilder.BakeGrassInScene(grassDensity, grassScale); }
                    finally { EditorUtility.ClearProgressBar(); }
                });
            }

            // ---- Grass distance (performance) ------------------------------------
            GUILayout.Space(10);
            GUILayout.Label("GRASS DISTANCE (performance)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            grassDist = EditorGUILayout.Slider("Grass render distance (m)", grassDist, 5f, 300f);
            glowDist = EditorGUILayout.Slider("Glow under grass distance (m)", glowDist, 5f, 300f);
            if (EditorGUI.EndChangeCheck()) Save();
            EditorGUILayout.HelpBox(
                "Stops drawing grass too far away (hard to see but costly). Glow distance is independent now. Press Apply - it takes effect immediately in the Scene view, not only in Play.",
                MessageType.None);
            if (GUILayout.Button("Apply grass + glow distance"))
                Defer(() => MysticMapBuilder.ApplyGrassDistancesToScene(grassDist, glowDist));

            // ---- Grass color + glow ---------------------------------------------
            GUILayout.Space(12);
            GUILayout.Label("GRASS COLOR + GLOW", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            grassColor = EditorGUILayout.ColorField("Grass color", grassColor);
            grassBrightness = EditorGUILayout.Slider("Grass brightness / vibrance", grassBrightness, 0.5f, 2f);
            if (EditorGUI.EndChangeCheck()) Save();

            if (GUILayout.Button("Apply grass color + brightness"))
                Defer(() => MysticMapBuilder.SetGrassVisualInScene(grassColor, grassBrightness));

            GUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            glowOn = GUILayout.Toggle(glowOn, "  Glow under the actual grass sprites");
            if (glowOn)
            {
                glowColor = EditorGUILayout.ColorField("Grass glow color", glowColor);
                glowStrength = EditorGUILayout.Slider("Glow strength", glowStrength, 0f, 3f);
                glowSize = EditorGUILayout.Slider("Glow size (m)", glowSize, 0.3f, 4f);
                glowHeight = EditorGUILayout.Slider("Glow height above grass (m)", glowHeight, 0f, 3f);
            }
            if (EditorGUI.EndChangeCheck()) Save();

            EditorGUILayout.HelpBox(
                "A small glow is attached under every grass tuft / plant / stone you painted.\n" +
                "Size = how big the glow is. Height = how far above the ground it floats.\n" +
                "Strength scales them all together.",
                MessageType.None);
            if (GUILayout.Button(glowOn ? "Add glow to grass sprites" : "Remove grass glow"))
            {
                Defer(() =>
                {
                    if (glowOn)
                        MysticMapBuilder.CreateGrassSpriteGlowsInScene(glowColor, glowStrength, glowSize, glowHeight, true);
                    else
                        MysticMapBuilder.RemoveGrassSpriteGlows();
                });
            }

            // ---- Add more props --------------------------------------------------
            GUILayout.Space(12);
            GUILayout.Label("ADD MORE ON THE MAP", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 150 stones/rocks"))
                Defer(() => AddProps("stones", 150));
            if (GUILayout.Button("+ 200 flowers"))
                Defer(() => AddProps("flowers", 200));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ 150 plants/bushes"))
                Defer(() => AddProps("plants", 150));
            if (GUILayout.Button("+ 150 grass tufts"))
                Defer(() => AddProps("grass", 150));
            EditorGUILayout.EndHorizontal();

            // ---- Magical floating dust -------------------------------------------
            GUILayout.Space(12);
            GUILayout.Label("MAGICAL FLOATING DUST (near the player)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            dustOn = GUILayout.Toggle(dustOn, "  Small glowing dust rises from the ground around you");
            if (dustOn)
            {
                dustColor = EditorGUILayout.ColorField("Dust glow color", dustColor);
                dustCount = Mathf.RoundToInt(EditorGUILayout.Slider("Amount (particles)", dustCount, 10f, 200f));
                dustRadius = EditorGUILayout.Slider("Spread radius (m)", dustRadius, 3f, 40f);
                dustSize = EditorGUILayout.Slider("Particle size (m)", dustSize, 0.03f, 1.2f);
                dustSpeed = EditorGUILayout.Slider("Rise speed (m/s)", dustSpeed, 0.1f, 3f);
                dustGlow = EditorGUILayout.Slider("Glow brightness", dustGlow, 0.2f, 4f);
                dustPulse = EditorGUILayout.Slider("Pulse strength (0 = steady)", dustPulse, 0f, 1f);
                dustPulseSpeed = EditorGUILayout.Slider("Pulse speed (per second)", dustPulseSpeed, 0.2f, 5f);
                dustLight = GUILayout.Toggle(dustLight, "  Softly light up the ground around you");
                if (dustLight)
                    dustLightStrength = EditorGUILayout.Slider("Light strength (soft glow)", dustLightStrength, 0.05f, 1.5f);
            }
            if (EditorGUI.EndChangeCheck()) Save();
            EditorGUILayout.HelpBox(
                "Tiny glowing motes float up from the ground and twinkle around your camera. They only spawn near the player so it stays cheap. Press Play to see them rise around you.",
                MessageType.None);
            if (GUILayout.Button(dustOn ? "Apply magical dust now" : "Remove magical dust"))
                Defer(() => MysticMapBuilder.CreateMagicalDustInScene(dustColor, dustOn, dustCount, dustRadius, dustSize, dustSpeed, dustGlow, dustPulse, dustPulseSpeed, dustLight, dustLightStrength));

            GUILayout.Space(12);
            if (GUILayout.Button("Rebuild whole map (fresh build)"))
            {
                Defer(() =>
                {
                    if (EditorUtility.DisplayDialog("Mystic Map",
                        "This opens a NEW scene and regenerates the whole 1km map with current settings.\n" +
                        "Any edits you made to the current scene manually would be lost.", "Rebuild", "Cancel"))
                        MysticMapBuilder.Build();
                });
            }

            EditorGUILayout.EndScrollView();
        }

        void AddProps(string group, int count)
        {
            int placed = MysticMapBuilder.AddPropsToScene(group, count);
            EditorUtility.DisplayDialog("Mystic Map",
                "Added " + placed + " '" + group + "' to the map (they appear as you walk near them).",
                "OK");
            Repaint();
        }
    }
}
#endif
