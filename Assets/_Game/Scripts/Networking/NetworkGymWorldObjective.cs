using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Quests.Data;
using Emberfall.Quests.Domain;
using Unity.Netcode;
using UnityEngine;

namespace Emberfall.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkGymWorldObjective : NetworkBehaviour
    {
        private const float InteractionRange = 2.2f;
        private const string DefaultQuestId = "quest:forest-seal";
        private const string DefaultSealId = "seal:network-gym";
        private const string DefaultRewardId = "reward:network-ember";

        [SerializeField] private TextAsset _questDefinitions;
        [SerializeField] private string _questId = DefaultQuestId;
        [SerializeField] private string _sealId = DefaultSealId;
        [SerializeField] private string _rewardId = DefaultRewardId;
        [SerializeField] private Transform _sealAnchor;
        [SerializeField] private Transform _gateAnchor;
        [SerializeField] private Transform _rewardAnchor;
        [SerializeField] private Transform _scoutAnchor;
        [SerializeField] private GameObject _sealCore;
        [SerializeField] private GameObject _gateBlocker;
        [SerializeField] private GameObject _rewardVisual;
        [SerializeField] private Renderer _sealMarker;

        private readonly NetworkVariable<int> _questStage = new NetworkVariable<int>(
            (int)MainQuestStage.MeetScout,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _activatedSealCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _gateOpen = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _rewardClaimed = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<ulong> _rewardClaimantId = new NetworkVariable<ulong>(
            ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _bossRouteEnabled = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _resultPublished = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _resultSequence = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _resultElapsedSeconds = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _resultDownedCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _resultSecretsFound = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _resultInitialPlayers = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _resultConnectedPlayers = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _resultTeammateLeft = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> _resultGrade = new NetworkVariable<int>(
            (int)NetworkRunGrade.C,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private NetworkGymSceneController _controller;
        private MainQuestState _quest;
        private UniqueRewardClaimState _reward;
        private bool _active;
        private bool _bossRoute;
        private bool BossRoute => _bossRoute || _bossRouteEnabled.Value;

        public MainQuestStage ReplicatedStage => (MainQuestStage)_questStage.Value;
        public int ActivatedSealCount => _activatedSealCount.Value;
        public bool GateOpen => _gateOpen.Value;
        public bool RewardClaimed => _rewardClaimed.Value;
        public ulong RewardClaimantId => _rewardClaimantId.Value;
        public bool ResultPublished => _resultPublished.Value;
        public float PlayerInteractionRange => InteractionRange;
        public NetworkRunResultSummary ReplicatedRunResult => !_resultPublished.Value
            ? default
            : new NetworkRunResultSummary(
                _resultSequence.Value,
                _resultElapsedSeconds.Value,
                _resultDownedCount.Value,
                _resultSecretsFound.Value,
                _resultInitialPlayers.Value,
                _resultConnectedPlayers.Value,
                _resultTeammateLeft.Value,
                (NetworkRunGrade)_resultGrade.Value);

        public void Configure(
            TextAsset questDefinitions,
            Transform sealAnchor,
            Transform gateAnchor,
            Transform rewardAnchor,
            GameObject sealCore,
            GameObject gateBlocker,
            GameObject rewardVisual,
            Renderer sealMarker,
            string questId = DefaultQuestId,
            string sealId = DefaultSealId,
            string rewardId = DefaultRewardId)
        {
            _questDefinitions = questDefinitions;
            _sealAnchor = sealAnchor;
            _gateAnchor = gateAnchor;
            _rewardAnchor = rewardAnchor;
            _sealCore = sealCore;
            _gateBlocker = gateBlocker;
            _rewardVisual = rewardVisual;
            _sealMarker = sealMarker;
            _questId = string.IsNullOrWhiteSpace(questId) ? DefaultQuestId : questId;
            _sealId = string.IsNullOrWhiteSpace(sealId) ? DefaultSealId : sealId;
            _rewardId = string.IsNullOrWhiteSpace(rewardId) ? DefaultRewardId : rewardId;
        }

        public void ConfigureBossRoute(Transform scoutAnchor)
        {
            _scoutAnchor = scoutAnchor;
            _bossRoute = scoutAnchor != null;
        }

        public override void OnNetworkSpawn()
        {
            _questStage.OnValueChanged += OnQuestStageChanged;
            _gateOpen.OnValueChanged += OnPresentationFactChanged;
            _rewardClaimed.OnValueChanged += OnPresentationFactChanged;
            _resultPublished.OnValueChanged += OnResultPublishedChanged;
            _controller = NetworkGymSceneController.Find();
            _controller?.Register(this);
            if (IsServer)
            {
                _bossRouteEnabled.Value = _bossRoute;
                InitializeServerDomain();
            }
            ApplyPresentation();
            Debug.Log($"[M5_NETWORK_WORLD_SPAWNED] server={IsServer} objectId={NetworkObjectId}");
        }

        public override void OnNetworkDespawn()
        {
            _questStage.OnValueChanged -= OnQuestStageChanged;
            _gateOpen.OnValueChanged -= OnPresentationFactChanged;
            _rewardClaimed.OnValueChanged -= OnPresentationFactChanged;
            _resultPublished.OnValueChanged -= OnResultPublishedChanged;
            _controller?.Unregister(this);
        }

        internal void ServerSetActive()
        {
            if (!IsServer || _quest == null || _active) return;
            _active = true;
            _quest.TalkToScout();
            PublishServerState();
            Debug.Log("[M5_WORLD_OBJECTIVE_STARTED] authority=server stage=ActivateSeals");
        }

        internal bool ServerTryInteract(NetworkGymPlayer player, out string reason)
        {
            if (!IsServer || !_active || _quest == null || _reward == null || player == null || player.IsDead)
            {
                reason = "world-not-ready";
                return false;
            }

            if (_quest.Stage == MainQuestStage.ActivateSeals)
            {
                if (!IsWithinRange(player.transform.position, _sealAnchor))
                {
                    reason = "seal-out-of-range";
                    return false;
                }

                ContentId sealId = new ContentId(_sealId);
                if (!_quest.ActivateSeal(sealId))
                {
                    reason = "seal-already-activated";
                    return false;
                }

                PublishServerState();
                reason = "seal-activated";
                Debug.Log($"[M5_WORLD_SEAL_ACTIVATED] clientId={player.OwnerClientId} seal={sealId}");
                return true;
            }

            if (_quest.Stage == MainQuestStage.EnterSanctum)
            {
                if (!IsWithinRange(player.transform.position, _gateAnchor))
                {
                    reason = "gate-out-of-range";
                    return false;
                }

                if (BossRoute)
                {
                    if (_controller == null)
                    {
                        reason = "scene-controller-missing";
                        return false;
                    }
                    if (!_controller.ServerBeginSanctumTransition(out reason)) return false;
                }

                if (!_quest.EnterSanctum())
                {
                    reason = "gate-stage-rejected";
                    return false;
                }

                _gateOpen.Value = true;
                PublishServerState();
                reason = "sanctum-opened";
                Debug.Log($"[M5_WORLD_GATE_OPENED] clientId={player.OwnerClientId} authority=server");
                return true;
            }

            if (_quest.Stage == MainQuestStage.DefeatWarden && !_reward.IsClaimed)
            {
                if (BossRoute)
                {
                    reason = "warden-still-active";
                    return false;
                }
                if (!IsWithinRange(player.transform.position, _rewardAnchor))
                {
                    reason = "reward-out-of-range";
                    return false;
                }

                ContentId rewardId = new ContentId(_rewardId);
                if (!_reward.TryClaim(rewardId, player.OwnerClientId, out reason)) return false;
                player.ServerGrantSharedReward();
                _rewardClaimed.Value = true;
                _rewardClaimantId.Value = player.OwnerClientId;
                ApplyPresentation();
                reason = "reward-claimed";
                Debug.Log(
                    $"[M5_WORLD_REWARD_CLAIMED] clientId={player.OwnerClientId} reward={rewardId} unique=true");
                return true;
            }

            if (BossRoute && _quest.Stage == MainQuestStage.ReturnToScout)
            {
                if (!IsWithinRange(player.transform.position, _scoutAnchor))
                {
                    reason = "scout-out-of-range";
                    return false;
                }
                if (!_quest.TalkToScout())
                {
                    reason = "return-stage-rejected";
                    return false;
                }
                ContentId rewardId = new ContentId(_rewardId);
                if (_reward.TryClaim(rewardId, player.OwnerClientId, out _))
                {
                    player.ServerGrantSharedReward();
                    _rewardClaimed.Value = true;
                    _rewardClaimantId.Value = player.OwnerClientId;
                    Debug.Log(
                        $"[M5_WORLD_REWARD_CLAIMED] clientId={player.OwnerClientId} reward={rewardId} unique=true");
                }
                PublishServerState();
                reason = "route-complete";
                Debug.Log($"[M5_WORLD_COMPLETE] clientId={player.OwnerClientId} authority=server");
                string resultReason = "scene-controller-missing";
                if (_controller == null || !_controller.ServerFinalizeSharedRoute(out resultReason))
                    Debug.LogError($"[M5_RUN_RESULT_FAILED] reason={resultReason}");
                return true;
            }

            reason = _reward.IsClaimed ? "already-claimed" : "no-active-objective";
            return false;
        }

        internal bool ServerMarkWardenDefeated()
        {
            if (!IsServer || !BossRoute || _quest == null || !_quest.DefeatWarden()) return false;
            PublishServerState();
            Debug.Log("[M5_WORLD_WARDEN_DEFEATED] stage=ReturnToScout authority=server");
            return true;
        }

        internal bool ServerPublishRunResult(NetworkRunResultSummary result, out string reason)
        {
            if (!IsServer || !BossRoute || _quest == null || !_quest.IsComplete)
            {
                reason = "result-stage-not-ready";
                return false;
            }
            if (!result.IsValid)
            {
                reason = "result-invalid";
                return false;
            }
            if (_resultPublished.Value)
            {
                reason = "result-already-published";
                return false;
            }

            _resultSequence.Value = result.Sequence;
            _resultElapsedSeconds.Value = result.ElapsedSeconds;
            _resultDownedCount.Value = result.DownedCount;
            _resultSecretsFound.Value = result.SecretsFound;
            _resultInitialPlayers.Value = result.InitialPlayerCount;
            _resultConnectedPlayers.Value = result.ConnectedPlayerCount;
            _resultTeammateLeft.Value = result.TeammateLeft;
            _resultGrade.Value = (int)result.Grade;
            _resultPublished.Value = true;
            reason = string.Empty;
            Debug.Log(
                $"[M5_RUN_RESULT_PUBLISHED] sequence={result.Sequence} grade={result.Grade} " +
                $"elapsed={result.ElapsedSeconds} downed={result.DownedCount} secrets={result.SecretsFound} " +
                $"players={result.ConnectedPlayerCount}/{result.InitialPlayerCount} teammateLeft={result.TeammateLeft} authority=server");
            return true;
        }

        public bool TryGetCurrentInteractionTarget(out Vector3 position)
        {
            if (ReplicatedStage == MainQuestStage.ActivateSeals && _sealAnchor != null)
            {
                position = _sealAnchor.position;
                return true;
            }

            if (ReplicatedStage == MainQuestStage.EnterSanctum && _gateAnchor != null)
            {
                position = _gateAnchor.position;
                return true;
            }

            if (!BossRoute && ReplicatedStage == MainQuestStage.DefeatWarden && !RewardClaimed && _rewardAnchor != null)
            {
                position = _rewardAnchor.position;
                return true;
            }

            if (BossRoute && ReplicatedStage == MainQuestStage.ReturnToScout && _scoutAnchor != null)
            {
                position = _scoutAnchor.position;
                return true;
            }

            position = default;
            return false;
        }

        public string GetObjectiveTextId()
        {
            return ReplicatedStage switch
            {
                MainQuestStage.MeetScout => "text:network.quest.syncing",
                MainQuestStage.ActivateSeals => "text:network.quest.activate-seal",
                MainQuestStage.EnterSanctum => "text:network.quest.enter-sanctum",
                MainQuestStage.DefeatWarden when BossRoute => "text:network.quest.defeat-warden",
                MainQuestStage.DefeatWarden when !RewardClaimed => "text:network.quest.claim-reward",
                MainQuestStage.ReturnToScout => "text:network.quest.return-scout",
                MainQuestStage.Complete => "text:network.quest.complete",
                _ => "text:network.quest.complete"
            };
        }

        public string GetInteractionTextId()
        {
            return ResolveInteractionTextId(ReplicatedStage, BossRoute, RewardClaimed);
        }

        public static string ResolveInteractionTextId(
            MainQuestStage stage,
            bool bossRoute,
            bool rewardClaimed)
        {
            return stage switch
            {
                MainQuestStage.ActivateSeals => "text:interaction.activate-forest-seal",
                MainQuestStage.EnterSanctum => "text:interaction.enter-sanctum",
                MainQuestStage.DefeatWarden when !bossRoute && !rewardClaimed => "text:network.quest.claim-reward",
                MainQuestStage.ReturnToScout when bossRoute => "text:interaction.finish-quest",
                _ => string.Empty
            };
        }

        public bool IsWithinInteractionRange(Vector3 playerPosition)
        {
            return TryGetCurrentInteractionTarget(out Vector3 position) &&
                   PlanarDistanceSquared(playerPosition, position) <= InteractionRange * InteractionRange;
        }

        private void InitializeServerDomain()
        {
            string json = ContentPackageRuntime.IsInitialized
                ? ContentPackageRuntime.GetRequiredText(RuntimeContentPaths.Quests)
                : (_questDefinitions != null ? _questDefinitions.text : string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogError("[M5_NETWORK_WORLD] Quest definitions are unavailable.", this);
                enabled = false;
                return;
            }

            ContentId questId = new ContentId(_questId);
            ContentId rewardId = new ContentId(_rewardId);
            QuestDefinition definition = QuestDefinitionJsonLoader.Load(json).GetRequired(questId);
            _quest = new MainQuestState(definition);
            _reward = new UniqueRewardClaimState(rewardId);
            PublishServerState();
        }

        private void PublishServerState()
        {
            _questStage.Value = (int)_quest.Stage;
            _activatedSealCount.Value = _quest.ActivatedSealCount;
            ApplyPresentation();
        }

        private bool IsWithinRange(Vector3 playerPosition, Transform anchor)
        {
            if (anchor == null) return false;
            float x = playerPosition.x - anchor.position.x;
            float z = playerPosition.z - anchor.position.z;
            return (x * x) + (z * z) <= InteractionRange * InteractionRange;
        }

        private static float PlanarDistanceSquared(Vector3 first, Vector3 second)
        {
            float x = first.x - second.x;
            float z = first.z - second.z;
            return (x * x) + (z * z);
        }

        private void OnQuestStageChanged(int previous, int current)
        {
            ApplyPresentation();
            if (!IsServer)
                Debug.Log($"[M5_WORLD_STAGE_SYNC] previous={(MainQuestStage)previous} current={(MainQuestStage)current}");
        }

        private void OnPresentationFactChanged(bool previous, bool current)
        {
            ApplyPresentation();
            if (!IsServer)
                Debug.Log($"[M5_WORLD_FACT_SYNC] previous={previous} current={current}");
        }

        private void OnResultPublishedChanged(bool previous, bool current)
        {
            if (!current || IsServer) return;
            Debug.Log("[M5_RUN_RESULT_SYNC] published=true");
        }

        private void ApplyPresentation()
        {
            bool sealActivated = ReplicatedStage >= MainQuestStage.EnterSanctum;
            if (_sealCore != null) _sealCore.SetActive(!sealActivated);
            if (_gateBlocker != null) _gateBlocker.SetActive(!GateOpen);
            if (_rewardVisual != null) _rewardVisual.SetActive(
                BossRoute ? ReplicatedStage == MainQuestStage.ReturnToScout && !RewardClaimed : GateOpen && !RewardClaimed);
            if (_sealMarker == null) return;
            _sealMarker.gameObject.SetActive(ReplicatedStage == MainQuestStage.ActivateSeals);

            Color color = sealActivated
                ? new Color(0.16f, 0.9f, 0.62f)
                : new Color(0.82f, 0.24f, 1f);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_EmissionColor", color * 1.8f);
            _sealMarker.SetPropertyBlock(block);
        }
    }
}
