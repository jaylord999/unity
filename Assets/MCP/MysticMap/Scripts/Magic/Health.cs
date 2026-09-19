using System;
using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// Hit points for anything the slime's magic should be able to hurt. It is the ready-made
    /// <see cref="IDamageable"/>, so props, creature prefabs and the test dummies the editor
    /// tool can spawn all take spell damage the same way: play the hit effect at the impact
    /// point, flash the body, optionally push it with a rigidbody, and die (or come back).
    ///
    /// The flash is driven through a MaterialPropertyBlock, so it never instantiates a material
    /// and it works on the pack's additive shaders as well as on the Standard one.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MysticMap/Health")]
    public class Health : MonoBehaviour, IDamageable
    {
        [Header("Hit points")]
        [Tooltip("Hit points before the target dies.")]
        public float maxHealth = 100f;
        [Tooltip("Ignore all damage (a friendly, an invulnerable boss phase, ...).")]
        public bool invulnerable;

        [Header("Death")]
        [Tooltip("Remove the object once it dies.")]
        public bool destroyOnDeath = true;
        [Tooltip("Small delay so the death effect is seen before the object goes.")]
        public float destroyDelay = 0.25f;
        public GameObject deathEffect;
        [Tooltip("Seconds after dying before the target is restored (0 = stays dead). " +
                 "Handy for test dummies, so they can be hit again and again.")]
        public float reviveAfter;

        [Header("Feedback")]
        [Tooltip("Played at the impact point of every hit.")]
        public GameObject hitEffect;
        [Tooltip("How long the hit effect lives (0 = as long as its own particles need).")]
        public float hitEffectLifetime;
        public Color flashColor = new Color(1f, 0.55f, 0.55f, 1f);
        public float flashTime = 0.12f;
        [Tooltip("Log every hit (useful while tuning damage numbers).")]
        public bool logDamage;

        [Header("World reaction")]
        [Tooltip("Make the ground around the victim react to the hit: grass is blown away, trees are " +
                 "shaken and - when the victim dies - rocks and heavy props around it are shattered.")]
        public bool stirEnvironment = true;
        [Tooltip("Radius of that reaction, in metres.")]
        public float environmentRadius = 3f;
        [Tooltip("Debris thrown when the victim dies (empty = procedural pebbles).")]
        public GameObject[] debrisPrefabs;
        [Range(0, 40)] public int debrisOnDeath = 8;

        /// <summary>Raised for every hit that lands, with the full description of it.</summary>
        public event Action<DamageInfo> Damaged;
        public event Action Died;
        public event Action Revived;

        float _health;
        float _flash;
        float _deadFor;
        bool _dead;
        Renderer[] _renderers;
        MaterialPropertyBlock _block;

        /// <summary>Current hit points.</summary>
        public float Current => _health;
        /// <summary>Current hit points as 0..1.</summary>
        public float Normalized => maxHealth > 0.01f ? Mathf.Clamp01(_health / maxHealth) : 0f;
        public bool IsAlive => !_dead && _health > 0f;

        void Awake()
        {
            _health = Mathf.Max(1f, maxHealth);
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        void OnValidate()
        {
            maxHealth = Mathf.Max(1f, maxHealth);
            flashTime = Mathf.Max(0f, flashTime);
            reviveAfter = Mathf.Max(0f, reviveAfter);
        }

        void Update()
        {
            if (_flash > 0f)
            {
                _flash -= Time.deltaTime;
                if (_flash <= 0f) ClearFlash();
            }

            if (_dead && reviveAfter > 0f)
            {
                _deadFor += Time.deltaTime;
                if (_deadFor >= reviveAfter) Revive();
            }
        }

        public void ApplyDamage(DamageInfo info)
        {
            if (!IsAlive || invulnerable) return;

            float damage = Mathf.Max(0f, info.amount);
            _health = Mathf.Max(0f, _health - damage);
            _flash = Mathf.Max(_flash, flashTime);
            ApplyFlash();

            if (hitEffect != null)
            {
                GameObject fx = MagicEffects.Spawn(hitEffect, info.point, Damage.LookRotation(info.normal));
                MagicEffects.DestroyLater(fx, hitEffectLifetime);
            }

            Push(info);

            // The ground right around the victim reacts to the hit (the spell adds its own blast).
            if (stirEnvironment)
                MagicEnvironment.Wind(info.point, Mathf.Max(0.6f, environmentRadius * 0.6f), 24f, 0.4f);

            if (logDamage)
                Debug.Log("[MysticMap] " + name + " took " + damage.ToString("0.#") + " damage (" +
                          _health.ToString("0.#") + "/" + maxHealth.ToString("0.#") + ").", this);

            Damaged?.Invoke(info);

            if (_health <= 0f) Die(info);
        }

        /// <summary>Heals (and brings a dead target back when <see cref="reviveAfter"/> is used).</summary>
        public void Heal(float amount)
        {
            if (amount <= 0f) return;

            if (_dead) Revive();
            _health = Mathf.Min(Mathf.Max(1f, maxHealth), _health + amount);
        }

        /// <summary>Full hit points, alive and visible again.</summary>
        public void ResetHealth()
        {
            _health = Mathf.Max(1f, maxHealth);
            _dead = false;
            _deadFor = 0f;
            ClearFlash();
            SetVisible(true);
        }

        /// <summary>Pushes a rigidbody victim along the hit direction (no-op without one).</summary>
        void Push(DamageInfo info)
        {
            if (info.knockback <= 0f) return;
            if (!TryGetComponent(out Rigidbody body) || body.isKinematic) return;

            Vector3 direction = info.direction.sqrMagnitude > 0.0001f ? info.direction.normalized : info.normal;
            body.AddForce(direction * info.knockback, ForceMode.VelocityChange);
        }

        void Die(DamageInfo info)
        {
            if (_dead) return;
            _dead = true;
            _deadFor = 0f;
            ClearFlash();

            if (deathEffect != null)
                MagicEffects.DestroyLater(MagicEffects.Spawn(deathEffect, transform.position, Quaternion.identity));

            // A death is loud: the rocks and props around the victim shatter and debris is thrown.
            if (stirEnvironment)
            {
                MagicEnvironment.Break(transform.position, Mathf.Max(0.6f, environmentRadius), 2f,
                                       debrisPrefabs, Mathf.Max(2, debrisOnDeath / 2));

                MagicDebris.Spray(debrisPrefabs, transform.position + Vector3.up * 0.35f, info.direction,
                                  debrisOnDeath, Mathf.Clamp(info.amount * 0.03f, 0.6f, 2.4f));
            }

            Died?.Invoke();

            if (reviveAfter > 0f)
            {
                SetVisible(false);
                return;
            }

            if (destroyOnDeath) Destroy(gameObject, Mathf.Max(0f, destroyDelay));
        }

        void Revive()
        {
            ResetHealth();
            Revived?.Invoke();
        }

        void SetVisible(bool visible)
        {
            if (_renderers == null) return;
            foreach (Renderer r in _renderers)
                if (r != null) r.enabled = visible;
        }

        // ---- flash ---------------------------------------------------------

        void ApplyFlash()
        {
            if (_renderers == null || flashTime <= 0f) return;

            if (_block == null) _block = new MaterialPropertyBlock();

            foreach (Renderer r in _renderers)
            {
                if (r == null) continue;

                Material shared = r.sharedMaterial;
                if (shared == null) continue;

                _block.Clear();
                if (shared.HasProperty("_Color")) _block.SetColor("_Color", flashColor);
                if (shared.HasProperty("_BaseColor")) _block.SetColor("_BaseColor", flashColor);
                if (shared.HasProperty("_TintColor")) _block.SetColor("_TintColor", flashColor);
                r.SetPropertyBlock(_block);
            }
        }

        void ClearFlash()
        {
            _flash = 0f;
            if (_renderers == null) return;

            foreach (Renderer r in _renderers)
                if (r != null) r.SetPropertyBlock(null);
        }
    }
}
