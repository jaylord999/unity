using System.Collections.Generic;
using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// Everything an attack tells its victim about a hit: how much, where, from which direction.
    /// It is a plain struct so a cast can hand the same description to every target it touches.
    /// </summary>
    public struct DamageInfo
    {
        public float amount;
        public Vector3 point;       // world position of the hit
        public Vector3 direction;   // travel direction of the attack
        public Vector3 normal;      // surface normal (or "away from the caster")
        public float knockback;     // impulse, in metres (0 = none)
        public GameObject source;   // who caused it (the player, a projectile, ...)

        public DamageInfo(float amount, Vector3 point, Vector3 direction = default,
                          float knockback = 0f, GameObject source = null)
        {
            this.amount = amount;
            this.point = point;
            this.direction = direction;
            this.normal = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;
            this.knockback = knockback;
            this.source = source;
        }

        /// <summary>A copy of this hit aimed at another point (used for area spells).</summary>
        public DamageInfo Retarget(Vector3 newPoint)
        {
            DamageInfo copy = this;
            Vector3 away = newPoint - copy.point;
            copy.point = newPoint;
            if (away.sqrMagnitude > 0.0001f) copy.normal = away.normalized;
            return copy;
        }
    }

    /// <summary>Anything a spell may hurt. Implement it (or use <see cref="Health"/>) on props,
    /// creatures and anything else that should react to magic.</summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        void ApplyDamage(DamageInfo info);
    }

    /// <summary>
    /// The shared damage plumbing: turning a <see cref="DamageInfo"/> into hits on whatever
    /// colliders an effect runs into. Every spell in the project goes through here, so a new
    /// victim only has to implement <see cref="IDamageable"/> once.
    /// </summary>
    public static class Damage
    {
        const int MaxHits = 64;

        static readonly Collider[] Overlaps = new Collider[MaxHits];
        static readonly List<IDamageable> Treated = new List<IDamageable>(MaxHits);

        /// <summary>The <see cref="IDamageable"/> a collider belongs to (itself, a parent or a child).</summary>
        public static IDamageable Find(Collider collider)
        {
            if (collider == null) return null;

            var own = collider.GetComponentInParent<IDamageable>();
            if (own != null) return own;

            // Some packs put the hurtable script on a child of the collider (weak points, hit boxes).
            return collider.GetComponentInChildren<IDamageable>();
        }

        /// <summary>
        /// Hits one collider and plays <paramref name="hitEffect"/> (if any) at the impact point.
        /// Returns true when something was actually hurt, so a projectile knows whether it landed.
        /// </summary>
        public static bool Apply(Collider target, DamageInfo info, Transform ignore = null,
                                 GameObject hitEffect = null, float effectLifetime = 0f)
        {
            if (target == null) return false;
            if (ignore != null && (target.transform == ignore || target.transform.IsChildOf(ignore))) return false;

            IDamageable victim = Find(target);
            if (victim == null || !victim.IsAlive) return false;

            victim.ApplyDamage(info);

            if (hitEffect != null)
            {
                GameObject fx = MagicEffects.Spawn(hitEffect, info.point, LookRotation(info.normal));
                MagicEffects.DestroyLater(fx, effectLifetime);
            }

            return true;
        }

        /// <summary>
        /// Hits everything hurtable inside a sphere (each victim once) and appends the world
        /// position of every victim to <paramref name="hitPoints"/> when a list is given, so the
        /// caller can spray its impact effect around.
        /// </summary>
        /// <returns>How many victims were damaged.</returns>
        public static int ApplyRadial(Vector3 center, float radius, DamageInfo info, Transform ignore,
                                      LayerMask mask, List<Vector3> hitPoints = null)
        {
            if (radius <= 0f) return 0;

            int count = Physics.OverlapSphereNonAlloc(
                center, radius, Overlaps, mask, QueryTriggerInteraction.Ignore);

            Treated.Clear();

            int hits = 0;
            for (int i = 0; i < count; i++)
            {
                Collider col = Overlaps[i];
                if (col == null) continue;
                if (ignore != null && (col.transform == ignore || col.transform.IsChildOf(ignore))) continue;

                IDamageable victim = Find(col);
                if (victim == null || !victim.IsAlive) continue;

                bool seen = false;
                for (int t = 0; t < Treated.Count; t++)
                    if (ReferenceEquals(Treated[t], victim)) { seen = true; break; }
                if (seen) continue;

                Treated.Add(victim);

                // Aim the hit from the centre of the blast, so knockback points outwards.
                var component = victim as Component;
                Vector3 point = component != null ? component.transform.position : col.bounds.center;
                victim.ApplyDamage(info.Retarget(point));

                hits++;
                if (hitPoints != null) hitPoints.Add(point);
            }

            return hits;
        }

        /// <summary>Rotation that makes an effect's +Z look along the given surface normal.</summary>
        public static Quaternion LookRotation(Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.0001f) return Quaternion.identity;
            return Quaternion.LookRotation(normal.normalized, Vector3.up);
        }

        /// <summary>The ground under a point, or the point itself when there is no ground there.</summary>
        public static Vector3 SnapToGround(Vector3 point, LayerMask mask, float searchUp = 4f, float searchDown = 40f)
        {
            Vector3 origin = point + Vector3.up * Mathf.Max(0f, searchUp);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                searchUp + searchDown, mask, QueryTriggerInteraction.Ignore))
                return hit.point;

            return point;
        }
    }

    /// <summary>
    /// Everything a cast needs to know about the world around it: how hard it hits
    /// (<see cref="power"/>), whether it was released at maximum charge, and which debris it may
    /// throw. It is handed to projectiles and blasts, so every spell reacts to the environment
    /// (grass, trees, rocks, roadside clutter) in exactly the same way.
    /// </summary>
    public struct MagicCastSpec
    {
        /// <summary>Damage of the hit.</summary>
        public float damage;
        /// <summary>Spell power: ~0.2 for a tap, 1 for a mid level, 2 for a fully overcharged top level.</summary>
        public float power;
        /// <summary>Released after the overcharge window was full - this is what shatters big rocks.</summary>
        public bool maxCharge;
        /// <summary>Multiplier on every radius the spell applies to the world.</summary>
        public float environmentScale;
        /// <summary>Power needed to shatter rocks and heavy props (1.5 = only the top levels, overcharged).</summary>
        public float rockBreakPower;
        /// <summary>Prefabs the debris is built from (empty = procedural pebbles).</summary>
        public GameObject[] debris;
        public int debrisPerHit;
        public int debrisPerExplosion;
        /// <summary>The slime that cast it (used for the camera shake).</summary>
        public SlimePlayer caster;
        /// <summary>Travel direction of the spell.</summary>
        public Vector3 direction;

        /// <summary>How much of the spell reaches the world around the impact.</summary>
        public float EnvironmentPower => Mathf.Max(0.05f, power);

        /// <summary>True when this cast is strong enough to break rocks and heavy props.</summary>
        public bool BreaksTheWorld => maxCharge || power >= Mathf.Max(0.5f, rockBreakPower);

        /// <summary>Shakes the caster's camera (no-op when there is no caster).</summary>
        public void Shake(float amount, float duration)
        {
            if (caster != null && amount > 0.001f) caster.ShakeCamera(amount, duration);
        }

        /// <summary>Throws debris out of a point, scaled by the spell's power.</summary>
        public int ThrowDebris(Vector3 point, int count, float force = 1f)
        {
            if (count <= 0) return 0;
            return MagicDebris.Spray(debris, point, direction, count, power * Mathf.Max(0.1f, force));
        }
    }
}
