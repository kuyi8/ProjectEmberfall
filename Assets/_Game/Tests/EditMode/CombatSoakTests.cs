using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class CombatSoakTests
    {
        [Test]
        public void CombatStateMachine_TenMinuteAcceleratedSoak_DoesNotStickOrCorruptResources()
        {
            CombatStateMachine machine = new CombatStateMachine(CombatTuning.CreateDefault());
            const float step = 1f / 60f;
            const int steps = 10 * 60 * 60;
            int actionIndex = 0;
            int hazardSequence = 0;
            float nextHazardAt = 3f;
            float elapsed = 0f;

            for (int i = 0; i < steps; i++)
            {
                if (machine.State == CombatState.Dead)
                {
                    machine.Reset();
                }
                else if (machine.State == CombatState.Locomotion)
                {
                    switch (actionIndex++ % 3)
                    {
                        case 0:
                            machine.Submit(CombatCommand.LightAttack);
                            break;
                        case 1:
                            machine.Submit(CombatCommand.Dodge);
                            break;
                        default:
                            machine.Submit(CombatCommand.HeavyPressed);
                            break;
                    }
                }
                else if (machine.State == CombatState.HeavyCharge && machine.StateElapsed >= 0.56f)
                {
                    machine.Submit(CombatCommand.HeavyReleased);
                }

                if (elapsed >= nextHazardAt)
                {
                    machine.ReceiveDamage(new DamageRequest(99, ++hazardSequence, 18f, 5f, AttackTag.Hazard));
                    nextHazardAt += 3f;
                }

                machine.Tick(step);
                elapsed += step;

                Assert.That(machine.Stamina.Current, Is.InRange(0f, machine.Stamina.Maximum));
                Assert.That(machine.Health.Current, Is.InRange(0f, machine.Health.Maximum));
                Assert.That(machine.StateElapsed, Is.LessThan(3f), $"State {machine.State} appears stuck at step {i}.");
            }
        }
    }
}
