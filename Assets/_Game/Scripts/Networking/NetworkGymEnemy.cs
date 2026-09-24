using System;
using System.Collections.Generic;
using Emberfall.AI.Data;
using Emberfall.AI.Domain;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Unity;
using Unity.Netcode;
using UnityEngine;

namespace Emberfall.Networking
{
    public enum NetworkEnemyArchetype
    {
        Fogwalker = 0,
        RunePriest = 1,
        RuinGuard = 2
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkGymEnemy : NetworkBehaviour
    {
        private const float EnemyFacingDot = -0.1f;
        private const float ProjectileHitRadius = 0.56f;
        private const float ProjectileCastRadius = 0.08f;

        [SerializeField] private TextAsset _enemyDefinitions;
        [SerializeField] private NetworkEnemyArchetype _archetype;
        [SerializeField] private string _enemyId = "enemy:fogwalker";
        [SerializeField] private Renderer _stateMarker;
        [SerializeField] private Renderer _attackIndicator;
        [SerializeField] private Animator _animator;
        [SerializeField] private Emberfall.Gameplay.Animation.PlayerAnimationSet _animationSet;

        private readonly NetworkVariable<Vector3> _serverPosition = new NetworkVariable<Vector3>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _serverYaw = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _state = new NetworkVariable<int>(
            (int)MeleeEnemyState.Idle, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _health = new NetworkVariable<float>(
            1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _maximumHealth = new NetworkVariable<float>(
            1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _attackSequence = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _attackKind = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> _secondaryResource = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _releaseSequence = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector3> _releaseOrigin = new NetworkVariable<Vector3>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector3> _releaseVector = new NetworkVariable<Vector3>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MeleeEnemyDefinition _definition;
        private MeleeEnemyBrain _brain;
        private RangedEnemyDefinition _rangedDefinition;
        private RangedEnemyBrain _rangedBrain;
        private ShieldEnemyDefinition _shieldDefinition;
        private ShieldEnemyBrain _shieldBrain;
        private NetworkGymSceneController _controller;
        private Vector3 _spawnPosition;
        private bool _combatActive;
        private int _lastResolvedHitSequence = -1;
        private int _lastPresentationState = -1;
        private int _presentedReleaseSequence;
        private bool _attackAllowed = true;
        private int _serverProjectileReleaseCount;
        private int _serverGroundRuneReleaseCount;
        private int _serverScorchedBurstReleaseCount;
        private int _serverShieldBlockedHitCount;
        private readonly List<ServerProjectile> _serverProjectiles = new List<ServerProjectile>();
        private readonly List<ServerGroundRune> _serverGroundRunes = new List<ServerGroundRune>();
        private readonly Dictionary<int, int> _lastProjectileAttackBySource =
            new Dictionary<int, int>();

        public float Health => _health.Value;
        public float MaximumHealth => _maximumHealth.Value;
        public MeleeEnemyState ReplicatedState => (MeleeEnemyState)_state.Value;
        public NetworkEnemyArchetype Archetype => _archetype;
        public float SecondaryResourceNormalized => _secondaryResource.Value;
        public string ReplicatedStateLabel => GetStateLabel(_state.Value);
        public bool IsAlive => _health.Value > 0f;
        internal int ServerCombatantId => unchecked((int)(NetworkObjectId & 0x3FFFFFFF)) + 1;
        internal bool ServerIsResetReady => IsServer && IsResetReady();
        internal bool ServerUsesMeleeQuota => _archetype != NetworkEnemyArchetype.RunePriest;
        internal bool ServerQuotaCommitted => IsQuotaCommitted();
        internal bool ServerQuotaEligible => IsQuotaEligible();
        internal bool ServerHasRangedAttackCoverage =>
            _serverProjectileReleaseCount > 0 && _serverGroundRuneReleaseCount > 0;
        internal int ServerShieldBlockedHitCount => _serverShieldBlockedHitCount;
        internal int ServerScorchedBurstReleaseCount => _serverScorchedBurstReleaseCount;

        public void Configure(
            TextAsset enemyDefinitions,
            NetworkEnemyArchetype archetype,
            string enemyId,
            Renderer stateMarker,
            Renderer attackIndicator,
            Animator animator = null,
            Emberfall.Gameplay.Animation.PlayerAnimationSet animationSet = null)
        {
            _enemyDefinitions = enemyDefinitions;
            _archetype = archetype;
            _enemyId = enemyId;
            _stateMarker = stateMarker;
            _attackIndicator = attackIndicator;
            _animator = animator;
            _animationSet = animationSet;
        }

        public override void OnNetworkSpawn()
        {
            _health.OnValueChanged += OnReplicatedHealthChanged;
            _controller = NetworkGymSceneController.Find();
            _controller?.Register(this);
            if (IsServer)
            {
                _spawnPosition = transform.position;
                InitializeServerDomain();
            }
            ApplyPresentation(true);
            Debug.Log($"[M5_NETWORK_ENEMY_SPAWNED] server={IsServer} objectId={NetworkObjectId}");
        }

        public override void OnNetworkDespawn()
        {
            _health.OnValueChanged -= OnReplicatedHealthChanged;
            _controller?.Unregister(this);
        }

        private void OnReplicatedHealthChanged(float previous, float current)
        {
            if (!IsServer)
                Debug.Log($"[M5_ENEMY_HEALTH_SYNC] previous={previous:F1} current={current:F1}");
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) TickServer();
            else InterpolateRemotePose();
            ApplyPresentation(false);
        }

        private void InitializeServerDomain()
        {
            string json = ContentPackageRuntime.IsInitialized
                ? ContentPackageRuntime.GetRequiredText(RuntimeContentPaths.Enemies)
                : (_enemyDefinitions != null ? _enemyDefinitions.text : string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogError("[M5_NETWORK_ENEMY] Enemy definitions are unavailable.", this);
                enabled = false;
                return;
            }

            ContentId id = new ContentId(_enemyId);
            switch (_archetype)
            {
                case NetworkEnemyArchetype.RunePriest:
                    _rangedDefinition = RangedEnemyDefinitionJsonLoader.Load(json).GetRequired(id);
                    _rangedBrain = new RangedEnemyBrain(_rangedDefinition);
                    break;
                case NetworkEnemyArchetype.RuinGuard:
                    _shieldDefinition = ShieldEnemyDefinitionJsonLoader.Load(json).GetRequired(id);
                    _shieldBrain = new ShieldEnemyBrain(_shieldDefinition);
                    break;
                default:
                    _definition = MeleeEnemyDefinitionJsonLoader.Load(json).GetRequired(id);
                    _brain = new MeleeEnemyBrain(_definition);
                    break;
            }
            _serverPosition.Value = transform.position;
            _serverYaw.Value = transform.eulerAngles.y;
            PublishServerState();
        }

        private void TickServer()
        {
            if (!HasServerBrain()) return;
            if (!_combatActive)
            {
                PublishServerState();
                return;
            }

            if (_archetype == NetworkEnemyArchetype.RunePriest)
            {
                TickRangedServer();
                TickServerProjectiles();
                TickServerGroundRunes();
                PublishPoseAndState();
                return;
            }

            if (_archetype == NetworkEnemyArchetype.RuinGuard)
            {
                TickShieldServer();
                TickServerGroundRunes();
                PublishPoseAndState();
                return;
            }

            NetworkGymPlayer target = _controller != null
                ? _controller.GetNearestAlivePlayer(transform.position)
                : null;
            bool targetAvailable = target != null;
            float distanceToTarget = targetAvailable
                ? PlanarDistance(transform.position, target.transform.position)
                : float.MaxValue;
            float distanceToSpawn = PlanarDistance(transform.position, _spawnPosition);
            _brain.Tick(Time.deltaTime, new MeleeEnemyPerception(
                targetAvailable,
                targetAvailable,
                distanceToTarget,
                distanceToSpawn,
                _attackAllowed));

            if (targetAvailable)
            {
                if (_brain.WantsFaceTarget) FaceTarget(target.transform.position);
                if (_brain.WantsTargetMovement) MoveTowards(target.transform.position, _definition.MoveSpeed);
            }
            else if (_brain.WantsReturnMovement)
            {
                FaceTarget(_spawnPosition);
                MoveTowards(_spawnPosition, _definition.MoveSpeed);
            }

            if (_brain.IsDamageWindowOpen && _brain.CurrentHitSequence != _lastResolvedHitSequence)
                TryResolveEnemyHit(target);

            PublishPoseAndState();
        }

        private void TickRangedServer()
        {
            NetworkGymPlayer target = _controller != null
                ? _controller.GetNearestAlivePlayer(transform.position)
                : null;
            bool available = target != null;
            float distance = available ? PlanarDistance(transform.position, target.transform.position) : float.MaxValue;
            int previousSequence = _rangedBrain.AttackSequence;
            _rangedBrain.Tick(Time.deltaTime, new RangedEnemyPerception(
                available,
                available,
                distance,
                PlanarDistance(transform.position, _spawnPosition)));

            if (available && _rangedBrain.WantsFaceTarget) FaceTarget(target.transform.position, _rangedDefinition.RotationSpeed);
            if (available && _rangedBrain.WantsTargetMovement)
                MoveTowards(target.transform.position, _rangedDefinition.MoveSpeed);
            else if (available && _rangedBrain.WantsRetreatMovement)
                MoveAwayFrom(target.transform.position, _rangedDefinition.MoveSpeed);
            else if (_rangedBrain.WantsReturnMovement)
                MoveTowards(_spawnPosition, _rangedDefinition.MoveSpeed);

            if (_rangedBrain.AttackSequence != previousSequence && target != null)
                ReleaseRangedAttack(target);
        }

        private void TickShieldServer()
        {
            NetworkGymPlayer target = _controller != null
                ? _controller.GetNearestAlivePlayer(transform.position)
                : null;
            bool available = target != null;
            float distance = available ? PlanarDistance(transform.position, target.transform.position) : float.MaxValue;
            int previousSequence = _shieldBrain.AttackSequence;
            _shieldBrain.Tick(Time.deltaTime, new MeleeEnemyPerception(
                available,
                available,
                distance,
                PlanarDistance(transform.position, _spawnPosition),
                _attackAllowed));

            if (available && _shieldBrain.WantsFaceTarget)
                FaceTarget(target.transform.position, _shieldDefinition.RotationSpeed);
            if (available && _shieldBrain.WantsTargetMovement)
                MoveTowards(target.transform.position, _shieldDefinition.MoveSpeed);
            else if (_shieldBrain.WantsReturnMovement)
                MoveTowards(_spawnPosition, _shieldDefinition.MoveSpeed);

            if (_shieldBrain.IsDamageWindowOpen && _shieldBrain.AttackSequence != _lastResolvedHitSequence)
                TryResolveShieldHit(target);
            if (_shieldBrain.AttackSequence != previousSequence &&
                _shieldBrain.CurrentAttack == ShieldAttackKind.ScorchedBurst)
                ReleaseScorchedBurst();
        }

        private void FaceTarget(Vector3 targetPosition)
        {
            FaceTarget(targetPosition, _definition.RotationSpeed);
        }

        private void FaceTarget(Vector3 targetPosition, float rotationSpeed)
        {
            Vector3 direction = targetPosition - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f) return;
            Quaternion target = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target, rotationSpeed * Time.deltaTime);
        }

        private void MoveTowards(Vector3 targetPosition, float speed)
        {
            Vector3 destination = new Vector3(targetPosition.x, transform.position.y, targetPosition.z);
            Vector3 next = Vector3.MoveTowards(transform.position, destination, speed * Time.deltaTime);
            transform.position = _controller != null ? _controller.ConstrainToPlayableBounds(next) : next;
        }

        private void MoveAwayFrom(Vector3 targetPosition, float speed)
        {
            Vector3 direction = transform.position - targetPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f) direction = -transform.forward;
            MoveTowards(transform.position + direction.normalized * 2f, speed);
        }

