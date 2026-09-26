using System;
using System.IO;
using System.Linq;
using Emberfall.Gameplay.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    public static class M1AnimationSetup
    {
        internal const string Library1Path =
            "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/UAL1/AnimationLibrary_Unity_Standard.fbx";
        internal const string Library2Path =
            "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/UAL2/UAL2_Standard.fbx";
        internal const string ControllerPath = "Assets/_Game/Art/Animations/Player/AC_Player_M1.controller";
        internal const string AnimationSetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        internal const string DerivedFolder = "Assets/_Game/Art/Animations/Player/Derived";

        [MenuItem("Emberfall/Setup/Apply M1 Animation Setup")]
        public static PlayerAnimationSet Apply()
        {
            EnsureFolder("Assets/_Game/Art/Animations/Player");
            EnsureFolder(DerivedFolder);
            ConfigureHumanoidLibrary(Library1Path);
            ConfigureHumanoidLibrary(Library2Path);

            AnimationClip idle = FindClip(Library1Path, "Rig|Idle_Loop");
            AnimationClip walk = FindClip(Library1Path, "Rig|Walk_Loop");
            AnimationClip sprint = FindClip(Library1Path, "Rig|Sprint_Loop");
            AnimationClip light1Source = FindClip(Library2Path, "Armature|Sword_Regular_A");
            Quaternion light1NeutralRoot = EvaluateRootRotation(light1Source, 0f);
            AnimationClip light1 = CreateInPlaceClip(
                light1Source,
                "A_Player_LightAttack1_InPlace",
                light1NeutralRoot);
            AnimationClip light1Recovery = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|Sword_Regular_A_Rec"),
                "A_Player_LightAttack1Recovery_InPlace",
                light1NeutralRoot);
            AnimationClip light2 = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|Sword_Regular_B"),
                "A_Player_LightAttack2_InPlace");
            AnimationClip light2Recovery = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|Sword_Regular_B_Rec"),
                "A_Player_LightAttack2Recovery_InPlace");
            AnimationClip light3 = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|Sword_Regular_C"),
                "A_Player_LightAttack3_InPlace");
            AnimationClip heavyCharge = FindClip(Library1Path, "Rig|Sword_Idle");
            AnimationClip heavyAttack = CreateInPlaceClip(
                FindClip(Library1Path, "Rig|Sword_Attack"),
                "A_Player_HeavyAttack_InPlace");
            AnimationClip sweep = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|Sword_Regular_Combo"),
                "A_Player_Sweep_InPlace");
            AnimationClip rangedAttack = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|OverhandThrow"),
                "A_Player_RangedAttack_InPlace");
            AnimationClip dodge = CreateInPlaceClip(
                FindClip(Library1Path, "Rig|Roll"),
                "A_Player_Dodge_InPlace");
            AnimationClip guard = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|Sword_Block"),
                "A_Player_Guard_InPlace");
            AnimationClip guardBreak = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|Hit_Knockback"),
                "A_Player_GuardBreak_InPlace");
            AnimationClip hitReact = CreateInPlaceClip(
                FindClip(Library1Path, "Rig|Hit_Chest"),
                "A_Player_HitReact_InPlace");
            AnimationClip heal = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|Consume"),
                "A_Player_Heal_InPlace");
            AnimationClip dead = CreateInPlaceClip(
                FindClip(Library1Path, "Rig|Death01"),
                "A_Player_Dead_InPlace");
            AnimationClip enemyMeleeCombo = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|Sword_Regular_Combo"),
                "A_Enemy_MeleeCombo_InPlace");
            AnimationClip enemyRuneCast = CreateInPlaceClip(
                FindClip(Library1Path, "Rig|Spell_Simple_Shoot"),
                "A_Enemy_RuneCast_InPlace");
            AnimationClip enemyShieldGuard = CreateInPlacePoseClip(
                FindClip(Library2Path, "Armature|Sword_Block"),
                "A_Enemy_ShieldGuard_InPlace",
                0.36f);
            AnimationClip enemyShieldBash = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|Shield_OneShot"),
                "A_Enemy_ShieldBash_InPlace");
            AnimationClip wardenCharge = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|Sword_Dash_RM"),
                "A_Warden_Charge_InPlace");
            AnimationClip wardenPhaseBreak = CreateInPlaceClip(
                FindClip(Library2Path, "Armature|Idle_Shield_Break"),
                "A_Warden_PhaseBreak_InPlace");
            AnimationClip wardenRuneCleave = CreateInPlaceClip(
                FindClip(Library1Path, "Rig|Sword_Attack"),
                "A_Warden_RuneCleave_InPlace");

            AnimatorController controller = CreateController(
                idle, walk, sprint, light1, light1Recovery, light2, light2Recovery, light3,
                heavyCharge, heavyAttack, sweep, rangedAttack, dodge, guard, guardBreak, hitReact, heal, dead,
                enemyMeleeCombo, enemyRuneCast, enemyShieldGuard, enemyShieldBash, wardenCharge,
                wardenPhaseBreak, wardenRuneCleave);
            PlayerAnimationSet animationSet = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(AnimationSetPath);
            if (animationSet == null)
            {
                animationSet = ScriptableObject.CreateInstance<PlayerAnimationSet>();
                AssetDatabase.CreateAsset(animationSet, AnimationSetPath);
            }

            animationSet.Configure(
                controller, light1, light1Recovery, light2, light2Recovery, light3, heavyCharge,
                heavyAttack, sweep, rangedAttack, dodge, guard, guardBreak, hitReact, heal, dead,
                enemyMeleeCombo, enemyRuneCast, enemyShieldGuard, enemyShieldBash, wardenCharge,
                wardenPhaseBreak, wardenRuneCleave);
            EditorUtility.SetDirty(animationSet);
            PriestProjectileAnimationSetup.Apply();
            SweepAnimationSetup.Apply();
            AssetDatabase.SaveAssets();
            Debug.Log("M1 player animation setup completed with CC0 Humanoid clips and root motion disabled.");
            return animationSet;
        }

        [MenuItem("Emberfall/Setup/Audit M1 Animation Sources")]
        public static void LogSourceClips()
        {
            LogClips(Library1Path);
            LogClips(Library2Path);
        }

        private static void LogClips(string assetPath)
        {
            string[] clips = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__"))
                .Select(clip => clip.name)
                .OrderBy(name => name)
                .ToArray();

            Debug.Log($"Animation source: {assetPath}\n{string.Join("\n", clips)}");
        }

        private static void ConfigureHumanoidLibrary(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not ModelImporter importer)
            {
                throw new FileNotFoundException("Animation library FBX is missing.", assetPath);
            }

            bool rigChanged = importer.animationType != ModelImporterAnimationType.Human ||
                              importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                bool shouldLoop = clip.name.EndsWith("_Loop", StringComparison.Ordinal) ||
                                  clip.name.EndsWith("|Sword_Idle", StringComparison.Ordinal);
                clip.loopTime = shouldLoop;
                clip.loopPose = shouldLoop;
                clip.lockRootHeightY = true;
                // Preserve authored root extraction metadata for source inspection. Gameplay never consumes
                // this travel: project-owned derived clips flatten RootT.x/z before entering the controller.
                clip.lockRootPositionXZ = false;
                clip.keepOriginalPositionXZ = true;
                clip.lockRootRotation = true;
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                throw new InvalidOperationException($"Animation source could not produce a valid Humanoid avatar: {assetPath}");
            }

            if (rigChanged)
            {
                Debug.Log($"Configured Humanoid retargeting for {assetPath}");
            }
        }

        private static AnimatorController CreateController(
            AnimationClip idle,
            AnimationClip walk,
            AnimationClip sprint,
            AnimationClip light1,
            AnimationClip light1Recovery,
            AnimationClip light2,
            AnimationClip light2Recovery,
            AnimationClip light3,
            AnimationClip heavyCharge,
            AnimationClip heavyAttack,
            AnimationClip sweep,
            AnimationClip rangedAttack,
            AnimationClip dodge,
            AnimationClip guard,
            AnimationClip guardBreak,
            AnimationClip hitReact,
            AnimationClip heal,
            AnimationClip dead,
            AnimationClip enemyMeleeCombo,
            AnimationClip enemyRuneCast,
            AnimationClip enemyShieldGuard,
            AnimationClip enemyShieldBash,
            AnimationClip wardenCharge,
            AnimationClip wardenPhaseBreak,
            AnimationClip wardenRuneCleave)
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;

            var locomotionTree = new BlendTree
            {
                name = "BT_Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false
            };
            AssetDatabase.AddObjectToAsset(locomotionTree, controller);
            locomotionTree.AddChild(idle, 0f);
            locomotionTree.AddChild(walk, 2.2f);
            locomotionTree.AddChild(sprint, 5.4f);

            AnimatorState locomotion = AddState(stateMachine, "Locomotion", locomotionTree);
            stateMachine.defaultState = locomotion;
            AddState(stateMachine, "LightAttack1", light1);
            AddState(stateMachine, "LightAttack1Recovery", light1Recovery);
            AddState(stateMachine, "LightAttack2", light2);
            AddState(stateMachine, "LightAttack2Recovery", light2Recovery);
            AddState(stateMachine, "LightAttack3", light3);
            AddState(stateMachine, "HeavyCharge", heavyCharge);
            AddState(stateMachine, "HeavyAttack", heavyAttack);
            AddState(stateMachine, "Sweep", sweep);
            AddState(stateMachine, "RangedAttack", rangedAttack);
            AddState(stateMachine, "Dodge", dodge);
            AddState(stateMachine, "Guard", guard);
            AddState(stateMachine, "GuardBreak", guardBreak);
            AddState(stateMachine, "HitReact", hitReact);
            AddState(stateMachine, "Heal", heal);
            AddState(stateMachine, "Execution", heavyAttack);
            AddState(stateMachine, "Dead", dead);
            AddState(stateMachine, "EnemyMeleeCombo", enemyMeleeCombo);
            AddState(stateMachine, "EnemyRuneCast", enemyRuneCast);
            AddState(stateMachine, "EnemyShieldGuard", enemyShieldGuard);
            AddState(stateMachine, "EnemyShieldBash", enemyShieldBash);
            AddState(stateMachine, "WardenChargeWindup", wardenCharge);
            AddState(stateMachine, "WardenCharge", wardenCharge);
            AddState(stateMachine, "WardenChargeRecovery", wardenCharge);
            AddState(stateMachine, "WardenPhaseBreak", wardenPhaseBreak);
            AddState(stateMachine, "WardenRuneCleave", wardenRuneCleave);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static AnimatorState AddState(AnimatorStateMachine stateMachine, string name, Motion motion)
        {
            AnimatorState state = stateMachine.AddState(name);
            state.motion = motion;
            state.writeDefaultValues = false;
            return state;
        }

        private static AnimationClip FindClip(string assetPath, string exactName)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate => candidate.name == exactName);
            if (clip == null)
            {
                throw new InvalidOperationException($"Required animation clip '{exactName}' was not found in {assetPath}");
            }

            return clip;
        }

        private static AnimationClip CreateInPlaceClip(
            AnimationClip source,
            string assetName,
            Quaternion? lockedRootRotation = null)
        {
            string path = $"{DerivedFolder}/{assetName}.anim";
            AnimationClip derived = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (derived == null)
            {
                derived = UnityEngine.Object.Instantiate(source);
                AssetDatabase.CreateAsset(derived, path);
            }
            else
            {
                EditorUtility.CopySerialized(source, derived);
            }

            derived.name = assetName;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(derived))
            {
                if (binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z")
                {
                    AnimationUtility.SetEditorCurve(
                        derived,
                        binding,
                        AnimationCurve.Constant(0f, derived.length, 0f));
                }
                else if (lockedRootRotation.HasValue && binding.propertyName.StartsWith("RootQ.", StringComparison.Ordinal))
                {
                    Quaternion root = lockedRootRotation.Value;
                    float value = binding.propertyName switch
                    {
                        "RootQ.x" => root.x,
                        "RootQ.y" => root.y,
                        "RootQ.z" => root.z,
                        "RootQ.w" => root.w,
                        _ => throw new InvalidOperationException($"Unexpected root rotation curve {binding.propertyName}")
                    };
                    AnimationUtility.SetEditorCurve(
                        derived,
                        binding,
                        AnimationCurve.Constant(0f, derived.length, value));
                }
            }

            EditorUtility.SetDirty(derived);
            return derived;
        }

        private static Quaternion EvaluateRootRotation(AnimationClip source, float time)
        {
            Quaternion root = Quaternion.identity;
            bool foundX = false;
            bool foundY = false;
            bool foundZ = false;
            bool foundW = false;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                if (!binding.propertyName.StartsWith("RootQ.", StringComparison.Ordinal))
                    continue;

                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
                if (curve == null) continue;
                float value = curve.Evaluate(time);
                switch (binding.propertyName)
                {
                    case "RootQ.x": root.x = value; foundX = true; break;
                    case "RootQ.y": root.y = value; foundY = true; break;
                    case "RootQ.z": root.z = value; foundZ = true; break;
                    case "RootQ.w": root.w = value; foundW = true; break;
                }
            }

            if (!foundX || !foundY || !foundZ || !foundW)
                throw new InvalidOperationException($"Animation clip '{source.name}' has no complete Humanoid RootQ curves.");

            return root.normalized;
        }

        private static AnimationClip CreateInPlacePoseClip(
            AnimationClip source,
            string assetName,
            float normalizedTime)
        {
            string path = $"{DerivedFolder}/{assetName}.anim";
            AnimationClip derived = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (derived == null)
            {
                derived = UnityEngine.Object.Instantiate(source);
                AssetDatabase.CreateAsset(derived, path);
            }
            else
            {
                EditorUtility.CopySerialized(source, derived);
            }

            derived.name = assetName;
            float sampleTime = Mathf.Clamp01(normalizedTime) * source.length;
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(derived))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(derived, binding);
                if (curve == null)
                    continue;

                float value = binding.propertyName == "RootT.x" || binding.propertyName == "RootT.z"
                    ? 0f
                    : curve.Evaluate(sampleTime);
                AnimationUtility.SetEditorCurve(
                    derived,
                    binding,
                    AnimationCurve.Constant(0f, 1f, value));
            }

            AnimationUtility.SetAnimationEvents(derived, Array.Empty<AnimationEvent>());
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(derived);
            settings.loopTime = true;
            settings.loopBlend = true;
            AnimationUtility.SetAnimationClipSettings(derived, settings);
            EditorUtility.SetDirty(derived);
            return derived;
        }

        private static void EnsureFolder(string path)
        {
            Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }
    }
}
