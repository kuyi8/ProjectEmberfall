using System.Collections.Generic;
using Emberfall.AI.Data;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Infrastructure.Content;
using Emberfall.Infrastructure.Scripting;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class RuntimeSelectedContentTests
    {
        [SetUp]
        public void SetUp() => ContentPackageRuntime.ResetForTests();

        [TearDown]
        public void TearDown() => ContentPackageRuntime.ResetForTests();

        [Test]
        public void SelectedPatch_OwnsEnemyJsonAndQuestLuaForWholeSession()
        {
            Dictionary<string, byte[]> files = M4ContentTestFactory.LoadAuthoredFiles();
            M4ContentTestFactory.ReplaceText(
                files,
                RuntimeContentPaths.Enemies,
                "\"maximumHealth\": 165.0",
                "\"maximumHealth\": 145.0");
            M4ContentTestFactory.ReplaceText(
                files,
                M4ContentTestFactory.QuestScriptPath,
                "text:quest.forest-seal.ready",
                "text:quest.forest-seal.ready-patch");
            ContentPackageSnapshot snapshot = M4ContentTestFactory.CreateSnapshot(
                new SemanticVersion(0, 5, 5),
                files,
                "rsa-sha256",
                "test-signature");
            Assert.That(new GameContentPackagePreflight().Validate(snapshot, out string reason), Is.True, reason);

            ContentPackageRuntime.Initialize(new ContentPackageSelection(snapshot, ContentPackageSource.Patch));
            float health = MeleeEnemyDefinitionJsonLoader.Load(
                    ContentPackageRuntime.GetRequiredText(RuntimeContentPaths.Enemies))
                .GetRequired(new ContentId("enemy:fogwalker"))
                .MaximumHealth;
            using var lua = new LuaOrchestrationRuntime();
            LuaQuestConditionResult quest = lua.EvaluateQuestCondition(
                ContentPackageRuntime.Current.Snapshot,
                new LuaQuestConditionContext(new ContentId("quest:forest-seal"), true, false));

            Assert.That(health, Is.EqualTo(145f));
            Assert.That(quest.ReasonId.Value, Is.EqualTo("text:quest.forest-seal.ready-patch"));
            Assert.That(ContentPackageRuntime.Current.Snapshot.Manifest.ContentVersion,
                Is.EqualTo(new SemanticVersion(0, 5, 5)));
        }

        [Test]
        public void RuntimeSelection_CannotChangeDuringSession()
        {
            ContentPackageSnapshot snapshot = M4ContentTestFactory.CreateSnapshot(new SemanticVersion(0, 5, 4));
            ContentPackageRuntime.Initialize(new ContentPackageSelection(snapshot, ContentPackageSource.Builtin));

            Assert.Throws<System.InvalidOperationException>(() =>
                ContentPackageRuntime.Initialize(new ContentPackageSelection(snapshot, ContentPackageSource.Patch)));
        }
    }
}
