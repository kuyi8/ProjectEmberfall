using System;
using Emberfall.AI.Domain;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.EditMode
{
    public sealed class RangedRetreatConstraintTests
    {
        static RangedEnemyPerception Close(bool blocked = true) => new RangedEnemyPerception(true, true, 2f, 0f, blocked);
        static RangedEnemyBrain Retreat(float elapsed = 0f)
        {
            var brain = new RangedEnemyBrain(RangedEnemyBrainTests.CreateDefinition(), 2.5f);
            brain.Tick(.01f, Close());
            if (elapsed > 0) brain.Tick(elapsed, Close());
            return brain;
        }

        [Test] public void SustainedConstraint_CompletesOriginalWindupReleaseRecovery_WithoutDirectDamage()
        {
            var brain = Retreat(2.49f);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            Assert.That(brain.AttackSequence, Is.Zero);
            brain.Tick(.02f, Close());
            Assert.That(brain.IsCorneredWindup, Is.True);
            Assert.That(brain.StateElapsed, Is.Zero);
            brain.Tick(.71f, Close());
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Windup));
            Assert.That(brain.AttackSequence, Is.Zero);
            brain.Tick(.02f, Close());
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Release));
            Assert.That(brain.AttackSequence, Is.EqualTo(1));
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
            brain.Tick(.34f, Close());
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Release));
            brain.Tick(.02f, Close());
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Recovery));
            brain.Tick(.99f, Close());
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Recovery));
            brain.Tick(.02f, Close());
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            Assert.That(brain.Health.Current, Is.EqualTo(brain.Health.Maximum));
        }

        [Test] public void ProgressOnAttainablePath_ClearsClock_AndNeverFallsBack()
        {
            var brain = Retreat(2.4f);
            for (int i = 0; i < 100; i++) brain.Tick(.1f, Close(false));
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
            Assert.That(brain.AttackSequence, Is.Zero);
            brain.Tick(.1f, Close());
            Assert.That(brain.RetreatBlockedElapsed, Is.EqualTo(.1f));
        }

        [Test] public void FeasibleWindup_StillRetreatsAtOriginalPoint65Threshold()
        {
            var brain = new RangedEnemyBrain(RangedEnemyBrainTests.CreateDefinition());
            brain.Tick(.01f, new RangedEnemyPerception(true, true, 7f, 0));
            brain.Tick(.1f, new RangedEnemyPerception(true, true, 3.24f, 0, false));
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            Assert.That(brain.AttackSequence, Is.Zero);
            Assert.That(brain.IsCorneredWindup, Is.False);
        }

        [Test] public void LegalFiringBand_ResetsConstraint_AndUsesNormalAttack()
        {
            var brain = Retreat(2.4f);
            var legal = new RangedEnemyPerception(true, true, 6f, 0, true);
            brain.Tick(.1f, legal);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Windup));
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
            Assert.That(brain.IsCorneredWindup, Is.False);
            brain.Tick(.73f, legal);
            Assert.That(brain.AttackSequence, Is.EqualTo(1));
        }

        [TestCase(false, true, 2f, 0f)] // unavailable / outside encounter is supplied by the adapter
        [TestCase(true, false, 2f, 0f)] // actual line of sight lost
        [TestCase(true, true, 10f, 0f)] // outside firing engagement
        [TestCase(true, true, 19f, 0f)] // beyond LoseTargetRange
        [TestCase(true, true, 2f, 23f)] // beyond spawn leash
        public void LostEligibility_ClearsClock_AndCannotUseFallback(bool available, bool visible, float distance, float spawn)
        {
            var brain = Retreat(2.4f);
            var lost = new RangedEnemyPerception(available, visible, distance, spawn, true);
            brain.Tick(5f, lost);
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
            Assert.That(brain.AttackSequence, Is.Zero);
            Assert.That(brain.IsCorneredWindup, Is.False);
        }

        [TestCase(false, true, 2f, 0f)]
        [TestCase(true, false, 2f, 0f)]
        [TestCase(true, true, 10f, 0f)]
        [TestCase(true, true, 2f, 23f)]
        public void CorneredWindup_LosesEligibility_AndCannotRelease(bool available, bool visible, float distance, float spawn)
        {
            var brain = Retreat(2.51f);
            brain.Tick(2f, new RangedEnemyPerception(available, visible, distance, spawn, true));
            Assert.That(brain.State, Is.Not.EqualTo(RangedEnemyState.Release));
            Assert.That(brain.AttackSequence, Is.Zero);
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
        }

        [Test] public void RetreatBecomesFeasible_DuringCorneredWindup_ExitsFallback()
        {
            var brain = Retreat(2.51f);
            brain.Tick(.73f, Close(false));
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            Assert.That(brain.IsCorneredWindup, Is.False);
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
            Assert.That(brain.AttackSequence, Is.Zero);
        }

        [Test] public void PostureBreak_ClearsConstraint_AndCannotAccumulateDuringControlWindow()
        {
            var brain = Retreat(2.51f);
            Assert.That(brain.ApplyCounterPosture(999), Is.True);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.HitReact));
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
            brain.Tick(.1f, Close());
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.HitReact));
            for (int i = 0; i < 15; i++) brain.Tick(.1f, Close());
            Assert.That(brain.IsPostureExecutionWindow, Is.True);
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
            Assert.That(brain.AttackSequence, Is.Zero);
        }

        [Test] public void ExecutionHoldReset_DoesNotChangeState_AndRequiresFreshEvidence()
        {
            var brain = Retreat(2.4f);
            brain.ResetRetreatConstraint();
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            brain.Tick(.2f, Close());
            Assert.That(brain.RetreatBlockedElapsed, Is.EqualTo(.2f));
            Assert.That(brain.AttackSequence, Is.Zero);
        }

        [Test] public void DeathAndRespawn_ClearConstraint()
        {
            var brain = Retreat(2.51f);
            brain.ReceiveDamage(new DamageRequest(1, 1, 999, 0, AttackTag.Light));
            brain.Tick(10f, Close());
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Dead));
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
            Assert.That(brain.AttackSequence, Is.Zero);
            brain.Reset(); brain.Tick(.01f, Close());
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
        }

        [Test] public void LegacyAdapterDefault_DoesNotInventConstraintEvidence()
        {
            var brain = new RangedEnemyBrain(RangedEnemyBrainTests.CreateDefinition());
            var legacy = new RangedEnemyPerception(true, true, 2f, 0f);
            for (int i = 0; i < 100; i++) brain.Tick(.1f, legacy);
            Assert.That(legacy.RetreatUnavailable, Is.False);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            Assert.That(brain.AttackSequence, Is.Zero, "Network adapter must assess navigation before claiming this fix.");
        }

        [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidThreshold_IsRejected(float value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RangedEnemyBrain(RangedEnemyBrainTests.CreateDefinition(), value));
            var copy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<CombatTuningAsset>("Assets/_Game/Settings/CombatTuning_M1.asset"));
            try
            {
                var so = new SerializedObject(copy);
                so.FindProperty("_enemyRetreatBlockedSeconds").floatValue = value; so.ApplyModifiedPropertiesWithoutUndo();
                Assert.Throws<ArgumentOutOfRangeException>(() => copy.CreateRuntimeCopy());
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [Test] public void CombatTuningSnapshot_IsImmutable_AndOwnsTheThreshold()
        {
            var asset = AssetDatabase.LoadAssetAtPath<CombatTuningAsset>("Assets/_Game/Settings/CombatTuning_M1.asset");
            Assert.That(asset.CreateRuntimeCopy().EnemyRetreatBlockedSeconds, Is.EqualTo(2.5f));
            var copy = Object.Instantiate(asset);
            try
            {
                var so = new SerializedObject(copy);
                so.FindProperty("_enemyRetreatBlockedSeconds").floatValue = 3.25f; so.ApplyModifiedPropertiesWithoutUndo();
                var snapshot = copy.CreateRuntimeCopy();
                so.FindProperty("_enemyRetreatBlockedSeconds").floatValue = 5f; so.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(snapshot.EnemyRetreatBlockedSeconds, Is.EqualTo(3.25f));
                Assert.That(asset.CreateRuntimeCopy().EnemyRetreatBlockedSeconds, Is.EqualTo(2.5f));
            }
            finally { Object.DestroyImmediate(copy); }
        }
    }
}
