using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// Gives the slime hit points, so the spiders' bites and slashes actually mean something.
    ///
    /// The project's damage plumbing already knows how to hurt anything with an
    /// <see cref="IDamageable"/> (see <see cref="Health"/>), it just had no player to hurt: this
    /// adds the <see cref="Health"/> component to the slime and reacts to it.
    ///
    /// What it does:
    ///   * makes the slime hurtable (it is added to the prefab by the spider setup tool),
    ///   * shakes the third-person camera and flashes the screen whenever the slime is hit,
    ///   * slowly regenerates out of combat,
    ///   * when the slime dies, waits a moment and puts it back on its feet at the spot it started
    ///     from (the game has no death screen - the spiders should be a threat, not a game over),
    ///   * draws a small health bar and the damage flash with plain IMGUI, so nothing has to be
    ///     wired into the scene.
    ///
    /// The spider's own spells ignore their caster, and the slime ignores itself as caster too
    /// (SlimeMagic passes itself as the ignored transform), so the two can fight without
    /// punching themselves.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MysticMap/Player Vitals (health + damage feedback)")]
    [RequireComponent(typeof(Health))]
    public class PlayerVitals : MonoBehaviour
    {
        [Header("Health")]
        [Tooltip("The hit points component (auto-filled from this object).")]
        public Health health;
        [Tooltip("Hit points of the slime.")]
        public float maxHealth = 150f;
        [Tooltip("Hit points per second recovered while not being hit.")]
        public float regenPerSecond = 2.5f;
        [Tooltip("Seconds after the last hit before the regeneration starts.")]
        public float regenDelay = 6f;
        [Tooltip("Seconds the slime lies knocked out before it gets back up.")]
        public float reviveDelay = 3.5f;

        [Header("Feedback")]
        [Tooltip("How hard the camera kicks when the slime is hit.")]
        [Range(0f, 2f)] public float hitShake = 0.4f;
        [Tooltip("How long the red damage flash stays on screen.")]
        public float hitFlash = 0.4f;
        [Tooltip("Show a small health bar in the corner of the screen.")]
        public bool showHealthBar = true;
        [Tooltip("Log every hit the slime takes (handy while tuning the spiders).")]
        public bool logDamage;

        [Header("Where it comes back")]
        [Tooltip("Spot the slime revives at (empty = the place it started from).")]
        public Transform respawnPoint;

        SlimePlayer _player;
        float _flash;
        float _lastHit;
        float _downFor;
        bool _down;
        Vector3 _startPosition;
        Texture2D _pixel;

        /// <summary>Hit points left (0 when knocked out).</summary>
        public float Current => health != null ? health.Current : 0f;
        /// <summary>Hit points as 0..1 (for a HUD or a test).</summary>
        public float Normalized => health != null ? health.Normalized : 0f;
        /// <summary>True while the slime is knocked out and waiting to get back up.</summary>
        public bool IsDown => _down;

        // =====================================================================
        //  Lifecycle
        // =====================================================================
        void Awake()
        {
            if (health == null) health = GetComponent<Health>();
            _player = GetComponent<SlimePlayer>();

            if (health == null) health = gameObject.AddComponent<Health>();

            // The player never disappears: PlayerVitals handles getting up again.
            health.maxHealth = Mathf.Max(1f, maxHealth);
            health.destroyOnDeath = false;
            health.reviveAfter = 0f;
            health.ResetHealth();       // makes the hit points match maxHealth whatever Awake ran first

            _startPosition = respawnPoint != null ? respawnPoint.position : transform.position;
        }

        void OnEnable()
        {
            if (health == null) return;
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        void OnDisable()
        {
            if (health == null) return;
            health.Damaged -= OnDamaged;
            health.Died -= OnDied;
        }

        void OnDestroy()
        {
            if (_pixel != null) Destroy(_pixel);
        }

        // =====================================================================
        //  Loop
        // =====================================================================
        void Update()
        {
            float dt = Time.deltaTime;

            if (_flash > 0f) _flash -= dt;

            if (_down)
            {
                _downFor -= dt;
                if (_downFor <= 0f) StandUp();
                return;
            }

            _lastHit += dt;

            if (regenPerSecond > 0f && _lastHit >= Mathf.Max(0f, regenDelay) &&
                health != null && health.IsAlive)
                health.Heal(regenPerSecond * dt);
        }

        void OnDamaged(DamageInfo info)
        {
            _lastHit = 0f;
            _flash = Mathf.Max(_flash, Mathf.Max(0.05f, hitFlash));

            if (_player != null && hitShake > 0.001f)
                _player.ShakeCamera(hitShake, 0.18f);

            if (logDamage)
                Debug.Log("[MysticMap] the slime took " + info.amount.ToString("0.#") + " damage from " +
                          (info.source != null ? info.source.name : "something") + " (" +
                          Current.ToString("0.#") + "/" + maxHealth.ToString("0.#") + " left).", this);
        }

        void OnDied()
        {
            _down = true;
            _downFor = Mathf.Max(0.2f, reviveDelay);

            if (_player != null) _player.ShakeCamera(0.8f, 0.5f);

            Debug.Log("[MysticMap] the slime was knocked out - it gets back up in " +
                      reviveDelay.ToString("0.#") + " s.", this);
        }

        /// <summary>Puts the slime back on its feet at its starting spot with full hit points.</summary>
        void StandUp()
        {
            _down = false;
            _flash = 0f;

            if (_player != null) _player.Teleport(_startPosition);
            else transform.position = _startPosition;

            if (health != null) health.ResetHealth();

            _lastHit = 0f;
        }

        // =====================================================================
        //  HUD (plain IMGUI - nothing to wire up in the scene)
        // =====================================================================
        void OnGUI()
        {
            if (_flash > 0f) DrawFlash();
            if (showHealthBar) DrawHealthBar();
        }

        void DrawFlash()
        {
            float alpha = Mathf.Clamp01(_flash / Mathf.Max(0.05f, hitFlash)) * 0.28f;
            Color old = GUI.color;
            GUI.color = new Color(0.85f, 0.1f, 0.12f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Pixel());
            GUI.color = old;
        }

        void DrawHealthBar()
        {
            const float width = 260f;
            const float height = 16f;
            const float margin = 22f;

            float value = Normalized;
            var background = new Rect(margin, Screen.height - margin - height, width, height);
            var fill = new Rect(background.x + 2f, background.y + 2f,
                                (background.width - 4f) * value, background.height - 4f);

            Color old = GUI.color;

            GUI.color = new Color(0.05f, 0.05f, 0.09f, 0.68f);
            GUI.DrawTexture(new Rect(background.x - 2f, background.y - 2f,
                                     background.width + 4f, background.height + 4f), Pixel());

            GUI.color = Color.Lerp(new Color(0.95f, 0.2f, 0.2f), new Color(0.35f, 0.95f, 0.45f), value);
            GUI.DrawTexture(fill, Pixel());

            GUI.color = old;
        }

        Texture2D Pixel()
        {
            if (_pixel != null) return _pixel;

            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
            _pixel.name = "PlayerVitalsPixel";
            _pixel.hideFlags = HideFlags.HideAndDontSave;
            return _pixel;
        }
    }
}