        private void TryResolveEnemyHit(NetworkGymPlayer target)
        {
            if (target == null || target.IsDead) return;
            Vector3 source = transform.position;
            Vector3 forward = transform.forward;
            Vector3 targetPosition = target.transform.position;
            float range = (_definition.AttackRange * _brain.CurrentHitRadiusMultiplier) + 0.45f;
            if (!NetworkCombatSpatialValidator.IsValidMeleeHit(
                    source.x, source.z, forward.x, forward.z,
                    targetPosition.x, targetPosition.z, range, EnemyFacingDot)) return;

            _lastResolvedHitSequence = _brain.CurrentHitSequence;
            Vector3 targetForward = target.transform.forward;
            bool inDefenderFrontArc = NetworkCombatSpatialValidator.IsThreatInFrontArc(
                targetPosition.x,
                targetPosition.z,
                targetForward.x,
                targetForward.z,
                source.x,
                source.z);
            DamageResult result = target.ServerReceiveDamage(new DamageRequest(
                unchecked((int)(NetworkObjectId & int.MaxValue)),
                _brain.CurrentHitSequence,
                _brain.CurrentAttackDamage,
                _brain.CurrentPostureDamage,
                AttackTag.Light,
                true,
                inDefenderFrontArc));
            if (result.PerfectGuard && result.CounterPostureDamage > 0f)
            {
                bool staggered = _brain.ApplyCounterPosture(result.CounterPostureDamage);
                PublishServerState();
                Debug.Log(
                    $"[M5_PERFECT_GUARD_COUNTER] defender={target.OwnerClientId} " +
                    $"posture={result.CounterPostureDamage:F1} enemyStaggered={staggered}");
            }
            if (result.Accepted || result.Invulnerable || result.Defended)
            {
                Debug.Log(
                    $"[M5_ENEMY_ATTACK_HIT] target={target.OwnerClientId} sequence={_brain.CurrentHitSequence} " +
                    $"applied={result.AppliedDamage:F1} evaded={result.Invulnerable} " +
                    $"defended={result.Defended} perfect={result.PerfectGuard} front={inDefenderFrontArc}");
            }
        }

