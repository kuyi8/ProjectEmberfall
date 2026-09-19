using Emberfall.Quests.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class ForestSealTemplateStateTests
    {
        [Test]
        public void Encounter_RequiresBothEnemiesInOneAttempt()
        {
            var state = new ForestSealTemplateState();

            Assert.That(state.RegisterBearerDefeat(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(ForestSealPhase.Encounter));
            Assert.That(state.ResetEncounterAttempt(), Is.True);
            Assert.That(state.RegisterPriestDefeat(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(ForestSealPhase.Encounter));
            Assert.That(state.RegisterBearerDefeat(), Is.True);

            Assert.That(state.Phase, Is.EqualTo(ForestSealPhase.SigilAvailable));
        }

        [Test]
        public void RewardChoice_IsExclusiveAndRoundTrips()
        {
            var state = new ForestSealTemplateState();
            state.RegisterBearerDefeat();
            state.RegisterPriestDefeat();

            Assert.That(state.TryClaimSigil(), Is.True);
            Assert.That(state.CanActivateSeal, Is.True);
            Assert.That(state.TryActivateSeal(), Is.True);
            Assert.That(state.TryChooseRune(ForestRuneChoice.Guard), Is.True);
            Assert.That(state.TryChooseRune(ForestRuneChoice.Ember), Is.False);
            Assert.That(state.TryClaimSupply(), Is.True);
            Assert.That(state.TryClaimSupply(), Is.False);

            ForestSealTemplateState restored = ForestSealTemplateState.Restore(state.CaptureSnapshot());
            Assert.That(restored.Phase, Is.EqualTo(ForestSealPhase.Completed));
            Assert.That(restored.RuneChoice, Is.EqualTo(ForestRuneChoice.Guard));
            Assert.That(restored.SupplyClaimed, Is.True);
        }
    }
}
