using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// Keeps a large set of scattered props performant by only keeping children active
    /// when they are BOTH (a) close enough AND (b) roughly IN FRONT of the tracked camera.
    ///
    /// Unity already skips *drawing* anything off-screen (automatic frustum culling), so this
    /// script targets the CPU side: it deactivates props that are far away OR clearly behind
    /// the camera, so they never tick Update(), glows or animation. That costs far less than
    /// keeping a full circle of props alive all around the player.
    ///
    /// To avoid visible pop-in when the player turns or walks near the cut edge:
    ///   - "keepBehindRadius" keeps a small all-around bubble always on, and
    ///   - radius + angle hysteresis means a prop has to drift clearly out of range before it
    ///     is switched off again, and it pops back the moment it is comfortably back in view.
    /// </summary>
    public class PropStreamer : MonoBehaviour
    {
        [Tooltip("Who to measure distance and view direction from (usually the player camera).")]
        public Transform target;

        [Tooltip("Max distance (m) at which props are kept active in front of the camera.")]
        public float radius = 90f;

        [Tooltip("Small all-around radius (m). Props this close are ALWAYS active no matter where the camera points, so turning/moving near them never causes pop-in.")]
        public float keepBehindRadius = 25f;

        [Tooltip("Only cull a prop once it sits MORE than this many degrees from the camera's forward direction. 90 = front half only; ~110-130 adds a buffer so props at the sides don't flicker.")]
        [Range(90f, 170f)]
        public float behindCullAngle = 115f;

        [Tooltip("Hysteresis multiplier: a shown prop must drift to this larger distance / this wider angle before it is hidden, preventing flicker at the cut boundary.")]
        [Range(1.02f, 2f)]
        public float hysteresis = 1.2f;

        [Tooltip("How often (seconds) the distance/view check runs. Larger = cheaper.")]
        public float checkInterval = 0.35f;

        Transform[] _children;
        bool[] _shown;
        float _timer;

        float _keepBehindSqr;
        float _activateCos;      // cos(cull angle): threshold to switch a prop ON
        float _deactivateCos;    // cos(cull angle * hysteresis): threshold to switch OFF
        float _deactivateRadius; // radius * hysteresis

        void Start()
        {
            Collect();
            ResolveTarget();
            RecomputeConstants();
        }

        void OnEnable()
        {
            if (_children == null) Collect();
        }

        void ResolveTarget()
        {
            if (target == null)
            {
                Camera c = Camera.main;
                if (c != null) target = c.transform;
            }
            if (target == null) target = transform;
        }

        void Collect()
        {
            int n = transform.childCount;
            _children = new Transform[n];
            _shown = new bool[n];
            for (int i = 0; i < n; i++)
            {
                _children[i] = transform.GetChild(i);
                _shown[i] = _children[i] != null && _children[i].gameObject.activeSelf;
            }
        }

        void RecomputeConstants()
        {
            _keepBehindSqr = keepBehindRadius * keepBehindRadius;

            float cull = Mathf.Clamp(behindCullAngle, 90f, 170f);
            float cullOff = Mathf.Min(cull * hysteresis, 179f);
            _activateCos = Mathf.Cos(cull * Mathf.Deg2Rad);
            _deactivateCos = Mathf.Cos(cullOff * Mathf.Deg2Rad);

            _deactivateRadius = Mathf.Max(radius, radius * hysteresis);
        }

        void Update()
        {
            if (target == null)
            {
                ResolveTarget();
                if (target == null) return;
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = checkInterval;

            Vector3 camPos = target.position;
            Vector3 fwd = target.forward;

            for (int i = 0; i < _children.Length; i++)
            {
                Transform ch = _children[i];
                if (ch == null) continue;

                bool on = ShouldShow(ch, camPos, fwd, _shown[i]);
                if (ch.gameObject.activeSelf != on)
                {
                    ch.gameObject.SetActive(on);
                    _shown[i] = on;
                }
            }
        }

        // Decides whether one prop should be active for this check.
        bool ShouldShow(Transform ch, Vector3 camPos, Vector3 fwd, bool shown)
        {
            Vector3 toObj = ch.position - camPos;
            float distSqr = toObj.sqrMagnitude;

            // Always-kept bubble: props this close stay on regardless of facing, so you
            // never see pop-in while turning/moving through the nearby world.
            if (distSqr <= _keepBehindSqr) return true;

            // Distance test with hysteresis: switch on within "radius", only switch off
            // once it goes beyond "radius * hysteresis".
            float maxDist = shown ? _deactivateRadius : radius;
            if (distSqr > maxDist * maxDist) return false;

            // Front/behind test: hide only props that are clearly behind the camera,
            // keeping everything roughly forward/sideways active. Hysteresis widens the
            // cull angle while a prop is shown so it doesn't flicker at the boundary.
            float dist = Mathf.Sqrt(distSqr);
            float cosA = dist > 0.0001f
                ? (toObj.x * fwd.x + toObj.y * fwd.y + toObj.z * fwd.z) / dist
                : 1f;

            return cosA >= (shown ? _deactivateCos : _activateCos);
        }

        void OnDrawGizmosSelected()
        {
            Vector3 p = transform.position;

            // Always-on keep bubble.
            Gizmos.color = new Color(0f, 1f, 0.5f, 0.6f);
            Gizmos.DrawWireSphere(p, keepBehindRadius);

            // Full view range (a prop here is only active if it is also in front).
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.1f);
            Gizmos.DrawSphere(p, radius);

            // Indicate the forward cull cone so you can preview the active region.
            Vector3 fwd = (target != null) ? target.forward : transform.forward;
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.4f);
            Vector3 side = Vector3.Cross(fwd, Vector3.up);
            if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
            side.Normalize();
            float reach = Mathf.Max(radius, 1f);
            float rad = behindCullAngle * Mathf.Deg2Rad;
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 dir = (Quaternion.AngleAxis(i * rad, side) * fwd).normalized;
                Gizmos.DrawRay(p, dir * reach);
                Gizmos.DrawRay(p + Vector3.up * 0.5f, dir * reach);
            }
        }
    }
}
