using System;
using Emberfall.AI.Unity;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Infrastructure.Scripting;
using Emberfall.Quests.Domain;
using UnityEngine;

namespace Emberfall.Application.Flow
{
    [DefaultExecutionOrder(-70)]
    public sealed class ForestSealTemplateCoordinator : MonoBehaviour
    {
        private static readonly ContentId ForestQuestId = new ContentId("quest:forest-seal");
        private static readonly ContentId ForestEncounterId = new ContentId("encounter:forest-seal");
        private static readonly ContentId OpeningWaveId = new ContentId("wave:forest.guard-pair");
        private static readonly ContentId OpeningPresentationId = new ContentId("presentation:forest.encounter-start");
        private static readonly ContentId SigilClaimedMessage = new ContentId("text:message.forest-sigil-claimed");
        private static readonly ContentId SupplyClaimedMessage = new ContentId("text:message.forest-supply-claimed");
        private static readonly ContentId EmberChosenMessage = new ContentId("text:message.ember-rune-chosen");
        private static readonly ContentId GuardChosenMessage = new ContentId("text:message.guard-rune-chosen");

        [SerializeField] private M2RouteFlowController _flow;
        [SerializeField] private PlayerCombatActor _player;
        [SerializeField] private MeleeEnemyActor _sigilBearer;
        [SerializeField] private RangedEnemyActor _runePriest;
        [SerializeField] private ForestTemplateInteractable _sigilPickup;
        [SerializeField] private ForestTemplateInteractable _supplyCache;
        [SerializeField] private ForestTemplateInteractable _emberRune;
        [SerializeField] private ForestTemplateInteractable _guardRune;
        [SerializeField] private GameObject _shortcutBlocker;
        [SerializeField] private GameObject _restoredWorldRoot;

        private ForestSealTemplateState _state;
        private M2StageBarrier _shortcutPresentation;
        private bool _subscribed;
        private bool _encounterWaveQueued;
        private bool _questConditionEligible;
        private ContentId _questConditionReasonId;

        public ForestSealPhase Phase => _state?.Phase ?? ForestSealPhase.Encounter;
        public ForestRuneChoice RuneChoice => _state?.RuneChoice ?? ForestRuneChoice.None;
        public bool CanActivateSeal => _state?.CanActivateSeal == true;
        public bool IsInitialized => _state != null;
        public bool SupplyClaimed => _state?.SupplyClaimed == true;
        public ForestTemplateInteractable SigilPickup => _sigilPickup;
        public ForestTemplateInteractable SupplyCache => _supplyCache;
        public ForestTemplateInteractable EmberRune => _emberRune;
        public ForestTemplateInteractable GuardRune => _guardRune;
        public GameObject ShortcutBlocker => _shortcutBlocker;
        public GameObject RestoredWorldRoot => _restoredWorldRoot;
        public ContentId EncounterWaveId => _encounterWaveQueued ? OpeningWaveId : default;
        public ContentId EncounterPresentationId => _encounterWaveQueued ? OpeningPresentationId : default;
        public ContentId QuestConditionReasonId => _questConditionReasonId;

        public ContentId ObjectiveTextId => Phase switch
        {
            ForestSealPhase.Encounter => new ContentId("text:quest.forest.defeat-pair"),
            ForestSealPhase.SigilAvailable when !_questConditionEligible => _questConditionReasonId,
            ForestSealPhase.SigilAvailable => new ContentId("text:quest.forest.claim-sigil"),
            ForestSealPhase.SigilClaimed => new ContentId("text:quest.forest.activate-seal"),
            ForestSealPhase.RuneChoice => new ContentId("text:quest.forest.choose-rune"),
            _ => default
        };

        public void Configure(
            M2RouteFlowController flow,
            PlayerCombatActor player,
            MeleeEnemyActor sigilBearer,
            RangedEnemyActor runePriest,
            ForestTemplateInteractable sigilPickup,
            ForestTemplateInteractable supplyCache,
            ForestTemplateInteractable emberRune,
            ForestTemplateInteractable guardRune,
            GameObject shortcutBlocker,
            GameObject restoredWorldRoot)
        {
            _flow = flow;
            _player = player;
            _sigilBearer = sigilBearer;
            _runePriest = runePriest;
            _sigilPickup = sigilPickup;
            _supplyCache = supplyCache;
            _emberRune = emberRune;
            _guardRune = guardRune;
            _shortcutBlocker = shortcutBlocker;
            _restoredWorldRoot = restoredWorldRoot;
        }

