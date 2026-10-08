using System;
using System.Globalization;
using System.IO;
using Emberfall.AI.Unity;
using Emberfall.Application.Content;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Infrastructure.Saves;
using Emberfall.Infrastructure.Storage;
using Emberfall.Quests.Data;
using Emberfall.Quests.Domain;
using UnityEngine;

namespace Emberfall.Application.Flow
{
    [DefaultExecutionOrder(-80)]
    public sealed class M2RouteFlowController : MonoBehaviour
    {
        private static readonly ContentId MainQuestId = new ContentId("quest:emberfall.main");
        private static readonly ContentId NewGameTextId = new ContentId("text:message.new-game");
        private static readonly ContentId LoadedTextId = new ContentId("text:message.loaded");
        private static readonly ContentId RecoveredTextId = new ContentId("text:message.recovered");
        private static readonly ContentId SavedTextId = new ContentId("text:message.saved");
        private static readonly ContentId ForestSealId = new ContentId("seal:forest");
        private static readonly ContentId BridgeSealId = new ContentId("seal:bridge");
        private static readonly ContentId CourtyardSealId = new ContentId("seal:courtyard");
        private const string BridgeMechanismAId = "bridge-mechanism:A";
        private const string BridgeMechanismBId = "bridge-mechanism:B";

        [SerializeField] private TextAsset _questDefinitionsJson;
        [SerializeField] private TextAsset _localizedTextsJson;
        [SerializeField] private PlayerCombatActor _player;
        [SerializeField] private Transform _defaultCheckpoint;
        [SerializeField] private WardenActor _warden;
        [SerializeField] private GameObject _wardenEntranceBarrier;
        private M2StageBarrier _wardenBarrierPresentation;
        [SerializeField] private ForestSealTemplateCoordinator _forestTemplate;
        [SerializeField] private GameObject _ashReturnRoot;
        [SerializeField] private RouteChoiceEncounterModifier _reinforcementModifier;
        private Vector3[] _ashReturnSpawnPoints = Array.Empty<Vector3>();
        private bool _ashReturnActivationPending;
        [SerializeField] private string _saveFileName = "emberfall-save-v1.json";

        private LocalizedTextCatalog _texts;
        private QuestDefinition _definition;
        private MainQuestState _quest;
        private JsonSaveGameStore _store;
        private string _savePathOverride;
        private ContentId _lastMessageTextId;
        private PacingTelemetryRecorder _pacing;
        private CombatEncounterCoordinator[] _encounterCoordinators = Array.Empty<CombatEncounterCoordinator>();
        private bool _encounterTelemetrySubscribed;
        private SummonerEnemyActor[] _summoners = Array.Empty<SummonerEnemyActor>();
        private RouteEnrichmentState _routeEnrichment;
        private SealConditionState _sealConditions;
        private ShieldEnemyActor _courtyardElite;

        public MainQuestState Quest => _quest;
        public MainQuestStage Stage => _quest?.Stage ?? MainQuestStage.MeetScout;
        public SaveLoadStatus LoadStatus { get; private set; }
        public int DeathCount { get; private set; }
        public float SessionElapsedSeconds { get; private set; }
        public bool IsInitialized { get; private set; }
        public bool AshApproachCleared => _routeEnrichment?.AshApproachCleared == true;
        public bool AshGuardPassCleared => _routeEnrichment?.AshGuardPassCleared == true;
        public bool AshReturnCleared => _routeEnrichment?.AshReturnCleared == true;
        public bool SupplyCartClaimed => _routeEnrichment?.SupplyCartClaimed == true;
        public float SupplyCartHeavyPostureMultiplier => _definition?.SupplyCartHeavyPostureMultiplier ?? 1f;
        public string SupplyCartRewardPercent => ((SupplyCartHeavyPostureMultiplier - 1f) * 100f).ToString("0.#", CultureInfo.InvariantCulture);
        public string SupplyCartStatus => SupplyCartClaimed ? $"已取得（重击架势 +{SupplyCartRewardPercent}%）" : "未取得";
        public bool AshReturnActivationPending => _ashReturnActivationPending;
        public AshReinforcementChoice ReinforcementChoice => _routeEnrichment?.ReinforcementChoice ?? AshReinforcementChoice.None;
        public string ReinforcementChoiceStatus => ReinforcementChoice switch
        {
            AshReinforcementChoice.Staged => "守住窄口 · 两人先出、后补一人",
            AshReinforcementChoice.Together => "引到空地 · 三人同时在场",
            _ => "未选择（原两人组合）"
        };
        public bool IsComplete => _quest?.IsComplete == true;
        public bool IsSanctumOpen => Stage >= MainQuestStage.DefeatWarden;
        public string SavePath => _store?.SavePath ?? string.Empty;
        public string QuestTitle => Resolve(new ContentId("text:quest.main.title"));
        public string RouteTitle => Resolve(new ContentId("text:ui.route-title"));
        public string LastMessage => _lastMessageTextId.IsEmpty ? string.Empty :
            _lastMessageTextId.Value == "text:message.supply-cart-claimed"
                ? string.Format(CultureInfo.CurrentCulture, Resolve(_lastMessageTextId), SupplyCartRewardPercent)
                : Resolve(_lastMessageTextId);
        public ForestSealTemplateCoordinator ForestTemplate => _forestTemplate;
        public WardenActor Warden => _warden;
        public bool IsWardenEncounterActive { get; private set; }
        public string PacingRunId => _pacing?.RunId ?? string.Empty;
        public bool WatchtowerDiscovered => _routeEnrichment?.WatchtowerDiscovered == true;
        public bool RiskRouteRewardClaimed => _routeEnrichment?.RiskRewardClaimed == true;
        public bool BridgeEncounterCleared => _sealConditions?.BridgeEncounterCleared == true;
        public bool BridgeMechanismAActivated => _sealConditions?.BridgeMechanismAActivated == true;
        public bool BridgeMechanismBActivated => _sealConditions?.BridgeMechanismBActivated == true;
        public bool CourtyardGuardBroken => _sealConditions?.CourtyardGuardBroken == true;
        public EmberValleyRouteChoice RouteChoice =>
            _routeEnrichment?.RouteChoice ?? EmberValleyRouteChoice.None;
        public string RouteChoiceStatus => RouteChoice switch
        {
            EmberValleyRouteChoice.Supply => "补给路线",
            EmberValleyRouteChoice.Risk => "险径路线",
            _ => "未选择"
        };
        public string SealConditionChecklist => Stage != MainQuestStage.ActivateSeals || _quest == null
            ? string.Empty
            : SealConditionChecklistText.Build(
                Resolve,
                !_quest.IsSealActivated(BridgeSealId),
                BridgeMechanismAActivated,
                BridgeEncounterCleared,
                BridgeMechanismBActivated,
                !_quest.IsSealActivated(CourtyardSealId),
                CourtyardGuardBroken);
        public string RuneStatus => _forestTemplate?.RuneChoice switch
        {
            ForestRuneChoice.Ember => Resolve(new ContentId("text:ui.rune-status.ember")),
            ForestRuneChoice.Guard => Resolve(new ContentId("text:ui.rune-status.guard")),
            _ => Resolve(new ContentId("text:ui.rune-status.none"))
        };

