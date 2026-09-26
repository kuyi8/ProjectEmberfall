using System.Collections;
using System.Linq;
using System.Reflection;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Animation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Emberfall.Tests.PlayMode
{
    public sealed class PriestProjectilePresentationTests
    {
        [UnityTest]
        public IEnumerator CroppedClipsShareActualHumanoidBoundaryPose()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null;
            var actor = Object.FindObjectsOfType<RangedEnemyActor>(true).First(a => a.isActiveAndEnabled);
            var presenter = actor.GetComponent<RangedEnemyAnimationPresenter>();
            var animator = actor.GetComponentInChildren<Animator>(true);
            var set = (PlayerAnimationSet)typeof(RangedEnemyAnimationPresenter)
                .GetField("_animationSet", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(presenter);
            actor.enabled = false;
            presenter.enabled = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            var anchorPosition = animator.transform.localPosition;
            var anchorRotation = animator.transform.localRotation;
            var graph = PlayableGraph.Create("Priest seam regression");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                    .Select(b => animator.GetBoneTransform((HumanBodyBones)b)).Where(b => b != null).ToArray();
                Vector3[] positions = null;
                Quaternion[] rotations = null;
                var clips = new[] { set.GetClip(Emberfall.Gameplay.Combat.Domain.CombatState.HeavyAttack),
                    set.GetEnemyClip(EnemyAnimationAction.PriestProjectileWindup),
                    set.GetEnemyClip(EnemyAnimationAction.PriestProjectileRelease) };
                var times = new[] { 13f / 30f, clips[1].length, 0f };
                for (int index = 0; index < clips.Length; index++)
                {
                    var clip = clips[index];
                    var playable = AnimationClipPlayable.Create(graph, clip);
                    playable.SetApplyFootIK(false);
                    playable.SetApplyPlayableIK(false);
                    output.SetSourcePlayable(playable);
                    graph.Play();
                    playable.SetTime(times[index]);
                    graph.Evaluate(0);
                    animator.transform.SetLocalPositionAndRotation(anchorPosition, anchorRotation);
                    if (positions == null)
                    {
                        positions = bones.Select(b => b.position).ToArray();
                        rotations = bones.Select(b => b.rotation).ToArray();
                    }
                    else
                    {
                        float distance = bones.Select((b, i) => Vector3.Distance(b.position, positions[i])).Max();
                        float angle = bones.Select((b, i) => Quaternion.Angle(b.rotation, rotations[i])).Max();
                        Debug.Log($"[PRIEST_SEAM] maxBoneDistance={distance:R} maxBoneAngle={angle:R}");
                        Assert.That(distance, Is.LessThan(.001f), "Matching muscle curves must also yield matching actual bones.");
                        Assert.That(angle, Is.LessThan(.1f));
                    }
                }
            }
            finally { graph.Destroy(); }
        }

        [UnityTest]
        public IEnumerator RealPresenterSplitsWindupAndReleaseWithoutChangingDomainTiming()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley",LoadSceneMode.Single);
            yield return null;
            var actor=Object.FindObjectsOfType<RangedEnemyActor>(true).First(a=>a.isActiveAndEnabled);
            var presenter=actor.GetComponent<RangedEnemyAnimationPresenter>();
            var animator=actor.GetComponentInChildren<Animator>(true);
            actor.enabled=false;
            presenter.enabled=false;
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            var brain=actor.Brain;
            brain.Reset();
            var perception=new RangedEnemyPerception(true,true,7f,0);
            var update=typeof(RangedEnemyAnimationPresenter).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
            brain.Tick(0,perception);
            Assert.That(brain.State,Is.EqualTo(RangedEnemyState.Windup));
            Assert.That(brain.CurrentAttack,Is.EqualTo(RangedAttackKind.Projectile));
            update.Invoke(presenter,null);
            animator.Update(0);
            Assert.That(animator.GetNextAnimatorStateInfo(0).IsName("PriestProjectileWindup"),Is.True);
            Assert.That(animator.speed,Is.EqualTo((13f/30f)/.72f).Within(.0001f));
            animator.Update(actor.Definition.WindupDuration);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime,Is.EqualTo(1).Within(.002f));
            Assert.That(brain.AttackSequence,Is.Zero);
            brain.Tick(actor.Definition.WindupDuration-.001f,perception);
            Assert.That(brain.IsAttackReleaseOpen,Is.False);
            brain.Tick(.0011f,perception);
            Assert.That(brain.State,Is.EqualTo(RangedEnemyState.Release));
            Assert.That(brain.AttackSequence,Is.EqualTo(1));
            update.Invoke(presenter,null);
            animator.Update(0);
            Assert.That(animator.GetNextAnimatorStateInfo(0).IsName("PriestProjectileRelease"),Is.True);
            Assert.That(animator.GetNextAnimatorStateInfo(0).normalizedTime,Is.Zero.Within(.0001f));
            Assert.That(animator.speed,Is.EqualTo((1.3f-13f/30f)/.35f).Within(.0001f));
            animator.Update(actor.Definition.ReleaseDuration);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime,Is.EqualTo(1).Within(.002f));
            Assert.That(brain.StateElapsed,Is.Zero);
            brain.Tick(actor.Definition.ReleaseDuration,perception);
            Assert.That(brain.State,Is.EqualTo(RangedEnemyState.Recovery));
            Assert.That(brain.AttackSequence,Is.EqualTo(1));
            Debug.Log("[PRIEST_PRESENTATION] realPresenter windup=.72 release=.35 sequence=1 phasesComplete=true domainNotAdvancedByAnimator=true");
        }
    }
}
