using Emberfall.AI.Domain;
using UnityEngine;

namespace Emberfall.AI.Unity
{
    /// <summary>
    /// Lightweight project-owned audio feedback for the Alpha boss. Generated clips are presentation-only;
    /// phase and attack timing remain owned by <see cref="WardenBrain"/>.
    /// </summary>
    public sealed class WardenAudioPresenter : MonoBehaviour
    {
        private const int SampleRate = 22050;
        [SerializeField] private WardenActor _actor;
        [SerializeField] private AudioSource _source;
        private AudioClip _phaseBreak;
        private AudioClip _runeCleave;
        private AudioClip _blastCast;
        private AudioClip _blastImpact;
        private WardenState _previousState = (WardenState)(-1);
        private int _previousAttackSequence = -1;

        public bool IsConfigured => _actor != null && _source != null;

        public void Configure(WardenActor actor, AudioSource source)
        {
            _actor = actor;
            _source = source;
            ConfigureSource();
        }

        private void Awake()
        {
            ConfigureSource();
            _phaseBreak = CreateClip("Warden_PhaseBreak", 1.35f, SamplePhaseBreak);
            _runeCleave = CreateClip("Warden_RuneCleave", 0.42f, SampleWhoosh);
            _blastCast = CreateClip("Warden_BlastCast", 0.48f, SampleCast);
            _blastImpact = CreateClip("Warden_BlastImpact", 0.5f, SampleImpact);
        }

        private void OnEnable()
        {
            if (_actor != null) _actor.DelayedBlastResolved += PlayBlastImpact;
        }

        private void OnDisable()
        {
            if (_actor != null) _actor.DelayedBlastResolved -= PlayBlastImpact;
        }

        private void Update()
        {
            if (!IsConfigured || _actor.Brain == null) return;
            if (_actor.State != _previousState)
            {
                if (_actor.State == WardenState.PhaseTransition) Play(_phaseBreak, 0.92f);
                _previousState = _actor.State;
            }

            if (_actor.State != WardenState.Attack ||
                _actor.Brain.AttackSequence == _previousAttackSequence) return;
            _previousAttackSequence = _actor.Brain.AttackSequence;
            if (_actor.Brain.CurrentAttack == WardenAttackKind.RuneCleave) Play(_runeCleave, 0.78f);
            else if (_actor.Brain.CurrentAttack == WardenAttackKind.DelayedBlast) Play(_blastCast, 0.72f);
        }

        private void ConfigureSource()
        {
            if (_source == null) return;
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 1f;
            _source.minDistance = 2.5f;
            _source.maxDistance = 24f;
            _source.rolloffMode = AudioRolloffMode.Linear;
        }

        private void PlayBlastImpact() => Play(_blastImpact, 0.9f);

        private void Play(AudioClip clip, float volume)
        {
            if (_source != null && clip != null) _source.PlayOneShot(clip, volume);
        }

        private static AudioClip CreateClip(string name, float duration, System.Func<float, uint, float> sampler)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            var samples = new float[count];
            uint noise = 0xA53C9E21u;
            for (int i = 0; i < count; i++)
            {
                noise = (noise * 1664525u) + 1013904223u;
                samples[i] = Mathf.Clamp(sampler(i / (float)SampleRate, noise), -1f, 1f);
            }
            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float Noise(uint value) => ((value >> 8) / 8388607.5f) - 1f;

        private static float SamplePhaseBreak(float time, uint noise)
        {
            float normalized = Mathf.Clamp01(time / 1.35f);
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, normalized * 2.2f));
            float decay = 1f - Mathf.SmoothStep(0.58f, 1f, normalized);
            float frequency = Mathf.Lerp(82f, 238f, normalized);
            return (Mathf.Sin(time * frequency * Mathf.PI * 2f) * 0.2f + Noise(noise) * 0.055f) * rise * decay;
        }

        private static float SampleWhoosh(float time, uint noise)
        {
            float normalized = Mathf.Clamp01(time / 0.42f);
            float envelope = Mathf.Sin(normalized * Mathf.PI);
            return (Noise(noise) * 0.22f + Mathf.Sin(time * 165f * Mathf.PI * 2f) * 0.06f) * envelope;
        }

        private static float SampleCast(float time, uint noise)
        {
            float normalized = Mathf.Clamp01(time / 0.48f);
            float envelope = Mathf.Sin(normalized * Mathf.PI);
            return (Mathf.Sin(time * 260f * Mathf.PI * 2f) * 0.13f +
                    Mathf.Sin(time * 390f * Mathf.PI * 2f) * 0.08f + Noise(noise) * 0.025f) * envelope;
        }

        private static float SampleImpact(float time, uint noise)
        {
            float normalized = Mathf.Clamp01(time / 0.5f);
            float decay = (1f - normalized) * (1f - normalized);
            return (Mathf.Sin(time * 72f * Mathf.PI * 2f) * 0.28f + Noise(noise) * 0.16f) * decay;
        }

        private void OnDestroy()
        {
            if (_phaseBreak != null) Destroy(_phaseBreak);
            if (_runeCleave != null) Destroy(_runeCleave);
            if (_blastCast != null) Destroy(_blastCast);
            if (_blastImpact != null) Destroy(_blastImpact);
        }
    }
}