        public string CurrentObjective
        {
            get
            {
                if (Stage == MainQuestStage.ActivateSeals && _forestTemplate != null &&
                    _forestTemplate.Phase != ForestSealPhase.Completed)
                {
                    ContentId forestObjective = _forestTemplate.ObjectiveTextId;
                    if (!forestObjective.IsEmpty)
                    {
                        return Resolve(forestObjective);
                    }
                }

                ContentId textId = Stage switch
                {
                    MainQuestStage.MeetScout => new ContentId("text:quest.main.meet-scout"),
                    MainQuestStage.ActivateSeals => new ContentId("text:quest.main.activate-seals"),
                    MainQuestStage.EnterSanctum => new ContentId("text:quest.main.enter-sanctum"),
                    MainQuestStage.DefeatWarden => new ContentId("text:quest.main.defeat-warden"),
                    MainQuestStage.ReturnToScout => new ContentId("text:quest.main.return-scout"),
                    _ => new ContentId("text:quest.main.complete")
                };
                string template = Resolve(textId);
                return Stage == MainQuestStage.ActivateSeals
                    ? string.Format(CultureInfo.InvariantCulture, template, _quest.ActivatedSealCount, _quest.RequiredSealCount)
                    : template;
            }
        }

        public void Configure(
            TextAsset questDefinitionsJson,
            TextAsset localizedTextsJson,
            PlayerCombatActor player,
            Transform defaultCheckpoint,
            WardenActor warden,
            GameObject wardenEntranceBarrier,
            string savePathOverride = null)
        {
            _questDefinitionsJson = questDefinitionsJson;
            _localizedTextsJson = localizedTextsJson;
            _player = player;
            _defaultCheckpoint = defaultCheckpoint;
            _warden = warden;
            _wardenEntranceBarrier = wardenEntranceBarrier;
            _savePathOverride = savePathOverride;
        }

        public void SetForestTemplate(ForestSealTemplateCoordinator forestTemplate)
        {
            _forestTemplate = forestTemplate;
        }

#if UNITY_EDITOR
        public static string EditorTestSavePath { get; set; }
#endif

        private void Awake()
        {
            try
            {
#if UNITY_EDITOR
                // Direct scene Play has no bootstrap scene. Use the real validated content/Lua path,
                // rather than letting the forest start with a partial editor-only rules fallback.
                if (!ContentPackageRuntime.IsInitialized && !Emberfall.Infrastructure.Bootstrap.AppBootstrap.IsInitialized)
                    new GameObject("[Editor Direct Play Bootstrap]").AddComponent<Emberfall.Infrastructure.Bootstrap.AppBootstrap>();
#endif
                Initialize();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_player != null)
            {
                _player.Died += OnPlayerDied;
                _player.CombatProgressed += OnCombatProgressed;
            }

            if (_warden != null)
            {
                _warden.EncounterStarted += OnWardenEncounterStarted;
                _warden.EncounterReset += OnWardenEncounterReset;
                _warden.Died += OnWardenDied;
            }

            SubscribeEncounterTelemetry();
            SubscribeCourtyardElite();
        }

