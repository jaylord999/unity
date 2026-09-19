using UnityEngine;

namespace MysticMap
{
    /// <summary>
    /// Drives one charging-circle layer of a spell.
    ///
    /// The layer is a pivot (this component) with the effect prefab as its child:
    ///   * the pivot revolves around the slime (<see cref="MagicLayer.orbit"/>) from a
    ///     <see cref="MagicLayer.orbitRadius"/> offset, so a new circle can be seen sweeping
    ///     around the one below it,
    ///   * the pivot also leans over (<see cref="MagicLayer.tilt"/>) which is what makes a
    ///     stack of flat circles read as a 3D cage of rings,
    ///   * the effect itself keeps spinning on its own axis (<see cref="MagicLayer.spin"/>),
    ///   * and it blooms in from nothing, breathes, and can be collapsed again in a fraction
    ///     of a second when the magic is released.
    ///
    /// The size is measured, not guessed: the effect is sampled for a moment after it starts
    /// and then scaled so its footprint matches <see cref="MagicLayer.diameter"/>, which keeps
    /// every circle of the pack consistent no matter how big the authoring was.
    /// </summary>
    [DisallowMultipleComponent]
    public class MagicLayerVisual : MonoBehaviour
    {
        const float FitWindow = 0.45f;      // seconds of sampling before the size is locked in
        const float PulseRate = 2.2f;       // breathing speed

        MagicLayer _layer;
        Transform _effect;
        Vector3 _prefabScale = Vector3.one;

        float _age;
        float _pulsePhase;
        float _orbitAngle;
        float _spinAngle;
        float _appliedScale = 1f;
        float _fitScale = 1f;
        float _measured;                    // footprint of the effect at scale 1
        float _diameter = 4.6f;             // the diameter this layer was built with (tuning included)
        float _height;                      // how high above the slime's feet the pivot sits
        float _orbitOffset;                 // how far off-centre the pivot travels, in metres
        float _swell = 1f;                  // live charge-up: how much the circle has grown
        float _spinUp = 1f;                 // live charge-up: how much faster it turns
        float _riderUp = 1f;                // live charge-up: how much faster the riders walk

        Transform[] _riders;                // little elements riding around the rim (the "clock")
        Vector3 _riderPrefabScale = Vector3.one;
        float _riderRadius;
        float _riderFit = 1f;
        float _riderMeasured;
        float _riderApplied = 0.001f;
        float _riderAngle;
        float _riderSpinAngle;

        bool _retiring;
        float _retireFor = 0.2f;
        float _retireAge;

        /// <summary>
        /// Creates a layer under the given rig (usually the "Magic Charge Rig").
        /// <paramref name="circleScale"/> and <paramref name="heightScale"/> are the live magic
        /// tuning (see <see cref="SlimeMagic.circleScale"/>), applied here so what is authored on
        /// the layer itself stays untouched. <paramref name="rimDiameter"/> is the diameter of the
        /// biggest circle that is already out: a layer that is set to orbit but has no offset of
        /// its own travels along that rim, which is the clockwork around the main circle.
        /// </summary>
        public static MagicLayerVisual Add(Transform rig, MagicLayer layer, bool spawnLights,
                                           float pulsePhase, float circleScale = 1f, float heightScale = 1f,
                                           float rimDiameter = 0f)
        {
            if (rig == null || layer == null || layer.effect == null) return null;

            float sizeScale = Mathf.Max(0.05f, circleScale);
            float diameter = Mathf.Max(0.05f, layer.diameter * sizeScale);
            float height = layer.height * Mathf.Max(0.05f, heightScale);

            // A layer that is told to travel, but has no offset of its own, rides the rim of the
            // biggest circle that is already out: that is the clockwork look, where the smaller
            // circles revolve along the edge of the main one. An explicit orbitRadius always wins.
            float orbitRadius = layer.orbitRadius > 0.01f || Mathf.Abs(layer.orbit) <= 0.01f ||
                                rimDiameter <= 0.01f
                ? layer.orbitRadius
                : Mathf.Max(0f, (rimDiameter - layer.diameter) * 0.5f);

            float orbit = orbitRadius * sizeScale;

            var pivot = new GameObject(layer.effect.name);
            pivot.transform.SetParent(rig, false);
            pivot.transform.localPosition = new Vector3(orbit, height, 0f);

            GameObject effect = MagicEffects.Spawn(layer.effect, pivot.transform.position, Quaternion.identity, pivot.transform);
            MagicEffects.Show(effect);
            MagicEffects.ForcePlay(effect, true);                     // circles stay while charging
            if (!spawnLights) MagicEffects.StripLights(effect);
            if (layer.tint != Color.white) MagicEffects.Tint(effect, layer.tint);

            var visual = pivot.AddComponent<MagicLayerVisual>();
            visual._layer = layer;
            visual._effect = effect.transform;
            visual._prefabScale = effect.transform.localScale;
            visual._pulsePhase = pulsePhase;
            visual._orbitAngle = pulsePhase * 40f;                    // stagger the starting angles
            visual._diameter = diameter;
            visual._orbitOffset = orbit;
            visual._height = height;
            visual._riderRadius = diameter * 0.5f * (1f + Mathf.Clamp(layer.riderOffset, 0f, 1f));

            // The effect starts at a hair of its size (the pivot itself stays unit-sized) so the
            // measured-to-scale ratio it samples from the very first frame is always consistent.
            visual._appliedScale = 0.001f;
            effect.transform.localScale = visual._prefabScale * visual._appliedScale;

            visual.CreateRiders(pivot.transform, layer, spawnLights);

            return visual;
        }

