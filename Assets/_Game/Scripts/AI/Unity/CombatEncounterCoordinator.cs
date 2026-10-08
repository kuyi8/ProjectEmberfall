using System;
using System.Collections.Generic;
using Emberfall.AI.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Diagnostics;
using UnityEngine;
using UnityEngine.AI;

namespace Emberfall.AI.Unity
{
    [DefaultExecutionOrder(-130)]
    public sealed class CombatEncounterCoordinator : MonoBehaviour, IEncounterTelemetrySource
    {
        [SerializeField] private PlayerCombatActor _player;
        [SerializeField] private MeleeEnemyActor[] _meleeEnemies = System.Array.Empty<MeleeEnemyActor>();
        [SerializeField] private ShieldEnemyActor[] _shieldEnemies = System.Array.Empty<ShieldEnemyActor>();
        [SerializeField] private RangedEnemyActor[] _rangedEnemies = System.Array.Empty<RangedEnemyActor>();
        [SerializeField] private SummonerEnemyActor[] _summoners = System.Array.Empty<SummonerEnemyActor>();
        [SerializeField] private bool _endAttemptOnExit;
        [SerializeField, Min(1)] private int _maximumConcurrentMeleeAttackers = 1;
        [SerializeField] private bool _resetMembersOnPlayerDeath;
        [SerializeField] private Vector3 _arenaCenter;
        [SerializeField] private Vector2 _arenaHalfExtents = new Vector2(8f, 8f);
        [SerializeField, Min(0f)] private float _telemetryActivationMargin = 0.75f;
        [SerializeField, Min(3.2f)] private float _supportRadius = 4.15f;
        [SerializeField, Min(0.2f)] private float _supportArrivalDistance = 0.65f;
        [SerializeField, Min(2f)] private float _supportReanchorDistance = 4.5f;
        [SerializeField] private string _telemetrySegment;

        private CombatAttackQuotaModel _quota;
        private readonly Dictionary<int, SupportAnchor> _supportAnchors = new Dictionary<int, SupportAnchor>();
        private bool _telemetryEncounterStarted;
        private bool _telemetryEncounterCleared;
        private bool _combatAlerted;
        private NavMeshPath _supportPath;
        private readonly Vector3[] _supportCorners = new Vector3[64];
        private readonly RaycastHit[] _supportSightHits = new RaycastHit[16];
        private static readonly float[] SupportAngles = { 0f, 30f, -30f, 60f, -60f, 80f, -80f };

        // An observed intruder alerts only this authored encounter. This is not LOS,
        // an attack grant, or a global target registry; actors still raycast independently.
        public bool HasCombatAlertFor(PlayerCombatActor player) =>
            isActiveAndEnabled && _combatAlerted && player == _player &&
            player != null && player.IsAvailable && IsPlayerPositionInsideTelemetryArena();

        public int MaximumConcurrentMeleeAttackers => _maximumConcurrentMeleeAttackers;
        public int MeleeMemberCount => (_meleeEnemies?.Length ?? 0) + (_shieldEnemies?.Length ?? 0);
        public int RangedMemberCount => _rangedEnemies?.Length ?? 0;
        public int SummonerMemberCount => _summoners?.Length ?? 0;
        public bool EndsAttemptOnExit => _endAttemptOnExit;
        public bool HasRunFirstUpdate { get; private set; }
        public Vector2 FirstUpdateArenaHalfExtents { get; private set; }
        public int GrantedAttackCount => _quota?.GrantedCount ?? 0;
        public float SupportRadius => _supportRadius;
        public float SupportArrivalDistance => _supportArrivalDistance;
        public Vector3 ArenaCenter => _arenaCenter;
        public Vector2 ArenaHalfExtents => _arenaHalfExtents;
        public float TelemetryActivationMargin => _telemetryActivationMargin;
        public bool IsTelemetryActive => _telemetryEncounterStarted && !_telemetryEncounterCleared;
        public string TelemetrySegment => !string.IsNullOrWhiteSpace(_telemetrySegment)
            ? _telemetrySegment
            : name.IndexOf("Courtyard", StringComparison.OrdinalIgnoreCase) >= 0
            ? "courtyard-encounter"
            : name.IndexOf("Forest", StringComparison.OrdinalIgnoreCase) >= 0
                ? "forest-encounter"
                : "combat-encounter";

