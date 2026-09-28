using System.Linq;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Emberfall.Editor.Setup
{
    public static class KnifeGripPoseSetup
    {
        public const string Path = "Assets/_Game/Settings/KnifeGripPose_Ranger.asset";
        public const string AvatarPath = "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab";
        public static void Apply()
        {
            var clone = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPath));
            var graph = PlayableGraph.Create("Bake same-source knife hand");
            try
            {
                var animator = clone.GetComponentInChildren<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerKnifeAnimationSetup.SourcePath);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var clip = AnimationClipPlayable.Create(graph, source);
                clip.SetApplyFootIK(false); clip.SetApplyPlayableIK(false);
                AnimationPlayableOutput.Create(graph, "Pose", animator).SetSourcePlayable(clip);
                graph.Play(); clip.SetTime(1.3); graph.Evaluate(0);
                var bones = new[] { HumanBodyBones.RightHand }.Concat(Enumerable.Range(
                    (int)HumanBodyBones.RightThumbProximal, 15).Select(i => (HumanBodyBones)i)).ToArray();
                var pose = AssetDatabase.LoadAssetAtPath<KnifeGripPose>(Path);
                bool create = pose == null;
                if (create) pose = ScriptableObject.CreateInstance<KnifeGripPose>();
                pose.joints = bones.Select(b => new KnifeGripPose.Joint {
                    bone=b, rotation=animator.GetBoneTransform(b).localRotation }).ToArray();
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Vector3 index = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal).position;
                Vector3 little = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal).position;
                Vector3 fingerCenter = (animator.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate).position +
                    animator.GetBoneTransform(HumanBodyBones.RightRingIntermediate).position) * .5f;
                Vector3 center = Vector3.Lerp(fingerCenter, hand.position, .2f);
                Vector3 axis = (index-little).normalized;
                Vector3 palm = Vector3.Cross(axis, (index+little)*.5f-hand.position).normalized;
                pose.gripLocalPosition = hand.InverseTransformPoint(center);
                pose.gripLocalRotation = Quaternion.Inverse(hand.rotation) * Quaternion.LookRotation(axis, palm);
                pose.sourcePath = PlayerKnifeAnimationSetup.SourcePath;
                pose.sourceHash = AssetDatabase.GetAssetDependencyHash(pose.sourcePath).ToString();
                pose.avatarPath = AvatarPath; pose.sourceTime = 1.3f;
                if (create) AssetDatabase.CreateAsset(pose, Path);
                EditorUtility.SetDirty(pose); AssetDatabase.SaveAssets();
                Debug.Log("[KNIFE_GRIP] Baked16 joints, source="+pose.sourceHash);
            }
            finally { graph.Destroy(); Object.DestroyImmediate(clone); }
        }
    }
}
