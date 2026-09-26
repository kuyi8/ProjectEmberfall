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
