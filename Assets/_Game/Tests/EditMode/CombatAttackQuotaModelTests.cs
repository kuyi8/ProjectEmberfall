using Emberfall.AI.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class CombatAttackQuotaModelTests
    {
        [Test]
        public void OneSlot_RotatesAfterCommittedSequenceCompletes()
        {
            var model = new CombatAttackQuotaModel(1);
            model.Register(11);
            model.Register(22);
            model.UpdateMember(11, true, false);
            model.UpdateMember(22, true, false);

            model.Resolve();
            Assert.That(model.IsAttackAllowed(11), Is.True);
            Assert.That(model.IsAttackAllowed(22), Is.False);

            model.UpdateMember(11, true, true);
            model.Resolve();
            model.UpdateMember(11, true, false);
            model.Resolve();

            Assert.That(model.IsAttackAllowed(11), Is.False);
            Assert.That(model.IsAttackAllowed(22), Is.True);
            Assert.That(model.GrantedCount, Is.EqualTo(1));
        }

        [Test]
        public void IneligibleHolder_ReleasesSlotWithoutWaitingForCommitment()
        {
            var model = new CombatAttackQuotaModel(1);
            model.Register(11);
            model.Register(22);
            model.UpdateMember(11, true, false);
            model.UpdateMember(22, true, false);
            model.Resolve();

            model.UpdateMember(11, false, false);
            model.Resolve();

            Assert.That(model.IsAttackAllowed(11), Is.False);
            Assert.That(model.IsAttackAllowed(22), Is.True);
        }

        [Test]
        public void ConfiguredCapacity_IsNeverExceeded()
        {
            var model = new CombatAttackQuotaModel(2);
            for (int id = 1; id <= 4; id++)
            {
                model.Register(id);
                model.UpdateMember(id, true, false);
            }

            model.Resolve();
            Assert.That(model.GrantedCount, Is.EqualTo(2));
        }

        [Test]
        public void SupportSide_RemainsStableAcrossGrantRotationAndReset()
        {
            var model = new CombatAttackQuotaModel(1);
            model.Register(11);
            model.Register(22);

            Assert.That(model.GetSupportSide(11), Is.EqualTo(-1));
            Assert.That(model.GetSupportSide(22), Is.EqualTo(1));

            model.UpdateMember(11, true, false);
            model.UpdateMember(22, true, false);
            model.Resolve();
            model.UpdateMember(11, true, true);
            model.Resolve();
            model.UpdateMember(11, true, false);
            model.Resolve();
            model.Reset();

            Assert.That(model.GetSupportSide(11), Is.EqualTo(-1));
            Assert.That(model.GetSupportSide(22), Is.EqualTo(1));
        }
    }
}
