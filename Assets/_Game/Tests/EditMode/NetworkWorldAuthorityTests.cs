using Emberfall.Core.Identifiers;
using Emberfall.Networking;
using Emberfall.Quests.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class NetworkWorldAuthorityTests
    {
        [Test]
        public void InteractionIntentAcceptsOnlyIncreasingSequences()
        {
            var validator = new NetworkInteractionIntentValidator();

            Assert.That(validator.TryAccept(1, out string firstReason), Is.True);
            Assert.That(firstReason, Is.Empty);
            Assert.That(validator.TryAccept(4, out string secondReason), Is.True);
            Assert.That(secondReason, Is.Empty);
            Assert.That(validator.LastAcceptedSequence, Is.EqualTo(4));
        }

        [Test]
        public void InteractionIntentRejectsReplayWithoutChangingAuthoritySequence()
        {
            var validator = new NetworkInteractionIntentValidator();
            Assert.That(validator.TryAccept(3, out _), Is.True);

            Assert.That(validator.TryAccept(2, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("stale-sequence"));
            Assert.That(validator.LastAcceptedSequence, Is.EqualTo(3));
        }

        [Test]
        public void UniqueRewardCanBeClaimedByExactlyOneClient()
        {
            var rewardId = new ContentId("reward:network-ember");
            var state = new UniqueRewardClaimState(rewardId);

            Assert.That(state.TryClaim(rewardId, 7, out string firstReason), Is.True);
            Assert.That(firstReason, Is.Empty);
            Assert.That(state.TryClaim(rewardId, 9, out string duplicateReason), Is.False);
            Assert.That(duplicateReason, Is.EqualTo("already-claimed"));
            Assert.That(state.ClaimantId, Is.EqualTo(7));
        }

        [Test]
        public void WrongRewardIdCannotConsumeUniqueReward()
        {
            var expected = new ContentId("reward:network-ember");
            var state = new UniqueRewardClaimState(expected);

            Assert.That(state.TryClaim(new ContentId("reward:forged"), 8, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("reward-id-mismatch"));
            Assert.That(state.IsClaimed, Is.False);
            Assert.That(state.TryClaim(expected, 8, out _), Is.True);
        }

        [Test]
        public void ForestSliceUsesExistingOneSealQuestProgression()
        {
            var definition = new QuestDefinition(
                new ContentId("quest:forest-seal"),
                new ContentId("text:quest.forest.title"),
                1);
            var quest = new MainQuestState(definition);

            Assert.That(quest.TalkToScout(), Is.True);
            Assert.That(quest.ActivateSeal(new ContentId("seal:forest")), Is.True);
            Assert.That(quest.Stage, Is.EqualTo(MainQuestStage.EnterSanctum));
            Assert.That(quest.ActivateSeal(new ContentId("seal:forged")), Is.False);
            Assert.That(quest.EnterSanctum(), Is.True);
            Assert.That(quest.Stage, Is.EqualTo(MainQuestStage.DefeatWarden));
        }
    }
}
