using Emberfall.AI.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberfall.AI.Unity
{
    /// <summary>Read-only speed streaks for the committed Warden charge.</summary>
    [DefaultExecutionOrder(120)]
    public sealed class WardenChargeTrailPresenter : MonoBehaviour
    {
        [SerializeField] private WardenActor _actor;
        [SerializeField] private Material _material;

        private TrailRenderer[] _trails;
        private bool _wasEmitting;

        public bool IsConfigured => _actor != null && _material != null;
        public int TrailCount => _trails?.Length ?? 0;

        public void Configure(WardenActor actor, Material material)
        {
            _actor = actor;
            _material = material;
        }

        private void Awake()
        {
            if (!IsConfigured)
            {
                Debug.LogError("WardenChargeTrailPresenter is not configured.", this);
                enabled = false;
                return;
            }

            _trails = new[]
            {
                CreateTrail("ChargeTrail_Left", new Vector3(-0.34f, 0.16f, -0.24f)),
                CreateTrail("ChargeTrail_Right", new Vector3(0.34f, 0.16f, -0.24f))
            };
        }

        private void LateUpdate()
        {
            bool emitting = _actor.Brain != null &&
                _actor.State == WardenState.Attack &&
                _actor.Brain.CurrentAttack == WardenAttackKind.Charge;
            if (emitting == _wasEmitting) return;

            _wasEmitting = emitting;
            foreach (TrailRenderer trail in _trails)
            {
                trail.emitting = emitting;
                if (emitting) trail.Clear();
            }
        }

        private TrailRenderer CreateTrail(string objectName, Vector3 localPosition)
        {
            var trailObject = new GameObject(objectName, typeof(TrailRenderer));
            trailObject.transform.SetParent(transform, false);
            trailObject.transform.localPosition = localPosition;
            TrailRenderer trail = trailObject.GetComponent<TrailRenderer>();
            trail.time = 0.24f;
            trail.minVertexDistance = 0.04f;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.15f),
                new Keyframe(0.55f, 0.08f),
                new Keyframe(1f, 0f));
            trail.colorGradient = new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(new Color(1f, 0.48f, 0.08f), 0f),
                    new GradientColorKey(new Color(0.9f, 0.035f, 0.01f), 1f)
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0.78f, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            };
            trail.sharedMaterial = _material;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.emitting = false;
            return trail;
        }
    }
}
