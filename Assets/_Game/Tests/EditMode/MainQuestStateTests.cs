using System;
using Emberfall.Core.Identifiers;
using Emberfall.Quests.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class MainQuestStateTests
    {
        [Test]
        public void MainRoute_RequiresUniqueSealsAndCompletesInOrder()
        {
            MainQuestState state = CreateState();

            Assert.That(state.EnterSanctum(), Is.False);
            Assert.That(state.TalkToScout(), Is.True);
            Assert.That(state.Stage, Is.EqualTo(MainQuestStage.ActivateSeals));
            Assert.That(state.ActivateSeal(new ContentId("seal:forest")), Is.True);
            Assert.That(state.IsSealActivated(new ContentId("seal:forest")), Is.True);
            Assert.That(state.ActivateSeal(new ContentId("seal:forest")), Is.False);
            Assert.That(state.ActivateSeal(new ContentId("seal:bridge")), Is.True);
            Assert.That(state.ActivateSeal(new ContentId("seal:courtyard")), Is.True);
            Assert.That(state.Stage, Is.EqualTo(MainQuestStage.EnterSanctum));
            Assert.That(state.EnterSanctum(), Is.True);
            Assert.That(state.DefeatWarden(), Is.True);
            Assert.That(state.TalkToScout(), Is.True);
            Assert.That(state.IsComplete, Is.True);
        }

        [Test]
        public void Snapshot_RestoresStableIdProgressWithoutMutatingDefinition()
        {
            QuestDefinition definition = CreateDefinition();
            var original = new MainQuestState(definition);
            original.TalkToScout();
            original.ActivateSeal(new ContentId("seal:bridge"));

            QuestProgressSnapshot snapshot = original.CaptureSnapshot();
            MainQuestState restored = MainQuestState.Restore(definition, snapshot);

            Assert.That(restored.Stage, Is.EqualTo(MainQuestStage.ActivateSeals));
            Assert.That(restored.ActivatedSealCount, Is.EqualTo(1));
            Assert.That(restored.RequiredSealCount, Is.EqualTo(3));
            Assert.That(definition.RequiredSealCount, Is.EqualTo(3));
        }

        [Test]
        public void Snapshot_RejectsStageAndSealCountMismatch()
        {
            var invalid = new QuestProgressSnapshot(
                "quest:emberfall.main",
                MainQuestStage.EnterSanctum,
                new[] { "seal:forest" });

            Assert.Throws<ArgumentException>(() => MainQuestState.Restore(CreateDefinition(), invalid));
        }

        private static MainQuestState CreateState() => new MainQuestState(CreateDefinition());

        private static QuestDefinition CreateDefinition() => new QuestDefinition(
            new ContentId("quest:emberfall.main"),
            new ContentId("text:quest.main.title"),
            3);
    }
}
