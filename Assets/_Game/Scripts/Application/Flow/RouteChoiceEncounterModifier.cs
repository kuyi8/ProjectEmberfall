using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Quests.Domain;
using UnityEngine;
using UnityEngine.AI;

namespace Emberfall.Application.Flow
{
    /// <summary>Existing route quota modifier plus the optional authored Ash Guard wave variant.</summary>
    [DefaultExecutionOrder(-65)]
    public sealed class RouteChoiceEncounterModifier : MonoBehaviour
    {
        [SerializeField] private M2RouteFlowController _flow;
        [SerializeField] private CombatEncounterCoordinator _encounter;
        [SerializeField, Min(1)] private int _supplyRouteMeleeQuota = 1;
        [SerializeField, Min(1)] private int _riskRouteMeleeQuota = 2;
        [SerializeField] private PlayerCombatActor _player;
        [SerializeField] private ShieldEnemyActor _openingGuard;
        [SerializeField] private SummonerEnemyActor _openingCaller;
        [SerializeField] private MeleeEnemyActor _reinforcement;
        [SerializeField] private GameObject _narrowWalls;
        [SerializeField] private GameObject _arrivalCue;

        private EmberValleyRouteChoice _appliedChoice = (EmberValleyRouteChoice)(-1);
        private AshReinforcementChoice _appliedReinforcement = (AshReinforcementChoice)(-1);
        private Vector3 _reinforcementSpawn;
        private bool _reinforcementDeployed, _choiceClosed;
        private float _arrivalReadyAt = -1f;
        private float _nextActivationProbe;
        private NavMeshAgent _reinforcementAgent;

        public bool IsReinforcementVariant => _reinforcement != null;
        public int SupplyRouteMeleeQuota => _supplyRouteMeleeQuota;
        public int RiskRouteMeleeQuota => _riskRouteMeleeQuota;
        public bool ReinforcementChoiceClosed => _choiceClosed || _encounter != null && _encounter.IsTelemetryActive;
        public bool ReinforcementPending => IsReinforcementVariant && _appliedReinforcement == AshReinforcementChoice.Staged &&
            !_reinforcementDeployed && _openingGuard.Brain != null && _openingGuard.Brain.Health.IsDead && !_flow.AshGuardPassCleared;
        public MeleeEnemyActor Reinforcement => _reinforcement;
        public bool AppliedBeforeEncounterFirstUpdate { get; private set; }
        public int ReinforcementActivationFailures { get; private set; }
        public int ActualAvailableAuthoredCount => Count(_openingGuard) + Count(_openingCaller) + Count(_reinforcement);
        private static int Count(CombatTarget actor) => actor != null && actor.isActiveAndEnabled && actor.IsAvailable ? 1 : 0;

        public void Configure(M2RouteFlowController flow, CombatEncounterCoordinator encounter)
        { _flow = flow; _encounter = encounter; }

        public void ConfigureReinforcement(M2RouteFlowController flow, CombatEncounterCoordinator encounter,
            PlayerCombatActor player, ShieldEnemyActor guard, SummonerEnemyActor caller, MeleeEnemyActor reinforcement,
            GameObject narrowWalls, GameObject arrivalCue)
        {
            Configure(flow, encounter); _player = player; _openingGuard = guard; _openingCaller = caller;
            _reinforcement = reinforcement; _narrowWalls = narrowWalls; _arrivalCue = arrivalCue;
        }

        private void Awake()
        {
            if (!IsReinforcementVariant) return;
            _reinforcementSpawn = _reinforcement.transform.position;
            _reinforcementAgent = _reinforcement.GetComponent<NavMeshAgent>();
            // Flow (-80) initializes persisted facts before this Awake (-65), before any Update.
            if (_flow != null && _flow.IsInitialized) ApplyReinforcementChoice(_flow.ReinforcementChoice);
        }
        private void OnEnable()
        {
            if (!IsReinforcementVariant) return;
            _encounter.EncounterStarted += OnEncounterStarted;
            _player.Died += OnPlayerDied;
        }
        private void OnDisable()
        {
            if (!IsReinforcementVariant) return;
            _encounter.EncounterStarted -= OnEncounterStarted;
            _player.Died -= OnPlayerDied;
            if (_arrivalCue != null) _arrivalCue.SetActive(false);
        }

