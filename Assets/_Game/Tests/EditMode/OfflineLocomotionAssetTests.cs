using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Animation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class OfflineLocomotionAssetTests
    {
        const string SharedPath = "Assets/_Game/Art/Animations/Player/AC_Player_M1.controller";
        const string SetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";

        [Test]
        public void OfflineTree_DistinguishesWalkJogAndSprint_WithoutChangingSharedTree()
        {
            var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(SetPath);
            Assert.That(set.Controller, Is.Not.SameAs(set.OfflineController));
            var shared = (AnimatorController)set.Controller;
            var owned = (AnimatorController)set.OfflineController;
            BlendTree Tree(AnimatorController c) => (BlendTree)c.layers[0].stateMachine.states
                .Single(s => s.state.name == "Locomotion").state.motion;
            Assert.That(AssetDatabase.GetAssetPath(owned), Is.EqualTo(OfflineLocomotionSetup.ControllerPath));
            Assert.That(Tree(owned).children.Select(c => c.motion.name), Is.EqualTo(new[] {
                "Rig|Idle_Loop", "Rig|Walk_Loop", "Rig|Jog_Fwd_Loop", "Rig|Sprint_Loop" }));
            Assert.That(Tree(owned).children.Select(c => c.threshold), Is.EqualTo(new[] { 0f, 2.2f, 5.4f, 8.2f }));
            Assert.That(Tree(owned).children.All(c => c.timeScale == 1f), Is.True);
            Assert.That(Tree(shared).children.Select(c => c.motion.name), Is.EqualTo(new[] {
                "Rig|Idle_Loop", "Rig|Walk_Loop", "Rig|Sprint_Loop" }));
            Assert.That(Tree(shared).children.Select(c => c.threshold), Is.EqualTo(new[] { 0f, 2.2f, 5.4f }));
        }

        [Test]
        public void OfflineCopy_PreservesEveryOtherSerializedStateParameterAndTransition()
        {
            string Normalize(string path) => Regex.Replace(File.ReadAllText(path).Replace("\r\n", "\n"),
                @"(?ms)^--- !u!206 &-?\d+\n.*?(?=^--- !u!|\z)", "")
                .Replace("m_Name: AC_Player_OfflineLocomotion", "m_Name: AC_Player_M1");
            Assert.That(Normalize(OfflineLocomotionSetup.ControllerPath), Is.EqualTo(Normalize(SharedPath)),
                "Only the locomotion BlendTree/controller name may differ, not action clocks or transitions.");
        }

        [Test]
        public void ScopedSetup_RepeatedApplicationWritesNeitherSharedNorOwnedAssets()
        {
            string[] paths = { SharedPath, SharedPath + ".meta", OfflineLocomotionSetup.ControllerPath,
                OfflineLocomotionSetup.ControllerPath + ".meta", SetPath, SetPath + ".meta" };
            var before = paths.Select(File.ReadAllBytes).ToArray();
            var times = paths.Select(File.GetLastWriteTimeUtc).ToArray();
            OfflineLocomotionSetup.Apply(); OfflineLocomotionSetup.Apply();
            for (int i = 0; i < paths.Length; i++)
            {
                Assert.That(File.ReadAllBytes(paths[i]), Is.EqualTo(before[i]), paths[i]);
                Assert.That(File.GetLastWriteTimeUtc(paths[i]), Is.EqualTo(times[i]), paths[i]);
            }
        }

        [Test]
        public void LegacySet_WithoutOfflineControllerFallsBackToShared()
        {
            var set = ScriptableObject.CreateInstance<PlayerAnimationSet>();
            try
            {
                var serialized = new SerializedObject(set);
                serialized.FindProperty("_controller").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AnimatorController>(SharedPath);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(set.OfflineController, Is.SameAs(set.Controller));
            }
            finally { Object.DestroyImmediate(set); }
        }
    }
}
