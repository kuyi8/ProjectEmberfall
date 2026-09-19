using System.Collections.Generic;
using System.Text;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Infrastructure.Scripting;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class LuaOrchestrationRuntimeTests
    {
        private const string QuestScript = @"
local quest = Emberfall.Quest
local ready = quest.forestEnemiesDefeated == true and quest.sealActivated ~= true
if ready then
    return { eligible = true, reasonId = 'text:quest.ready' }
end
return { eligible = false, reasonId = 'text:quest.blocked' }
";

        private const string EncounterScript = @"
local encounter = Emberfall.Encounter
local world = Emberfall.World
local presentation = Emberfall.Presentation
if encounter.defeatedCount == 0 and encounter.playerCount >= 1 and not world.gateOpen and presentation.allowCue then
    return { action = 'queue_wave', waveId = 'wave:forest.guard-pair', presentationId = 'presentation:forest.encounter-start' }
end
return { action = 'none' }
";

        [Test]
        public void QuestCondition_UsesCopiedContextAndReturnsValidatedReason()
        {
            ContentPackageSnapshot snapshot = CreateSnapshot(QuestScript, EncounterScript);
            using var runtime = new LuaOrchestrationRuntime();

            LuaQuestConditionResult ready = runtime.EvaluateQuestCondition(
                snapshot,
                new LuaQuestConditionContext(new ContentId("quest:forest-seal"), true, false));
            LuaQuestConditionResult blocked = runtime.EvaluateQuestCondition(
                snapshot,
                new LuaQuestConditionContext(new ContentId("quest:forest-seal"), true, true));

            Assert.That(ready.Eligible, Is.True);
            Assert.That(ready.ReasonId.Value, Is.EqualTo("text:quest.ready"));
            Assert.That(blocked.Eligible, Is.False);
            Assert.That(blocked.ReasonId.Value, Is.EqualTo("text:quest.blocked"));
        }

        [Test]
        public void Encounter_ReturnsOnlyValidatedDeclarativeCommand()
        {
            ContentPackageSnapshot snapshot = CreateSnapshot(QuestScript, EncounterScript);
            using var runtime = new LuaOrchestrationRuntime();

            LuaEncounterResult queued = runtime.EvaluateEncounter(
                snapshot,
                new LuaEncounterContext(new ContentId("encounter:forest-seal"), 0, 1, false));
            LuaEncounterResult none = runtime.EvaluateEncounter(
                snapshot,
                new LuaEncounterContext(new ContentId("encounter:forest-seal"), 2, 1, true));

            Assert.That(queued.Action, Is.EqualTo(LuaEncounterAction.QueueWave));
            Assert.That(queued.WaveId.Value, Is.EqualTo("wave:forest.guard-pair"));
            Assert.That(queued.PresentationId.Value, Is.EqualTo("presentation:forest.encounter-start"));
            Assert.That(none.Action, Is.EqualTo(LuaEncounterAction.None));
            Assert.That(none.WaveId.IsEmpty, Is.True);
        }

        [Test]
        public void Sandbox_DoesNotInheritStandardOrCSharpGlobals()
        {
            const string isolatedQuest = @"
return {
    eligible = math ~= nil,
    reasonId = 'text:sandbox.isolated'
}
";
            using var runtime = new LuaOrchestrationRuntime();

            LuaQuestConditionResult result = runtime.EvaluateQuestCondition(
                CreateSnapshot(isolatedQuest, EncounterScript),
                new LuaQuestConditionContext(new ContentId("quest:forest-seal"), true, false));

            Assert.That(result.Eligible, Is.False, "The package environment must not inherit Lua globals.");
        }

        [TestCase("return { eligible = true, reasonId = 'text:ok' ")]
        [TestCase("return { eligible = CS.System ~= nil, reasonId = 'text:bad' }")]
        [TestCase("while true do end")]
        public void Preflight_RejectsSyntaxUnauthorizedApiAndUnboundedControlFlow(string questScript)
        {
            using var runtime = new LuaOrchestrationRuntime();

            bool accepted = runtime.ValidatePackage(
                CreateSnapshot(questScript, EncounterScript),
                out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("Lua package preflight failed"));
        }

        [Test]
        public void Preflight_RejectsUnlistedLuaEntryPoint()
        {
            ContentPackageSnapshot snapshot = CreateSnapshot(
                QuestScript,
                EncounterScript,
                new KeyValuePair<string, string>("Scripts/extra.lua", "return {}"));
            using var runtime = new LuaOrchestrationRuntime();

            bool accepted = runtime.ValidatePackage(snapshot, out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("not allow-listed"));
        }

        [Test]
        public void Preflight_ExecutesBothQuestBranches()
        {
            const string branchFailure = @"
if Emberfall.Quest.forestEnemiesDefeated then
    return { eligible = true, reasonId = 'text:quest.ready' }
end
return { eligible = math.abs(1) > 0, reasonId = 'text:quest.blocked' }
";
            using var runtime = new LuaOrchestrationRuntime();

            bool accepted = runtime.ValidatePackage(
                CreateSnapshot(branchFailure, EncounterScript),
                out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("Lua package preflight failed"));
        }

        private static ContentPackageSnapshot CreateSnapshot(
            string quest,
            string encounter,
            params KeyValuePair<string, string>[] extraScripts)
        {
            var files = new Dictionary<string, byte[]>(System.StringComparer.Ordinal)
            {
                [LuaOrchestrationRuntime.QuestConditionPath] = Encoding.UTF8.GetBytes(quest),
                [LuaOrchestrationRuntime.EncounterOrchestrationPath] = Encoding.UTF8.GetBytes(encounter)
            };
            for (int i = 0; i < extraScripts.Length; i++)
                files.Add(extraScripts[i].Key, Encoding.UTF8.GetBytes(extraScripts[i].Value));

            var descriptors = new List<ContentPackageFile>(files.Count);
            foreach (KeyValuePair<string, byte[]> pair in files)
            {
                descriptors.Add(new ContentPackageFile(
                    pair.Key,
                    ContentFileKind.Lua,
                    pair.Value.LongLength,
                    ContentPackageValidator.ComputeSha256Hex(pair.Value)));
            }

            var manifest = new ContentPackageManifest(
                1,
                new ContentId("package:lua-tests"),
                new SemanticVersion(0, 5, 3),
                new SemanticVersion(0, 5, 0),
                new SemanticVersion(0, 6, 999),
                "builtin",
                "trusted",
                descriptors);
            return new ContentPackageSnapshot(manifest, files);
        }
    }
}