        public event Action<CombatEncounterCoordinator> EncounterStarted;
        public event Action<CombatEncounterCoordinator> EncounterCleared;
        public event Action<CombatEncounterCoordinator> EncounterReset;
        public int ActiveCommittedCount
        {
            get
            {
                int count = 0;
                foreach (MeleeEnemyActor enemy in _meleeEnemies)
                    if (enemy != null && enemy.IsAttackSlotCommitted) count++;
                foreach (ShieldEnemyActor enemy in _shieldEnemies)
                    if (enemy != null && enemy.IsAttackSlotCommitted) count++;
                return count;
            }
        }

        public void Configure(
            PlayerCombatActor player,
            MeleeEnemyActor[] meleeEnemies,
            ShieldEnemyActor[] shieldEnemies,
            RangedEnemyActor[] rangedEnemies,
            int maximumConcurrentMeleeAttackers,
            bool resetMembersOnPlayerDeath,
            Vector3 arenaCenter,
            Vector2 arenaHalfExtents,
            float supportRadius = 4.15f,
            string telemetrySegment = null,
            float telemetryActivationMargin = 0.75f)
        {
            _player = player;
            _meleeEnemies = meleeEnemies ?? System.Array.Empty<MeleeEnemyActor>();
            _shieldEnemies = shieldEnemies ?? System.Array.Empty<ShieldEnemyActor>();
            _rangedEnemies = rangedEnemies ?? System.Array.Empty<RangedEnemyActor>();
            _maximumConcurrentMeleeAttackers = maximumConcurrentMeleeAttackers;
            _resetMembersOnPlayerDeath = resetMembersOnPlayerDeath;
            _arenaCenter = arenaCenter;
            _arenaHalfExtents = arenaHalfExtents;
            _supportRadius = Mathf.Max(3.2f, supportRadius);
            _telemetrySegment = telemetrySegment;
            _telemetryActivationMargin = Mathf.Max(0f, telemetryActivationMargin);
        }

        public void ConfigureTelemetryArena(
            Vector3 center,
            Vector2 halfExtents,
            float activationMargin)
        {
            if (halfExtents.x <= 0f || halfExtents.y <= 0f)
                throw new ArgumentOutOfRangeException(nameof(halfExtents));
            if (activationMargin < 0f)
                throw new ArgumentOutOfRangeException(nameof(activationMargin));
            _arenaCenter = center;
            _arenaHalfExtents = halfExtents;
            _telemetryActivationMargin = activationMargin;
        }

        public void ConfigureSummoners(SummonerEnemyActor[] summoners, bool endAttemptOnExit)
        {
            _summoners = summoners ?? System.Array.Empty<SummonerEnemyActor>();
            _endAttemptOnExit = endAttemptOnExit;
        }

        /// <summary>Restore a completed authored encounter without dealing damage or granting loot.</summary>
        public void RestoreClearedMembers()
        {
            _combatAlerted = false;
            foreach (MeleeEnemyActor enemy in _meleeEnemies) if (enemy != null) enemy.gameObject.SetActive(false);
            foreach (ShieldEnemyActor enemy in _shieldEnemies) if (enemy != null) enemy.gameObject.SetActive(false);
            foreach (RangedEnemyActor enemy in _rangedEnemies) if (enemy != null) enemy.gameObject.SetActive(false);
            foreach (SummonerEnemyActor enemy in _summoners) if (enemy != null) enemy.gameObject.SetActive(false);
            _telemetryEncounterStarted = false;
            _telemetryEncounterCleared = true;
        }

        public void SetMaximumConcurrentMeleeAttackers(int maximumConcurrentMeleeAttackers)
        {
            if (maximumConcurrentMeleeAttackers <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrentMeleeAttackers));

