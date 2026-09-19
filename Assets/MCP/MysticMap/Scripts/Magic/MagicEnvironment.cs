using System.Collections.Generic;
using UnityEngine;

namespace MysticMap
{
    /// <summary>What happens to a streamed prop once the magic has torn it away.</summary>
    public enum MagicEnvMode
    {
        /// <summary>Delete it. Chunk content (trees, grass, rocks) grows back when the chunk is built again.</summary>
        Destroy,
        /// <summary>Only switch it off, so a pool (the roadside deco) can reuse the object later.</summary>
        Hide
    }

    /// <summary>
    /// The world's reaction to magic.
    ///
    /// Nothing in the world is damageable by default, so this script is what makes grass,
    /// flowers, trees, rocks and the scattered roadside clutter behave like they were hit:
    ///
    ///   * <see cref="Wind"/> is the aura that runs while a spell charges. Nearby plants lean
    ///     away from the slime and swirl around it, and a strong aura tears a few blades off so
    ///     they float away - the "magic bends the world around it" look.
    ///   * <see cref="Impact"/> is a hit: plants are ripped out and flung (they shrink and vanish),
    ///     trees and big structures are shaken and spring back, small stones and roadside props
    ///     are kicked away, and a little debris is thrown with each one.
    ///   * <see cref="Break"/> is a maximum-charge hit: on top of the impact it also shatters the
    ///     big rocks and the heavier props in the radius.
    ///
    /// Everything is discovered through the roots that register themselves here (a chunk's
    /// "Props" object, the roadside deco object), so no prop needs a component, a collider or a
    /// tag - and prop culling, pooling and chunk unloading keep working exactly as they did.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MysticMap/Magic Environment")]
    public class MagicEnvironment : MonoBehaviour
    {
        /// <summary>Name of the (runtime-created) object the reactions live on.</summary>
        public const string ObjectName = "Magic Environment";

        [Header("Reactions")]
        [Tooltip("Master switch for everything the magic does to the world.")]
        public bool reactions = true;
        [Tooltip("Most props a single impact or wind pulse may touch.")]
        [Range(16, 900)] public int maxPerImpact = 280;
        [Tooltip("Most props flying through the air at the same time.")]
        [Range(16, 900)] public int maxFlying = 320;
        [Tooltip("Most props leaning / swaying at the same time.")]
        [Range(16, 1500)] public int maxSwaying = 560;
        [Tooltip("Props scanned per call (a safety valve for very heavy chunks).")]
        [Range(200, 20000)] public int maxScan = 5000;
        [Tooltip("Seconds a torn prop takes to fly away and disappear.")]
        [Range(0.3f, 5f)] public float tearLife = 1.5f;
        [Tooltip("Blades of grass a strong wind aura may tear off per second.")]
        [Range(0f, 60f)] public float windTearRate = 9f;
        [Tooltip("Debris thrown per prop that is torn away (0 = none).")]
        [Range(0, 3)] public int debrisPerProp = 1;
        [Tooltip("Draw the reaction counts in the console while tuning (verbose).")]
        public bool logReactions;

        // ---- the registration of the world's prop containers ----------------
        class EnvRoot { public Transform t; public MagicEnvMode mode; public float extent; }

        class Lean
        {
            public Transform t;
            public Quaternion baseRot;
            public Vector3 dir;
            public float amp, age, rate, angle, dur;
            public bool wind;
            public float until;
        }

        class Flyer
        {
            public Transform t;
            public MagicEnvMode mode;
            public Quaternion baseRot;
            public Vector3 from, vel, spin, baseScale;
            public float age, dur;
        }

        static MagicEnvironment _instance;
        static readonly List<EnvRoot> Roots = new List<EnvRoot>(64);

        readonly List<Lean> _sway = new List<Lean>(128);
        readonly Dictionary<Transform, Lean> _wind = new Dictionary<Transform, Lean>(256);
        readonly List<Flyer> _flyers = new List<Flyer>(128);
        readonly HashSet<Transform> _flying = new HashSet<Transform>();
        readonly List<Transform> _finished = new List<Transform>(32);

        float _tearBudget;
        float _prune;
        int _cursor;

