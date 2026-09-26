using System.Collections;
using System.Reflection;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class SweepPresentationTests
    {
        [UnityTest]
        public IEnumerator RealPresenterCompletesSingleClipInsideUnchangedDomainDuration()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            var actor = Object.FindObjectOfType<PlayerCombatActor>();
            var presenter = actor.GetComponent<PlayerAnimationPresenter>();
            var animator = actor.GetComponentInChildren<Animator>(true);
            actor.enabled = false;
            presenter.enabled = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            var model = actor.Model;
            Assert.That(model.State, Is.EqualTo(CombatState.Locomotion));
            int sequence = model.AttackSequence;
            Assert.That(model.Submit(CombatCommand.Sweep), Is.True);
            Assert.That(model.StateDuration, Is.EqualTo(.82f).Within(.0001f));
            Assert.That(model.CurrentAttackDamage, Is.EqualTo(44));
            var update = typeof(PlayerAnimationPresenter).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
            update.Invoke(presenter, null);
            animator.Update(0);
            Assert.That(animator.GetNextAnimatorStateInfo(0).IsName("Sweep"), Is.True);
            Assert.That(animator.speed, Is.EqualTo((19f / 30f) / .82f).Within(.0001f));
            // Step the real Presenter/Animator in isolation: not a natural input/contact acceptance test.
            int openEdges = 0;
            bool wasOpen = false;
            for (int frame = 1; frame <= 82; frame++)
            {
                animator.Update(.01f);
                if (frame == 82)
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(1).Within(.003f));
                model.Tick(.01f);
                if (model.IsDamageWindowOpen && !wasOpen) openEdges++;
                wasOpen = model.IsDamageWindowOpen;
            }
            model.Tick(.0001f); // Float summation tolerance at the .82 boundary.
            Assert.That(openEdges, Is.EqualTo(1));
            Assert.That(model.AttackSequence, Is.EqualTo(sequence + 1));
            Assert.That(model.State, Is.EqualTo(CombatState.Locomotion));
            update.Invoke(presenter, null);
            animator.Update(.12f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), Is.True);
            Assert.That(animator.applyRootMotion, Is.False);
            Debug.Log("[SWEEP_PRESENTATION] realPresenter duration=.82 complete=true damageWindows=1 attackSequences=1 controlled=true");
        }
    }
}