            _maximumConcurrentMeleeAttackers = maximumConcurrentMeleeAttackers;
            _quota = new CombatAttackQuotaModel(_maximumConcurrentMeleeAttackers);
            foreach (MeleeEnemyActor enemy in _meleeEnemies)
                if (enemy != null) _quota.Register(enemy.CombatantId);
            foreach (ShieldEnemyActor enemy in _shieldEnemies)
                if (enemy != null) _quota.Register(enemy.CombatantId);
        }

        private void Awake()
        {
            _supportPath = new NavMeshPath();
            _quota = new CombatAttackQuotaModel(_maximumConcurrentMeleeAttackers);
            foreach (MeleeEnemyActor enemy in _meleeEnemies)
                if (enemy != null) _quota.Register(enemy.CombatantId);
            foreach (ShieldEnemyActor enemy in _shieldEnemies)
                if (enemy != null) _quota.Register(enemy.CombatantId);
        }

        private void OnEnable()
        {
            if (_player != null) _player.Died += OnPlayerDied;
        }

        private void OnDisable()
        {
            _combatAlerted = false;
            if (_player != null) _player.Died -= OnPlayerDied;
            foreach (MeleeEnemyActor enemy in _meleeEnemies)
                enemy?.SetGroupDirective(true, false, Vector3.zero, 0, _supportArrivalDistance);
            foreach (ShieldEnemyActor enemy in _shieldEnemies)
                enemy?.SetGroupDirective(true, false, Vector3.zero, 0, _supportArrivalDistance);
        }

        private void Update()
        {
            if (!HasRunFirstUpdate) { FirstUpdateArenaHalfExtents = _arenaHalfExtents; HasRunFirstUpdate = true; }
            if (_quota == null || _player == null) return;
            bool playerAvailable = _player.IsAvailable;
            if (!playerAvailable || !IsPlayerPositionInsideTelemetryArena()) _combatAlerted = false;
            else if (!_combatAlerted)
            {
                foreach (MeleeEnemyActor enemy in _meleeEnemies)
                    if (enemy != null && enemy.HasDirectVisualContactWith(_player)) _combatAlerted = true;
                foreach (ShieldEnemyActor enemy in _shieldEnemies)
                    if (enemy != null && enemy.HasDirectVisualContactWith(_player)) _combatAlerted = true;
                foreach (RangedEnemyActor enemy in _rangedEnemies)
                    if (enemy != null && enemy.HasDirectVisualContactWith(_player)) _combatAlerted = true;
            }
            foreach (MeleeEnemyActor enemy in _meleeEnemies)
            {
                if (enemy == null) continue;
                _quota.UpdateMember(
                    enemy.CombatantId,
                    playerAvailable && enemy.isActiveAndEnabled && enemy.IsAvailable && enemy.IsAttackSlotEligible,
                    playerAvailable && enemy.isActiveAndEnabled && enemy.IsAvailable && enemy.IsAttackSlotCommitted);
            }
            foreach (ShieldEnemyActor enemy in _shieldEnemies)
            {
                if (enemy == null) continue;
                _quota.UpdateMember(
                    enemy.CombatantId,
                    playerAvailable && enemy.isActiveAndEnabled && enemy.IsAvailable && enemy.IsAttackSlotEligible,
                    playerAvailable && enemy.isActiveAndEnabled && enemy.IsAvailable && enemy.IsAttackSlotCommitted);
            }

            _quota.Resolve();
            foreach (MeleeEnemyActor enemy in _meleeEnemies)
            {
                if (enemy == null) continue;
                ApplyDirective(enemy, enemy.CombatantId);
            }
            foreach (ShieldEnemyActor enemy in _shieldEnemies)
            {
                if (enemy == null) continue;
                ApplyDirective(enemy, enemy.CombatantId);
            }

            UpdatePacingTelemetry();
        }

