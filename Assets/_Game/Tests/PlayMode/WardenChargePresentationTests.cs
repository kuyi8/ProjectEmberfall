using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Animation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class WardenChargePresentationTests
    {
        [UnityTest]
        public IEnumerator NormalizedOffsetProbeAcrossSpeedsAndFreeze()
        {
            yield return LoadFixture();
            var actor = UnityEngine.Object.FindObjectsOfType<WardenActor>(true).Single();
            var animator = Prepare(actor);
            float length = ReadLength(actor);
            float travelSpeed = length * .54f / actor.Definition.Charge.AttackDuration;
            float recoverySpeed = length * .22f / actor.Definition.Charge.RecoveryDuration;
            foreach (bool recovery in new[] { false, true })
            foreach (float speed in new[] { 0f, travelSpeed, recoverySpeed })
            {
                string state = recovery ? "WardenChargeRecovery" : "WardenCharge";
                float fraction = recovery ? .78f : .24f;
                animator.Rebind();
                AnimatorSpeedCoordinator.SetBase(animator, speed);
                animator.Play("WardenChargeWindup", 0, 0);
                animator.Update(0);
                animator.CrossFade(state, (recovery ? .12f : .06f) / length, 0, fraction);
                animator.Update(0);
                var next = animator.GetNextAnimatorStateInfo(0);
                Debug.Log(FormattableString.Invariant($"[WARDEN_NORMALIZED_PROBE] phase={state} speed={speed:R} normalized={next.normalizedTime:R} target={fraction:R} next={next.IsName(state)}"));
                Assert.That(next.IsName(state), Is.True);
                Assert.That(next.normalizedTime, Is.EqualTo(fraction).Within(.0001f));
                if (speed == 0)
                {
                    animator.Update(.1f);
                    Assert.That(animator.GetNextAnimatorStateInfo(0).normalizedTime, Is.EqualTo(fraction).Within(.0001f));
                    AnimatorSpeedCoordinator.SetBase(animator, recovery ? recoverySpeed : travelSpeed);
                    animator.Update(.001f);
                    Assert.That(animator.GetNextAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(fraction));
                }
            }
        }

        [UnityTest]
        public IEnumerator NormalizedTransitionInterruptionPreservesDestinationOffset()
        {
            yield return LoadFixture();
            var actor = UnityEngine.Object.FindObjectsOfType<WardenActor>(true).Single();
            var animator = Prepare(actor);
            float length = ReadLength(actor);
            AnimatorSpeedCoordinator.SetBase(animator, length * .54f / actor.Definition.Charge.AttackDuration);
            animator.Play("WardenChargeWindup", 0, .2f);
            animator.Update(0);
            animator.CrossFade("WardenCharge", .06f / length, 0, .24f);
            animator.Update(.005f);
            Assert.That(animator.IsInTransition(0), Is.True);
            AnimatorSpeedCoordinator.SetBase(animator, length * .22f / actor.Definition.Charge.RecoveryDuration);
            animator.CrossFade("WardenChargeRecovery", .12f / length, 0, .78f);
            animator.Update(0);
            var next = animator.GetNextAnimatorStateInfo(0);
            Debug.Log(FormattableString.Invariant($"[WARDEN_NORMALIZED_INTERRUPT] next={next.IsName("WardenChargeRecovery")} normalized={next.normalizedTime:R}"));
            Assert.That(next.IsName("WardenChargeRecovery"), Is.True);
            Assert.That(next.normalizedTime, Is.EqualTo(.78f).Within(.0001f));
        }

        [UnityTest]
        public IEnumerator CompareFixedAndNormalizedBlendDurations()
        {
            yield return LoadFixture();
            var actor = UnityEngine.Object.FindObjectsOfType<WardenActor>(true).Single();
            var animator = Prepare(actor);
            float length = ReadLength(actor);
            foreach (bool recovery in new[] { false, true })
            foreach (bool normalized in new[] { false, true })
            {
                float duration = recovery ? .12f : .06f;
                float speed = length * (recovery ? .22f : .54f) /
                    (recovery ? actor.Definition.Charge.RecoveryDuration : actor.Definition.Charge.AttackDuration);
                string source = recovery ? "WardenCharge" : "WardenChargeWindup";
                string target = recovery ? "WardenChargeRecovery" : "WardenCharge";
                animator.Rebind();
                AnimatorSpeedCoordinator.SetBase(animator, speed);
                animator.Play(source, 0, .3f);
                animator.Update(0);
                if (normalized) animator.CrossFade(target, duration / length, 0, recovery ? .78f : .24f);
                else animator.CrossFadeInFixedTime(target, duration, 0, recovery ? .78f : .24f);
                animator.Update(0);
                var transition = animator.GetAnimatorTransitionInfo(0);
                int ticks = 0;
                while (animator.IsInTransition(0) && ticks < 2000) { animator.Update(.001f); ticks++; }
                Debug.Log(FormattableString.Invariant($"[WARDEN_BLEND_PROBE] phase={target} api={(normalized ? "normalized" : "fixed")} speed={speed:R} parameter={(normalized ? duration/length : duration):R} transitionDuration={transition.duration:R} unit={transition.durationUnit} elapsed={ticks*.001f:R}"));
                Assert.That(ticks, Is.GreaterThan(0).And.LessThan(2000));
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(target), Is.True);
            }
        }

        private static IEnumerator LoadFixture()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
        }

        private static Animator Prepare(WardenActor actor)
        {
            actor.enabled = false;
            actor.GetComponent<WardenAnimationPresenter>().enabled = false;
            var animator = actor.GetComponentInChildren<Animator>(true);
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        private static float ReadLength(WardenActor actor)
        {
            var set = (PlayerAnimationSet)typeof(WardenAnimationPresenter)
                .GetField("_animationSet", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(actor.GetComponent<WardenAnimationPresenter>());
            return set.GetEnemyClip(EnemyAnimationAction.WardenCharge).length;
        }

        // Engine behaviour probe, NOT an assertion that current production phases are aligned.
        [UnityTest]
        public IEnumerator FixedTimeOffsetProbeReportsSpeedScaledEntry()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            var actor = UnityEngine.Object.FindObjectsOfType<WardenActor>(true).Single();
            var animator = actor.GetComponentInChildren<Animator>(true);
            actor.enabled = false;
            actor.GetComponent<WardenAnimationPresenter>().enabled = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var set = (PlayerAnimationSet)typeof(WardenAnimationPresenter)
                .GetField("_animationSet", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(actor.GetComponent<WardenAnimationPresenter>());
            float length = set.GetEnemyClip(EnemyAnimationAction.WardenCharge).length;
            foreach (bool recovery in new[] { false, true })
            {
                string state = recovery ? "WardenChargeRecovery" : "WardenCharge";
                float fraction = recovery ? .78f : .24f;
                float speed = length * (recovery ? .22f : .54f) /
                    (recovery ? actor.Definition.Charge.RecoveryDuration : actor.Definition.Charge.AttackDuration);
                foreach (string variant in new[] { "literal", "length-only", "speed-compensated" })
                {
                    animator.Rebind();
                    AnimatorSpeedCoordinator.SetBase(animator, speed);
                    animator.Play("Locomotion", 0, 0);
                    animator.Update(0);
                    float offset = variant == "literal" ? fraction : fraction * length;
                    if (variant == "speed-compensated") offset /= speed;
                    animator.CrossFadeInFixedTime(state, recovery ? .12f : .06f, 0, offset);
                    animator.Update(0);
                    var next = animator.GetNextAnimatorStateInfo(0);
                    Assert.That(next.IsName(state), Is.True);
                    Debug.Log(FormattableString.Invariant($"[WARDEN_OFFSET_PROBE] phase={state} variant={variant} length={length:R} speed={speed:R} argumentSeconds={offset:R} normalized={next.normalizedTime:R} target={fraction:R}"));
                    Assert.That(next.normalizedTime, Is.EqualTo(offset * speed / length).Within(.0001f),
                        "Keep this as a measured Unity 2022.3 behaviour diagnostic, not phase acceptance.");
                    if (variant == "speed-compensated")
                        Assert.That(next.normalizedTime, Is.EqualTo(fraction).Within(.0001f));
                }
            }
        }
    }
}
