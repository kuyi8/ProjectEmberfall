using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class CombatStateMachineTests
    {
        [Test]
        public void LightInput_BufferedBeforeWindow_AdvancesCombo()
        {
            CombatStateMachine machine = CreateMachine();
            Assert.That(machine.Submit(CombatCommand.LightAttack), Is.True);

            machine.Tick(0.20f);
            Assert.That(machine.Submit(CombatCommand.LightAttack), Is.False);
            machine.Tick(0.09f);

            Assert.That(machine.State, Is.EqualTo(CombatState.LightAttack2));
            Assert.That(machine.AttackSequence, Is.EqualTo(2));
        }

        [Test]
        public void Dodge_AfterCancelWindow_CancelsLightAttack()
        {
            CombatStateMachine machine = CreateMachine();
            machine.Submit(CombatCommand.LightAttack);
            machine.Tick(0.37f);

            Assert.That(machine.Submit(CombatCommand.Dodge), Is.True);
            Assert.That(machine.State, Is.EqualTo(CombatState.Dodge));
        }

        [Test]
        public void Dodge_InvulnerabilityCoversTravelButNotRecovery()
        {
            CombatStateMachine machine = CreateMachine();
            machine.Submit(CombatCommand.Dodge);
            var request = new DamageRequest(7, 1, 40f, 10f, AttackTag.Hazard);

            machine.Tick(0.40f);
            DamageResult evaded = machine.ReceiveDamage(request);
            machine.Tick(0.04f);
            DamageResult hit = machine.ReceiveDamage(request);

            Assert.That(evaded.Invulnerable, Is.True);
            Assert.That(hit.Accepted, Is.True);
            Assert.That(machine.Health.Current, Is.EqualTo(80f).Within(0.001f));
        }

        [TestCase(CombatState.Locomotion, false)]
        [TestCase(CombatState.Dodge, false)]
        [TestCase(CombatState.HitReact, false)]
        [TestCase(CombatState.LightAttack1, true)]
        [TestCase(CombatState.LightAttack2, true)]
        [TestCase(CombatState.LightAttack3, true)]
        [TestCase(CombatState.HeavyCharge, true)]
        [TestCase(CombatState.HeavyAttack, true)]
        [TestCase(CombatState.RangedAttack, true)]
        [TestCase(CombatState.Guard, true)]
        public void LockOnFacingPolicy_SeparatesLocomotionFromAttackFacing(CombatState state, bool expected)
        {
            Assert.That(LockOnFacingPolicy.ShouldFaceTarget(state), Is.EqualTo(expected));
        }

        [Test]
        public void HeavyCharge_ReachesAuthoredFullDamage()
        {
            CombatStateMachine machine = CreateMachine();
            machine.Submit(CombatCommand.HeavyPressed);
            machine.Tick(0.55f);

            Assert.That(machine.Submit(CombatCommand.HeavyReleased), Is.True);
            Assert.That(machine.State, Is.EqualTo(CombatState.HeavyAttack));
            Assert.That(machine.CurrentAttackDamage, Is.EqualTo(55f).Within(0.001f));
            Assert.That(machine.IsHeavyFullyCharged, Is.True);
        }

        [Test]
        public void RangedAttack_ReleasesOnceAndRemainsOnIndependentCooldown()
        {
            CombatStateMachine machine = CreateMachine();

            Assert.That(machine.Submit(CombatCommand.RangedAttack), Is.True);
            Assert.That(machine.State, Is.EqualTo(CombatState.RangedAttack));
            Assert.That(machine.CurrentAttackTag, Is.EqualTo(AttackTag.Projectile));
            Assert.That(machine.RangedReleaseSequence, Is.Zero);
            machine.Tick(0.21f);
            Assert.That(machine.RangedReleaseSequence, Is.Zero);
            machine.Tick(0.02f);
            Assert.That(machine.RangedReleaseSequence, Is.EqualTo(1));
            machine.Tick(0.5f);
            Assert.That(machine.State, Is.EqualTo(CombatState.Locomotion));
            Assert.That(machine.Submit(CombatCommand.RangedAttack), Is.False);

            machine.Tick(3f);
            Assert.That(machine.RangedCooldownRemaining, Is.Zero.Within(0.001f));
            Assert.That(machine.Submit(CombatCommand.RangedAttack), Is.True);
        }

        [Test]
        public void RangedAttack_InterruptedBeforeReleaseConsumesCooldownWithoutSpawning()
        {
            CombatStateMachine machine = CreateMachine();
            Assert.That(machine.Submit(CombatCommand.RangedAttack), Is.True);
            machine.Tick(0.1f);

            DamageResult result = machine.ReceiveDamage(
                new DamageRequest(7, 31, 10f, 0f, AttackTag.Light));

            Assert.That(result.Accepted, Is.True);
            Assert.That(machine.State, Is.EqualTo(CombatState.HitReact));
            Assert.That(machine.RangedReleaseSequence, Is.Zero);
            Assert.That(machine.RangedCooldownRemaining, Is.GreaterThan(3f));
        }

        [Test]
        public void Heal_ResolvesLateInCommitmentAndConsumesOneCharge()
        {
            CombatStateMachine machine = CreateMachine();
            machine.ReceiveDamage(new DamageRequest(7, 1, 60f, 0f, AttackTag.Hazard));
            machine.Tick(0.43f);

            Assert.That(machine.Submit(CombatCommand.Heal), Is.True);
            machine.Tick(0.77f);
            Assert.That(machine.Health.Current, Is.EqualTo(60f).Within(0.001f));
            Assert.That(machine.HealingFlasks.CurrentCharges, Is.EqualTo(2));

            machine.Tick(0.02f);
            Assert.That(machine.Health.Current, Is.EqualTo(114f).Within(0.001f));
            Assert.That(machine.HealingFlasks.CurrentCharges, Is.EqualTo(1));
            Assert.That(machine.HealSequence, Is.EqualTo(1));
        }

        [Test]
        public void Heal_InterruptedBeforeResolveDoesNotConsumeCharge()
        {
            CombatStateMachine machine = CreateMachine();
            machine.ReceiveDamage(new DamageRequest(7, 1, 60f, 0f, AttackTag.Hazard));
            machine.Tick(0.43f);
            machine.Submit(CombatCommand.Heal);
            machine.Tick(0.4f);

            DamageResult result = machine.ReceiveDamage(new DamageRequest(7, 2, 10f, 0f, AttackTag.Light));

            Assert.That(result.Accepted, Is.True);
            Assert.That(machine.State, Is.EqualTo(CombatState.HitReact));
            Assert.That(machine.HealingFlasks.CurrentCharges, Is.EqualTo(2));
            Assert.That(machine.HealSequence, Is.Zero);
        }

        [Test]
        public void Reset_RefillsHealingFlasks()
        {
            CombatStateMachine machine = CreateMachine();
            machine.ReceiveDamage(new DamageRequest(7, 1, 60f, 0f, AttackTag.Hazard));
            machine.Tick(0.43f);
            machine.Submit(CombatCommand.Heal);
            machine.Tick(0.8f);
            Assert.That(machine.HealingFlasks.CurrentCharges, Is.EqualTo(1));

            machine.Reset();

            Assert.That(machine.HealingFlasks.CurrentCharges, Is.EqualTo(2));
            Assert.That(machine.Health.Current, Is.EqualTo(machine.Health.Maximum));
        }

        [Test]
        public void PerfectGuard_NegatesDamageAndReturnsCounterPostureDamage()
        {
            CombatStateMachine machine = CreateMachine();
            Assert.That(machine.Submit(CombatCommand.GuardPressed), Is.True);

            DamageResult result = machine.ReceiveDamage(
                new DamageRequest(7, 1, 40f, 30f, AttackTag.Light, true, true));

            Assert.That(result.PerfectGuard, Is.True);
            Assert.That(result.Defended, Is.True);
            Assert.That(result.AppliedDamage, Is.Zero);
            Assert.That(result.CounterPostureDamage, Is.EqualTo(60f));
            Assert.That(machine.Posture.Current, Is.EqualTo(95.5f).Within(0.001f));
            Assert.That(machine.State, Is.EqualTo(CombatState.Guard));
        }

        [Test]
        public void Guard_BackAttackBypassesDefense()
        {
            CombatStateMachine machine = CreateMachine();
            machine.Submit(CombatCommand.GuardPressed);

            DamageResult result = machine.ReceiveDamage(
                new DamageRequest(7, 1, 40f, 30f, AttackTag.Light, true, false));

            Assert.That(result.Defended, Is.False);
            Assert.That(result.AppliedDamage, Is.EqualTo(40f));
            Assert.That(machine.State, Is.EqualTo(CombatState.HitReact));
        }

        [Test]
        public void SustainedGuardDamage_BreaksPostureThenRecovers()
        {
            CombatStateMachine machine = CreateMachine();
            machine.Submit(CombatCommand.GuardPressed);
            machine.Tick(0.201f);

            DamageResult result = machine.ReceiveDamage(
                new DamageRequest(7, 1, 20f, 100f, AttackTag.Heavy, true, true));

            Assert.That(result.GuardBroken, Is.True);
            Assert.That(machine.State, Is.EqualTo(CombatState.GuardBreak));
            Assert.That(machine.Submit(CombatCommand.Dodge), Is.False);
            machine.Tick(0.79f);
            Assert.That(machine.State, Is.EqualTo(CombatState.Locomotion));
            Assert.That(machine.Posture.Current, Is.EqualTo(machine.Posture.Maximum));
        }

        [Test]
        public void LethalDamage_EntersExplicitDeadState()
        {
            CombatStateMachine machine = CreateMachine();

            DamageResult result = machine.ReceiveDamage(new DamageRequest(9, 1, 999f, 100f, AttackTag.Heavy));

            Assert.That(result.Killed, Is.True);
            Assert.That(machine.State, Is.EqualTo(CombatState.Dead));
            Assert.That(machine.Submit(CombatCommand.LightAttack), Is.False);
        }

        [Test]
        public void ForcedEnvironmentDeath_BypassesDodgeInvulnerabilityAndIsIdempotent()
        {
            CombatStateMachine machine = CreateMachine();
            Assert.That(machine.Submit(CombatCommand.Dodge), Is.True);
            machine.Tick(0.1f);
            Assert.That(machine.IsInvulnerable, Is.True);

            Assert.That(machine.ForceDeath(), Is.True);
            Assert.That(machine.State, Is.EqualTo(CombatState.Dead));
            Assert.That(machine.Health.Current, Is.EqualTo(0f));
            Assert.That(machine.ForceDeath(), Is.False);
        }

        [Test]
        public void DodgeTravelProfile_CompletesBeforeRecoveryAndIsMonotonic()
        {
            float previous = 0f;
            for (int step = 0; step <= 100; step++)
            {
                float value = DodgeTravelProfile.Evaluate(step / 100f);
                Assert.That(value, Is.GreaterThanOrEqualTo(previous));
                previous = value;
            }

            Assert.That(DodgeTravelProfile.Evaluate(0f), Is.EqualTo(0f));
            Assert.That(DodgeTravelProfile.Evaluate(DodgeTravelProfile.TravelEndNormalized), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(DodgeTravelProfile.Evaluate(1f), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void PerfectGuard_WindowAcceptsAtPointTwoSecondsButNotAfter()
        {
            CombatStateMachine perfect = CreateMachine();
            perfect.Submit(CombatCommand.GuardPressed);
            perfect.Tick(0.20f);
            DamageResult accepted = perfect.ReceiveDamage(
                new DamageRequest(7, 1, 20f, 10f, AttackTag.Light, true, true));

            CombatStateMachine late = CreateMachine();
            late.Submit(CombatCommand.GuardPressed);
            late.Tick(0.201f);
            DamageResult rejected = late.ReceiveDamage(
                new DamageRequest(7, 1, 20f, 10f, AttackTag.Light, true, true));

            Assert.That(accepted.PerfectGuard, Is.True);
            Assert.That(rejected.PerfectGuard, Is.False);
        }

        [Test]
        public void PerfectDodge_RestoresStaminaAndEmpowersExactlyNextMeleeAttack()
        {
            CombatStateMachine machine = CreateMachine();
            Assert.That(machine.Submit(CombatCommand.Dodge), Is.True);
            float afterDodgeCost = machine.Stamina.Current;
            machine.Tick(0.10f);

            DamageResult evaded = machine.ReceiveDamage(
                new DamageRequest(7, 1, 40f, 10f, AttackTag.Light));

            Assert.That(evaded.PerfectDodge, Is.True);
            Assert.That(machine.Stamina.Current, Is.EqualTo(afterDodgeCost + 20f).Within(0.001f));
            Assert.That(machine.PerfectDodgeAttackReady, Is.True);
            machine.Tick(machine.StateDuration);
            Assert.That(machine.Submit(CombatCommand.LightAttack), Is.True);
            Assert.That(machine.CurrentAttackEmpowered, Is.True);
            Assert.That(machine.CurrentAttackDamage, Is.EqualTo(34f).Within(0.001f));
            Assert.That(machine.CurrentAttackPostureBonus, Is.EqualTo(18f).Within(0.001f));
            machine.Tick(machine.StateDuration);
            Assert.That(machine.Submit(CombatCommand.LightAttack), Is.True);
            Assert.That(machine.CurrentAttackEmpowered, Is.False);
            Assert.That(machine.CurrentAttackDamage, Is.EqualTo(22f).Within(0.001f));
        }

        [Test]
        public void Sprint_RequiresWarmupThenDrainsAuthoritativeStamina()
        {
            CombatStateMachine machine = CreateMachine();
            machine.SetSprintRequested(true);

            machine.Tick(0.49f);
            Assert.That(machine.IsSprinting, Is.False);
            float beforeDrain = machine.Stamina.Current;
            machine.Tick(0.02f);
            Assert.That(machine.IsSprinting, Is.True);
            Assert.That(machine.Stamina.Current, Is.EqualTo(beforeDrain - 0.24f).Within(0.001f));
            machine.Tick(1f);
            Assert.That(machine.Stamina.Current, Is.EqualTo(beforeDrain - 12.24f).Within(0.001f));

            machine.SetSprintRequested(false);
            Assert.That(machine.IsSprinting, Is.False);
        }

        [Test]
        public void Execution_SpendsStaminaGrantsInvulnerabilityAndResolvesOnce()
        {
            CombatStateMachine machine = CreateMachine();
            float before = machine.Stamina.Current;

            Assert.That(machine.Submit(CombatCommand.Execution), Is.True);
            Assert.That(machine.State, Is.EqualTo(CombatState.Execution));
            Assert.That(machine.Stamina.Current, Is.EqualTo(before - 22f).Within(0.001f));
            Assert.That(machine.IsInvulnerable, Is.True);
            Assert.That(machine.ReceiveDamage(
                new DamageRequest(7, 1, 999f, 0f, AttackTag.Heavy)).Invulnerable, Is.True);

            machine.Tick(0.31f);
            Assert.That(machine.ExecutionResolveSequence, Is.Zero);
            machine.Tick(0.02f);
            Assert.That(machine.ExecutionResolveSequence, Is.EqualTo(1));
            machine.Tick(1f);
            Assert.That(machine.ExecutionResolveSequence, Is.EqualTo(1));
            Assert.That(machine.State, Is.EqualTo(CombatState.Locomotion));
        }

        private static CombatStateMachine CreateMachine() => new CombatStateMachine(CombatTuning.CreateDefault());
    }
}
