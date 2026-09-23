using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>
    /// Builds a short-lived ribbon between the authored sword root and tip.
    /// It reads combat state for timing but never owns hit detection or damage.
    /// </summary>
    public sealed class SwordTrailPresenter : MonoBehaviour
    {
        private const int MaxSamples = 14;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private PlayerCombatActor _actor;
        [SerializeField] private Transform _bladeRoot;
        [SerializeField] private Transform _bladeTip;
        [SerializeField] private Material _trailMaterial;
        [SerializeField, Range(0f, 0.45f)] private float _gripInset = 0.18f;
        [SerializeField, Min(0.05f)] private float _trailDuration = 0.16f;
        [SerializeField, Min(0.005f)] private float _minimumSampleDistance = 0.025f;
        [SerializeField, Min(0.005f)] private float _maximumSampleInterval = 0.02f;

        private readonly Vector3[] _sampleRoots = new Vector3[MaxSamples];
        private readonly Vector3[] _sampleTips = new Vector3[MaxSamples];
        private readonly float[] _sampleTimes = new float[MaxSamples];
        private readonly Vector3[] _vertices = new Vector3[MaxSamples * 2];
        private readonly Vector2[] _uvs = new Vector2[MaxSamples * 2];
        private readonly Color[] _colors = new Color[MaxSamples * 2];
        private readonly int[] _triangles = new int[(MaxSamples - 1) * 6];

        private Mesh _mesh;
        private MeshRenderer _meshRenderer;
        private MaterialPropertyBlock _propertyBlock;
        private int _sampleCount;
        private CombatState _lastSwingState = CombatState.Locomotion;

        public void Configure(
            PlayerCombatActor actor,
            Transform bladeRoot,
            Transform bladeTip,
            Material trailMaterial)
        {
            _actor = actor;
            _bladeRoot = bladeRoot;
            _bladeTip = bladeTip;
            _trailMaterial = trailMaterial;
        }

        private void Awake()
        {
            if (_actor == null || _bladeRoot == null || _bladeTip == null || _trailMaterial == null)
            {
                Debug.LogError("SwordTrailPresenter is not configured.", this);
                enabled = false;
                return;
            }

            CreateRuntimeMesh();
        }

        private void LateUpdate()
        {
            if (_actor?.Model == null || _mesh == null) return;

            CombatStateMachine model = _actor.Model;
            bool swingState = IsSwingState(model.State);
            if (swingState && model.State != _lastSwingState)
            {
                ClearSamples();
                _lastSwingState = model.State;
            }
            else if (!swingState)
            {
                _lastSwingState = CombatState.Locomotion;
            }

            float now = Time.time;
            RemoveExpiredSamples(now);
            if (swingState && model.StateNormalized >= 0.12f && model.StateNormalized <= 0.7f)
            {
                AddSample(now);
            }

            RebuildMesh(now, model);
        }

        private void CreateRuntimeMesh()
        {
            var trailObject = new GameObject("SwordTrail_Runtime", typeof(MeshFilter), typeof(MeshRenderer));
            trailObject.layer = gameObject.layer;
            trailObject.transform.SetParent(transform, false);
            _mesh = new Mesh { name = "SwordTrail_RuntimeMesh" };
            _mesh.MarkDynamic();
            trailObject.GetComponent<MeshFilter>().sharedMesh = _mesh;
            _meshRenderer = trailObject.GetComponent<MeshRenderer>();
            _meshRenderer.sharedMaterial = _trailMaterial;
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            _meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _meshRenderer.sortingOrder = 12;
            _meshRenderer.enabled = false;
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void AddSample(float now)
        {
            Vector3 tip = _bladeTip.position;
            Vector3 root = Vector3.Lerp(_bladeRoot.position, tip, _gripInset);
            if (_sampleCount > 0 &&
                (tip - _sampleTips[_sampleCount - 1]).sqrMagnitude <
                _minimumSampleDistance * _minimumSampleDistance &&
                now - _sampleTimes[_sampleCount - 1] < _maximumSampleInterval)
            {
                _sampleRoots[_sampleCount - 1] = root;
                _sampleTips[_sampleCount - 1] = tip;
                return;
            }

            if (_sampleCount == MaxSamples) ShiftSamplesLeft();
            _sampleRoots[_sampleCount] = root;
            _sampleTips[_sampleCount] = tip;
            _sampleTimes[_sampleCount] = now;
            _sampleCount++;
        }

        private void RemoveExpiredSamples(float now)
        {
            while (_sampleCount > 0 && now - _sampleTimes[0] > _trailDuration)
                ShiftSamplesLeft();
        }

        private void ShiftSamplesLeft()
        {
            for (int i = 1; i < _sampleCount; i++)
            {
                _sampleRoots[i - 1] = _sampleRoots[i];
                _sampleTips[i - 1] = _sampleTips[i];
                _sampleTimes[i - 1] = _sampleTimes[i];
            }
            _sampleCount = Mathf.Max(0, _sampleCount - 1);
        }

        private void RebuildMesh(float now, CombatStateMachine model)
        {
            if (_sampleCount < 2)
            {
                _mesh.Clear(false);
                _meshRenderer.enabled = false;
                return;
            }

            for (int i = 0; i < _sampleCount; i++)
            {
                _vertices[i * 2] = transform.InverseTransformPoint(_sampleRoots[i]);
                _vertices[(i * 2) + 1] = transform.InverseTransformPoint(_sampleTips[i]);
                float progress = _sampleCount <= 1 ? 1f : i / (float)(_sampleCount - 1);
                float alpha = Mathf.Lerp(0.04f, 1f, progress * progress);
                _uvs[i * 2] = new Vector2(progress, 0f);
                _uvs[(i * 2) + 1] = new Vector2(progress, 1f);
                _colors[i * 2] = new Color(1f, 1f, 1f, alpha);
                _colors[(i * 2) + 1] = new Color(1f, 1f, 1f, alpha);
                if (i >= _sampleCount - 1) continue;
                int vertex = i * 2;
                int triangle = i * 6;
                _triangles[triangle] = vertex;
                _triangles[triangle + 1] = vertex + 1;
                _triangles[triangle + 2] = vertex + 2;
                _triangles[triangle + 3] = vertex + 1;
                _triangles[triangle + 4] = vertex + 3;
                _triangles[triangle + 5] = vertex + 2;
            }

            _mesh.Clear(false);
            _mesh.SetVertices(_vertices, 0, _sampleCount * 2);
            _mesh.SetUVs(0, _uvs, 0, _sampleCount * 2);
            _mesh.SetColors(_colors, 0, _sampleCount * 2);
            _mesh.SetTriangles(_triangles, 0, (_sampleCount - 1) * 6, 0, true);

            float fade = Mathf.Clamp01(1f - ((now - _sampleTimes[_sampleCount - 1]) / _trailDuration));
            Color color = ResolveColor(model);
            color.a *= fade;
            _propertyBlock.SetColor(BaseColorId, color);
            _meshRenderer.SetPropertyBlock(_propertyBlock);
            _meshRenderer.enabled = true;
        }

        private static bool IsSwingState(CombatState state) =>
            state >= CombatState.LightAttack1 && state <= CombatState.LightAttack3 ||
            state == CombatState.HeavyAttack || state == CombatState.Sweep;

        private Color ResolveColor(CombatStateMachine model)
        {
            if (model.State == CombatState.HeavyAttack && _actor.ActiveRuneBlessing == RuneBlessing.Ember)
                return new Color(1f, 0.24f, 0.035f, 0.72f);
            if (model.State == CombatState.HeavyAttack)
                return new Color(1f, 0.62f, 0.16f, 0.62f);
            if (model.State == CombatState.Sweep)
                return new Color(1f, 0.82f, 0.24f, 0.82f);
            return new Color(0.56f, 0.86f, 1f, 0.52f);
        }

        private void ClearSamples()
        {
            _sampleCount = 0;
            if (_mesh != null) _mesh.Clear(false);
            if (_meshRenderer != null) _meshRenderer.enabled = false;
        }

        private void OnDisable() => ClearSamples();

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
