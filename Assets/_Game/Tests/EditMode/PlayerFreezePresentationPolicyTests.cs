using System;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class PlayerFreezePresentationPolicyTests
    {
        [Test]
        public void OnlyDodgeSuppressesOwnerRequest_AttacksExecutionAndHitReactKeepTheirFeedback()
        {
            bool before=PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving;
            try
            {
                PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving=true;
                foreach(CombatState state in Enum.GetValues(typeof(CombatState)))
                    Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(state),Is.EqualTo(state!=CombatState.Dodge),state.ToString());
                Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(null),Is.True,"Network/no-actor adapter unchanged.");
                Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Guard,CombatState.Guard),Is.True);
                Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Dodge,CombatState.Dodge),Is.False);
                Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Dodge,CombatState.Locomotion),Is.False,
                    "Dodge confirmation kind must not turn into a late freeze after the state boundary.");
            }
            finally{PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving=before;}
        }

        [Test]
        public void DeveloperComparison_RestoresLegacyPresentation_WithoutAnyMotorOption()
        {
            bool before=PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving;
            try
            {
                PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving=false;
                Assert.That(PlayerFreezePresentationPolicy.AllowOwnerFreeze(CombatState.Dodge),Is.True);
                Assert.That(PlayerFreezePresentationPolicy.AllowPerfectDefenseFreeze(PerfectDefenseKind.Dodge,CombatState.Dodge),Is.True);
            }
            finally{PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving=before;}
        }
    }
}
