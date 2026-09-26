using System;
using System.Linq;
using Emberfall.Gameplay.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    public static class SweepAnimationSetup
    {
        // Observed second swing: after the uppercut, before the jumping third strike.
        // Integer source frames preserve Humanoid's baked sampling grid. The matching
        // .255s damage window starts just after natural contact, before hit stop.
        public const float SourceStart = 16f / 30f;
        public const float SourceEnd = 35f / 30f;
        public const string SourcePath = M1AnimationSetup.DerivedFolder + "/A_Player_Sweep_InPlace.anim";
        public const string CandidatePath = M1AnimationSetup.DerivedFolder + "/A_Player_Sweep_SingleSwing.anim";

        public static void Preview()
        {
            if (!UnityEngine.Application.isBatchMode) throw new InvalidOperationException("Isolated batch entry only.");
            CreateCandidate();
            AttackContactSampling.CaptureSweepCandidate();
        }

        public static void CreateCandidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(SourcePath);
            var clip = HumanoidClipDerivation.Crop(source, SourceStart, SourceEnd, CandidatePath);
            HumanoidClipDerivation.PreserveSourceFacing(source, SourceStart,
                "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Player_Warrior.prefab", clip);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SWEEP_CANDIDATE] source={source.length:R} start={SourceStart:R} end={SourceEnd:R} length={clip.length:R} wired=false");
        }

        public static void Apply()
        {
            CreateCandidate();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(CandidatePath);
            var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(M1AnimationSetup.AnimationSetPath);
            var controller = (AnimatorController)set.Controller;
            var state = controller.layers[0].stateMachine.states.Single(s => s.state.name == "Sweep").state;
            state.motion = clip;
            set.ConfigureSweep(clip);
            var annotations = AssetDatabase.LoadAssetAtPath<AttackTimingAnnotations>(AttackTimingAudit.AnnotationPath);
            var contact = annotations?.contacts.SingleOrDefault(c => c.actionId == "player.sweep");
            if (contact != null)
            {
                contact.clip = clip;
                // Preserve old evidence text/path for provenance, but changed clips require a NEW natural capture.
                if (contact.observedClipHash != AttackTimingAudit.ClipHash(clip))
                { contact.confirmed = false; contact.synchronizationObserved = false; }
                EditorUtility.SetDirty(annotations);
            }
            EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
        }

        public static void ApplyAndAudit()
        {
            Apply();
            AttackTimingAudit.Export();
        }
    }
}
