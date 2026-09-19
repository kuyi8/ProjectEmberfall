using UnityEngine;

namespace Emberfall.Application.Flow
{
    public enum RouteHighlightState
    {
        Locked = 0,
        Ready = 1,
        Completed = 2
    }

    /// <summary>
    /// Presentation-only pulse for important route interactables. Quest availability remains owned by
    /// M2RouteInteractable; this component only animates already-authored renderers and light intensity.
    /// </summary>
    public sealed class M2QuestHighlightPresenter : MonoBehaviour
    {
        [SerializeField] private Renderer _core;
        [SerializeField] private Renderer _ring;
        [SerializeField] private Light _light;
        [SerializeField, Min(0f)] private float _bobHeight = 0.12f;
        [SerializeField, Min(0.1f)] private float _pulseSpeed = 2.4f;

        private Vector3 _coreBasePosition;
        private Vector3 _coreBaseScale;
        private Vector3 _ringBaseScale;
        private MaterialPropertyBlock _propertyBlock;
        private RouteHighlightState _state = RouteHighlightState.Ready;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public RouteHighlightState State => _state;
        public bool RingVisible => _ring != null && _ring.enabled;
        public bool LightVisible => _light != null && _light.enabled;
        public bool UsesPropertyBlocks =>
            (_core == null || _core.HasPropertyBlock()) && (_ring == null || _ring.HasPropertyBlock());

        public void Configure(Renderer core, Renderer ring, Light highlightLight)
        {
            _core = core;
            _ring = ring;
            _light = highlightLight;
            CaptureBasePose();
            ApplyState(true);
        }

        public void SetState(RouteHighlightState state)
        {
            if (_state == state) return;
            _state = state;
            ApplyState(true);
        }

        private void Awake()
        {
            CaptureBasePose();
            ApplyState(true);
        }

        private void Update()
        {
            float wave = _state == RouteHighlightState.Ready
                ? 0.5f + (0.5f * Mathf.Sin(Time.unscaledTime * _pulseSpeed))
                : 0f;
            if (_core != null)
            {
                _core.transform.localPosition = _coreBasePosition +
                    (Vector3.up * (_state == RouteHighlightState.Ready ? _bobHeight * wave : 0f));
                _core.transform.localScale = _state == RouteHighlightState.Ready
                    ? _coreBaseScale * Mathf.Lerp(0.92f, 1.12f, wave)
                    : _coreBaseScale;
            }

            if (_ring != null)
            {
                _ring.transform.localScale = _state == RouteHighlightState.Ready
                    ? _ringBaseScale * Mathf.Lerp(0.94f, 1.08f, wave)
                    : _ringBaseScale;
            }

            if (_light != null)
            {
                _light.intensity = _state == RouteHighlightState.Ready
                    ? Mathf.Lerp(1.1f, 2.2f, wave)
                    : 0f;
            }
        }

        private void ApplyState(bool force)
        {
            if (!force) return;
            Color color = _state switch
            {
                RouteHighlightState.Ready => new Color(1f, 0.48f, 0.08f, 1f),
                RouteHighlightState.Completed => new Color(0.12f, 0.38f, 0.34f, 1f),
                _ => new Color(0.28f, 0.31f, 0.34f, 1f)
            };
            Color emission = _state == RouteHighlightState.Ready ? color * 2.2f : color * 0.35f;
            ApplyColor(_core, color, emission);
            ApplyColor(_ring, color, emission);

            if (_core != null) _core.enabled = true;
            if (_ring != null) _ring.enabled = _state != RouteHighlightState.Completed;
            if (_light != null)
            {
                _light.enabled = _state == RouteHighlightState.Ready;
                _light.color = color;
            }
        }

        private void ApplyColor(Renderer renderer, Color color, Color emission)
        {
            if (renderer == null) return;
            _propertyBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            _propertyBlock.SetColor(EmissionColorId, emission);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        private void CaptureBasePose()
        {
            if (_core != null)
            {
                _coreBasePosition = _core.transform.localPosition;
                _coreBaseScale = _core.transform.localScale;
            }

            if (_ring != null)
            {
                _ringBaseScale = _ring.transform.localScale;
            }
        }
    }
}
