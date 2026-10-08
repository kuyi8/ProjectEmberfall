using System;
using System.Linq;
using Emberfall.Gameplay.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    public static class PlayerStrikeAnimationSetup
    {
        public const string SourcePath = M1AnimationSetup.DerivedFolder + "/A_Player_HeavyAttack_InPlace.anim";
        public const string HeavyPath = M1AnimationSetup.DerivedFolder + "/A_Player_HeavyAttack_Dedicated.anim";
        public const string ExecutionPath = M1AnimationSetup.DerivedFolder + "/A_Player_Execution_Dedicated.anim";

        // Independent assets/states, full source poses preserved. No crop or changes to shared states.
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(SourcePath);
            var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(M1AnimationSetup.AnimationSetPath);
            if (source == null || set == null) throw new InvalidOperationException("Missing strike source/set.");
            var controller = (AnimatorController)set.Controller;
            AnimationClip Copy(string path, string stateName, string actionId)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) { clip = UnityEngine.Object.Instantiate(source); AssetDatabase.CreateAsset(clip, path); }
                else EditorUtility.CopySerialized(source, clip);
                clip.name = System.IO.Path.GetFileNameWithoutExtension(path);
                var machine = controller.layers[0].stateMachine;
                var state = machine.states.Select(s => s.state).SingleOrDefault(s => s.name == stateName) ?? machine.AddState(stateName);
                state.motion = clip;
                state.writeDefaultValues = false;
                var annotations = AssetDatabase.LoadAssetAtPath<AttackTimingAnnotations>(AttackTimingAudit.AnnotationPath);
                var contact = annotations?.contacts.SingleOrDefault(c => c.actionId == actionId);
                if (contact != null)
                {
                    if (contact.clip != clip || contact.observedClipHash != AttackTimingAudit.ClipHash(clip))
                    { contact.confirmed = false; contact.synchronizationObserved = false; }
                    contact.clip = clip;
                    EditorUtility.SetDirty(annotations);
                }
                EditorUtility.SetDirty(clip); EditorUtility.SetDirty(state);
                return clip;
            }
            var heavy = Copy(HeavyPath, "PlayerHeavyAttack", "player.heavy");
            var execution = Copy(ExecutionPath, "PlayerExecution", "player.execution");
            set.ConfigureOfflineStrikes(heavy, execution);
            EditorUtility.SetDirty(set); EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            OfflineLocomotionSetup.Apply();
        }
    }
}
