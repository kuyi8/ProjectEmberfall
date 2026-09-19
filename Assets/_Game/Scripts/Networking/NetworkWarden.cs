using System;
using System.Collections.Generic;
using Emberfall.AI.Data;
using Emberfall.AI.Domain;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using Unity.Netcode;
using UnityEngine;

namespace Emberfall.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkWarden : NetworkBehaviour
    {
        private const string DefaultWardenId = "boss:ember-warden";
        private const float TwoPlayerHealthMultiplier = 1.65f;
        private const float PlayerMeleeRange = 2.6f;
        private const float PlayerFacingDot = 0.05f;
        private const float EnemyFacingDot = -0.15f;

        [SerializeField] private TextAsset _enemyDefinitions;
        [SerializeField] private string _wardenId = DefaultWardenId;
        [SerializeField] private Renderer _stateMarker;
        [SerializeField] private Renderer _attackIndicator;
        [SerializeField] private Renderer _phaseIndicator;
        [SerializeField] private Animator _animator;
        [SerializeField] private Emberfall.Gameplay.Animation.PlayerAnimationSet _animationSet;

        private readonly NetworkVariable<Vector3> _serverPosition = new NetworkVariable<Vector3>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _serverYaw = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _state = new NetworkVariable<int>(
            (int)WardenState.Dormant, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _phase = new NetworkVariable<int>(
            (int)WardenPhase.PhaseOne, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _attackKind = new NetworkVariable<int>(
            (int)WardenAttackKind.SwordCombo, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _attackSequence = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _health = new NetworkVariable<float>(
            1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _maximumHealth = new NetworkVariable<float>(
            1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _posture = new NetworkVariable<float>(
            1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ulong> _targetClientId = new NetworkVariable<ulong>(
            ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _blastSequence = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector3> _blastCenter = new NetworkVariable<Vector3>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkWardenThreatModel _threat = new NetworkWardenThreatModel();
        private readonly List<NetworkWardenTargetCandidate> _targetCandidates =
            new List<NetworkWardenTargetCandidate>(2);
        private readonly Dictionary<ulong, int> _lastHitByTarget = new Dictionary<ulong, int>();
        private readonly Dictionary<int, int> _lastProjectileAttackBySource = new Dictionary<int, int>();
        private readonly List<DelayedBlast> _blasts = new List<DelayedBlast>();
        private NetworkGymSceneController _controller;
        private WardenDefinition _definition;
        private WardenBrain _brain;
        private NetworkGymPlayer _target;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Vector3 _committedChargeDirection;
        private float _chargeTravelRemaining;
        private bool _combatActive;
        private int _releasedBlastAttackSequence;
        private int _presentedBlastSequence;
        private int _lastAnimatorKey = int.MinValue;
        private bool _serverSawPhaseTransition;
        private bool _serverSawRuneCleave;
        private bool _serverSawDelayedBlast;
        private bool _serverResolvedDelayedBlast;

        public float Health => _health.Value;
        public float MaximumHealth => _maximumHealth.Value;
        public float HealthNormalized => _maximumHealth.Value > 0f ? _health.Value / _maximumHealth.Value : 0f;
        public float PostureNormalized => _posture.Value;
        public WardenState ReplicatedState => (WardenState)_state.Value;
        public WardenPhase ReplicatedPhase => (WardenPhase)_phase.Value;
        public WardenAttackKind ReplicatedAttack => (WardenAttackKind)_attackKind.Value;
        public int AttackSequence => _attackSequence.Value;
        public ulong TargetClientId => _targetClientId.Value;
        public bool IsAlive => _health.Value > 0f;
        internal int ServerCombatantId => unchecked((int)(NetworkObjectId & 0x3FFFFFFF)) + 10000;
        internal bool ServerSawPhaseTransition => _serverSawPhaseTransition;
        internal bool ServerSawRuneCleave => _serverSawRuneCleave;
        internal bool ServerSawDelayedBlast => _serverSawDelayedBlast;
        internal bool ServerResolvedDelayedBlast => _serverResolvedDelayedBlast;

        public void Configure(
            TextAsset enemyDefinitions,
            string wardenId,
            Renderer stateMarker,
            Renderer attackIndicator,
            Renderer phaseIndicator,
            Animator animator,
            Emberfall.Gameplay.Animation.PlayerAnimationSet animationSet)
        {
            _enemyDefinitions = enemyDefinitions;
            _wardenId = string.IsNullOrWhiteSpace(wardenId) ? DefaultWardenId : wardenId;
            _stateMarker = stateMarker;
            _attackIndicator = attackIndicator;
            _phaseIndicator = phaseIndicator;
            _animator = animator;
            _animationSet = animationSet;
        }

        public override void OnNetworkSpawn()
        {
            _controller = NetworkGymSceneController.Find();
            _controller?.Register(this);
            if (IsServer)
            {
                _spawnPosition = transform.position;
                _spawnRotation = transform.rotation;
                InitializeServerDomain();
            }
            ApplyPresentation();
            Debug.Log($"[M5_NETWORK_WARDEN_SPAWNED] server={IsServer} objectId={NetworkObjectId}");
        }

        public override void OnNetworkDespawn() => _controller?.Unregister(this);

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) TickServer();
            else InterpolateRemotePose();
            ApplyPresentation();
        }

        internal void ServerSetCombatActive()
        {
            if (IsServer) _combatActive = true;
        }

        internal void ServerResetEncounter()
        {
            if (!IsServer || _brain == null) return;
            _brain.Reset();
            _threat.Reset(ServerNow);
            _lastHitByTarget.Clear();
            _lastProjectileAttackBySource.Clear();
            _blasts.Clear();
            _chargeTravelRemaining = 0f;
            _releasedBlastAttackSequence = 0;
            _serverSawPhaseTransition = false;
            _serverSawRuneCleave = false;
            _serverSawDelayedBlast = false;
            _serverResolvedDelayedBlast = false;
            _target = null;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            PublishServerState();
            Debug.Log("[M5_WARDEN_RESET] authority=server");
        }

        internal bool ServerApplySmokeDamage(NetworkGymPlayer sourcePlayer, float damage, int sequence)
        {
            if (!IsServer || _brain == null || sourcePlayer == null || !Debug.isDebugBuild) return false;
            DamageResult result = _brain.ReceiveDamage(new DamageRequest(
                sourcePlayer.ServerCombatantId,
                sequence,
                damage,
                0f,
                AttackTag.Heavy), false);
            if (result.AppliedDamage > 0f)
                _threat.RecordDamage(sourcePlayer.OwnerClientId, result.AppliedDamage, ServerNow);
            PublishServerState();
            if (result.Killed) _controller?.ServerHandleWardenDefeated(this);
            Debug.Log($"[M5_WARDEN_SMOKE_DAMAGE] sequence={sequence} applied={result.AppliedDamage:F1} " +
                      $"phase={_brain.Phase} killed={result.Killed}");
            return result.Accepted || result.Killed;
        }

        internal bool ServerReceivePlayerAttack(NetworkGymPlayer sourcePlayer, CombatStateMachine combat)
        {
            if (!IsServer || _brain == null || !IsAlive || sourcePlayer == null || combat == null ||
                !combat.IsDamageWindowOpen) return false;
            if (!NetworkCombatSpatialValidator.IsValidMeleeHit(
                    sourcePlayer.transform.position.x, sourcePlayer.transform.position.z,
                    sourcePlayer.transform.forward.x, sourcePlayer.transform.forward.z,
                    transform.position.x, transform.position.z,
                    PlayerMeleeRange, PlayerFacingDot)) return false;

            float posture = combat.CurrentAttackTag == AttackTag.Heavy ? 60f :
                combat.State == CombatState.LightAttack3 ? 38f : 18f;
            posture += combat.CurrentAttackPostureBonus;
            DamageResult result = ReceivePlayerDamage(sourcePlayer, new DamageRequest(
                sourcePlayer.ServerCombatantId,
                combat.AttackSequence,
                combat.CurrentAttackDamage,
                posture,
                combat.CurrentAttackTag));
            return result.Accepted || result.Blocked;
        }

        internal bool ServerReceivePlayerProjectile(
            NetworkGymPlayer sourcePlayer,
            RangedAttackRelease release,
            Vector3 direction,
            out string reason)
        {
            if (!IsServer || _brain == null || !IsAlive || sourcePlayer == null)
            {
                reason = "warden-unavailable";
                return false;
            }
            if (_lastProjectileAttackBySource.TryGetValue(sourcePlayer.ServerCombatantId, out int prior) &&
                release.AttackSequence <= prior)
            {
                reason = "duplicate-projectile-sequence";
                return false;
            }

            Vector3 planar = new Vector3(direction.x, 0f, direction.z);
            if (planar.sqrMagnitude <= 0.0001f)
            {
                reason = "invalid-projectile-direction";
                return false;
            }
            planar.Normalize();
            Vector3 source = sourcePlayer.transform.position;
            if (!NetworkCombatSpatialValidator.TryEvaluateProjectileSweep(
                    source.x, source.z, planar.x, planar.z,
                    transform.position.x, transform.position.z,
                    release.MaximumDistance, 0.7f, out _))
            {
                reason = "projectile-missed-warden";
                return false;
            }

            _lastProjectileAttackBySource[sourcePlayer.ServerCombatantId] = release.AttackSequence;
            DamageResult result = ReceivePlayerDamage(sourcePlayer, new DamageRequest(
                sourcePlayer.ServerCombatantId,
                release.AttackSequence,
                release.Damage,
                release.PostureDamage,
                AttackTag.Projectile));
            reason = result.Accepted || result.Blocked ? string.Empty : "warden-damage-rejected";
            return string.IsNullOrEmpty(reason);
        }

        private DamageResult ReceivePlayerDamage(NetworkGymPlayer sourcePlayer, DamageRequest request)
        {
            Vector3 toAttacker = sourcePlayer.transform.position - transform.position;
            toAttacker.y = 0f;
            bool frontal = toAttacker.sqrMagnitude > 0.001f &&
                Vector3.Angle(transform.forward, toAttacker) <= _definition.FrontalBlockAngle * 0.5f;
            DamageResult result = _brain.ReceiveDamage(request, frontal);
            float threatAmount = result.AppliedDamage + result.PostureDamageApplied * 0.08f;
            if (threatAmount > 0f) _threat.RecordDamage(sourcePlayer.OwnerClientId, threatAmount, ServerNow);
            PublishServerState();
            if (result.Accepted || result.Blocked)
                Debug.Log($"[M5_WARDEN_DAMAGE] source={sourcePlayer.OwnerClientId} applied={result.AppliedDamage:F1} " +
                          $"blocked={result.Blocked} phase={_brain.Phase} health={_brain.Health.Current:F1}");
            if (result.Killed) _controller?.ServerHandleWardenDefeated(this);
            return result;
        }

        private void InitializeServerDomain()
        {
            string json = ContentPackageRuntime.IsInitialized
                ? ContentPackageRuntime.GetRequiredText(RuntimeContentPaths.Enemies)
                : (_enemyDefinitions != null ? _enemyDefinitions.text : string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogError("[M5_NETWORK_WARDEN] Enemy definitions are unavailable.", this);
                enabled = false;
                return;
            }

            _definition = WardenDefinitionJsonLoader.Load(json).GetRequired(new ContentId(_wardenId));
            float multiplier = _controller != null && _controller.ConnectedPlayerCount >= 2
                ? TwoPlayerHealthMultiplier
                : 1f;
            _brain = new WardenBrain(_definition, multiplier);
            PublishServerState();
            Debug.Log($"[M5_WARDEN_HEALTH_SCALE] players={_controller?.ConnectedPlayerCount ?? 0} " +
                      $"multiplier={multiplier:F2} maximum={_brain.Health.Maximum:F1}");
        }

        private void TickServer()
        {
            if (_brain == null) return;
            SelectServerTarget();
            bool targetAvailable = _target != null && !_target.IsDead && !_target.IsDowned;
            float targetDistance = targetAvailable
                ? PlanarDistance(transform.position, _target.transform.position)
                : float.MaxValue;
            int previousAttackSequence = _brain.AttackSequence;
            _brain.Tick(Time.deltaTime, new MeleeEnemyPerception(
                targetAvailable,
                targetAvailable,
                targetDistance,
                PlanarDistance(transform.position, _spawnPosition),
                true), _combatActive);
            if (_brain.State == WardenState.PhaseTransition) _serverSawPhaseTransition = true;

            if (targetAvailable && _brain.WantsFaceTarget) FaceTarget(_target.transform.position);
            if (targetAvailable && _brain.WantsTargetMovement)
                MoveTowards(_target.transform.position, _definition.MoveSpeed * _brain.MoveSpeedMultiplier);
            else if (_brain.WantsReturnMovement)
                MoveTowards(_spawnPosition, _definition.MoveSpeed);

            if (_brain.AttackSequence != previousAttackSequence)
            {
                if (_brain.CurrentAttack == WardenAttackKind.Charge && targetAvailable)
                {
                    Vector3 direction = _target.transform.position - transform.position;
                    direction.y = 0f;
                    _committedChargeDirection = direction.sqrMagnitude > 0.001f
                        ? direction.normalized
                        : transform.forward;
                    _chargeTravelRemaining = _definition.ChargeTravelDistance;
                }
                if (_brain.CurrentAttack == WardenAttackKind.DelayedBlast && targetAvailable)
                    ReleaseDelayedBlast(_target.transform.position, _brain.AttackSequence);
                if (_brain.CurrentAttack == WardenAttackKind.RuneCleave) _serverSawRuneCleave = true;
                if (_brain.CurrentAttack == WardenAttackKind.DelayedBlast) _serverSawDelayedBlast = true;
            }

            if (_brain.State == WardenState.Attack && _brain.CurrentAttack == WardenAttackKind.Charge &&
                _chargeTravelRemaining > 0f)
            {
                float step = Mathf.Min(_chargeTravelRemaining, _definition.ChargeSpeed * Time.deltaTime);
                transform.position = _controller != null
                    ? _controller.ConstrainToPlayableBounds(transform.position + _committedChargeDirection * step)
                    : transform.position + _committedChargeDirection * step;
                _chargeTravelRemaining -= step;
            }

            if (_brain.IsDamageWindowOpen) ResolveAttackHits();
            TickDelayedBlasts();
            PublishServerState();
        }

        private void SelectServerTarget()
        {
            _targetCandidates.Clear();
            if (_controller != null)
            {
                foreach (NetworkGymPlayer player in _controller.GetAlivePlayers())
                    _targetCandidates.Add(new NetworkWardenTargetCandidate(
                        player.OwnerClientId,
                        PlanarDistance(transform.position, player.transform.position)));
            }
            ulong selected = _threat.SelectTarget(_targetCandidates, ServerNow);
            _target = selected == ulong.MaxValue ? null : _controller?.FindPlayerByClientId(selected);
            _targetClientId.Value = selected;
        }

        private void ResolveAttackHits()
        {
            if (_controller == null || _brain.CurrentAttack == WardenAttackKind.DelayedBlast) return;
            int hitKey = (_brain.AttackSequence * 10) + _brain.CurrentHitIndex;
            foreach (NetworkGymPlayer player in _controller.GetAlivePlayers())
            {
                if (_lastHitByTarget.TryGetValue(player.OwnerClientId, out int previous) && previous == hitKey)
                    continue;
                float range = _definition.SwordCombo.MaximumRange * _brain.CurrentHitRadiusMultiplier + 0.55f;
                if (PlanarDistance(transform.position, player.transform.position) > range) continue;
                if (_brain.CurrentAttack == WardenAttackKind.RuneCleave && !CanHitRuneCleave(player)) continue;
                if (_brain.CurrentAttack != WardenAttackKind.RuneCleave &&
                    !NetworkCombatSpatialValidator.IsValidMeleeHit(
                        transform.position.x, transform.position.z,
                        transform.forward.x, transform.forward.z,
                        player.transform.position.x, player.transform.position.z,
                        range, EnemyFacingDot)) continue;

                _lastHitByTarget[player.OwnerClientId] = hitKey;
                bool front = NetworkCombatSpatialValidator.IsThreatInFrontArc(
                    player.transform.position.x, player.transform.position.z,
                    player.transform.forward.x, player.transform.forward.z,
                    transform.position.x, transform.position.z);
                DamageResult result = player.ServerReceiveDamage(new DamageRequest(
                    ServerCombatantId,
                    hitKey,
                    _brain.CurrentAttackDamage,
                    _brain.CurrentPostureDamage,
                    _brain.CurrentAttack == WardenAttackKind.SwordCombo ? AttackTag.Light : AttackTag.Heavy,
                    true,
                    front));
                if (result.PerfectGuard && result.CounterPostureDamage > 0f)
                    _brain.ApplyCounterPosture(result.CounterPostureDamage);
                if (_brain.CurrentAttack == WardenAttackKind.Charge) _chargeTravelRemaining = 0f;
                Debug.Log($"[M5_WARDEN_ATTACK] target={player.OwnerClientId} kind={_brain.CurrentAttack} " +
                          $"sequence={hitKey} applied={result.AppliedDamage:F1} authority=server");
            }
        }

        private bool CanHitRuneCleave(NetworkGymPlayer player)
        {
            Vector3 direction = player.transform.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f ||
                Vector3.Angle(transform.forward, direction) > _definition.RuneCleaveAngle * 0.5f)
                return false;
            return HasLineOfSight(player);
        }

        private bool HasLineOfSight(NetworkGymPlayer player)
        {
            Vector3 origin = transform.position + Vector3.up;
            Vector3 target = player.transform.position + Vector3.up * 0.9f;
            Vector3 delta = target - origin;
            RaycastHit[] hits = Physics.RaycastAll(
                origin, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hit = hits[i].collider;
                if (hit == null || hit.transform.IsChildOf(transform)) continue;
                return hit.GetComponentInParent<NetworkGymPlayer>() == player;
            }
            return true;
        }

        private void ReleaseDelayedBlast(Vector3 targetPosition, int sequence)
        {
            if (_releasedBlastAttackSequence == sequence) return;
            _releasedBlastAttackSequence = sequence;
            targetPosition.y = transform.position.y + 0.04f;
            _blasts.Add(new DelayedBlast(
                targetPosition,
                _definition.DelayedBlastFuse,
                sequence,
                _brain.CurrentAttackDamage,
                _brain.CurrentPostureDamage));
            _blastCenter.Value = targetPosition;
            _blastSequence.Value = sequence;
            Debug.Log($"[M5_WARDEN_BLAST_ARMED] sequence={sequence} authority=server");
        }

        private void TickDelayedBlasts()
        {
            for (int i = _blasts.Count - 1; i >= 0; i--)
            {
                DelayedBlast blast = _blasts[i];
                blast.Remaining -= Time.deltaTime;
                if (blast.Remaining > 0f) continue;
                foreach (NetworkGymPlayer player in _controller.GetAlivePlayers())
                {
                    if (PlanarDistance(player.transform.position, blast.Center) > _definition.DelayedBlastRadius)
                        continue;
                    bool front = NetworkCombatSpatialValidator.IsThreatInFrontArc(
                        player.transform.position.x, player.transform.position.z,
                        player.transform.forward.x, player.transform.forward.z,
                        blast.Center.x, blast.Center.z);
                    player.ServerReceiveDamage(new DamageRequest(
                        ServerCombatantId,
                        blast.Sequence,
                        blast.Damage,
                        blast.PostureDamage,
                        AttackTag.Projectile,
                        true,
                        front));
                }
                _blasts.RemoveAt(i);
                _serverResolvedDelayedBlast = true;
                Debug.Log($"[M5_WARDEN_BLAST_RESOLVED] sequence={blast.Sequence} authority=server");
            }
        }

        private void FaceTarget(Vector3 targetPosition)
        {
            Vector3 direction = targetPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction.normalized, Vector3.up),
                _definition.RotationSpeed * Time.deltaTime);
        }

        private void MoveTowards(Vector3 targetPosition, float speed)
        {
            Vector3 destination = new Vector3(targetPosition.x, transform.position.y, targetPosition.z);
            Vector3 next = Vector3.MoveTowards(transform.position, destination, speed * Time.deltaTime);
            transform.position = _controller != null ? _controller.ConstrainToPlayableBounds(next) : next;
        }

        private void PublishServerState()
        {
            if (_brain == null) return;
            _serverPosition.Value = transform.position;
            _serverYaw.Value = transform.eulerAngles.y;
            _state.Value = (int)_brain.State;
            _phase.Value = (int)_brain.Phase;
            _attackKind.Value = (int)_brain.CurrentAttack;
            _attackSequence.Value = _brain.AttackSequence;
            _health.Value = _brain.Health.Current;
            _maximumHealth.Value = _brain.Health.Maximum;
            _posture.Value = _brain.GuardNormalized;
        }

        private void InterpolateRemotePose()
        {
            transform.position = Vector3.Lerp(transform.position, _serverPosition.Value, 14f * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.Euler(0f, _serverYaw.Value, 0f),
                14f * Time.deltaTime);
        }

        private void ApplyPresentation()
        {
            bool dead = ReplicatedState == WardenState.Dead;
            bool attack = ReplicatedState == WardenState.Attack;
            bool transition = ReplicatedState == WardenState.PhaseTransition;
            if (_attackIndicator != null)
            {
                _attackIndicator.enabled = attack || ReplicatedState == WardenState.Windup;
                var block = new MaterialPropertyBlock();
                Color attackColor = ReplicatedAttack == WardenAttackKind.DelayedBlast
                    ? new Color(0.68f, 0.08f, 1f)
                    : ReplicatedAttack == WardenAttackKind.RuneCleave
                        ? new Color(1f, 0.15f, 0.04f)
                        : new Color(1f, 0.55f, 0.04f);
                block.SetColor("_BaseColor", attackColor);
                block.SetColor("_EmissionColor", attackColor * 1.8f);
                _attackIndicator.SetPropertyBlock(block);
            }
            if (_phaseIndicator != null) _phaseIndicator.enabled = transition || ReplicatedPhase == WardenPhase.PhaseTwo;
            if (_stateMarker != null)
            {
                Color color = dead ? Color.black : transition ? new Color(0.62f, 0.08f, 1f) :
                    ReplicatedPhase == WardenPhase.PhaseTwo ? new Color(1f, 0.12f, 0.04f) :
                    new Color(0.9f, 0.34f, 0.06f);
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", color);
                block.SetColor("_EmissionColor", color * 1.35f);
                _stateMarker.SetPropertyBlock(block);
            }
            PresentBlast();
            PresentAnimation();
        }

        private void PresentBlast()
        {
            if (_blastSequence.Value <= 0 || _presentedBlastSequence == _blastSequence.Value) return;
            _presentedBlastSequence = _blastSequence.Value;
            NetworkEnemyAttackPresentation.Spawn(
                RangedAttackKind.GroundRune,
                _blastCenter.Value,
                new Vector3(_definition != null ? _definition.DelayedBlastRadius : 2.8f, 0f, 0f),
                0f,
                0f,
                _definition != null ? _definition.DelayedBlastFuse : 1.3f,
                _stateMarker != null ? _stateMarker.sharedMaterial : null);
        }

        private void PresentAnimation()
        {
            if (_animator == null || _animationSet == null || _animationSet.Controller == null) return;
            if (_animator.runtimeAnimatorController != _animationSet.Controller)
            {
                _animator.runtimeAnimatorController = _animationSet.Controller;
                _animator.applyRootMotion = false;
            }
            int key = ((int)ReplicatedState * 10) + (int)ReplicatedAttack;
            if (key == _lastAnimatorKey) return;
            _lastAnimatorKey = key;
            string stateName = ReplicatedState switch
            {
                WardenState.Dead => CombatState.Dead.ToString(),
                WardenState.GuardBreak => CombatState.HitReact.ToString(),
                WardenState.PhaseTransition => CombatState.HitReact.ToString(),
                WardenState.Windup => ResolveAttackAnimation(),
                WardenState.Attack => ResolveAttackAnimation(),
                _ => CombatState.Locomotion.ToString()
            };
            _animator.CrossFadeInFixedTime(stateName, 0.08f, 0, 0f);
        }

        private string ResolveAttackAnimation() => ReplicatedAttack switch
        {
            WardenAttackKind.ShieldBash => "EnemyShieldBash",
            WardenAttackKind.Charge => CombatState.Dodge.ToString(),
            WardenAttackKind.DelayedBlast => "EnemyRuneCast",
            WardenAttackKind.RuneCleave => CombatState.HeavyAttack.ToString(),
            _ => CombatState.LightAttack1.ToString()
        };

        private double ServerNow => NetworkManager != null ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x;
            float z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        private sealed class DelayedBlast
        {
            public DelayedBlast(
                Vector3 center,
                float remaining,
                int sequence,
                float damage,
                float postureDamage)
            {
                Center = center;
                Remaining = remaining;
                Sequence = sequence;
                Damage = damage;
                PostureDamage = postureDamage;
            }

            public readonly Vector3 Center;
            public float Remaining;
            public readonly int Sequence;
            public readonly float Damage;
            public readonly float PostureDamage;
        }
    }
}