        public void Initialize(ForestSealProgressSnapshot snapshot, bool forestSealAlreadyActivated)
        {
            if (_player == null || _sigilBearer == null || _runePriest == null ||
                _sigilPickup == null || _supplyCache == null || _emberRune == null || _guardRune == null)
            {
                throw new InvalidOperationException("Forest seal template is not configured.");
            }

            if (snapshot == null && forestSealAlreadyActivated)
            {
                snapshot = new ForestSealProgressSnapshot(
                    ForestSealPhase.RuneChoice,
                    false,
                    ForestRuneChoice.None);
            }

            _state = ForestSealTemplateState.Restore(snapshot);
            InitializeLuaOrchestration();
            Subscribe();
            ApplyStateToScene();
        }

        private void OnDisable()
        {
            if (!_subscribed)
            {
                return;
            }

            _sigilBearer.Died -= OnBearerDied;
            _runePriest.Died -= OnPriestDied;
            _subscribed = false;
        }

        public bool CanInteract(ForestTemplateInteractionRole role)
        {
            if (_state == null || _flow == null || _flow.IsComplete ||
                _flow.Stage < MainQuestStage.ActivateSeals)
            {
                return false;
            }

            return role switch
            {
                ForestTemplateInteractionRole.SigilPickup =>
                    Phase == ForestSealPhase.SigilAvailable && _questConditionEligible,
                ForestTemplateInteractionRole.SupplyCache => !_state.SupplyClaimed,
                ForestTemplateInteractionRole.EmberRune => Phase == ForestSealPhase.RuneChoice,
                ForestTemplateInteractionRole.GuardRune => Phase == ForestSealPhase.RuneChoice,
                _ => false
            };
        }

        public bool TryInteract(ForestTemplateInteractionRole role)
        {
            if (!CanInteract(role))
            {
                return false;
            }

            bool changed;
            ContentId message;
            switch (role)
            {
                case ForestTemplateInteractionRole.SigilPickup:
                    changed = _state.TryClaimSigil();
                    message = SigilClaimedMessage;
                    break;
                case ForestTemplateInteractionRole.SupplyCache:
                    if (!_player.TryUseSupply(55f, 45f))
                    {
                        return false;
                    }

                    changed = _state.TryClaimSupply();
                    message = SupplyClaimedMessage;
                    break;
                case ForestTemplateInteractionRole.EmberRune:
                    changed = _state.TryChooseRune(ForestRuneChoice.Ember);
                    message = EmberChosenMessage;
                    break;
                case ForestTemplateInteractionRole.GuardRune:
                    changed = _state.TryChooseRune(ForestRuneChoice.Guard);
                    message = GuardChosenMessage;
                    break;
                default:
                    return false;
            }

            if (!changed)
            {
                return false;
            }

            ApplyStateToScene();
            _flow.NotifyForestTemplateChanged(message);
            return true;
        }

        public bool NotifyForestSealActivated()
        {
            if (_state == null || !_state.TryActivateSeal())
            {
                return false;
            }

            ApplyStateToScene();
            return true;
        }

        public void HandlePlayerDeath()
        {
            if (_state == null || !_state.ResetEncounterAttempt())
            {
                return;
            }

            _sigilBearer.gameObject.SetActive(true);
            _runePriest.gameObject.SetActive(true);
            _sigilBearer.ResetToSpawn();
            _runePriest.ResetToSpawn();
        }

        public ForestSealProgressSnapshot CaptureSnapshot() => _state?.CaptureSnapshot();

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            _sigilBearer.Died += OnBearerDied;
            _runePriest.Died += OnPriestDied;
            _subscribed = true;
        }

