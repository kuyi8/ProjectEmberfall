using Emberfall.AI.Domain;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class RangedEnemyBrainTests
    {
        [Test]
        public void VisibleTarget_DrivesApproachWindupReleaseRecoveryAndRetreat()
        {
            RangedEnemyDefinition definition = CreateDefinition();
            var brain = new RangedEnemyBrain(definition);

            brain.Tick(0.01f, Perception(true, true, 11f, 0f));
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Approach));

            brain.Tick(0.01f, Perception(true, true, 7f, 2f));
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Windup));

            brain.Tick(0.8f, Perception(true, true, 7f, 2f));
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Release));
            Assert.That(brain.AttackSequence, Is.EqualTo(1));
            Assert.That(brain.IsProjectileReleaseOpen, Is.True);

            brain.Tick(0.4f, Perception(true, true, 7f, 2f));
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Recovery));

            brain.Tick(1.1f, Perception(true, true, 3f, 2f));
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Retreat));
            Assert.That(brain.WantsRetreatMovement, Is.True);
        }

        [Test]
        public void LostTarget_ReturnsHomeAndDeathWaitsForAuthorityReset()
        {
            RangedEnemyDefinition definition = CreateDefinition();
            var brain = new RangedEnemyBrain(definition);
            brain.Tick(0.01f, Perception(true, true, 7f, 0f));

            brain.Tick(0.01f, Perception(false, false, float.PositiveInfinity, 4f));
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Return));
            Assert.That(brain.WantsReturnMovement, Is.True);

            DamageResult lethal = brain.ReceiveDamage(
                new DamageRequest(1, 1, 999f, 50f, AttackTag.Heavy));
            Assert.That(lethal.Killed, Is.True);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Dead));

            brain.Tick(definition.RespawnDelay + 0.1f, Perception(false, false, float.PositiveInfinity, 4f));
            Assert.That(brain.IsResetReady, Is.True);
            brain.Reset();
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Idle));
            Assert.That(brain.Health.Current, Is.EqualTo(definition.MaximumHealth));
        }

        [Test]
        public void RangedEnemy_RequiresPostureBreakBeforeHitReact()
        {
            var brain = new RangedEnemyBrain(CreateDefinition());

            DamageResult first = brain.ReceiveDamage(new DamageRequest(1, 1, 10f, 18f, AttackTag.Light));
            Assert.That(first.Staggered, Is.False);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Idle));

            DamageResult second = brain.ReceiveDamage(new DamageRequest(1, 2, 10f, 20f, AttackTag.Light));
            Assert.That(second.Staggered, Is.True);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.HitReact));
        }

        [Test]
        public void SecondAttack_SelectsDelayedGroundRuneWithIndependentTiming()
        {
            var brain = new RangedEnemyBrain(CreateDefinition());
            RangedEnemyPerception combat = Perception(true, true, 7f, 2f);
            brain.Tick(0.01f, combat);
            Assert.That(brain.CurrentAttack, Is.EqualTo(RangedAttackKind.Projectile));
            brain.Tick(0.73f, combat);
            brain.Tick(0.36f, combat);
            brain.Tick(1.01f, combat);

            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Windup));
            Assert.That(brain.CurrentAttack, Is.EqualTo(RangedAttackKind.GroundRune));
            Assert.That(brain.CurrentWindupDuration, Is.EqualTo(0.9f));
            brain.Tick(0.91f, combat);
            Assert.That(brain.State, Is.EqualTo(RangedEnemyState.Release));
            Assert.That(brain.AttackSequence, Is.EqualTo(2));
        }

        [Test]
        public void DelayedAreaAttack_ResolvesExactlyOnceAfterFuse()
        {
            var timer = new DelayedAreaAttackModel(1f);
            timer.Tick(0.99f);
            Assert.That(timer.IsReady, Is.False);
            Assert.That(timer.Resolve(), Is.False);
            timer.Tick(0.02f);
            Assert.That(timer.IsReady, Is.True);
            Assert.That(timer.Resolve(), Is.True);
            Assert.That(timer.Resolve(), Is.False);
            Assert.That(timer.IsResolved, Is.True);
        }

        private static RangedEnemyPerception Perception(
            bool available,
            bool visible,
            float targetDistance,
            float spawnDistance) =>
            new RangedEnemyPerception(available, visible, targetDistance, spawnDistance);

        private static RangedEnemyDefinition CreateDefinition() =>
            new RangedEnemyDefinition(
                new ContentId("enemy:ranged-test"),
                new ContentId("text:enemy.ranged-test"),
                105f,
                1f,
                14f,
                18f,
                190f,
                22f,
                5f,
                9f,
                2.6f,
                480f,
                0.72f,
                0.35f,
                1f,
                9.5f,
                3f,
                0.22f,
                17f,
                8f,
                0.3f,
                3.5f);
    }
}
