using System;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>Opt-in style for independent render copies; never projectile authority or a source trail.</summary>
    [CreateAssetMenu(menuName = "Emberfall/Presentation/Knife Trail Style")]
    public sealed class KnifeTrailStyle : ScriptableObject
    {
        [SerializeField] private float _lifetimeSeconds = .10f;
        [SerializeField] private float _widthMultiplierScale = .45f;
        [SerializeField] private float _alphaMultiplier = .5625f;

        public float LifetimeSeconds => _lifetimeSeconds;
        public float WidthMultiplierScale => _widthMultiplierScale;
        public float AlphaMultiplier => _alphaMultiplier;

        public void Validate()
        {
            if (!Finite(_lifetimeSeconds) || _lifetimeSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(LifetimeSeconds));
            if (!Finite(_widthMultiplierScale) || _widthMultiplierScale <= 0)
                throw new ArgumentOutOfRangeException(nameof(WidthMultiplierScale));
            if (!Finite(_alphaMultiplier) || _alphaMultiplier < 0 || _alphaMultiplier > 1)
                throw new ArgumentOutOfRangeException(nameof(AlphaMultiplier));
        }

        public void ApplyToVisual(TrailRenderer copy, TrailRenderer source)
        {
            ValidateForVisual(copy, source);
            var baseline = source.colorGradient;
            var alphaKeys = baseline.alphaKeys;
            for (int i = 0; i < alphaKeys.Length; i++)
                alphaKeys[i].alpha *= _alphaMultiplier;
            var gradient = new Gradient { mode = baseline.mode, colorSpace = baseline.colorSpace };
            gradient.SetKeys(baseline.colorKeys, alphaKeys);
            float width = source.widthMultiplier * _widthMultiplierScale;
            // Prepare/validate everything before the first native property write.
            copy.time = _lifetimeSeconds;
            copy.widthMultiplier = width;
            copy.colorGradient = gradient;
        }

        internal void ValidateForVisual(TrailRenderer copy, TrailRenderer source)
        {
            ValidatePair(copy, source);
            Validate();
            float width = source.widthMultiplier * _widthMultiplierScale;
            if (!Finite(width) || width < 0)
                throw new ArgumentOutOfRangeException(nameof(WidthMultiplierScale),
                    "The source width multiplied by the style must remain finite and nonnegative.");
        }

        internal static void ValidatePair(TrailRenderer copy, TrailRenderer source)
        {
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (copy == source)
                throw new ArgumentException("Only an independent visual copy may be styled.", nameof(copy));
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
