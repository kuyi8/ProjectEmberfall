using Emberfall.AI.Domain;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class ShieldEnemyBrainTests
    {
        [Test]
        public void FrontalHeavyHits_BreakGuardThenExposeHealth()
        {
            var brain = new ShieldEnemyBrain(CreateDefinition());

            DamageResult first = brain.ReceiveDamage(
                new DamageRequest(1, 1, 35f, 40f, AttackTag.Heavy), true);
            Assert.That(first.Blocked, Is.True);
            Assert.That(first.Accepted, Is.False);
            Assert.That(first.GuardBroken, Is.False);
            Assert.That(brain.Health.Current, Is.EqualTo(200f));
            Assert.That(brain.GuardCurrent, Is.EqualTo(40f));

            DamageResult breakHit = brain.ReceiveDamage(
                new DamageRequest(1, 2, 35f, 40f, AttackTag.Heavy), true);
            Assert.That(breakHit.Blocked, Is.True);
            Assert.That(breakHit.GuardBroken, Is.True);
            Assert.That(brain.State, Is.EqualTo(ShieldEnemyState.GuardBreak));

            DamageResult punish = brain.ReceiveDamage(
                new DamageRequest(1, 3, 40f, 18f, AttackTag.Light), true);
            Assert.That(punish.Accepted, Is.True);
            Assert.That(punish.AppliedDamage, Is.EqualTo(58f).Within(0.001f));
            Assert.That(brain.State, Is.EqualTo(ShieldEnemyState.GuardBreak));
        }

        [Test]
        public void RearHit_BypassesGuardAndGuardRestoresAfterBreakWindow()
        {
            ShieldEnemyDefinition definition = CreateDefinition();
            var brain = new ShieldEnemyBrain(definition);

            DamageResult rear = brain.ReceiveDamage(
                new DamageRequest(1, 1, 30f, 18f, AttackTag.Light), false);
            Assert.That(rear.Accepted, Is.True);
            Assert.That(rear.AppliedDamage, Is.EqualTo(25f).Within(0.001f));
            Assert.That(brain.GuardCurrent, Is.EqualTo(definition.GuardCapacity));

            brain.ReceiveDamage(new DamageRequest(1, 2, 1f, 80f, AttackTag.Heavy), true);
            Assert.That(brain.State, Is.EqualTo(ShieldEnemyState.GuardBreak));
            brain.Tick(definition.GuardBreakDuration + 0.01f,
                new MeleeEnemyPerception(true, true, 3f, 2f));
            Assert.That(brain.State, Is.EqualTo(ShieldEnemyState.Chase));
            Assert.That(brain.GuardCurrent, Is.EqualTo(definition.GuardCapacity));
        }

        [Test]
        public void SecondAttack_SelectsFastHighPostureShieldBash()
        {
            ShieldEnemyDefinition definition = CreateDefinition();
            var brain = new ShieldEnemyBrain(definition);
            var close = new MeleeEnemyPerception(true, true, 1.4f, 2f);
            brain.Tick(0.01f, new MeleeEnemyPerception(true, true, 6f, 0f));
            brain.Tick(0.01f, close);
            Assert.That(brain.CurrentAttack, Is.EqualTo(ShieldAttackKind.HeavyStrike));
            brain.Tick(0.73f, close);
            brain.Tick(0.53f, close);
            brain.Tick(0.86f, close);
            brain.Tick(0.01f, close);

            Assert.That(brain.State, Is.EqualTo(ShieldEnemyState.Windup));
            Assert.That(brain.CurrentAttack, Is.EqualTo(ShieldAttackKind.ShieldBash));
            Assert.That(brain.CurrentWindupDuration, Is.LessThan(definition.WindupDuration));
            Assert.That(brain.CurrentPostureDamage, Is.GreaterThan(definition.PostureDamage));
        }

        [Test]
        public void DeniedAttackSlot_KeepsGuardInChase()
        {
            var brain = new ShieldEnemyBrain(CreateDefinition());
            brain.Tick(0.01f, new MeleeEnemyPerception(true, true, 6f, 0f));
            brain.Tick(0.01f, new MeleeEnemyPerception(true, true, 1.4f, 2f, false));
            Assert.That(brain.State, Is.EqualTo(ShieldEnemyState.Chase));

            brain.Tick(0.01f, new MeleeEnemyPerception(true, true, 1.4f, 2f, true));
            Assert.That(brain.State, Is.EqualTo(ShieldEnemyState.Windup));
        }

        [Test]
        public void ScorchedElite_SelectsDelayedBurstWithoutOpeningMeleeWindow()
        {
            ShieldEnemyDefinition definition = CreateScorchedDefinition();
            var brain = new ShieldEnemyBrain(definition);
            var close = new MeleeEnemyPerception(true, true, 1.4f, 2f);

            brain.Tick(0.01f, new MeleeEnemyPerception(true, true, 6f, 0f));
            brain.Tick(0.01f, close);
            brain.Tick(definition.WindupDuration + 0.01f, close);
            brain.Tick(definition.AttackDuration + 0.01f, close);
            brain.Tick(definition.RecoveryDuration + 0.01f, close);
            brain.Tick(0.01f, close);

            Assert.That(brain.CurrentAttack, Is.EqualTo(ShieldAttackKind.ScorchedBurst));
            brain.Tick(definition.ScorchedBurstWindupDuration + 0.01f, close);
            Assert.That(brain.State, Is.EqualTo(ShieldEnemyState.Attack));
            Assert.That(brain.IsDamageWindowOpen, Is.False);
            Assert.That(brain.CurrentAttackDuration, Is.EqualTo(definition.ScorchedBurstAttackDuration));
        }

        private static ShieldEnemyDefinition CreateDefinition() =>
            new ShieldEnemyDefinition(
                new ContentId("enemy:test-guard"),
                new ContentId("text:enemy.test-guard"),
                200f, 5f, 10f, 14f, 180f, 18f, 1.85f, 1.95f, 360f,
                0.72f, 0.52f, 0.18f, 0.34f, 0.85f, 25f, 24f, 0.38f, 4f,
                80f, 140f, 2.4f, 1.45f);

        private static ShieldEnemyDefinition CreateScorchedDefinition() =>
            new ShieldEnemyDefinition(
                new ContentId("enemy:test-scorched"),
                new ContentId("text:enemy.test-scorched"),
                345f, 7f, 11f, 15f, 190f, 19f, 1.95f, 2.05f, 380f,
                0.78f, 0.54f, 0.19f, 0.36f, 0.88f, 28f, 28f, 0.26f, 4.5f,
                138f, 145f, 2.15f, 1.4f,
                scorchedBurstEnabled: true,
                scorchedBurstWeight: 0.78f,
                scorchedBurstCooldown: 6f,
                scorchedBurstWindupDuration: 1.05f,
                scorchedBurstAttackDuration: 0.2f,
                scorchedBurstRecoveryDuration: 1f,
                scorchedBurstTriggerDelay: 0.85f,
                scorchedBurstRadius: 2.65f,
                scorchedBurstDamage: 32f,
                scorchedBurstPostureDamage: 42f);
    }
}
