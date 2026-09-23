using System.Collections;
using System.Collections.Generic;
using System.IO;
using Emberfall.AI.Domain;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Quests.Domain;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Networking
{
    [System.Serializable]
    public struct NetworkEnemySpawnPlan
    {
        [SerializeField] private NetworkEnemyArchetype _archetype;
        [SerializeField] private Vector3 _position;

        public NetworkEnemySpawnPlan(NetworkEnemyArchetype archetype, Vector3 position)
        {
            _archetype = archetype;
            _position = position;
        }

        public NetworkEnemyArchetype Archetype => _archetype;
        public Vector3 Position => _position;
    }

    [System.Serializable]
    public sealed class NetworkEncounterPlan
    {
        [SerializeField] private string _stableId;
        [SerializeField] private Vector3 _triggerCenter;
        [SerializeField, Min(1f)] private float _triggerRadius = 8f;
        [SerializeField] private NetworkEnemySpawnPlan[] _spawns = System.Array.Empty<NetworkEnemySpawnPlan>();

        public NetworkEncounterPlan(
            string stableId,
            Vector3 triggerCenter,
            float triggerRadius,
            NetworkEnemySpawnPlan[] spawns)
        {
            _stableId = stableId;
            _triggerCenter = triggerCenter;
            _triggerRadius = Mathf.Max(1f, triggerRadius);
            _spawns = spawns ?? System.Array.Empty<NetworkEnemySpawnPlan>();
        }

        public string StableId => _stableId;
        public Vector3 TriggerCenter => _triggerCenter;
        public float TriggerRadius => _triggerRadius;
        public IReadOnlyList<NetworkEnemySpawnPlan> Spawns => _spawns;
        public bool IsValid => !string.IsNullOrWhiteSpace(_stableId) && _spawns != null && _spawns.Length > 0;
    }

    [DisallowMultipleComponent]
    public sealed class NetworkGymSceneController : MonoBehaviour
    {
        private const float RescueRange = 2.25f;
        private const float ReviveHealthFraction = 0.45f;
        private const float PartyResetDelay = 3f;

        [SerializeField] private GameObject _playerPrefab;
        [SerializeField] private GameObject _enemyPrefab;
        [SerializeField] private GameObject _rangedEnemyPrefab;
        [SerializeField] private GameObject _shieldEnemyPrefab;
        [SerializeField] private GameObject _wardenPrefab;
        [SerializeField] private GameObject _worldObjectivePrefab;
        [SerializeField] private bool _spawnEnemyOnServer = true;
        [SerializeField] private bool _spawnWorldObjectiveOnServer = true;
        [SerializeField] private bool _requireWorldObjective = true;
        [SerializeField] private string _sceneLabel = "network-gym";
        [SerializeField] private Vector2 _movementBoundsCenter = Vector2.zero;
        [SerializeField] private Vector2 _movementBoundsHalfExtents = new Vector2(8.5f, 5f);
        [SerializeField] private TextAsset _sharedWorldQuestDefinitions;
        [SerializeField] private Transform _sharedWorldSealAnchor;
        [SerializeField] private Transform _sharedWorldGateAnchor;
        [SerializeField] private Transform _sharedWorldRewardAnchor;
        [SerializeField] private Transform _sharedWorldScoutAnchor;
        [SerializeField] private GameObject _sharedWorldSealCore;
        [SerializeField] private GameObject _sharedWorldGateBlocker;
        [SerializeField] private GameObject _sharedWorldRewardVisual;
        [SerializeField] private Renderer _sharedWorldSealMarker;
        [SerializeField] private string _sharedWorldQuestId = "quest:forest-seal";
        [SerializeField] private string _sharedWorldSealId = "seal:forest";
        [SerializeField] private string _sharedWorldRewardId = "reward:forest-coop";
        [SerializeField] private string _sanctumSceneName = "20_Sanctum";
        [SerializeField] private Vector3 _enemySpawnPoint = new Vector3(0f, 0f, 0.2f);
        [SerializeField] private Vector3 _rangedEnemySpawnPoint = new Vector3(-4f, 0f, 2f);
        [SerializeField] private Vector3 _shieldEnemySpawnPoint = new Vector3(4f, 0f, 2f);
        [SerializeField] private NetworkEncounterPlan[] _authoredEncounters =
            System.Array.Empty<NetworkEncounterPlan>();
        [SerializeField] private Vector3[] _spawnPoints =
        {
            new Vector3(-3f, 0f, 0f),
            new Vector3(3f, 0f, 0f)
        };

        private readonly List<NetworkGymPlayer> _players = new List<NetworkGymPlayer>(2);
        private readonly List<NetworkGymEnemy> _enemies = new List<NetworkGymEnemy>(3);
        private readonly HashSet<string> _spawnedEncounterIds = new HashSet<string>();
        private NetworkGymEnemy _enemy;
        private NetworkWarden _warden;
        private readonly CombatAttackQuotaModel _enemyAttackQuota = new CombatAttackQuotaModel(1);
        private NetworkGymWorldObjective _worldObjective;
        private readonly NetworkRescueState _rescue = new NetworkRescueState();
        private bool _matchStarted;
        private bool _returningToMenu;
        private bool _partyDefeated;
        private bool _sanctumLoading;
        private bool _sanctumReady;
        private bool _sanctumUnloading;
        private Vector2 _mainWorldBoundsCenter;
        private Vector2 _mainWorldBoundsHalfExtents;
        private Vector3[] _sanctumSpawnPoints;
        private double _matchStartedAt;
        private int _initialPlayerCount;
        private int _serverDownedCount;
        private bool _teammateLeftDuringRun;
        private bool _runResultPresentedLocally;
        private bool _autoRescueSmoke;
        private bool _autoWorldSmoke;
        private bool _autoDefenseSmoke;
        private bool _autoAttackSuiteSmoke;
        private bool _autoWorldDisconnectAfterSeal;
        private bool _autoEnemySuite;
        private bool _autoWardenSuite;
        private string _autoEncounterStopId;
        private bool _autoEncounterPositioned;
        private bool _autoEncounterReadyLogged;
        private bool _autoStopAtReward;
        private bool _autoStopAtInteraction;
        private bool _autoRewardReadyLogged;
        private bool _autoRewardPositioned;
        private float _autoRewardCaptureAt;
        private string _autoRewardScreenshotPath;
        private bool _autoInteractionReadyLogged;
        private bool _autoInteractionPositioned;
        private float _autoInteractionCaptureAt;
        private string _autoInteractionScreenshotPath;
        private int _autoWardenSuiteStage;
        private float _autoWardenSuiteAt;
        private int _autoEnemySuiteStage;
        private float _autoEnemySuiteStageAt;
        private float _nextAutoEnemyTeleportAt;
        private bool _rescueSmokeInjected;
        private bool _defenseDodgeInjected;
        private bool _defensePerfectGuardInjected;
        private bool _defenseGuardBreakInjected;
        private bool _attackSuiteDamageInjected;
        private bool _attackSuiteCompleted;
        private MainQuestStage _lastMainWorldSmokeStage = (MainQuestStage)(-1);
        private float _nextMainWorldSmokeRepositionAt;
        private float _defenseFollowUpAt;
        private float _rescueSmokeAt;
        private float _partyResetAt = -1f;
        private float _nextRescueProgressPublish;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;

        internal static NetworkGymSceneController Find() => FindObjectOfType<NetworkGymSceneController>();
        internal NetworkGymWorldObjective WorldObjective => _worldObjective;
        internal int ConnectedPlayerCount => _players.Count;
        public bool IsSharedMainWorld => string.Equals(
            _sceneLabel,
            "ember-valley-forest",
            System.StringComparison.Ordinal);
        public bool IsSharedWorldConfigured =>
            IsSharedMainWorld && _sharedWorldQuestDefinitions != null &&
            _sharedWorldSealAnchor != null && _sharedWorldGateAnchor != null &&
            _sharedWorldRewardAnchor != null && _sharedWorldGateBlocker != null &&
            _sharedWorldRewardVisual != null && _sharedWorldSealMarker != null;
        public bool IsEnemyRosterConfigured =>
            _enemyPrefab != null && _rangedEnemyPrefab != null && _shieldEnemyPrefab != null;
        public bool UsesServerTriggeredEncounters =>
            IsSharedMainWorld && _authoredEncounters != null && _authoredEncounters.Length > 0;
        public int AuthoredEncounterCount => _authoredEncounters?.Length ?? 0;
        public int AuthoredEnemySpawnCount
        {
            get
            {
                int count = 0;
                if (_authoredEncounters == null) return count;
                for (int i = 0; i < _authoredEncounters.Length; i++)
                    count += _authoredEncounters[i]?.Spawns.Count ?? 0;
                return count;
            }
        }
        public IReadOnlyList<NetworkEncounterPlan> AuthoredEncounters => _authoredEncounters;

        public void Configure(
            GameObject playerPrefab,
            GameObject enemyPrefab,
            GameObject worldObjectivePrefab,
            Vector3[] spawnPoints,
            Vector3 enemySpawnPoint,
            bool spawnEnemyOnServer = true,
            bool spawnWorldObjectiveOnServer = true,
            string sceneLabel = "network-gym",
            Vector2? movementBoundsCenter = null,
            Vector2? movementBoundsHalfExtents = null)
        {
            _playerPrefab = playerPrefab;
            _enemyPrefab = enemyPrefab;
            _worldObjectivePrefab = worldObjectivePrefab;
            _spawnPoints = spawnPoints;
            _enemySpawnPoint = enemySpawnPoint;
            _spawnEnemyOnServer = spawnEnemyOnServer;
            _spawnWorldObjectiveOnServer = spawnWorldObjectiveOnServer;
            _sceneLabel = string.IsNullOrWhiteSpace(sceneLabel) ? "network-gym" : sceneLabel;
            _movementBoundsCenter = movementBoundsCenter ?? Vector2.zero;
            _movementBoundsHalfExtents = movementBoundsHalfExtents ?? new Vector2(8.5f, 5f);
        }

        public void ConfigureSharedWorldSlice(
            TextAsset questDefinitions,
            Transform sealAnchor,
            Transform gateAnchor,
            Transform rewardAnchor,
            Transform scoutAnchor,
            GameObject sealCore,
            GameObject gateBlocker,
            GameObject rewardVisual,
            Renderer sealMarker,
            string questId,
            string sealId,
            string rewardId)
        {
            _sharedWorldQuestDefinitions = questDefinitions;
            _sharedWorldSealAnchor = sealAnchor;
            _sharedWorldGateAnchor = gateAnchor;
            _sharedWorldRewardAnchor = rewardAnchor;
            _sharedWorldScoutAnchor = scoutAnchor;
            _sharedWorldSealCore = sealCore;
            _sharedWorldGateBlocker = gateBlocker;
            _sharedWorldRewardVisual = rewardVisual;
            _sharedWorldSealMarker = sealMarker;
            _sharedWorldQuestId = questId;
            _sharedWorldSealId = sealId;
            _sharedWorldRewardId = rewardId;
        }

        public void ConfigureEnemyRoster(
            GameObject fogwalkerPrefab,
            Vector3 fogwalkerSpawn,
            GameObject rangedPrefab,
            Vector3 rangedSpawn,
            GameObject shieldPrefab,
            Vector3 shieldSpawn)
        {
            _enemyPrefab = fogwalkerPrefab;
            _enemySpawnPoint = fogwalkerSpawn;
            _rangedEnemyPrefab = rangedPrefab;
            _rangedEnemySpawnPoint = rangedSpawn;
            _shieldEnemyPrefab = shieldPrefab;
            _shieldEnemySpawnPoint = shieldSpawn;
            _spawnEnemyOnServer = fogwalkerPrefab != null || rangedPrefab != null || shieldPrefab != null;
        }

        public void ConfigureAuthoredEncounters(NetworkEncounterPlan[] encounters)
        {
            _authoredEncounters = encounters ?? System.Array.Empty<NetworkEncounterPlan>();
        }

        public void ConfigureSanctum(GameObject wardenPrefab, string sceneName = "20_Sanctum")
        {
            _wardenPrefab = wardenPrefab;
            _sanctumSceneName = string.IsNullOrWhiteSpace(sceneName) ? "20_Sanctum" : sceneName;
        }

        private IEnumerator Start()
        {
            yield return null;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening)
            {
                if (!IsSharedMainWorld)
                    Debug.LogWarning("[M5_NETWORK_GYM] Scene opened without an active network session.");
                yield break;
            }

            manager.SceneManager.OnSceneEvent += HandleNetworkSceneEvent;

            if (manager.IsServer)
            {
                _autoRescueSmoke = HasCommandLineFlag("-emberfall-network-auto-rescue=");
                _autoWorldSmoke = HasCommandLineFlag("-emberfall-network-auto-world=");
                _autoDefenseSmoke = HasCommandLineFlag("-emberfall-network-auto-defense=");
                _autoAttackSuiteSmoke = HasCommandLineFlag("-emberfall-network-auto-attack-suite=");
                _autoEnemySuite = HasCommandLineFlag("-emberfall-network-auto-enemy-suite=");
                _autoWardenSuite = HasCommandLineFlag("-emberfall-network-auto-warden=");
                if (Debug.isDebugBuild || Application.isBatchMode)
                {
                    _autoEncounterStopId = GetCommandLineValue("-emberfall-network-stop-at-encounter=");
                    _autoStopAtReward = HasCommandLineFlag("-emberfall-network-stop-at-reward=");
                    _autoRewardScreenshotPath = GetCommandLineValue("-emberfall-network-reward-screenshot=");
                    _autoInteractionScreenshotPath = GetCommandLineValue(
                        "-emberfall-network-interaction-screenshot=");
                    _autoStopAtInteraction =
                        HasCommandLineFlag("-emberfall-network-stop-at-interaction=") ||
                        !string.IsNullOrWhiteSpace(_autoInteractionScreenshotPath);
                    if (_autoStopAtInteraction)
                        Debug.Log(
                            $"[M5C_INTERACTION_CAPTURE_REQUESTED] path={_autoInteractionScreenshotPath}");
                }
                _autoWorldDisconnectAfterSeal =
                    HasCommandLineFlag("-emberfall-network-stop-world-after-seal=");
                SpawnConnectedPlayers(manager);
                if (_spawnEnemyOnServer && !UsesServerTriggeredEncounters) SpawnEnemies();
                if (_spawnWorldObjectiveOnServer) SpawnWorldObjective();
            }
        }

        private void Update()
        {
            if (!_matchStarted && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                TryStartMatch();
            }

            if (_matchStarted && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer &&
                !IsSharedMainWorld && HasEnemyReadyForReset())
            {
                ResetReadyEnemies();
            }

            if (_matchStarted && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                TickAuthoredEncounterActivation();
                TickEncounterPointSmokeIfRequested();
                UpdateEnemyAttackQuota();
                TickSharedMainWorldSmokeIfRequested();
                TickMainWorldEnemySuiteIfRequested();
                TickWardenSuiteIfRequested();
                TickDefenseSmokeIfRequested();
                TickAttackSuiteSmokeIfRequested();
                InjectRescueSmokeIfRequested();
                TickServerRescue();
                if (_partyDefeated && Time.unscaledTime >= _partyResetAt) ResetPartyAfterDefeat();
            }

            if (!_returningToMenu && SessionRuntime.Current.Snapshot.Mode == SessionMode.Client &&
                SessionRuntime.Current.Snapshot.State == SessionConnectionState.Failed)
            {
                _returningToMenu = true;
                StartCoroutine(ReturnClientToMenu());
            }

            UpdateRunResultPresentation();
        }

        internal void Register(NetworkGymPlayer player)
        {
            if (player == null || _players.Contains(player)) return;
            _players.Add(player);
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                _rescue.Register(player.OwnerClientId);
        }

        internal void Unregister(NetworkGymPlayer player)
        {
            if (player == null) return;
            bool wasServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            ulong clientId = player.OwnerClientId;
            _players.Remove(player);
            if (!wasServer) return;

            if (_matchStarted && clientId != NetworkManager.ServerClientId &&
                _worldObjective?.ResultPublished != true)
                _teammateLeftDuringRun = true;
            if (_autoWorldSmoke && _worldObjective?.ResultPublished != true &&
                _worldObjective?.ReplicatedStage == MainQuestStage.ReturnToScout)
            {
                _lastMainWorldSmokeStage = (MainQuestStage)(-1);
                _nextMainWorldSmokeRepositionAt = Time.unscaledTime + 0.5f;
                Debug.Log("[M5_MAIN_WORLD_SMOKE_HANDOFF] stage=ReturnToScout owner=remaining-host");
            }

            bool rescueWasActive = _rescue.IsRescueActive;
            ulong rescueTargetId = _rescue.ActiveTargetId;
            _rescue.Unregister(clientId);
            if (rescueWasActive)
            {
                FindPlayer(rescueTargetId)?.ServerSetRescueProgress(0f);
                Debug.Log($"[M5_RESCUE_CANCELLED] reason=player-disconnected clientId={clientId}");
            }

            if (_rescue.TryRecoverSolo(out ulong recoveredClientId))
            {
                NetworkGymPlayer remaining = FindPlayer(recoveredClientId);
                if (remaining != null && remaining.ServerRevive(ReviveHealthFraction))
                    Debug.Log($"[M5_SOLO_CONTINUATION_REVIVE] clientId={recoveredClientId} healthFraction={ReviveHealthFraction:F2}");
            }

            _partyDefeated = false;
            _partyResetAt = -1f;
            for (int i = 0; i < _players.Count; i++) _players[i]?.ServerSetPartyDefeated(false);
            Debug.Log($"[M5_CLIENT_DISCONNECT_CLEANUP] clientId={clientId} remaining={_players.Count} rescueActive={_rescue.IsRescueActive}");
        }

        internal void Register(NetworkGymEnemy enemy)
        {
            if (enemy == null || _enemies.Contains(enemy)) return;
            _enemies.Add(enemy);
            if (_enemy == null || enemy.Archetype == NetworkEnemyArchetype.Fogwalker) _enemy = enemy;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && enemy.ServerUsesMeleeQuota)
                _enemyAttackQuota.Register(enemy.ServerCombatantId);
        }

        internal void Unregister(NetworkGymEnemy enemy)
        {
            _enemies.Remove(enemy);
            if (_enemy == enemy) _enemy = null;
        }

        internal void Register(NetworkWarden warden)
        {
            if (warden != null) _warden = warden;
        }

        internal void Unregister(NetworkWarden warden)
        {
            if (_warden == warden) _warden = null;
        }

        internal void Register(NetworkGymWorldObjective worldObjective)
        {
            if (worldObjective != null) _worldObjective = worldObjective;
        }

        internal void Unregister(NetworkGymWorldObjective worldObjective)
        {
            if (_worldObjective == worldObjective) _worldObjective = null;
        }

        internal bool ServerTryInteract(NetworkGymPlayer player, out string reason)
        {
            if (_worldObjective == null)
            {
                reason = "world-objective-missing";
                return false;
            }
            return _worldObjective.ServerTryInteract(player, out reason);
        }

        internal bool ServerBeginSanctumTransition(out string reason)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (!IsSharedMainWorld || manager == null || !manager.IsServer || _sanctumLoading || _sanctumReady)
            {
                reason = _sanctumReady ? string.Empty : "sanctum-transition-not-ready";
                return _sanctumReady;
            }
            if (_wardenPrefab == null)
            {
                reason = "warden-prefab-missing";
                return false;
            }

            _mainWorldBoundsCenter = _movementBoundsCenter;
            _mainWorldBoundsHalfExtents = _movementBoundsHalfExtents;
            SetPlayersInputSuppressed(true);
            SceneEventProgressStatus status = manager.SceneManager.LoadScene(_sanctumSceneName, LoadSceneMode.Additive);
            if (status != SceneEventProgressStatus.Started)
            {
                SetPlayersInputSuppressed(false);
                reason = $"sanctum-load-{status}";
                return false;
            }

            _sanctumLoading = true;
            reason = string.Empty;
            Debug.Log($"[M5_SANCTUM_LOAD_STARTED] scene={_sanctumSceneName} authority=server");
            return true;
        }

        internal void ServerHandleWardenDefeated(NetworkWarden warden)
        {
            if (warden == null || warden != _warden || NetworkManager.Singleton == null ||
                !NetworkManager.Singleton.IsServer) return;
            if (_worldObjective?.ServerMarkWardenDefeated() != true) return;

            SetPlayersInputSuppressed(true);
            _movementBoundsCenter = _mainWorldBoundsCenter;
            _movementBoundsHalfExtents = _mainWorldBoundsHalfExtents;
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkGymPlayer player = _players[i];
                if (player == null) continue;
                Vector3 spawn = _spawnPoints[Mathf.Min(i, _spawnPoints.Length - 1)];
                player.ServerTeleportForSceneTransition(spawn, Quaternion.identity);
            }
            BeginSanctumUnload();
            Debug.Log("[M5_WARDEN_DEFEATED] stage=ReturnToScout authority=server");
        }

        internal bool ServerFinalizeSharedRoute(out string reason)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (!IsSharedMainWorld || manager == null || !manager.IsServer || !_matchStarted ||
                _worldObjective == null)
            {
                reason = "result-controller-not-ready";
                return false;
            }
            if (_worldObjective.ResultPublished)
            {
                reason = "result-already-published";
                return false;
            }

            float elapsed = Mathf.Max(0f, (float)(Time.realtimeSinceStartupAsDouble - _matchStartedAt));
            int initialPlayers = Mathf.Clamp(_initialPlayerCount, 1, 2);
            int connectedPlayers = Mathf.Clamp(_players.Count, 1, initialPlayers);
            NetworkRunResultSummary result = NetworkRunResultEvaluator.Create(
                1,
                elapsed,
                _serverDownedCount,
                0,
                initialPlayers,
                connectedPlayers,
                _teammateLeftDuringRun);
            if (!_worldObjective.ServerPublishRunResult(result, out reason)) return false;

            SetPlayersInputSuppressed(true);
            Debug.Log("[M5_RUN_COMPLETE] inputSuppressed=true authority=server");
            return true;
        }

        internal bool HasDownedTeammate(NetworkGymPlayer player) => GetNearestDownedPlayer(player, false) != null;

        internal bool TryGetNearestDownedTeammatePosition(NetworkGymPlayer player, out Vector3 position)
        {
            NetworkGymPlayer target = GetNearestDownedPlayer(player, false);
            if (target != null)
            {
                position = target.transform.position;
                return true;
            }
            position = default;
            return false;
        }

        internal bool IsWithinRescueRangeOfDownedTeammate(NetworkGymPlayer player) =>
            GetNearestDownedPlayer(player, true) != null;

        internal bool ServerSubmitRescueIntent(NetworkGymPlayer rescuer, bool held, out string reason)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || !_matchStarted || rescuer == null)
            {
                reason = "rescue-not-ready";
                return false;
            }

            if (!held)
            {
                if (_rescue.IsRescueActive)
                {
                    ulong targetId = _rescue.ActiveTargetId;
                    if (_rescue.TryCancel(rescuer.OwnerClientId))
                    {
                        FindPlayer(targetId)?.ServerSetRescueProgress(0f);
                        Debug.Log($"[M5_RESCUE_CANCELLED] reason=input-released rescuer={rescuer.OwnerClientId} target={targetId}");
                    }
                }
                reason = string.Empty;
                return true;
            }

            NetworkGymPlayer target = GetNearestDownedPlayer(rescuer, false);
            if (target == null)
            {
                reason = "no-downed-teammate";
                return false;
            }

            bool inRange = IsInRescueRange(rescuer, target);
            bool wasSameRescue = _rescue.IsRescueActive &&
                                 _rescue.ActiveRescuerId == rescuer.OwnerClientId &&
                                 _rescue.ActiveTargetId == target.OwnerClientId;
            if (!_rescue.TryStartOrRefresh(rescuer.OwnerClientId, target.OwnerClientId, inRange, out reason))
                return false;

            if (!wasSameRescue)
                Debug.Log($"[M5_RESCUE_STARTED] rescuer={rescuer.OwnerClientId} target={target.OwnerClientId} required={NetworkRescueState.RequiredSeconds:F1}");
            return true;
        }

        internal void ServerHandlePlayerLethal(NetworkGymPlayer player)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || player == null) return;
            if (!_rescue.TryDown(player.OwnerClientId, out string reason))
            {
                Debug.LogWarning($"[M5_DOWNED_REJECTED] clientId={player.OwnerClientId} reason={reason}");
                return;
            }

            _serverDownedCount++;
            player.ServerSetDowned(true);
            Debug.Log($"[M5_PLAYER_DOWNED] clientId={player.OwnerClientId} connected={_rescue.ConnectedCount} downed={_rescue.DownedCount}");
            if (!_rescue.PartyDefeated) return;

            _partyDefeated = true;
            _partyResetAt = Time.unscaledTime + PartyResetDelay;
            for (int i = 0; i < _players.Count; i++) _players[i]?.ServerSetPartyDefeated(true);
            Debug.Log($"[M5_PARTY_DEFEATED] resetAfter={PartyResetDelay:F1}");
        }

        internal NetworkGymPlayer GetNearestAlivePlayer(Vector3 position)
        {
            NetworkGymPlayer nearest = null;
            float nearestDistanceSquared = float.MaxValue;
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkGymPlayer player = _players[i];
                if (player == null || !player.IsSpawned || player.IsDead || player.IsDowned) continue;
                Vector3 offset = player.transform.position - position;
                offset.y = 0f;
                if (offset.sqrMagnitude >= nearestDistanceSquared) continue;
                nearestDistanceSquared = offset.sqrMagnitude;
                nearest = player;
            }
            return nearest;
        }

        internal NetworkGymPlayer FindPlayerByClientId(ulong clientId) => FindPlayer(clientId);

        internal IEnumerable<NetworkGymPlayer> GetAlivePlayers()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkGymPlayer player = _players[i];
                if (player != null && player.IsSpawned && !player.IsDead && !player.IsDowned)
                    yield return player;
            }
        }

        internal Vector3 ConstrainToPlayableBounds(Vector3 position)
        {
            float halfWidth = Mathf.Max(0.5f, _movementBoundsHalfExtents.x);
            float halfDepth = Mathf.Max(0.5f, _movementBoundsHalfExtents.y);
            position.x = Mathf.Clamp(
                position.x,
                _movementBoundsCenter.x - halfWidth,
                _movementBoundsCenter.x + halfWidth);
            position.z = Mathf.Clamp(
                position.z,
                _movementBoundsCenter.y - halfDepth,
                _movementBoundsCenter.y + halfDepth);
            return position;
        }

        internal int ServerResolvePlayerAttack(NetworkGymPlayer player, Emberfall.Gameplay.Combat.Domain.CombatStateMachine combat)
        {
            int hitCount = ResolveAllMeleeTargets(
                GetEnemiesNearestFirst(player != null ? player.transform.position : Vector3.zero),
                enemy => enemy.ServerReceivePlayerAttack(player, combat));
            if (_warden != null && _warden.ServerReceivePlayerAttack(player, combat)) hitCount++;
            return hitCount;
        }

        public static int ResolveAllMeleeTargets<T>(
            IEnumerable<T> serverTargets,
            System.Func<T, bool> tryResolve)
        {
            if (serverTargets == null) throw new System.ArgumentNullException(nameof(serverTargets));
            if (tryResolve == null) throw new System.ArgumentNullException(nameof(tryResolve));

            int hitCount = 0;
            foreach (T target in serverTargets)
            {
                if (tryResolve(target)) hitCount++;
            }
            return hitCount;
        }

        internal bool ServerTryResolvePlayerProjectile(
            NetworkGymPlayer player,
            RangedAttackRelease release,
            Vector3 direction,
            out string reason)
        {
            if (_enemies.Count == 0 && _warden == null)
            {
                reason = "network-enemy-missing";
                return false;
            }

            foreach (NetworkGymEnemy enemy in GetEnemiesNearestFirst(player != null ? player.transform.position : Vector3.zero))
            {
                if (enemy.ServerReceivePlayerProjectile(player, release, direction, out reason)) return true;
            }
            if (_warden != null && _warden.ServerReceivePlayerProjectile(player, release, direction, out reason))
                return true;
            reason = "projectile-no-enemy-resolved";
            return false;
        }

        internal bool TryGetEnemyAimDirection(NetworkGymPlayer player, out Vector3 direction)
        {
            direction = player != null ? player.transform.forward : Vector3.forward;
            NetworkGymEnemy target = FindNearestAliveEnemy(player != null ? player.transform.position : Vector3.zero);
            if (player == null) return false;

            if (_warden != null && _warden.IsAlive &&
                (target == null || (_warden.transform.position - player.transform.position).sqrMagnitude <
                 (target.transform.position - player.transform.position).sqrMagnitude))
            {
                Vector3 bossOffset = _warden.transform.position - player.transform.position;
                bossOffset.y = 0f;
                if (bossOffset.sqrMagnitude <= 0.0001f) return false;
                direction = bossOffset.normalized;
                return true;
            }
            if (target == null) return false;

            Vector3 offset = target.transform.position - player.transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= 0.0001f) return false;
            direction = offset.normalized;
            return true;
        }

        private void SpawnConnectedPlayers(NetworkManager manager)
        {
            if (_playerPrefab == null)
            {
                Debug.LogError("[M5_NETWORK_GYM] Player prefab is missing.");
                return;
            }

            var clientIds = new List<ulong>(manager.ConnectedClientsIds);
            clientIds.Sort();
            for (int i = 0; i < clientIds.Count; i++)
            {
                ulong clientId = clientIds[i];
                if (manager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null)
                    continue;

                Vector3 spawn = _spawnPoints[Mathf.Min(i, _spawnPoints.Length - 1)];
                GameObject instance = Instantiate(_playerPrefab, spawn, Quaternion.identity);
                instance.name = $"NetworkPlayer_{clientId}";
                instance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
            }
        }

        private void SpawnEnemies()
        {
            SpawnEnemy(_enemyPrefab, _enemySpawnPoint, "NetworkEnemy_Fogwalker");
            SpawnEnemy(_rangedEnemyPrefab, _rangedEnemySpawnPoint, "NetworkEnemy_RunePriest");
            SpawnEnemy(_shieldEnemyPrefab, _shieldEnemySpawnPoint, "NetworkEnemy_RuinGuard");
        }

        private void TickAuthoredEncounterActivation()
        {
            if (!UsesServerTriggeredEncounters || _authoredEncounters == null) return;
            for (int i = 0; i < _authoredEncounters.Length; i++)
            {
                NetworkEncounterPlan encounter = _authoredEncounters[i];
                if (encounter == null || !encounter.IsValid || _spawnedEncounterIds.Contains(encounter.StableId))
                    continue;
                if (!IsAnyAlivePlayerInside(encounter.TriggerCenter, encounter.TriggerRadius)) continue;

                _spawnedEncounterIds.Add(encounter.StableId);
                for (int spawnIndex = 0; spawnIndex < encounter.Spawns.Count; spawnIndex++)
                {
                    NetworkEnemySpawnPlan spawn = encounter.Spawns[spawnIndex];
                    GameObject prefab = ResolveEnemyPrefab(spawn.Archetype);
                    NetworkGymEnemy enemy = SpawnEnemy(
                        prefab,
                        spawn.Position,
                        $"NetworkEnemy_{encounter.StableId}_{spawnIndex:00}");
                    enemy?.ServerSetCombatActive();
                }
                Debug.Log(
                    $"[M5C_ENCOUNTER_ACTIVATED] id={encounter.StableId} enemies={encounter.Spawns.Count} authority=server");
            }
        }

        private bool IsAnyAlivePlayerInside(Vector3 center, float radius)
        {
            float radiusSquared = radius * radius;
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkGymPlayer player = _players[i];
                if (player == null || !player.IsSpawned || player.IsDead) continue;
                Vector3 offset = player.transform.position - center;
                offset.y = 0f;
                if (offset.sqrMagnitude <= radiusSquared) return true;
            }
            return false;
        }

        private GameObject ResolveEnemyPrefab(NetworkEnemyArchetype archetype)
        {
            return archetype switch
            {
                NetworkEnemyArchetype.RunePriest => _rangedEnemyPrefab,
                NetworkEnemyArchetype.RuinGuard => _shieldEnemyPrefab,
                _ => _enemyPrefab
            };
        }

        private static NetworkGymEnemy SpawnEnemy(GameObject prefab, Vector3 position, string instanceName)
        {
            if (prefab == null) return null;
            GameObject instance = Instantiate(prefab, position, Quaternion.Euler(0f, 180f, 0f));
            instance.name = instanceName;
            instance.GetComponent<NetworkObject>().Spawn(true);
            return instance.GetComponent<NetworkGymEnemy>();
        }

        private void SpawnWorldObjective()
        {
            if (_worldObjectivePrefab == null)
            {
                Debug.LogError("[M5_NETWORK_GYM] World objective prefab is missing.");
                return;
            }

            GameObject instance = Instantiate(_worldObjectivePrefab, Vector3.zero, Quaternion.identity);
            instance.name = "NetworkWorldObjective";
            if (IsSharedMainWorld)
            {
                for (int i = 0; i < instance.transform.childCount; i++)
                    instance.transform.GetChild(i).gameObject.SetActive(false);
                NetworkGymWorldObjective objective = instance.GetComponent<NetworkGymWorldObjective>();
                objective.Configure(
                    _sharedWorldQuestDefinitions,
                    _sharedWorldSealAnchor,
                    _sharedWorldGateAnchor,
                    _sharedWorldRewardAnchor,
                    _sharedWorldSealCore,
                    _sharedWorldGateBlocker,
                    _sharedWorldRewardVisual,
                    _sharedWorldSealMarker,
                    _sharedWorldQuestId,
                    _sharedWorldSealId,
                    _sharedWorldRewardId);
                objective.ConfigureBossRoute(_sharedWorldScoutAnchor);
            }
            instance.GetComponent<NetworkObject>().Spawn(true);
        }

        private void TryStartMatch()
        {
            if (_players.Count < 2) return;
            if (_requireWorldObjective && _worldObjective == null) return;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i] == null || !_players[i].IsSpawned || !_players[i].IsReady) return;
            }

            _matchStarted = true;
            _matchStartedAt = Time.realtimeSinceStartupAsDouble;
            _initialPlayerCount = Mathf.Clamp(_players.Count, 1, 2);
            _serverDownedCount = 0;
            _teammateLeftDuringRun = false;
            for (int i = 0; i < _players.Count; i++) _players[i].ServerSetMatchStarted();
            if (!_autoEnemySuite)
            {
                for (int i = 0; i < _enemies.Count; i++) _enemies[i]?.ServerSetCombatActive();
            }
            _worldObjective?.ServerSetActive();
            _rescueSmokeAt = Time.unscaledTime + 1.5f;
            Debug.Log($"[M5_NETWORK_GYM_STARTED] players={_players.Count} scene={_sceneLabel}");
        }

        private void TickSharedMainWorldSmokeIfRequested()
        {
            if (!_autoWorldSmoke || !IsSharedMainWorld || _worldObjective == null ||
                Time.unscaledTime < _nextMainWorldSmokeRepositionAt ||
                !_worldObjective.TryGetCurrentInteractionTarget(out Vector3 target))
                return;

            MainQuestStage stage = _worldObjective.ReplicatedStage;
            if (_autoStopAtInteraction && stage == MainQuestStage.ActivateSeals)
            {
                if (_sharedWorldSealMarker == null || !_sharedWorldSealMarker.gameObject.activeInHierarchy) return;
                if (!_autoInteractionPositioned)
                {
                    NetworkGymPlayer interactionActor = FindPlayer(NetworkManager.ServerClientId) ??
                                                         GetNearestAlivePlayer(_sharedWorldSealAnchor.position);
                    if (interactionActor == null) return;
                    Vector3 direction = _sharedWorldSealAnchor.forward;
                    direction.y = 0f;
                    if (direction.sqrMagnitude <= 0.001f) direction = Vector3.forward;
                    Vector3 right = Vector3.Cross(Vector3.up, direction.normalized);
                    Vector3 interactionDestination =
                        _sharedWorldSealAnchor.position - (direction.normalized * 4.4f) + (right * 1.8f);
                    interactionDestination.y = interactionActor.transform.position.y;
                    interactionActor.ServerTeleportForSceneTransition(
                        interactionDestination,
                        Quaternion.LookRotation(direction.normalized, Vector3.up));
                    _autoInteractionPositioned = true;
                    _autoInteractionCaptureAt = Time.unscaledTime;
                    Debug.Log(
                        $"[M5C_INTERACTION_CAPTURE_POSITIONED] clientId={interactionActor.OwnerClientId} " +
                        $"target={_sharedWorldSealAnchor.position} authority=server");
                }
                if (_autoInteractionReadyLogged || Time.unscaledTime < _autoInteractionCaptureAt) return;
                _autoInteractionReadyLogged = true;
                MeshFilter ring = _sharedWorldSealMarker.GetComponent<MeshFilter>();
                Transform iconTransform = _sharedWorldSealMarker.transform.Find("Seal_Forest_HighlightIcon");
                MeshFilter icon = iconTransform != null ? iconTransform.GetComponent<MeshFilter>() : null;
                if (!Application.isBatchMode && !string.IsNullOrWhiteSpace(_autoInteractionScreenshotPath))
                {
                    if (TryCaptureInteractionEvidence(_autoInteractionScreenshotPath))
                        Debug.Log($"[M5C_INTERACTION_CAPTURE_WRITTEN] path={_autoInteractionScreenshotPath}");
                    else
                        Debug.LogError($"[M5C_INTERACTION_CAPTURE_FAILED] path={_autoInteractionScreenshotPath}");
                }
                Debug.Log(
                    $"[M5C_INTERACTION_PRESENTATION_READY] active=true " +
                    $"ring={ring?.sharedMesh?.name ?? "missing"} icon={icon?.sharedMesh?.name ?? "missing"} " +
                    $"collider={_sharedWorldSealMarker.GetComponent<Collider>() != null} authority=server");
                return;
            }
            if (_autoStopAtReward && stage == MainQuestStage.ReturnToScout)
            {
                if (_sharedWorldRewardVisual == null || !_sharedWorldRewardVisual.activeInHierarchy) return;
                if (!_autoRewardPositioned)
                {
                    NetworkGymPlayer rewardActor = FindPlayer(NetworkManager.ServerClientId) ??
                                                   GetNearestAlivePlayer(_sharedWorldRewardAnchor.position);
                    if (rewardActor == null) return;
                    Vector3 direction = _sharedWorldRewardAnchor.forward;
                    direction.y = 0f;
                    if (direction.sqrMagnitude <= 0.001f) direction = Vector3.forward;
                    Vector3 right = Vector3.Cross(Vector3.up, direction.normalized);
                    Vector3 rewardDestination = _sharedWorldRewardAnchor.position - (direction.normalized * 4.2f) +
                                                (right * 2.1f);
                    rewardDestination.y = rewardActor.transform.position.y;
                    rewardActor.ServerTeleportForSceneTransition(
                        rewardDestination,
                        Quaternion.LookRotation(direction.normalized, Vector3.up));
                    _autoRewardPositioned = true;
                    _autoRewardCaptureAt = Time.unscaledTime + 2f;
                    Debug.Log(
                        $"[M5C_REWARD_CAPTURE_POSITIONED] clientId={rewardActor.OwnerClientId} " +
                        $"target={_sharedWorldRewardAnchor.position} authority=server");
                    return;
                }
                if (_autoRewardReadyLogged || Time.unscaledTime < _autoRewardCaptureAt) return;
                _autoRewardReadyLogged = true;
                MeshFilter filter = _sharedWorldRewardVisual.GetComponent<MeshFilter>();
                if (!Application.isBatchMode && !string.IsNullOrWhiteSpace(_autoRewardScreenshotPath))
                {
                    if (TryCaptureRewardEvidence(_autoRewardScreenshotPath))
                        Debug.Log($"[M5C_REWARD_CAPTURE_WRITTEN] path={_autoRewardScreenshotPath}");
                    else
                        Debug.LogError($"[M5C_REWARD_CAPTURE_FAILED] path={_autoRewardScreenshotPath}");
                }
                Debug.Log(
                    $"[M5C_REWARD_PRESENTATION_READY] active=true mesh={filter?.sharedMesh?.name ?? "missing"} " +
                    $"collider={_sharedWorldRewardVisual.GetComponent<Collider>() != null} authority=server");
                return;
            }
            if (_autoWorldDisconnectAfterSeal && stage != MainQuestStage.ActivateSeals) return;
            if (stage == _lastMainWorldSmokeStage) return;

            ulong expectedOwner = stage == MainQuestStage.ActivateSeals
                ? NetworkManager.ServerClientId
                : ulong.MaxValue;
            NetworkGymPlayer actor = null;
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkGymPlayer candidate = _players[i];
                if (candidate == null || !candidate.IsSpawned) continue;
                bool matches = expectedOwner == ulong.MaxValue
                    ? candidate.OwnerClientId != NetworkManager.ServerClientId
                    : candidate.OwnerClientId == expectedOwner;
                if (matches)
                {
                    actor = candidate;
                    break;
                }
            }
            if (actor == null && _players.Count == 1)
                actor = GetNearestAlivePlayer(target);
            if (actor == null) return;

            Vector3 approach = actor.transform.position - target;
            approach.y = 0f;
            if (approach.sqrMagnitude <= 0.0001f) approach = Vector3.back;
            Vector3 destination = target + (approach.normalized * 1.25f);
            destination.y = actor.transform.position.y;
            actor.ServerTeleportForWorldSmoke(destination, Quaternion.LookRotation(-approach.normalized, Vector3.up));
            _lastMainWorldSmokeStage = stage;
            _nextMainWorldSmokeRepositionAt = Time.unscaledTime + 0.75f;
            Debug.Log(
                $"[M5_MAIN_WORLD_SMOKE_REPOSITION] stage={stage} clientId={actor.OwnerClientId} " +
                $"target={target}");
        }

        private void TickMainWorldEnemySuiteIfRequested()
        {
            if (!_autoEnemySuite || !IsSharedMainWorld || _worldObjective == null || !_worldObjective.RewardClaimed)
                return;

            NetworkGymPlayer actor = FindRemoteDefenseTarget();
            NetworkGymEnemy runePriest = FindEnemy(NetworkEnemyArchetype.RunePriest);
            NetworkGymEnemy ruinGuard = FindEnemy(NetworkEnemyArchetype.RuinGuard);
            if (actor == null || runePriest == null || ruinGuard == null) return;

            if (_autoEnemySuiteStage == 0)
            {
                for (int i = 0; i < _enemies.Count; i++) _enemies[i]?.ServerSetCombatActive();
                _autoEnemySuiteStage = 1;
                _autoEnemySuiteStageAt = Time.unscaledTime + 10f;
                _nextAutoEnemyTeleportAt = 0f;
                Debug.Log("[M5_MAIN_WORLD_ENEMY_SUITE_STARTED] authority=server");
                return;
            }

            if (_autoEnemySuiteStage == 1)
            {
                TeleportEnemySuiteActor(actor, runePriest, 6.2f);
                if (!runePriest.ServerHasRangedAttackCoverage && Time.unscaledTime < _autoEnemySuiteStageAt)
                    return;
                _autoEnemySuiteStage = 2;
                _nextAutoEnemyTeleportAt = 0f;
                return;
            }

            if (_autoEnemySuiteStage == 2)
            {
                TeleportEnemySuiteActor(actor, runePriest, 1.35f);
                if (runePriest.IsAlive) return;
                _autoEnemySuiteStage = 3;
                _nextAutoEnemyTeleportAt = 0f;
                Debug.Log("[M5_MAIN_WORLD_RUNE_PRIEST_DEFEATED] authority=server");
                return;
            }

            if (_autoEnemySuiteStage == 3)
            {
                float approachDistance = ruinGuard.ServerShieldBlockedHitCount == 0 ? 1.35f : -1.35f;
                TeleportEnemySuiteActor(actor, ruinGuard, approachDistance);
                if (ruinGuard.IsAlive) return;
                _autoEnemySuiteStage = 4;
                Debug.Log(
                    $"[M5_MAIN_WORLD_ENEMY_SUITE_COMPLETE] runeAttacks={runePriest.ServerHasRangedAttackCoverage} " +
                    $"shieldBlocks={ruinGuard.ServerShieldBlockedHitCount} authority=server");
            }
        }

        private bool TryCaptureRewardEvidence(string path)
        {
            Camera sourceCamera = Camera.main;
            if (sourceCamera == null || _sharedWorldRewardVisual == null || string.IsNullOrWhiteSpace(path))
                return false;
            const int width = 1920;
            const int height = 1080;
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var cameraObject = new GameObject("M5c_RewardEvidenceCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            try
            {
                camera.CopyFrom(sourceCamera);
                Vector3 focus = _sharedWorldRewardVisual.transform.position + (Vector3.up * 0.15f);
                Vector3 approach = _sharedWorldScoutAnchor != null
                    ? _sharedWorldScoutAnchor.position - focus
                    : -_sharedWorldRewardAnchor.forward;
                approach.y = 0f;
                if (approach.sqrMagnitude <= 0.001f) approach = Vector3.back;
                camera.transform.position = focus + (approach.normalized * 3.1f) + (Vector3.up * 1.35f);
                camera.transform.rotation = Quaternion.LookRotation(
                    focus + (Vector3.up * 0.12f) - camera.transform.position,
                    Vector3.up);
                camera.fieldOfView = 40f;
                target.Create();
                camera.targetTexture = target;
                RenderTexture.active = target;
                camera.Render();
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                image.Apply(false, false);
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(path, image.EncodeToPNG());
                return File.Exists(path) && new FileInfo(path).Length > 10000;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousActive;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(image);
                Object.Destroy(cameraObject);
            }
        }

        private bool TryCaptureInteractionEvidence(string path)
        {
            Camera sourceCamera = Camera.main;
            if (sourceCamera == null || _sharedWorldSealMarker == null || string.IsNullOrWhiteSpace(path))
                return false;
            const int width = 1920;
            const int height = 1080;
            RenderTexture previousActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            var cameraObject = new GameObject("M5c_InteractionEvidenceCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            try
            {
                camera.CopyFrom(sourceCamera);
                Vector3 focus = _sharedWorldSealMarker.transform.position + (Vector3.up * 0.85f);
                Vector3 approach = -_sharedWorldSealAnchor.forward;
                approach.y = 0f;
                if (approach.sqrMagnitude <= 0.001f) approach = Vector3.back;
                camera.transform.position = focus + (approach.normalized * 4.6f) + (Vector3.up * 1.45f);
                camera.transform.rotation = Quaternion.LookRotation(
                    focus + (Vector3.up * 0.15f) - camera.transform.position,
                    Vector3.up);
                camera.fieldOfView = 43f;
                target.Create();
                camera.targetTexture = target;
                RenderTexture.active = target;
                camera.Render();
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                image.Apply(false, false);
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(path, image.EncodeToPNG());
                return File.Exists(path) && new FileInfo(path).Length > 10000;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousActive;
                target.Release();
                Object.Destroy(target);
                Object.Destroy(image);
                Object.Destroy(cameraObject);
            }
        }

        private void TickEncounterPointSmokeIfRequested()
        {
            if (string.IsNullOrWhiteSpace(_autoEncounterStopId) || !IsSharedMainWorld || !_matchStarted ||
                !UsesServerTriggeredEncounters) return;

            NetworkEncounterPlan targetEncounter = null;
            for (int i = 0; i < _authoredEncounters.Length; i++)
            {
                NetworkEncounterPlan candidate = _authoredEncounters[i];
                if (candidate != null && string.Equals(
                        candidate.StableId,
                        _autoEncounterStopId,
                        System.StringComparison.Ordinal))
                {
                    targetEncounter = candidate;
                    break;
                }
            }
            if (targetEncounter == null)
            {
                if (!_autoEncounterReadyLogged)
                {
                    _autoEncounterReadyLogged = true;
                    Debug.LogError($"[M5C_ENCOUNTER_STOP_FAILED] id={_autoEncounterStopId} reason=unknown-stable-id");
                }
                return;
            }

            NetworkGymPlayer actor = GetNearestAlivePlayer(targetEncounter.TriggerCenter);
            if (actor == null) return;
            if (!_spawnedEncounterIds.Contains(targetEncounter.StableId))
            {
                if (_autoEncounterPositioned) return;
                Vector3 destination = targetEncounter.TriggerCenter;
                destination.y = actor.transform.position.y;
                actor.ServerTeleportForSceneTransition(destination, actor.transform.rotation);
                _autoEncounterPositioned = true;
                Debug.Log(
                    $"[M5C_ENCOUNTER_STOP_POSITIONED] id={targetEncounter.StableId} clientId={actor.OwnerClientId} " +
                    $"center={targetEncounter.TriggerCenter} authority=server");
                return;
            }

            if (!string.Equals(
                    targetEncounter.StableId,
                    "encounter:scorched-courtyard",
                    System.StringComparison.Ordinal))
            {
                LogEncounterStopReady(targetEncounter.StableId, "activation");
                return;
            }

            NetworkGymEnemy elite = null;
            for (int i = 0; i < _enemies.Count; i++)
            {
                NetworkGymEnemy candidate = _enemies[i];
                if (candidate != null && candidate.Archetype == NetworkEnemyArchetype.RuinGuard &&
                    candidate.name.Contains(targetEncounter.StableId))
                {
                    elite = candidate;
                    break;
                }
            }
            if (elite == null) return;
            if (elite.ServerScorchedBurstReleaseCount <= 0)
            {
                TeleportEncounterPointActor(actor, elite, 1.8f);
                return;
            }
            if (elite.IsAlive && !elite.ServerDefeatForEncounterVerification(actor)) return;
            LogEncounterStopReady(
                targetEncounter.StableId,
                $"scorchedBurst={elite.ServerScorchedBurstReleaseCount};eliteKilled={!elite.IsAlive}");
        }

        private void LogEncounterStopReady(string stableId, string evidence)
        {
            if (_autoEncounterReadyLogged) return;
            _autoEncounterReadyLogged = true;
            Debug.Log($"[M5C_ENCOUNTER_STOP_READY] id={stableId} evidence={evidence} authority=server");
        }

        private void TeleportEncounterPointActor(
            NetworkGymPlayer actor,
            NetworkGymEnemy enemy,
            float distance)
        {
            if (Time.unscaledTime < _nextAutoEnemyTeleportAt) return;
            _nextAutoEnemyTeleportAt = Time.unscaledTime + 0.7f;
            Vector3 direction = enemy.transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f) direction = Vector3.forward;
            Vector3 destination = enemy.transform.position + direction.normalized * distance;
            destination.y = actor.transform.position.y;
            Vector3 face = enemy.transform.position - destination;
            face.y = 0f;
            actor.ServerTeleportForSceneTransition(
                destination,
                face.sqrMagnitude > 0.001f ? Quaternion.LookRotation(face.normalized, Vector3.up) : Quaternion.identity);
        }

        private void TickWardenSuiteIfRequested()
        {
            if (!_autoWardenSuite || !IsSharedMainWorld || _worldObjective == null ||
                _worldObjective.ReplicatedStage != MainQuestStage.DefeatWarden || _warden == null) return;
            NetworkGymPlayer actor = FindRemoteDefenseTarget() ?? GetNearestAlivePlayer(_warden.transform.position);
            if (actor == null) return;

            if (_autoWardenSuiteStage == 0)
            {
                if (_warden.ReplicatedState == WardenState.Dormant || _warden.AttackSequence <= 0) return;
                if (!_warden.ServerApplySmokeDamage(actor, 100000f, 71201)) return;
                _autoWardenSuiteStage = 1;
                _autoWardenSuiteAt = Time.unscaledTime + 8f;
                Debug.Log("[M5_WARDEN_SUITE_PHASE_GATE] authority=server");
                return;
            }

            if (_autoWardenSuiteStage == 1)
            {
                if (_warden.ReplicatedPhase != WardenPhase.PhaseTwo) return;
                TeleportWardenSuiteActor(actor, 2f);
                _autoWardenSuiteStage = 2;
                _autoWardenSuiteAt = Time.unscaledTime + 12f;
                return;
            }

            if (_autoWardenSuiteStage == 2)
            {
                if (!_warden.ServerSawRuneCleave && Time.unscaledTime < _autoWardenSuiteAt) return;
                TeleportWardenSuiteActor(actor, 6f);
                _autoWardenSuiteStage = 3;
                _autoWardenSuiteAt = Time.unscaledTime + 14f;
                return;
            }

            if (_autoWardenSuiteStage == 3)
            {
                if (!_warden.ServerResolvedDelayedBlast) return;
                bool phaseCovered = _warden.ServerSawPhaseTransition;
                bool runeCovered = _warden.ServerSawRuneCleave;
                bool blastCovered = _warden.ServerResolvedDelayedBlast;
                if (!_warden.ServerApplySmokeDamage(actor, 100000f, 71202)) return;
                _autoWardenSuiteStage = 4;
                Debug.Log($"[M5_WARDEN_SUITE_COMPLETE] phase={phaseCovered} rune={runeCovered} " +
                          $"blast={blastCovered} authority=server");
            }
        }

        private void TeleportWardenSuiteActor(NetworkGymPlayer actor, float distance)
        {
            Vector3 direction = _warden.transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f) direction = Vector3.forward;
            Vector3 destination = _warden.transform.position + direction.normalized * distance;
            Vector3 face = _warden.transform.position - destination;
            face.y = 0f;
            actor.ServerTeleportForSceneTransition(
                destination,
                face.sqrMagnitude > 0.001f ? Quaternion.LookRotation(face, Vector3.up) : Quaternion.identity);
        }

        private NetworkGymEnemy FindEnemy(NetworkEnemyArchetype archetype)
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                NetworkGymEnemy enemy = _enemies[i];
                if (enemy != null && enemy.Archetype == archetype) return enemy;
            }
            return null;
        }

        private void TeleportEnemySuiteActor(
            NetworkGymPlayer actor,
            NetworkGymEnemy enemy,
            float distance)
        {
            if (Time.unscaledTime < _nextAutoEnemyTeleportAt) return;
            _nextAutoEnemyTeleportAt = Time.unscaledTime + 0.7f;
            Vector3 direction = enemy.transform.forward;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f) direction = Vector3.forward;
            Vector3 destination = enemy.transform.position + direction.normalized * distance;
            destination.y = actor.transform.position.y;
            Vector3 face = enemy.transform.position - destination;
            face.y = 0f;
            actor.ServerTeleportForWorldSmoke(
                destination,
                Quaternion.LookRotation(face.normalized, Vector3.up));
        }

        private void InjectRescueSmokeIfRequested()
        {
            if (!_autoRescueSmoke || _rescueSmokeInjected || Time.unscaledTime < _rescueSmokeAt) return;
            if (_autoWorldSmoke && (_worldObjective == null || !_worldObjective.RewardClaimed)) return;
            NetworkGymPlayer victim = null;
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkGymPlayer candidate = _players[i];
                if (candidate != null && candidate.OwnerClientId != NetworkManager.ServerClientId)
                {
                    victim = candidate;
                    break;
                }
            }
            if (victim == null) return;
            _rescueSmokeInjected = victim.ServerForceDownForSmoke();
            if (_rescueSmokeInjected)
                Debug.Log($"[M5_RESCUE_SMOKE_DOWNED] clientId={victim.OwnerClientId}");
        }

        private void TickDefenseSmokeIfRequested()
        {
            if (!_autoDefenseSmoke || _defenseGuardBreakInjected) return;
            NetworkGymPlayer target = FindRemoteDefenseTarget();
            if (target == null) return;

            if (!_defenseDodgeInjected)
            {
                if (target.ReplicatedCombatState != CombatState.Dodge) return;
                DamageResult result = target.ServerReceiveDamage(new DamageRequest(
                    -700, 7001, 20f, 18f, AttackTag.Light, true, true));
                if (!result.Invulnerable) return;
                _defenseDodgeInjected = true;
                Debug.Log($"[M5_DEFENSE_SMOKE_DODGE] clientId={target.OwnerClientId} invulnerable=true");
                return;
            }

            if (!_defensePerfectGuardInjected)
            {
                if (target.ReplicatedCombatState != CombatState.Guard) return;
                DamageResult result = target.ServerReceiveDamage(new DamageRequest(
                    -700, 7002, 20f, 18f, AttackTag.Light, true, true));
                if (!result.PerfectGuard) return;
                _defensePerfectGuardInjected = true;
                _defenseFollowUpAt = Time.unscaledTime + 0.22f;
                Debug.Log(
                    $"[M5_DEFENSE_SMOKE_PERFECT_GUARD] clientId={target.OwnerClientId} " +
                    $"counterPosture={result.CounterPostureDamage:F1}");
                return;
            }

            if (Time.unscaledTime < _defenseFollowUpAt ||
                target.ReplicatedCombatState != CombatState.Guard) return;

            DamageResult guardBreak = target.ServerReceiveDamage(new DamageRequest(
                -700, 7003, 20f, 120f, AttackTag.Heavy, true, true));
            if (!guardBreak.GuardBroken) return;
            _defenseGuardBreakInjected = true;
            Debug.Log(
                $"[M5_DEFENSE_SMOKE_COMPLETE] clientId={target.OwnerClientId} " +
                $"dodge={target.DodgeEvadeCount} perfect={target.PerfectGuardCount} " +
                $"break={target.GuardBreakCount}");
        }

        private void TickAttackSuiteSmokeIfRequested()
        {
            if (!_autoAttackSuiteSmoke || _attackSuiteCompleted) return;
            NetworkGymPlayer target = FindRemoteDefenseTarget();
            if (target == null) return;

            if (!_attackSuiteDamageInjected && target.RangedHitCount > 0 &&
                target.ReplicatedCombatState == CombatState.Locomotion)
            {
                _attackSuiteDamageInjected = true;
                if (target.Health >= 119.5f)
                {
                    DamageResult result = target.ServerReceiveDamage(new DamageRequest(
                        -709,
                        7091,
                        36f,
                        0f,
                        AttackTag.Projectile,
                        false,
                        false));
                    Debug.Log(
                        $"[M5_ATTACK_SUITE_HEAL_SETUP] clientId={target.OwnerClientId} " +
                        $"applied={result.AppliedDamage:F1} health={target.Health:F1}");
                }
                else
                {
                    Debug.Log(
                        $"[M5_ATTACK_SUITE_HEAL_SETUP] clientId={target.OwnerClientId} " +
                        $"usedExistingDamage=true health={target.Health:F1}");
                }
            }

            if (target.FullyChargedHeavyHitCount <= 0 || target.RangedHitCount <= 0 ||
                target.HealResolvedCount <= 0) return;

            _attackSuiteCompleted = true;
            Debug.Log(
                $"[M5_ATTACK_SUITE_COMPLETE] clientId={target.OwnerClientId} " +
                $"heavy={target.HeavyHitCount} fullHeavy={target.FullyChargedHeavyHitCount} " +
                $"ranged={target.RangedHitCount} heal={target.HealResolvedCount} " +
                $"flasks={target.HealingFlaskCharges} health={target.Health:F1}");
        }

        private void TickServerRescue()
        {
            if (!_rescue.IsRescueActive) return;
            NetworkGymPlayer rescuer = FindPlayer(_rescue.ActiveRescuerId);
            NetworkGymPlayer target = FindPlayer(_rescue.ActiveTargetId);
            bool connected = rescuer != null && target != null && rescuer.IsSpawned && target.IsSpawned;
            bool inRange = connected && IsInRescueRange(rescuer, target);
            NetworkRescueTickResult result = _rescue.Tick(
                Time.deltaTime,
                connected,
                inRange,
                out ulong rescuerId,
                out ulong targetId);

            if (result == NetworkRescueTickResult.None)
            {
                if (Time.unscaledTime >= _nextRescueProgressPublish)
                {
                    _nextRescueProgressPublish = Time.unscaledTime + 0.1f;
                    target?.ServerSetRescueProgress(_rescue.ProgressNormalized);
                }
                return;
            }

            target?.ServerSetRescueProgress(0f);
            if (result == NetworkRescueTickResult.Cancelled)
            {
                Debug.Log($"[M5_RESCUE_CANCELLED] reason=range-or-heartbeat rescuer={rescuerId} target={targetId}");
                return;
            }

            if (target != null && target.ServerRevive(ReviveHealthFraction))
                Debug.Log($"[M5_RESCUE_COMPLETED] rescuer={rescuerId} target={targetId} healthFraction={ReviveHealthFraction:F2}");
        }

        private void ResetPartyAfterDefeat()
        {
            _partyDefeated = false;
            _partyResetAt = -1f;
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkGymPlayer player = _players[i];
                if (player == null) continue;
                _rescue.MarkRevived(player.OwnerClientId);
                Vector3 spawn = _spawnPoints[Mathf.Min(i, _spawnPoints.Length - 1)];
                player.ServerResetAfterPartyDefeat(spawn);
            }
            for (int i = 0; i < _enemies.Count; i++) _enemies[i]?.ServerResetForRematch();
            if (_sanctumReady && _warden != null)
            {
                _warden.ServerResetEncounter();
                for (int i = 0; i < _players.Count; i++)
                {
                    NetworkGymPlayer player = _players[i];
                    if (player == null || _sanctumSpawnPoints == null || _sanctumSpawnPoints.Length == 0) continue;
                    player.ServerTeleportForSceneTransition(
                        _sanctumSpawnPoints[Mathf.Min(i, _sanctumSpawnPoints.Length - 1)],
                        Quaternion.identity);
                }
            }
            _enemyAttackQuota.Reset();
            Debug.Log($"[M5_PARTY_RESET] players-restored=true enemy-reset={_enemies.Count}");
        }

        private void UpdateEnemyAttackQuota()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                NetworkGymEnemy enemy = _enemies[i];
                if (enemy == null || !enemy.ServerUsesMeleeQuota) continue;
                _enemyAttackQuota.UpdateMember(
                    enemy.ServerCombatantId,
                    enemy.ServerQuotaEligible,
                    enemy.ServerQuotaCommitted);
            }
            _enemyAttackQuota.Resolve();
            for (int i = 0; i < _enemies.Count; i++)
            {
                NetworkGymEnemy enemy = _enemies[i];
                if (enemy == null) continue;
                enemy.ServerSetAttackAllowed(
                    !enemy.ServerUsesMeleeQuota || _enemyAttackQuota.IsAttackAllowed(enemy.ServerCombatantId));
            }
        }

        private bool HasEnemyReadyForReset()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                if (_enemies[i] != null && _enemies[i].ServerIsResetReady) return true;
            }
            return false;
        }

        private void ResetReadyEnemies()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                NetworkGymEnemy enemy = _enemies[i];
                if (enemy != null && enemy.ServerIsResetReady) enemy.ServerResetForRematch();
            }
        }

        private IEnumerable<NetworkGymEnemy> GetEnemiesNearestFirst(Vector3 position)
        {
            var ordered = new List<NetworkGymEnemy>(_enemies.Count);
            for (int i = 0; i < _enemies.Count; i++)
            {
                NetworkGymEnemy enemy = _enemies[i];
                if (enemy != null && enemy.IsSpawned && enemy.IsAlive) ordered.Add(enemy);
            }
            ordered.Sort((left, right) =>
                (left.transform.position - position).sqrMagnitude.CompareTo(
                    (right.transform.position - position).sqrMagnitude));
            return ordered;
        }

        private NetworkGymEnemy FindNearestAliveEnemy(Vector3 position)
        {
            NetworkGymEnemy nearest = null;
            float distance = float.MaxValue;
            for (int i = 0; i < _enemies.Count; i++)
            {
                NetworkGymEnemy enemy = _enemies[i];
                if (enemy == null || !enemy.IsSpawned || !enemy.IsAlive) continue;
                float candidate = (enemy.transform.position - position).sqrMagnitude;
                if (candidate >= distance) continue;
                distance = candidate;
                nearest = enemy;
            }
            return nearest;
        }

        private NetworkGymPlayer GetNearestDownedPlayer(NetworkGymPlayer rescuer, bool requireRange)
        {
            if (rescuer == null) return null;
            NetworkGymPlayer nearest = null;
            float nearestDistanceSquared = float.MaxValue;
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkGymPlayer candidate = _players[i];
                if (candidate == null || candidate == rescuer || !candidate.IsSpawned || !candidate.IsDowned) continue;
                Vector3 offset = candidate.transform.position - rescuer.transform.position;
                offset.y = 0f;
                float distanceSquared = offset.sqrMagnitude;
                if (requireRange && distanceSquared > RescueRange * RescueRange) continue;
                if (distanceSquared >= nearestDistanceSquared) continue;
                nearestDistanceSquared = distanceSquared;
                nearest = candidate;
            }
            return nearest;
        }

        private NetworkGymPlayer FindRemoteDefenseTarget()
        {
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkGymPlayer player = _players[i];
                if (player != null && player.IsSpawned &&
                    player.OwnerClientId != NetworkManager.ServerClientId) return player;
            }
            return null;
        }

        private NetworkGymPlayer FindPlayer(ulong clientId)
        {
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkGymPlayer player = _players[i];
                if (player != null && player.OwnerClientId == clientId) return player;
            }
            return null;
        }

        private static bool IsInRescueRange(NetworkGymPlayer rescuer, NetworkGymPlayer target)
        {
            Vector3 offset = rescuer.transform.position - target.transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= RescueRange * RescueRange;
        }

        private static bool HasCommandLineFlag(string prefix)
        {
            string[] arguments = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (!arguments[i].StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) continue;
                string value = arguments[i].Substring(prefix.Length).Trim();
                return string.Equals(value, "1", System.StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "true", System.StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private static string GetCommandLineValue(string prefix)
        {
            string[] arguments = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    return arguments[i].Substring(prefix.Length).Trim();
            }
            return string.Empty;
        }

        private void BeginSanctumUnload()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer)
            {
                SetPlayersInputSuppressed(false);
                return;
            }

            if (_warden != null && _warden.IsSpawned)
            {
                _warden.NetworkObject.Despawn(true);
                _warden = null;
            }

            Scene sanctum = SceneManager.GetSceneByName(_sanctumSceneName);
            if (!sanctum.IsValid() || !sanctum.isLoaded)
            {
                _sanctumReady = false;
                _sanctumSpawnPoints = null;
                SetPlayersInputSuppressed(false);
                Debug.LogWarning($"[M5_SANCTUM_UNLOAD_SKIPPED] scene={_sanctumSceneName} reason=not-loaded");
                return;
            }

            SceneEventProgressStatus status = manager.SceneManager.UnloadScene(sanctum);
            if (status != SceneEventProgressStatus.Started)
            {
                SetPlayersInputSuppressed(false);
                Debug.LogError($"[M5_SANCTUM_UNLOAD_FAILED] scene={_sanctumSceneName} status={status}");
                return;
            }

            _sanctumUnloading = true;
            _sanctumReady = false;
            Debug.Log($"[M5_SANCTUM_UNLOAD_STARTED] scene={_sanctumSceneName} authority=server");
        }

        private void HandleNetworkSceneEvent(SceneEvent sceneEvent)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer ||
                !string.Equals(sceneEvent.SceneName, _sanctumSceneName, System.StringComparison.Ordinal)) return;

            if (_sanctumUnloading && sceneEvent.SceneEventType == SceneEventType.UnloadEventCompleted)
            {
                _sanctumUnloading = false;
                _sanctumSpawnPoints = null;
                SetPlayersInputSuppressed(_worldObjective?.ResultPublished == true);
                Debug.Log($"[M5_SANCTUM_UNLOAD_COMPLETED] scene={_sanctumSceneName} authority=server");
                return;
            }

            if (!_sanctumLoading || sceneEvent.SceneEventType != SceneEventType.LoadEventCompleted) return;

            Scene sanctum = SceneManager.GetSceneByName(_sanctumSceneName);
            if (!sanctum.IsValid() || !sanctum.isLoaded)
            {
                Debug.LogError($"[M5_SANCTUM_LOAD_FAILED] scene={_sanctumSceneName} reason=scene-not-loaded");
                return;
            }

            Transform spawnA = FindTransformInScene(sanctum, "NetworkSanctum_PlayerSpawn_A");
            Transform spawnB = FindTransformInScene(sanctum, "NetworkSanctum_PlayerSpawn_B");
            Transform bossSpawn = FindTransformInScene(sanctum, "NetworkSanctum_WardenSpawn");
            if (spawnA == null || spawnB == null || bossSpawn == null)
            {
                Debug.LogError("[M5_SANCTUM_LOAD_FAILED] reason=required-anchor-missing");
                return;
            }

            _sanctumSpawnPoints = new[] { spawnA.position, spawnB.position };
            _movementBoundsCenter = new Vector2(bossSpawn.position.x, bossSpawn.position.z);
            _movementBoundsHalfExtents = new Vector2(10.5f, 10.5f);
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkGymPlayer player = _players[i];
                if (player == null) continue;
                Vector3 spawn = _sanctumSpawnPoints[Mathf.Min(i, _sanctumSpawnPoints.Length - 1)];
                Vector3 facing = bossSpawn.position - spawn;
                facing.y = 0f;
                player.ServerTeleportForSceneTransition(
                    spawn,
                    facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing, Vector3.up) : Quaternion.identity);
            }

            GameObject instance = Instantiate(_wardenPrefab, bossSpawn.position, bossSpawn.rotation);
            instance.name = "NetworkWarden_Ember";
            instance.GetComponent<NetworkObject>().Spawn(true);
            instance.GetComponent<NetworkWarden>().ServerSetCombatActive();
            _sanctumLoading = false;
            _sanctumReady = true;
            SetPlayersInputSuppressed(false);
            Debug.Log($"[M5_SANCTUM_LOAD_COMPLETED] scene={_sanctumSceneName} players={_players.Count} authority=server");
        }

        private void UpdateRunResultPresentation()
        {
            if (_worldObjective?.ResultPublished != true || _runResultPresentedLocally) return;
            _runResultPresentedLocally = true;
            NetworkRunResultSummary result = _worldObjective.ReplicatedRunResult;
            string role = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer ? "host" : "client";
            Debug.Log(
                $"[M5_RUN_RESULT_PRESENTED] role={role} sequence={result.Sequence} grade={result.Grade} " +
                $"elapsed={result.ElapsedSeconds} downed={result.DownedCount} secrets={result.SecretsFound} " +
                $"initialPlayers={result.InitialPlayerCount} connectedPlayers={result.ConnectedPlayerCount} " +
                $"teammateLeft={result.TeammateLeft}");
            if (Application.isBatchMode) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void SetPlayersInputSuppressed(bool value)
        {
            for (int i = 0; i < _players.Count; i++) _players[i]?.ServerSetInputSuppressed(value);
        }

        private static Transform FindTransformInScene(Scene scene, string objectName)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < transforms.Length; j++)
                    if (transforms[j].name == objectName) return transforms[j];
            }
            return null;
        }

        private IEnumerator ReturnClientToMenu()
        {
            yield return new WaitForSecondsRealtime(0.75f);
            SessionRuntime.Current.Shutdown();
            SceneManager.LoadScene("01_MainMenu", LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.SceneManager != null)
                manager.SceneManager.OnSceneEvent -= HandleNetworkSceneEvent;
        }

        private void OnGUI()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return;
            _titleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _bodyStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                normal = { textColor = new Color(0.86f, 0.9f, 0.93f) }
            };

            NetworkGymPlayer local = null;
            int readyCount = 0;
            for (int i = 0; i < _players.Count; i++)
            {
                NetworkGymPlayer player = _players[i];
                if (player == null) continue;
                if (player.IsReady) readyCount++;
                if (player.IsOwner) local = player;
            }

            if (_worldObjective?.ResultPublished == true)
            {
                DrawRunResultPanel();
                return;
            }

            // The formal shared route uses the owner-local player-facing HUD. Keep this
            // verbose diagnostics panel for Network Gym and spawn troubleshooting only.
            if (IsSharedMainWorld && local != null) return;

            NetworkGymPlayer downedTeammate = GetNearestDownedPlayer(local, false);
            GUILayout.BeginArea(new Rect(24f, 24f, 660f, 350f), GUI.skin.box);
            GUILayout.Label(
                $"{(IsSharedMainWorld ? "M5c 余烬谷联机主线" : "M5 NETWORK GYM")} / {Application.version}",
                _titleStyle);
            GUILayout.Label($"Session: {SessionRuntime.Current.Snapshot.Mode}  Players: {_players.Count}/2  Ready: {readyCount}/2", _bodyStyle);
            if (local == null)
                GUILayout.Label("Synchronizing player spawn...", _bodyStyle);
            else if (!local.IsReady)
                GUILayout.Label("Press E / Gamepad South to READY", _bodyStyle);
            else if (!local.MatchStarted)
                GUILayout.Label("Ready. Waiting for the other player...", _bodyStyle);
            else if (local.PartyDefeated)
                GUILayout.Label("PARTY DEFEATED - Server resetting encounter...", _bodyStyle);
            else if (local.IsDowned)
                GUILayout.Label($"DOWNED - wait for teammate rescue  |  Progress {local.RescueProgress:P0}", _bodyStyle);
            else
                GUILayout.Label(
                    $"Barrier open - WASD / Left Stick to move  |  Shift / Stick Press: sprint\n" +
                    $"LMB / Gamepad West: light combo\n" +
                    $"RMB / Right Shoulder: heavy  |  F: throwing knife  |  R: heal\n" +
                    $"Space / Gamepad East: dodge  |  Q / Left Shoulder: guard\n" +
                    $"Player HP {local.Health:F0}  Stamina {local.Stamina:F0}  Posture {local.Posture:F0}  State {local.ReplicatedCombatState}\n" +
                    $"Flasks {local.HealingFlaskCharges}  Knife CD {local.ApproximateRangedCooldownRemaining:F1}s  Heavy {local.ApproximateHeavyChargeNormalized:P0}\n" +
                    $"Attack proof: Heavy {local.HeavyHitCount} / Full {local.FullyChargedHeavyHitCount} / Ranged {local.RangedHitCount} / Heal {local.HealResolvedCount}\n" +
                    $"Defense: Dodge {local.DodgeEvadeCount}  Perfect guard {local.PerfectGuardCount}  " +
                    $"Perfect dodge {local.PerfectDodgeCount}  Break {local.GuardBreakCount}\n" +
                    $"Enemy HP {(_enemy != null ? _enemy.Health : 0f):F0}/{(_enemy != null ? _enemy.MaximumHealth : 0f):F0}  " +
                    $"State {(_enemy != null ? _enemy.ReplicatedState.ToString() : "Syncing")}\n" +
                    $"Shared objective: {(_worldObjective != null ? _worldObjective.GetObjectiveTextId() : "Syncing")}\n" +
                    $"Gate {(_worldObjective != null && _worldObjective.GateOpen ? "OPEN" : "CLOSED")}  " +
                    $"Unique reward {(_worldObjective != null && _worldObjective.RewardClaimed ? "CLAIMED" : "AVAILABLE/PENDING")}  " +
                    $"Owned {local.SharedRewardCount}\n" +
                    $"Co-op rescue: {(downedTeammate != null ? $"HOLD E near Client {downedTeammate.OwnerClientId}  {downedTeammate.RescueProgress:P0}" : "team active")}\n" +
                    $"Server corrections: {local.CorrectionCount}", _bodyStyle);
            GUILayout.Label("Hold E / Gamepad South: rescue  |  Tap E: interact  |  Esc / Menu: leave", _bodyStyle);
            GUILayout.EndArea();
        }

        private void DrawRunResultPanel()
        {
            NetworkRunResultSummary result = _worldObjective.ReplicatedRunResult;
            float width = 520f;
            float height = 360f;
            Rect panel = new Rect(
                (Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f,
                width,
                height);
            GUILayout.BeginArea(panel, GUI.skin.box);
            GUILayout.Space(18f);
            GUILayout.Label("双人协作路线完成", _titleStyle);
            GUILayout.Space(18f);
            GUILayout.Label($"评价：{result.Grade}", _titleStyle);
            GUILayout.Label($"用时：{FormatRunTime(result.ElapsedSeconds)}", _bodyStyle);
            GUILayout.Label($"倒地次数：{result.DownedCount}", _bodyStyle);
            GUILayout.Label($"发现秘密：{result.SecretsFound}", _bodyStyle);
            GUILayout.Label(
                $"结算时队伍：{result.ConnectedPlayerCount}/{result.InitialPlayerCount}",
                _bodyStyle);
            if (result.TeammateLeft)
                GUILayout.Label("队友在结算前离开；Server 已为剩余成员完成结算。", _bodyStyle);
            GUILayout.Space(22f);
            if (GUILayout.Button("返回主菜单", GUILayout.Height(48f)))
                ReturnToMainMenuFromResult();
            GUILayout.EndArea();
        }

        private void ReturnToMainMenuFromResult()
        {
            if (_returningToMenu) return;
            _returningToMenu = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SessionRuntime.Current.Shutdown();
            SceneManager.LoadScene("01_MainMenu", LoadSceneMode.Single);
        }

        private static string FormatRunTime(int elapsedSeconds)
        {
            int seconds = Mathf.Max(0, elapsedSeconds);
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
}
