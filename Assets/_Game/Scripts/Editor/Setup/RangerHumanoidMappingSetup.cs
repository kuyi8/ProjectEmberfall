using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    /// <summary>Correct only the owned Ranger's anatomical mapping, never source models or clip keys.</summary>
    public static class RangerHumanoidMappingSetup
    {
        public const string DerivedPath = "Assets/_Game/Art/Characters/M3Art/Derived/Male_Ranger_Emberfall.fbx";

        public static HumanDescription CorrectedDescription(HumanDescription current, Transform model)
        {
            if (model == null || current.human == null)
                throw new InvalidOperationException("Existing Ranger mapping and hierarchy required.");
            Transform[] hierarchy = model.GetComponentsInChildren<Transform>(true);
            Transform control = Unique(hierarchy, "root");
            Transform pelvis = Unique(hierarchy, "pelvis");
            if (pelvis.parent != control || Unique(hierarchy, "spine_01").parent != pelvis ||
                Unique(hierarchy, "thigh_l").parent != pelvis || Unique(hierarchy, "thigh_r").parent != pelvis)
                throw new InvalidOperationException("Ranger pelvis must join the existing torso and both upper legs.");
            int[] hips = current.human.Select((h, i) => new { h, i })
                .Where(x => x.h.humanName == "Hips").Select(x => x.i).ToArray();
            if (hips.Length != 1) throw new InvalidOperationException("Exactly one existing Hips mapping required.");
            HumanBone bone = current.human[hips[0]];
            if (bone.boneName != "root" && bone.boneName != "pelvis")
                throw new InvalidOperationException("Unexpected mapping; never silently reinterpret another rig.");
            var corrected = current;
            corrected.human = (HumanBone[])current.human.Clone();
            bone.boneName = "pelvis";
            corrected.human[hips[0]] = bone;
            return corrected;
        }

        private static Transform Unique(Transform[] hierarchy, string name)
        {
            Transform[] matches = hierarchy.Where(t => t.name == name).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Unique anatomical node required: " + name);
            return matches[0];
        }

        [MenuItem("Emberfall/Presentation/Correct Owned Ranger Hips Mapping")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle Edit Mode required.");
            ApplyToImporter(DerivedPath);
        }

        internal static bool ApplyToImporter(string path)
        {
            if (!string.Equals(path, DerivedPath, StringComparison.Ordinal))
                throw new InvalidOperationException("Only the existing owned Ranger derivative is in scope.");
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (importer == null || model == null || importer.animationType != ModelImporterAnimationType.Human ||
                importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                throw new InvalidOperationException("Existing owned Human/CreateFromThisModel derivative required.");
            HumanDescription current = importer.humanDescription;
            HumanDescription corrected = CorrectedDescription(current, model.transform);
            if (current.human.Single(h => h.humanName == "Hips").boneName == "pelvis") return false;

            // Preserve the exact old owned meta before its first mapping change. Never
            // regenerate the map, all animations, scene layout, scale or floor offsets.
            string backup = "Builds/ArtReview/light-combo/ranger-mapping-20261007-a1";
            Directory.CreateDirectory(backup);
            string retained = backup + "/Male_Ranger_Emberfall.fbx.meta.before";
            if (!File.Exists(retained)) File.Copy(path + ".meta", retained, false);
            importer.humanDescription = corrected;
            importer.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().Single();
            if (!avatar.isValid || !avatar.isHuman ||
                ((ModelImporter)AssetImporter.GetAtPath(path)).humanDescription.human.Single(h => h.humanName == "Hips").boneName != "pelvis")
                throw new InvalidOperationException("Corrected Avatar did not import; owned backup retained.");
            Debug.Log("[RANGER_HIPS_MAPPING] owned anatomical Hips=pelvis; no animation/time/authority changes. Native and visible acceptance pending.");
            return true;
        }
    }
}
