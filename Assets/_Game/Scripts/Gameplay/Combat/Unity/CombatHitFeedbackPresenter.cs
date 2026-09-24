using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Movement;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    [DisallowMultipleComponent, DefaultExecutionOrder(150)]
    public sealed class CombatHitFeedbackPresenter : MonoBehaviour
    {
        [SerializeField] private PlayerCombatActor _actor;
        [SerializeField] private Animator _animator;
        [SerializeField] private CombatImpactAudioSet _audioSet;
        [SerializeField] private CombatCameraImpulse _cameraImpulse;
        private readonly HitFeedbackBatch _batch = new HitFeedbackBatch();
        private readonly Animator[] _targets = new Animator[32];
        private int _targetCount;
        private AudioSource _audio;
        private bool _subscribed;
        private bool _owner = true;
        private string _sourceId;
        private double _nextAudioAt;
        private HitFeedbackGrade _lastAudioGrade;
        private System.Random _audioRandom;
        private readonly int[] _lastAudioIndices = { -1, -1, -1, -1, -1, -1 };
        public int PresentedCount { get; private set; }
        public int LastAudioFrame { get; private set; } = -1;
        public int LastFeedbackFrame { get; private set; } = -1;

        public void Configure(PlayerCombatActor actor, Animator animator, CombatImpactAudioSet audioSet,
            CombatCameraImpulse cameraImpulse)
        {
            Unsubscribe();
            _actor = actor; _animator = animator; _audioSet = audioSet; _cameraImpulse = cameraImpulse;
            if (Application.isPlaying && _audioSet != null) _audioSet.Preload();
            Subscribe();
        }

        public void SetOwner(bool owner) => _owner = owner;
        public void SetNetworkOwner(bool owner, ulong sourceId)
        {
            _owner = owner;
            _sourceId = "network:" + sourceId;
        }

        private void Awake()
        {
            _sourceId = "offline:" + GetInstanceID();
            _audioRandom = new System.Random(GetInstanceID()); // Never consume Unity's gameplay random stream.
            if (_audioSet != null) _audioSet.Preload();
            // Dedicated voice, not the existing perfect-defense source; one hit voice per attacker.
            var voice = new GameObject("HitFeedbackVoice");
            voice.transform.SetParent(transform, false);
            _audio = voice.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 0.65f;
            _audio.minDistance = 2f; _audio.maxDistance = 24f;
        }

        private void OnEnable() => Subscribe();
        private void Subscribe()
        {
            if (_subscribed || _actor == null || !isActiveAndEnabled) return;
            _actor.ImpactPresented += Enqueue; _subscribed = true;
        }
        private void Unsubscribe()
        {
            if (_subscribed && _actor != null) _actor.ImpactPresented -= Enqueue;
            _subscribed = false;
        }

        public void Enqueue(CombatImpactPresentationEvent impact)
        {
            if (!isActiveAndEnabled || !_batch.Offer(impact)) return;
            if (impact.TargetAnimator != null && _targetCount < _targets.Length)
                _targets[_targetCount++] = impact.TargetAnimator;
        }

        // Actors/projectiles/network adapters settle before order 150; dispatch before Animator evaluation.
        private void Update()
        {
            if (_actor != null && _actor.Model != null && _actor.Model.IsDead)
            {
                _batch.DiscardPending(); ClearTargets();
                AnimatorSpeedCoordinator.For(_animator)?.Cancel("source-dead");
                return;
            }
            if (!_batch.Take(out CombatImpactPresentationEvent impact)) return;
            bool frozen = AnimatorSpeedCoordinator.For(_animator)?.Request(impact.Grade, impact.Sequence) == true;
            for (int i = 0; i < _targetCount; i++)
                AnimatorSpeedCoordinator.For(_targets[i])?.Request(impact.Grade, impact.Sequence);
            ClearTargets();
            if (_owner && _cameraImpulse != null) _cameraImpulse.Request(impact.Grade);
            LastFeedbackFrame = Time.frameCount;
            LastAudioFrame = -1;
            if (_audioSet != null && (Time.realtimeSinceStartupAsDouble >= _nextAudioAt || impact.Grade > _lastAudioGrade))
            {
                int slot = CombatImpactAudioSet.SlotIndex(impact.Grade, impact.Surface);
                var playback = _audioSet.Select(impact.Grade, impact.Surface, _lastAudioIndices[slot],
                    (float)_audioRandom.NextDouble(), (float)_audioRandom.NextDouble());
                if (playback.Clip != null)
                {
                    _audio.transform.position = impact.Position;
                    _audio.Stop(); _audio.clip = playback.Clip; _audio.volume = 0.6f * playback.Gain;
                    _audio.pitch = playback.Pitch;
                    _audio.Play(); LastAudioFrame = Time.frameCount;
                    _lastAudioIndices[slot] = playback.Index;
                    _nextAudioAt = Time.realtimeSinceStartupAsDouble + 0.05d;
                    _lastAudioGrade = impact.Grade;
                }
            }
            PresentedCount++;
            Debug.Log($"[M5C_FEEL] event=hit-feedback grade={impact.Grade} attack={impact.Attack} surface={impact.Surface} source={_sourceId} " +
                $"sequence={impact.Sequence} attackSequence={impact.AttackSequence} target={impact.TargetId} " +
                $"feedbackFrame={LastFeedbackFrame} audioFrame={LastAudioFrame} freezeStarted={frozen} ownerCamera={_owner}");
        }

        private void ClearTargets()
        {
            for (int i = 0; i < _targetCount; i++) _targets[i] = null;
            _targetCount = 0;
        }

        private void OnDisable()
        {
            Unsubscribe(); _batch.DiscardPending(); ClearTargets();
            if (_animator != null) _animator.GetComponent<AnimatorSpeedCoordinator>()?.Cancel("presenter-disabled");
            if (_audio != null) _audio.Stop();
        }
    }
}