        private void TryResolveShieldHit(NetworkGymPlayer target)
        {
            if (target == null || target.IsDead) return;
            float range = (_shieldDefinition.AttackRange * _shieldBrain.CurrentHitRadiusMultiplier) + 0.45f;
            if (!NetworkCombatSpatialValidator.IsValidMeleeHit(
                    transform.position.x, transform.position.z,
                    transform.forward.x, transform.forward.z,
                    target.transform.position.x, target.transform.position.z,
                    range, EnemyFacingDot)) return;

            _lastResolvedHitSequence = _shieldBrain.AttackSequence;
            Vector3 targetForward = target.transform.forward;
            bool inFront = NetworkCombatSpatialValidator.IsThreatInFrontArc(
                target.transform.position.x, target.transform.position.z,
                targetForward.x, targetForward.z,
                transform.position.x, transform.position.z);
            DamageResult result = target.ServerReceiveDamage(new DamageRequest(
                ServerCombatantId,
                _shieldBrain.AttackSequence,
                _shieldBrain.CurrentAttackDamage,
                _shieldBrain.CurrentPostureDamage,
                AttackTag.Heavy,
                true,
                inFront));
            if (result.PerfectGuard && result.CounterPostureDamage > 0f)
                _shieldBrain.ApplyCounterPosture(result.CounterPostureDamage);
            Debug.Log(
                $"[M5_RUIN_GUARD_ATTACK] target={target.OwnerClientId} " +
                $"sequence={_shieldBrain.AttackSequence} kind={_shieldBrain.CurrentAttack} " +
                $"applied={result.AppliedDamage:F1} defended={result.Defended} authority=server");
        }

