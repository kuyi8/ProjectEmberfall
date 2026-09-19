using System;

namespace Emberfall.Quests.Domain
{
    public enum ForestSealPhase
    {
        Encounter = 0,
        SigilAvailable = 1,
        SigilClaimed = 2,
        RuneChoice = 3,
        Completed = 4
    }

    public enum ForestRuneChoice
    {
        None = 0,
        Ember = 1,
        Guard = 2
    }

    public sealed class ForestSealProgressSnapshot
    {
        public ForestSealProgressSnapshot(
            ForestSealPhase phase,
            bool supplyClaimed,
            ForestRuneChoice runeChoice)
        {
            Phase = phase;
            SupplyClaimed = supplyClaimed;
            RuneChoice = runeChoice;
            Validate();
        }

        public ForestSealPhase Phase { get; }
        public bool SupplyClaimed { get; }
        public ForestRuneChoice RuneChoice { get; }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(ForestSealPhase), Phase) ||
                !Enum.IsDefined(typeof(ForestRuneChoice), RuneChoice) ||
                (Phase == ForestSealPhase.Completed) != (RuneChoice != ForestRuneChoice.None))
            {
                throw new FormatException("Forest seal progress is inconsistent.");
            }
        }
    }

    /// <summary>
    /// Deterministic authority for the forest gameplay template. Scene actors only submit facts.
    /// A future server adapter can own this state without changing the presentation contract.
    /// </summary>
    public sealed class ForestSealTemplateState
    {
        private bool _bearerDefeatedThisAttempt;
        private bool _priestDefeatedThisAttempt;

        public ForestSealTemplateState()
        {
            Phase = ForestSealPhase.Encounter;
        }

        private ForestSealTemplateState(ForestSealProgressSnapshot snapshot)
        {
            snapshot.Validate();
            Phase = snapshot.Phase;
            SupplyClaimed = snapshot.SupplyClaimed;
            RuneChoice = snapshot.RuneChoice;
        }

        public ForestSealPhase Phase { get; private set; }
        public ForestRuneChoice RuneChoice { get; private set; }
        public bool SupplyClaimed { get; private set; }
        public bool BearerDefeatedThisAttempt => _bearerDefeatedThisAttempt;
        public bool PriestDefeatedThisAttempt => _priestDefeatedThisAttempt;
        public bool CanActivateSeal => Phase == ForestSealPhase.SigilClaimed;

        public static ForestSealTemplateState Restore(ForestSealProgressSnapshot snapshot) =>
            snapshot == null ? new ForestSealTemplateState() : new ForestSealTemplateState(snapshot);

        public bool RegisterBearerDefeat()
        {
            if (Phase != ForestSealPhase.Encounter || _bearerDefeatedThisAttempt)
            {
                return false;
            }

            _bearerDefeatedThisAttempt = true;
            PromoteEncounterIfCleared();
            return true;
        }

        public bool RegisterPriestDefeat()
        {
            if (Phase != ForestSealPhase.Encounter || _priestDefeatedThisAttempt)
            {
                return false;
            }

            _priestDefeatedThisAttempt = true;
            PromoteEncounterIfCleared();
            return true;
        }

        public bool ResetEncounterAttempt()
        {
            if (Phase != ForestSealPhase.Encounter ||
                (!_bearerDefeatedThisAttempt && !_priestDefeatedThisAttempt))
            {
                return false;
            }

            _bearerDefeatedThisAttempt = false;
            _priestDefeatedThisAttempt = false;
            return true;
        }

        public bool TryClaimSigil()
        {
            if (Phase != ForestSealPhase.SigilAvailable)
            {
                return false;
            }

            Phase = ForestSealPhase.SigilClaimed;
            return true;
        }

        public bool TryActivateSeal()
        {
            if (Phase != ForestSealPhase.SigilClaimed)
            {
                return false;
            }

            Phase = ForestSealPhase.RuneChoice;
            return true;
        }

        public bool TryChooseRune(ForestRuneChoice choice)
        {
            if (Phase != ForestSealPhase.RuneChoice || choice == ForestRuneChoice.None ||
                !Enum.IsDefined(typeof(ForestRuneChoice), choice))
            {
                return false;
            }

            RuneChoice = choice;
            Phase = ForestSealPhase.Completed;
            return true;
        }

        public bool TryClaimSupply()
        {
            if (SupplyClaimed)
            {
                return false;
            }

            SupplyClaimed = true;
            return true;
        }

        public ForestSealProgressSnapshot CaptureSnapshot() =>
            new ForestSealProgressSnapshot(Phase, SupplyClaimed, RuneChoice);

        private void PromoteEncounterIfCleared()
        {
            if (_bearerDefeatedThisAttempt && _priestDefeatedThisAttempt)
            {
                Phase = ForestSealPhase.SigilAvailable;
            }
        }
    }
}