        private void OnDisable()
        {
            if (_player != null)
            {
                _player.Died -= OnPlayerDied;
                _player.CombatProgressed -= OnCombatProgressed;
            }

            if (_warden != null)
            {
                _warden.EncounterStarted -= OnWardenEncounterStarted;
                _warden.EncounterReset -= OnWardenEncounterReset;
                _warden.Died -= OnWardenDied;
            }

            UnsubscribeEncounterTelemetry();
            UnsubscribeCourtyardElite();
        }

        private void Update()
        {
            if (IsInitialized && _ashReturnActivationPending) TryActivateReturnEncounter();
            if (IsInitialized && !IsComplete)
            {
                SessionElapsedSeconds += Time.unscaledDeltaTime;
            }
        }

        public bool CanTalkToScout() => Stage == MainQuestStage.MeetScout || Stage == MainQuestStage.ReturnToScout;

        public bool CanActivateSeal(ContentId sealId)
        {
            if (Stage != MainQuestStage.ActivateSeals || _quest.IsSealActivated(sealId))
            {
                return false;
            }

            if (sealId != ForestSealId && _forestTemplate != null &&
                _forestTemplate.Phase == ForestSealPhase.RuneChoice)
            {
                return false;
            }

            if (sealId == ForestSealId)
            {
                return _forestTemplate == null || _forestTemplate.CanActivateSeal;
            }

            if (sealId == BridgeSealId)
            {
                return _sealConditions?.CanActivateBridgeSeal == true;
            }

            if (sealId == CourtyardSealId)
            {
                return _sealConditions?.CourtyardGuardBroken == true;
            }

            return true;
        }

        public bool CanAttemptSeal(ContentId sealId) =>
            IsInitialized && Stage == MainQuestStage.ActivateSeals && !sealId.IsEmpty &&
            sealId.Value.StartsWith("seal:", StringComparison.Ordinal) && !_quest.IsSealActivated(sealId);

        public bool CanAttemptBridgeMechanism(string mechanismId) =>
            IsInitialized && Stage == MainQuestStage.ActivateSeals &&
            ((mechanismId == BridgeMechanismAId && !BridgeMechanismAActivated) ||
             (mechanismId == BridgeMechanismBId && !BridgeMechanismBActivated));

        public bool CanActivateBridgeMechanism(string mechanismId) =>
            CanAttemptBridgeMechanism(mechanismId) &&
            (mechanismId == BridgeMechanismAId || BridgeEncounterCleared);

        public bool TryActivateBridgeMechanism(string mechanismId)
        {
            if (!CanAttemptBridgeMechanism(mechanismId))
            {
                return false;
            }

            if (mechanismId == BridgeMechanismBId && !BridgeEncounterCleared)
            {
                _lastMessageTextId = new ContentId("text:message.bridge-mechanism-b-blocked");
                return false;
            }

            bool activated = mechanismId == BridgeMechanismAId
                ? _sealConditions.TryActivateBridgeMechanismA()
                : mechanismId == BridgeMechanismBId && _sealConditions.TryActivateBridgeMechanismB();
            if (!activated)
            {
                return false;
            }

            _lastMessageTextId = new ContentId(mechanismId == BridgeMechanismAId
                ? "text:message.bridge-mechanism-a-activated"
                : "text:message.bridge-mechanism-b-activated");
            SaveProgress(false);
            _pacing?.RecordMilestone(
                mechanismId == BridgeMechanismAId ? "bridge-mechanism-a" : "bridge-mechanism-b",
                "activated",
                Stage,
                DeathCount);
            return true;
        }

        public bool CanEnterSanctum() => Stage == MainQuestStage.EnterSanctum;

        public bool CanDiscoverWatchtower() =>
            IsInitialized && !IsComplete && Stage >= MainQuestStage.ActivateSeals && !WatchtowerDiscovered;

        public bool CanClaimSupplyCart() => IsInitialized && !IsComplete && Stage >= MainQuestStage.ActivateSeals &&
            !SupplyCartClaimed && SupplyCartHeavyPostureMultiplier > 1f && _player != null && _player.IsAvailable;

        public bool TryClaimSupplyCart(PlayerCombatActor actor)
        {
            // Reject an invalid recipient before committing the permanent save fact.
            if (actor != _player || !CanClaimSupplyCart() ||
                !actor.ApplySupplyCartHeavyPostureReward(SupplyCartHeavyPostureMultiplier)) return false;
            if (!_routeEnrichment.TryClaimSupplyCart()) return false;
            _lastMessageTextId = new ContentId("text:message.supply-cart-claimed");
            SaveProgress(false);
            _pacing?.RecordMilestone("abandoned-supply-cart", "claimed", Stage, DeathCount);
            return true;
        }

