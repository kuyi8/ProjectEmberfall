using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class CombatResourceTests
    {
        [Test]
        public void Stamina_RegeneratesOnlyAfterConfiguredDelay()
        {
            var stamina = new StaminaModel(100f, 20f, 0.7f);
            stamina.TrySpend(24f);

            stamina.Tick(0.69f, true);
            Assert.That(stamina.Current, Is.EqualTo(76f).Within(0.001f));

            stamina.Tick(0.02f, true);
            Assert.That(stamina.Current, Is.GreaterThan(76f));
        }

        [Test]
        public void Health_AppliesArmorWithMinimumChipDamage()
        {
            var health = new HealthModel(100f);

            float normal = health.ApplyDamage(25f, 5f);
            float chip = health.ApplyDamage(2f, 100f);

            Assert.That(normal, Is.EqualTo(20f));
            Assert.That(chip, Is.EqualTo(1f));
            Assert.That(health.Current, Is.EqualTo(79f));
        }

        [Test]
        public void HitRegistry_AllowsOneTargetOncePerAttackSequence()
        {
            var registry = new HitRegistry();

            Assert.That(registry.TryRegister(1, 42), Is.True);
            Assert.That(registry.TryRegister(1, 42), Is.False);
            Assert.That(registry.TryRegister(2, 42), Is.True);
        }

        [Test]
        public void Posture_RegeneratesOnlyAfterDelayAndOnlyWhenAllowed()
        {
            var posture = new PostureModel(100f, 20f, 0.5f);
            posture.ApplyDamage(40f);

            posture.Tick(0.6f, false);
            Assert.That(posture.Current, Is.EqualTo(60f));

            posture.Tick(0.1f, true);
            Assert.That(posture.Current, Is.GreaterThan(60f));
        }

        [Test]
        public void PartialRestores_ClampAtMaximum()
        {
            var health = new HealthModel(100f);
            health.ApplyDamage(70f, 0f);
            Assert.That(health.Restore(45f), Is.EqualTo(45f));
            Assert.That(health.Restore(45f), Is.EqualTo(25f));

            var stamina = new StaminaModel(80f, 10f, 0.5f);
            stamina.TrySpend(50f);
            Assert.That(stamina.Restore(35f), Is.EqualTo(35f));
            Assert.That(stamina.Restore(35f), Is.EqualTo(15f));
        }

        [Test]
        public void RuneBlessings_OnlyModifyTheirAuthoredAttackCondition()
        {
            Assert.That(
                RuneBlessingRules.GetBonusDamage(RuneBlessing.Ember, AttackTag.Heavy, false, true),
                Is.EqualTo(16f));
            Assert.That(
                RuneBlessingRules.GetBonusDamage(RuneBlessing.Ember, AttackTag.Heavy, false, false),
                Is.Zero);
            Assert.That(
                RuneBlessingRules.GetBonusDamage(RuneBlessing.Ember, AttackTag.Light, false, true),
                Is.Zero);
            Assert.That(RuneBlessingRules.ArmsGuardCounter(RuneBlessing.Guard, true), Is.True);
            Assert.That(
                RuneBlessingRules.GetBonusDamage(RuneBlessing.Guard, AttackTag.Light, true, false),
                Is.EqualTo(12f));
            Assert.That(
                RuneBlessingRules.GetBonusDamage(RuneBlessing.Guard, AttackTag.Heavy, true, true),
                Is.Zero);
        }

        [Test]
        public void HealingFlask_ConsumesAndRefillsWithoutExceedingMaximum()
        {
            var flask = new HealingFlaskModel(2);

            Assert.That(flask.TryConsume(), Is.True);
            Assert.That(flask.TryConsume(), Is.True);
            Assert.That(flask.TryConsume(), Is.False);
            flask.Refill();

            Assert.That(flask.CurrentCharges, Is.EqualTo(2));
        }

        [Test]
        public void HealingFlask_CapacityUpgradeGrantsExactlyTheNewCharges()
        {
            var flask = new HealingFlaskModel(2);
            Assert.That(flask.TryConsume(), Is.True);

            flask.IncreaseMaximum(1);

            Assert.That(flask.MaximumCharges, Is.EqualTo(3));
            Assert.That(flask.CurrentCharges, Is.EqualTo(2));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => flask.IncreaseMaximum(0));
        }
    }
}
