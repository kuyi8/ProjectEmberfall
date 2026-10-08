using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Gameplay.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    // A generated copy, never a fork of combat rules. Re-sync after shared state authoring.
    public static class OfflineLocomotionSetup
    {
        public const string ControllerPath = "Assets/_Game/Art/Animations/Player/AC_Player_OfflineLocomotion.controller";

        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle Edit Mode required.");
            var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(M1AnimationSetup.AnimationSetPath);
            var shared = AssetDatabase.LoadAssetAtPath<AnimatorController>(M1AnimationSetup.ControllerPath);
            if (set == null || shared == null || set.Controller != shared || EditorUtility.IsDirty(shared))
                throw new InvalidOperationException("Saved shared controller/set required.");
            AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(M1AnimationSetup.Library1Path)
                .OfType<AnimationClip>().Single(c => c.name == name);
            var clips = new[] { Clip("Rig|Idle_Loop"), Clip("Rig|Walk_Loop"),
                Clip("Rig|Jog_Fwd_Loop"), Clip("Rig|Sprint_Loop") };
            string sourceHash;
            using (var sha = SHA256.Create()) sourceHash = BitConverter.ToString(
                sha.ComputeHash(File.ReadAllBytes(M1AnimationSetup.ControllerPath))).Replace("-", "");
            var owned = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (owned == null)
            {
                if (File.Exists(ControllerPath) || !AssetDatabase.CopyAsset(M1AnimationSetup.ControllerPath, ControllerPath))
                    throw new InvalidOperationException("Cannot create the exact owned controller.");
                owned = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            }
            else if (set.OfflineControllerSourceHash != sourceHash)
            {
                if (EditorUtility.IsDirty(owned)) throw new InvalidOperationException("Preserve unsaved owned controller.");
                // Replace only this generated payload, preserving its GUID and a recoverable prior copy.
                string backup = "Builds/ArtReview/locomotion-rebuild/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
                Directory.CreateDirectory(backup);
                File.Copy(ControllerPath, backup + "/previous.controller", false);
                File.Copy(ControllerPath + ".meta", backup + "/previous.controller.meta", false);
                File.Copy(M1AnimationSetup.ControllerPath, ControllerPath, true);
                AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate);
                owned = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            }
            var tree = owned.layers[0].stateMachine.states.Single(s => s.state.name == "Locomotion").state.motion as BlendTree;
            if (tree == null || tree.blendType != BlendTreeType.Simple1D || tree.blendParameter != "Speed")
                throw new InvalidOperationException("Unexpected locomotion graph; nothing further authored.");
            float[] thresholds = { 0f, 2.2f, 5.4f, 8.2f };
            var old = tree.children;
            bool same = old.Length == clips.Length && !tree.useAutomaticThresholds;
            for (int i = 0; same && i < old.Length; i++)
                same &= old[i].motion == clips[i] && old[i].threshold == thresholds[i] && old[i].timeScale == 1f;
            if (!same)
            {
                tree.useAutomaticThresholds = false;
                tree.children = clips.Select((clip, i) => new ChildMotion { motion = clip,
                    threshold = thresholds[i], timeScale = 1f }).ToArray();
                EditorUtility.SetDirty(tree); EditorUtility.SetDirty(owned);
                AssetDatabase.SaveAssetIfDirty(owned);
            }
            if (set.OfflineController != owned || set.OfflineControllerSourceHash != sourceHash)
            {
                set.ConfigureOfflineController(owned, sourceHash);
                EditorUtility.SetDirty(set); AssetDatabase.SaveAssetIfDirty(set);
            }
        }
    }
}
