using System.Collections.Generic;
using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// Small helpers for the Hovl Studio magic prefabs (they are pure particle/mesh/light
    /// visuals - no scripts of their own - so everything is driven from here):
    /// spawning, forcing a loop on the "stays there while you charge" effects, tinting the
    /// additive materials, working out how long a one-shot effect wants to live, and dropping
    /// the baked-in lights that the pack puts inside some circles.
    /// </summary>
    public static class MagicEffects
    {
        /// <summary>How long a one-shot effect may live when its own particles say nothing.</summary>
        public const float DefaultLifetime = 4f;

        /// <summary>Spawns an effect prefab (a plain Instantiate - the prefabs are not pooled).</summary>
        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
                                       Transform parent = null, float scale = 1f)
        {
            if (prefab == null) return null;

            GameObject go = Object.Instantiate(prefab, position, rotation, parent);
            go.name = prefab.name;
            if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
            return go;
        }

        /// <summary>
        /// Plays every particle system in the effect. <paramref name="loop"/> is forced so a
        /// charging circle really does stay around the player until the magic is released.
        /// </summary>
        public static void ForcePlay(GameObject go, bool loop)
        {
            if (go == null) return;

            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;

                var main = ps.main;
                main.loop = loop;
                main.playOnAwake = true;
                ps.Play(true);
            }
        }

        /// <summary>
        /// How long the effect needs before its last particle is gone
        /// (system duration + the longest particle lifetime), so one-shots can be destroyed
        /// exactly when they are done instead of lingering.
        /// </summary>
        public static float NaturalLifetime(GameObject go, float fallback = DefaultLifetime)
        {
            if (go == null) return fallback;

            float longest = 0f;
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;

                var main = ps.main;
                float life = main.duration + main.startLifetime.constantMax;
                if (main.loop) life += main.duration;         // two loops of a looping effect
                if (life > longest) longest = life;
            }

            if (longest <= 0.01f) longest = fallback;
            return Mathf.Clamp(longest, 0.15f, 30f);
        }

        /// <summary>Destroys an effect after <paramref name="seconds"/> (0 or less = its natural life).</summary>
        public static void DestroyLater(GameObject go, float seconds = 0f)
        {
            if (go == null) return;

            float life = seconds > 0.01f ? seconds : NaturalLifetime(go);
            Object.Destroy(go, life);
        }

        /// <summary>
        /// True when a point is far enough from everything in <paramref name="used"/> to deserve its
        /// own effect. One hit used to show the same impact three times - the direct hit, its splash
        /// and the maximum-charge explosion all land on the same victim - and this is what keeps a
        /// single spell to a single visual per spot.
        /// </summary>
        public static bool Fresh(List<Vector3> used, Vector3 point, float minDistance = 1.2f)
        {
            if (used == null || used.Count == 0) return true;

            float limit = Mathf.Max(0.01f, minDistance);
            limit *= limit;

            for (int i = 0; i < used.Count; i++)
                if ((used[i] - point).sqrMagnitude < limit) return false;

            return true;
        }

        /// <summary>
        /// Multiplies a tint into every material the effect uses. The pack's additive shaders
        /// expose "_TintColor" (legacy particles); "_Color" / "_BaseColor" are tried as well so
        /// the same call keeps working if a material is ever swapped for a different shader.
        /// </summary>
        public static void Tint(GameObject go, Color tint)
        {
            if (go == null || tint == Color.white) return;

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;

                Material[] materials = r.materials;            // instances, so the asset is untouched
                foreach (Material m in materials)
                {
                    if (m == null) continue;
                    if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", m.GetColor("_TintColor") * tint);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", m.GetColor("_Color") * tint);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.GetColor("_BaseColor") * tint);
                }
                r.materials = materials;
            }
        }

        /// <summary>
        /// Removes the lights the pack bakes into some effects. Five stacked circles would
        /// otherwise add five real-time lights to the scene at once.
        /// </summary>
        public static int StripLights(GameObject go)
        {
            if (go == null) return 0;

            int removed = 0;
            foreach (Light light in go.GetComponentsInChildren<Light>(true))
            {
                if (light == null) continue;
                Object.Destroy(light);
                removed++;
            }
            return removed;
        }

        /// <summary>Every renderer keeps drawing even when something (a culler, a pool) disabled it.</summary>
        public static void Show(GameObject go)
        {
            if (go == null) return;

            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                if (t != null && !t.gameObject.activeSelf) t.gameObject.SetActive(true);
        }

        /// <summary>Biggest footprint (X/Z) of everything the effect draws right now, in world units.</summary>
        public static float Footprint(GameObject go)
        {
            if (go == null) return 0f;

            bool any = false;
            Bounds bounds = new Bounds();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;
                if (r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }

            return any ? Mathf.Max(bounds.size.x, bounds.size.z) : 0f;
        }
    }
}
