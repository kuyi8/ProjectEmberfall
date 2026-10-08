using System;
using Emberfall.AI.Domain;
using Emberfall.UI;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class WardenHudPresentationTests
    {
        [TestCase(WardenAttackKind.SwordCombo, "二连斩")]
        [TestCase(WardenAttackKind.ShieldBash, "盾击")]
        [TestCase(WardenAttackKind.Charge, "直线冲锋")]
        [TestCase(WardenAttackKind.RuneCleave, "符文劈斩")]
        [TestCase(WardenAttackKind.DelayedBlast, "延迟爆破")]
        public void EachKnownAttackHasItsOwnNameAcrossAllThreeCommittedStates(WardenAttackKind attack, string name)
        {
            foreach (var state in new[] { WardenState.Windup, WardenState.Attack, WardenState.Recovery })
            {
                Assert.That(WardenHudPresentation.CommittedAttackName(state, attack), Is.EqualTo(name));
                Assert.That(WardenHudPresentation.AppendCommittedAttack("状态", state, attack), Is.EqualTo("状态 · " + name));
                Assert.That(NetworkRouteHud.BuildBossStatus("阶段", "状态", state, attack),
                    Is.EqualTo("阶段 · 状态 · " + name), "The actual HUD adapter appends the identity exactly once.");
            }
        }

        [TestCase(WardenState.Dormant)] [TestCase(WardenState.Chase)]
        [TestCase(WardenState.GuardBreak)] [TestCase(WardenState.Return)]
        [TestCase(WardenState.Dead)] [TestCase(WardenState.PhaseTransition)]
        [TestCase((WardenState)(-1))] [TestCase((WardenState)99)]
        public void NonAttackAndUnknownStatesKeepTheirStatusWithoutAnyStaleAttack(WardenState state)
        {
            foreach (WardenAttackKind attack in Enum.GetValues(typeof(WardenAttackKind)))
            {
                Assert.That(WardenHudPresentation.CommittedAttackName(state, attack), Is.Empty);
                Assert.That(WardenHudPresentation.AppendCommittedAttack("当前状态", state, attack), Is.EqualTo("当前状态"));
                Assert.That(NetworkRouteHud.BuildBossStatus("阶段", "当前状态", state, attack), Is.EqualTo("阶段 · 当前状态"));
            }
        }

        [TestCase((WardenAttackKind)(-1))] [TestCase((WardenAttackKind)99)]
        public void UnknownAttackNeverBecomesSwordComboOrReusesThePreviousLabel(WardenAttackKind unknown)
        {
            Assert.That(WardenHudPresentation.AttackName(unknown), Is.Empty);
            foreach (var state in new[] { WardenState.Windup, WardenState.Attack, WardenState.Recovery })
            {
                Assert.That(WardenHudPresentation.CommittedAttackName(state, WardenAttackKind.Charge), Is.EqualTo("直线冲锋"));
                Assert.That(WardenHudPresentation.AppendCommittedAttack("当前状态", state, unknown), Is.EqualTo("当前状态"));
            }
        }

        [Test]
        public void InterruptedAttackIsClearedImmediatelyAndTheNextIdentityIsNotLatched()
        {
            foreach (var exit in new[] { WardenState.PhaseTransition, WardenState.GuardBreak, WardenState.Dead, WardenState.Dormant })
            {
                Assert.That(WardenHudPresentation.CommittedAttackName(WardenState.Windup, WardenAttackKind.Charge), Is.EqualTo("直线冲锋"));
                Assert.That(WardenHudPresentation.CommittedAttackName(exit, WardenAttackKind.Charge), Is.Empty);
                Assert.That(WardenHudPresentation.CommittedAttackName(WardenState.Windup, WardenAttackKind.DelayedBlast), Is.EqualTo("延迟爆破"));
            }
        }
    }
}
