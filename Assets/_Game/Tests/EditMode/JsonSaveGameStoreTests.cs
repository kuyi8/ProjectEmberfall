using System;
using System.IO;
using Emberfall.Core.Identifiers;
using Emberfall.Infrastructure.Saves;
using Emberfall.Quests.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class JsonSaveGameStoreTests
    {
        private string _testDirectory;
        private string _savePath;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "EmberfallSaveTests", Guid.NewGuid().ToString("N"));
            _savePath = Path.Combine(_testDirectory, "save.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        [Test]
        public void Save_RoundTripsAndKeepsPreviousValidRevision()
        {
            var store = new JsonSaveGameStore(_savePath);
            store.Save(CreateSave("checkpoint:camp", 1f));
            store.Save(CreateSave("checkpoint:forest", 8f, 42.5f, 3));

            SaveLoadResult result = store.LoadOrCreate(() => CreateSave("checkpoint:fallback", 0f));

            Assert.That(result.Status, Is.EqualTo(SaveLoadStatus.Loaded));
            Assert.That(result.Save.checkpointId, Is.EqualTo("checkpoint:forest"));
            Assert.That(result.Save.checkpointPosition.x, Is.EqualTo(8f));
            Assert.That(result.Save.mainQuest.ToSnapshot().ActivatedSealIds.Count, Is.EqualTo(1));
            Assert.That(result.Save.elapsedSeconds, Is.EqualTo(42.5f));
            Assert.That(result.Save.deathCount, Is.EqualTo(3));
            Assert.That(result.Save.hasForestSealProgress, Is.False,
                "Legacy V1 saves without the optional forest template payload must stay compatible.");
            Assert.That(File.Exists(store.PreviousPath), Is.True);
            Assert.That(File.Exists(_savePath + ".tmp"), Is.False);
        }

        [Test]
        public void MissingSave_ReturnsValidatedNewGameWithoutWritingImplicitly()
        {
            var store = new JsonSaveGameStore(_savePath);

            SaveLoadResult result = store.LoadOrCreate(() => CreateSave("checkpoint:camp", 0f));

            Assert.That(result.Status, Is.EqualTo(SaveLoadStatus.NewGame));
            Assert.That(result.Save.checkpointId, Is.EqualTo("checkpoint:camp"));
            Assert.That(File.Exists(_savePath), Is.False);
        }

        [Test]
        public void InvalidSave_IsPreservedAndFallsBackToNewGame()
        {
            Directory.CreateDirectory(_testDirectory);
            File.WriteAllText(_savePath, "{\"schemaVersion\":1,\"checkpointId\":\"invalid\"}");
            var store = new JsonSaveGameStore(_savePath);

            SaveLoadResult result = store.LoadOrCreate(() => CreateSave("checkpoint:camp", 0f));

            Assert.That(result.Status, Is.EqualTo(SaveLoadStatus.RecoveredCorrupt));
            Assert.That(result.Save.checkpointId, Is.EqualTo("checkpoint:camp"));
            Assert.That(File.Exists(store.CorruptPath), Is.True);
            Assert.That(File.Exists(_savePath), Is.False);
        }

        [Test]
        public void SaveValidation_RejectsUnsupportedSchemaAndDuplicateSealIds()
        {
            SaveGameV1 save = CreateSave("checkpoint:camp", 0f);
            save.schemaVersion = 2;
            Assert.Throws<NotSupportedException>(save.Validate);

            save.schemaVersion = SaveGameV1.CurrentSchemaVersion;
            save.mainQuest.activatedSealIds = new[] { "seal:forest", "seal:forest" };
            Assert.Throws<FormatException>(save.Validate);
        }

        [Test]
        public void SaveValidation_RejectsInvalidSessionStatistics()
        {
            SaveGameV1 save = CreateSave("checkpoint:camp", 0f);
            save.elapsedSeconds = float.NaN;
            Assert.Throws<FormatException>(save.Validate);

            save.elapsedSeconds = 0f;
            save.deathCount = -1;
            Assert.Throws<FormatException>(save.Validate);
        }

        [Test]
        public void Save_RoundTripsForestTemplateWithoutSchemaBump()
        {
            SaveGameV1 save = CreateSave("checkpoint:forest", 8f);
            save.hasForestSealProgress = true;
            save.forestSeal = ForestSealProgressV1.FromSnapshot(
                new ForestSealProgressSnapshot(
                    ForestSealPhase.Completed,
                    true,
                    ForestRuneChoice.Ember));

            var store = new JsonSaveGameStore(_savePath);
            store.Save(save);
            SaveLoadResult loaded = store.LoadOrCreate(() => null);

            Assert.That(loaded.Save.schemaVersion, Is.EqualTo(1));
            Assert.That(loaded.Save.hasForestSealProgress, Is.True);
            Assert.That(loaded.Save.forestSeal.ToSnapshot().Phase, Is.EqualTo(ForestSealPhase.Completed));
            Assert.That(loaded.Save.forestSeal.ToSnapshot().RuneChoice, Is.EqualTo(ForestRuneChoice.Ember));
            Assert.That(loaded.Save.forestSeal.ToSnapshot().SupplyClaimed, Is.True);
        }

        [Test]
        public void Save_RoundTripsRouteEnrichmentWithoutBreakingLegacyV1()
        {
            SaveGameV1 save = CreateSave("checkpoint:forest", 8f);
            save.hasRouteEnrichmentProgress = true;
            save.routeEnrichment = RouteEnrichmentProgressV1.FromSnapshot(
                new RouteEnrichmentSnapshot(true, EmberValleyRouteChoice.Supply));

            var store = new JsonSaveGameStore(_savePath);
            store.Save(save);
            SaveLoadResult loaded = store.LoadOrCreate(() => null);

            Assert.That(loaded.Save.schemaVersion, Is.EqualTo(1));
            Assert.That(loaded.Save.hasRouteEnrichmentProgress, Is.True);
            Assert.That(loaded.Save.routeEnrichment.ToSnapshot().WatchtowerDiscovered, Is.True);
            Assert.That(loaded.Save.routeEnrichment.ToSnapshot().RouteChoice,
                Is.EqualTo(EmberValleyRouteChoice.Supply));
        }

        [Test]
        public void Save_RoundTripsOptionalSealConditionsAndRiskReward()
        {
            SaveGameV1 save = CreateSave("checkpoint:courtyard", 12f);
            save.hasRouteEnrichmentProgress = true;
            save.routeEnrichment = RouteEnrichmentProgressV1.FromSnapshot(
                new RouteEnrichmentSnapshot(true, EmberValleyRouteChoice.Risk, true, true));
            save.hasSealConditionProgress = true;
            save.sealConditions = SealConditionProgressV1.FromSnapshot(
                new SealConditionSnapshot(true, true, true, true));

            var store = new JsonSaveGameStore(_savePath);
            store.Save(save);
            SaveLoadResult loaded = store.LoadOrCreate(() => null);

            Assert.That(loaded.Save.routeEnrichment.ToSnapshot().RiskRewardClaimed, Is.True);
            Assert.That(loaded.Save.sealConditions.ToSnapshot().BridgeMechanismAActivated, Is.True);
            Assert.That(loaded.Save.sealConditions.ToSnapshot().BridgeMechanismBActivated, Is.True);
            Assert.That(loaded.Save.sealConditions.ToSnapshot().CourtyardGuardBroken, Is.True);
        }

        [Test]
        public void DeleteAllRevisions_RemovesOnlyThisSaveFamily()
        {
            Directory.CreateDirectory(_testDirectory);
            var store = new JsonSaveGameStore(_savePath);
            store.Save(CreateSave("checkpoint:camp", 0f));
            store.Save(CreateSave("checkpoint:forest", 8f));
            File.WriteAllText(store.CorruptPath, "preserved-invalid-data");
            string unrelated = Path.Combine(_testDirectory, "unrelated.txt");
            File.WriteAllText(unrelated, "keep");

            store.DeleteAllRevisions();

            Assert.That(File.Exists(store.SavePath), Is.False);
            Assert.That(File.Exists(store.PreviousPath), Is.False);
            Assert.That(File.Exists(store.CorruptPath), Is.False);
            Assert.That(File.Exists(unrelated), Is.True);
        }

        [Test]
        public void DomainValidation_PreservesSemanticallyInvalidSaveAndRecovers()
        {
            var definition = new QuestDefinition(
                new ContentId("quest:emberfall.main"),
                new ContentId("text:quest.main.title"),
                3);
            var store = new JsonSaveGameStore(_savePath);
            SaveGameV1 invalid = CreateSave("checkpoint:forest", 8f);
            invalid.mainQuest.stage = (int)MainQuestStage.EnterSanctum;
            store.Save(invalid);

            SaveLoadResult result = store.LoadOrCreate(
                () => CreateSave("checkpoint:camp", 0f),
                save => MainQuestState.Restore(definition, save.mainQuest.ToSnapshot()));

            Assert.That(result.Status, Is.EqualTo(SaveLoadStatus.RecoveredCorrupt));
            Assert.That(File.Exists(store.CorruptPath), Is.True);
            Assert.That(result.Save.checkpointId, Is.EqualTo("checkpoint:camp"));
        }

        private static SaveGameV1 CreateSave(
            string checkpointId,
            float x,
            float elapsedSeconds = 0f,
            int deathCount = 0)
        {
            QuestDefinition definition = new QuestDefinition(
                new ContentId("quest:emberfall.main"),
                new ContentId("text:quest.main.title"),
                3);
            var quest = new MainQuestState(definition);
            quest.TalkToScout();
            quest.ActivateSeal(new ContentId("seal:forest"));
            return SaveGameV1.Create(
                new ContentId(checkpointId),
                x,
                1f,
                2f,
                90f,
                quest.CaptureSnapshot(),
                elapsedSeconds,
                deathCount);
        }
    }
}
