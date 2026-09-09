#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Keeps the "magic" effects (grass glow, floating dust/orbs, ...) persistent.
    /// If you activate one and it was created while in Play mode, Play/Stop would
    /// normally delete it - this listens for when you leave Play mode and quietly
    /// re-adds anything whose toggle is still ON so you never have to re-apply it.
    /// </summary>
    [InitializeOnLoad]
    public static class MysticMapAutoRestore
    {
        static MysticMapAutoRestore()
        {
            EditorApplication.playModeStateChanged += OnPlayStateChanged;
        }

        static void OnPlayStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                // Back in edit mode after a Play/Stop: bring back any effect that is ON.
                EditorApplication.delayCall += RestorePersistentEffects;
            }
            else if (state == PlayModeStateChange.EnteredPlayMode)
            {
                // Play reloads the scene from disk, which can drop unsaved changes.
                // Re-apply the grass/glow distance so it is enforced in Play too.
                EditorApplication.delayCall += ApplyRuntimeGrassDistances;
            }
        }

        static void ApplyRuntimeGrassDistances()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if (!EditorPrefs.HasKey("MM.GrassDist") && !EditorPrefs.HasKey("MM.GlowDist")) return;
            try
            {
                float g = EditorPrefs.GetFloat("MM.GrassDist", 60f);
                float gl = EditorPrefs.GetFloat("MM.GlowDist", g);
                MysticMapBuilder.ApplyGrassDistancesToScene(g, gl);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Mystic Map runtime grass distance skipped: " + e.Message);
            }
        }

        static void RestorePersistentEffects()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            try
            {
                // 1) Glow under the grass / props (toggle stored in MM.GlowOn).
                if (EditorPrefs.GetBool("MM.GlowOn", true) && !AnyGrassGlow())
                {
                    MysticMapBuilder.CreateGrassSpriteGlowsInScene(
                        LoadColor("MM.GlowColor", new Color(0.75f, 1f, 0.6f, 1f)),
                        EditorPrefs.GetFloat("MM.GlowStrength", 0.6f),
                        EditorPrefs.GetFloat("MM.GlowSize", 1.2f),
                        EditorPrefs.GetFloat("MM.GlowHeight", 0.25f),
                        true);
                }

                // 2) Floating magical dust / glowing orbs near the player (MM.DustOn).
                if (EditorPrefs.GetBool("MM.DustOn", true) && GameObject.Find("Magical Dust") == null)
                {
                    MysticMapBuilder.CreateMagicalDustInScene(
                        LoadColor("MM.DustColor", new Color(0.7f, 0.95f, 1f, 1f)),
                        true,
                        EditorPrefs.GetInt("MM.DustCount", 60),
                        EditorPrefs.GetFloat("MM.DustRadius", 16f),
                        EditorPrefs.GetFloat("MM.DustSize", 0.22f),
                        EditorPrefs.GetFloat("MM.DustSpeed", 0.9f),
                        EditorPrefs.GetFloat("MM.DustGlow", 1.2f),
                        EditorPrefs.GetFloat("MM.DustPulse", 0.55f),
                        EditorPrefs.GetFloat("MM.DustPulseSpeed", 1.6f),
                        EditorPrefs.GetBool("MM.DustLight", true),
                        EditorPrefs.GetFloat("MM.DustLightStrength", 0.6f));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Mystic Map auto-restore skipped: " + e.Message);
            }
        }

        static bool AnyGrassGlow()
        {
            string[] roots = { "Nature Props", "Painted Props" };
            foreach (var rn in roots)
            {
                var root = GameObject.Find(rn);
                if (root == null) continue;
                for (int i = 0; i < root.transform.childCount; i++)
                {
                    var ch = root.transform.GetChild(i);
                    if (ch != null && ch.Find("GlowSprite") != null) return true;
                }
            }
            return false;
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
    }
}
#endif