        /// <summary>
        /// Puts little copies of the effect on the rim, evenly spaced, so the layer reads as a
        /// clock face. They travel around the rim (a full lap every <see cref="MagicLayer.riderLap"/>
        /// seconds) while the layer as a whole still revolves with its own orbit.
        /// </summary>
        void CreateRiders(Transform pivot, MagicLayer layer, bool spawnLights)
        {
            int count = Mathf.Clamp(layer.rimRiders, 0, 16);
            if (count <= 0) return;

            GameObject prefab = layer.riderEffect != null ? layer.riderEffect : layer.effect;
            if (prefab == null) return;

            _riders = new Transform[count];

            for (int i = 0; i < count; i++)
            {
                GameObject rider = MagicEffects.Spawn(prefab, pivot.position, Quaternion.identity, pivot);
                if (rider == null) continue;

                rider.name = prefab.name + " (rider " + i + ")";
                MagicEffects.Show(rider);
                MagicEffects.ForcePlay(rider, true);                  // riders stay while charging
                if (!spawnLights) MagicEffects.StripLights(rider);
                if (layer.tint != Color.white) MagicEffects.Tint(rider, layer.tint);

                if (i == 0) _riderPrefabScale = rider.transform.localScale;

                rider.transform.localScale = _riderPrefabScale * 0.001f;
                _riders[i] = rider.transform;
            }
        }

        /// <summary>
        /// Feeds the charge into the layer, so the circle reacts to the magic building up:
        /// <paramref name="fill"/> (0..1) is how far the hold is toward the next threshold,
        /// <paramref name="swell"/> how much it may grow and <paramref name="spinUp"/> how much
        /// faster it and its clock riders may turn at the top of the bar.
        /// </summary>
        public void SetCharge(float fill, float swell, float spinUp)
        {
            float t = Mathf.Clamp01(fill);

            _swell = 1f + Mathf.Max(0f, swell) * t;
            _spinUp = 1f + Mathf.Max(0f, spinUp) * t;
            _riderUp = 1f + Mathf.Max(0f, spinUp) * 0.5f * t;
        }

        /// <summary>The layer this visual was built from (used to swap circles / auras out).</summary>
        public MagicLayer Source => _layer;

        /// <summary>Which "one at a time" group the layer belongs to (circle, aura or alongside).</summary>
        public MagicLayerGroup Group => _layer != null ? _layer.group : MagicLayerGroup.Alongside;

        /// <summary>Collapses the layer (it swells a little, then vanishes) and destroys it.</summary>
        public void Retire(float seconds)
        {
            if (_retiring) return;

            _retiring = true;
            _retireFor = Mathf.Max(0.01f, seconds);

            StopEmitting(_effect);

            if (_riders == null) return;
            for (int i = 0; i < _riders.Length; i++) StopEmitting(_riders[i]);
        }