        public void ResetEncounter()
        {
            _combatAlerted = false;
            ResetPacingTelemetry();
            _quota?.Reset();
            _supportAnchors.Clear();
            foreach (MeleeEnemyActor enemy in _meleeEnemies) enemy?.ResetToSpawn();
            foreach (ShieldEnemyActor enemy in _shieldEnemies) enemy?.ResetToSpawn();
            foreach (RangedEnemyActor enemy in _rangedEnemies) enemy?.ResetToSpawn();
            foreach (SummonerEnemyActor enemy in _summoners) enemy?.ResetToSpawn();
        }

        private void ApplyDirective(MeleeEnemyActor enemy, int combatantId)
        {
            bool allowed = _quota.IsAttackAllowed(combatantId);
            int side = _quota.GetSupportSide(combatantId);
            if (allowed) _supportAnchors.Remove(combatantId);
            enemy.SetGroupDirective(
                allowed,
                !allowed,
                allowed ? Vector3.zero : ResolveStableSupportPosition(enemy, combatantId, side),
                allowed ? 0 : side,
                _supportArrivalDistance);
        }

        private void ApplyDirective(ShieldEnemyActor enemy, int combatantId)
        {
            bool allowed = _quota.IsAttackAllowed(combatantId);
            int side = _quota.GetSupportSide(combatantId);
            if (allowed) _supportAnchors.Remove(combatantId);
            enemy.SetGroupDirective(
                allowed,
                !allowed,
                allowed ? Vector3.zero : ResolveStableSupportPosition(enemy, combatantId, side),
                allowed ? 0 : side,
                _supportArrivalDistance);
        }

        private Vector3 ResolveStableSupportPosition(CombatTarget member, int combatantId, int side)
        {
            if (_supportAnchors.TryGetValue(combatantId, out SupportAnchor anchor))
            {
                // A stable side point may become occluded after the player moves less
                // than the reanchor threshold. Validate at a bounded rate, not per frame.
                if (anchor.Validated && Time.time >= anchor.NextPlanTime)
                {
                    bool visible = EnemyLineOfSight.HasContactFrom(
                        anchor.Destination + member.AimPoint.position - member.transform.position,
                        _player, _supportSightHits);
                    anchor = new SupportAnchor(anchor.PlayerPosition,
                        visible ? anchor.Destination : member.transform.position, visible,
                        visible ? Time.time + .25f : Time.time);
                    _supportAnchors[combatantId] = anchor;
                }
                float playerDrift = Vector3.ProjectOnPlane(
                    _player.transform.position - anchor.PlayerPosition,
                    Vector3.up).magnitude;
                if (anchor.Validated && (ActiveCommittedCount > 0 || playerDrift <= _supportReanchorDistance) ||
                    !anchor.Validated && Time.time < anchor.NextPlanTime)
                {
                    return anchor.Destination;
                }
            }

            bool valid = TryResolveSupportPosition(member, side, out Vector3 destination);
            _supportAnchors[combatantId] = new SupportAnchor(_player.transform.position, destination, valid, Time.time + .25f);
            return destination;
        }

