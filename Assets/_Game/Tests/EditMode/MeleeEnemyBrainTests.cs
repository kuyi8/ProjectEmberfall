using Emberfall.AI.Domain;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class MeleeEnemyBrainTests
    {
        [Test]
        public void VisibleTarget_DrivesChaseWindupAttackRecoveryCycle()
        {
            MeleeEnemyDefinition definition = CreateDefinition();
            var brain = new MeleeEnemyBrain(definition);

            brain.Tick(0.01f, Perception(true, true, 6f, 0f));
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Chase));

            brain.Tick(0.01f, Perception(true, true, 1.5f, 2f));
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Windup));

            brain.Tick(0.6f, Perception(true, true, 1.5f, 2f));
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Attack));
            Assert.That(brain.AttackSequence, Is.EqualTo(1));

            brain.Tick(0.2f, Perception(true, true, 1.5f, 2f));
            Assert.That(brain.IsDamageWindowOpen, Is.True);

            brain.Tick(0.3f, Perception(true, true, 1.5f, 2f));
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Recovery));
            Assert.That(brain.IsDamageWindowOpen, Is.False);

            brain.Tick(0.8f, Perception(true, true, 2.5f, 2f));
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Chase));
        }

        [Test]
        public void LostTarget_ReturnsHomeAndBecomesIdle()
        {
            var brain = new MeleeEnemyBrain(CreateDefinition());
            brain.Tick(0.01f, Perception(true, true, 6f, 0f));

            brain.Tick(0.01f, Perception(true, false, 14f, 4f));
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Return));
            Assert.That(brain.WantsReturnMovement, Is.True);

            brain.Tick(0.01f, Perception(false, false, float.PositiveInfinity, 0.1f));
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Idle));
        }

        [Test]
        public void DeniedAttackSlot_KeepsCloseEnemyInChase()
        {
            var brain = new MeleeEnemyBrain(CreateDefinition());
            brain.Tick(0.01f, Perception(true, true, 6f, 0f));
            brain.Tick(0.01f, new MeleeEnemyPerception(true, true, 1.5f, 2f, false));
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Chase));

            brain.Tick(0.01f, new MeleeEnemyPerception(true, true, 1.5f, 2f, true));
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Windup));
        }

        [Test]
        public void PostureDamage_OnlyInterruptsOnBreakThenDeathWaitsForAuthorityReset()
        {
            MeleeEnemyDefinition definition = CreateDefinition();
            var brain = new MeleeEnemyBrain(definition);

            DamageResult first = brain.ReceiveDamage(new DamageRequest(1, 1, 20f, 8f, AttackTag.Light));
            Assert.That(first.AppliedDamage, Is.EqualTo(17f).Within(0.001f));
            Assert.That(first.Staggered, Is.False);
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Idle));

            DamageResult breakHit = brain.ReceiveDamage(new DamageRequest(1, 2, 20f, 50f, AttackTag.Heavy));
            Assert.That(breakHit.Staggered, Is.True);
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.HitReact));

            DamageResult lethal = brain.ReceiveDamage(new DamageRequest(1, 3, 999f, 30f, AttackTag.Heavy));
            Assert.That(lethal.Killed, Is.True);
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Dead));

            brain.Tick(definition.RespawnDelay + 0.01f, Perception(false, false, float.PositiveInfinity, 5f));
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Dead));
            Assert.That(brain.IsResetReady, Is.True);

            brain.Reset();
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Idle));
            Assert.That(brain.Health.Current, Is.EqualTo(definition.MaximumHealth));
            Assert.That(brain.AttackSequence, Is.Zero);
        }

        [Test]
        public void SecondAttack_UsesDelayedTwoHitWindowsAfterQuickSlash()
        {
            var brain = new MeleeEnemyBrain(CreateDefinition());
            MeleeEnemyPerception close = Perception(true, true, 1.5f, 2f);
            brain.Tick(0.01f, Perception(true, true, 6f, 0f));
            brain.Tick(0.01f, close);
            Assert.That(brain.CurrentAttack, Is.EqualTo(MeleeAttackKind.QuickSlash));
            brain.Tick(0.6f, close);
            brain.Tick(0.49f, close);
            brain.Tick(0.73f, close);
            brain.Tick(0.01f, close);

            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Windup));
            Assert.That(brain.CurrentAttack, Is.EqualTo(MeleeAttackKind.DelayedCombo));
            brain.Tick(0.83f, close);
            Assert.That(brain.State, Is.EqualTo(MeleeEnemyState.Attack));
            brain.Tick(0.2f, close);
            int firstHit = brain.CurrentHitSequence;
            Assert.That(brain.DamageWindowIndex, Is.EqualTo(1));
            brain.Tick(0.38f, close);
            Assert.That(brain.DamageWindowIndex, Is.EqualTo(2));
            Assert.That(brain.CurrentHitSequence, Is.Not.EqualTo(firstHit));
        }

        private static MeleeEnemyPerception Perception(
            bool available,
            bool visible,
            float targetDistance,
            float spawnDistance) =>
            new MeleeEnemyPerception(available, visible, targetDistance, spawnDistance);

        internal static MeleeEnemyDefinition CreateDefinition() =>
            new MeleeEnemyDefinition(
                new ContentId("enemy:test"),
                new ContentId("text:enemy.test"),
                100f,
                3f,
                9f,
                13f,
                170f,
                16f,
                1.75f,
                2.35f,
                420f,
                0.58f,
                0.48f,
                0.16f,
                0.31f,
                0.72f,
                20f,
                18f,
                0.34f,
                3.5f);
    }
}
