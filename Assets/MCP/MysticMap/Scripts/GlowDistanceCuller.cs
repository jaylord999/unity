using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// Keeps the little glow sprites (the glow under grass/props) only within a short
    /// distance of the player, switching each prop's "GlowSprite" child off when it is
    /// too far away. Very cheap: it just toggles active state on a timer (the same
    /// trick PropStreamer uses), which removes far, unnoticeable glow draw calls.
    /// </summary>
    public class GlowDistanceCuller : MonoBehaviour
    {
        [Tooltip("Who to measure distance to (usually the player camera).")]
        public Transform target;
        [Tooltip("Glows further than this (m) from the player are hidden.")]
        public float radius = 25f;
        [Tooltip("Seconds between distance checks. Bigger = cheaper.")]
        public float checkInterval = 0.35f;

        Transform[] _prop;
        Transform[] _glow;
        float _timer;
        int _recheck;
        float _sqr;

        void Start() { Collect(); }

        void OnEnable()
        {
            if (_glow == null || _glow.Length == 0) Collect();
        }

        void Collect()
        {
            int n = transform.childCount;
            _prop = new Transform[n];
            _glow = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                _prop[i] = transform.GetChild(i);
                _glow[i] = (_prop[i] != null) ? _prop[i].Find("GlowSprite") : null;
            }
            _sqr = radius * radius;
        }

        void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = checkInterval;

            // Periodically re-read the children so glows added later (after this
            // component started) still get culled correctly.
            _recheck++;
            if (_recheck >= 3) { _recheck = 0; Collect(); }
            if (_prop == null || _prop.Length == 0) return;

            Transform t = target;
            if (t == null && Camera.main != null) t = Camera.main.transform;
            Vector3 cp = (t != null) ? t.position : transform.position;

            for (int i = 0; i < _glow.Length; i++)
            {
                Transform par = _prop[i];
                if (par == null || !par.gameObject.activeSelf) continue;  // prop already culled
                Transform g = _glow[i];
                if (g == null) continue;                                  // prop has no glow
                bool on = (par.position - cp).sqrMagnitude <= _sqr;
                if (g.gameObject.activeSelf != on) g.gameObject.SetActive(on);
            }
        }
    }
}