        private bool TryResolveSupportPosition(CombatTarget member, int side, out Vector3 destination)
        {
            Vector3 right = Vector3.ProjectOnPlane(_player.transform.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(_player.transform.forward, Vector3.up).normalized;
            var agent = member.GetComponent<NavMeshAgent>();
            var leash = member.GetComponent<EncounterLeash>();
            destination = member.transform.position;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh || _supportPath == null) return false;
            foreach (float angle in SupportAngles)
            {
                Vector3 desired = _player.transform.position +
                    Quaternion.Euler(0f, angle, 0f) * (right * side * _supportRadius) - forward * .35f;
                desired.x = Mathf.Clamp(desired.x, _arenaCenter.x - _arenaHalfExtents.x, _arenaCenter.x + _arenaHalfExtents.x);
                desired.z = Mathf.Clamp(desired.z, _arenaCenter.z - _arenaHalfExtents.y, _arenaCenter.z + _arenaHalfExtents.y);
                if (leash != null) desired = leash.ClampDestination(desired);
                if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, 2.2f, agent.areaMask) ||
                    leash != null && !leash.Contains(hit.position, agent.radius) ||
                    !agent.CalculatePath(hit.position, _supportPath) || _supportPath.status != NavMeshPathStatus.PathComplete)
                    continue;
                int count = _supportPath.GetCornersNonAlloc(_supportCorners);
                bool inside = count > 0 && count < _supportCorners.Length;
                if (leash != null) for (int i = 0; i < count; i++) inside &= leash.Contains(_supportCorners[i]);
                if (!inside || !EnemyLineOfSight.HasContactFrom(hit.position + member.AimPoint.position - member.transform.position,
                    _player, _supportSightHits)) continue;
                destination = hit.position;
                return true;
            }
            // No visible contained side slot: hold/retry, not a cached point behind a closed gate.
            return false;
        }

        public bool TryCaptureEncounterTelemetry(out EncounterTelemetrySnapshot snapshot)
        {
            snapshot = default;
            if (_quota == null || _player == null) return false;

            string attacker = string.Empty;
            string supportLeft = string.Empty;
            string supportRight = string.Empty;
            int available = 0;
            CaptureMembers(_meleeEnemies, ref attacker, ref supportLeft, ref supportRight, ref available);
            CaptureMembers(_shieldEnemies, ref attacker, ref supportLeft, ref supportRight, ref available);
            if (available == 0) return false;

            float distance = Vector3.ProjectOnPlane(_player.transform.position - _arenaCenter, Vector3.up).magnitude;
            float activeRadius = Mathf.Max(_arenaHalfExtents.x, _arenaHalfExtents.y) + 4f;
            float relevance = distance <= activeRadius ? 1000f - distance : -distance;
            snapshot = new EncounterTelemetrySnapshot(
                name.IndexOf("Courtyard", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "庭院" : "林地",
                attacker,
                supportLeft,
                supportRight,
                relevance);
            return true;
        }

        private void CaptureMembers<T>(
            T[] members,
            ref string attacker,
            ref string supportLeft,
            ref string supportRight,
            ref int available) where T : CombatTarget
        {
            if (members == null) return;
            for (int i = 0; i < members.Length; i++)
            {
                T member = members[i];
                if (member == null || !member.isActiveAndEnabled || !member.IsAvailable) continue;
                available++;
                int id = member.CombatantId;
                string label = ShortCombatantName(member.name);
                if (_quota.IsAttackAllowed(id))
                {
                    attacker = label;
                }
                else if (_quota.GetSupportSide(id) < 0)
                {
                    supportLeft = label;
                }
                else
                {
                    supportRight = label;
                }
            }
        }

        private static string ShortCombatantName(string objectName)
        {
            if (objectName.IndexOf("RuinGuard", System.StringComparison.OrdinalIgnoreCase) >= 0) return "RuinGuard";
            if (objectName.IndexOf("Fogwalker", System.StringComparison.OrdinalIgnoreCase) >= 0) return "Fogwalker";
            return objectName;
        }

        private void OnPlayerDied(PlayerCombatActor _)
        {
            _combatAlerted = false;
            // Every coordinator observes the same player death. Settle a same-frame clear first.
            // An unfinished encounter is abandoned without respawning its members once the player
            // has left its authored arena; only the arena containing the death may reset members.
            UpdatePacingTelemetry();
            if (!IsTelemetryActive) return;

            if (!IsPlayerPositionInsideTelemetryArena())
            {
                ResetPacingTelemetry();
                _quota?.Reset();
                _supportAnchors.Clear();
                return;
            }

            if (_resetMembersOnPlayerDeath) ResetEncounter();
            else
            {
                ResetPacingTelemetry();
                _quota?.Reset();
            }
        }

        private void ResetPacingTelemetry()
        {
            if (_telemetryEncounterStarted && !_telemetryEncounterCleared)
            {
                EncounterReset?.Invoke(this);
            }

            _telemetryEncounterStarted = false;
            _telemetryEncounterCleared = false;
        }

        private void UpdatePacingTelemetry()
        {
            int available = CountAvailableMembers();
            // New corridor encounter only: leaving abandons the attempt, not its health/deaths.
            // Settle a last-frame clear before exit; old arenas retain their default behaviour.
            if (_endAttemptOnExit && IsTelemetryActive && !AllAuthoredMembersDefeated() && !IsPlayerPositionInsideTelemetryArena())
                ResetPacingTelemetry();
            if (!_telemetryEncounterStarted && !_telemetryEncounterCleared && available > 0 && IsPlayerInsideTelemetryArena())
            {
                _telemetryEncounterStarted = true;
                EncounterStarted?.Invoke(this);
            }

            if (_telemetryEncounterStarted && !_telemetryEncounterCleared && available == 0 &&
                (!_endAttemptOnExit || AllAuthoredMembersDefeated()))
            {
                _telemetryEncounterCleared = true;
                EncounterCleared?.Invoke(this);
            }
        }

        private int CountAvailableMembers()
        {
            // Availability/lifecycle and AI execution are different facts. A temporarily
            // disabled behaviour is not defeated; an inactive future wave is not in play.
            // Attack-slot eligibility above still requires isActiveAndEnabled.
            int count = 0;
            foreach (MeleeEnemyActor enemy in _meleeEnemies)
                if (enemy != null && enemy.gameObject.activeInHierarchy && enemy.IsAvailable) count++;
            foreach (ShieldEnemyActor enemy in _shieldEnemies)
                if (enemy != null && enemy.gameObject.activeInHierarchy && enemy.IsAvailable) count++;
            foreach (RangedEnemyActor enemy in _rangedEnemies)
                if (enemy != null && enemy.gameObject.activeInHierarchy && enemy.IsAvailable) count++;
            foreach (SummonerEnemyActor enemy in _summoners)
                if (enemy != null && enemy.gameObject.activeInHierarchy && enemy.IsAvailable) count++;
            return count;
        }

        private bool AllAuthoredMembersDefeated()
        {
            foreach (MeleeEnemyActor enemy in _meleeEnemies) if (enemy == null || enemy.Brain == null || !enemy.Brain.Health.IsDead) return false;
            foreach (ShieldEnemyActor enemy in _shieldEnemies) if (enemy == null || enemy.Brain == null || !enemy.Brain.Health.IsDead) return false;
            foreach (RangedEnemyActor enemy in _rangedEnemies) if (enemy == null || enemy.Brain == null || !enemy.Brain.Health.IsDead) return false;
            foreach (SummonerEnemyActor enemy in _summoners) if (enemy == null || enemy.Brain == null || !enemy.Brain.Health.IsDead) return false;
            return MeleeMemberCount + RangedMemberCount + SummonerMemberCount > 0;
        }

        private bool IsPlayerInsideTelemetryArena()
        {
            if (_player == null || !_player.IsAvailable)
            {
                return false;
            }

            return IsPlayerPositionInsideTelemetryArena();
        }

        private bool IsPlayerPositionInsideTelemetryArena()
        {
            if (_player == null) return false;
            Vector3 offset = Vector3.ProjectOnPlane(_player.transform.position - _arenaCenter, Vector3.up);
            float margin = _telemetryActivationMargin;
            return Mathf.Abs(offset.x) <= _arenaHalfExtents.x + margin &&
                   Mathf.Abs(offset.z) <= _arenaHalfExtents.y + margin;
        }

        private readonly struct SupportAnchor
        {
            public SupportAnchor(Vector3 playerPosition, Vector3 destination, bool validated, float nextPlanTime)
            {
                PlayerPosition = playerPosition;
                Destination = destination;
                Validated = validated;
                NextPlanTime = nextPlanTime;
            }

            public Vector3 PlayerPosition { get; }
            public Vector3 Destination { get; }
            public bool Validated { get; }
            public float NextPlanTime { get; }
        }
    }
}
