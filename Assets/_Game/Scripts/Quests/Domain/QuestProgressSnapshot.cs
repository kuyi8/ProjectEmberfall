using System;
using System.Collections.Generic;

namespace Emberfall.Quests.Domain
{
    /// <summary>Serialization-neutral runtime snapshot. Save adapters own the file schema.</summary>
    public sealed class QuestProgressSnapshot
    {
        public QuestProgressSnapshot(string questId, MainQuestStage stage, IReadOnlyList<string> activatedSealIds)
        {
            QuestId = questId ?? throw new ArgumentNullException(nameof(questId));
            Stage = stage;
            ActivatedSealIds = activatedSealIds ?? throw new ArgumentNullException(nameof(activatedSealIds));
        }

        public string QuestId { get; }
        public MainQuestStage Stage { get; }
        public IReadOnlyList<string> ActivatedSealIds { get; }
    }
}
