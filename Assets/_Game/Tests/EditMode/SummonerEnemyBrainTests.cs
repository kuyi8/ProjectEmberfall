using System;
using System.IO;
using Emberfall.AI.Data;
using Emberfall.AI.Domain;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class SummonerEnemyBrainTests
    {
        private static string Json => File.ReadAllText("Assets/_Game/Data/Review/SummonerCandidate.v1.json");
        private static SummonerEnemyDefinition Definition => SummonerEnemyDefinitionJsonLoader.Load(Json).GetRequired(new ContentId("enemy:ash-caller"));
        private static RangedEnemyPerception InRange => new RangedEnemyPerception(true, true, 10f, 0f);
        private static SummonerEnemyBrain NewBrain() => new SummonerEnemyBrain(Definition);
        private static void ReachSummon(SummonerEnemyBrain brain)
        {
            // A cooldown can become ready during a committed projectile; let that full recovery finish.
            for (int i = 0; i < 600 && !brain.IsSummoning; i++) brain.Tick(.02f, InRange);
            Assert.That(brain.IsSummoning, Is.True);
        }
        private static void Register(SummonerEnemyBrain brain, int id)
        {
            ReachSummon(brain); brain.Tick(Definition.SummonWindup, InRange);
            Assert.That(brain.TryRegisterSummon(id, brain.AttackSequence), Is.True);
        }

        [Test] public void Data_HasExplicitChantInterruptAndWeakerThanShieldPosture()
        {
            Assert.That(Definition.SummonWindup, Is.EqualTo(1.2f));
            Assert.That(Definition.InterruptCooldown, Is.EqualTo(1.5f));
            Assert.That(Definition.Combat.MaximumPosture, Is.EqualTo(75f).And.LessThan(80f));
        }
        [Test] public void Windup_DoesNotGenerateUntilAuthoritativeRelease()
        {
            var brain = NewBrain(); ReachSummon(brain);
            brain.Tick(1.19f, InRange);
            Assert.That(brain.TryRegisterSummon(1, brain.AttackSequence), Is.False);
            Assert.That(brain.LivingSummonCount, Is.Zero);
            brain.Tick(.011f, InRange);
            Assert.That(brain.TryRegisterSummon(1, brain.AttackSequence), Is.True);
        }
        [Test] public void CompletedCast_AcknowledgesExactlyOneEntityAndRejectsDuplicateOrStaleToken()
        {
            var brain = NewBrain(); Register(brain, 11); int sequence = brain.AttackSequence;
            Assert.That(brain.TryRegisterSummon(12, sequence), Is.False);
            Assert.That(brain.TryRegisterSummon(11, sequence), Is.False);
            ReachSummon(brain); brain.Tick(1.2f, InRange);
            Assert.That(brain.TryRegisterSummon(12, sequence), Is.False);
            Assert.That(brain.TryRegisterSummon(12, brain.AttackSequence), Is.True);
            Assert.That(brain.SummonCount, Is.EqualTo(2));
        }
        [Test] public void TwoLivingSummons_SkipSummonAndUseWeakProjectileWithoutReplacingMinions()
        {
            var brain = NewBrain(); Register(brain, 1); Register(brain, 2);
            for (int i = 0; i < 1000; i++)
            {
                brain.Tick(.02f, InRange);
                Assert.That(brain.LivingSummonCount, Is.EqualTo(2));
                Assert.That(brain.IsSummoning, Is.False);
                Assert.That(brain.TryRegisterSummon(3, brain.AttackSequence), Is.False);
            }
            Assert.That(brain.SummonCount, Is.EqualTo(2));
        }
        [Test] public void MinionDeath_ReturnsOnlyItsOwnSlotAndAllowsOneReplacement()
        {
            var brain = NewBrain(); Register(brain, 1); Register(brain, 2);
            Assert.That(brain.UnregisterSummon(1), Is.True);
            Assert.That(brain.UnregisterSummon(1), Is.False);
            Assert.That(brain.UnregisterSummon(99), Is.False);
            Register(brain, 3);
            Assert.That(brain.LivingSummonCount, Is.EqualTo(2));
            Assert.That(brain.SummonCount, Is.EqualTo(3));
        }
        [TestCase(.01f)] [TestCase(1.199f)]
        public void DamageBeforeRelease_InterruptsWithoutHalfSpawnAndStartsCooldown(float elapsed)
        {
            var brain = NewBrain(); ReachSummon(brain); brain.Tick(elapsed, InRange);
            var result = brain.ReceiveDamage(new DamageRequest(7, 1, 4f, 0f, AttackTag.Projectile));
            Assert.That(result.AppliedDamage, Is.GreaterThan(0));
            Assert.That(brain.InterruptCount, Is.EqualTo(1));
            Assert.That(brain.SummonCooldownRemaining, Is.EqualTo(1.5f));
            Assert.That(brain.TryRegisterSummon(1, brain.AttackSequence), Is.False);
            Assert.That(brain.LivingSummonCount, Is.Zero);
            for (int i = 0; i < 74; i++) { brain.Tick(.02f, InRange); Assert.That(brain.IsSummoning, Is.False); }
        }
        [Test] public void DamageAfterRelease_DoesNotRetroactivelyInterruptCompletedSummon()
        {
            var brain = NewBrain(); Register(brain, 1);
            brain.ReceiveDamage(new DamageRequest(7, 1, 4f, 0f, AttackTag.Projectile));
            Assert.That(brain.InterruptCount, Is.Zero);
            Assert.That(brain.LivingSummonCount, Is.EqualTo(1));
        }
        [Test] public void MinimumDamageHit_StillInterruptsUsingActualHealthResult()
        {
            var brain = NewBrain(); ReachSummon(brain);
            var result = brain.ReceiveDamage(new DamageRequest(7, 1, 1f, 0f, AttackTag.Light));
            Assert.That(result.AppliedDamage, Is.EqualTo(1f), "The existing health rule has a one-damage floor.");
            Assert.That(brain.InterruptCount, Is.EqualTo(1));
        }
        [Test] public void PostureBreak_InterruptsChantAndKeepsExecutionWindowBeforeRecovery()
        {
            var brain = NewBrain(); ReachSummon(brain);
            Assert.That(brain.ApplyCounterPosture(75f), Is.True);
            Assert.That(brain.InterruptCount, Is.EqualTo(1));
            brain.Tick(.5f, InRange);
            Assert.That(brain.IsPostureExecutionWindow, Is.True);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.HitReact));
        }
        [TestCase(AttackTag.Light)] [TestCase(AttackTag.Heavy)]
        public void OwnerDeath_ClearsExistingMinionsAndRejectsEveryLaterRelease(AttackTag tag)
        {
            var brain = NewBrain(); Register(brain, 1); Register(brain, 2);
            Assert.That(brain.ReceiveDamage(new DamageRequest(7, 1, 999f, 0f, tag)).Killed, Is.True);
            Assert.That(brain.LivingSummonCount, Is.Zero);
            Assert.That(brain.TryRegisterSummon(3, brain.AttackSequence), Is.False);
            brain.ClearSummons(); brain.ClearSummons();
            Assert.That(brain.LivingSummonCount, Is.Zero);
            Assert.That(brain.ReceiveDamage(new DamageRequest(7, 2, 999f, 0f, tag)).Accepted, Is.False);
        }
        [Test] public void OwnerDiesDuringWindup_NoUnfinishedEntityCanBeRegistered()
        {
            var brain = NewBrain(); ReachSummon(brain);
            brain.ReceiveDamage(new DamageRequest(1, 1, 999f, 0f, AttackTag.Heavy));
            brain.Tick(2f, InRange);
            Assert.That(brain.TryRegisterSummon(1, brain.AttackSequence), Is.False);
            Assert.That(brain.SummonCount, Is.Zero);
        }
        [Test] public void ReachableCloseRange_NeverTreatsLowDistanceAloneAsBlocked()
        {
            var brain = NewBrain(); var close = new RangedEnemyPerception(true, true, 5f, 0f, false);
            for (int i = 0; i < 500; i++) brain.Tick(.02f, close);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
            Assert.That(brain.SummonCount, Is.Zero);
            Assert.That(brain.IsSummoning, Is.False);
        }
        [Test] public void ConfirmedCorner_WaitsFullBlockedIntervalThenFullInterruptibleChant()
        {
            var brain = NewBrain(); var viable = new RangedEnemyPerception(true, true, 5f, 0f, false);
            var blocked = new RangedEnemyPerception(true, true, 5f, 0f, true);
            brain.Tick(3f, viable); // Ready cooldown, but positioning alone does not authorize a chant.
            brain.Tick(2.49f, blocked);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            brain.Tick(.011f, blocked);
            Assert.That(brain.IsSummoning, Is.True); Assert.That(brain.StateElapsed, Is.Zero);
            Assert.That(brain.CurrentWindupDuration, Is.EqualTo(1.2f));
            Assert.That(brain.TryRegisterSummon(11, brain.AttackSequence), Is.False);
            brain.Tick(1.19f, blocked);
            Assert.That(brain.IsSummoning, Is.True); Assert.That(brain.LivingSummonCount, Is.Zero);
            brain.Tick(.011f, blocked);
            Assert.That(brain.TryRegisterSummon(11, brain.AttackSequence), Is.True);
            Assert.That(brain.LivingSummonCount, Is.EqualTo(1));
        }
        [Test] public void CornerInterrupted_ResetBlockedTimerBeforeAnotherNormalChant()
        {
            var brain = NewBrain(); var viable = new RangedEnemyPerception(true, true, 5f, 0f, false);
            var blocked = new RangedEnemyPerception(true, true, 5f, 0f, true);
            brain.Tick(3f, viable); brain.Tick(2.5f, blocked);
            Assert.That(brain.IsSummoning, Is.True);
            brain.ReceiveDamage(new DamageRequest(7, 1, 4f, 0f, AttackTag.Projectile));
            Assert.That(brain.InterruptCount, Is.EqualTo(1)); Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
            Assert.That(brain.TryRegisterSummon(1, brain.AttackSequence), Is.False);
            brain.Tick(.35f, blocked); Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            brain.Tick(2.49f, blocked); Assert.That(brain.IsSummoning, Is.False);
            brain.Tick(.011f, blocked); Assert.That(brain.IsSummoning, Is.True);
            Assert.That(brain.StateElapsed, Is.Zero); Assert.That(brain.LivingSummonCount, Is.Zero);
        }
        [Test] public void ViableRetreatReturning_ResetsAccumulatedBlockedInterval()
        {
            var brain = NewBrain(); var blocked = new RangedEnemyPerception(true, true, 5f, 0f, true);
            brain.Tick(3f, new RangedEnemyPerception(true, true, 5f, 0f, false));
            brain.Tick(2.4f, blocked); Assert.That(brain.RetreatBlockedElapsed, Is.GreaterThan(2f));
            brain.Tick(.1f, new RangedEnemyPerception(true, true, 7f, 0f, false));
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
            brain.Tick(.2f, blocked); Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            Assert.That(brain.IsSummoning, Is.False);
        }
        [Test] public void ResetRetreatConstraint_DoesNotResetHealthCooldownOrAttackFacts()
        {
            var brain = NewBrain(); var close = new RangedEnemyPerception(true, true, 5f, 0f, false);
            brain.Tick(1f, close); brain.Tick(1f, new RangedEnemyPerception(true, true, 5f, 0f, true));
            Assert.That(brain.RetreatBlockedElapsed, Is.EqualTo(1f));
            float health = brain.Health.Current, cooldown = brain.SummonCooldownRemaining;
            brain.ResetRetreatConstraint();
            Assert.That(brain.RetreatBlockedElapsed, Is.Zero);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            Assert.That(brain.Health.Current, Is.EqualTo(health));
            Assert.That(brain.SummonCooldownRemaining, Is.EqualTo(cooldown));
            Assert.That(brain.AttackSequence, Is.Zero);
        }

        [Test] public void FullCapacityCorner_StillUsesWeakProjectileWithoutReplacingMinions()
        {
            var brain = NewBrain(); Register(brain, 1); Register(brain, 2);
            var blocked = new RangedEnemyPerception(true, true, 5f, 0f, true);
            for (int i = 0; i < 1000; i++)
            {
                brain.Tick(.02f, blocked);
                Assert.That(brain.IsSummoning, Is.False);
                Assert.That(brain.TryRegisterSummon(3, brain.AttackSequence), Is.False);
            }
            Assert.That(brain.LivingSummonCount, Is.EqualTo(2)); Assert.That(brain.SummonCount, Is.EqualTo(2));
            Assert.That(brain.CurrentAttack, Is.EqualTo(SummonerAttackKind.Projectile));
        }

        [Test] public void Reset_RestoresCleanCountersHealthAndInitialDelay()
        {
            var brain = NewBrain(); Register(brain, 1); brain.Reset();
            Assert.That(brain.SummonCount, Is.Zero); Assert.That(brain.LivingSummonCount, Is.Zero);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Idle));
            Assert.That(brain.Health.Normalized, Is.EqualTo(1f));
            Assert.That(brain.SummonCooldownRemaining, Is.EqualTo(2.8f));
        }
        [Test] public void SummonedFogwalker_UsesActualArmorAndDiesToOneHeavyOrThreeLightHits()
        {
            string meleeJson = File.ReadAllText("Assets/_Game/Data/M2/enemies.v1.json");
            var source = MeleeEnemyDefinitionJsonLoader.Load(meleeJson).GetRequired(Definition.MinionId);
            var minion = SummonedMeleeDefinition.From(source, Definition.MinionHealth);
            var light = new MeleeEnemyBrain(minion);
            Assert.That(light.ReceiveDamage(new DamageRequest(1, 1, 22f, 0f, AttackTag.Light)).Killed, Is.False);
            Assert.That(light.ReceiveDamage(new DamageRequest(1, 2, 26f, 0f, AttackTag.Light)).Killed, Is.False);
            Assert.That(light.ReceiveDamage(new DamageRequest(1, 3, 34f, 0f, AttackTag.Light)).Killed, Is.True);
            Assert.That(new MeleeEnemyBrain(minion).ReceiveDamage(new DamageRequest(1, 4, 55f, 0f, AttackTag.Heavy)).Killed, Is.True);
            Assert.That(source.MaximumHealth, Is.Not.EqualTo(minion.MaximumHealth), "Never mutate the session template.");
            Assert.That(minion.ComboAttackDuration, Is.EqualTo(source.ComboAttackDuration));
        }
        [TestCase("\"schemaVersion\": 1", "\"schemaVersion\": 2", typeof(NotSupportedException))]
        [TestCase("\"maximumPosture\": 75", "\"maximumPosture\": 80", typeof(FormatException))]
        [TestCase("\"summonWindup\": 1.2", "\"summonWindup\": 0", typeof(FormatException))]
        [TestCase("\"minionId\": \"enemy:fogwalker\"", "\"minionId\": \"quest:wrong\"", typeof(FormatException))]
        public void Loader_RejectsInvalidDataRatherThanDefaultingIt(string from, string to, Type exception)
        { Assert.Throws(exception, () => SummonerEnemyDefinitionJsonLoader.Load(Json.Replace(from, to))); }
        [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void Tick_RejectsInvalidClock(float delta) => Assert.Throws<ArgumentOutOfRangeException>(() => NewBrain().Tick(delta, InRange));
    }
}
