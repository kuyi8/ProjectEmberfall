using System.Collections.Generic;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Diagnostics;
using UnityEngine;

namespace Emberfall.Gameplay.Interaction
{
    public sealed class TacticalPostureShrine : InteractableBehaviour, ITacticalTelemetrySource
    {
        private const int HitBufferSize = 32;
        private static readonly ContentId PromptId = new ContentId("text:interaction.arm-posture-shrine");

        [SerializeField] private PlayerCombatActor _player;
        [SerializeField] private Renderer _coreRenderer;
        [SerializeField] private Renderer[] _warningRenderers = System.Array.Empty<Renderer>();
        [SerializeField] private Light _warningLight;
        [SerializeField, Min(0.1f)] private float _telegraphDuration = 1.1f;
        [SerializeField, Min(1f)] private float _radius = 4.6f;
        [SerializeField, Min(0f)] private float _postureDamage = 52f;

        private readonly Collider[] _hitBuffer = new Collider[HitBufferSize];
        private readonly HashSet<int> _resolvedTargets = new HashSet<int>();
        private TacticalPostureBurstModel _model;
        private Material _coreMaterial;
        private Material _warningMaterial;
        private float _spentVisualRemaining;

        public override ContentId PromptTextId => PromptId;
        public override bool IsAvailable => _model?.IsReady == true && _player != null && _player.IsAvailable;
        public bool IsTelegraphing => _model?.IsTelegraphing == true;
        public bool IsSpent => _model?.IsSpent == true;
        public float Radius => _radius;
        public int WarningSegmentCount => _warningRenderers?.Length ?? 0;
        public int LastAffectedCount { get; private set; }

        public void Configure(
            PlayerCombatActor player,
            Renderer coreRenderer,
            Renderer[] warningRenderers,
            Light warningLight,
            float telegraphDuration,
            float radius,
            float postureDamage)
        {
            _player = player;
            _coreRenderer = coreRenderer;
            _warningRenderers = warningRenderers ?? System.Array.Empty<Renderer>();
            _warningLight = warningLight;
            _telegraphDuration = telegraphDuration;
            _radius = radius;
            _postureDamage = postureDamage;
        }

        private void Awake()
        {
            _model = new TacticalPostureBurstModel(_telegraphDuration);
            _coreMaterial = _coreRenderer != null ? _coreRenderer.material : null;
            if (_warningRenderers.Length > 0 && _warningRenderers[0] != null)
            {
                _warningMaterial = new Material(_warningRenderers[0].sharedMaterial);
                for (int i = 0; i < _warningRenderers.Length; i++)
                    if (_warningRenderers[i] != null) _warningRenderers[i].sharedMaterial = _warningMaterial;
            }
            ApplyReadyPresentation();
        }

        private void OnEnable()
        {
            if (_player != null) _player.Died += OnPlayerDied;
        }

        private void OnDisable()
        {
            if (_player != null) _player.Died -= OnPlayerDied;
        }

        private void Update()
        {
            if (_model == null) return;
            if (_model.IsTelegraphing)
            {
                float pulse = 0.96f + Mathf.Sin(Time.time * 18f) * 0.035f;
                UpdateWarningSegments(Mathf.Lerp(0.28f, 1f, _model.Normalized) * pulse);
                if (_warningMaterial != null)
                {
                    _warningMaterial.color = Color.Lerp(
                        new Color(0.04f, 0.78f, 0.96f),
                        new Color(0.68f, 0.24f, 1f),
                        _model.Normalized);
                }
                if (_warningLight != null)
                {
                    _warningLight.intensity = Mathf.Lerp(1.2f, 5f, _model.Normalized);
                }
            }

            if (_model.Tick(Time.deltaTime))
            {
                ResolveBurst();
                _spentVisualRemaining = 0.28f;
            }
            if (_model.IsSpent && _spentVisualRemaining > 0f)
            {
                _spentVisualRemaining -= Time.deltaTime;
                if (_spentVisualRemaining <= 0f) SetWarningVisible(false);
            }
        }