        /// <summary>The live environment (null when nothing registered yet).</summary>
        public static MagicEnvironment Active => _instance;

        /// <summary>How many props are being thrown around right now.</summary>
        public int FlyingCount => _flyers.Count;
        /// <summary>How many props are leaning / swaying right now.</summary>
        public int SwayingCount => _sway.Count + _wind.Count;

        // =====================================================================
        //  Registration
        // =====================================================================
        static MagicEnvironment Ensure()
        {
            if (!Application.isPlaying) return null;

            if (_instance == null)
            {
                var go = new GameObject(ObjectName);
                _instance = go.AddComponent<MagicEnvironment>();
            }

            return _instance;
        }

        /// <summary>
        /// Teaches the environment about a container of props (a chunk's "Props" object, the
        /// roadside deco object, ...). <paramref name="extent"/> is the radius of that container,
        /// used to skip whole chunks that are too far away to be inside a blast.
        /// </summary>
        public static void Register(Transform root, MagicEnvMode mode, float extent = 120f)
        {
            if (root == null || !Application.isPlaying) return;

            Ensure();

            for (int i = 0; i < Roots.Count; i++)
            {
                if (Roots[i].t != root) continue;
                Roots[i].mode = mode;
                Roots[i].extent = Mathf.Max(8f, extent);
                return;
            }

            Roots.Add(new EnvRoot { t = root, mode = mode, extent = Mathf.Max(8f, extent) });
        }

        /// <summary>Forgets a container again (called when a chunk is unloaded).</summary>
        public static void Unregister(Transform root)
        {
            if (root == null) return;

            for (int i = Roots.Count - 1; i >= 0; i--)
                if (Roots[i].t == root) Roots.RemoveAt(i);
        }

        // =====================================================================
        //  Public reactions
        // =====================================================================
        /// <summary>
        /// A soft aura pulse: everything within the radius leans / swirls away from the centre
        /// (and, when the aura is strong, a couple of plants are pulled off the ground).
        /// Keep calling it while the aura is up - the lean is refreshed, not stacked.
        /// </summary>
        public static void Wind(Vector3 center, float radius, float strength, float duration = 0.45f)
        {
            MagicEnvironment env = _instance;
            if (env == null || !env.reactions || radius <= 0.05f || strength <= 0.01f) return;

            env.WindScan(center, radius, strength, duration);
        }

        /// <summary>
        /// A blunt hit on the world: plants are torn away, trees are shaken, small stones and
        /// roadside clutter are kicked, and a little debris is thrown with them.
        /// <paramref name="power"/> is the spell's power (0.2 = a weak slash, 2 = a full charge).
        /// </summary>
        /// <returns>How many props reacted.</returns>
        public static int Impact(Vector3 center, float radius, float power,
                                 GameObject[] debris = null, int debrisCount = 0)
        {
            MagicEnvironment env = _instance;
            if (env == null || !env.reactions) return 0;

            return env.React(center, radius, power, false, debris, debrisCount);
        }

        /// <summary>
        /// A maximum-charge hit: like <see cref="Impact"/>, but the big rocks and the heavier
        /// props inside the radius are shattered as well.
        /// </summary>
        public static int Break(Vector3 center, float radius, float power,
                                GameObject[] debris = null, int debrisCount = 0)
        {
            MagicEnvironment env = _instance;
            if (env == null || !env.reactions) return 0;

            return env.React(center, radius, power, true, debris, debrisCount);
        }

