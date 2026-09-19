using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;

namespace Emberfall.Quests.Domain
{
    /// <summary>Immutable authored quest data loaded from JSON.</summary>
    public sealed class QuestDefinition : IContentDefinition
    {
        public QuestDefinition(ContentId id, ContentId titleTextId, int requiredSealCount)
        {
            Id = id;
            TitleTextId = titleTextId;
            RequiredSealCount = requiredSealCount;
        }

        public ContentId Id { get; }
        public ContentId TitleTextId { get; }
        public int RequiredSealCount { get; }
    }
}
