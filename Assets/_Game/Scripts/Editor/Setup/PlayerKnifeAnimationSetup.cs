using System;
using System.Linq;
using Emberfall.Gameplay.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    public static class PlayerKnifeAnimationSetup
    {
        public const string SourcePath = M1AnimationSetup.DerivedFolder + "/A_Player_RangedAttack_InPlace.anim";
        public const string CandidatePath = M1AnimationSetup.DerivedFolder + "/A_Player_RangedAttack_Dedicated.anim";
        public const float SourceEnd = 28f / 30f;

        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(SourcePath);
            var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(M1AnimationSetup.AnimationSetPath);
            if (source == null || set == null) throw new InvalidOperationException("Missing knife source/set.");
            // Shorter presentation interval slows the forward throw within the unchanged .56s action.
            // Shared/network source and the authoritative release/origin are intentionally untouched.
            var clip = HumanoidClipDerivation.Crop(source, 0, SourceEnd, CandidatePath);
            HumanoidClipDerivation.PreserveSourceFacing(source, 0,
                "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Player_Warrior.prefab", clip);
            var controller = (AnimatorController)set.Controller;
            var machine = controller.layers[0].stateMachine;
            var state = machine.states.Select(s => s.state).SingleOrDefault(s => s.name == "PlayerRangedAttack")
                ?? machine.AddState("PlayerRangedAttack");
            state.motion = clip;
            state.writeDefaultValues = false;
            set.ConfigureOfflineRangedAttack(clip);
            var annotations = AssetDatabase.LoadAssetAtPath<AttackTimingAnnotations>(AttackTimingAudit.AnnotationPath);
            var contact = annotations?.contacts.SingleOrDefault(c => c.actionId == "player.knife");
            if (contact != null)
            {
                if (contact.clip != clip || contact.observedClipHash != AttackTimingAudit.ClipHash(clip))
                { contact.confirmed = false; contact.synchronizationObserved = false; }
                contact.clip = clip;
                EditorUtility.SetDirty(annotations);
            }
            EditorUtility.SetDirty(state); EditorUtility.SetDirty(set); EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            OfflineLocomotionSetup.Apply();
        }
    }
}