        // =====================================================================
        //  The scan itself
        // =====================================================================
        /// <summary>One pass over every registered container, reacting to the props in range.</summary>
        int React(Vector3 center, float radius, float power, bool breakBig,
                  GameObject[] debris, int debrisCount)
        {
            radius = Mathf.Max(0.2f, radius);
            power = Mathf.Max(0.05f, power);

            float sqr = radius * radius;
            int touched = 0, scanned = 0;
            int budget = Mathf.Clamp(debrisCount, 0, 30);

            for (int r = 0; r < Roots.Count; r++)
            {
                EnvRoot root = Roots[r];
                Transform rt = root != null ? root.t : null;
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;

                float far = radius + root.extent;
                if ((rt.position - center).sqrMagnitude > far * far) continue;

                int n = rt.childCount;
                if (n == 0) continue;

                // Start at a different child every call, so a cap never starves the same props.
                int start = Mathf.Abs(_cursor++) % n;

                for (int k = 0; k < n; k++)
                {
                    if (touched >= maxPerImpact || scanned >= maxScan) break;

                    Transform c = rt.GetChild((start + k) % n);
                    if (c == null || !c.gameObject.activeInHierarchy) continue;

                    Vector3 p = c.position;
                    float dx = p.x - center.x;
                    float dy = (p.y - center.y) * 0.6f;      // flattened distance: a blast reaches up
                    float dz = p.z - center.z;
                    float d2 = dx * dx + dy * dy + dz * dz;
                    if (d2 > sqr) continue;

                    scanned++;

                    float falloff = 1f - Mathf.Sqrt(d2) / radius;
                    if (ReactOne(c, new Vector3(dx, 0f, dz), falloff, power, breakBig, root.mode,
                                 debris, ref budget))
                        touched++;
                }
            }

            if (logReactions && touched > 0)
                Debug.Log("[MysticMap] The world reacted: " + touched + " props in " +
                          radius.ToString("0.#") + " m (power " + power.ToString("0.##") + ").", this);

            return touched;
        }

        /// <summary>
        /// How the magic sees one piece of the world when a spell runs into it: plants are soft (a
        /// slash flies straight through grass), while rocks, trees, props and structures are solid
        /// and stop it.
        ///
        /// The prop containers registered with <see cref="Register"/> are used when they are there,
        /// so this always agrees with how the world reacts. Without them the world's own ground
        /// (the chunk meshes) is never in the way, and anything else is.
        /// </summary>
        public static bool IsSolid(Collider collider)
        {
            if (collider == null) return false;
            if (collider is TerrainCollider) return false;         // real terrain is never an obstacle
            if (collider.GetComponentInParent<MagicDebrisPiece>() != null) return false;   // our own debris

            Transform root = RootOf(collider.transform);

            if (root != null)
            {
                EnvKind kind = Classify(collider.gameObject.name.ToLowerInvariant(),
                                        SizeOf(collider.transform));
                return kind != EnvKind.Plant;
            }

            return !Groundish(collider);
        }

        /// <summary>The registered prop container a transform sits inside, or null.</summary>
        static Transform RootOf(Transform t)
        {
            if (t == null || Roots.Count == 0) return null;

            for (Transform p = t; p != null; p = p.parent)
                for (int i = 0; i < Roots.Count; i++)
                    if (Roots[i] != null && Roots[i].t == p) return p;

            return null;
        }

        /// <summary>True when a collider is the world's own ground (or soft vegetation) rather than
        /// something a spell should stop on.</summary>
        static bool Groundish(Collider collider)
        {
            GameObject go = collider.gameObject;
            string name = go.name.ToLowerInvariant();

            if (Has(name, "chunk") || Has(name, "terrain") || Has(name, "ground") ||
                Has(name, "road") || Has(name, "land") || Has(name, "floor") ||
                Has(name, "grass") || Has(name, "flower") || Has(name, "bush") ||
                Has(name, "fern") || Has(name, "plant") || Has(name, "leaf") ||
                Has(name, "vine") || Has(name, "water"))
                return true;

            return go.GetComponentInParent<World.WorldChunk>() != null;   // the procedural ground itself
        }

