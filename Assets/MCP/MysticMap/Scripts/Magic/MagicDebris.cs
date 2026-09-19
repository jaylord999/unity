using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// Throws small stones (and bits of whatever the magic just hit) around: every piece is a real
    /// rigidbody that is kicked out of the impact point, spins, bounces over the ground and cleans
    /// itself up.
    ///
    /// The prefabs come from the world's own palette (any small rock, stone or prop will do) - a
    /// piece is shrunk to pebble size no matter how big its source prefab is - and a primitive
    /// pebble is built on the fly when nothing is set, so debris always works.
    /// </summary>
    public static class MagicDebris
    {
        /// <summary>Most pieces alive at once, so a blast can never grow the scene without limit.</summary>
        public const int MaxAlive = 160;

        static int _alive;
        static Material _pebbleMaterial;

        /// <summary>How many pieces of debris are in the air right now.</summary>
        public static int Alive => _alive;

        /// <summary>
        /// Kicks <paramref name="count"/> pieces out of a point. <paramref name="direction"/> is the
        /// way the spell was travelling, <paramref name="power"/> (0.2 - 2.5) is how hard they fly
        /// and <paramref name="size"/> is roughly how big one piece is, in metres.
        /// </summary>
        public static int Spray(GameObject[] prefabs, Vector3 center, Vector3 direction, int count,
                                float power, float size = 0.22f, Transform parent = null)
        {
            if (count <= 0) return 0;

            count = Mathf.Min(count, 32);

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            direction.Normalize();

            int spawned = 0;
            for (int i = 0; i < count && _alive < MaxAlive; i++)
                if (Spawn(prefabs, center, direction, power, size, parent) != null) spawned++;

            return spawned;
        }

        /// <summary>One piece of debris. Returns it (null when nothing could be built).</summary>
        public static GameObject Spawn(GameObject[] prefabs, Vector3 center, Vector3 direction,
                                       float power, float size, Transform parent = null)
        {
            GameObject prefab = (prefabs != null && prefabs.Length > 0)
                ? prefabs[Random.Range(0, prefabs.Length)]
                : null;

            Vector3 spot = center + new Vector3(Random.Range(-1f, 1f), Random.Range(0.05f, 0.8f),
                                                Random.Range(-1f, 1f)) * size * 2f;

            GameObject go = prefab != null
                ? Object.Instantiate(prefab, spot, Random.rotation, parent)
                : Pebble(spot, parent);
            if (go == null) return null;

            go.name = (prefab != null ? prefab.name : "Pebble") + " (debris)";

            // Pebble-sized, whatever the source prefab was.
            float target = size * Random.Range(0.55f, 1.35f);
            float current = Largest(go);
            if (current > 0.001f)
                go.transform.localScale *= Mathf.Clamp(target / current, 0.02f, 40f);

            Rigidbody body = go.GetComponent<Rigidbody>();
            if (body == null) body = go.AddComponent<Rigidbody>();
            body.mass = Mathf.Max(0.05f, size * 6f);
            body.isKinematic = false;

            // The colliders are sorted out first: fast, small pieces need continuous detection, and
            // that only works once the piece really has a collider.
            MakePhysicsReady(go, size);

            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            Vector3 impulse = direction * Random.Range(0.35f, 1f) + Random.insideUnitSphere * 0.8f;
            impulse.y = Mathf.Abs(impulse.y) + 0.35f;
            float speed = Random.Range(2.2f, 6.4f) * (0.6f + 0.55f * Mathf.Clamp(power, 0.2f, 2.5f));

            body.AddForce(impulse.normalized * speed, ForceMode.VelocityChange);
            body.AddTorque(Random.insideUnitSphere * Random.Range(2f, 9f), ForceMode.VelocityChange);

            var piece = go.GetComponent<MagicDebrisPiece>();
            if (piece == null) piece = go.AddComponent<MagicDebrisPiece>();
            piece.life = Random.Range(4.5f, 11f);

            _alive++;
            return go;
        }

        /// <summary>
        /// Makes a freshly instantiated piece safe to simulate.
        ///
        /// Unity refuses to drive a concave (non-convex) mesh collider with a moving rigidbody -
        /// which is exactly what the map's rock prefabs are made of - so every mesh is turned into
        /// a convex hull when its asset allows it, replaced by a primitive when it does not, and a
        /// piece is guaranteed to end up with one solid (non-trigger) collider so it can never
        /// fall through the ground either.
        /// </summary>
        static void MakePhysicsReady(GameObject go, float size)
        {
            bool solid = false;

            foreach (Collider collider in go.GetComponentsInChildren<Collider>(true))
            {
                if (collider == null) continue;

                var mesh = collider as MeshCollider;
                if (mesh != null)
                {
                    if (!mesh.convex)
                    {
                        // Cooking a convex hull needs readable vertex data; the environment pack is
                        // mostly imported without "Read/Write Enabled", so fall back to a primitive.
                        if (mesh.sharedMesh != null && mesh.sharedMesh.isReadable) mesh.convex = true;
                        else { Object.Destroy(mesh); continue; }
                    }

                    mesh.isTrigger = false;
                    solid = true;
                    continue;
                }

                if (collider is TerrainCollider || collider is CharacterController)
                {
                    Object.Destroy(collider);
                    continue;
                }

                if (!collider.isTrigger) solid = true;
            }

            if (solid) return;

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = Mathf.Clamp(Mathf.Max(size * 0.6f, Largest(go) * 0.35f), 0.03f, 2f);
            sphere.isTrigger = false;
        }

        internal static void Release()
        {
            if (_alive > 0) _alive--;
        }

        static GameObject Pebble(Vector3 position, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Random.rotation;
            go.transform.localScale = Vector3.one * 0.18f;

            Material material = PebbleMaterial();
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;

            return go;
        }

        /// <summary>One shared rock-coloured material for the fallback pebbles.</summary>
        static Material PebbleMaterial()
        {
            if (_pebbleMaterial != null) return _pebbleMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
            if (shader == null) return null;                    // the primitive's own material will do

            _pebbleMaterial = new Material(shader) { name = "Magic debris pebble" };

            Color rock = new Color(0.42f, 0.40f, 0.36f);
            if (_pebbleMaterial.HasProperty("_BaseColor")) _pebbleMaterial.SetColor("_BaseColor", rock);
            if (_pebbleMaterial.HasProperty("_Color")) _pebbleMaterial.SetColor("_Color", rock);
            if (_pebbleMaterial.HasProperty("_Smoothness")) _pebbleMaterial.SetFloat("_Smoothness", 0.1f);
            if (_pebbleMaterial.HasProperty("_Glossiness")) _pebbleMaterial.SetFloat("_Glossiness", 0.1f);

            return _pebbleMaterial;
        }

        /// <summary>Biggest side of whatever the piece draws, in world metres.</summary>
        static float Largest(GameObject go)
        {
            float biggest = 0f;

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (r == null || r is TrailRenderer || r is LineRenderer) continue;

                Vector3 s = r.bounds.size;
                biggest = Mathf.Max(biggest, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
            }

            return biggest;
        }
    }

    /// <summary>
    /// One piece of debris: it lives for a few seconds, then removes itself (and gives its slot
    /// back to <see cref="MagicDebris"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MysticMap/Magic Debris Piece")]
    public class MagicDebrisPiece : MonoBehaviour
    {
        [Tooltip("Seconds this piece stays in the world.")]
        public float life = 8f;

        void Start() => Destroy(gameObject, Mathf.Max(0.25f, life));

        void OnDestroy() => MagicDebris.Release();
    }
}