        public bool CanChooseRoute() =>
            IsInitialized && !IsComplete && Stage >= MainQuestStage.ActivateSeals &&
            Stage < MainQuestStage.DefeatWarden && RouteChoice == EmberValleyRouteChoice.None;

        public bool TryDiscoverWatchtower(PlayerCombatActor actor)
        {
            if (actor == null || !CanDiscoverWatchtower() || !_routeEnrichment.TryDiscoverWatchtower() ||
                !actor.ApplyHealingFlaskCapacityBonus(1))
            {
                return false;
            }

            _lastMessageTextId = new ContentId("text:message.watchtower-discovered");
            SaveProgress(false);
            _pacing?.RecordMilestone("old-watchtower", "discovered", Stage, DeathCount);
            _pacing?.RecordMilestone("watchtower-flask", "upgraded", Stage, DeathCount);
            return true;
        }

        public bool TryChooseRoute(EmberValleyRouteChoice choice)
        {
            if (!CanChooseRoute() || !_routeEnrichment.TryChooseRoute(choice))
            {
                return false;
            }

            bool rewardGranted = TryGrantRiskRouteReward();
            _lastMessageTextId = new ContentId(rewardGranted
                ? "text:message.route-risk-reward"
                : choice == EmberValleyRouteChoice.Supply
                ? "text:message.route-supply-chosen"
                : "text:message.route-risk-chosen");
            SaveProgress(false);
            _pacing?.RecordMilestone(
                choice == EmberValleyRouteChoice.Supply ? "route-choice-supply" : "route-choice-risk",
                "selected",
                Stage,
                DeathCount);
            return true;
        }

        public bool CanChooseReinforcement() => IsInitialized && !IsComplete &&
            Stage >= MainQuestStage.ActivateSeals && Stage < MainQuestStage.DefeatWarden &&
            ReinforcementChoice == AshReinforcementChoice.None && !AshGuardPassCleared &&
            _reinforcementModifier != null && !_reinforcementModifier.ReinforcementChoiceClosed;

        public bool TryChooseReinforcement(AshReinforcementChoice choice)
        {
            if (!CanChooseReinforcement() || !_routeEnrichment.TryChooseReinforcement(choice)) return false;
            _reinforcementModifier.ApplyReinforcementChoice(choice);
            _lastMessageTextId = new ContentId(choice == AshReinforcementChoice.Staged
                ? "text:message.reinforcement-staged" : "text:message.reinforcement-together");
            SaveProgress(false);
            _pacing?.RecordMilestone(choice == AshReinforcementChoice.Staged
                ? "reinforcement-choice:staged" : "reinforcement-choice:together", "selected", Stage, DeathCount);
            return true;
        }

        public void NotifyReinforcementNavigationWaiting()
        {
            if (IsInitialized) _lastMessageTextId = new ContentId("text:message.reinforcement-nav-waiting");
        }

        public void NotifyReinforcementArrival(bool arrived)
        {
            if (!IsInitialized || ReinforcementChoice != AshReinforcementChoice.Staged) return;
            _lastMessageTextId = new ContentId(arrived ? "text:message.reinforcement-arrived" : "text:message.reinforcement-incoming");
            if (arrived) _pacing?.RecordMilestone("reinforcement-wave", "arrived", Stage, DeathCount);
        }

        public bool TryTalkToScout()
        {
            MainQuestStage previousStage = Stage;
            if (_quest == null || !_quest.TalkToScout())
            {
                return false;
            }

            SaveProgress();
            if (previousStage == MainQuestStage.MeetScout)
            {
                _pacing?.RecordMilestone("camp-scout", "completed", Stage, DeathCount);
            }
            else if (previousStage == MainQuestStage.ReturnToScout)
            {
                UpdateReturnEncounterForStage();
                _pacing?.RecordMilestone("return-scout", "completed", Stage, DeathCount);
                _pacing?.RecordMilestone("result", "published", Stage, DeathCount);
            }
            return true;
        }

        public bool TryActivateSeal(ContentId sealId)
        {
            if (_quest == null || !CanAttemptSeal(sealId))
            {
                return false;
            }

            if (!CanActivateSeal(sealId))
            {
                _lastMessageTextId = ResolveSealBlockedMessage(sealId);
                return false;
            }

            if (!_quest.ActivateSeal(sealId)) return false;

            if (sealId == ForestSealId && _forestTemplate != null &&
                !_forestTemplate.NotifyForestSealActivated())
            {
                throw new InvalidOperationException("Forest template rejected an authorized seal activation.");
            }

            SaveProgress();
            string segment = sealId.Value switch
            {
                "seal:forest" => "forest-seal",
                "seal:bridge" => "bridge-seal",
                "seal:courtyard" => "courtyard-seal",
                _ => "route-seal"
            };
            _pacing?.RecordMilestone(segment, "activated", Stage, DeathCount);
            return true;
        }

        public bool TryEnterSanctum()
        {
            if (_quest == null || !_quest.EnterSanctum())
            {
                return false;
            }

            _warden?.SetEncounterEnabled(true);
            SaveProgress();
            _pacing?.RecordMilestone("sanctum-gate", "opened", Stage, DeathCount);
            _pacing?.RecordMilestone("sanctum-load", "entered", Stage, DeathCount);
            return true;
        }