        /// <summary>How one prop reacts to a hit. Returns true when it actually did something.</summary>
        bool ReactOne(Transform c, Vector3 away, float falloff, float power, bool breakBig,
                      MagicEnvMode mode, GameObject[] debris, ref int budget)
        {
            if (_flying.Contains(c)) return false;       // already on its way out

            Vector3 dir = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
            dir.y = 0f;

            // A physics prop is pushed around instead of being torn away.
            Rigidbody body = c.GetComponent<Rigidbody>();
            if (body == null) body = c.GetComponentInParent<Rigidbody>();
            if (body != null && !body.isKinematic)
            {
                body.AddForce((dir + Vector3.up * 0.7f) * (0.4f + power) * falloff, ForceMode.VelocityChange);
                return true;
            }

            float size = SizeOf(c);
            EnvKind kind = Classify(c.name.ToLowerInvariant(), size);
            bool small = size <= 1.35f;

            switch (kind)
            {
                case EnvKind.Tree:
                    Sway(c, dir, 3f + 9f * power * falloff, false);
                    return true;

                case EnvKind.Structure:
                    // Walls, ruins and buildings are only shaken - they never get destroyed.
                    Sway(c, dir, 1.5f + 4f * power * falloff, false);
                    return true;

                case EnvKind.Rock:
                    if (breakBig || (power >= 1.4f && small))
                    {
                        Throw(c, dir, power, 0.85f + 0.35f * power, mode);
                        ThrowDebris(c, debris, ref budget, power, 0.5f);
                        return true;
                    }
                    Sway(c, dir, 2.5f + 6f * power * falloff, false);
                    return true;

                case EnvKind.Prop:
                    if (breakBig || (power >= 0.75f && small))
                    {
                        Throw(c, dir, power, 1f + 0.4f * power, mode);
                        ThrowDebris(c, debris, ref budget, power, 0.35f);
                        return true;
                    }
                    Sway(c, dir, 4f + 8f * power * falloff, false);
                    return true;

                default:    // plants, grass, flowers and small clutter
                    if (power >= 0.35f && small)
                    {
                        Throw(c, dir, power, 1.15f + 0.5f * power, mode);
                        ThrowDebris(c, debris, ref budget, power, 0.2f);
                        return true;
                    }
                    Sway(c, dir, 8f + 16f * power * falloff, false);
                    return true;
            }
        }

        /// <summary>Sorts a prop by name (and, as a fallback, by size) so it reacts the right way.</summary>
        enum EnvKind { Plant, Tree, Rock, Prop, Structure }

        static EnvKind Classify(string n, float size)
        {
            // Structural things are never destroyed, so they are checked first.
            if (Has(n, "wall") || Has(n, "ruin") || Has(n, "house") || Has(n, "building") ||
                Has(n, "fence") || Has(n, "gate") || Has(n, "pillar") || Has(n, "arch") ||
                Has(n, "bridge") || Has(n, "tower") || Has(n, "statue") || Has(n, "column"))
                return EnvKind.Structure;

            if (Has(n, "rock") || Has(n, "boulder") || Has(n, "stone") || Has(n, "pebble") ||
                Has(n, "crystal") || Has(n, "cliff") || Has(n, "ore") || Has(n, "rubble"))
                return EnvKind.Rock;

            if (Has(n, "tree") || Has(n, "pine") || Has(n, "oak") || Has(n, "birch") ||
                Has(n, "palm") || Has(n, "log") || Has(n, "stump") || size > 3.2f)
                return EnvKind.Tree;

            if (Has(n, "cart") || Has(n, "wagon") || Has(n, "barrel") || Has(n, "sack") ||
                Has(n, "crate") || Has(n, "box") || Has(n, "pot") || Has(n, "chest") ||
                Has(n, "lantern") || Has(n, "sign") || Has(n, "bucket") || Has(n, "basket") ||
                Has(n, "stall") || Has(n, "table") || Has(n, "chair") || Has(n, "lamp") ||
                Has(n, "wheel") || Has(n, "plank") || Has(n, "hay"))
                return EnvKind.Prop;

            return EnvKind.Plant;
        }

        static bool Has(string haystack, string needle) => haystack.IndexOf(needle) >= 0;

        /// <summary>Biggest side of a prop's world scale.</summary>
        static float SizeOf(Transform c)
        {
            Vector3 s = c.lossyScale;
            return Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        }

        // =====================================================================
        //  Tearing a prop away and shaking it
        // =====================================================================
        /// <summary>Lifts a prop off the ground and blows it outwards; it shrinks away as it flies.</summary>
        void Throw(Transform c, Vector3 dir, float power, float lift, MagicEnvMode mode)
        {
            if (_flyers.Count >= maxFlying || c.childCount > 6) return;   // never a whole little house

            _flyers.Add(new Flyer
            {
                t = c,
                mode = mode,
                baseRot = c.rotation,
                baseScale = c.localScale,
                from = c.position,
                vel = dir * Random.Range(1.1f, 2.4f) * (0.45f + power) +
                      Vector3.up * Random.Range(1.2f, 2.6f) * lift,
                spin = new Vector3(Random.Range(-420f, 420f), Random.Range(-540f, 540f), Random.Range(-420f, 420f)),
                dur = tearLife * Random.Range(0.75f, 1.3f)
            });

            _flying.Add(c);
        }

