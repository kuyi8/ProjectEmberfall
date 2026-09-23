using Emberfall.Quests.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;

namespace Emberfall.Application.Flow
{
    public sealed class M2StageBarrier : MonoBehaviour
    {
        [SerializeField] private M2RouteFlowController _flow;
        [SerializeField] private MainQuestStage _openAtStage = MainQuestStage.ReturnToScout;
        [SerializeField] private GameObject _blocker;

        private bool? _lastOpen;
        private Renderer _face;
        private Renderer[] _facades;
        private Collider[] _colliders;
        private UnityEngine.AI.NavMeshObstacle[] _obstacles;
        private MaterialPropertyBlock _properties;
        private Color _baseColor;
        private float _opacity;
        private AudioSource _audio;
        private AudioClip _tone;
        private bool _initialized;
        public const float TransitionSeconds = 0.5f;
        public float VisualOpacity => _opacity;
        public bool IsTransitioning { get; private set; }
        public string LastFeedbackTextId { get; private set; }
        public float FeedbackUntil { get; private set; }
        public int SoundSequence { get; private set; }
        public static event System.Action<string> FeedbackPresented;

        public bool IsOpen => _flow != null && _flow.IsInitialized && _flow.Stage >= _openAtStage;

        public void Configure(M2RouteFlowController flow, MainQuestStage openAtStage, GameObject blocker)
        {
            _flow = flow;
            _openAtStage = openAtStage;
            _blocker = blocker;
        }

        private void Start()
        {
            Initialize();
            if (_flow != null) Refresh(true);
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            if (_blocker != null)
            {
                _face = _blocker.GetComponent<Renderer>();
                _facades = _blocker.GetComponentsInChildren<Renderer>(true);
                _colliders = _blocker.GetComponentsInChildren<Collider>(true);
                _obstacles = _blocker.GetComponentsInChildren<UnityEngine.AI.NavMeshObstacle>(true);
                _properties = new MaterialPropertyBlock();
                _baseColor = _face != null ? _face.sharedMaterial.color : Color.cyan;
            }
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0.3f;
            _tone = ConfirmationTone.Create("SealDissolve", 540f);
        }

        private void Update()
        {
            if (_flow != null) Refresh(false);
            if (!IsTransitioning) return;
            _opacity = Mathf.MoveTowards(_opacity, _lastOpen == true ? 0f : 1f,
                Time.deltaTime / TransitionSeconds);
            ApplyOpacity();
            if (Mathf.Approximately(_opacity, _lastOpen == true ? 0f : 1f))
            {
                IsTransitioning = false;
                _blocker.SetActive(_lastOpen != true);
            }
        }

        private void ApplyOpacity()
        {
            if (_face == null) return;
            _face.GetPropertyBlock(_properties);
            Color color = _baseColor;
            color.a *= _opacity;
            _properties.SetColor("_BaseColor", color);
            _face.SetPropertyBlock(_properties);
        }

        private void OnDestroy()
        {
            if (_tone != null) Destroy(_tone);
        }

        private void Refresh(bool force)
        {
            SetOpen(IsOpen, force);
        }

        public static M2StageBarrier CreateManual(GameObject owner, GameObject blocker)
        {
            var presenter = owner.AddComponent<M2StageBarrier>();
            presenter._blocker = blocker;
            presenter.Initialize();
            presenter.SetOpen(!blocker.activeSelf, true);
            return presenter;
        }

        public void SetOpen(bool open, bool force = false)
        {
            Initialize();
            if (!force && _lastOpen == open)
            {
                return;
            }

            _lastOpen = open;
            if (_blocker != null)
            {
                // Collision follows the quest fact immediately; only the visible face fades.
                foreach (Collider collider in _colliders) collider.enabled = !open;
                foreach (var obstacle in _obstacles) obstacle.enabled = !open;
                foreach (Renderer facade in _facades)
                    if (facade != _face) facade.gameObject.SetActive(!open);
                if (force)
                {
                    IsTransitioning = false;
                    _opacity = open ? 0f : 1f;
                    _blocker.SetActive(!open);
                    ApplyOpacity();
                    return;
                }
                _blocker.SetActive(true);
                IsTransitioning = true;
                LastFeedbackTextId = open ? "text:barrier.open" : "text:barrier.closed";
                FeedbackUntil = Time.time + 3f;
                _audio.pitch = open ? 1.2f : 0.75f;
                _audio.PlayOneShot(_tone, 0.65f);
                SoundSequence++;
                FeedbackPresented?.Invoke(LastFeedbackTextId);
            }
        }
    }
}
