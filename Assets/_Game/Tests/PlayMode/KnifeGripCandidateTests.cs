using System;
using System.Collections;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed class KnifeGripCandidateTests
    {
        [UnityTest]
        public IEnumerator RenderResolvedWholeHandGrip()
        {
#if UNITY_EDITOR
            if (!Environment.GetCommandLineArgs().Contains("-emberfall-knife-grip-resolved") ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Explicit close-up only.");
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null; yield return null;
            foreach (var item in Object.FindObjectsOfType<MonoBehaviour>())
                if (item is MeleeEnemyActor || item is RangedEnemyActor || item is ShieldEnemyActor) item.enabled = false;
            var actor = Object.FindObjectOfType<PlayerCombatActor>();
            var visual = actor.GetComponent<PlayerThrowingKnifeLauncher>().PreparePresentationCandidate();
            var animator = actor.GetComponentInChildren<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in actor.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
            var pose = UnityEditor.AssetDatabase.LoadAssetAtPath<KnifeGripPose>("Assets/_Game/Settings/KnifeGripPose_Ranger.asset");
            Assert.That(pose, Is.Not.Null);
            var cameraObject = new GameObject("Knife resolved grip close-up");
            var camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(Camera.main); camera.enabled=false; camera.fieldOfView=30; camera.nearClipPlane=.01f;
            var rt = new RenderTexture(960,960,24); rt.Create();
            string output = Path.GetFullPath("Builds/ArtReview/0.9.3-knife-grip-resolved-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(output);
            try
            {
                Assert.That(actor.Model.Submit(CombatCommand.RangedAttack), Is.True);
                while (actor.Model.StateElapsed < .13f) yield return null;
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Vector3 focus = hand.position;
                camera.transform.position = focus+actor.transform.right*.85f-actor.transform.forward*.65f+Vector3.up*.2f;
                camera.transform.LookAt(focus);
                void Capture(string file)
                {
                    RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest {destination=rt});
                    var old=RenderTexture.active; var texture=new Texture2D(960,960,TextureFormat.RGB24,false);
                    try { RenderTexture.active=rt; texture.ReadPixels(new Rect(0,0,960,960),0,0); texture.Apply();
                        File.WriteAllBytes(Path.Combine(output,file),texture.EncodeToPNG()); }
                    finally { RenderTexture.active=old; Object.Destroy(texture); }
                }
                Capture("01-original.png");
                visual.ConfigureGrip(pose);
                yield return null;
                Capture("02-source-whole-hand.png");
                File.WriteAllText(Path.Combine(output,"scope.txt"),
                    "Adjacent natural frames, static comparison only, not release timing. Real Ranger, same-source whole hand16 joints at1.3s. No individual joint tuning. Source="+pose.sourceHash);
                Debug.Log("[KNIFE_GRIP_RESOLVED] output="+output);
            }
            finally { rt.Release(); Object.Destroy(rt); Object.Destroy(cameraObject); }
#else
            Assert.Ignore("Editor only."); yield break;
#endif
        }

        [UnityTest]
        public IEnumerator RenderSingleSourceFingerFrameCandidate()
        {
#if UNITY_EDITOR
            if (!Environment.GetCommandLineArgs().Contains("-emberfall-knife-grip-candidate") ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Explicit static grip comparison only.");
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null; yield return null;
            foreach (var item in Object.FindObjectsOfType<MonoBehaviour>())
                if (item is MeleeEnemyActor || item is RangedEnemyActor || item is ShieldEnemyActor) item.enabled = false;
            var actor = Object.FindObjectOfType<PlayerCombatActor>();
            actor.GetComponent<PlayerThrowingKnifeLauncher>().PreparePresentationCandidate();
            var animator = actor.GetComponentInChildren<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.updateWhenOffscreen = true;
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab");
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/_Game/Art/Animations/Player/Derived/A_Player_RangedAttack_InPlace.anim");
            var clone = Object.Instantiate(prefab);
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            var cloneAnimator = clone.GetComponentInChildren<Animator>();
            cloneAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var graph = PlayableGraph.Create("Static grip same-source candidate");
            var cameraObject = new GameObject("Static grip comparison camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(Camera.main); camera.enabled = false; camera.fieldOfView = 35;
            camera.nearClipPlane = .01f; camera.cullingMask = ~0;
            var rt = new RenderTexture(960, 960, 24); rt.Create();
            var sourceBones = Enumerable.Range((int)HumanBodyBones.RightThumbProximal,
                (int)HumanBodyBones.RightLittleDistal-(int)HumanBodyBones.RightThumbProximal+1).Select(i => (HumanBodyBones)i)
                .Where(b => cloneAnimator.GetBoneTransform(b) != null && animator.GetBoneTransform(b) != null).ToArray();
            Assert.That(sourceBones.Length, Is.GreaterThan(0), "Avatar must expose at least one finger joint for this candidate.");
            var rotations = new Quaternion[sourceBones.Length];
            var originals = new Quaternion[sourceBones.Length];
            string output = Path.GetFullPath("Builds/ArtReview/0.9.3-knife-grip-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(output);
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, source);
                playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                AnimationPlayableOutput.Create(graph, "Pose", cloneAnimator).SetSourcePlayable(playable);
                graph.Play(); playable.SetTime(1.3); graph.Evaluate(0);
                for (int i=0;i<sourceBones.Length;i++) rotations[i]=cloneAnimator.GetBoneTransform(sourceBones[i]).localRotation;
                graph.Stop();
                Assert.That(actor.Model.Submit(CombatCommand.RangedAttack), Is.True);
                while (actor.Model.StateElapsed < .15f) yield return null;
                // Adjacent-frame static pose comparison: not contact timing or a final animation.
                var grip = actor.GetComponentsInChildren<Transform>().Single(t => t.name == "Sword_M6_Player_Equipped");
                Vector3 focus = grip.position + grip.up * .09f;
                camera.transform.position = focus + actor.transform.right * 1.0f - actor.transform.forward * .75f + Vector3.up * .2f;
                camera.transform.LookAt(focus);
                void Capture(string name)
                {
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination=rt });
                    var old=RenderTexture.active; var texture=new Texture2D(960,960,TextureFormat.RGB24,false);
                    try { RenderTexture.active=rt; texture.ReadPixels(new Rect(0,0,960,960),0,0); texture.Apply();
                        File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG()); }
                    finally { RenderTexture.active=old; Object.Destroy(texture); }
                }
                Capture("01-original.png");
                animator.enabled = false;
                float maxFingerDelta = 0;
                for (int i=0;i<sourceBones.Length;i++)
                { var bone=animator.GetBoneTransform(sourceBones[i]); originals[i]=bone.localRotation;
                    maxFingerDelta=Mathf.Max(maxFingerDelta,Quaternion.Angle(originals[i],rotations[i])); bone.localRotation=rotations[i]; }
                // SkinnedMeshRenderer caches bone matrices within a rendered frame. Allow the
                // next skinning update with Animator disabled; do not mislabel identical cached pixels.
                yield return null;
                Capture("02-source-1.3s-fingers.png");
                for (int i=0;i<sourceBones.Length;i++) animator.GetBoneTransform(sourceBones[i]).localRotation=originals[i];
                animator.enabled = true;
                File.WriteAllText(Path.Combine(output,"scope.txt"),
                    "STATIC TEST CANDIDATE ONLY. Same OverhandThrow InPlace source at1.3s, all right finger local rotations copied as one pose; no per-bone hand tuning. No deliberate body/wrist/weapon/authority edits; actor motor/domain remain live between adjacent skinning frames, so not pixel-identical body controls. Not production or natural contact evidence. Source="+
                    UnityEditor.AssetDatabase.GetAssetPath(source)+" hash="+UnityEditor.AssetDatabase.GetAssetDependencyHash(UnityEditor.AssetDatabase.GetAssetPath(source))+
                    " mappedFingerBones="+string.Join(",",sourceBones)+" maxFingerRotationDelta="+maxFingerDelta);
                Debug.Log("[KNIFE_GRIP_CANDIDATE] output="+output);
            }
            finally { animator.enabled=true; graph.Destroy(); Object.Destroy(clone); rt.Release(); Object.Destroy(rt); Object.Destroy(cameraObject); }
#else
            Assert.Ignore("Editor asset comparison only."); yield break;
#endif
        }
    }
}
