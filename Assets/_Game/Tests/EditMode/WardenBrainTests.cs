using Emberfall.AI.Domain;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class WardenBrainTests
    {
        [Test]
        public void PhaseOne_SelectsChargeAtRange_AndCommitsDirectionWindow()
        {
            WardenBrain brain = CreateBrain();
            brain.Tick(0.01f, Perception(6f), true);
            brain.Tick(0.01f, Perception(6f), true);

            Assert.That(brain.State, Is.EqualTo(WardenState.Windup));
            Assert.That(brain.CurrentAttack, Is.EqualTo(WardenAttackKind.Charge));

            brain.Tick(0.91f, Perception(6f), true);
            Assert.That(brain.State, Is.EqualTo(WardenState.Attack));
            brain.Tick(0.1f, Perception(6f), true);
            Assert.That(brain.IsDamageWindowOpen, Is.True);
        }

        [Test]
        public void SwordCombo_ExposesTwoIndependentDamageWindows()
        {
            WardenBrain brain = CreateBrain();
            brain.Tick(0.01f, Perception(2f), true);
            brain.Tick(0.01f, Perception(2f), true);
            Assert.That(brain.CurrentAttack, Is.EqualTo(WardenAttackKind.SwordCombo));
            brain.Tick(0.73f, Perception(2f), true);

            brain.Tick(0.18f, Perception(2f), true);
            Assert.That(brain.CurrentHitIndex, Is.EqualTo(1));
            brain.Tick(0.22f, Perception(2f), true);
            Assert.That(brain.CurrentHitIndex, Is.EqualTo(0));
            brain.Tick(0.2f, Perception(2f), true);
            Assert.That(brain.CurrentHitIndex, Is.EqualTo(2));
        }

        [Test]
        public void OrdinaryHealthHit_DoesNotInterruptActiveAttack()
        {
            WardenBrain brain = CreateBrain();
            brain.Tick(0.01f, Perception(2f), true);
            brain.Tick(0.01f, Perception(2f), true);
            brain.Tick(0.73f, Perception(2f), true);

            DamageResult result = brain.ReceiveDamage(Hit(15f, 0f), false);

            Assert.That(result.Accepted, Is.True);
            Assert.That(brain.State, Is.EqualTo(WardenState.Attack));
        }

        [Test]
        public void FrontalPostureBreak_OpensGuardBreakWithoutHealthDamage()
        {
            WardenBrain brain = CreateBrain();
            brain.Tick(0.01f, Perception(2f), true);

            DamageResult result = brain.ReceiveDamage(Hit(50f, 130f), true);

            Assert.That(result.Blocked, Is.True);
            Assert.That(result.GuardBroken, Is.True);
            Assert.That(brain.Health.Current, Is.EqualTo(brain.Health.Maximum));
            Assert.That(brain.State, Is.EqualTo(WardenState.GuardBreak));
        }

        [Test]
        public void ThresholdHit_EntersInvulnerableTransition_ThenPhaseTwo()
        {
            WardenBrain brain = CreateBrain();
            brain.Tick(0.01f, Perception(2f), true);
            brain.Tick(0.01f, Perception(2f), true);
            brain.Tick(0.73f, Perception(2f), true);
            brain.Tick(0.93f, Perception(2f), true);
            Assert.That(brain.State, Is.EqualTo(WardenState.Recovery));

            float before = brain.Health.Current;
            DamageResult opening = brain.ReceiveDamage(Hit(100f, 0f), true);
            Assert.That(opening.AppliedDamage, Is.EqualTo(128f).Within(0.01f));
            Assert.That(brain.Health.Current, Is.EqualTo(before - 128f).Within(0.01f));

            DamageResult thresholdHit = brain.ReceiveDamage(Hit(230f, 0f), false);
            Assert.That(brain.PhaseTwoThresholdReached, Is.True);
            Assert.That(brain.Phase, Is.EqualTo(WardenPhase.Transition));
            Assert.That(brain.State, Is.EqualTo(WardenState.PhaseTransition));
            Assert.That(brain.Health.Normalized, Is.EqualTo(0.55f).Within(0.001f));
            Assert.That(thresholdHit.Killed, Is.False);
            Assert.That(brain.ReceiveDamage(Hit(999f, 100f), false).Accepted, Is.False);

            brain.Tick(4.21f, Perception(2f), true);
            Assert.That(brain.Phase, Is.EqualTo(WardenPhase.PhaseTwo));
            Assert.That(brain.State, Is.EqualTo(WardenState.Chase));
            Assert.That(brain.GuardNormalized, Is.EqualTo(1f));
        }

        [Test]
        public void PhaseTwo_CloseRangeSelectsRuneCleave_AndFrontHitsDamageHealthAndPosture()
        {
            WardenBrain brain = CreateBrain();
            EnterPhaseTwo(brain, 2f);
            brain.Tick(0.01f, Perception(2f), true);

            Assert.That(brain.CurrentAttack, Is.EqualTo(WardenAttackKind.RuneCleave));
            Assert.That(brain.State, Is.EqualTo(WardenState.Windup));
            float healthBefore = brain.Health.Current;
            float postureBefore = brain.GuardCurrent;
            DamageResult result = brain.ReceiveDamage(Hit(25f, 30f), true);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Blocked, Is.False);
            Assert.That(brain.Health.Current, Is.LessThan(healthBefore));
            Assert.That(brain.GuardCurrent, Is.EqualTo(postureBefore - 30f));
        }

        [Test]
        public void PhaseTwo_MidRangeSelectsDelayedBlastReleaseWindow()
        {
            WardenBrain brain = CreateBrain();
            EnterPhaseTwo(brain, 6f);
            brain.Tick(0.01f, Perception(6f), true);

            Assert.That(brain.CurrentAttack, Is.EqualTo(WardenAttackKind.DelayedBlast));
            brain.Tick(0.83f, Perception(6f), true);
            Assert.That(brain.State, Is.EqualTo(WardenState.Attack));
            brain.Tick(0.18f, Perception(6f), true);
            Assert.That(brain.IsDamageWindowOpen, Is.True);
        }

        [Test]
        public void DisabledEncounter_ResetsAuthorityToDormant()
        {
            WardenBrain brain = CreateBrain();
            brain.Tick(0.01f, Perception(2f), true);
            Assert.That(brain.EncounterActive, Is.True);

            brain.Tick(0.01f, Perception(2f), false);

            Assert.That(brain.State, Is.EqualTo(WardenState.Dormant));
            Assert.That(brain.EncounterActive, Is.False);
        }

        [Test]
        public void TwoPlayerHealthMultiplier_ScalesMaximumAndPreservesPhaseGateRatio()
        {
            WardenBrain brain = CreateBrain(1.65f);
            Assert.That(brain.Health.Maximum, Is.EqualTo(1023f).Within(0.01f));
            brain.Tick(0.01f, Perception(2f), true);
            brain.ReceiveDamage(Hit(5000f, 0f), false);
            Assert.That(brain.Phase, Is.EqualTo(WardenPhase.Transition));
            Assert.That(brain.Health.Normalized, Is.EqualTo(0.55f).Within(0.001f));
        }

        private static WardenBrain CreateBrain(float healthMultiplier = 1f) => new WardenBrain(new WardenDefinition(
            new ContentId("boss:test-warden"), new ContentId("text:test-warden"),
            620f, 7f, 10.5f, 18f, 15f, 2.15f, 300f,
            125f, 145f, 2.5f, 1.55f, 1.28f, 0.55f,
            4.2f, 3f, 1.12f, 105f, 110f, 1.15f, 2.55f, 7.4f, 5.2f,
            new WardenAttackDefinition(WardenAttackKind.SwordCombo, 0f, 2.15f, 0.72f, 0.92f, 0.95f,
                18f, 18f, 1.08f, 0.16f, 0.31f, 0.58f, 0.76f),
            new WardenAttackDefinition(WardenAttackKind.ShieldBash, 0f, 1.65f, 0.42f, 0.38f, 0.78f,
                15f, 42f, 0.92f, 0.1f, 0.25f, cooldown: 3.6f),
            new WardenAttackDefinition(WardenAttackKind.Charge, 3f, 8.5f, 0.9f, 0.76f, 1.25f,
                28f, 34f, 1.18f, 0.08f, 0.68f, cooldown: 5.5f),
            new WardenAttackDefinition(WardenAttackKind.RuneCleave, 0f, 3.1f, 0.95f, 0.52f, 1.05f,
                27f, 28f, 2.55f, 0.15f, 0.36f, cooldown: 4f),
            new WardenAttackDefinition(WardenAttackKind.DelayedBlast, 1.5f, 9f, 0.82f, 0.42f, 1.15f,
                32f, 35f, 1f, 0.16f, 0.26f, cooldown: 5.4f)), healthMultiplier);

        private static void EnterPhaseTwo(WardenBrain brain, float distance)
        {
            brain.Tick(0.01f, Perception(distance), true);
            DamageResult gate = brain.ReceiveDamage(Hit(999f, 0f), false);
            Assert.That(gate.Killed, Is.False);
            Assert.That(brain.State, Is.EqualTo(WardenState.PhaseTransition));
            brain.Tick(4.21f, Perception(distance), true);
            Assert.That(brain.Phase, Is.EqualTo(WardenPhase.PhaseTwo));
        }

        private static MeleeEnemyPerception Perception(float distance) =>
            new MeleeEnemyPerception(true, true, distance, 0f);

        private static DamageRequest Hit(float damage, float posture) =>
            new DamageRequest(1, 1, damage, posture, AttackTag.Heavy);
    }
}