        public void NotifyCheckpointActivated(ContentId checkpointId)
        {
            if (IsInitialized)
            {
                SaveProgress();
                _pacing?.RecordMilestone(checkpointId.Value, "activated", Stage, DeathCount, false);
            }
        }

        public string Resolve(ContentId textId)
        {
            if (_texts != null && _texts.TryResolve(textId, out string value))
            {
                return value;
            }

            return $"[{textId}]";
        }

        private void Initialize()
        {
            if (_player == null || _defaultCheckpoint == null)
            {
                throw new InvalidOperationException("M2 route flow is not configured.");
            }

            _texts = LocalizedTextJsonLoader.Load(LoadRuntimeText(
                RuntimeContentPaths.SimplifiedChineseTexts,
                _localizedTextsJson,
                "localized text"));
            _definition = QuestDefinitionJsonLoader.Load(LoadRuntimeText(
                RuntimeContentPaths.Quests,
                _questDefinitionsJson,
                "quest definition")).GetRequired(MainQuestId);
            string saveDirectory = UnityEngine.Application.isBatchMode
                ? UnityEngine.Application.temporaryCachePath
                : ApplicationStoragePaths.PersistentDataRoot;
            string path = string.IsNullOrWhiteSpace(_savePathOverride)
                ? Path.Combine(saveDirectory, _saveFileName)
                : _savePathOverride;
#if UNITY_EDITOR
            // Test Runner also runs inside the interactive Editor, where isBatchMode is false.
            // The test assembly owns this scoped override; never let a new-game fixture delete user saves.
            if (!string.IsNullOrEmpty(EditorTestSavePath)) path = EditorTestSavePath;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (VisualFoundationReviewProbe.IsActive) path = VisualFoundationReviewProbe.SavePath;
#endif
            _store = new JsonSaveGameStore(path);

            M2LaunchMode launchMode = M2LaunchIntent.Consume();
            _pacing = new PacingTelemetryRecorder(
                () => Time.realtimeSinceStartupAsDouble,
                line => Debug.Log(line, this));
            if (launchMode == M2LaunchMode.NewGame)
            {
                _store.DeleteAllRevisions();
            }

            SaveLoadResult result = _store.LoadOrCreate(CreateDefaultSave, ValidateQuestSnapshot);
            LoadStatus = result.Status;
            _quest = MainQuestState.Restore(_definition, result.Save.mainQuest.ToSnapshot());
            SessionElapsedSeconds = Mathf.Max(0f, result.Save.elapsedSeconds);
            DeathCount = Mathf.Max(0, result.Save.deathCount);
            _routeEnrichment = RouteEnrichmentState.Restore(
                result.Save.hasRouteEnrichmentProgress
                    ? result.Save.routeEnrichment.ToSnapshot()
                    : null);
            _sealConditions = SealConditionState.Restore(
                result.Save.hasSealConditionProgress
                    ? result.Save.sealConditions.ToSnapshot()
                    : CreateLegacySealConditionSnapshot(_quest));

            var checkpointId = new ContentId(result.Save.checkpointId);
            Vector3 position = new Vector3(
                result.Save.checkpointPosition.x,
                result.Save.checkpointPosition.y,
                result.Save.checkpointPosition.z);
            Quaternion rotation = Quaternion.Euler(0f, result.Save.checkpointYaw, 0f);
            if (!_player.RestoreCheckpoint(checkpointId, position, rotation, true))
            {
                throw new InvalidOperationException("Player rejected the loaded checkpoint snapshot.");
            }

            if (_routeEnrichment.WatchtowerDiscovered && !_player.ApplyHealingFlaskCapacityBonus(1))
            {
                throw new InvalidOperationException("Player rejected the restored watchtower flask upgrade.");
            }

            if (_routeEnrichment.RiskRewardClaimed && !_player.ApplyHealingFlaskCapacityBonus(1))
            {
                throw new InvalidOperationException("Player rejected the restored risk-route flask upgrade.");
            }

            if (_routeEnrichment.SupplyCartClaimed &&
                !_player.ApplySupplyCartHeavyPostureReward(SupplyCartHeavyPostureMultiplier))
                throw new InvalidOperationException("Claimed supply-cart reward is unavailable in the selected content; save preserved, no silent reward removal.");

            if (_forestTemplate != null)
            {
                _forestTemplate.Initialize(
                    result.Save.hasForestSealProgress
                        ? result.Save.forestSeal.ToSnapshot()
                        : null,
                    _quest.IsSealActivated(ForestSealId));
            }

            _lastMessageTextId = result.Status switch
            {
                SaveLoadStatus.Loaded => LoadedTextId,
                SaveLoadStatus.RecoveredCorrupt => RecoveredTextId,
                _ => NewGameTextId
            };

            if (result.Status != SaveLoadStatus.Loaded)
            {
                SaveProgress(false);
            }

            if (_warden != null)
            {
                if (Stage >= MainQuestStage.ReturnToScout) _warden.gameObject.SetActive(false);
                else _warden.SetEncounterEnabled(Stage == MainQuestStage.DefeatWarden);
            }

            SetWardenEncounterActive(false);

            IsInitialized = true;
            _encounterCoordinators = FindObjectsOfType<CombatEncounterCoordinator>(true);
            if (_routeEnrichment.AshApproachCleared)
                foreach (CombatEncounterCoordinator coordinator in _encounterCoordinators)
                    if (coordinator.TelemetrySegment == "ash-approach-encounter") coordinator.RestoreClearedMembers();
            if (_routeEnrichment.AshGuardPassCleared)
                foreach (CombatEncounterCoordinator coordinator in _encounterCoordinators)
                    if (coordinator.TelemetrySegment == "ash-guard-pass-encounter") coordinator.RestoreClearedMembers();
            if (_routeEnrichment.AshReturnCleared)
                foreach (CombatEncounterCoordinator coordinator in _encounterCoordinators)
                    if (coordinator.TelemetrySegment == "ash-return-encounter") coordinator.RestoreClearedMembers();
            if (_ashReturnRoot != null)
            {
                var members = _ashReturnRoot.GetComponentsInChildren<CombatTarget>(true);
                _ashReturnSpawnPoints = new Vector3[members.Length];
                for (int i = 0; i < members.Length; i++) _ashReturnSpawnPoints[i] = members[i].transform.position;
            }
            _courtyardElite = FindCourtyardElite();
            SubscribeEncounterTelemetry();
            SubscribeCourtyardElite();
            UpdateReturnEncounterForStage();
            _pacing.RecordMilestone(
                launchMode == M2LaunchMode.NewGame ? "new-game" : "continue",
                "enter",
                Stage,
                DeathCount);
        }

