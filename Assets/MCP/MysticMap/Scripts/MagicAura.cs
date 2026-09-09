using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// A soft, additive "magic aura" glow. Strongest in the middle and fading outward.
    /// Color, strength, size and softness are all tunable (works on Built-in pipeline).
    /// The visible glow lives on a child "GlowDisc" that this component keeps updated.
    /// </summary>
    public class MagicAura : MonoBehaviour
    {
        [Header("Look")]
        public Color color = new Color(0.35f, 1f, 0.6f, 1f);
        [Range(0f, 4f)] public float intensity = 0.8f;
        [Range(0.5f, 6f)] public float softness = 2f;

        [Header("Size / position")]
        [Tooltip("Radius of the glow in metres.")]
        public float radius = 60f;
        [Tooltip("Height above the ground/anchor the glow hovers at.")]
        public float centerHeight = 1.5f;
        [Tooltip("If on, the glow follows the player so the field is magical wherever you walk.")]
        public bool followPlayer = false;

        Transform _disc;
        Material _mat;
        Camera _cam;
        float _lastRadius = -1f;
        bool _built;

        void Awake()
        {
            _cam = Camera.main;
        }

        void Start()
        {
            BuildDisc();
        }

        void BuildDisc()
        {
            if (_built) return;
            _built = true;

            Transform child = transform.Find("GlowDisc");
            if (child == null)
            {
                var go = new GameObject("GlowDisc");
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                var quad = new Mesh();
                quad.name = "AuraQuad";
                quad.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
                };
                quad.uv = new[]
                {
                    new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(0f, 1f), new Vector2(1f, 1f)
                };
                quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                quad.RecalculateNormals();
                mf.mesh = quad;

                Shader s = Shader.Find("MysticMap/MagicAura");
                var mat = new Material(s != null ? s : Shader.Find("Sprites/Default"));
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                child = go.transform;
            }

            _disc = child;
            var mr = _disc.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                _mat = mr.material; // per-instance so it can be animated
            }
        }

        void Update()
        {
            BuildDisc();
            if (_disc == null) return;

            if (_mat != null)
            {
                _mat.SetColor("_Color", color);
                _mat.SetFloat("_Intensity", Mathf.Max(0f, intensity));
                _mat.SetFloat("_Falloff", Mathf.Max(0.05f, softness));
            }

            // Size
            float d = radius * 2f;
            if (Mathf.Abs(d - _lastRadius) > 0.001f)
            {
                _disc.localScale = new Vector3(d, d, 1f);
                _lastRadius = d;
            }

            // Center + billboard
            if (_cam == null) _cam = Camera.main;
            Vector3 target = transform.position;
            if (followPlayer && _cam != null)
                target = _cam.transform.position + Vector3.down * 1.4f;

            // Hover just above the ground so it looks like a glow at the plants' bases.
            float ground = 0f;
            var terr = Terrain.activeTerrain;
            if (terr != null) ground = terr.SampleHeight(new Vector3(target.x, 0f, target.z));
            target.y = ground + centerHeight;
            transform.position = target;

            if (_cam != null)
                _disc.rotation = _cam.transform.rotation; // face the camera (soft glow blob)
        }
    }
}
