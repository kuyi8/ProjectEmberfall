using System;
using System.Linq;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    public static class PriestProjectileAnimationSetup
    {
        // Observed source frames, not back-solved from the damage clock.
        public const float ReleaseSourceSeconds = 13f / 30f;
        public const float RecoveryEndSeconds = 1.3f;
        public const string WindupPath = M1AnimationSetup.DerivedFolder + "/A_Priest_ProjectileWindup.anim";
        public const string ReleasePath = M1AnimationSetup.DerivedFolder + "/A_Priest_ProjectileRelease.anim";

        [MenuItem("Emberfall/Setup/Apply Priest Projectile Phases")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(M1AnimationSetup.AnimationSetPath);
            var source = set.GetClip(CombatState.HeavyAttack);
            var windup = HumanoidClipDerivation.Crop(source, 0, ReleaseSourceSeconds, WindupPath);
            var release = HumanoidClipDerivation.Crop(source, ReleaseSourceSeconds, RecoveryEndSeconds, ReleasePath);
            HumanoidClipDerivation.PreserveSourceFacing(source, 0,
                "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_RunePriest.prefab", windup, release);
            var controller = (AnimatorController)set.Controller;
            SetState(controller, "PriestProjectileWindup", windup);
            SetState(controller, "PriestProjectileRelease", release);
            set.ConfigurePriestProjectile(windup, release);
            var annotations = AssetDatabase.LoadAssetAtPath<AttackTimingAnnotations>(AttackTimingAudit.AnnotationPath);
            var contact = annotations?.contacts.SingleOrDefault(c => c.actionId == "priest.projectile");
            if (contact != null && !contact.confirmed)
            {
                contact.clip = release;
                EditorUtility.SetDirty(annotations);
            }
            EditorUtility.SetDirty(set);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PRIEST_PHASES] windup={windup.length:R} release={release.length:R} sourceUnchanged={source.name}");
        }

        private static void SetState(AnimatorController controller, string name, AnimationClip clip)
        {
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.Select(s => s.state).SingleOrDefault(s => s.name == name) ?? machine.AddState(name);
            state.motion = clip;
            state.writeDefaultValues = false;
            EditorUtility.SetDirty(state);
        }

    }
}