        private static string LoadRuntimeText(string path, TextAsset editorFallback, string label)
        {
            if (ContentPackageRuntime.IsInitialized)
                return ContentPackageRuntime.GetRequiredText(path);
#if UNITY_EDITOR
            if (editorFallback != null)
                return editorFallback.text;
#endif
            throw new InvalidOperationException($"Route {label} content requires an initialized runtime package.");
        }

        private SaveGameV1 CreateDefaultSave()
        {
            var newQuest = new MainQuestState(_definition);
            Vector3 position = _defaultCheckpoint.position;
            return SaveGameV1.Create(
                new ContentId("checkpoint:camp"),
                position.x,
                position.y,
                position.z,
                _defaultCheckpoint.eulerAngles.y,
                newQuest.CaptureSnapshot(),
                0f,
                0,
                _forestTemplate != null
                    ? new ForestSealProgressSnapshot(
                        ForestSealPhase.Encounter,
                        false,
                        ForestRuneChoice.None)
                    : null,
                new RouteEnrichmentSnapshot(false, EmberValleyRouteChoice.None),
                new SealConditionSnapshot(false, false, false, false));
        }

        private void ValidateQuestSnapshot(SaveGameV1 save)
        {
            MainQuestState restoredQuest = MainQuestState.Restore(_definition, save.mainQuest.ToSnapshot());
            if (save.hasForestSealProgress)
            {
                ForestSealProgressSnapshot forest = save.forestSeal.ToSnapshot();
                bool questHasForestSeal = restoredQuest.IsSealActivated(ForestSealId);
                bool templateHasActivatedSeal = forest.Phase >= ForestSealPhase.RuneChoice;
                if (questHasForestSeal != templateHasActivatedSeal)
                {
                    throw new FormatException("Forest template progress disagrees with main quest progress.");
                }
            }

            if (save.hasSealConditionProgress)
            {
                SealConditionSnapshot conditions = save.sealConditions.ToSnapshot();
                if (restoredQuest.IsSealActivated(BridgeSealId) &&
                    !(conditions.BridgeMechanismAActivated && conditions.BridgeMechanismBActivated))
                {
                    throw new FormatException("Bridge seal progress is missing its mechanism conditions.");
                }

                if (restoredQuest.IsSealActivated(CourtyardSealId) && !conditions.CourtyardGuardBroken)
                {
                    throw new FormatException("Courtyard seal progress is missing its guard-break condition.");
                }
            }
        }

        private void SaveProgress(bool showSavedMessage = true)
        {
            Vector3 position = _player.RespawnPosition;
            _store.Save(SaveGameV1.Create(
                _player.ActiveCheckpointId,
                position.x,
                position.y,
                position.z,
                _player.RespawnRotation.eulerAngles.y,
                _quest.CaptureSnapshot(),
                SessionElapsedSeconds,
                DeathCount,
                _forestTemplate?.CaptureSnapshot(),
                _routeEnrichment?.CaptureSnapshot(),
                _sealConditions?.CaptureSnapshot()));
            if (showSavedMessage)
            {
                _lastMessageTextId = SavedTextId;
            }
        }

        private void OnPlayerDied(PlayerCombatActor _)
        {
            DeathCount++;
            _pacing?.RecordDeath(Stage, DeathCount);
            _forestTemplate?.HandlePlayerDeath();
            if (Stage == MainQuestStage.DefeatWarden)
            {
                _warden?.ResetEncounter();
                SetWardenEncounterActive(false);
            }
        }