        private void OnBearerDied(MeleeEnemyActor enemy)
        {
            if (enemy == _sigilBearer && _state.RegisterBearerDefeat())
            {
                HandleEncounterProgress();
            }
        }

        private void OnPriestDied(RangedEnemyActor enemy)
        {
            if (enemy == _runePriest && _state.RegisterPriestDefeat())
            {
                HandleEncounterProgress();
            }
        }

        private void HandleEncounterProgress()
        {
            if (Phase != ForestSealPhase.SigilAvailable)
            {
                return;
            }

            EvaluateQuestCondition();
            ApplyStateToScene();
            _flow.NotifyForestTemplateChanged(_questConditionReasonId);
        }

        private void ApplyStateToScene()
        {
            bool encounterActive = Phase == ForestSealPhase.Encounter && _encounterWaveQueued;
            bool showSigil = Phase == ForestSealPhase.SigilAvailable && _questConditionEligible;
            bool showRuneChoice = Phase == ForestSealPhase.RuneChoice;
            bool restored = Phase == ForestSealPhase.Completed;

            SetActive(_sigilBearer.gameObject, encounterActive);
            SetActive(_runePriest.gameObject, encounterActive);
            SetActive(_sigilPickup.gameObject, showSigil);
            SetActive(_supplyCache.gameObject, !_state.SupplyClaimed);
            SetActive(_emberRune.gameObject, showRuneChoice);
            SetActive(_guardRune.gameObject, showRuneChoice);
            if (_shortcutBlocker != null)
            {
                if (_shortcutPresentation == null)
                {
                    _shortcutPresentation = M2StageBarrier.CreateManual(gameObject, _shortcutBlocker);
                    _shortcutPresentation.SetOpen(restored, true);
                }
                else _shortcutPresentation.SetOpen(restored);
            }
            SetActive(_restoredWorldRoot, restored);

            RuneBlessing blessing = RuneChoice switch
            {
                ForestRuneChoice.Ember => RuneBlessing.Ember,
                ForestRuneChoice.Guard => RuneBlessing.Guard,
                _ => RuneBlessing.None
            };
            _player.ApplyRuneBlessing(blessing);
        }

        private void InitializeLuaOrchestration()
        {
#if UNITY_EDITOR
            if (!ContentPackageRuntime.IsInitialized)
            {
                _encounterWaveQueued = true;
                _questConditionEligible = Phase >= ForestSealPhase.SigilAvailable;
                _questConditionReasonId = new ContentId(
                    _questConditionEligible
                        ? "text:quest.forest-seal.ready"
                        : "text:quest.forest-seal.blocked");
                return;
            }
#endif
            using var lua = new LuaOrchestrationRuntime();
            LuaEncounterResult result = lua.EvaluateEncounter(
                ContentPackageRuntime.Current.Snapshot,
                new LuaEncounterContext(ForestEncounterId, 0, 1, false));
            if (result.Action != LuaEncounterAction.QueueWave ||
                result.WaveId != OpeningWaveId || result.PresentationId != OpeningPresentationId)
                throw new InvalidOperationException("Forest encounter Lua returned an unsupported opening wave.");
            _encounterWaveQueued = true;

            if (Phase >= ForestSealPhase.SigilAvailable && Phase < ForestSealPhase.Completed)
                EvaluateQuestCondition(lua);
            else
                _questConditionReasonId = new ContentId("text:quest.forest-seal.blocked");
        }

        private void EvaluateQuestCondition()
        {
            using var lua = new LuaOrchestrationRuntime();
            EvaluateQuestCondition(lua);
        }

        private void EvaluateQuestCondition(LuaOrchestrationRuntime lua)
        {
            LuaQuestConditionResult result = lua.EvaluateQuestCondition(
                ContentPackageRuntime.Current.Snapshot,
                new LuaQuestConditionContext(
                    ForestQuestId,
                    _state.BearerDefeatedThisAttempt || Phase >= ForestSealPhase.SigilAvailable,
                    Phase >= ForestSealPhase.RuneChoice));
            _questConditionEligible = result.Eligible;
            _questConditionReasonId = result.ReasonId;
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }
    }
}
