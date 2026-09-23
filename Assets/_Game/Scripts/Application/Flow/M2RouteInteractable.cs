using System;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Interaction;
using Emberfall.Quests.Domain;
using UnityEngine;

namespace Emberfall.Application.Flow
{
    public sealed class M2RouteInteractable : InteractableBehaviour
    {
        [SerializeField] private M2RouteFlowController _flow;
        [SerializeField] private M2RouteRole _role;
        [SerializeField] private string _stableId = "seal:forest";
        [SerializeField] private Transform _checkpointSpawn;
        [SerializeField] private GameObject _gateBlocker;
        [SerializeField] private Renderer _indicator;
        [SerializeField] private Renderer _artIndicator;

        private bool? _lastAvailable;
        private MaterialPropertyBlock _propertyBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public M2RouteRole Role => _role;
        public ContentId StableId => ContentId.TryCreate(_stableId, out ContentId id) ? id : default;

        public override ContentId PromptTextId => _role switch
        {
            M2RouteRole.Scout when _flow != null && _flow.Stage == MainQuestStage.ReturnToScout =>
                new ContentId("text:interaction.finish-quest"),
            M2RouteRole.Scout => new ContentId("text:interaction.talk-scout"),
            M2RouteRole.Seal when StableId.Value == "seal:forest" =>
                new ContentId("text:interaction.activate-forest-seal"),
            M2RouteRole.Seal => new ContentId("text:interaction.activate-seal"),
            M2RouteRole.SanctumGate => new ContentId("text:interaction.enter-sanctum"),
            _ => new ContentId("text:interaction.activate-checkpoint")
        };

        public override bool IsAvailable
        {
            get
            {
                if (_flow == null || !_flow.IsInitialized)
                {
                    return false;
                }

                return _role switch
                {
                    M2RouteRole.Scout => _flow.CanTalkToScout(),
                    M2RouteRole.Seal => !StableId.IsEmpty && _flow.CanAttemptSeal(StableId),
                    M2RouteRole.SanctumGate => _flow.CanEnterSanctum(),
                    M2RouteRole.Checkpoint => true,
                    _ => false
                };
            }
        }

        public void Configure(
            M2RouteFlowController flow,
            M2RouteRole role,
            string stableId,
            Transform checkpointSpawn = null,
            GameObject gateBlocker = null,
            Renderer indicator = null)
        {
            _flow = flow;
            _role = role;
            _stableId = stableId;
            _checkpointSpawn = checkpointSpawn;
            _gateBlocker = gateBlocker;
            _indicator = indicator;
        }

        public void SetArtIndicator(Renderer artIndicator)
        {
            _artIndicator = artIndicator;
            RefreshPresentation(true);
        }

        private M2StageBarrier _barrierPresentation;

        private void Start()
        {
            if (_gateBlocker != null && _flow != null)
            {
                _barrierPresentation = M2StageBarrier.CreateManual(gameObject, _gateBlocker);
                _barrierPresentation.SetOpen(_flow.IsSanctumOpen, true);
            }

            RefreshPresentation(true);
        }

        private void Update()
        {
            RefreshPresentation(false);
        }

        public override bool TryInteract(InteractionContext context)
        {
            if (!IsAvailable || context.Actor == null)
            {
                return false;
            }

            bool succeeded = _role switch
            {
                M2RouteRole.Scout => _flow.TryTalkToScout(),
                M2RouteRole.Seal => _flow.TryActivateSeal(StableId),
                M2RouteRole.SanctumGate => _flow.TryEnterSanctum(),
                M2RouteRole.Checkpoint => ActivateCheckpoint(context),
                _ => false
            };

            if (succeeded && _role == M2RouteRole.SanctumGate && _gateBlocker != null)
            {
                _barrierPresentation?.SetOpen(true);
            }

            RefreshPresentation(true);
            return succeeded;
        }

        private bool ActivateCheckpoint(InteractionContext context)
        {
            if (StableId.IsEmpty || !StableId.Value.StartsWith("checkpoint:", StringComparison.Ordinal))
            {
                return false;
            }

            Transform spawn = _checkpointSpawn != null ? _checkpointSpawn : transform;
            if (!context.Actor.ActivateCheckpoint(StableId, spawn.position, spawn.rotation))
            {
                return false;
            }

            _flow.NotifyCheckpointActivated(StableId);
            return true;
        }

        private void RefreshPresentation(bool force)
        {
            bool ready = _role == M2RouteRole.Seal
                ? _flow != null && !StableId.IsEmpty && _flow.CanActivateSeal(StableId)
                : IsAvailable;
            if (!force && _lastAvailable == ready)
            {
                return;
            }

            _lastAvailable = ready;
            if (_indicator != null)
            {
                Color color = ready
                    ? new Color(1f, 0.52f, 0.08f)
                    : new Color(0.16f, 0.34f, 0.30f);
                ApplyColor(_indicator, color);
                if (_artIndicator != null)
                {
                    ApplyColor(_artIndicator, color);
                }
            }
        }

        private void ApplyColor(Renderer renderer, Color color)
        {
            _propertyBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
