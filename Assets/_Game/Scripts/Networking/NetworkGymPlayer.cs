using System;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Movement;
using Emberfall.Gameplay.Targeting;
using Emberfall.Quests.Domain;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject), typeof(CharacterController))]
    public sealed partial class NetworkGymPlayer : NetworkBehaviour
    {
        private const float SendInterval = 0.05f;
        private const float RescueSendInterval = 0.1f;
        private const float MoveSpeed = 5.5f;
        private const float SprintSpeed = 8.2f;
        private const float TurnSpeed = 900f;
        private const float Gravity = -25f;
        private const float GroundedVerticalSpeed = -2f;
        private const float CombatFacingMinimumDot = -0.25f;
        private static readonly int AnimationSpeedId = Animator.StringToHash("Speed");
        private ulong _confirmedHitSequence;
        private CombatHitFeedbackPresenter _hitFeedback;
        private CombatImpactVfxPresenter _impactVfx;

        internal void ServerPresentHit(ulong targetId, int attackSequence, AttackTag tag,
            DamageResult result, Vector3 position, ImpactSurface surface)
        {
            if (!IsServer) return;
            HitFeedbackGrade grade = HitFeedbackRules.Classify(result, tag);
            if (grade == HitFeedbackGrade.None) return;
            // Host also consumes the RPC once; never invoke the presenter on the Server side here.
            PresentConfirmedHitClientRpc(++_confirmedHitSequence, targetId, attackSequence,
                (byte)grade, (byte)(result.Blocked ? ImpactSurface.Metal : surface), (byte)tag, position, result.Killed);
        }

        [ClientRpc]
        private void PresentConfirmedHitClientRpc(ulong sequence, ulong targetId, int attackSequence,
            byte grade, byte surface, byte tag, Vector3 position, bool killed)
        {
            _hitFeedback ??= GetComponent<CombatHitFeedbackPresenter>();
            if (_hitFeedback == null) return;
            Animator targetAnimator = null;
            if (!killed && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetId, out NetworkObject target))
                targetAnimator = target.GetComponentInChildren<Animator>();
            _hitFeedback.SetNetworkOwner(IsOwner && !_inputSuppressed.Value && !IsDowned, OwnerClientId);
            var style = (HitFeedbackGrade)grade == HitFeedbackGrade.GuardBreak || (ImpactSurface)surface == ImpactSurface.Metal
                ? CombatImpactStyle.Guard : CombatImpactStyle.Steel;
            var impact = new CombatImpactPresentationEvent(position, style,
                sequence, attackSequence, unchecked((int)targetId), (HitFeedbackGrade)grade,
                (ImpactSurface)surface, targetAnimator, (AttackTag)tag);
            _hitFeedback.Enqueue(impact);
            _impactVfx ??= GetComponent<CombatImpactVfxPresenter>();
            if (_impactVfx != null) _impactVfx.Present(impact);
        }

        [SerializeField] private CharacterController _controller;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Renderer[] _teamMarkers;
        [SerializeField] private Renderer _attackIndicator;
        [SerializeField] private Renderer _defenseIndicator;
        [SerializeField] private Renderer _downedIndicator;
        [SerializeField] private CombatTuningAsset _combatTuning;
        [SerializeField] private Animator _animator;
        [SerializeField] private PlayerAnimationSet _animationSet;
        [SerializeField] private GameObject _throwingKnifeVisualPrefab;
        [SerializeField] private ThirdPersonCameraRig _ownerCameraRig;
        [SerializeField] private LockOnTargeting _targeting;

        private readonly NetworkVariable<Vector3> _serverPosition = new NetworkVariable<Vector3>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _serverYaw = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<uint> _acknowledgedSequence = new NetworkVariable<uint>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _ready = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _matchStarted = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _inputSuppressed = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<uint> _sceneTransitionEpoch = new NetworkVariable<uint>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private uint _appliedSceneTransitionEpoch;
        private readonly NetworkVariable<int> _correctionCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _health = new NetworkVariable<float>(
            120f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _stamina = new NetworkVariable<float>(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _posture = new NetworkVariable<float>(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _combatState = new NetworkVariable<int>(
            (int)CombatState.Locomotion,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _attackSequence = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _heavyFullyCharged = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _rangedReleaseSequence = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector3> _rangedDirection = new NetworkVariable<Vector3>(
            Vector3.forward,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<double> _rangedReadyServerTime = new NetworkVariable<double>(
            0d,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _healSequence = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _healingFlaskCharges = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _heavyHitCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _fullyChargedHeavyHitCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _rangedHitCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _healResolvedCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _sharedRewardCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _downed = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _rescueProgress = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _partyDefeated = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _dodgeEvadeCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _perfectGuardCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _perfectDodgeCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _sprinting = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _guardBreakCount = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private NetworkMovementValidator _validator;
        private NetworkCombatIntentValidator _combatIntentValidator;
        private NetworkInteractionIntentValidator _interactionIntentValidator;
        private NetworkInteractionIntentValidator _rescueIntentValidator;
        private CombatStateMachine _combat;
        private NetworkGymWorldObjective _worldObjective;
        private uint _nextSequence;
        private uint _nextCombatIntentSequence;
        private uint _nextInteractionIntentSequence;
        private uint _nextRescueIntentSequence;
        private uint _lastReconciledSequence;
        private int _lastResolvedAttackSequence = -1;
        private int _lastResolvedRangedReleaseSequence;
        private int _presentedRangedReleaseSequence;
        private int _presentedHealSequence;
        private float _nextSendTime;
        private float _nextAutoAttackTime;
        private float _nextAutoInteractionTime;
        private float _nextRescueSendTime;
        private float _spawnTime;
        private bool _autoReady;
        private bool _autoMove;
        private bool _autoCombat;
        private bool _autoWorld;
        private bool _autoEncounterPoint;
        private bool _autoRescue;
        private bool _autoDisconnectDuringRescue;
        private bool _autoDisconnectIssued;
        private bool _autoDefense;
        private bool _autoAttackSuite;
        private bool _autoDisconnectAfterWorldSeal;
        private bool _autoDisconnectBeforeResult;
        private bool _worldDisconnectIssued;
        private bool _lastRescueHeld;
        private int _autoDefenseStage;
        private float _serverDodgeElapsed;
        private float _serverDodgeDuration;
        private float _serverDodgePreviousTravel;
        private Vector3 _serverDodgeDirection;
        private bool _serverDodgeActive;
        private Vector3 _serverPendingRangedDirection = Vector3.forward;
        private CombatState _previousOwnerCombatState = CombatState.Locomotion;
        private CombatState _presentedAnimationState = (CombatState)(-1);
        private CombatState _lastPresentationState = (CombatState)(-1);
        private CombatTuning _presentationTuning;
        private float _replicatedStateEnteredAt;
        private Vector3 _lastPresentationPosition;
        private Vector3 _animatorAnchorPosition;
        private Quaternion _animatorAnchorRotation;
        private bool _hasAnimatorAnchor;
        private int _autoAttackSuiteStage;
        private float _autoAttackSuiteStageAt;
        private float _verticalSpeed;
        private float _serverPoseSuppressedUntil;
        private float _interactionFeedbackExpiresAt;
        private float _serverSprintRequestExpiresAt;
        private float _serverSprintSessionSeconds;
        private bool _serverSprintSessionActive;

        public bool IsReady => _ready.Value;
        public bool MatchStarted => _matchStarted.Value;
        public bool InputSuppressed => _inputSuppressed.Value ||
            (IsOwner && _appliedSceneTransitionEpoch != _sceneTransitionEpoch.Value);
        public bool OwnerCameraUsesGameplayRig => _ownerCameraRig != null;
        public bool OwnerHasNetworkTargeting => _targeting != null;
        public int CorrectionCount => _correctionCount.Value;
        public float Health => _health.Value;
        public float Stamina => _stamina.Value;
        public float Posture => _posture.Value;
        public float MaximumHealth => _presentationTuning?.MaxHealth ?? 120f;
        public float MaximumStamina => _presentationTuning?.MaxStamina ?? 100f;
        public float MaximumPosture => _presentationTuning?.MaxPosture ?? 100f;
        public bool IsDead => (CombatState)_combatState.Value == CombatState.Dead;
        public bool IsDowned => _downed.Value;
        public float RescueProgress => _rescueProgress.Value;
        public bool PartyDefeated => _partyDefeated.Value;
        public CombatState ReplicatedCombatState => (CombatState)_combatState.Value;
        public int SharedRewardCount => _sharedRewardCount.Value;
        public int DodgeEvadeCount => _dodgeEvadeCount.Value;
        public int PerfectGuardCount => _perfectGuardCount.Value;
        public int PerfectDodgeCount => _perfectDodgeCount.Value;
        public bool IsSprinting => _sprinting.Value;
        public int GuardBreakCount => _guardBreakCount.Value;
        public bool HeavyFullyCharged => _heavyFullyCharged.Value;
        public int HealingFlaskCharges => _healingFlaskCharges.Value;
        public int HeavyHitCount => _heavyHitCount.Value;
        public int FullyChargedHeavyHitCount => _fullyChargedHeavyHitCount.Value;
        public int RangedHitCount => _rangedHitCount.Value;
        public int HealResolvedCount => _healResolvedCount.Value;
        public string InteractionFeedback { get; private set; } = string.Empty;
        public bool LastInteractionAccepted { get; private set; }
        public bool HasInteractionFeedback => Time.unscaledTime < _interactionFeedbackExpiresAt &&
                                              !string.IsNullOrWhiteSpace(InteractionFeedback);
        public bool IsAttackPresentationConfigured =>
            _animator != null && _animationSet != null && _animationSet.Controller != null &&
            _throwingKnifeVisualPrefab != null;
        public float ApproximateHeavyChargeNormalized => ReplicatedCombatState == CombatState.HeavyCharge &&
            _presentationTuning != null
                ? Mathf.Clamp01((Time.unscaledTime - _replicatedStateEnteredAt) /
                                _presentationTuning.HeavyFullChargeSeconds)
                : 0f;
        public float ApproximateRangedCooldownRemaining
        {
            get
            {
                if (NetworkManager == null || !_matchStarted.Value) return 0f;
                return Mathf.Max(0f, (float)(_rangedReadyServerTime.Value - NetworkManager.ServerTime.Time));
            }
        }
        internal int ServerCombatantId => unchecked((int)(OwnerClientId & int.MaxValue));

        public void Configure(
            PlayerInputReader input,
            CharacterController controller,
            Renderer[] teamMarkers,
            Renderer attackIndicator,
            Renderer defenseIndicator,
            Renderer downedIndicator,
            CombatTuningAsset combatTuning,
            Animator animator,
            PlayerAnimationSet animationSet,
            GameObject throwingKnifeVisualPrefab,
            ThirdPersonCameraRig ownerCameraRig,
            LockOnTargeting targeting)
        {
            _input = input;
            _controller = controller;
            _teamMarkers = teamMarkers ?? Array.Empty<Renderer>();
            _attackIndicator = attackIndicator;
            _defenseIndicator = defenseIndicator;
            _downedIndicator = downedIndicator;
            _combatTuning = combatTuning;
            _animator = animator;
            _animationSet = animationSet;
            _throwingKnifeVisualPrefab = throwingKnifeVisualPrefab;
            _ownerCameraRig = ownerCameraRig;
            _targeting = targeting;
        }

        private void Awake()
        {
            _controller ??= GetComponent<CharacterController>();
            _input ??= GetComponent<PlayerInputReader>();
            _animator ??= GetComponentInChildren<Animator>(true);
            _targeting ??= GetComponent<LockOnTargeting>();
        }

        public override void OnNetworkSpawn()
        {
            _health.OnValueChanged += OnReplicatedHealthChanged;
            _sharedRewardCount.OnValueChanged += OnReplicatedRewardChanged;
            _downed.OnValueChanged += OnReplicatedDownedChanged;
            _rescueProgress.OnValueChanged += OnReplicatedRescueProgressChanged;
            _partyDefeated.OnValueChanged += OnReplicatedPartyDefeatedChanged;
            _spawnTime = Time.unscaledTime;
            _autoReady = HasCommandLineFlag("-emberfall-network-auto-ready=");
            _autoMove = HasCommandLineFlag("-emberfall-network-auto-move=");
            _autoCombat = HasCommandLineFlag("-emberfall-network-auto-combat=");
            _autoWorld = HasCommandLineFlag("-emberfall-network-auto-world=");
            _autoEncounterPoint = HasCommandLineOption("-emberfall-network-stop-at-encounter=");
            _autoRescue = HasCommandLineFlag("-emberfall-network-auto-rescue=");
            _autoDisconnectDuringRescue = HasCommandLineFlag("-emberfall-network-disconnect-during-rescue=");
            _autoDefense = HasCommandLineFlag("-emberfall-network-auto-defense=");
            _autoAttackSuite = HasCommandLineFlag("-emberfall-network-auto-attack-suite=");
            _autoDisconnectAfterWorldSeal =
                HasCommandLineFlag("-emberfall-network-disconnect-after-world-seal=");
            _autoDisconnectBeforeResult =
                HasCommandLineFlag("-emberfall-network-disconnect-before-result=");
            _presentationTuning = _combatTuning != null
                ? _combatTuning.CreateRuntimeCopy()
                : CombatTuning.CreateDefault();
            _replicatedStateEnteredAt = Time.unscaledTime;
            _lastPresentationPosition = transform.position;
            ConfigureAnimatorPresentation();

            if (IsServer)
            {
                _combat = new CombatStateMachine(_presentationTuning);
                _combatIntentValidator = new NetworkCombatIntentValidator();
                _interactionIntentValidator = new NetworkInteractionIntentValidator();
                _rescueIntentValidator = new NetworkInteractionIntentValidator();
                PublishCombatState();
                _serverPosition.Value = transform.position;
                _serverYaw.Value = transform.eulerAngles.y;
                _validator = new NetworkMovementValidator(SprintSpeed, 0.65f, 0.25d);
                _validator.Reset(transform.position.x, transform.position.z, Time.realtimeSinceStartupAsDouble);
            }

            if (_controller != null) _controller.enabled = IsOwner;
            if (_input != null) _input.enabled = IsOwner;
            if (_targeting != null) _targeting.enabled = IsOwner;
            if (_ownerCameraRig != null) _ownerCameraRig.gameObject.SetActive(false);
            if (IsOwner) ActivateOwnerCamera();
            ApplyTeamColor();
            UpdateCombatPresentation();
            UpdateNetworkAnimation();
            NetworkGymSceneController.Find()?.Register(this);
            Debug.Log($"[M5_NETWORK_GYM_PLAYER_SPAWNED] owner={OwnerClientId} local={IsOwner} server={IsServer}");
        }

        public override void OnNetworkDespawn()
        {
            _health.OnValueChanged -= OnReplicatedHealthChanged;
            _sharedRewardCount.OnValueChanged -= OnReplicatedRewardChanged;
            _downed.OnValueChanged -= OnReplicatedDownedChanged;
            _rescueProgress.OnValueChanged -= OnReplicatedRescueProgressChanged;
            _partyDefeated.OnValueChanged -= OnReplicatedPartyDefeatedChanged;
            NetworkGymSceneController.Find()?.Unregister(this);
            if (_input != null) _input.enabled = false;
            if (_targeting != null) _targeting.enabled = false;
            if (_ownerCameraRig != null) _ownerCameraRig.gameObject.SetActive(false);
        }

        private void OnReplicatedHealthChanged(float previous, float current)
        {
            if (IsOwner && !IsServer)
                Debug.Log($"[M5_PLAYER_HEALTH_SYNC] previous={previous:F1} current={current:F1}");
        }

        private void OnReplicatedRewardChanged(int previous, int current)
        {
            if (IsOwner && !IsServer)
                Debug.Log($"[M5_WORLD_REWARD_SYNC] previous={previous} current={current}");
        }

        private void OnReplicatedDownedChanged(bool previous, bool current)
        {
            ApplyTeamColor();
            UpdateCombatPresentation();
            if (!IsServer)
                Debug.Log($"[M5_DOWNED_SYNC] clientId={OwnerClientId} previous={previous} current={current}");
        }

        private void OnReplicatedRescueProgressChanged(float previous, float current)
        {
            UpdateCombatPresentation();
            if (!IsServer && (current <= 0f || current >= 1f))
                Debug.Log($"[M5_RESCUE_PROGRESS_SYNC] clientId={OwnerClientId} previous={previous:F2} current={current:F2}");
        }

        private void OnReplicatedPartyDefeatedChanged(bool previous, bool current)
        {
            if (!IsServer)
                Debug.Log($"[M5_PARTY_DEFEAT_SYNC] previous={previous} current={current}");
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner && _ownerCameraRig != null)
                _ownerCameraRig.SetLookInputBlocked(InputSuppressed);
            _worldObjective ??= NetworkGymSceneController.Find()?.WorldObjective;
            if (IsServer) TickServerCombat();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            TickPerformanceCombatTelemetry();
#endif
            UpdateCombatPresentation();
            PresentReplicatedActionEvents();
            if (TryRunDisconnectAfterWorldSealSmoke()) return;
            if (TryRunDisconnectBeforeResultSmoke()) return;
            if (TryRunDisconnectDuringRescueSmoke()) return;
            if (!IsOwner)
            {
                InterpolateRemotePose();
                UpdateNetworkAnimation();
                return;
            }

            if (!_ready.Value && ((_input != null && _input.ConsumeInteractPressed()) ||
                                  (_autoReady && Time.unscaledTime - _spawnTime >= 0.75f)))
            {
                _autoReady = false;
                SetReadyServerRpc();
            }

            if (_input != null && _input.ConsumePausePressed())
            {
                SessionRuntime.Current.Shutdown();
                SceneManager.LoadScene("01_MainMenu", LoadSceneMode.Single);
                return;
            }

            if (_matchStarted.Value && !InputSuppressed && !IsDowned && !IsDead && !PartyDefeated)
            {
                DriveOwner();
                CaptureCombatInput();
                bool hasDownedTeammate = NetworkGymSceneController.Find()?.HasDownedTeammate(this) == true;
                CaptureRescueInput(hasDownedTeammate);
                if (!hasDownedTeammate) CaptureInteractionInput();
                else _input?.ConsumeInteractPressed();
            }
            ReconcileOwner();
            UpdateNetworkAnimation();
        }

        private void LateUpdate()
        {
            if (!_hasAnimatorAnchor || _animator == null) return;
            _animator.transform.localPosition = _animatorAnchorPosition;
            _animator.transform.localRotation = _animatorAnchorRotation;
        }

        private bool TryRunDisconnectDuringRescueSmoke()
        {
            if (!_autoDisconnectDuringRescue || _autoDisconnectIssued || !IsOwner || IsServer ||
                !IsDowned || RescueProgress < 0.15f)
                return false;

            _autoDisconnectIssued = true;
            Debug.Log($"[M5_RESCUE_SMOKE_DISCONNECT] clientId={OwnerClientId} progress={RescueProgress:F2}");
            SessionRuntime.Current.Shutdown();
            Application.Quit(0);
            return true;
        }

        private bool TryRunDisconnectAfterWorldSealSmoke()
        {
            if (!_autoDisconnectAfterWorldSeal || _worldDisconnectIssued || !IsOwner || IsServer ||
                _worldObjective == null || NetworkGymSceneController.Find()?.IsSharedMainWorld != true ||
                _worldObjective.ReplicatedStage != MainQuestStage.EnterSanctum)
                return false;

            _worldDisconnectIssued = true;
            Debug.Log(
                $"[M5_MAIN_WORLD_SMOKE_DISCONNECT] clientId={OwnerClientId} " +
                $"stage={_worldObjective.ReplicatedStage}");
            SessionRuntime.Current.Shutdown();
            Application.Quit(0);
            return true;
        }

        private bool TryRunDisconnectBeforeResultSmoke()
        {
            if (!_autoDisconnectBeforeResult || _worldDisconnectIssued || !IsOwner || IsServer ||
                _worldObjective == null || NetworkGymSceneController.Find()?.IsSharedMainWorld != true ||
                _worldObjective.ResultPublished ||
                _worldObjective.ReplicatedStage != MainQuestStage.ReturnToScout)
                return false;

            _worldDisconnectIssued = true;
            Debug.Log(
                $"[M5_PRE_RESULT_DISCONNECT] clientId={OwnerClientId} " +
                $"stage={_worldObjective.ReplicatedStage}");
            SessionRuntime.Current.Shutdown();
            Application.Quit(0);
            return true;
        }

        private void DriveOwner()
        {
            CombatState replicatedState = ReplicatedCombatState;
            if (replicatedState != CombatState.Locomotion)
            {
                if (replicatedState == CombatState.Dodge)
                {
                    transform.position = Vector3.Lerp(
                        transform.position, _serverPosition.Value, 20f * Time.deltaTime);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation,
                        Quaternion.Euler(0f, _serverYaw.Value, 0f),
                        20f * Time.deltaTime);
                }
                _previousOwnerCombatState = replicatedState;
                return;
            }

            if (_previousOwnerCombatState == CombatState.Dodge)
            {
                transform.SetPositionAndRotation(
                    _serverPosition.Value,
                    Quaternion.Euler(0f, _serverYaw.Value, 0f));
            }
            _previousOwnerCombatState = replicatedState;

            Vector2 move;
            bool useCameraRelativeMovement = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TryPerformanceCombatMovement(out move)) { }
            else
#endif
            if (_autoRescue && OwnerClientId == NetworkManager.ServerClientId &&
                NetworkGymSceneController.Find()?.TryGetNearestDownedTeammatePosition(this, out Vector3 rescueTarget) == true)
            {
                Vector3 offset = rescueTarget - transform.position;
                offset.y = 0f;
                move = offset.sqrMagnitude > 1.8f
                    ? new Vector2(offset.normalized.x, offset.normalized.z)
                    : Vector2.zero;
            }
            else if (_autoWorld && _worldObjective != null &&
                _worldObjective.TryGetCurrentInteractionTarget(out Vector3 worldTarget))
            {
                if (IsAssignedAutoWorldActor())
                {
                    Vector3 offset = worldTarget - transform.position;
                    offset.y = 0f;
                    move = offset.sqrMagnitude > 1.8f
                        ? new Vector2(offset.normalized.x, offset.normalized.z)
                        : Vector2.zero;
                }
                else
                {
                    move = Vector2.zero;
                }
            }
            else
            {
                move = _autoMove
                    ? new Vector2(OwnerClientId == NetworkManager.ServerClientId ? 0.65f : -0.65f, 0.35f)
                    : (_input != null ? _input.Move : Vector2.zero);
                useCameraRelativeMovement = !_autoMove;
            }
            Vector3 direction = ResolveMovementDirection(move, useCameraRelativeMovement);
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, TurnSpeed * Time.deltaTime);
            }

            if (_controller.isGrounded && _verticalSpeed < 0f)
                _verticalSpeed = GroundedVerticalSpeed;
            else
                _verticalSpeed += Gravity * Time.deltaTime;
            bool sprintRequested = direction.sqrMagnitude > 0.001f && _input != null && _input.SprintHeld;
            float moveSpeed = _sprinting.Value ? SprintSpeed : MoveSpeed;
            Vector3 velocity = (direction * moveSpeed) + (Vector3.up * _verticalSpeed);
            _controller.Move(velocity * Time.deltaTime);

            NetworkGymSceneController sceneController = NetworkGymSceneController.Find();
            transform.position = sceneController != null
                ? sceneController.ConstrainToPlayableBounds(transform.position)
                : transform.position;

            if (Time.unscaledTime < _nextSendTime) return;
            _nextSendTime = Time.unscaledTime + SendInterval;
            SubmitPoseServerRpc(transform.position, transform.eulerAngles.y, ++_nextSequence, sprintRequested);
        }

        private void CaptureCombatInput()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TryPerformanceCombatInput()) return;
