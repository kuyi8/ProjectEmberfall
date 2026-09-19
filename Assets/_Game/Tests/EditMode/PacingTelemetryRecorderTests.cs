using System.Collections.Generic;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Quests.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class PacingTelemetryRecorderTests
    {
        [Test]
        public void RecordMilestone_UsesMonotonicRelativeTimeAndStableFields()
        {
            double now = 100d;
            var lines = new List<string>();
            var recorder = new PacingTelemetryRecorder(() => now, lines.Add, "run test");

            now = 102.5d;
            Assert.That(recorder.RecordMilestone(
                "camp scout",
                "completed",
                MainQuestStage.ActivateSeals,
                0), Is.True);

            now = 101d;
            Assert.That(recorder.RecordMilestone(
                "forest-seal",
                "activated",
                MainQuestStage.ActivateSeals,
                1), Is.True);

            Assert.That(lines[0], Does.Contain("run=run_test index=1 segment=camp_scout event=completed mono=2.500"));
            Assert.That(lines[1], Does.Contain("index=2 segment=forest-seal event=activated mono=2.500"));
            Assert.That(lines[1], Does.Contain("deaths=1 combatActive=false"));
        }

        [Test]
        public void RecordMilestone_DeduplicatesOnlyWhenRequested()
        {
            var lines = new List<string>();
            var recorder = new PacingTelemetryRecorder(() => 1d, lines.Add, "run");

            Assert.That(recorder.RecordMilestone("checkpoint", "activated", MainQuestStage.MeetScout, 0), Is.True);
            Assert.That(recorder.RecordMilestone("checkpoint", "activated", MainQuestStage.MeetScout, 0), Is.False);
            Assert.That(recorder.RecordMilestone("checkpoint", "activated", MainQuestStage.MeetScout, 0, false), Is.True);
            Assert.That(lines, Has.Count.EqualTo(2));
        }

        [Test]
        public void EncounterLifecycle_CountsAttemptsAndReportsActiveState()
        {
            double now = 20d;
            var lines = new List<string>();
            var recorder = new PacingTelemetryRecorder(() => now, lines.Add, "run");

            now = 21d;
            Assert.That(recorder.EnterEncounter("forest-encounter", MainQuestStage.ActivateSeals, 0), Is.True);
            Assert.That(recorder.EnterEncounter("forest-encounter", MainQuestStage.ActivateSeals, 0), Is.False);
            now = 25d;
            recorder.RecordDeath(MainQuestStage.ActivateSeals, 1);
            now = 26d;
            Assert.That(recorder.EndEncounter("forest-encounter", "reset", MainQuestStage.ActivateSeals, 1), Is.True);
            now = 30d;
            Assert.That(recorder.EnterEncounter("forest-encounter", MainQuestStage.ActivateSeals, 1), Is.True);
            now = 35d;
            Assert.That(recorder.EndEncounter("forest-encounter", "cleared", MainQuestStage.ActivateSeals, 1), Is.True);

            Assert.That(recorder.EncounterAttemptCount, Is.EqualTo(2));
            Assert.That(recorder.ActiveEncounterCount, Is.Zero);
            Assert.That(lines[0], Does.Contain("encounters=1 deaths=0 combatActive=true"));
            Assert.That(lines[1], Does.Contain("segment=player event=died").And.Contain("combatActive=true"));
            Assert.That(lines[2], Does.Contain("event=reset").And.Contain("combatActive=false"));
            Assert.That(lines[4], Does.Contain("event=cleared").And.Contain("encounters=2"));
        }

        [Test]
        public void CombatProgress_IsRepeatableAndExcludedFromMilestoneDeduplication()
        {
            double now = 10d;
            var lines = new List<string>();
            var recorder = new PacingTelemetryRecorder(() => now, lines.Add, "run");

            recorder.RecordCombatProgress(CombatProgressKind.DamageDealt, MainQuestStage.ActivateSeals, 0);
            now = 10.5d;
            recorder.RecordCombatProgress(CombatProgressKind.DamageReceived, MainQuestStage.ActivateSeals, 0);
            now = 11d;
            recorder.RecordCombatProgress(CombatProgressKind.DamageDealt, MainQuestStage.ActivateSeals, 0);

            Assert.That(lines, Has.Count.EqualTo(3));
            Assert.That(lines[0], Does.Contain("segment=combat-progress event=dealt"));
            Assert.That(lines[1], Does.Contain("segment=combat-progress event=received mono=0.500"));
            Assert.That(lines[2], Does.Contain("segment=combat-progress event=dealt mono=1.000"));
        }
    }
}
