using Emberfall.AI.Data;
using Emberfall.AI.Domain;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class ExecutionReadabilityTests
    {
        [Test]
        public void AuthoredPosture_RequiresSecondUninterruptedCombo_WithoutHealthInflation()
        {
            string json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Game/Data/M2/enemies.v1.json").text;
            var melee = new MeleeEnemyBrain(MeleeEnemyDefinitionJsonLoader.Load(json)
                .GetRequired(new ContentId("enemy:fogwalker")));
            var ranged = new RangedEnemyBrain(RangedEnemyDefinitionJsonLoader.Load(json)
                .GetRequired(new ContentId("enemy:rune-priest")));
            Assert.That(melee.Posture.Maximum, Is.EqualTo(110f));
            Assert.That(ranged.Posture.Maximum, Is.EqualTo(85f));
            Assert.That(melee.Health.Maximum, Is.EqualTo(165f));
            Assert.That(ranged.Health.Maximum, Is.EqualTo(105f));
            foreach (float hit in new[] { 18f, 20f, 38f })
            {
                Assert.That(melee.ApplyCounterPosture(hit), Is.False);
                Assert.That(ranged.ApplyCounterPosture(hit), Is.False);
            }
            Assert.That(melee.ApplyCounterPosture(18f), Is.False);
            Assert.That(ranged.ApplyCounterPosture(18f), Is.True);
            Assert.That(melee.ApplyCounterPosture(20f), Is.True);
        }

        [Test]
        public void BothBrains_HoldZeroPostureForExactlyTwoSeconds_WithoutRefreshingOnHit()
        {
            var melee = new MeleeEnemyBrain(MeleeEnemyBrainTests.CreateDefinition());
            var ranged = new RangedEnemyBrain(RangedEnemyBrainTests.CreateDefinition());
            melee.ApplyCounterPosture(999f);
            ranged.ApplyCounterPosture(999f);
            melee.Tick(1.99f, default);
            ranged.Tick(1.99f, default);
            Assert.That(melee.IsPostureExecutionWindow, Is.True);
            Assert.That(ranged.IsPostureExecutionWindow, Is.True);
            Assert.That(melee.Posture.Current, Is.Zero);
            Assert.That(ranged.Posture.Current, Is.Zero);
            Assert.That(ExecutionRules.IsEligible(ExecutionTargetKind.Ordinary, 1f,
                melee.IsPostureExecutionWindow, false), Is.True);
            melee.ApplyCounterPosture(1f);
            ranged.ApplyCounterPosture(1f);
            melee.Tick(0.02f, default);
            ranged.Tick(0.02f, default);
            Assert.That(melee.IsPostureExecutionWindow, Is.False);
            Assert.That(ranged.IsPostureExecutionWindow, Is.False);
            Assert.That(melee.Posture.Normalized, Is.EqualTo(1f));
            Assert.That(ranged.Posture.Normalized, Is.EqualTo(1f));
            Assert.That(ExecutionRules.IsEligible(ExecutionTargetKind.Ordinary, 1f,
                melee.IsPostureExecutionWindow, false), Is.False);
        }

        [TestCase(1f, false, false, 100f, "text:execution.need-posture")]
        [TestCase(0.15f, false, false, 100f, "text:execution.need-health")]
        [TestCase(1f, true, true, 100f, "text:execution.already-claimed")]
        [TestCase(1f, true, false, 21f, "text:execution.need-stamina")]
        public void FailureReasons_AreReachable(float health, bool broken, bool claimed, float stamina, string id)
        {
            Assert.That(ExecutionRules.FailureTextId(ExecutionTargetKind.Ordinary,
                health, broken, claimed, stamina, 22f), Is.EqualTo(id));
        }

        [Test]
        public void Readability_DistinguishesBreakHealthAndSpent()
        {
            Assert.That(ExecutionRules.ConditionTextId(ExecutionTargetKind.Ordinary, 1f, true, false),
                Is.EqualTo("text:execution.posture-ready"));
            Assert.That(ExecutionRules.ConditionTextId(ExecutionTargetKind.Ordinary, 0.1f, false, false),
                Is.EqualTo("text:execution.health-ready"));
            Assert.That(ExecutionRules.ConditionTextId(ExecutionTargetKind.Elite, 0.2f, true, true),
                Is.EqualTo("text:execution.already-claimed"));
        }

        [Test]
        public void AuthoredBrains_ResumeMovementBeforeWindowEnds_AndHitsDoNotRestun()
        {
            string json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Game/Data/M2/enemies.v1.json").text;
            var md = MeleeEnemyDefinitionJsonLoader.Load(json).GetRequired(new ContentId("enemy:fogwalker"));
            var rd = RangedEnemyDefinitionJsonLoader.Load(json).GetRequired(new ContentId("enemy:rune-priest"));
            Assert.That(md.HitReactDuration, Is.EqualTo(1.1f));
            Assert.That(rd.HitReactDuration, Is.EqualTo(1.1f));
            Assert.That(md.HitReactDuration, Is.LessThan(ExecutionRules.PostureWindowSeconds));
            var melee = new MeleeEnemyBrain(md);
            var ranged = new RangedEnemyBrain(rd);
            var mp = new MeleeEnemyPerception(true, true, 5f, 0f);
            var rp = new RangedEnemyPerception(true, true, 2f, 0f);
            melee.ApplyCounterPosture(999f);
            ranged.ApplyCounterPosture(999f);
            melee.Tick(1.09f, mp);
            ranged.Tick(1.09f, rp);
            Assert.That(melee.State, Is.EqualTo(MeleeEnemyState.HitReact));
            Assert.That(ranged.State, Is.EqualTo(RangedEnemyState.HitReact));
            melee.Tick(0.02f, mp);
            ranged.Tick(0.02f, rp);
            Assert.That(melee.WantsTargetMovement, Is.True);
            Assert.That(ranged.WantsRetreatMovement, Is.True);
            var hit = new DamageRequest(1, 1, 5f, 10f, AttackTag.Light);
            Assert.That(melee.ReceiveDamage(hit).Staggered, Is.False);
            Assert.That(ranged.ReceiveDamage(hit).Staggered, Is.False);
            Assert.That(melee.ApplyCounterPosture(999f), Is.False);
            Assert.That(ranged.ApplyCounterPosture(999f), Is.False);
            melee.Tick(0.88f, mp);
            ranged.Tick(0.88f, rp);
            Assert.That(melee.IsPostureExecutionWindow && ranged.IsPostureExecutionWindow, Is.True);
            Assert.That(melee.Posture.Current + ranged.Posture.Current, Is.Zero);
            melee.Tick(0.02f, mp);
            ranged.Tick(0.02f, rp);
            Assert.That(melee.IsPostureExecutionWindow || ranged.IsPostureExecutionWindow, Is.False);
            Assert.That(melee.Posture.Normalized + ranged.Posture.Normalized, Is.EqualTo(2f));
            melee.ApplyCounterPosture(999f);
            ranged.ApplyCounterPosture(999f);
            melee.Reset(); ranged.Reset();
            Assert.That(melee.IsPostureExecutionWindow || ranged.IsPostureExecutionWindow, Is.False);
        }

        [Test]
        public void Markers_DistinguishUnlockedStates_AndEliteNeverAdvertisesHealthFallback()
        {
            Assert.That(ExecutionRules.MarkerTextId(ExecutionTargetKind.Ordinary, 1f, true, false, 0f),
                Is.EqualTo("text:execution.marker-posture"));
            Assert.That(ExecutionRules.MarkerTextId(ExecutionTargetKind.Ordinary, 0.1f, false, false, 1f),
                Is.EqualTo("text:execution.marker-health"));
            Assert.That(ExecutionRules.MarkerTextId(ExecutionTargetKind.Elite, 0.5f, false, true, 1f),
                Is.EqualTo("text:execution.already-claimed"));
            Assert.That(ExecutionRules.MarkerTextId(ExecutionTargetKind.Ordinary, 1f, false, false, 0.2f),
                Is.EqualTo("text:execution.near-break"));
            Assert.That(ExecutionRules.MarkerTextId(ExecutionTargetKind.Elite, 0.01f, false, false, 1f), Is.Empty);
            Assert.That(ExecutionRules.FailureTextId(ExecutionTargetKind.Elite, 0.01f, false, false, 100f, 22f),
                Is.EqualTo("text:execution.need-guard-break"));
        }

        [Test]
        public void AuthoredLightCombos_ExposePriestSimultaneousThreshold_NotAFalseEarlierBreakClaim()
        {
            string json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Game/Data/M2/enemies.v1.json").text;
            var melee = new MeleeEnemyBrain(MeleeEnemyDefinitionJsonLoader.Load(json)
                .GetRequired(new ContentId("enemy:fogwalker")));
            var ranged = new RangedEnemyBrain(RangedEnemyDefinitionJsonLoader.Load(json)
                .GetRequired(new ContentId("enemy:rune-priest")));
            var tuning = AssetDatabase.LoadAssetAtPath<Emberfall.Gameplay.Combat.Unity.CombatTuningAsset>(
                "Assets/_Game/Settings/CombatTuning_M1.asset").CreateRuntimeCopy();
            float[] posture = { 18f, 20f, 38f };
            for (int hit = 0; hit < 5; hit++)
            {
                int index = hit % 3;
                var damage = new DamageRequest(1, hit, tuning.GetLightDamage(index), posture[index], AttackTag.Light);
                melee.ReceiveDamage(damage);
                if (hit < 4) ranged.ReceiveDamage(damage);
                if (hit == 2)
                {
                    Assert.That(ranged.Health.Current, Is.EqualTo(26f));
                    Assert.That(ranged.IsPostureExecutionWindow, Is.False);
                }
                if (hit == 3)
                {
                    Assert.That(ranged.Health.Current, Is.EqualTo(5f));
                    Assert.That(ranged.IsPostureExecutionWindow, Is.True);
                    Assert.That(ranged.Health.Normalized, Is.LessThanOrEqualTo(ExecutionRules.OrdinaryHealthThreshold));
                }
            }
            Assert.That(melee.IsPostureExecutionWindow, Is.True);
            Assert.That(melee.Health.Current, Is.EqualTo(50f));
            Assert.That(melee.Health.Normalized, Is.GreaterThan(ExecutionRules.OrdinaryHealthThreshold));
        }

        [Test]
        public void VoidPenalty_CannotKillOrRefillResources_AndCancelsCommittedActions()
        {
            var tuning = AssetDatabase.LoadAssetAtPath<Emberfall.Gameplay.Combat.Unity.CombatTuningAsset>(
                "Assets/_Game/Settings/CombatTuning_M1.asset").CreateRuntimeCopy();
            var model = new CombatStateMachine(tuning);
            model.HealingFlasks.TryConsume();
            model.Submit(CombatCommand.Execution);
            float stamina = model.Stamina.Current;
            Assert.That(model.RecoverFromVoidFall(), Is.True);
            Assert.That(model.State, Is.EqualTo(CombatState.Locomotion));
            Assert.That(model.Health.Normalized, Is.EqualTo(0.88f).Within(0.0001f));
            Assert.That(model.Stamina.Current, Is.EqualTo(stamina));
            Assert.That(model.HealingFlasks.CurrentCharges, Is.EqualTo(1));
            model.Tick(1f);
            Assert.That(model.ExecutionResolveSequence, Is.Zero);
            for (int i = 0; i < 20; i++) model.RecoverFromVoidFall();
            Assert.That(model.Health.Current, Is.EqualTo(1f));
            Assert.That(model.IsDead, Is.False);
        }
    }
}
