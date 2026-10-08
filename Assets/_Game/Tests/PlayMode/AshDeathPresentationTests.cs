#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Controlled lethal damage, NORMAL authority/PlayerLoop, never manual animation/state forcing.</summary>
    public sealed class AshDeathPresentationTests
    {
        [Serializable] sealed class Row
        {
            public string phase, actor, brain, animationClip, presenterState, freeze, culling, updateMode;
            public bool authority, actorEnabled, rootActive, animatorEnabled, inDead, inTransition;
            public bool anyRendererVisible, editorPaused, animatorInitialized;
            public int frame; public float time, speed, normalizedTime; public Vector3 root, hips, head;
        }
        [Serializable] sealed class Report
        {
            public string scope = "Actual formal A production rigs/Animator, isolated new game, controlled player placement and lethal DamageRequest callbacks. Normal authority/AI retained; no manual Animator.Update, CrossFade, Tick, pose or speed changes. Records callback, SAME clear-frame rendering, then completion of actual death clip. Not natural input/difficulty/contact or all enemy-death acceptance.";
            public Row[] rows;
        }
        [UnityTest] public IEnumerator NormalAuthorityDeathTransitionsActuallyFinishBeforeSettledClearScreenshot()
        {
            Time.timeScale=1;
            Assert.That(M2RouteFlowController.EditorTestSavePath,Does.Contain("IsolatedSaves"));
            M2LaunchIntent.RequestNewGame();yield return SceneManager.LoadSceneAsync("10_EmberValley");yield return null;yield return null;
            var flow=Object.FindObjectOfType<M2RouteFlowController>();Assert.That(flow.SavePath,Is.EqualTo(M2RouteFlowController.EditorTestSavePath));
            var player=Object.FindObjectOfType<PlayerCombatActor>();var controller=player.GetComponent<CharacterController>();
            Vector3 offset=player.transform.position-player.NavigationFootPosition;
            controller.enabled=false;player.transform.position=new Vector3(0,.04f,10.2f)+offset;controller.enabled=true;Physics.SyncTransforms();
            yield return new WaitForSeconds(.2f);
            var encounter=GameObject.Find("Content_AshApproach_v1");var owner=encounter.GetComponentInChildren<SummonerEnemyActor>();var companions=encounter.GetComponentsInChildren<MeleeEnemyActor>();
            Assert.That(owner.HasSimulationAuthority,Is.True);Assert.That(companions.All(e=>e.HasSimulationAuthority),Is.True);
            var objects=new[]{owner.gameObject}.Concat(companions.Select(e=>e.gameObject)).ToArray();
            var animators=objects.Select(e=>e.GetComponentInChildren<Animator>()).ToArray();
            string folder=Path.GetFullPath("Builds/ArtReview/ash-death/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(folder);
            var rows=new List<Row>();
            try
            {
                Assert.That(owner.ReceiveDamage(new DamageRequest(player.CombatantId,701,9999,100,AttackTag.Heavy)).Killed,Is.True);
                foreach(var e in companions)Assert.That(e.ReceiveDamage(new DamageRequest(player.CombatantId,702,9999,100,AttackTag.Heavy)).Killed,Is.True);
                CaptureState("lethal-callback",objects,animators,rows);
                yield return null;Assert.That(flow.AshApproachCleared,Is.True);int clearFrame=Time.frameCount;
                yield return new WaitForEndOfFrame();Assert.That(Time.frameCount,Is.EqualTo(clearFrame));
                CaptureState("same-clear-frame-render",objects,animators,rows);ScreenCapture.CaptureScreenshot(folder+"/same-clear-frame.png");
                // Observe the existing blend and actual clip finish, never force a pose or change its speed.
                yield return new WaitForSeconds(.12f);yield return new WaitForEndOfFrame();CaptureState("post-blend-render",objects,animators,rows);
                foreach(var a in animators){Assert.That(a.GetCurrentAnimatorStateInfo(0).IsName("Dead"),Is.True);Assert.That(a.speed,Is.GreaterThan(0));}
                float duration=animators.Max(a=>a.GetCurrentAnimatorClipInfo(0).Max(c=>c.clip.length)/a.speed);
                yield return new WaitForSeconds(duration+.12f);yield return new WaitForEndOfFrame();
                CaptureState("settled-death",objects,animators,rows);
                foreach(var a in animators)
                {
                    var state=a.GetCurrentAnimatorStateInfo(0);Assert.That(state.IsName("Dead"),Is.True);
                    Assert.That(state.normalizedTime,Is.GreaterThanOrEqualTo(.95f),"Death must advance, not freeze at its entry pose.");
                    Assert.That(AnimatorSpeedCoordinator.For(a).ActiveGrade,Is.EqualTo(HitFeedbackGrade.None));
                }
                Assert.That(owner.Brain.State,Is.EqualTo(RangedEnemyState.Dead));Assert.That(companions.All(e=>e.State==MeleeEnemyState.Dead),Is.True);
                Assert.That(owner.HasSimulationAuthority,Is.True);Assert.That(companions.All(e=>e.HasSimulationAuthority),Is.True);
                Assert.That(Object.FindObjectsOfType<M2EncounterBannerPresenter>().Single(p=>p.Encounter==AshEncounterBanner.Approach).IsBannerVisible,Is.True);
                ScreenCapture.CaptureScreenshot(folder+"/settled-death-and-clear-flag.png");
                float end=Time.realtimeSinceStartup+5;while(!File.Exists(folder+"/settled-death-and-clear-flag.png")&&Time.realtimeSinceStartup<end)yield return null;
                Assert.That(File.Exists(folder+"/settled-death-and-clear-flag.png"),Is.True);Assert.That(Time.timeScale,Is.EqualTo(1));
            }
            finally {File.WriteAllText(folder+"/report.json",JsonUtility.ToJson(new Report{rows=rows.ToArray()},true));Time.timeScale=1;}
        }
        static void CaptureState(string phase,GameObject[] actors,Animator[] animators,List<Row> rows)
        {
            for(int i=0;i<actors.Length;i++)
            {
                var owner=actors[i].GetComponent<SummonerEnemyActor>();var melee=actors[i].GetComponent<MeleeEnemyActor>();var a=animators[i];var state=a.GetCurrentAnimatorStateInfo(0);
                var presenter=actors[i].GetComponent<MeleeEnemyAnimationPresenter>();
                string value=owner!=null?(string)typeof(SummonerEnemyActor).GetField("_presentedState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(owner)
                    :presenter==null?"no presenter":typeof(MeleeEnemyAnimationPresenter).GetField("_presented",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(presenter).ToString();
                rows.Add(new Row{phase=phase,actor=actors[i].name,brain=owner!=null?owner.Brain.State.ToString():melee.State.ToString(),authority=owner!=null?owner.HasSimulationAuthority:melee.HasSimulationAuthority,
                    actorEnabled=owner!=null?owner.enabled:melee.enabled,rootActive=actors[i].activeInHierarchy,animatorEnabled=a.enabled,inDead=state.IsName("Dead"),inTransition=a.IsInTransition(0),
                    frame=Time.frameCount,time=Time.time,speed=a.speed,normalizedTime=state.normalizedTime,root=actors[i].transform.position,
                    hips=a.GetBoneTransform(HumanBodyBones.Hips).position,head=a.GetBoneTransform(HumanBodyBones.Head).position,presenterState=value,
                    animationClip=string.Join("+",a.GetCurrentAnimatorClipInfo(0).Select(c=>c.clip.name)),freeze=AnimatorSpeedCoordinator.For(a).ActiveGrade.ToString(),
                    anyRendererVisible=actors[i].GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled&&r.isVisible),editorPaused=UnityEditor.EditorApplication.isPaused,
                    animatorInitialized=a.isInitialized,culling=a.cullingMode.ToString(),updateMode=a.updateMode.ToString()});
            }
        }
    }
}
#endif
