using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>
    /// Converts read-only combat presentation events into short-lived VFX instances.
    /// Damage, posture, hit registration and timing remain owned by PlayerCombatActor.
    /// </summary>
    public sealed class CombatImpactVfxPresenter : MonoBehaviour
    {
        [SerializeField] private PlayerCombatActor _actor;
        [SerializeField] private GameObject _steelImpactPrefab;
        [SerializeField] private GameObject _guardImpactPrefab;
        [SerializeField] private GameObject _emberImpactPrefab;
        [SerializeField] private GameObject _guardBreakPrefab;
        [SerializeField] private GameObject _executionPrefab;
        [SerializeField] private GameObject _sweepPrefab;
        [SerializeField, Min(0.1f)] private float _gradeLifetime = .7f;
        [SerializeField] private float _gradeVerticalOffset = -.55f;
        [SerializeField, Min(0.1f)] private float _lifetime = 1.8f;

        private bool _subscribed;
        private ulong _lastSequence;
        private int _lastSweepAttack = int.MinValue;
        public int PresentedCount { get; private set; }
        public int DroppedCount { get; private set; }
        public float GradeLifetimeSeconds => _gradeLifetime;

        public void ConfigureGradeEffects(GameObject guardBreak, GameObject execution, GameObject sweep)
        {
            _guardBreakPrefab = guardBreak;
            _executionPrefab = execution;
            _sweepPrefab = sweep;
            if (Application.isPlaying && isActiveAndEnabled) Prewarm();
        }

        public void Configure(
            PlayerCombatActor actor,
            GameObject steelImpactPrefab,
            GameObject guardImpactPrefab,
            GameObject emberImpactPrefab)
        {
            Unsubscribe();
            _actor = actor;
            _steelImpactPrefab = steelImpactPrefab;
            _guardImpactPrefab = guardImpactPrefab;
            _emberImpactPrefab = emberImpactPrefab;
            Subscribe();
            if (Application.isPlaying && isActiveAndEnabled) Prewarm();
        }

        private void OnEnable()
        {
            Subscribe();
            if (Application.isPlaying) Prewarm();
        }

        private void Prewarm()
        {
            if (_steelImpactPrefab == null && _guardImpactPrefab == null && _emberImpactPrefab == null &&
                _guardBreakPrefab == null && _executionPrefab == null && _sweepPrefab == null) return;
            // Scene-load OnEnable precedes isLoaded; Start runs after loading completes.
            if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return;
            var pool = CombatBurstVfxPool.ForScene(gameObject.scene);
            pool.Prewarm(CombatBurstKind.Steel, _steelImpactPrefab);
            pool.Prewarm(CombatBurstKind.Guard, _guardImpactPrefab);
            pool.Prewarm(CombatBurstKind.Ember, _emberImpactPrefab);
            pool.Prewarm(CombatBurstKind.GuardBreak, _guardBreakPrefab);
            pool.Prewarm(CombatBurstKind.Execution, _executionPrefab);
            pool.Prewarm(CombatBurstKind.Sweep, _sweepPrefab);
        }

        private void Start() => Prewarm();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed || _actor == null || !isActiveAndEnabled) return;
            _actor.ImpactPresented += Present;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_actor != null) _actor.ImpactPresented -= Present;
            _subscribed = false;
        }

        public void Present(CombatImpactPresentationEvent impact)
        {
            if (!isActiveAndEnabled || impact.Sequence == 0 || impact.Sequence <= _lastSequence || impact.Grade == HitFeedbackGrade.None) return;
            _lastSequence = impact.Sequence;
            // A Sweep that breaks every target still has one range arc, alongside per-target break accents.
            // This does not change the grade used by audio, freeze or damage.
            if (impact.Attack == Domain.AttackTag.Sweep && impact.Grade != HitFeedbackGrade.Sweep &&
                impact.Sector.IsValid && _sweepPrefab != null && _lastSweepAttack != impact.AttackSequence)
            {
                _lastSweepAttack = impact.AttackSequence;
                bool arcSpawned = CombatBurstVfxPool.ForScene(gameObject.scene).TrySpawn(CombatBurstKind.Sweep,
                    _sweepPrefab, impact.Sector.Origin + Vector3.down * .8f,
                    Quaternion.LookRotation(impact.Sector.Forward, Vector3.up), _gradeLifetime, impact.Sector);
                if (arcSpawned) PresentedCount++; else DroppedCount++;
                Debug.Log($"[M6_VFX] event=confirmed-impact sequence={impact.Sequence} kind=Sweep spawned={arcSpawned} active={CombatBurstVfxPool.ActiveCount(CombatBurstKind.Sweep)} grade={impact.Grade} attackSequence={impact.AttackSequence}");
            }
            // A Sweep that breaks every target still has one range arc, alongside per-target break accents.
            // This does not change the grade used by audio, freeze or damage.
            if (impact.Attack == Domain.AttackTag.Sweep && impact.Grade != HitFeedbackGrade.Sweep &&
                impact.Sector.IsValid && _sweepPrefab != null && _lastSweepAttack != impact.AttackSequence)
            {
                _lastSweepAttack = impact.AttackSequence;
                bool arcSpawned = CombatBurstVfxPool.ForScene(gameObject.scene).TrySpawn(CombatBurstKind.Sweep,
                    _sweepPrefab, impact.Sector.Origin + Vector3.down * .8f,
                    Quaternion.LookRotation(impact.Sector.Forward, Vector3.up), _gradeLifetime, impact.Sector);
                if (arcSpawned) PresentedCount++; else DroppedCount++;
            }
            GameObject prefab = impact.Style switch
            {
                CombatImpactStyle.Guard => _guardImpactPrefab,
                CombatImpactStyle.Ember => _emberImpactPrefab,
                _ => _steelImpactPrefab
            };
            var kind = impact.Style switch
            {
                CombatImpactStyle.Guard => CombatBurstKind.Guard,
                CombatImpactStyle.Ember => CombatBurstKind.Ember,
                _ => CombatBurstKind.Steel
            };
            float lifetime = _lifetime;
            GameObject gradePrefab = impact.Grade switch
            {
                HitFeedbackGrade.GuardBreak => _guardBreakPrefab,
                HitFeedbackGrade.Execution => _executionPrefab,
                HitFeedbackGrade.Sweep => _sweepPrefab,
                _ => null
            };
            if (gradePrefab != null)
            {
                prefab = gradePrefab;
                lifetime = _gradeLifetime;
                kind = impact.Grade switch
                {
                    HitFeedbackGrade.GuardBreak => CombatBurstKind.GuardBreak,
                    HitFeedbackGrade.Execution => CombatBurstKind.Execution,
                    _ => CombatBurstKind.Sweep
                };
                // One confirmed arc per attack, not one full arc per struck target.
                // Damage, per-target audio and guard-break accents still process independently.
                if (kind == CombatBurstKind.Sweep)
                {
                    if (_lastSweepAttack == impact.AttackSequence) return;
                    _lastSweepAttack = impact.AttackSequence;
                }
            }
            if (prefab == null) return;
            var rotation = gradePrefab != null ? Quaternion.Euler(0, transform.eulerAngles.y, 0) : Quaternion.identity;
            // AimPoint is near the head; wide accents read at the upper torso instead of as a head halo.
            Vector3 position = impact.Position + (gradePrefab != null ? Vector3.up * _gradeVerticalOffset : Vector3.zero);
            if (kind == CombatBurstKind.GuardBreak)
            {
                // AimPoint is inside the body: place the local burst on the attacker-facing surface,
                // not through the victim (and never disable depth testing to force visibility).
                Vector3 source = impact.Sector.IsValid ? impact.Sector.Origin : transform.position;
                position += Vector3.ProjectOnPlane(source - impact.Position, Vector3.up).normalized * .34f;
            }
            if (kind == CombatBurstKind.Sweep && impact.Sector.IsValid)
            {
                // Query origin is the capsule centre. Vertical offset is decorative; XZ/radius/edges stay exact.
                position = impact.Sector.Origin + Vector3.down * .8f;
                rotation = Quaternion.LookRotation(impact.Sector.Forward, Vector3.up);
            }
            bool spawned = CombatBurstVfxPool.ForScene(gameObject.scene).TrySpawn(kind, prefab, position, rotation, lifetime, impact.Sector);
            if (spawned) PresentedCount++; else DroppedCount++;
            Debug.Log($"[M6_VFX] event=confirmed-impact sequence={impact.Sequence} kind={kind} spawned={spawned} active={CombatBurstVfxPool.ActiveCount(kind)} grade={impact.Grade} attackSequence={impact.AttackSequence}");
        }
    }
}
