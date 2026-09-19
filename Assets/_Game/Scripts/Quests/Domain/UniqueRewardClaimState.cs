using System;
using Emberfall.Core.Identifiers;

namespace Emberfall.Quests.Domain
{
    /// <summary>Pure, session-scoped authority for a reward that may be claimed exactly once.</summary>
    public sealed class UniqueRewardClaimState
    {
        private readonly ContentId _rewardId;

        public UniqueRewardClaimState(ContentId rewardId)
        {
            if (rewardId.IsEmpty || !rewardId.Value.StartsWith("reward:", StringComparison.Ordinal))
                throw new ArgumentException("A unique reward must use a stable reward ID.", nameof(rewardId));
            _rewardId = rewardId;
        }

        public bool IsClaimed { get; private set; }
        public ulong ClaimantId { get; private set; } = ulong.MaxValue;

        public bool TryClaim(ContentId rewardId, ulong claimantId, out string reason)
        {
            if (rewardId != _rewardId)
            {
                reason = "reward-id-mismatch";
                return false;
            }

            if (IsClaimed)
            {
                reason = "already-claimed";
                return false;
            }

            IsClaimed = true;
            ClaimantId = claimantId;
            reason = string.Empty;
            return true;
        }
    }
}