        /// <summary>Shakes a prop (a tree, a wall) and lets it spring back.</summary>
        void Sway(Transform c, Vector3 dir, float amplitude, bool wind, float duration = 0.75f)
        {
            float amp = Mathf.Min(Mathf.Max(0f, amplitude), 60f);
            if (amp <= 0.05f) return;

            if (wind)
            {
                if (_wind.TryGetValue(c, out Lean existing))
                {
                    existing.amp = Mathf.Max(existing.amp, amp);
                    existing.dir = dir;
                    existing.until = Time.time + duration;
                    return;
                }

                if (_wind.Count >= maxSwaying) return;

                _wind[c] = new Lean
                {
                    t = c, baseRot = c.rotation, dir = dir, amp = amp,
                    age = 0f, rate = 2.7f, dur = duration, wind = true, until = Time.time + duration
                };
                return;
            }

            if (_sway.Count >= maxSwaying) return;

            _sway.Add(new Lean
            {
                t = c, baseRot = c.rotation, dir = dir, amp = amp,
                age = 0f, rate = 11f, dur = duration, wind = false, until = Time.time + duration
            });
        }

        /// <summary>Throws a little debris with a prop that was torn away.</summary>
        void ThrowDebris(Transform c, GameObject[] debris, ref int budget, float power, float chance)
        {
            if (budget <= 0 || debrisPerProp <= 0) return;
            if (Random.value > chance) return;

            budget -= MagicDebris.Spray(debris, c.position + Vector3.up * 0.15f, Vector3.up,
                                        debrisPerProp, power * 0.6f);
        }

        // =====================================================================
        //  The wind aura (keeps the grass leaning while a spell charges)
        // =====================================================================
        void WindScan(Vector3 center, float radius, float strength, float duration)
        {
            // A strong aura does not only bend the grass: it pulls a few blades off the ground.
            int budget = strength >= 26f ? Mathf.Min(6, Mathf.FloorToInt(_tearBudget)) : 0;
            int spent = 0;
            int perRoot = Mathf.Max(64, maxScan / 4);
            float sqr = radius * radius;

            for (int r = 0; r < Roots.Count; r++)
            {
                EnvRoot root = Roots[r];
                Transform rt = root != null ? root.t : null;
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;

                float far = radius + root.extent;
                if ((rt.position - center).sqrMagnitude > far * far) continue;

                int n = rt.childCount;
                if (n == 0) continue;

                int start = Mathf.Abs(_cursor++) % n;
                int here = 0;

                for (int k = 0; k < n && here < perRoot; k++)
                {
                    Transform c = rt.GetChild((start + k) % n);
                    if (c == null || !c.gameObject.activeInHierarchy) continue;

                    Vector3 p = c.position;
                    float dx = p.x - center.x;
                    float dy = p.y - center.y;
                    float dz = p.z - center.z;
                    float d2 = dx * dx + dy * dy * 0.25f + dz * dz;
                    if (d2 > sqr) continue;

                    here++;

                    Vector3 radial = new Vector3(dx, 0f, dz);
                    if (radial.sqrMagnitude < 0.0001f) radial = Vector3.forward;
                    radial.Normalize();

                    // A swirl (mostly around the slime) reads much more like magic than a shove.
                    Vector3 tangent = Vector3.Cross(Vector3.up, radial);
                    Vector3 dir = (radial * 0.45f + tangent * 0.95f).normalized;

                    float falloff = 1f - Mathf.Sqrt(d2) / radius;
                    Sway(c, dir, strength * (0.3f + 0.7f * falloff), true, duration);

                    if (spent < budget && _flyers.Count < maxFlying &&
                        Classify(c.name.ToLowerInvariant(), SizeOf(c)) == EnvKind.Plant &&
                        Random.value < 0.3f)
                    {
                        Throw(c, dir, 0.5f, 1.7f, root.mode);
                        spent++;
                    }
                }
            }

            if (spent > 0) _tearBudget = Mathf.Clamp(_tearBudget - spent, 0f, 12f);
        }