        /// <summary>Stops a group of particles from emitting any more (the ones in flight still fade).</summary>
        static void StopEmitting(Transform root)
        {
            if (root == null) return;

            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void Update()
        {
            if (_effect == null || _layer == null) { Destroy(gameObject); return; }

            float dt = Time.deltaTime;
            _age += dt;

            // ---- size ---------------------------------------------------------
            float bloom = _layer.growIn > 0.01f ? Mathf.Clamp01(_age / _layer.growIn) : 1f;
            bloom = bloom * bloom * (3f - 2f * bloom);                            // smoothstep

            if (_layer.FitsToDiameter && _age <= FitWindow + _layer.growIn && _appliedScale > 0.1f)
            {
                // While the particles spread out the measurement grows; the biggest one wins, so
                // the final size is the steady-state footprint of the effect.
                float now = MagicEffects.Footprint(_effect.gameObject) / _appliedScale;
                if (now > _measured) _measured = now;

                if (_measured > 0.01f)
                    _fitScale = Mathf.Clamp(_diameter / _measured, 0.01f, 60f);

                MeasureRiders();
            }

            float pulse = _layer.pulse > 0f ? 1f + Mathf.Sin((_age + _pulsePhase) * PulseRate) * _layer.pulse : 1f;

            // ---- motion -------------------------------------------------------
            _orbitAngle += _layer.orbit * _spinUp * dt;
            _spinAngle += _layer.spin * _spinUp * dt;

            // The pivot itself travels around the player, so a layer that is set to orbit really
            // revolves along the rim it rides instead of only turning where it stands.
            if (_orbitOffset > 0.001f)
            {
                float rad = _orbitAngle * Mathf.Deg2Rad;
                transform.localPosition = new Vector3(Mathf.Cos(rad) * _orbitOffset, _height,
                                                      Mathf.Sin(rad) * _orbitOffset);
            }

            transform.localRotation = Quaternion.Euler(_layer.tilt, _orbitAngle, 0f);
            _effect.localRotation = Quaternion.Euler(0f, _spinAngle, 0f);

            // ---- scale --------------------------------------------------------
            float scale;
            if (_retiring)
            {
                _retireAge += dt;
                float t = Mathf.Clamp01(_retireAge / _retireFor);
                scale = (1f - t) * (1f + 0.25f * Mathf.Sin(t * Mathf.PI));
                if (t >= 1f) { Destroy(gameObject); return; }
            }
            else
            {
                scale = bloom * pulse * _swell;
            }

            _appliedScale = _fitScale * scale;
            transform.localScale = Vector3.one;                       // the pivot stays unit-sized
            _effect.localScale = _prefabScale * _appliedScale;

            TickRiders(dt, scale);
        }

        /// <summary>Fits the riders to the size the layer asks for (a fraction of the diameter).</summary>
        void MeasureRiders()
        {
            if (_riders == null || _riders.Length == 0 || _riders[0] == null) return;

            float applied = Mathf.Max(0.001f, _riderApplied);
            float now = MagicEffects.Footprint(_riders[0].gameObject) / applied;
            if (now > _riderMeasured) _riderMeasured = now;

            if (_riderMeasured > 0.01f)
                _riderFit = Mathf.Clamp(_diameter * Mathf.Max(0.02f, _layer.riderScale) / _riderMeasured,
                                        0.005f, 60f);
        }

        /// <summary>Walks the riders around the rim, each one spinning on its own axis.</summary>
        void TickRiders(float dt, float scale)
        {
            if (_riders == null || _riders.Length == 0) return;

            float lap = _layer.riderLap > 0.01f ? 360f / _layer.riderLap * _riderUp : 0f;
            _riderAngle = Mathf.Repeat(_riderAngle + lap * dt, 360f);
            _riderSpinAngle = Mathf.Repeat(_riderSpinAngle + (_layer.spin + _layer.riderSpin) * dt, 360f);

            float step = 360f / Mathf.Max(1, _riders.Length);
            float riderScale = _riderFit * scale;
            _riderApplied = Mathf.Max(0.001f, riderScale);

            // The riders stay glued to the rim even while the circle swells up with the charge.
            float radius = _riderRadius * _swell;

            for (int i = 0; i < _riders.Length; i++)
            {
                Transform rider = _riders[i];
                if (rider == null) continue;

                float angle = _riderAngle + i * step;
                float rad = angle * Mathf.Deg2Rad;

                rider.localPosition = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
                rider.localRotation = Quaternion.Euler(0f, _riderSpinAngle + i * step, 0f);
                rider.localScale = _riderPrefabScale * riderScale;
            }
        }
    }
}