        public override bool TryInteract(InteractionContext context)
        {
            if (context.Actor == null || context.Actor != _player || !IsAvailable || !_model.TryArm())
            {
                return false;
            }
            SetWarningVisible(true);
            UpdateWarningSegments(0.28f);
            if (_coreMaterial != null) _coreMaterial.color = new Color(0.2f, 0.86f, 1f);
            return true;
        }

        public void ResetForEncounter()
        {
            _model?.Reset();
            _resolvedTargets.Clear();
            LastAffectedCount = 0;
            _spentVisualRemaining = 0f;
            ApplyReadyPresentation();
        }

        private void ResolveBurst()
        {
            _resolvedTargets.Clear();
            LastAffectedCount = 0;
            int count = Physics.OverlapSphereNonAlloc(
                transform.position,
                _radius,
                _hitBuffer,
                ~0,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                CombatTarget target = _hitBuffer[i].GetComponentInParent<CombatTarget>();
                if (target == null || !target.IsAvailable || !_resolvedTargets.Add(target.CombatantId)) continue;
                if (target.ApplyNeutralPostureDamage(_postureDamage) > 0f) LastAffectedCount++;
            }
            if (_coreMaterial != null) _coreMaterial.color = new Color(0.16f, 0.18f, 0.24f);
            if (_warningLight != null) _warningLight.intensity = 6f;
        }

        private void ApplyReadyPresentation()
        {
            SetWarningVisible(false);
            if (_coreMaterial != null) _coreMaterial.color = new Color(0.08f, 0.7f, 0.88f);
            if (_warningLight != null)
            {
                _warningLight.color = new Color(0.12f, 0.78f, 1f);
                _warningLight.intensity = 1.1f;
            }
        }

        private void SetWarningVisible(bool visible)
        {
            for (int i = 0; i < _warningRenderers.Length; i++)
                if (_warningRenderers[i] != null) _warningRenderers[i].gameObject.SetActive(visible);
            if (_warningLight != null) _warningLight.enabled = visible || _model?.IsReady == true;
        }

        private void UpdateWarningSegments(float normalizedRadius)
        {
            int count = _warningRenderers.Length;
            if (count == 0) return;
            float distance = _radius * Mathf.Clamp01(normalizedRadius);
            float rotationOffset = Time.time * 22f;
            for (int i = 0; i < count; i++)
            {
                Renderer segment = _warningRenderers[i];
                if (segment == null) continue;
                float degrees = rotationOffset + (360f * i / count);
                float radians = degrees * Mathf.Deg2Rad;
                segment.transform.localPosition = new Vector3(
                    Mathf.Cos(radians) * distance,
                    0.075f,
                    Mathf.Sin(radians) * distance);
                segment.transform.localRotation = Quaternion.Euler(0f, -degrees, 0f);
                segment.transform.localScale = new Vector3(0.72f, 0.035f, 0.16f);
            }
        }

        public bool TryCaptureTacticalTelemetry(out TacticalTelemetrySnapshot snapshot)
        {
            if (_model == null)
            {
                snapshot = default;
                return false;
            }
            TacticalTelemetryState state = _model.IsTelegraphing
                ? TacticalTelemetryState.Telegraph
                : _model.IsSpent ? TacticalTelemetryState.Spent : TacticalTelemetryState.Ready;
            snapshot = new TacticalTelemetrySnapshot("中立架势符文", state);
            return true;
        }

        private void OnPlayerDied(PlayerCombatActor _) => ResetForEncounter();

        private void OnDestroy()
        {
            if (_coreMaterial != null) Destroy(_coreMaterial);
            if (_warningMaterial != null) Destroy(_warningMaterial);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.7f, 0.2f, 1f, 0.65f);
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
