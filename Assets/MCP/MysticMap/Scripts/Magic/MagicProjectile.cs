using System.Collections.Generic;
using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// A travelling spell: the slash / bolt / shard that leaves the slime when the button is
    /// released. It flies straight ahead (optionally skimming the ground, which looks right for
    /// a crescent of wind or water over a rolling procedural landscape), hurts the first thing
    /// it runs into, sprays the impact effect on it, and can either stop there or pierce on.
    ///
    /// Damage is dealt through <see cref="Damage"/>, so anything with an <see cref="IDamageable"/>
    /// (see <see cref="Health"/>) is a valid target and the terrain is not.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MysticMap/Magic Projectile")]
    public class MagicProjectile : MonoBehaviour
    {
        readonly RaycastHit[] _hits = new RaycastHit[8];
        readonly List<IDamageable> _alreadyHit = new List<IDamageable>(8);
        readonly List<Vector3> _splashPoints = new List<Vector3>(8);
        readonly List<Vector3> _shown = new List<Vector3>(16);   // where the impact effect was played

        Vector3 _direction = Vector3.forward;
        Transform _owner;
        LayerMask _mask;
        GameObject _impactEffect;

        float _speed = 22f;
        float _radius = 0.6f;
        float _damage;
        float _knockback;
        float _life = 3.2f;
        float _age;
        float _distance;
        float _maxDistance = 60f;
        float _splash;
        float _splashRadius;
        bool _pierce;
        bool _hugGround = true;
        float _hugHeight = 0.45f;

        MagicSpellLevel _level;
        MagicCastSpec _spec;
        float _windTimer;

        DamageInfo _info;

        /// <summary>Fires one projectile (the effect prefab is spawned and given this component).</summary>
        public static MagicProjectile Launch(GameObject prefab, Vector3 position, Vector3 direction,
                                             MagicSpellLevel level, float damage, GameObject owner,
                                             LayerMask mask)
        {
            var spec = new MagicCastSpec
            {
                damage = damage,
                power = 0.4f,
                environmentScale = level != null ? Mathf.Max(0f, level.environmentScale) : 1f,
                direction = direction.normalized
            };

            return Launch(prefab, position, direction, level, spec, owner, mask);
        }

        /// <summary>
        /// Fires one projectile with everything the world reaction needs (power, maximum-charge
        /// flag and the debris it may throw).
        /// </summary>
        public static MagicProjectile Launch(GameObject prefab, Vector3 position, Vector3 direction,
                                             MagicSpellLevel level, MagicCastSpec spec, GameObject owner,
                                             LayerMask mask)
        {
            if (prefab == null || level == null) return null;

            direction = Flat(direction);
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;

            GameObject go = MagicEffects.Spawn(prefab, position, Quaternion.LookRotation(direction), null,
                                               Mathf.Max(0.01f, level.castScale));
            if (go == null) return null;

            MagicEffects.ForcePlay(go, true);

            var projectile = go.GetComponent<MagicProjectile>();
            if (projectile == null) projectile = go.AddComponent<MagicProjectile>();

            projectile._level = level;
            projectile._direction = direction.normalized;
            projectile._owner = owner != null ? owner.transform : null;
            projectile._mask = mask;
            projectile._speed = Mathf.Max(0.5f, level.speed);
            projectile._radius = Mathf.Max(0.05f, level.radius);
            projectile._damage = Mathf.Max(0f, spec.damage);
            projectile._knockback = Mathf.Max(0f, level.knockback);
            projectile._life = projectile._maxDistance / projectile._speed + 0.4f;
            projectile._splash = level.splash;
            projectile._splashRadius = Mathf.Max(0.1f, level.splashRadius);
            projectile._pierce = level.pierce;
            projectile._hugGround = level.hugGround;
            projectile._hugHeight = Mathf.Max(0f, level.hugHeight);
            projectile._impactEffect = level.impactEffect;

            spec.direction = direction.normalized;
            spec.damage = projectile._damage;
            projectile._spec = spec;
            projectile._info = new DamageInfo(projectile._damage, position, direction.normalized,
                                              projectile._knockback, owner);

            MagicEffects.DestroyLater(go, projectile._life + 1f);
            return projectile;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;

            if (_age > _life) { Destroy(gameObject); return; }

            float step = _speed * dt;
            Vector3 from = transform.position;
            Vector3 to = from + _direction * step;

            if (_hugGround) to = Hug(to);

            _distance += step;
            transform.SetPositionAndRotation(to, Quaternion.LookRotation(_direction));

            if (step > 0.0001f) Sweep(from, to);

            Breeze();

            if (_distance >= _maxDistance) Destroy(gameObject);
        }

        /// <summary>
        /// The grass leans out of the way while the spell flies over it (and a hard-charged spell
        /// even pulls a few blades off the ground).
        /// </summary>
        void Breeze()
        {
            if (_spec.environmentScale <= 0.01f) return;

            _windTimer -= Time.deltaTime;
            if (_windTimer > 0f) return;

            _windTimer = 0.07f;

            float radius = Mathf.Max(1.1f, _radius * 3f) * _spec.environmentScale;
            float strength = 8f + 18f * Mathf.Clamp01(_spec.power * 0.6f);

            MagicEnvironment.Wind(transform.position, radius, strength, 0.4f);
        }

        /// <summary>The travel direction is always flat, so a slash never dives into the ground.</summary>
        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        /// <summary>
        /// Keeps the flight path a fixed height above the ground (ground slashes). Rocks, trees and
        /// walls are skipped on purpose: the spell has to run into them and blast, not climb over
        /// them and sail on.
        /// </summary>
        Vector3 Hug(Vector3 to)
        {
            Vector3 probe = to + Vector3.up * 4f;
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit ground, 12f, _mask,
                                 QueryTriggerInteraction.Ignore))
                return to;

            if (MagicEnvironment.IsSolid(ground.collider)) return to;   // something to hit, not ground

            to.y = ground.point.y + _hugHeight;
            return to;
        }

        /// <summary>Sphere-casts the frame's movement and resolves the first (or every) victim.</summary>
        void Sweep(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.0001f) return;

            int count = Physics.SphereCastNonAlloc(from, _radius, delta / distance, _hits, distance,
                                                  _mask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider col = _hits[i].collider;
                if (col == null) continue;
                if (_owner != null && (col.transform == _owner || col.transform.IsChildOf(_owner))) continue;

                IDamageable victim = Damage.Find(col);
                if (victim == null || !victim.IsAlive)
                {
                    // Nothing hurtable on it: a rock, a tree or a wall still stops the spell, so it
                    // blasts on the surface instead of flying through it. Grass is not solid, so a
                    // slash keeps going over the fields.
                    if (!MagicEnvironment.IsSolid(col)) continue;

                    Land(_hits[i].point == Vector3.zero ? transform.position : _hits[i].point, false);
                    return;
                }

                if (_alreadyHit.Contains(victim)) continue;

                _alreadyHit.Add(victim);

                Vector3 point = _hits[i].point == Vector3.zero ? transform.position : _hits[i].point;

                // Damage.Apply already plays the impact effect on the victim.
                Damage.Apply(col, _info.Retarget(point), _owner, _impactEffect);

                // The world around the victim reacts as well (grass, dust, thrown stones).
                HitWorld(point, 0.8f);

                if (_pierce) continue;

                Land(point, true);
                return;
            }
        }

        /// <summary>
        /// Lets the world react to an impact: plants are torn away, trees are shaken, small stones
        /// are kicked and debris is thrown. <paramref name="scale"/> is how much of the projectile
        /// reaches the ground.
        /// </summary>
        void HitWorld(Vector3 point, float scale)
        {
            if (_spec.environmentScale <= 0.01f) return;

            float radius = Mathf.Max(0.8f, _radius * 3.2f * scale) * _spec.environmentScale;

            MagicEnvironment.Impact(point, radius, _spec.EnvironmentPower, _spec.debris, _spec.debrisPerHit);
            _spec.ThrowDebris(point, Mathf.Min(_spec.debrisPerHit, 4), 0.9f);
        }

        /// <summary>
        /// End of the flight: the splash damage, the world's reaction, and (when it was released at
        /// maximum charge) the big explosion around the impact point.
        /// </summary>
        void Land(Vector3 point, bool worldAlreadyHit = false)
        {
            if (!worldAlreadyHit) HitWorld(point, 1f);

            // The impact effect has already been played on the victim that stopped the spell, so the
            // splash and the big explosion below never play it a second (or third) time on it.
            _shown.Clear();
            _shown.Add(point);

            if (_splash > 0.01f && _splashRadius > 0.01f)
            {
                var splashInfo = new DamageInfo(_damage * _splash, point, _direction, _knockback, _info.source);

                _splashPoints.Clear();
                Damage.ApplyRadial(point, _splashRadius, splashInfo, _owner, _mask, _splashPoints);

                // One impact effect per extra victim, and never twice on the same spot.
                for (int i = 0; i < _splashPoints.Count; i++)
                {
                    Vector3 spot = _splashPoints[i];
                    if (!MagicEffects.Fresh(_shown, spot)) continue;

                    _shown.Add(spot);
                    MagicEffects.DestroyLater(
                        MagicEffects.Spawn(_impactEffect, spot, Quaternion.identity), 0f);
                }
            }

            if (_spec.maxCharge)
                SlimeMagic.MaxChargeExplosion(_level, point, _direction, _spec, _owner, _mask, _shown);

            Destroy(gameObject);
        }
    }
}
