using Emberfall.AI.Domain;
using UnityEngine;

namespace Emberfall.AI.Unity
{
    /// <summary>
    /// Read-only particle presentation for Warden state transitions and committed rune cleaves.
    /// The actor and brain remain the sole owners of state, timing, movement and damage.
    /// </summary>
    [DefaultExecutionOrder(130)]
    public sealed class WardenCombatVfxPresenter : MonoBehaviour
    {
        [SerializeField] private WardenActor _actor;
        [SerializeField] private GameObject _phaseTransitionPrefab;
        [SerializeField] private GameObject _runeCleavePrefab;

        private GameObject _phaseInstance;
        private GameObject _runeInstance;
        private ParticleSystem[] _phaseParticles;
        private ParticleSystem[] _runeParticles;
        private bool _phaseActive;
        private int _presentedRuneSequence = -1;

        public bool IsConfigured => _actor != null &&
            _phaseTransitionPrefab != null && _runeCleavePrefab != null;
        public bool IsPhaseEffectActive => _phaseInstance != null && _phaseInstance.activeSelf;
        public int PhaseParticleCount => _phaseParticles?.Length ?? 0;
        public int RuneParticleCount => _runeParticles?.Length ?? 0;

        public void Configure(
            WardenActor actor,
            GameObject phaseTransitionPrefab,
            GameObject runeCleavePrefab)
        {
            _actor = actor;
            _phaseTransitionPrefab = phaseTransitionPrefab;
            _runeCleavePrefab = runeCleavePrefab;
        }

        private void Awake()
        {
            if (!IsConfigured)
            {
                Debug.LogError("WardenCombatVfxPresenter is not configured.", this);
                enabled = false;
                return;
            }

            _phaseInstance = CreateInstance(
                _phaseTransitionPrefab,
                "WardenPhaseTransitionVfx_Runtime",
                new Vector3(0f, 0.045f, 0f));
            _runeInstance = CreateInstance(
                _runeCleavePrefab,
                "WardenRuneCleaveVfx_Runtime",
                new Vector3(0f, 0.12f, 0.42f));
            _phaseParticles = _phaseInstance.GetComponentsInChildren<ParticleSystem>(true);
            _runeParticles = _runeInstance.GetComponentsInChildren<ParticleSystem>(true);
            _phaseInstance.SetActive(false);
            _runeInstance.SetActive(false);
        }

        private void Update()
        {
            if (_actor.Brain == null) return;

            bool phaseActive = _actor.State == WardenState.PhaseTransition;
            if (phaseActive != _phaseActive)
            {
                _phaseActive = phaseActive;
                if (phaseActive) Play(_phaseInstance, _phaseParticles);
                else Stop(_phaseInstance, _phaseParticles);
            }

            if (_actor.State == WardenState.Attack &&
                _actor.Brain.CurrentAttack == WardenAttackKind.RuneCleave &&
                _actor.Brain.AttackSequence != _presentedRuneSequence)
            {
                _presentedRuneSequence = _actor.Brain.AttackSequence;
                Play(_runeInstance, _runeParticles);
            }

            if (_runeInstance.activeSelf && !AnyAlive(_runeParticles))
                _runeInstance.SetActive(false);
        }

        private GameObject CreateInstance(GameObject prefab, string objectName, Vector3 localPosition)
        {
            GameObject instance = Instantiate(prefab, transform);
            instance.name = objectName;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            return instance;
        }

        private static void Play(GameObject instance, ParticleSystem[] particles)
        {
            instance.SetActive(true);
            foreach (ParticleSystem particle in particles)
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(true);
            }
        }

        private static void Stop(GameObject instance, ParticleSystem[] particles)
        {
            foreach (ParticleSystem particle in particles)
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            instance.SetActive(false);
        }

        private static bool AnyAlive(ParticleSystem[] particles)
        {
            foreach (ParticleSystem particle in particles)
            {
                if (particle.IsAlive(true)) return true;
            }
            return false;
        }
    }
}