#endif
            if (_autoAttackSuite && OwnerClientId != NetworkManager.ServerClientId &&
                TryAdvanceAutoAttackSuite()) return;

            if (_input != null && _input.ConsumeHeavyReleased())
            {
                SubmitCombatIntent(CombatCommand.HeavyReleased, ResolveCombatAimDirection());
                return;
            }

            if (_input != null && _input.ConsumeDodgePressed())
            {
                SubmitCombatIntent(CombatCommand.Dodge, _input.Move);
                return;
            }

            if (_input != null && _input.ConsumeHeavyPressed())
            {
                SubmitCombatIntent(CombatCommand.HeavyPressed, ResolveCombatAimDirection());
                return;
            }

            if (_input != null && _input.ConsumeRangedPressed())
            {
                SubmitCombatIntent(CombatCommand.RangedAttack, ResolveCombatAimDirection());
                return;
            }

            if (_input != null && _input.ConsumeHealPressed())
            {
                SubmitCombatIntent(CombatCommand.Heal, Vector2.zero);
                return;
            }

            if (_input != null && _input.ConsumeGuardPressed())
            {
                SubmitCombatIntent(CombatCommand.GuardPressed, Vector2.zero);
                return;
            }

            if (_input != null && _input.ConsumeGuardReleased())
            {
                SubmitCombatIntent(CombatCommand.GuardReleased, Vector2.zero);
                return;
            }

            if (_autoDefense && OwnerClientId != NetworkManager.ServerClientId)
            {
                if (_autoDefenseStage == 0 && Time.unscaledTime - _spawnTime >= 1.25f)
                {
                    _autoDefenseStage = 1;
                    SubmitCombatIntent(CombatCommand.Dodge, new Vector2(0f, -1f));
                    return;
                }

                if (_autoDefenseStage == 1 && DodgeEvadeCount > 0 &&
                    ReplicatedCombatState == CombatState.Locomotion)
                {
                    _autoDefenseStage = 2;
                    SubmitCombatIntent(CombatCommand.GuardPressed, Vector2.zero);
                    return;
                }
            }

            bool manualLight = _input != null && _input.ConsumeLightPressed();
            bool automaticLight = _autoCombat && !_autoAttackSuite &&
                                  Time.unscaledTime >= _nextAutoAttackTime;
            if (!manualLight && !automaticLight) return;
            if (automaticLight) _nextAutoAttackTime = Time.unscaledTime + 0.32f;
            SubmitCombatIntent(CombatCommand.LightAttack, ResolveCombatAimDirection());
        }

        private bool TryAdvanceAutoAttackSuite()
        {
            if (Time.unscaledTime < _autoAttackSuiteStageAt) return false;
            switch (_autoAttackSuiteStage)
            {
                case 0:
                    if (Time.unscaledTime - _spawnTime < 1.25f ||
                        ReplicatedCombatState != CombatState.Locomotion) return false;
                    _autoAttackSuiteStage = 1;
                    // Keep a wide automation margin over the authored 0.55 s full-charge
                    // threshold so reliable retransmission under simulated loss cannot
                    // turn this acceptance probe into a timing flake.
                    _autoAttackSuiteStageAt = Time.unscaledTime + 0.9f;
                    SubmitCombatIntent(CombatCommand.HeavyPressed, ForwardAimDirection());
                    return true;
                case 1:
                    if (ReplicatedCombatState != CombatState.HeavyCharge) return false;
                    _autoAttackSuiteStage = 2;
                    _autoAttackSuiteStageAt = Time.unscaledTime + 0.9f;
                    SubmitCombatIntent(CombatCommand.HeavyReleased, ForwardAimDirection());
                    return true;
                case 2:
                    if (HeavyHitCount <= 0 || ReplicatedCombatState != CombatState.Locomotion)
                        return false;
                    Vector3 aim = transform.forward;
                    NetworkGymSceneController.Find()?.TryGetEnemyAimDirection(this, out aim);
                    _autoAttackSuiteStage = 3;
                    _autoAttackSuiteStageAt = Time.unscaledTime + 0.9f;
                    SubmitCombatIntent(CombatCommand.RangedAttack, new Vector2(aim.x, aim.z));
                    return true;
                case 3:
                    if (RangedHitCount <= 0 || Health >= 119.5f ||
                        ReplicatedCombatState != CombatState.Locomotion) return false;
                    _autoAttackSuiteStage = 4;
                    _autoAttackSuiteStageAt = Time.unscaledTime + 1.4f;
                    SubmitCombatIntent(CombatCommand.Heal, Vector2.zero);
                    return true;
                default:
                    return false;
            }
        }

        private void SubmitCombatIntent(CombatCommand command, Vector2 directionFact)
        {
            SubmitCombatIntentServerRpc(
                (byte)command,
                directionFact.x,
                directionFact.y,
                ++_nextCombatIntentSequence);
        }

        private Vector2 ResolveCombatAimDirection()
        {
            Vector3 direction = transform.forward;
            if (_targeting != null && _targeting.IsLocked && _targeting.CurrentTarget != null)
            {
                direction = _targeting.CurrentTarget.AimPoint.position - transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    direction.Normalize();
                    transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                }
                else
                {
                    direction = transform.forward;
                }
            }

            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f) direction = Vector3.forward;
            else direction.Normalize();
            return new Vector2(direction.x, direction.z);
        }

        private Vector2 ForwardAimDirection()
        {
            Vector3 forward = transform.forward;
            return new Vector2(forward.x, forward.z).normalized;
        }

        private void CaptureInteractionInput()
        {
            bool manualInteract = _input != null && _input.ConsumeInteractPressed();
            bool automaticInteract = false;
            if (_autoWorld && IsAssignedAutoWorldActor() && _worldObjective != null &&
                Time.unscaledTime >= _nextAutoInteractionTime &&
                _worldObjective.TryGetCurrentInteractionTarget(out Vector3 target))
            {
                Vector3 offset = target - transform.position;
                offset.y = 0f;
                automaticInteract = offset.sqrMagnitude <= 3.24f;
            }

            if (!manualInteract && !automaticInteract) return;
            if (automaticInteract) _nextAutoInteractionTime = Time.unscaledTime + 0.45f;
            SubmitInteractionIntentServerRpc(++_nextInteractionIntentSequence);
        }

        private void CaptureRescueInput(bool hasDownedTeammate)
        {
            bool automaticHeld = _autoRescue && OwnerClientId == NetworkManager.ServerClientId && hasDownedTeammate &&
                                 NetworkGymSceneController.Find()?.IsWithinRescueRangeOfDownedTeammate(this) == true;
            bool held = hasDownedTeammate && (automaticHeld || (_input != null && _input.InteractHeld));
            bool changed = held != _lastRescueHeld;
            if (!changed && (!held || Time.unscaledTime < _nextRescueSendTime)) return;

            _lastRescueHeld = held;
            _nextRescueSendTime = Time.unscaledTime + RescueSendInterval;
            SubmitRescueIntentServerRpc(held, ++_nextRescueIntentSequence);
        }

        private bool IsAssignedAutoWorldActor()
        {
            if (_worldObjective == null || NetworkManager == null) return false;
            bool hostStep = _worldObjective.ReplicatedStage == MainQuestStage.ActivateSeals;
            return hostStep
                ? OwnerClientId == NetworkManager.ServerClientId
                : OwnerClientId != NetworkManager.ServerClientId;
        }

        private void TickServerCombat()
        {
            if (_combat == null) return;
            _combat.SetSprintRequested(
                _matchStarted.Value && !_inputSuppressed.Value && !_downed.Value &&
                Time.unscaledTime <= _serverSprintRequestExpiresAt);
            TickServerDodge(Time.deltaTime);
            _combat.Tick(Time.deltaTime);
            TrackServerSprintSession(Time.deltaTime);
            ResolveServerRangedRelease();
            ResolveServerHeal();
            PublishCombatState();
            if (!_matchStarted.Value || !_combat.IsDamageWindowOpen ||
                _combat.AttackSequence == _lastResolvedAttackSequence) return;

            _lastResolvedAttackSequence = _combat.AttackSequence;
            NetworkGymSceneController controller = NetworkGymSceneController.Find();
            int hitCount = controller != null ? controller.ServerResolvePlayerAttack(this, _combat) : 0;
            Debug.Log(
                $"[M5C_FEEL] event=attack-hit-count value={hitCount} " +
                $"sequence={_combat.AttackSequence} source={OwnerClientId}");
            if (hitCount > 0)
            {
                if (_combat.CurrentAttackTag == AttackTag.Heavy)
                {
                    _heavyHitCount.Value++;
                    if (_combat.IsHeavyFullyCharged) _fullyChargedHeavyHitCount.Value++;
                    Debug.Log(
                        $"[M5_HEAVY_ENEMY_DAMAGE] source={OwnerClientId} " +
                        $"sequence={_combat.AttackSequence} damage={_combat.CurrentAttackDamage:F1} " +
                        $"fullyCharged={_combat.IsHeavyFullyCharged}");
                }
            }
        }

        private void ResolveServerRangedRelease()
        {
            if (_combat.RangedReleaseSequence == _lastResolvedRangedReleaseSequence) return;
            _lastResolvedRangedReleaseSequence = _combat.RangedReleaseSequence;
            Vector3 direction = _serverPendingRangedDirection.sqrMagnitude > 0.0001f
                ? _serverPendingRangedDirection.normalized
                : transform.forward;
            _rangedDirection.Value = direction;
            _rangedReleaseSequence.Value = _combat.RangedReleaseSequence;
            var release = new RangedAttackRelease(
                _combat.AttackSequence,
                _combat.RangedDamage,
                _combat.RangedPostureDamage,
                _combat.RangedProjectileSpeed,
                _combat.RangedMaximumDistance);
            string reason = "scene-controller-missing";
            NetworkGymSceneController controller = NetworkGymSceneController.Find();
            bool hit = controller != null &&
                       controller.ServerTryResolvePlayerProjectile(this, release, direction, out reason);
            if (hit) _rangedHitCount.Value++;
            Debug.Log(
                $"[M5_RANGED_RELEASE] clientId={OwnerClientId} sequence={release.AttackSequence} " +
                $"hit={hit} reason={(hit ? "accepted" : reason)} direction={direction}");
        }

        private void ResolveServerHeal()
        {
            if (_combat.HealSequence == _healSequence.Value) return;
            _healSequence.Value = _combat.HealSequence;
            _healResolvedCount.Value++;
            Debug.Log(
                $"[M5_HEAL_RESOLVED] clientId={OwnerClientId} sequence={_combat.HealSequence} " +
                $"amount={_combat.LastHealAmount:F1} health={_combat.Health.Current:F1} " +
                $"flasks={_combat.HealingFlasks.CurrentCharges}");
        }

        private void BeginServerDodge(float directionX, float directionZ)
        {
            Vector3 direction = new Vector3(directionX, 0f, directionZ);
            if (direction.sqrMagnitude <= 0.0001f) direction = transform.forward;
            direction.y = 0f;
            _serverDodgeDirection = direction.normalized;
            _serverDodgeElapsed = 0f;
            _serverDodgeDuration = _combat.StateDuration;
            _serverDodgePreviousTravel = 0f;
            _serverDodgeActive = true;
            transform.rotation = Quaternion.LookRotation(_serverDodgeDirection, Vector3.up);
            _serverYaw.Value = transform.eulerAngles.y;
        }

        private void TickServerDodge(float deltaTime)
        {
            if (!_serverDodgeActive || _combat.State != CombatState.Dodge) return;

            _serverDodgeElapsed = Mathf.Min(_serverDodgeDuration, _serverDodgeElapsed + deltaTime);
            float normalized = _serverDodgeDuration <= 0f ? 1f : _serverDodgeElapsed / _serverDodgeDuration;
            float travel = DodgeTravelProfile.Evaluate(normalized);
            float distance = Mathf.Max(0f, travel - _serverDodgePreviousTravel) * _combat.DodgeDistance;
            _serverDodgePreviousTravel = travel;

            Vector3 next = transform.position + (_serverDodgeDirection * distance);
            NetworkGymSceneController sceneController = NetworkGymSceneController.Find();
            if (sceneController != null) next = sceneController.ConstrainToPlayableBounds(next);
            transform.position = next;
            _serverPosition.Value = next;
            _serverYaw.Value = transform.eulerAngles.y;

            if (_serverDodgeElapsed < _serverDodgeDuration) return;
            _serverDodgeActive = false;
            _validator?.Reset(
                transform.position.x,
                transform.position.z,
                Time.realtimeSinceStartupAsDouble,
                _acknowledgedSequence.Value);
            Debug.Log(
                $"[M5_SERVER_DODGE_COMPLETED] clientId={OwnerClientId} " +
                $"distance={_combat.DodgeDistance:F2} position={transform.position}");
        }

        private void PublishCombatState()
        {
            _health.Value = _combat.Health.Current;
            _stamina.Value = _combat.Stamina.Current;
            _posture.Value = _combat.Posture.Current;
            _combatState.Value = (int)_combat.State;
            _attackSequence.Value = _combat.AttackSequence;
            _heavyFullyCharged.Value = _combat.IsHeavyFullyCharged;
            _healingFlaskCharges.Value = _combat.HealingFlasks.CurrentCharges;
            _sprinting.Value = _combat.IsSprinting;
        }

        private void TrackServerSprintSession(float deltaTime)
        {
            if (_combat.IsSprinting)
            {
                _serverSprintSessionActive = true;
                _serverSprintSessionSeconds += Mathf.Max(0f, deltaTime);
                return;
            }

            if (!_serverSprintSessionActive) return;
            Debug.Log(
                $"[M5C_FEEL] event=sprint-session value={_serverSprintSessionSeconds:F2} " +
                $"sequence=0 source={OwnerClientId} stamina={_combat.Stamina.Current:F1}");
            _serverSprintSessionActive = false;
            _serverSprintSessionSeconds = 0f;
        }

        private void UpdateCombatPresentation()
        {
            CombatState state = (CombatState)_combatState.Value;
            if (state != _lastPresentationState)
            {
                _lastPresentationState = state;
                _replicatedStateEnteredAt = Time.unscaledTime;
            }

            if (_attackIndicator != null)
            {
                bool attackVisible = !IsDowned &&
                                     (state == CombatState.HeavyCharge || state == CombatState.HeavyAttack ||
                                      state == CombatState.RangedAttack ||
                                      (state >= CombatState.LightAttack1 && state <= CombatState.LightAttack3));
                _attackIndicator.enabled = attackVisible;
                if (attackVisible)
                {
                    Color attackColor = state == CombatState.HeavyCharge
                        ? Color.Lerp(
                            new Color(1f, 0.5f, 0.08f),
                            new Color(1f, 0.08f, 0.02f),
                            HeavyFullyCharged ? 1f : ApproximateHeavyChargeNormalized)
                        : state == CombatState.RangedAttack
                            ? new Color(0.22f, 0.78f, 1f)
                            : state == CombatState.HeavyAttack
                                ? new Color(1f, 0.18f, 0.04f)
                                : new Color(1f, 0.68f, 0.12f);
                    var attackBlock = new MaterialPropertyBlock();
                    attackBlock.SetColor("_BaseColor", attackColor);
                    attackBlock.SetColor("_EmissionColor", attackColor * 2f);
                    _attackIndicator.SetPropertyBlock(attackBlock);
                    float chargeScale = state == CombatState.HeavyCharge
                        ? 0.7f + (0.5f * ApproximateHeavyChargeNormalized)
                        : 1f;
                    _attackIndicator.transform.localScale = Vector3.one * chargeScale;
                }
            }
            if (_defenseIndicator != null)
            {
                bool defenseVisible = !IsDowned &&
                                      (state == CombatState.Dodge || state == CombatState.Guard ||
                                       state == CombatState.GuardBreak || state == CombatState.Heal);
                _defenseIndicator.enabled = defenseVisible;
                if (defenseVisible)
                {
                    Color defenseColor = state == CombatState.Dodge
                        ? new Color(0.1f, 0.85f, 1f)
                        : state == CombatState.Guard
                            ? new Color(0.2f, 1f, 0.48f)
                            : state == CombatState.Heal
                                ? new Color(0.28f, 1f, 0.45f)
                                : new Color(1f, 0.28f, 0.05f);
                    var defenseBlock = new MaterialPropertyBlock();
                    defenseBlock.SetColor("_BaseColor", defenseColor);
                    defenseBlock.SetColor("_EmissionColor", defenseColor * 2f);
                    _defenseIndicator.SetPropertyBlock(defenseBlock);
                }
            }
            if (_downedIndicator == null) return;

            _downedIndicator.enabled = IsDowned;
            float pulse = 0.88f + (0.22f * Mathf.Sin(Time.unscaledTime * 5f));
            float progressScale = 1f + (RescueProgress * 0.45f);
            _downedIndicator.transform.localScale = new Vector3(pulse * progressScale, 0.025f, pulse * progressScale);
            Color color = Color.Lerp(new Color(1f, 0.12f, 0.08f), new Color(0.2f, 1f, 0.45f), RescueProgress);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", color * 2f);
            _downedIndicator.SetPropertyBlock(block);
        }

        private void ConfigureAnimatorPresentation()
        {
            if (_animator == null || _animationSet == null || _animationSet.Controller == null) return;
            _animator.runtimeAnimatorController = _animationSet.Controller;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            _animatorAnchorPosition = _animator.transform.localPosition;
            _animatorAnchorRotation = _animator.transform.localRotation;
            _hasAnimatorAnchor = true;
        }

        private void UpdateNetworkAnimation()
        {
            if (_animator == null || _animationSet == null || _animationSet.Controller == null) return;
            Vector3 delta = transform.position - _lastPresentationPosition;
            delta.y = 0f;
            float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            _lastPresentationPosition = transform.position;
            _animator.SetFloat(AnimationSpeedId, speed, 0.08f, Time.deltaTime);

            CombatState state = ReplicatedCombatState;
            if (state == _presentedAnimationState) return;
            _presentedAnimationState = state;
            AnimationClip clip = _animationSet.GetClip(state);
            float duration = GetPresentationStateDuration(state);
            AnimatorSpeedCoordinator.SetBase(_animator, clip != null && duration > 0f
                ? Mathf.Clamp(clip.length / duration, 0.35f, 3f)
                : 1f, state == CombatState.Dead);
            _animator.CrossFadeInFixedTime(state.ToString(), 0.08f, 0, 0f);
        }

        private float GetPresentationStateDuration(CombatState state)
        {
            if (_presentationTuning == null) return 0f;
            if (state >= CombatState.LightAttack1 && state <= CombatState.LightAttack3)
                return _presentationTuning.GetLightDuration((int)state - (int)CombatState.LightAttack1);
            return state switch
            {
                CombatState.HeavyAttack => _presentationTuning.HeavyDuration,
                CombatState.RangedAttack => _presentationTuning.RangedDuration,
                CombatState.Dodge => _presentationTuning.DodgeDuration,
                CombatState.HitReact => _presentationTuning.HitReactDuration,
                CombatState.GuardBreak => _presentationTuning.GuardBreakDuration,
                CombatState.Heal => _presentationTuning.HealDuration,
                _ => 0f
            };
        }

        private void PresentReplicatedActionEvents()
        {
            if (_rangedReleaseSequence.Value != _presentedRangedReleaseSequence)
            {
                _presentedRangedReleaseSequence = _rangedReleaseSequence.Value;
                if (_presentedRangedReleaseSequence > 0 && _presentationTuning != null)
                {
                    Transform hand = _animator != null && _animator.isHuman
                        ? _animator.GetBoneTransform(HumanBodyBones.RightHand)
                        : null;
                    Vector3 origin = hand != null
                        ? hand.position
                        : transform.position + (Vector3.up * 1.1f) + (transform.forward * 0.35f);
                    NetworkGymProjectilePresentation.Spawn(
                        _throwingKnifeVisualPrefab,
                        origin,
                        _rangedDirection.Value,
                        _presentationTuning.RangedProjectileSpeed,
                        _presentationTuning.RangedMaximumDistance);
                }
            }

            if (_healSequence.Value == _presentedHealSequence) return;
            _presentedHealSequence = _healSequence.Value;
            if (_presentedHealSequence > 0 && IsOwner && !IsServer)
                Debug.Log(
                    $"[M5_HEAL_SYNC] clientId={OwnerClientId} sequence={_presentedHealSequence} " +
                    $"health={Health:F1} flasks={HealingFlaskCharges}");
        }

        private void ReconcileOwner()
        {
            if (InputSuppressed) return;
            uint acknowledged = _acknowledgedSequence.Value;
            if (acknowledged == 0 || acknowledged == _lastReconciledSequence) return;
            _lastReconciledSequence = acknowledged;

            float error = Vector3.Distance(transform.position, _serverPosition.Value);
            if (error <= 0.4f) return;
            transform.position = error >= 1.5f
                ? _serverPosition.Value
                : Vector3.Lerp(transform.position, _serverPosition.Value, 0.65f);
        }

        private void InterpolateRemotePose()
        {
            transform.position = Vector3.Lerp(transform.position, _serverPosition.Value, 14f * Time.deltaTime);
            Quaternion target = Quaternion.Euler(0f, _serverYaw.Value, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, 14f * Time.deltaTime);
        }

        [ServerRpc]
        private void SetReadyServerRpc(ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || _ready.Value) return;
            _ready.Value = true;
            Debug.Log($"[M5_NETWORK_GYM_READY] clientId={OwnerClientId}");
        }

        [ServerRpc]
        private void SubmitPoseServerRpc(
            Vector3 candidatePosition,
            float candidateYaw,
            uint sequence,
            bool sprintRequested,
            ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !_matchStarted.Value || _validator == null) return;
            if (_combat == null || _combat.State != CombatState.Locomotion) return;
            if (_autoWorld && Time.unscaledTime < _serverPoseSuppressedUntil) return;

            if (sprintRequested)
                _serverSprintRequestExpiresAt = Time.unscaledTime + (SendInterval * 2.5f);
            else
                _serverSprintRequestExpiresAt = 0f;

            NetworkGymSceneController sceneController = NetworkGymSceneController.Find();
            Vector3 boundedPosition = sceneController != null
                ? sceneController.ConstrainToPlayableBounds(candidatePosition)
                : candidatePosition;
            float boundedX = boundedPosition.x;
            float boundedZ = boundedPosition.z;
            bool arenaCorrected = !Mathf.Approximately(boundedX, candidatePosition.x) ||
                                  !Mathf.Approximately(boundedZ, candidatePosition.z);
            NetworkMovementValidationResult result = _validator.Validate(
                boundedX,
                boundedZ,
                Time.realtimeSinceStartupAsDouble,
                sequence,
                _combat.IsSprinting ? SprintSpeed : MoveSpeed);
            if (result.Status == NetworkMovementValidationStatus.Ignored) return;

            float acceptedY = ResolveServerGroundY(result.AcceptedX, result.AcceptedZ, transform.position.y);
            Vector3 accepted = new Vector3(result.AcceptedX, acceptedY, result.AcceptedZ);
            _serverPosition.Value = accepted;
            _serverYaw.Value = NormalizeYaw(candidateYaw);
            _acknowledgedSequence.Value = sequence;
            transform.SetPositionAndRotation(accepted, Quaternion.Euler(0f, _serverYaw.Value, 0f));

            if (arenaCorrected || result.Status == NetworkMovementValidationStatus.Corrected)
            {
                _correctionCount.Value++;
                Debug.LogWarning(
                    $"[M5_MOVE_CORRECTION] clientId={OwnerClientId} sequence={sequence} " +
                    $"speed={result.ObservedSpeed:F2} reason={(arenaCorrected ? "arena-bounds" : result.Reason)}");
            }
            else if (sequence == 1)
            {
                Debug.Log($"[M5_MOVE_ACCEPTED] clientId={OwnerClientId} sequence=1");
            }
        }

        internal void ServerTeleportForWorldSmoke(Vector3 position, Quaternion rotation)
        {
            if (!IsServer || !_autoWorld || NetworkGymSceneController.Find()?.IsSharedMainWorld != true)
                return;

            position.y = ResolveServerGroundY(position.x, position.z, transform.position.y);
            float yaw = NormalizeYaw(rotation.eulerAngles.y);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _serverPosition.Value = position;
            _serverYaw.Value = yaw;
            _validator?.Reset(
                position.x,
                position.z,
                Time.realtimeSinceStartupAsDouble,
                _acknowledgedSequence.Value);
            _serverPoseSuppressedUntil = Time.unscaledTime + 1.2f;
            ApplyWorldSmokeTeleportClientRpc(position, yaw);
        }

        internal void ServerTeleportForSceneTransition(Vector3 position, Quaternion rotation)
        {
            if (!IsServer) return;
            position.y = ResolveServerGroundY(position.x, position.z, position.y);
            float yaw = NormalizeYaw(rotation.eulerAngles.y);
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _serverPosition.Value = position;
            _serverYaw.Value = yaw;
            _validator?.Reset(
                position.x,
                position.z,
                Time.realtimeSinceStartupAsDouble,
                _acknowledgedSequence.Value);
            _serverPoseSuppressedUntil = Time.unscaledTime + 1.2f;
            NetworkGymSceneController sceneController = NetworkGymSceneController.Find();
            if (sceneController == null) throw new InvalidOperationException("Scene transfer requires the active arena bounds.");
            uint epoch = ++_sceneTransitionEpoch.Value;
            ApplySceneTransitionTeleportClientRpc(position, yaw, sceneController.MovementBoundsSnapshot, epoch);
        }

        [ClientRpc]
        private void ApplySceneTransitionTeleportClientRpc(Vector3 position, float yaw, Vector4 bounds, uint epoch)
        {
            if (!IsOwner) return;
            ApplySceneTransitionSnapshot(position, yaw, bounds, epoch);
        }

        private void ApplySceneTransitionSnapshot(Vector3 position, float yaw, Vector4 bounds, uint epoch)
        {
            NetworkGymSceneController sceneController = NetworkGymSceneController.Find();
            if (sceneController == null) throw new InvalidOperationException("Owner scene transfer has no bounds controller.");
            sceneController.ApplyServerMovementBounds(bounds);
            bool wasEnabled = _controller != null && _controller.enabled;
            if (wasEnabled) _controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (wasEnabled) _controller.enabled = true;
            _verticalSpeed = 0f;
            _appliedSceneTransitionEpoch = epoch;
            Debug.Log($"[M5_SCENE_BOUNDS_APPLIED] owner={OwnerClientId} epoch={epoch} bounds={bounds} position={position}");
        }

        [ClientRpc]
        private void ApplyWorldSmokeTeleportClientRpc(Vector3 position, float yaw)
        {
            if (!IsOwner) return;
            bool controllerWasEnabled = _controller != null && _controller.enabled;
            if (controllerWasEnabled) _controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (controllerWasEnabled) _controller.enabled = true;
            _verticalSpeed = 0f;
            if (_matchStarted.Value)
                SubmitInteractionIntentServerRpc(++_nextInteractionIntentSequence);
        }

        private float ResolveServerGroundY(float x, float z, float fallbackY)
        {
            Vector3 origin = new Vector3(x, fallbackY + 3.5f, z);
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                Vector3.down,
                10f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(transform) ||
                    collider.GetComponentInParent<NetworkGymPlayer>() != null ||
                    collider.GetComponentInParent<NetworkGymEnemy>() != null)
                    continue;
                return hits[i].point.y;
            }

            return fallbackY;
        }

        [ServerRpc]
        private void SubmitCombatIntentServerRpc(
            byte commandValue,
            float directionX,
            float directionZ,
            uint sequence,
            ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !_matchStarted.Value ||
                _combat == null || _combatIntentValidator == null) return;

            CombatCommand command = (CombatCommand)commandValue;
            float normalizedDodgeX = 0f;
            float normalizedDodgeZ = 0f;
            if (command == CombatCommand.Dodge &&
                !NetworkCombatIntentValidator.TryNormalizeDodgeDirection(
                    directionX,
                    directionZ,
                    out normalizedDodgeX,
                    out normalizedDodgeZ,
                    out string directionReason))
            {
                Debug.LogWarning(
                    $"[M5_COMBAT_INTENT_REJECTED] clientId={OwnerClientId} sequence={sequence} reason={directionReason}");
                return;
            }

            float normalizedAimX = 0f;
            float normalizedAimZ = 0f;
            bool carriesFacing = command == CombatCommand.LightAttack ||
                                  command == CombatCommand.HeavyPressed ||
                                  command == CombatCommand.HeavyReleased ||
                                  command == CombatCommand.RangedAttack;
            if (carriesFacing)
            {
                if (!NetworkCombatIntentValidator.TryNormalizeAimDirection(
                        directionX,
                        directionZ,
                        out normalizedAimX,
                        out normalizedAimZ,
                        out string aimReason))
                {
                    Debug.LogWarning(
                        $"[M5_COMBAT_INTENT_REJECTED] clientId={OwnerClientId} " +
                        $"sequence={sequence} reason={aimReason}");
                    return;
                }

                Vector3 serverForward = transform.forward;
                float minimumDot = command == CombatCommand.RangedAttack ? 0f : CombatFacingMinimumDot;
                if (!NetworkCombatSpatialValidator.IsAimWithinFacingArc(
                        serverForward.x,
                        serverForward.z,
                        normalizedAimX,
                        normalizedAimZ,
                        minimumDot))
                {
                    Debug.LogWarning(
                        $"[M5_COMBAT_INTENT_REJECTED] clientId={OwnerClientId} " +
                        $"sequence={sequence} reason=aim-outside-server-facing-arc");
                    return;
                }
            }

            if (!_combatIntentValidator.TryAccept(sequence, command, out string reason))
            {
                Debug.LogWarning(
                    $"[M5_COMBAT_INTENT_REJECTED] clientId={OwnerClientId} sequence={sequence} reason={reason}");
                return;
            }

            bool executed = _combat.Submit(command);
            if (executed && carriesFacing)
            {
                Vector3 authoritativeFacing = new Vector3(normalizedAimX, 0f, normalizedAimZ);
                transform.rotation = Quaternion.LookRotation(authoritativeFacing, Vector3.up);
                _serverYaw.Value = transform.eulerAngles.y;
            }
            if (executed && command == CombatCommand.Dodge)
            {
                BeginServerDodge(normalizedDodgeX, normalizedDodgeZ);
                Debug.Log(
                    $"[M5C_FEEL] event=dodge-attempt value={_combat.DodgeAttemptCount} " +
                    $"sequence={sequence} source={OwnerClientId}");
            }
            if (executed && command == CombatCommand.GuardPressed)
                Debug.Log(
                    $"[M5C_FEEL] event=guard-attempt value={_combat.GuardAttemptCount} " +
                    $"sequence={sequence} source={OwnerClientId}");
            if (executed && command == CombatCommand.RangedAttack)
            {
                _serverPendingRangedDirection = new Vector3(normalizedAimX, 0f, normalizedAimZ);
                if (NetworkManager != null)
                    _rangedReadyServerTime.Value =
                        NetworkManager.ServerTime.Time + _combat.RangedCooldownRemaining;
            }
            PublishCombatState();
            Debug.Log(
                $"[M5_COMBAT_INTENT_ACCEPTED] clientId={OwnerClientId} sequence={sequence} " +
                $"command={command} executed={executed} state={_combat.State} " +
                $"stamina={_combat.Stamina.Current:F1} heavyFull={_combat.IsHeavyFullyCharged} " +
                $"flasks={_combat.HealingFlasks.CurrentCharges}");
        }

        [ServerRpc]
        private void SubmitInteractionIntentServerRpc(
            uint sequence,
            ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !_matchStarted.Value ||
                _interactionIntentValidator == null) return;

            if (!_interactionIntentValidator.TryAccept(sequence, out string reason))
            {
                Debug.LogWarning(
                    $"[M5_WORLD_INTENT_REJECTED] clientId={OwnerClientId} sequence={sequence} reason={reason}");
                return;
            }

            bool accepted = NetworkGymSceneController.Find()?.ServerTryInteract(this, out reason) == true;
            if (accepted)
                Debug.Log($"[M5_WORLD_INTENT_ACCEPTED] clientId={OwnerClientId} sequence={sequence}");
            else
                Debug.LogWarning(
                    $"[M5_WORLD_INTENT_REJECTED] clientId={OwnerClientId} sequence={sequence} reason={reason}");

            PresentInteractionFeedbackClientRpc(
                accepted,
                ResolveInteractionFeedback(accepted, reason),
                new ClientRpcParams
                {
                    Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
                });
        }

        [ClientRpc]
        private void PresentInteractionFeedbackClientRpc(
            bool accepted,
            string message,
            ClientRpcParams clientRpcParams = default)
        {
            if (!IsOwner) return;
            LastInteractionAccepted = accepted;
            InteractionFeedback = message ?? string.Empty;
            _interactionFeedbackExpiresAt = Time.unscaledTime + 2.6f;
        }

        [ServerRpc]
        private void SubmitRescueIntentServerRpc(
            bool held,
            uint sequence,
            ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || !_matchStarted.Value ||
                _rescueIntentValidator == null || IsDowned || IsDead) return;

            if (!_rescueIntentValidator.TryAccept(sequence, out string reason))
            {
                Debug.LogWarning(
                    $"[M5_RESCUE_INTENT_REJECTED] clientId={OwnerClientId} sequence={sequence} reason={reason}");
                return;
            }

            if (NetworkGymSceneController.Find()?.ServerSubmitRescueIntent(this, held, out reason) != true && held)
                Debug.LogWarning(
                    $"[M5_RESCUE_INTENT_REJECTED] clientId={OwnerClientId} sequence={sequence} reason={reason}");
        }

        internal DamageResult ServerReceiveDamage(DamageRequest request)
        {
            if (!IsServer || _combat == null) return DamageResult.Ignored;
            // The command-line world-objective smoke owns movement while it is active.
            // Keep that deterministic harness isolated from the independently verified
            // combat bot so an AI hit reaction cannot stall the interaction route.
            bool routeSmokeImmunity = _autoWorld && !_autoCombat;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (PerformanceCombatActive) routeSmokeImmunity = false;
#endif
            if (routeSmokeImmunity || _autoRescue || _autoEncounterPoint) return DamageResult.Ignored;
            DamageResult result = _combat.ReceiveDamage(request);
            PublishCombatState();
            if (result.Invulnerable)
            {
                _dodgeEvadeCount.Value++;
                Debug.Log(
                    $"[M5_DODGE_EVADE] clientId={OwnerClientId} sequence={request.AttackSequence} " +
                    $"count={_dodgeEvadeCount.Value}");
            }
            if (result.PerfectDodge)
            {
                _perfectDodgeCount.Value++;
                Debug.Log(
                    $"[M5C_FEEL] event=perfect-dodge value={_perfectDodgeCount.Value} " +
                    $"sequence={request.AttackSequence} source={OwnerClientId} stamina={_combat.Stamina.Current:F1}");
            }
            if (result.PerfectGuard)
            {
                _perfectGuardCount.Value++;
                Debug.Log(
                    $"[M5C_FEEL] event=perfect-guard value={_perfectGuardCount.Value} " +
                    $"sequence={request.AttackSequence} source={OwnerClientId}");
                Debug.Log(
                    $"[M5_PERFECT_GUARD] clientId={OwnerClientId} sequence={request.AttackSequence} " +
                    $"counterPosture={result.CounterPostureDamage:F1} count={_perfectGuardCount.Value}");
            }
            if (result.GuardBroken)
            {
                _guardBreakCount.Value++;
                Debug.Log(
                    $"[M5_PLAYER_GUARD_BREAK] clientId={OwnerClientId} sequence={request.AttackSequence} " +
                    $"count={_guardBreakCount.Value}");
            }
            if (result.Accepted || result.Killed || result.Defended || result.Invulnerable)
            {
                Debug.Log(
                    $"[M5_PLAYER_DAMAGE] clientId={OwnerClientId} applied={result.AppliedDamage:F1} " +
                    $"health={_combat.Health.Current:F1} posture={_combat.Posture.Current:F1} " +
                    $"evaded={result.Invulnerable} defended={result.Defended} perfect={result.PerfectGuard} " +
                    $"guardBroken={result.GuardBroken} killed={result.Killed}");
            }
            if (result.Killed)
                NetworkGymSceneController.Find()?.ServerHandlePlayerLethal(this);
            return result;
        }

        internal bool ServerForceDownForSmoke()
        {
            if (!IsServer || _combat == null || IsDowned || !_combat.ForceDeath()) return false;
            PublishCombatState();
            NetworkGymSceneController.Find()?.ServerHandlePlayerLethal(this);
            return true;
        }

        internal void ServerSetDowned(bool value)
        {
            if (!IsServer) return;
            _downed.Value = value;
            if (!value) _rescueProgress.Value = 0f;
            ApplyTeamColor();
            UpdateCombatPresentation();
        }

        internal void ServerSetRescueProgress(float normalized)
        {
            if (!IsServer) return;
            _rescueProgress.Value = Mathf.Clamp01(normalized);
        }

        internal bool ServerRevive(float healthFraction)
        {
            if (!IsServer || _combat == null || !_combat.Revive(healthFraction)) return false;
            _downed.Value = false;
            _rescueProgress.Value = 0f;
            _serverDodgeActive = false;
            PublishCombatState();
            ApplyTeamColor();
            UpdateCombatPresentation();
            return true;
        }

        internal void ServerResetAfterPartyDefeat(Vector3 spawnPosition)
        {
            if (!IsServer || _combat == null) return;
            if (_combat.IsDead) _combat.Revive(1f);
            else _combat.Reset();
            _downed.Value = false;
            _rescueProgress.Value = 0f;
            _partyDefeated.Value = false;
            _serverDodgeActive = false;
            _dodgeEvadeCount.Value = 0;
            _perfectGuardCount.Value = 0;
            _perfectDodgeCount.Value = 0;
            _guardBreakCount.Value = 0;
            _heavyHitCount.Value = 0;
            _fullyChargedHeavyHitCount.Value = 0;
            _rangedHitCount.Value = 0;
            _healResolvedCount.Value = 0;
            _serverSprintRequestExpiresAt = 0f;
            _serverSprintSessionActive = false;
            _serverSprintSessionSeconds = 0f;
            transform.position = spawnPosition;
            _serverPosition.Value = spawnPosition;
            _serverYaw.Value = 0f;
            _validator?.Reset(spawnPosition.x, spawnPosition.z, Time.realtimeSinceStartupAsDouble, _acknowledgedSequence.Value);
            PublishCombatState();
            ApplyTeamColor();
            UpdateCombatPresentation();
        }

        internal void ServerSetPartyDefeated(bool value)
        {
            if (IsServer) _partyDefeated.Value = value;
        }

        internal void ServerSetInputSuppressed(bool value)
        {
            if (IsServer) _inputSuppressed.Value = value;
        }

        internal void ServerGrantSharedReward()
        {
            if (!IsServer) return;
            _sharedRewardCount.Value = Mathf.Min(1, _sharedRewardCount.Value + 1);
        }

        internal void ServerSetMatchStarted()
        {
            if (!IsServer || _matchStarted.Value) return;
            _matchStarted.Value = true;
            _nextAutoAttackTime = Time.unscaledTime + 0.2f;
            _nextAutoInteractionTime = Time.unscaledTime + 0.3f;
            _nextRescueSendTime = Time.unscaledTime + 0.1f;
        }

        private void ApplyTeamColor()
        {
            Color color = IsDowned
                ? new Color(0.42f, 0.42f, 0.46f)
                : OwnerClientId == NetworkManager.ServerClientId
                    ? new Color(0.18f, 0.85f, 1f)
                    : new Color(1f, 0.45f, 0.12f);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", color * 1.5f);
            for (int i = 0; i < _teamMarkers.Length; i++)
            {
                if (_teamMarkers[i] != null) _teamMarkers[i].SetPropertyBlock(block);
            }
        }

        private Vector3 ResolveMovementDirection(Vector2 move, bool cameraRelative)
        {
            Vector3 raw = new Vector3(move.x, 0f, move.y);
            if (!cameraRelative || _ownerCameraRig == null || !_ownerCameraRig.gameObject.activeInHierarchy)
                return Vector3.ClampMagnitude(raw, 1f);

            Vector3 forward = Vector3.ProjectOnPlane(_ownerCameraRig.transform.forward, Vector3.up);
            if (forward.sqrMagnitude <= 0.0001f) return Vector3.ClampMagnitude(raw, 1f);
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            return Vector3.ClampMagnitude((right * move.x) + (forward * move.y), 1f);
        }

        private void ActivateOwnerCamera()
        {
            if (_ownerCameraRig == null || Application.isBatchMode) return;
            _ownerCameraRig.Configure(transform, _input, _targeting);
            _ownerCameraRig.SetLookInputBlocked(_inputSuppressed.Value);
            _ownerCameraRig.gameObject.SetActive(true);
        }

        private static float NormalizeYaw(float yaw)
        {
            if (float.IsNaN(yaw) || float.IsInfinity(yaw)) return 0f;
            return Mathf.Repeat(yaw, 360f);
        }

        private static string ResolveInteractionFeedback(bool accepted, string reason)
        {
            if (accepted)
            {
                return reason switch
                {
                    "seal-activated" => "text:network.interaction.seal-activated",
                    "sanctum-opened" => "text:network.interaction.sanctum-opened",
                    "reward-claimed" => "text:network.interaction.reward-claimed",
                    "route-complete" => "text:network.interaction.route-complete",
                    _ => "text:network.interaction.unavailable"
                };
            }
            return reason switch
            {
                "seal-out-of-range" => "text:network.interaction.seal-too-far",
                "gate-out-of-range" => "text:network.interaction.gate-too-far",
                "reward-out-of-range" => "text:network.interaction.reward-too-far",
                "scout-out-of-range" => "text:network.interaction.scout-too-far",
                "warden-still-active" => "text:network.interaction.warden-active",
                "already-claimed" => "text:network.interaction.already-claimed",
                "world-not-ready" => "text:network.interaction.world-syncing",
                "stale-sequence" => "text:network.interaction.request-expired",
                _ => "text:network.interaction.unavailable"
            };
        }

        private static bool HasCommandLineFlag(string prefix)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (!arguments[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string value = arguments[i].Substring(prefix.Length).Trim();
                return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private static bool HasCommandLineOption(string prefix)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    arguments[i].Length > prefix.Length) return true;
            }
            return false;
        }
    }

}