        private void ReleaseRangedAttack(NetworkGymPlayer target)
        {
            Vector3 origin = transform.position + Vector3.up * 1.15f + transform.forward * 0.25f;
            int sequence = _rangedBrain.AttackSequence;
            if (_rangedBrain.CurrentAttack == RangedAttackKind.GroundRune)
            {
                _serverGroundRuneReleaseCount++;
                Vector3 center = ProjectToGround(target.transform.position);
                _serverGroundRunes.Add(new ServerGroundRune(
                    center,
                    _rangedDefinition.GroundRuneTriggerDelay,
                    sequence,
                    _rangedDefinition.GroundRuneRadius,
                    _rangedDefinition.GroundRuneDamage,
                    _rangedDefinition.GroundRunePostureDamage,
                    false));
                _releaseOrigin.Value = center;
                _releaseVector.Value = new Vector3(_rangedDefinition.GroundRuneRadius, 0f, 0f);
            }
            else
            {
                _serverProjectileReleaseCount++;
                Vector3 aim = target.transform.position + Vector3.up * 0.9f;
                Vector3 direction = (aim - origin).normalized;
                _serverProjectiles.Add(new ServerProjectile(
                    origin,
                    direction,
                    _rangedDefinition.ProjectileSpeed,
                    _rangedDefinition.ProjectileSpeed * _rangedDefinition.ProjectileLifetime,
                    sequence));
                _releaseOrigin.Value = origin;
                _releaseVector.Value = direction;
            }
            _releaseSequence.Value = sequence;
            Debug.Log(
                $"[M5_RUNE_PRIEST_RELEASE] sequence={sequence} kind={_rangedBrain.CurrentAttack} authority=server");
        }

        private void ReleaseScorchedBurst()
        {
            _serverScorchedBurstReleaseCount++;
            int sequence = _shieldBrain.AttackSequence;
            Vector3 center = ProjectToGround(transform.position);
            _serverGroundRunes.Add(new ServerGroundRune(
                center,
                _shieldDefinition.ScorchedBurstTriggerDelay,
                sequence,
                _shieldDefinition.ScorchedBurstRadius,
                _shieldDefinition.ScorchedBurstDamage,
                _shieldDefinition.ScorchedBurstPostureDamage,
                true));
            _releaseOrigin.Value = center;
            _releaseVector.Value = new Vector3(_shieldDefinition.ScorchedBurstRadius, 0f, 0f);
            _releaseSequence.Value = sequence;
            Debug.Log(
                $"[M5_SCORCHED_BURST_RELEASE] sequence={sequence} radius={_shieldDefinition.ScorchedBurstRadius:F2} authority=server");
        }

        private void TickServerProjectiles()
        {
            for (int i = _serverProjectiles.Count - 1; i >= 0; i--)
            {
                ServerProjectile projectile = _serverProjectiles[i];
                float distance = Mathf.Min(projectile.RemainingDistance, projectile.Speed * Time.deltaTime);
                NetworkGymPlayer hitPlayer = FindFirstPlayerInProjectileStep(
                    projectile.Position,
                    projectile.Direction,
                    distance,
                    _rangedDefinition.ProjectileRadius);
                if (hitPlayer != null)
                {
                    ResolveRangedDamage(
                        hitPlayer,
                        projectile.Sequence,
                        _rangedDefinition.AttackDamage,
                        _rangedDefinition.PostureDamage,
                        "projectile");
                    _serverProjectiles.RemoveAt(i);
                    continue;
                }

                projectile.Position += projectile.Direction * distance;
                projectile.RemainingDistance -= distance;
                if (projectile.RemainingDistance <= 0.001f) _serverProjectiles.RemoveAt(i);
            }
        }

        private void TickServerGroundRunes()
        {
            for (int i = _serverGroundRunes.Count - 1; i >= 0; i--)
            {
                ServerGroundRune rune = _serverGroundRunes[i];
                rune.Remaining -= Time.deltaTime;
                if (rune.Remaining > 0f) continue;

                foreach (NetworkGymPlayer player in _controller.GetAlivePlayers())
                {
                    if (PlanarDistance(player.transform.position, rune.Center) > rune.Radius)
                        continue;
                    if (rune.IsScorchedHazard)
                        ResolveScorchedBurstDamage(player, rune);
                    else
                        ResolveRangedDamage(
                            player,
                            rune.Sequence,
                            rune.Damage,
                            rune.PostureDamage,
                            "ground-rune");
                }
                _serverGroundRunes.RemoveAt(i);
            }
        }

        private NetworkGymPlayer FindFirstPlayerInProjectileStep(
            Vector3 origin,
            Vector3 direction,
            float distance,
            float radius)
        {
            NetworkGymPlayer nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (NetworkGymPlayer player in _controller.GetAlivePlayers())
            {
                Vector3 target = player.transform.position + Vector3.up * 0.9f;
                Vector3 offset = target - origin;
                float along = Vector3.Dot(offset, direction);
                if (along < 0f || along > distance || along >= nearestDistance) continue;
                Vector3 closest = origin + direction * along;
                if ((target - closest).sqrMagnitude > (radius + 0.42f) * (radius + 0.42f)) continue;
                if (!HasLineOfSightToPlayer(origin, target, player)) continue;
                nearest = player;
                nearestDistance = along;
            }
            return nearest;
        }

