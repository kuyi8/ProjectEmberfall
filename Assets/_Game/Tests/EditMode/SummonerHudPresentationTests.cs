using Emberfall.AI.Domain;
using Emberfall.UI;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class SummonerHudPresentationTests
    {
        [TestCase(RangedEnemyState.Windup, SummonerAttackKind.Summon, 0, "text:summoner.windup", true)]
        [TestCase(RangedEnemyState.Windup, SummonerAttackKind.Projectile, 0, "text:summoner.projectile-ready", false)]
        [TestCase(RangedEnemyState.Windup, SummonerAttackKind.Projectile, 2, "text:summoner.projectile", false)]
        [TestCase(RangedEnemyState.Release, SummonerAttackKind.Summon, 1, "text:summoner.committed", false)]
        [TestCase(RangedEnemyState.Release, SummonerAttackKind.Projectile, 1, "text:summoner.projectile-released", false)]
        public void NewActualAttackAlwaysOutranksOldInterruptReceipt(RangedEnemyState state,
            SummonerAttackKind kind, int living, string id, bool progress)
        {
            foreach (bool selected in new[] { false, true })
            foreach (bool recent in new[] { false, true })
            {
                var view = SummonerHudPresentation.Resolve(state, kind, living, recent, selected);
                Assert.That(view.TextId, Is.EqualTo(id));
                Assert.That(view.ShowCastProgress, Is.EqualTo(progress));
                Assert.That(view.Emphasized, Is.True);
            }
        }

        [TestCase(RangedEnemyState.HitReact)]
        [TestCase(RangedEnemyState.Recovery)]
        [TestCase(RangedEnemyState.Retreat)]
        public void ValidReceiptUsesGoldOnlyWhileNoNewAttackIsCommitting(RangedEnemyState state)
        {
            var view = SummonerHudPresentation.Resolve(state, SummonerAttackKind.Summon, 1, true, true);
            Assert.That(view.TextId, Is.EqualTo("text:summoner.interrupted"));
            Assert.That(view.Accent, Is.EqualTo(EmberfallGuiTheme.Execution));
            Assert.That(view.ShowCastProgress, Is.False);
        }

        [Test]
        public void CastSelectionAndInterruptHaveDistinctNonGroundAccents()
        {
            var cast = SummonerHudPresentation.Resolve(RangedEnemyState.Windup, SummonerAttackKind.Summon, 0, false, true);
            var selected = SummonerHudPresentation.Resolve(RangedEnemyState.Idle, SummonerAttackKind.Projectile, 0, false, true);
            var interrupt = SummonerHudPresentation.Resolve(RangedEnemyState.HitReact, SummonerAttackKind.Summon, 0, true, true);
            Assert.That(cast.Accent, Is.Not.EqualTo(selected.Accent));
            Assert.That(cast.Accent, Is.Not.EqualTo(interrupt.Accent));
            Assert.That(selected.Accent, Is.Not.EqualTo(interrupt.Accent));
            Assert.That(selected.Accent, Is.EqualTo(EmberfallGuiTheme.Interaction));
            Assert.That(cast.Accent, Is.Not.EqualTo(EmberfallGuiTheme.Danger));
        }

        [Test]
        public void IdleAndDeathDoNotInventSuccessfulInterrupts()
        {
            var idle = SummonerHudPresentation.Resolve(RangedEnemyState.Idle, SummonerAttackKind.Projectile, 0, false, false);
            Assert.That(idle.TextId, Is.Null); Assert.That(idle.Emphasized, Is.False);
            var dead = SummonerHudPresentation.Resolve(RangedEnemyState.Dead, SummonerAttackKind.Summon, 0, true, true);
            Assert.That(dead.TextId, Is.Null); Assert.That(dead.Emphasized, Is.False);
        }
    }
}
