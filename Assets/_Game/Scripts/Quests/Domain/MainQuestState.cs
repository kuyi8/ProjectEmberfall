using System;
using System.Collections.Generic;
using System.Linq;
using Emberfall.Core.Identifiers;

namespace Emberfall.Quests.Domain
{
    /// <summary>
    /// Mutable runtime progress for the M2 main route. Authored QuestDefinition remains immutable.
    /// </summary>
    public sealed class MainQuestState
    {
        private readonly QuestDefinition _definition;
        private readonly HashSet<ContentId> _activatedSeals = new HashSet<ContentId>();

        public MainQuestState(QuestDefinition definition)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (_definition.RequiredSealCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(definition), "A quest must require at least one seal.");
            }

            Stage = MainQuestStage.MeetScout;
        }

        public MainQuestStage Stage { get; private set; }
        public int ActivatedSealCount => _activatedSeals.Count;
        public int RequiredSealCount => _definition.RequiredSealCount;
        public bool IsComplete => Stage == MainQuestStage.Complete;

        public bool IsSealActivated(ContentId sealId) => !sealId.IsEmpty && _activatedSeals.Contains(sealId);

        public bool TalkToScout()
        {
            if (Stage == MainQuestStage.MeetScout)
            {
                Stage = MainQuestStage.ActivateSeals;
                return true;
            }

            if (Stage == MainQuestStage.ReturnToScout)
            {
                Stage = MainQuestStage.Complete;
                return true;
            }

            return false;
        }

        public bool ActivateSeal(ContentId sealId)
        {
            if (Stage != MainQuestStage.ActivateSeals ||
                sealId.IsEmpty ||
                !sealId.Value.StartsWith("seal:", StringComparison.Ordinal) ||
                !_activatedSeals.Add(sealId))
            {
                return false;
            }

            if (_activatedSeals.Count == _definition.RequiredSealCount)
            {
                Stage = MainQuestStage.EnterSanctum;
            }

            return true;
        }

        public bool EnterSanctum()
        {
            if (Stage != MainQuestStage.EnterSanctum)
            {
                return false;
            }

            Stage = MainQuestStage.DefeatWarden;
            return true;
        }

        public bool DefeatWarden()
        {
            if (Stage != MainQuestStage.DefeatWarden)
            {
                return false;
            }

            Stage = MainQuestStage.ReturnToScout;
            return true;
        }

        public QuestProgressSnapshot CaptureSnapshot()
        {
            string[] seals = _activatedSeals
                .Select(id => id.Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            return new QuestProgressSnapshot(_definition.Id.Value, Stage, seals);
        }

        public static MainQuestState Restore(QuestDefinition definition, QuestProgressSnapshot snapshot)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (!string.Equals(definition.Id.Value, snapshot.QuestId, StringComparison.Ordinal))
            {
                throw new ArgumentException("Quest snapshot ID does not match its definition.", nameof(snapshot));
            }

            if (!Enum.IsDefined(typeof(MainQuestStage), snapshot.Stage))
            {
                throw new ArgumentException("Quest snapshot contains an unknown stage.", nameof(snapshot));
            }

            var restored = new MainQuestState(definition);
            foreach (string value in snapshot.ActivatedSealIds)
            {
                var sealId = new ContentId(value);
                if (!sealId.Value.StartsWith("seal:", StringComparison.Ordinal) || !restored._activatedSeals.Add(sealId))
                {
                    throw new ArgumentException("Quest snapshot contains an invalid or duplicate seal ID.", nameof(snapshot));
                }
            }

            int count = restored._activatedSeals.Count;
            bool countIsValid = count <= definition.RequiredSealCount;
            bool stageIsValid = snapshot.Stage switch
            {
                MainQuestStage.MeetScout => count == 0,
                MainQuestStage.ActivateSeals => count < definition.RequiredSealCount,
                _ => count == definition.RequiredSealCount
            };
            if (!countIsValid || !stageIsValid)
            {
                throw new ArgumentException("Quest snapshot progress is inconsistent with its stage.", nameof(snapshot));
            }

            restored.Stage = snapshot.Stage;
            return restored;
        }
    }
}