        private bool HasLineOfSightToPlayer(Vector3 origin, Vector3 target, NetworkGymPlayer player)
        {
            Vector3 delta = target - origin;
            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                delta.normalized,
                delta.magnitude,
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(transform)) continue;
                return collider.GetComponentInParent<NetworkGymPlayer>() == player;
            }
            return true;
        }

        private void ResolveRangedDamage(
            NetworkGymPlayer target,
            int sequence,
            float damage,
            float postureDamage,
            string sourceLabel)
        {
            Vector3 targetForward = target.transform.forward;
            bool inFront = NetworkCombatSpatialValidator.IsThreatInFrontArc(
                target.transform.position.x,
                target.transform.position.z,
                targetForward.x,
                targetForward.z,
                transform.position.x,
                transform.position.z);
            DamageResult result = target.ServerReceiveDamage(new DamageRequest(
                ServerCombatantId,
                sequence,
                damage,
                postureDamage,
                AttackTag.Projectile,
                true,
                inFront));
            if (result.Accepted || result.Invulnerable || result.Defended)
            {
                Debug.Log(
                    $"[M5_RUNE_PRIEST_HIT] source={sourceLabel} target={target.OwnerClientId} " +
                    $"sequence={sequence} applied={result.AppliedDamage:F1} evaded={result.Invulnerable} " +
                    $"defended={result.Defended} authority=server");
            }
        }

        private void ResolveScorchedBurstDamage(NetworkGymPlayer target, ServerGroundRune rune)
        {
            DamageResult result = target.ServerReceiveDamage(new DamageRequest(
                ServerCombatantId,
                (rune.Sequence * 10) + 9,
                rune.Damage,
                rune.PostureDamage,
                AttackTag.Hazard,
                false,
                false));
            if (result.Accepted || result.Invulnerable)
            {
                Debug.Log(
                    $"[M5_SCORCHED_BURST_HIT] target={target.OwnerClientId} sequence={rune.Sequence} " +
                    $"applied={result.AppliedDamage:F1} evaded={result.Invulnerable} authority=server");
            }
        }

        internal bool ServerReceivePlayerAttack(NetworkGymPlayer sourcePlayer, CombatStateMachine combat)
        {
            if (!IsServer || !HasServerBrain() || IsServerDead() ||
                sourcePlayer == null || combat == null || !combat.IsDamageWindowOpen) return false;

            Vector3 source = sourcePlayer.transform.position;
            Vector3 forward = sourcePlayer.transform.forward;
            Vector3 target = transform.position;
            if (!NetworkCombatSpatialValidator.IsValidPlayerMeleeHit(
                    source.x, source.z, forward.x, forward.z,
                    target.x, target.z)) return false;

            DamageResult result = ReceivePlayerDamage(new DamageRequest(
                sourcePlayer.ServerCombatantId,
                combat.AttackSequence,
                combat.CurrentAttackDamage,
                ResolvePlayerMeleePostureDamage(combat),
                combat.CurrentAttackTag), source);
            if (_archetype == NetworkEnemyArchetype.RuinGuard && result.Blocked)
                _serverShieldBlockedHitCount++;
            PublishServerState();
            if (result.Accepted || result.Blocked)
            {
                sourcePlayer.ServerPresentHit(NetworkObjectId, combat.AttackSequence, combat.CurrentAttackTag,
                    result, transform.position + Vector3.up, _archetype == NetworkEnemyArchetype.RuinGuard
                        ? ImpactSurface.Metal : ImpactSurface.Flesh);
                Debug.Log(
                    $"[M5_ENEMY_DAMAGE] source={sourcePlayer.OwnerClientId} sequence={combat.AttackSequence} " +
                    $"archetype={_archetype} applied={result.AppliedDamage:F1} blocked={result.Blocked} " +
                    $"health={GetServerHealth():F1} killed={result.Killed}");
            }
            return result.Accepted || result.Blocked;
        }

        internal bool ServerReceivePlayerProjectile(
            NetworkGymPlayer sourcePlayer,
            RangedAttackRelease release,
            Vector3 direction,
            out string reason)
        {
            if (!IsServer || !HasServerBrain() || IsServerDead() ||
                sourcePlayer == null)
            {
                reason = "projectile-target-unavailable";
                return false;
            }

            if (_lastProjectileAttackBySource.TryGetValue(
                    sourcePlayer.ServerCombatantId, out int lastSequence) &&
                release.AttackSequence <= lastSequence)
            {
                reason = "duplicate-projectile-sequence";
                return false;
            }

            Vector3 planarDirection = new Vector3(direction.x, 0f, direction.z);
            if (planarDirection.sqrMagnitude <= 0.0001f)
            {
                reason = "invalid-projectile-direction";
                return false;
            }
            planarDirection.Normalize();

            Vector3 source = sourcePlayer.transform.position;
            Vector3 target = transform.position;
            if (!NetworkCombatSpatialValidator.TryEvaluateProjectileSweep(
                    source.x,
                    source.z,
                    planarDirection.x,
                    planarDirection.z,
                    target.x,
                    target.z,
                    release.MaximumDistance,
                    ProjectileHitRadius,
                    out float targetDistance))
            {
                reason = "projectile-missed-target-sweep";
                return false;
            }

            Vector3 castOrigin = source + (Vector3.up * 0.95f);
            RaycastHit[] hits = Physics.SphereCastAll(
                castOrigin,
                ProjectileCastRadius,
                planarDirection,
                Mathf.Min(release.MaximumDistance, targetDistance + ProjectileHitRadius),
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            bool reachedTarget = false;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider hitCollider = hits[i].collider;
                if (hitCollider == null || hitCollider.transform.IsChildOf(sourcePlayer.transform)) continue;
                reachedTarget = hitCollider.GetComponentInParent<NetworkGymEnemy>() == this;
                break;
            }
            if (!reachedTarget)
            {
                reason = hits.Length == 0 ? "projectile-no-physics-contact" : "projectile-occluded";
                return false;
            }

            _lastProjectileAttackBySource[sourcePlayer.ServerCombatantId] = release.AttackSequence;
            DamageResult result = ReceivePlayerDamage(new DamageRequest(
                sourcePlayer.ServerCombatantId,
                release.AttackSequence,
                release.Damage,
                release.PostureDamage,
                AttackTag.Projectile), source);
            if (_archetype == NetworkEnemyArchetype.RuinGuard && result.Blocked)
                _serverShieldBlockedHitCount++;
            PublishServerState();
            bool resolved = result.Accepted || result.Blocked;
            reason = resolved ? string.Empty : "projectile-damage-rejected";
            if (resolved)
            {
                sourcePlayer.ServerPresentHit(NetworkObjectId, release.AttackSequence, AttackTag.Projectile,
                    result, transform.position + Vector3.up, _archetype == NetworkEnemyArchetype.RuinGuard
                        ? ImpactSurface.Metal : ImpactSurface.Flesh);
                Debug.Log(
                    $"[M5_RANGED_ENEMY_DAMAGE] source={sourcePlayer.OwnerClientId} " +
                    $"sequence={release.AttackSequence} applied={result.AppliedDamage:F1} " +
                    $"posture={result.PostureDamageApplied:F1} blocked={result.Blocked} " +
                    $"health={GetServerHealth():F1} killed={result.Killed}");
            }
            return resolved;
        }

        internal void ServerSetCombatActive()
        {
            if (IsServer) _combatActive = true;
        }

        internal void ServerSetAttackAllowed(bool allowed)
        {
            if (IsServer) _attackAllowed = allowed;
        }

        internal void ServerResetForRematch()
        {
            if (!IsServer || !HasServerBrain()) return;
            transform.SetPositionAndRotation(_spawnPosition, Quaternion.Euler(0f, 180f, 0f));
            switch (_archetype)
            {
                case NetworkEnemyArchetype.RunePriest: _rangedBrain.Reset(); break;
                case NetworkEnemyArchetype.RuinGuard: _shieldBrain.Reset(); break;
                default: _brain.Reset(); break;
            }
            _lastResolvedHitSequence = -1;
            _lastProjectileAttackBySource.Clear();
            _serverProjectiles.Clear();
            _serverGroundRunes.Clear();
            _serverProjectileReleaseCount = 0;
            _serverGroundRuneReleaseCount = 0;
            _serverScorchedBurstReleaseCount = 0;
            _serverShieldBlockedHitCount = 0;
            _serverPosition.Value = transform.position;
            _serverYaw.Value = transform.eulerAngles.y;
            PublishServerState();
            Debug.Log($"[M5_NETWORK_ENEMY_RESET] archetype={_archetype} authority=server");
        }

        internal bool ServerDefeatForEncounterVerification(NetworkGymPlayer sourcePlayer)
        {
            if (!IsServer || sourcePlayer == null || !HasServerBrain() || IsServerDead()) return false;
            Vector3 sourcePosition = transform.position - (transform.forward * 2f);
            DamageResult result = ReceivePlayerDamage(new DamageRequest(
                sourcePlayer.ServerCombatantId,
                88000 + _serverScorchedBurstReleaseCount,
                100000f,
                0f,
                AttackTag.Heavy), sourcePosition);
            PublishServerState();
            Debug.Log(
                $"[M5C_ENCOUNTER_VERIFY_DAMAGE] archetype={_archetype} killed={result.Killed} " +
                $"health={GetServerHealth():F1} authority=server");
            return result.Killed;
        }

        private void PublishServerState()
        {
            switch (_archetype)
            {
                case NetworkEnemyArchetype.RunePriest:
                    _state.Value = (int)_rangedBrain.State;
                    _health.Value = _rangedBrain.Health.Current;
                    _maximumHealth.Value = _rangedDefinition.MaximumHealth;
                    _attackSequence.Value = _rangedBrain.AttackSequence;
                    _attackKind.Value = (int)_rangedBrain.CurrentAttack;
                    _secondaryResource.Value = _rangedBrain.Posture.Normalized;
                    break;
                case NetworkEnemyArchetype.RuinGuard:
                    _state.Value = (int)_shieldBrain.State;
                    _health.Value = _shieldBrain.Health.Current;
                    _maximumHealth.Value = _shieldDefinition.MaximumHealth;
                    _attackSequence.Value = _shieldBrain.AttackSequence;
                    _attackKind.Value = (int)_shieldBrain.CurrentAttack;
                    _secondaryResource.Value = _shieldBrain.GuardNormalized;
                    break;
                default:
                    _state.Value = (int)_brain.State;
                    _health.Value = _brain.Health.Current;
                    _maximumHealth.Value = _definition.MaximumHealth;
                    _attackSequence.Value = _brain.AttackSequence;
                    _attackKind.Value = 0;
                    _secondaryResource.Value = _brain.Posture.Normalized;
                    break;
            }
        }

        private void PublishPoseAndState()
        {
            _serverPosition.Value = transform.position;
            _serverYaw.Value = transform.eulerAngles.y;
            PublishServerState();
        }

        private void InterpolateRemotePose()
        {
            transform.position = Vector3.Lerp(transform.position, _serverPosition.Value, 14f * Time.deltaTime);
            Quaternion target = Quaternion.Euler(0f, _serverYaw.Value, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, 14f * Time.deltaTime);
        }

        private void ApplyPresentation(bool force)
        {
            int state = _state.Value;
            PresentReplicatedRangedRelease();
            ApplyAnimatorPresentation();
            if (!force && state == _lastPresentationState) return;
            _lastPresentationState = state;
            bool dead = IsDeadState(state);
            bool windup = IsWindupState(state);
            bool attack = IsAttackState(state);
            if (_attackIndicator != null) _attackIndicator.enabled = attack;
            if (_stateMarker == null) return;

            Color color = dead
                ? new Color(0.18f, 0.18f, 0.18f)
                : windup
                    ? new Color(1f, 0.72f, 0.08f)
                    : attack
                        ? new Color(1f, 0.1f, 0.04f)
                        : _archetype == NetworkEnemyArchetype.RunePriest
                            ? new Color(0.56f, 0.2f, 0.86f)
                            : _archetype == NetworkEnemyArchetype.RuinGuard
                                ? new Color(0.2f, 0.64f, 0.78f)
                                : new Color(0.72f, 0.08f, 0.12f);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", color * 1.6f);
            _stateMarker.SetPropertyBlock(block);
        }

        private bool HasServerBrain() => _archetype switch
        {
            NetworkEnemyArchetype.RunePriest => _rangedBrain != null,
            NetworkEnemyArchetype.RuinGuard => _shieldBrain != null,
            _ => _brain != null
        };

        private bool IsServerDead() => _archetype switch
        {
            NetworkEnemyArchetype.RunePriest => _rangedBrain.State == RangedEnemyState.Dead,
            NetworkEnemyArchetype.RuinGuard => _shieldBrain.State == ShieldEnemyState.Dead,
            _ => _brain.State == MeleeEnemyState.Dead
        };

        private bool IsResetReady() => _archetype switch
        {
            NetworkEnemyArchetype.RunePriest => _rangedBrain != null && _rangedBrain.IsResetReady,
            NetworkEnemyArchetype.RuinGuard => _shieldBrain != null && _shieldBrain.IsResetReady,
            _ => _brain != null && _brain.IsResetReady
        };

        private float GetServerHealth() => _archetype switch
        {
            NetworkEnemyArchetype.RunePriest => _rangedBrain.Health.Current,
            NetworkEnemyArchetype.RuinGuard => _shieldBrain.Health.Current,
            _ => _brain.Health.Current
        };

        private DamageResult ReceivePlayerDamage(DamageRequest request, Vector3 sourcePosition)
        {
            switch (_archetype)
            {
                case NetworkEnemyArchetype.RunePriest:
                    return _rangedBrain.ReceiveDamage(request);
                case NetworkEnemyArchetype.RuinGuard:
                    Vector3 toAttacker = sourcePosition - transform.position;
                    toAttacker.y = 0f;
                    bool frontal = toAttacker.sqrMagnitude > 0.001f &&
                        Vector3.Angle(transform.forward, toAttacker) <= _shieldDefinition.FrontalBlockAngle * 0.5f;
                    return _shieldBrain.ReceiveDamage(request, frontal);
                default:
                    return _brain.ReceiveDamage(request);
            }
        }

        private bool IsQuotaCommitted()
        {
            if (!HasServerBrain() || IsServerDead()) return false;
            if (_archetype == NetworkEnemyArchetype.RuinGuard)
                return _shieldBrain.State == ShieldEnemyState.Windup ||
                       _shieldBrain.State == ShieldEnemyState.Attack ||
                       _shieldBrain.State == ShieldEnemyState.Recovery;
            if (_archetype == NetworkEnemyArchetype.Fogwalker)
                return _brain.State == MeleeEnemyState.Windup ||
                       _brain.State == MeleeEnemyState.Attack ||
                       _brain.State == MeleeEnemyState.Recovery;
            return false;
        }

        private bool IsQuotaEligible()
        {
            if (!HasServerBrain() || IsServerDead() || _archetype == NetworkEnemyArchetype.RunePriest) return false;
            if (IsQuotaCommitted()) return true;
            NetworkGymPlayer target = _controller != null
                ? _controller.GetNearestAlivePlayer(transform.position)
                : null;
            if (target == null) return false;
            float range = _archetype == NetworkEnemyArchetype.RuinGuard
                ? _shieldDefinition.AttackRange
                : _definition.AttackRange;
            return PlanarDistance(transform.position, target.transform.position) <= range + 2.8f;
        }

        private Vector3 ProjectToGround(Vector3 position)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                position + Vector3.up * 4f,
                Vector3.down,
                10f,
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(transform) ||
                    collider.GetComponentInParent<NetworkGymPlayer>() != null ||
                    collider.GetComponentInParent<NetworkGymEnemy>() != null) continue;
                return hits[i].point;
            }
            return position;
        }

        private void PresentReplicatedRangedRelease()
        {
            bool isRunePriest = _archetype == NetworkEnemyArchetype.RunePriest;
            bool isScorchedBurst = _archetype == NetworkEnemyArchetype.RuinGuard &&
                (ShieldAttackKind)_attackKind.Value == ShieldAttackKind.ScorchedBurst;
            if ((!isRunePriest && !isScorchedBurst) || _releaseSequence.Value <= 0 ||
                _releaseSequence.Value == _presentedReleaseSequence) return;
            _presentedReleaseSequence = _releaseSequence.Value;
            NetworkEnemyAttackPresentation.Spawn(
                isScorchedBurst ? RangedAttackKind.GroundRune : (RangedAttackKind)_attackKind.Value,
                _releaseOrigin.Value,
                _releaseVector.Value,
                _rangedDefinition != null ? _rangedDefinition.ProjectileSpeed : 9f,
                _rangedDefinition != null ? _rangedDefinition.ProjectileLifetime : 2f,
                isScorchedBurst
                    ? (_shieldDefinition != null ? _shieldDefinition.ScorchedBurstTriggerDelay : 0.85f)
                    : (_rangedDefinition != null ? _rangedDefinition.GroundRuneTriggerDelay : 1.05f),
                _stateMarker != null ? _stateMarker.sharedMaterial : null);
        }

        private void ApplyAnimatorPresentation()
        {
            if (_animator == null || _animationSet == null || _animationSet.Controller == null) return;
            if (_animator.runtimeAnimatorController != _animationSet.Controller)
            {
                _animator.runtimeAnimatorController = _animationSet.Controller;
                _animator.applyRootMotion = false;
            }
            int state = _state.Value;
            int key = (state * 10) + _attackKind.Value;
            if (_lastAnimatorState == key) return;
            _lastAnimatorState = key;
            string stateName = ResolveAnimatorStateName(state);
            AnimatorSpeedCoordinator.SetBase(_animator, 1f, IsDeadState(state));
            _animator.CrossFadeInFixedTime(stateName, 0.08f, 0, 0f);
        }

        private int _lastAnimatorState = int.MinValue;

        private string ResolveAnimatorStateName(int state)
        {
            if (IsDeadState(state)) return CombatState.Dead.ToString();
            if (_archetype == NetworkEnemyArchetype.RunePriest)
            {
                if ((RangedEnemyState)state == RangedEnemyState.HitReact) return CombatState.HitReact.ToString();
                if (IsWindupState(state) || IsAttackState(state))
                    return (RangedAttackKind)_attackKind.Value == RangedAttackKind.GroundRune
                        ? "EnemyRuneCast"
                        : CombatState.HeavyAttack.ToString();
            }
            else if (_archetype == NetworkEnemyArchetype.RuinGuard)
            {
                ShieldEnemyState shieldState = (ShieldEnemyState)state;
                if (shieldState == ShieldEnemyState.HitReact || shieldState == ShieldEnemyState.GuardBreak)
                    return CombatState.HitReact.ToString();
                if (IsWindupState(state) || IsAttackState(state))
                    return (ShieldAttackKind)_attackKind.Value switch
                    {
                        ShieldAttackKind.ShieldBash => "EnemyShieldBash",
                        ShieldAttackKind.ScorchedBurst => "EnemyRuneCast",
                        _ => CombatState.HeavyAttack.ToString()
                    };
                if (shieldState == ShieldEnemyState.Idle || shieldState == ShieldEnemyState.Recovery)
                    return "EnemyShieldGuard";
            }
            else
            {
                MeleeEnemyState meleeState = (MeleeEnemyState)state;
                if (meleeState == MeleeEnemyState.HitReact)
                    return CombatState.HitReact.ToString();
                if (IsWindupState(state) || IsAttackState(state)) return CombatState.LightAttack1.ToString();
            }
            return CombatState.Locomotion.ToString();
        }

        private string GetStateLabel(int state) => _archetype switch
        {
            NetworkEnemyArchetype.RunePriest => ((RangedEnemyState)state).ToString(),
            NetworkEnemyArchetype.RuinGuard => ((ShieldEnemyState)state).ToString(),
            _ => ((MeleeEnemyState)state).ToString()
        };

        private bool IsDeadState(int state) => _archetype switch
        {
            NetworkEnemyArchetype.RunePriest => (RangedEnemyState)state == RangedEnemyState.Dead,
            NetworkEnemyArchetype.RuinGuard => (ShieldEnemyState)state == ShieldEnemyState.Dead,
            _ => (MeleeEnemyState)state == MeleeEnemyState.Dead
        };

        private bool IsWindupState(int state) => _archetype switch
        {
            NetworkEnemyArchetype.RunePriest => (RangedEnemyState)state == RangedEnemyState.Windup,
            NetworkEnemyArchetype.RuinGuard => (ShieldEnemyState)state == ShieldEnemyState.Windup,
            _ => (MeleeEnemyState)state == MeleeEnemyState.Windup
        };

        private bool IsAttackState(int state) => _archetype switch
        {
            NetworkEnemyArchetype.RunePriest => (RangedEnemyState)state == RangedEnemyState.Release,
            NetworkEnemyArchetype.RuinGuard => (ShieldEnemyState)state == ShieldEnemyState.Attack,
            _ => (MeleeEnemyState)state == MeleeEnemyState.Attack
        };

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x;
            float z = a.z - b.z;
            return Mathf.Sqrt((x * x) + (z * z));
        }

        private static float ResolvePlayerMeleePostureDamage(CombatStateMachine combat)
        {
            float basePosture = combat.CurrentAttackTag == AttackTag.Heavy ? 60f : combat.State switch
            {
                CombatState.LightAttack2 => 20f,
                CombatState.LightAttack3 => 38f,
                _ => 18f
            };
            return basePosture + combat.CurrentAttackPostureBonus;
        }

        private sealed class ServerProjectile
        {
            public ServerProjectile(Vector3 position, Vector3 direction, float speed, float remainingDistance, int sequence)
            {
                Position = position;
                Direction = direction;
                Speed = speed;
                RemainingDistance = remainingDistance;
                Sequence = sequence;
            }

            public Vector3 Position;
            public readonly Vector3 Direction;
            public readonly float Speed;
            public float RemainingDistance;
            public readonly int Sequence;
        }

        private sealed class ServerGroundRune
        {
            public ServerGroundRune(
                Vector3 center,
                float remaining,
                int sequence,
                float radius,
                float damage,
                float postureDamage,
                bool isScorchedHazard)
            {
                Center = center;
                Remaining = remaining;
                Sequence = sequence;
                Radius = radius;
                Damage = damage;
                PostureDamage = postureDamage;
                IsScorchedHazard = isScorchedHazard;
            }

            public readonly Vector3 Center;
            public float Remaining;
            public readonly int Sequence;
            public readonly float Radius;
            public readonly float Damage;
            public readonly float PostureDamage;
            public readonly bool IsScorchedHazard;
        }
    }
}
