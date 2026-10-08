using Emberfall.Core.Content;
using System;
using Emberfall.Core.Identifiers;

namespace Emberfall.Quests.Domain
{
    /// <summary>Immutable authored quest data loaded from JSON.</summary>
    public sealed class QuestDefinition : IContentDefinition
    {
        public QuestDefinition(ContentId id, ContentId titleTextId, int requiredSealCount, float supplyCartHeavyPostureMultiplier = 0f)
        {
            Id = id;
            TitleTextId = titleTextId;
            RequiredSealCount = requiredSealCount;
            SupplyCartHeavyPostureMultiplier = NormalizeSupplyCartMultiplier(supplyCartHeavyPostureMultiplier);
        }

        public ContentId Id { get; }
        public ContentId TitleTextId { get; }
        public int RequiredSealCount { get; }
        public float SupplyCartHeavyPostureMultiplier { get; }
        // Missing optional schema-1 JSON float is zero: old content has no reward, not +15%.
        public static float NormalizeSupplyCartMultiplier(float value)
        {
            if (value == 0f) return 1f;
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 1f || value > 1.5f)
                throw new FormatException("Supply-cart heavy posture multiplier must be missing/zero or finite in [1, 1.5].");
            return value;
        }
    }
}