        private void OnCombatProgressed(PlayerCombatActor _, CombatProgressKind kind)
        {
            _pacing?.RecordCombatProgress(kind, Stage, DeathCount);
        }

        public void NotifyForestTemplateChanged(ContentId messageTextId)
        {
            if (!IsInitialized)
            {
                return;
            }

            SaveProgress(false);
            _lastMessageTextId = messageTextId;
            string segment = messageTextId.Value switch
            {
                "text:message.forest-sigil-claimed" => "forest-reward",
                "text:message.forest-supply-claimed" => "forest-supply",
                "text:message.ember-rune-chosen" => "forest-rune-ember",
                "text:message.guard-rune-chosen" => "forest-rune-guard",
                _ => "forest-progress"
            };
            _pacing?.RecordMilestone(segment, "completed", Stage, DeathCount);
        }

        public void SaveSessionProgress()
        {
            if (IsInitialized)
            {
                SaveProgress(false);
            }
        }

        private void OnWardenEncounterStarted(WardenActor warden)
        {
            if (warden == _warden && Stage == MainQuestStage.DefeatWarden)
            {
                SetWardenEncounterActive(true);
                _pacing?.EnterEncounter("warden-encounter", Stage, DeathCount);
            }
        }

        private void OnWardenEncounterReset(WardenActor warden)
        {
            if (warden == _warden)
            {
                SetWardenEncounterActive(false);
                _pacing?.EndEncounter("warden-encounter", "reset", Stage, DeathCount);
            }
        }

        private void OnWardenDied(WardenActor warden)
        {
            SetWardenEncounterActive(false);
            _pacing?.EndEncounter("warden-encounter", "defeated", Stage, DeathCount);
            if (warden == _warden && _quest != null && _quest.DefeatWarden())
            {
                SaveProgress();
                UpdateReturnEncounterForStage();
            }
        }

        private void SetWardenEncounterActive(bool active)
        {
            IsWardenEncounterActive = active;
            if (_wardenEntranceBarrier == null) return;
            if (_wardenBarrierPresentation == null)
            {
                _wardenBarrierPresentation = M2StageBarrier.CreateManual(gameObject, _wardenEntranceBarrier);
                _wardenBarrierPresentation.SetOpen(!active, true);
            }
            else _wardenBarrierPresentation.SetOpen(!active);
        }

        private void SubscribeEncounterTelemetry()
        {
            if (_encounterTelemetrySubscribed || _encounterCoordinators == null)
            {
                return;
            }

            foreach (CombatEncounterCoordinator coordinator in _encounterCoordinators)
            {
                if (coordinator == null) continue;
                coordinator.EncounterStarted += OnEncounterStarted;
                coordinator.EncounterCleared += OnEncounterCleared;
                coordinator.EncounterReset += OnEncounterReset;
            }

            _encounterTelemetrySubscribed = true;
            _summoners = FindObjectsOfType<SummonerEnemyActor>(true);
            foreach (SummonerEnemyActor summoner in _summoners) summoner.FirstSummonReleased += OnFirstSummon;
        }

        private void UnsubscribeEncounterTelemetry()
        {
            foreach (SummonerEnemyActor summoner in _summoners)
                if (summoner != null) summoner.FirstSummonReleased -= OnFirstSummon;
            _summoners = Array.Empty<SummonerEnemyActor>();
            if (!_encounterTelemetrySubscribed)
            {
                return;
            }

            foreach (CombatEncounterCoordinator coordinator in _encounterCoordinators)
            {
                if (coordinator == null) continue;
                coordinator.EncounterStarted -= OnEncounterStarted;
                coordinator.EncounterCleared -= OnEncounterCleared;
                coordinator.EncounterReset -= OnEncounterReset;
            }

            _encounterTelemetrySubscribed = false;
        }

        private void UpdateReturnEncounterForStage()
        {
            if (_ashReturnRoot == null) return;
            _ashReturnActivationPending = IsInitialized && Stage == MainQuestStage.ReturnToScout && !AshReturnCleared;
            if (!_ashReturnActivationPending) { _ashReturnRoot.SetActive(false); return; }
            TryActivateReturnEncounter();
        }

        private void TryActivateReturnEncounter()
        {
            if (_ashReturnRoot == null || !_ashReturnActivationPending || _player == null || !_player.IsAvailable) return;
            // This pending-only fixed array check avoids discovery each Update and spawning on a player
            // already beside an authored point when the quest advances or a checkpoint is continued.
            foreach (Vector3 spawn in _ashReturnSpawnPoints)
                if (Vector3.ProjectOnPlane(_player.transform.position - spawn, Vector3.up).sqrMagnitude < 4f)
                {
                    _lastMessageTextId = new ContentId("text:message.ash-return-waiting");
                    return;
                }
            _ashReturnActivationPending = false;
            _ashReturnRoot.SetActive(true);
            _lastMessageTextId = new ContentId("text:message.ash-return-active");
        }

