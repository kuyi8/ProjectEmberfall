using System;
using Emberfall.Quests.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class SealConditionStateTests
    {
        [Test]
        public void BridgeMechanisms_RequireEncounterForBAndRoundTrip()
        {
            var state = new SealConditionState();
            Assert.That(state.TryActivateBridgeMechanismA(), Is.True);
            Assert.That(state.TryActivateBridgeMechanismB(), Is.False);
            Assert.That(state.TryRecordBridgeEncounterCleared(), Is.True);
            Assert.That(state.TryActivateBridgeMechanismB(), Is.True);
            Assert.That(state.CanActivateBridgeSeal, Is.True);

            SealConditionState restored = SealConditionState.Restore(state.CaptureSnapshot());
            Assert.That(restored.BridgeEncounterCleared, Is.True);
            Assert.That(restored.CanActivateBridgeSeal, Is.True);
        }

        [Test]
        public void CourtyardGuardBreak_IsUniqueAndPersisted()
        {
            var state = new SealConditionState();
            Assert.That(state.TryRecordCourtyardGuardBroken(), Is.True);
            Assert.That(state.TryRecordCourtyardGuardBroken(), Is.False);
            Assert.That(
                SealConditionState.Restore(state.CaptureSnapshot()).CourtyardGuardBroken,
                Is.True);
        }

        [Test]
        public void Snapshot_RejectsMechanismBWithoutClearedEncounter()
        {
            Assert.Throws<FormatException>(() =>
                new SealConditionSnapshot(false, false, true, false));
        }
    }
}