        public void ApplyReinforcementChoice(AshReinforcementChoice choice)
        {
            if (!IsReinforcementVariant || _flow == null || !_flow.IsInitialized || _appliedReinforcement == choice) return;
            _appliedReinforcement = choice;
            AppliedBeforeEncounterFirstUpdate = !_encounter.HasRunFirstUpdate;
            if (choice == AshReinforcementChoice.None) return; // No change to the verified original B.
            bool staged = choice == AshReinforcementChoice.Staged;
            _narrowWalls.SetActive(staged);
            _encounter.ConfigureTelemetryArena(_encounter.ArenaCenter, new Vector2(staged ? 2.6f : 4.2f, 3.35f), .15f);
            // All three authored members exist in the clear condition BEFORE a death/arrival.
            // An inactive member with a null/live Brain is not defeated; no same-frame false clear.
            _encounter.Configure(_player, new[] { _reinforcement }, new[] { _openingGuard }, null, 1, true,
                _encounter.ArenaCenter, _encounter.ArenaHalfExtents, 3.2f, "ash-guard-pass-encounter", .15f);
            _encounter.ConfigureSummoners(new[] { _openingCaller }, true);
            _encounter.SetMaximumConcurrentMeleeAttackers(1); // Population choice never masquerades as attack quota.
            _reinforcementDeployed = !staged;
            _reinforcement.gameObject.SetActive(!staged && !_flow.AshGuardPassCleared);
            if (_flow.AshGuardPassCleared) _encounter.RestoreClearedMembers();
        }

        private void Update()
        {
            if (_flow == null || _encounter == null || !_flow.IsInitialized) return;
            if (IsReinforcementVariant)
            {
                if (_appliedReinforcement != _flow.ReinforcementChoice) ApplyReinforcementChoice(_flow.ReinforcementChoice);
                UpdatePendingWave();
                return;
            }
            // Preserve the original PreSanctum Supply/Risk quota behaviour.
            if (_appliedChoice == _flow.RouteChoice) return;
            _appliedChoice = _flow.RouteChoice;
            _encounter.SetMaximumConcurrentMeleeAttackers(_appliedChoice == EmberValleyRouteChoice.Risk
                ? _riskRouteMeleeQuota : _supplyRouteMeleeQuota);
        }

        private bool PlayerInsideArena()
        {
            if (_player == null) return false;
            Vector3 offset = _player.transform.position - _encounter.ArenaCenter;
            return Mathf.Abs(offset.x) <= _encounter.ArenaHalfExtents.x + _encounter.TelemetryActivationMargin &&
                Mathf.Abs(offset.z) <= _encounter.ArenaHalfExtents.y + _encounter.TelemetryActivationMargin;
        }
        private void UpdatePendingWave()
        {
            // Query the still-true guard-dead FACT, not a one-shot event consumed by leaving.
            if (!ReinforcementPending || !_player.IsAvailable || !PlayerInsideArena())
            { if (_arrivalCue != null) _arrivalCue.SetActive(false); return; }
            if (_arrivalReadyAt < 0f)
            { _arrivalReadyAt = Time.time + .65f; _flow.NotifyReinforcementArrival(false); }
            _arrivalCue.SetActive(true);
            if (Time.time < _arrivalReadyAt ||
                Vector3.ProjectOnPlane(_player.NavigationFootPosition - _reinforcementSpawn, Vector3.up).sqrMagnitude < 4f) return;
            if (Time.time < _nextActivationProbe) return;
            _nextActivationProbe = Time.time + .25f;
            if (_reinforcementAgent == null || !_reinforcementAgent.enabled ||
                !NavMesh.SamplePosition(_reinforcementSpawn, out NavMeshHit hit, .25f, NavMesh.AllAreas) ||
                (hit.position - _reinforcementSpawn).sqrMagnitude > .0625f)
            { RecordActivationFailure("navigation-unavailable"); return; }
            _reinforcement.gameObject.SetActive(true);
            if (_reinforcement.Brain == null || !_reinforcement.IsAvailable || !_reinforcementAgent.isOnNavMesh)
            { _reinforcement.gameObject.SetActive(false); RecordActivationFailure("actor-registration-unavailable"); return; }
            _reinforcementDeployed = true; _arrivalCue.SetActive(false);
            _flow.NotifyReinforcementArrival(true);
        }

        private void RecordActivationFailure(string reason)
        {
            ReinforcementActivationFailures++;
            if (ReinforcementActivationFailures != 1) return;
            _flow.NotifyReinforcementNavigationWaiting();
            Debug.LogWarning("EMBERFALL_REINFORCEMENT_WAIT reason=" + reason + " failures=1; required member retained, bounded retries active", this);
        }

        private void OnEncounterStarted(CombatEncounterCoordinator _) => _choiceClosed = true;
        private void OnPlayerDied(PlayerCombatActor _)
        {
            if (!PlayerInsideArena() || _flow.AshGuardPassCleared || _appliedReinforcement != AshReinforcementChoice.Staged) return;
            _reinforcementDeployed = false; _arrivalReadyAt = -1f; _nextActivationProbe = 0f;
            _arrivalCue.SetActive(false);
            // Coordinator resets all designated members first; hiding the restored future wave
            // is presentation/scheduling, never a defeat or a different permanent composition.
            _reinforcement.ResetToSpawn(); _reinforcement.gameObject.SetActive(false);
        }
    }
}
