using System.Linq;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class PlayerAnimationAssetTests
    {
        private const string LibraryRoot = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary";
        private const string Library1Path = LibraryRoot + "/UAL1/AnimationLibrary_Unity_Standard.fbx";
        private const string Library2Path = LibraryRoot + "/UAL2/UAL2_Standard.fbx";
        private const string ControllerPath = "Assets/_Game/Art/Animations/Player/AC_Player_M1.controller";
        private const string DerivedFolder = "Assets/_Game/Art/Animations/Player/Derived";
        private const string AnimationSetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";

        private static readonly string[] InPlaceClipNames =
        {
            "A_Player_LightAttack1_InPlace",
            "A_Player_LightAttack1Recovery_InPlace",
            "A_Player_LightAttack2_InPlace",
            "A_Player_LightAttack2Recovery_InPlace",
            "A_Player_LightAttack3_InPlace",
            "A_Player_HeavyAttack_InPlace",
            "A_Player_RangedAttack_InPlace",
            "A_Player_Dodge_InPlace",
            "A_Player_Guard_InPlace",
            "A_Player_GuardBreak_InPlace",
            "A_Player_HitReact_InPlace",
            "A_Player_Heal_InPlace",
            "A_Player_Dead_InPlace",
            "A_Enemy_MeleeCombo_InPlace",
            "A_Enemy_RuneCast_InPlace",
            "A_Enemy_ShieldGuard_InPlace",
            "A_Enemy_ShieldBash_InPlace",
            "A_Warden_Charge_InPlace"
        };

        [TestCase(Library1Path)]
        [TestCase(Library2Path)]
        public void AnimationLibrary_UsesValidHumanoidAvatar(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Human));

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
            Assert.That(avatar, Is.Not.Null);
            Assert.That(avatar.isHuman, Is.True);
            Assert.That(avatar.isValid, Is.True);
        }

        [TestCase(Library1Path)]
        [TestCase(Library2Path)]
        public void AnimationLibrary_ExtractsXzTravelInsteadOfBakingItIntoPose(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.clipAnimations, Is.Not.Empty);
            foreach (ModelImporterClipAnimation clip in importer.clipAnimations)
            {
                Assert.That(clip.lockRootPositionXZ, Is.False, $"XZ travel is still baked into {clip.name}");
                Assert.That(clip.keepOriginalPositionXZ, Is.True, $"XZ origin is not preserved for {clip.name}");
            }
        }

        [Test]
        public void AnimationController_CoversEveryCombatState()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null);

            string[] stateNames = controller.layers[0].stateMachine.states
                .Select(state => state.state.name)
                .ToArray();
            foreach (string expected in System.Enum.GetNames(typeof(CombatState)))
            {
                Assert.That(stateNames, Does.Contain(expected));
            }
            Assert.That(stateNames, Does.Contain("EnemyShieldGuard"));
            Assert.That(stateNames, Does.Contain("WardenChargeWindup"));
            Assert.That(stateNames, Does.Contain("WardenChargeRecovery"));
        }

        [Test]
        public void AnimationSet_MapsAllNonLocomotionStates()
        {
            PlayerAnimationSet animationSet = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(AnimationSetPath);
            Assert.That(animationSet, Is.Not.Null);
            Assert.That(animationSet.Controller, Is.Not.Null);

            foreach (CombatState state in System.Enum.GetValues(typeof(CombatState)))
            {
                if (state != CombatState.Locomotion)
                {
                    Assert.That(animationSet.GetClip(state), Is.Not.Null, $"Missing clip for {state}");
                }
            }

            Assert.That(animationSet.GetRecoveryClip(CombatState.LightAttack1), Is.Not.Null);
            Assert.That(animationSet.GetRecoveryClip(CombatState.LightAttack2), Is.Not.Null);
            foreach (EnemyAnimationAction action in System.Enum.GetValues(typeof(EnemyAnimationAction)))
            {
                Assert.That(animationSet.GetEnemyClip(action), Is.Not.Null, $"Missing enemy clip for {action}");
            }
        }

        [Test]
        public void DerivedActionClips_HaveNoAuthoredPlanarRootTravel()
        {
            foreach (string clipName in InPlaceClipNames)
            {
                string path = $"{DerivedFolder}/{clipName}.anim";
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                Assert.That(clip, Is.Not.Null, $"Missing derived In-Place clip: {path}");

                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip)
                             .Where(binding => binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z"))
                {
                    AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                    Assert.That(curve, Is.Not.Null);
                    Assert.That(curve.keys.Select(key => Mathf.Abs(key.value)).Max(), Is.LessThan(0.0001f),
                        $"{clipName} still contains planar travel in {binding.propertyName}");
                    Assert.That(Mathf.Abs(curve.Evaluate(clip.length)), Is.LessThan(0.0001f));
                }
            }
        }

        [Test]
        public void EnemyShieldGuard_IsAConstantLoopingPose()
        {
            string path = $"{DerivedFolder}/A_Enemy_ShieldGuard_InPlace.anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            Assert.That(clip, Is.Not.Null);
            Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.True);

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                Assert.That(curve, Is.Not.Null);
                Assert.That(curve.keys.Select(key => key.value).Max() - curve.keys.Select(key => key.value).Min(),
                    Is.LessThan(0.0001f), $"Guard pose still animates {binding.propertyName}");
            }
        }

        [Test]
        public void LightAttackOneAndRecovery_HaveNoAuthoredRootRotationDelta()
        {
            AnimationClip attack = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                $"{DerivedFolder}/A_Player_LightAttack1_InPlace.anim");
            AnimationClip recovery = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                $"{DerivedFolder}/A_Player_LightAttack1Recovery_InPlace.anim");
            Assert.That(attack, Is.Not.Null);
            Assert.That(recovery, Is.Not.Null);

            Quaternion reference = ReadRootRotation(attack, 0f);
            Assert.That(Quaternion.Angle(reference, ReadRootRotation(attack, attack.length)), Is.LessThan(0.1f));
            Assert.That(Quaternion.Angle(reference, ReadRootRotation(recovery, 0f)), Is.LessThan(0.1f));
            Assert.That(Quaternion.Angle(reference, ReadRootRotation(recovery, recovery.length)), Is.LessThan(0.1f));
        }

        private static Quaternion ReadRootRotation(AnimationClip clip, float time)
        {
            Quaternion rotation = Quaternion.identity;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip)
                         .Where(binding => binding.propertyName.StartsWith("RootQ.")))
            {
                float value = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(time);
                switch (binding.propertyName)
                {
                    case "RootQ.x": rotation.x = value; break;
                    case "RootQ.y": rotation.y = value; break;
                    case "RootQ.z": rotation.z = value; break;
                    case "RootQ.w": rotation.w = value; break;
                }
            }

            return rotation.normalized;
        }

        [Test]
        public void ThirdPartyAnimationLibraries_DeclareCc0License()
        {
            TextAsset license1 = AssetDatabase.LoadAssetAtPath<TextAsset>(LibraryRoot + "/UAL1/License.txt");
            TextAsset license2 = AssetDatabase.LoadAssetAtPath<TextAsset>(LibraryRoot + "/UAL2/License.txt");
            Assert.That(license1, Is.Not.Null);
            Assert.That(license2, Is.Not.Null);
            Assert.That(license1.text, Does.Contain("CC0 1.0"));
            Assert.That(license2.text, Does.Contain("CC0 1.0"));
        }
    }
}