        private void OnEncounterStarted(CombatEncounterCoordinator coordinator)
        {
            _pacing?.EnterEncounter(coordinator.TelemetrySegment, Stage, DeathCount);
        }

        private void OnFirstSummon(SummonerEnemyActor summoner)
        {
            // One meaningful beat per authored encounter/session, not per entity or retry.
            _pacing?.RecordMilestone(summoner.EncounterId, "first-summon", Stage, DeathCount);
        }

        private void OnEncounterCleared(CombatEncounterCoordinator coordinator)
        {
            _pacing?.EndEncounter(coordinator.TelemetrySegment, "cleared", Stage, DeathCount);
            if (coordinator.TelemetrySegment == "ash-approach-encounter" && _routeEnrichment.TryRecordAshApproachCleared())
            {
                _lastMessageTextId = new ContentId("text:message.ash-approach-cleared");
                SaveProgress(false);
            }
            if (coordinator.TelemetrySegment == "bridge-encounter" &&
                _sealConditions.TryRecordBridgeEncounterCleared())
            {
                _lastMessageTextId = new ContentId("text:message.bridge-encounter-cleared");
                SaveProgress(false);
                _pacing?.RecordMilestone("bridge-condition", "completed", Stage, DeathCount);
            }
            else if (coordinator.TelemetrySegment == "ash-guard-pass-encounter" && _routeEnrichment.TryRecordAshGuardPassCleared())
            {
                _lastMessageTextId = new ContentId("text:message.ash-guard-pass-cleared");
                SaveProgress(false);
            }
            else if (coordinator.TelemetrySegment == "ash-return-encounter" && _routeEnrichment.TryRecordAshReturnCleared())
            {
                _lastMessageTextId = new ContentId("text:message.ash-return-cleared");
                SaveProgress(false);
                UpdateReturnEncounterForStage();
            }
            else if (coordinator.TelemetrySegment == "pre-sanctum-encounter" &&
                     _routeEnrichment.TryRecordPreSanctumCleared())
            {
                bool rewardGranted = TryGrantRiskRouteReward();
                _lastMessageTextId = new ContentId(rewardGranted
                    ? "text:message.route-risk-reward"
                    : "text:message.pre-sanctum-cleared");
                SaveProgress(false);
            }
        }

        private void OnEncounterReset(CombatEncounterCoordinator coordinator)
        {
            _pacing?.EndEncounter(coordinator.TelemetrySegment, "reset", Stage, DeathCount);
        }

        private ContentId ResolveSealBlockedMessage(ContentId sealId)
        {
            if (sealId != ForestSealId && _forestTemplate != null &&
                _forestTemplate.Phase == ForestSealPhase.RuneChoice)
            {
                return new ContentId("text:message.choose-forest-rune-first");
            }

            if (sealId == BridgeSealId)
            {
                return new ContentId("text:message.bridge-seal-blocked");
            }

            if (sealId == CourtyardSealId)
            {
                return new ContentId("text:message.courtyard-seal-blocked");
            }

            return new ContentId("text:message.seal-condition-blocked");
        }

        private bool TryGrantRiskRouteReward()
        {
            if (!_routeEnrichment.TryClaimRiskReward())
            {
                return false;
            }

            if (!_player.ApplyHealingFlaskCapacityBonus(1))
            {
                throw new InvalidOperationException("Player rejected the risk-route flask upgrade.");
            }

            _pacing?.RecordMilestone("route-risk-reward", "claimed", Stage, DeathCount);
            return true;
        }

        private static SealConditionSnapshot CreateLegacySealConditionSnapshot(MainQuestState quest)
        {
            bool bridgeActivated = quest.IsSealActivated(BridgeSealId);
            return new SealConditionSnapshot(
                bridgeActivated,
                bridgeActivated,
                bridgeActivated,
                quest.IsSealActivated(CourtyardSealId));
        }

        private ShieldEnemyActor FindCourtyardElite()
        {
            foreach (ShieldEnemyActor actor in FindObjectsOfType<ShieldEnemyActor>(true))
            {
                if (actor != null && actor.name == "Enemy_RuinGuard_Courtyard") return actor;
            }

            return null;
        }

        private void SubscribeCourtyardElite()
        {
            if (_courtyardElite != null)
            {
                _courtyardElite.GuardBroken -= OnCourtyardGuardBroken;
                _courtyardElite.GuardBroken += OnCourtyardGuardBroken;
            }
        }

        private void UnsubscribeCourtyardElite()
        {
            if (_courtyardElite != null)
            {
                _courtyardElite.GuardBroken -= OnCourtyardGuardBroken;
            }
        }

        private void OnCourtyardGuardBroken(ShieldEnemyActor actor)
        {
            if (actor != _courtyardElite || _sealConditions == null ||
                !_sealConditions.TryRecordCourtyardGuardBroken())
            {
                return;
            }

            _lastMessageTextId = new ContentId("text:message.courtyard-guard-broken");
            SaveProgress(false);
            _pacing?.RecordMilestone("courtyard-guard", "broken", Stage, DeathCount);
        }
    }
}
