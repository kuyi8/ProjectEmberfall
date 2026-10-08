using System;
using System.Reflection;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class PerfectGuardCounterTests
    {
        static DamageRequest FrontalHit => new DamageRequest(7,1,20f,10f,AttackTag.Light,true,true);
        static CombatStateMachine Parry()
        {
            var m=new CombatStateMachine(CombatTuning.CreateDefault());
            m.Submit(CombatCommand.GuardPressed);
            Assert.That(m.ReceiveDamage(FrontalHit).PerfectGuard,Is.True);
            return m;
        }

        [Test]
        public void Window_FromSuccessfulParry_AcceptsDirectGuardLightWithoutDamageBuff()
        {
            var m=Parry();
            Assert.That(m.GuardCounterWindowRemaining,Is.EqualTo(.6f));
            m.Tick(.599f);
            Assert.That(m.CanUseGuardCounter,Is.True);
            Assert.That(m.Submit(CombatCommand.LightAttack),Is.True);
            Assert.That(m.State,Is.EqualTo(CombatState.LightAttack1));
            Assert.That(m.CurrentAttackIsGuardCounter,Is.True);
            Assert.That(m.CurrentAttackEmpowered,Is.True);
            Assert.That(m.CurrentAttackDamage,Is.EqualTo(22f));
            Assert.That(m.CurrentAttackPostureBonus,Is.Zero);
            Assert.That(m.StateDuration,Is.EqualTo(.58f));
            Assert.That(m.GuardCounterWindowRemaining,Is.Zero);
        }

        [Test]
        public void ExactDeadline_ExpiresAndRequiresNormalGuardRelease()
        {
            var m=Parry();m.Tick(.6f);
            Assert.That(m.GuardCounterWindowRemaining,Is.Zero);
            Assert.That(m.CanUseGuardCounter,Is.False);
            Assert.That(m.Submit(CombatCommand.LightAttack),Is.False);
            m.Submit(CombatCommand.GuardReleased);
            Assert.That(m.Submit(CombatCommand.LightAttack),Is.True);
            Assert.That(m.CurrentAttackIsGuardCounter,Is.False);
            Assert.That(m.CurrentAttackEmpowered,Is.False);
        }

        [Test]
        public void ReleaseGuard_DoesNotResetWindowAndStillAllowsCounter()
        {
            var m=Parry();m.Tick(.2f);m.Submit(CombatCommand.GuardReleased);m.Tick(.2f);
            Assert.That(m.GuardCounterWindowRemaining,Is.EqualTo(.2f).Within(.00001f));
            Assert.That(m.Submit(CombatCommand.LightAttack),Is.True);
            Assert.That(m.CurrentAttackIsGuardCounter,Is.True);
        }

        [Test]
        public void DodgeDuringOpportunity_DoesNotClearOrRestartItsDeadline()
        {
            var m=Parry();m.Tick(.1f);
            Assert.That(m.Submit(CombatCommand.Dodge),Is.True);
            Assert.That(m.GuardCounterWindowRemaining,Is.EqualTo(.5f).Within(.00001f));
            m.Tick(.499f);
            Assert.That(m.GuardCounterWindowRemaining,Is.GreaterThan(0f));
            m.Tick(.002f);
            Assert.That(m.GuardCounterWindowRemaining,Is.Zero);
            Assert.That(m.CurrentAttackIsGuardCounter,Is.False);
        }

        [Test]
        public void ExpiredOpportunity_DoesNotProtectAgainstUndefendableDamage()
        {
            var m=Parry();m.Tick(.601f);
            var result=m.ReceiveDamage(new DamageRequest(8,1,20f,0f,AttackTag.Hazard,false,false));
            Assert.That(result.Accepted,Is.True);
            Assert.That(result.Defended,Is.False);
            Assert.That(m.State,Is.EqualTo(CombatState.HitReact));
            Assert.That(m.Health.Current,Is.EqualTo(100f));
            Assert.That(m.GuardCounterWindowRemaining,Is.Zero);
        }

        [Test]
        public void FailedStaminaAttempt_DoesNotSpendOpportunityOrShowActionablePrompt()
        {
            var m=Parry();m.Stamina.Drain(m.Stamina.Current);
            Assert.That(m.CanUseGuardCounter,Is.False);
            Assert.That(m.Submit(CombatCommand.LightAttack),Is.False);
            Assert.That(m.GuardCounterWindowRemaining,Is.EqualTo(.6f));
            m.Stamina.Restore(10f);
            Assert.That(m.Submit(CombatCommand.LightAttack),Is.True);
        }

        [Test]
        public void CounterOnlyLabelsOpeningLight_NotComboOrNextSwing()
        {
            var m=Parry();m.Submit(CombatCommand.LightAttack);m.Tick(.29f);
            Assert.That(m.Submit(CombatCommand.LightAttack),Is.True);
            Assert.That(m.CurrentAttackIsGuardCounter,Is.False);
            Assert.That(m.CurrentAttackEmpowered,Is.False);
            m.Tick(m.StateDuration);m.Submit(CombatCommand.LightAttack);
            Assert.That(m.CurrentAttackIsGuardCounter,Is.False);
        }

        [Test]
        public void LateOrRearDefense_DoesNotArmWindow()
        {
            var m=new CombatStateMachine(CombatTuning.CreateDefault());m.Submit(CombatCommand.GuardPressed);m.Tick(.201f);
            Assert.That(m.ReceiveDamage(FrontalHit).PerfectGuard,Is.False);
            Assert.That(m.GuardCounterWindowRemaining,Is.Zero);
            m.Reset();m.Submit(CombatCommand.GuardPressed);
            m.ReceiveDamage(new DamageRequest(7,2,20f,10f,AttackTag.Light,true,false));
            Assert.That(m.GuardCounterWindowRemaining,Is.Zero);
        }

        [Test]
        public void ExistingPerfectGuardEffects_RemainIdentical()
        {
            var m=new CombatStateMachine(CombatTuning.CreateDefault());m.Submit(CombatCommand.GuardPressed);m.Tick(.20f);
            var result=m.ReceiveDamage(FrontalHit);
            Assert.That(result.PerfectGuard,Is.True);Assert.That(result.AppliedDamage,Is.Zero);
            Assert.That(result.PostureDamageApplied,Is.EqualTo(1.5f));
            Assert.That(result.CounterPostureDamage,Is.EqualTo(60f));
        }

        [Test]
        public void PerfectDodgeAndCounter_CombineWithoutOverwritingBonuses()
        {
            var m=new CombatStateMachine(CombatTuning.CreateDefault());m.Submit(CombatCommand.Dodge);
            Assert.That(m.ReceiveDamage(FrontalHit).PerfectDodge,Is.True);
            m.Tick(m.StateDuration);m.Submit(CombatCommand.GuardPressed);m.ReceiveDamage(FrontalHit);
            m.Submit(CombatCommand.LightAttack);
            Assert.That(m.CurrentAttackIsGuardCounter,Is.True);Assert.That(m.CurrentAttackEmpowered,Is.True);
            Assert.That(m.CurrentAttackDamage,Is.EqualTo(34f));Assert.That(m.CurrentAttackPostureBonus,Is.EqualTo(18f));
            Assert.That(m.PerfectDodgeAttackReady,Is.False);
            m.Tick(m.StateDuration);m.Submit(CombatCommand.LightAttack);
            Assert.That(m.CurrentAttackEmpowered,Is.False);Assert.That(m.CurrentAttackDamage,Is.EqualTo(22f));
        }

        [Test]
        public void ExpiredCounter_DoesNotConsumePerfectDodgeBonus()
        {
            var m=new CombatStateMachine(CombatTuning.CreateDefault());m.Submit(CombatCommand.Dodge);m.ReceiveDamage(FrontalHit);
            m.Tick(m.StateDuration);m.Submit(CombatCommand.GuardPressed);m.ReceiveDamage(FrontalHit);m.Tick(.601f);
            Assert.That(m.PerfectDodgeAttackReady,Is.True);
            m.Submit(CombatCommand.GuardReleased);m.Submit(CombatCommand.LightAttack);
            Assert.That(m.CurrentAttackIsGuardCounter,Is.False);Assert.That(m.CurrentAttackDamage,Is.EqualTo(34f));
        }

        [Test]
        public void NetworkOptOut_KeepsLegacyDefenseAndGuardAttackRestrictions()
        {
            var m=new CombatStateMachine(CombatTuning.CreateDefault(),enablePerfectGuardCounter:false);
            m.Submit(CombatCommand.GuardPressed);var hit=m.ReceiveDamage(FrontalHit);
            Assert.That(hit.PerfectGuard,Is.True);Assert.That(hit.CounterPostureDamage,Is.EqualTo(60f));
            Assert.That(m.GuardCounterWindowRemaining,Is.Zero);Assert.That(m.Submit(CombatCommand.LightAttack),Is.False);
        }

        [TestCase("reset")]
        [TestCase("void")]
        [TestCase("hit")]
        [TestCase("death")]
        [TestCase("break")]
        public void InterruptedOrResetOpportunity_CannotSurvive(string action)
        {
            var m=Parry();
            switch(action)
            {
                case "reset":m.Reset();break;
                case "void":m.RecoverFromVoidFall();break;
                case "hit":m.ReceiveDamage(new DamageRequest(8,1,1f,0f,AttackTag.Hazard,false));break;
                case "death":m.ForceDeath();break;
                case "break":m.ApplyNeutralPostureDamage(100f);break;
            }
            Assert.That(m.GuardCounterWindowRemaining,Is.Zero);Assert.That(m.CanUseGuardCounter,Is.False);
        }

        [Test]
        public void AuthoredGeneratorAndDefaultRuntime_AgreeAndGenerateIdempotently()
        {
            var tuning=ScriptableObject.CreateInstance<CombatTuningAsset>();
            try
            {
                var configure=typeof(M1ProjectSetup).GetMethod("ConfigureGuardAndPostureTuning",BindingFlags.Static|BindingFlags.NonPublic);
                configure.Invoke(null,new object[]{tuning});string first=EditorJsonUtility.ToJson(tuning);
                configure.Invoke(null,new object[]{tuning});Assert.That(EditorJsonUtility.ToJson(tuning),Is.EqualTo(first));
                Assert.That(tuning.CreateRuntimeCopy().PerfectGuardCounterWindow,Is.EqualTo(.6f));
                Assert.That(CombatTuning.CreateDefault().PerfectGuardCounterWindow,Is.EqualTo(.6f));
                Assert.That(AssetDatabase.LoadAssetAtPath<CombatTuningAsset>("Assets/_Game/Settings/CombatTuning_M1.asset")
                    .CreateRuntimeCopy().PerfectGuardCounterWindow,Is.EqualTo(.6f));
            }
            finally { UnityEngine.Object.DestroyImmediate(tuning); }
        }

        [TestCase(0f)] [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void WindowValidation_RejectsInvalidAuthoredDuration(float duration)
        {
            var tuning=ScriptableObject.CreateInstance<CombatTuningAsset>();
            try
            {
                typeof(CombatTuningAsset).GetField("_perfectGuardCounterWindow",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(tuning,duration);
                Assert.Throws<ArgumentOutOfRangeException>(()=>tuning.CreateRuntimeCopy());
            }
            finally { UnityEngine.Object.DestroyImmediate(tuning); }
        }
    }
}