        // =====================================================================
        //  Driving everything
        // =====================================================================
        void Update()
        {
            float dt = Time.deltaTime;

            _tearBudget = Mathf.Min(12f, _tearBudget + dt * Mathf.Max(0f, windTearRate));

            TickFlyers(dt);
            TickSway(dt);
            TickWind();

            _prune -= dt;
            if (_prune <= 0f) { _prune = 1.5f; PruneRoots(); }
        }

        void TickFlyers(float dt)
        {
            for (int i = _flyers.Count - 1; i >= 0; i--)
            {
                Flyer f = _flyers[i];
                f.age += dt;

                if (f.t == null) { _flyers.RemoveAt(i); continue; }   // unloaded mid-flight

                float t = Mathf.Clamp01(f.age / Mathf.Max(0.05f, f.dur));
                if (t >= 1f)
                {
                    Finish(f);
                    _flying.Remove(f.t);
                    _flyers.RemoveAt(i);
                    continue;
                }

                // A gentle arc: outwards and up, then down - shrinking away as it goes.
                f.t.position = f.from + f.vel * f.age + Vector3.down * (3.5f * f.age * f.age);
                f.t.rotation = f.baseRot * Quaternion.Euler(f.spin * f.age);
                f.t.localScale = f.baseScale * (1f - t * t);
            }
        }

        void TickSway(float dt)
        {
            for (int i = _sway.Count - 1; i >= 0; i--)
            {
                Lean s = _sway[i];
                if (s.t == null) { _sway.RemoveAt(i); continue; }

                s.age += dt;

                float t = Mathf.Clamp01(s.age / Mathf.Max(0.05f, s.dur));
                if (t >= 1f)
                {
                    s.t.rotation = s.baseRot;
                    _sway.RemoveAt(i);
                    continue;
                }

                float decay = (1f - t) * (1f - t);
                s.angle = s.amp * decay * Mathf.Sin(s.age * s.rate);
                Apply(s);
            }
        }

        void TickWind()
        {
            if (_wind.Count == 0) return;

            foreach (KeyValuePair<Transform, Lean> pair in _wind)
            {
                Lean s = pair.Value;
                if (s.t == null || Time.time >= s.until) { _finished.Add(pair.Key); continue; }

                s.age += Time.deltaTime;

                float fade = Mathf.Clamp01((s.until - Time.time) / 0.3f);
                s.angle = s.amp * fade * (0.5f + 0.5f * Mathf.Sin(s.age * s.rate));
                Apply(s);
            }

            for (int i = 0; i < _finished.Count; i++)
            {
                if (!_wind.TryGetValue(_finished[i], out Lean s)) continue;
                if (s.t != null) s.t.rotation = s.baseRot;
                _wind.Remove(_finished[i]);
            }

            _finished.Clear();
        }

        /// <summary>Tips a prop over along the push direction and lets it spring back.</summary>
        static void Apply(Lean s)
        {
            if (s.t == null) return;

            Vector3 axis = Vector3.Cross(Vector3.up, s.dir);
            if (axis.sqrMagnitude < 0.0001f) return;

            s.t.rotation = s.baseRot * Quaternion.AngleAxis(s.angle, axis.normalized);
        }

        /// <summary>End of a prop's flight: it vanishes (or is handed back to its pool).</summary>
        void Finish(Flyer f)
        {
            Transform t = f.t;
            if (t == null) return;

            t.localScale = f.baseScale;             // a pooled prop is reused exactly as it was
            t.rotation = f.baseRot;

            if (f.mode == MagicEnvMode.Hide) t.gameObject.SetActive(false);
            else Object.Destroy(t.gameObject);      // chunk content grows back with the chunk
        }

        static void PruneRoots()
        {
            for (int i = Roots.Count - 1; i >= 0; i--)
                if (Roots[i] == null || Roots[i].t == null) Roots.RemoveAt(i);
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;

            Roots.Clear();
            _sway.Clear();
            _wind.Clear();
            _flyers.Clear();
            _flying.Clear();
        }
    }
}


