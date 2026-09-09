using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// A magical cloud of tiny glowing dust motes that rise out of the ground near the
    /// player/camera. Each mote is a very cheap additive billboard dot (no real lights,
    /// no particle system) that fades in, floats upward with a little sway, twinkles
    /// (pulses), and is recycled close to the player - so the effect is always right
    /// around you and never rendered across the whole map.
    /// </summary>
    public class MagicalDust : MonoBehaviour
    {
        [Tooltip("Who the dust surrounds (usually the player camera).")]
        public Transform target;

        [Header("Appearance")]
        public Color color = new Color(0.7f, 0.95f, 1f, 1f);
        [Tooltip("Base brightness / glow of the motes.")]
        [Range(0f, 4f)] public float glowStrength = 1.2f;
        [Tooltip("How strongly the motes pulse (0 = steady).")]
        [Range(0f, 1f)] public float pulseAmount = 0.55f;
        [Tooltip("Pulse speed in cycles per second.")]
        [Range(0f, 5f)] public float pulseSpeed = 1.6f;

        [Header("Spawning")]
        [Tooltip("How many dust motes are alive around the player at once.")]
        public int count = 60;
        [Tooltip("Horizontal radius (m) around the player where motes rise from the ground.")]
        public float radius = 16f;
        [Tooltip("Base diameter of each mote in metres.")]
        [Range(0.03f, 1.5f)] public float particleSize = 0.22f;
        [Tooltip("How fast the motes float upward (m/s).")]
        [Range(0.1f, 3f)] public float riseSpeed = 0.9f;
        [Tooltip("Average seconds a mote floats before it fades and respawns.")]
        [Range(1f, 9f)] public float lifetime = 4f;

        [Header("Illumination")]
        [Tooltip("Also softly light the ground around you with a single cheap point light (no shadows).")]
        public bool illuminate = true;
        [Tooltip("How strong that surrounding light is.")]
        [Range(0f, 2f)] public float lightStrength = 0.6f;

        Material _mat;
        Light _light;
        MeshRenderer[] _rends;
        MaterialPropertyBlock[] _blocks;
        Vector3[] _origin;      // where the mote rose from (world)
        float[] _age;
        float[] _life;
        float[] _phase;
        float[] _dia;           // current world diameter of the mote
        float[] _vel;
        Camera _cam;
        bool _ready;

        void OnEnable() { if (!_ready) Build(); }

        void Build()
        {
            if (_ready) return;
            Shader sh = Shader.Find("MysticMap/BillboardGlow");
            if (sh == null)
            {
                Debug.LogWarning("MagicalDust: MysticMap/BillboardGlow shader not found - effect disabled.");
                enabled = false;
                return;
            }
            _mat = new Material(sh);              // private instance (won't touch the shared grass glow)
            _mat.name = "MagicalDustMat";
            _mat.renderQueue = 3000;

            Mesh mesh = CreateQuad();
            int n = Mathf.Clamp(count, 1, 300);
            _rends = new MeshRenderer[n];
            _blocks = new MaterialPropertyBlock[n];
            _origin = new Vector3[n];
            _age = new float[n];
            _life = new float[n];
            _phase = new float[n];
            _dia = new float[n];
            _vel = new float[n];

            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("Mote" + i);
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.mesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                _rends[i] = mr;
                _blocks[i] = new MaterialPropertyBlock();
                // Spread the pulse phases so the cloud shimmers instead of all blinking
                // together.
                _phase[i] = (i * 2.399963f) % 6.2831853f;
                _dia[i] = Mathf.Max(0.02f, particleSize);
                _age[i] = 0f;
            }

            // One gentle, shadow-less point light so the dust appears to light its
            // surroundings. A single light is cheap; real lights per-mote would not be.
            var liGo = new GameObject("GlowLight");
            liGo.transform.SetParent(transform, false);
            _light = liGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;
            _light.color = color;
            _light.range = Mathf.Clamp(radius * 1.15f, 4f, 60f);
            _ready = true;
        }

        void Update()
        {
            if (!_ready) { Build(); if (!_ready) return; }
            if (_cam == null) _cam = Camera.main;
            Transform t = target != null ? target : (_cam != null ? _cam.transform : transform);

            Vector3 center = t.position;
            float ground = SampleGround(center.x, center.z, center.y);

            // Follow the player with the soft light and match it to the dust glow.
            if (_light != null)
            {
                _light.enabled = illuminate && lightStrength > 0.01f;
                if (_light.enabled)
                {
                    float ly = Mathf.Max(ground, center.y - 0.2f) + 1.6f;   // hover near chest height
                    _light.transform.position = new Vector3(center.x, ly, center.z);
                    _light.color = color;
                    _light.intensity = lightStrength;
                    _light.range = Mathf.Clamp(radius * 1.15f, 4f, 60f);
                }
            }

            float pulsePeriod = pulseSpeed > 0f ? (pulseSpeed * 6.2831853f) : 0f;

            for (int i = 0; i < _rends.Length; i++)
            {
                _age[i] += Time.deltaTime;

                // Where this mote currently floats.
                Vector3 cur = _origin[i] + new Vector3(0f, _age[i] * _vel[i], 0f);
                float dx = cur.x - center.x, dz = cur.z - center.z;

                // Respawn when it fades out or drifts too far behind the moving player.
                bool stale = _age[i] >= _life[i] || (dx * dx + dz * dz) > radius * radius * 1.8f;
                if (stale) Respawn(i, center, ground);
                cur = _origin[i] + new Vector3(0f, _age[i] * _vel[i], 0f);

                // Gentle sideways drift so it reads as floating dust, not a laser beam.
                float swayX = Mathf.Sin((_age[i] * 0.6f) + _phase[i]) * 0.4f;
                float swayZ = Mathf.Cos((_age[i] * 0.5f) + _phase[i] * 1.3f) * 0.4f;
                MoteMove(i, new Vector3(cur.x + swayX, cur.y, cur.z + swayZ));

                // Soft fade in at the ground and fade out near the top of its life.
                float fadeIn = Mathf.Clamp01(_age[i] / 0.5f);
                float fadeOut = Mathf.Clamp01((_life[i] - _age[i]) / 0.8f);
                float fade = fadeIn * fadeOut;

                // Twinkle: a gentle sine brightness pulse at the chosen speed.
                float pulse = 1f;
                if (pulseAmount > 0f && pulsePeriod > 0f)
                    pulse += Mathf.Sin(Time.time * pulsePeriod + _phase[i]) * pulseAmount;

                float intensity = Mathf.Max(0.0001f, glowStrength * fade * Mathf.Max(0f, pulse));

                var b = _blocks[i];
                b.SetColor("_Color", color);
                b.SetFloat("_Intensity", intensity);
                b.SetFloat("_Falloff", 2.5f);
                b.SetFloat("_Size", _dia[i]);
                b.SetFloat("_Aspect", 1f);
                _rends[i].SetPropertyBlock(b);
            }
        }

        void Respawn(int i, Vector3 center, float ground)
        {
            float ang = Random.Range(0f, Mathf.PI * 2f);
            float rr = Mathf.Sqrt(Random.Range(0f, 1f));            // bias towards the player
            float r = Mathf.Lerp(1.5f, Mathf.Max(2f, radius), rr);
            float x = center.x + Mathf.Cos(ang) * r;
            float z = center.z + Mathf.Sin(ang) * r;
            float g = SampleGround(x, z, ground);

            _origin[i] = new Vector3(x, g + 0.03f + Random.Range(0f, 0.08f), z);
            _age[i] = 0f;
            _life[i] = Mathf.Max(0.5f, lifetime * Random.Range(0.7f, 1.3f));
            _vel[i] = riseSpeed * Random.Range(0.7f, 1.35f);
            _dia[i] = Mathf.Max(0.02f, particleSize * Random.Range(0.6f, 1.5f));
            MoteMove(i, _origin[i]);
        }

        // Reposition a mote child in world space.
        void MoteMove(int i, Vector3 world)
        {
            Transform ch = transform.GetChild(i);
            if (ch != null) ch.position = world;
        }


        static Mesh _quad;
        static Mesh CreateQuad()
        {
            if (_quad != null) return _quad;
            _quad = new Mesh();
            _quad.name = "MagicalDustQuad";
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
            };
            _quad.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            _quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            _quad.RecalculateNormals();
            return _quad;
        }

        static float SampleGround(float x, float z, float fallbackY)
        {
            var t = Terrain.activeTerrain;
            if (t != null) return t.SampleHeight(new Vector3(x, 0f, z));
            if (Physics.Raycast(new Vector3(x, 200f, z), Vector3.down, out RaycastHit hit, 400f))
                return hit.point.y;
            return fallbackY;
        }

        void OnDisable()
        {
            // Hide everything cleanly if the component gets turned off.
            for (int i = 0; i < transform.childCount; i++)
            {
                var ch = transform.GetChild(i);
                if (ch != null) ch.gameObject.SetActive(false);
            }
        }
    }
}

