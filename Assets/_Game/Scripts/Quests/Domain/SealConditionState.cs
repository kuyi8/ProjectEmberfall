using System;

namespace Emberfall.Quests.Domain
{
    public sealed class SealConditionSnapshot
    {
        public SealConditionSnapshot(
            bool bridgeEncounterCleared,
            bool bridgeMechanismAActivated,
            bool bridgeMechanismBActivated,
            bool courtyardGuardBroken)
        {
            BridgeEncounterCleared = bridgeEncounterCleared;
            BridgeMechanismAActivated = bridgeMechanismAActivated;
            BridgeMechanismBActivated = bridgeMechanismBActivated;
            CourtyardGuardBroken = courtyardGuardBroken;
            Validate();
        }

        public bool BridgeEncounterCleared { get; }
        public bool BridgeMechanismAActivated { get; }
        public bool BridgeMechanismBActivated { get; }
        public bool CourtyardGuardBroken { get; }

        public void Validate()
        {
            if (BridgeMechanismBActivated && !BridgeEncounterCleared)
            {
                throw new FormatException("Bridge mechanism B was activated before the bridge encounter was cleared.");
            }
        }
    }

    /// <summary>Persisted business facts for the bridge and courtyard seal conditions.</summary>
    public sealed class SealConditionState
    {
        private SealConditionState(SealConditionSnapshot snapshot)
        {
            snapshot.Validate();
            BridgeEncounterCleared = snapshot.BridgeEncounterCleared;
            BridgeMechanismAActivated = snapshot.BridgeMechanismAActivated;
            BridgeMechanismBActivated = snapshot.BridgeMechanismBActivated;
            CourtyardGuardBroken = snapshot.CourtyardGuardBroken;
        }

        public SealConditionState()
        {
        }

        public bool BridgeEncounterCleared { get; private set; }
        public bool BridgeMechanismAActivated { get; private set; }
        public bool BridgeMechanismBActivated { get; private set; }
        public bool CourtyardGuardBroken { get; private set; }
        public bool CanActivateBridgeSeal => BridgeMechanismAActivated && BridgeMechanismBActivated;

        public static SealConditionState Restore(SealConditionSnapshot snapshot) =>
            snapshot == null ? new SealConditionState() : new SealConditionState(snapshot);

        public bool TryRecordBridgeEncounterCleared()
        {
            if (BridgeEncounterCleared) return false;
            BridgeEncounterCleared = true;
            return true;
        }

        public bool TryActivateBridgeMechanismA()
        {
            if (BridgeMechanismAActivated) return false;
            BridgeMechanismAActivated = true;
            return true;
        }

        public bool TryActivateBridgeMechanismB()
        {
            if (BridgeMechanismBActivated || !BridgeEncounterCleared) return false;
            BridgeMechanismBActivated = true;
            return true;
        }

        public bool TryRecordCourtyardGuardBroken()
        {
            if (CourtyardGuardBroken) return false;
            CourtyardGuardBroken = true;
            return true;
        }

        public SealConditionSnapshot CaptureSnapshot() =>
            new SealConditionSnapshot(
                BridgeEncounterCleared,
                BridgeMechanismAActivated,
                BridgeMechanismBActivated,
                CourtyardGuardBroken);
    }
}
