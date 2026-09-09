using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// Manages a field of "glow orbs" sitting on the grass. Each orb is a soft,
    /// additive, camera-facing light ball (very cheap - no real point lights).
    /// Only orbs near the target are active, and they always face the camera so
    /// they read as round balls of light instead of flat ground stains.
    /// </summary>
    public class GlowOrbs : MonoBehaviour
    {
        [Tooltip("Who to measure distance to (usually the player camera).")]
        public Transform target;
        [Tooltip("Radius (m) around the target within which orbs are active.")]
        public float radius = 90f;
        [Tooltip("Seconds between distance checks. Bigger = cheaper.")]
        public float checkInterval = 0.25f;
        [Tooltip("Slow breathing of the orbs. 0 = steady.")]
        public float pulseSpeed = 0f;
        [Tooltip("How much the orbs grow/shrink when pulsing (0 = none).")]
        public float pulseAmount = 0f;

        Transform[] _children;
        Vector3[] _baseScale;
        Camera _cam;
        float _timer;
        float _sqr;

        void Start()
        {
            _children = new Transform[transform.childCount];
            _baseScale = new Vector3[transform.childCount];
            for (int i = 0; i < _children.Length; i++)
            {
                _children[i] = transform.GetChild(i);
                _baseScale[i] = _children[i].localScale;
            }
            if (target == null) target = (Camera.main != null) ? Camera.main.transform : transform;
            _cam = Camera.main;
            _sqr = radius * radius;
        }

        void Update()
        {
            if (_cam == null) _cam = Camera.main;
            if (target == null && _cam != null) target = _cam.transform;
            Vector3 cp = target != null ? target.position : transform.position;

            _timer -= Time.deltaTime;
            bool checkDist = _timer <= 0f;
            if (checkDist) _timer = checkInterval;

            bool pulseOn = Mathf.Abs(pulseAmount) > 0.001f;

            for (int i = 0; i < _children.Length; i++)
            {
                Transform ch = _children[i];
                if (ch == null) continue;

                if (checkDist)
                {
                    bool on = (cp - ch.position).sqrMagnitude <= _sqr;
                    if (ch.gameObject.activeSelf != on) ch.gameObject.SetActive(on);
                }
                if (!ch.gameObject.activeSelf) continue;

                // Always face the camera so it looks like a round ball of light.
                if (_cam != null) ch.rotation = _cam.transform.rotation;

                if (pulseOn)
                {
                    float s = 1f + Mathf.Sin(Time.time * pulseSpeed + i * 1.7f) * pulseAmount;
                    ch.localScale = _baseScale[i] * s;
                }
            }
        }
    }
}
