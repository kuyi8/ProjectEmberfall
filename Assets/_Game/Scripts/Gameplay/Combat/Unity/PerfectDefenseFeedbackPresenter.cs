using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class PerfectDefenseFeedbackPresenter : MonoBehaviour
    {
        [SerializeField] private PlayerCombatActor _actor;
        [SerializeField] private Animator _animator;
        [SerializeField, Min(0.01f)] private float _freezeSeconds = 0.045f;

        private AudioSource _source;
        private AudioClip _clip;
        private float _freezeRemaining;
        private float _restoreSpeed = 1f;

        public void Configure(PlayerCombatActor actor, Animator animator)
        {
            _actor = actor;
            _animator = animator;
        }

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0.15f;
            _clip = CreateConfirmationClip();
        }

        private void OnEnable()
        {
            if (_actor != null) _actor.PerfectDefensePresented += Present;
        }

        private void OnDisable()
        {
            if (_actor != null) _actor.PerfectDefensePresented -= Present;
            RestoreAnimator();
        }

        private void OnDestroy()
        {
            if (_clip != null) Destroy(_clip);
        }

        private void Update()
        {
            if (_freezeRemaining <= 0f) return;
            _freezeRemaining -= Time.unscaledDeltaTime;
            if (_freezeRemaining <= 0f) RestoreAnimator();
        }

        private void Present(PerfectDefenseKind kind)
        {
            if (_animator != null)
            {
                if (_freezeRemaining <= 0f) _restoreSpeed = Mathf.Max(0.01f, _animator.speed);
                _animator.speed = 0f;
                _freezeRemaining = _freezeSeconds;
            }

            if (_source != null && _clip != null)
            {
                _source.pitch = kind == PerfectDefenseKind.Guard ? 1.15f : 1.45f;
                _source.PlayOneShot(_clip, 0.62f);
            }
        }

        private void RestoreAnimator()
        {
            if (_animator != null && Mathf.Approximately(_animator.speed, 0f))
                _animator.speed = _restoreSpeed;
            _freezeRemaining = 0f;
        }

        private static AudioClip CreateConfirmationClip()
        {
            const int sampleRate = 22050;
            const int sampleCount = 1764;
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float envelope = 1f - (i / (float)sampleCount);
                samples[i] = Mathf.Sin(2f * Mathf.PI * 920f * time) * envelope * 0.3f;
            }

            AudioClip clip = AudioClip.Create("PerfectDefenseConfirm", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
