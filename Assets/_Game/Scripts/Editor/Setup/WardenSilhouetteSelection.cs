using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    /// <summary>Selected same-Avatar presentation references only. Existing owned
    /// meshes/materials must already have been prepared and reviewed. No asset creation,
    /// prefab/scene save, transforms, components, material property or gameplay writes.</summary>
    public static class WardenSilhouetteSelection
    {
        public const string WardenCharacterName = "P_M6_Boss_EmberWarden";
        public const string CanonicalSwordName = "P_M6_Warden_RuneSword";
        public const string OriginalBladePath = "Assets/_Game/Art/Materials/M6Art/M_M6_WardenBlade.mat";
        public const string SelectedBladePath = CharacterPresentationPaletteSetup.MaterialRoot + "/M_CP_WardenMetal.mat";

        [Serializable] public sealed class SourceDescription
        {
            public string helmetSource = WardenSilhouetteSetup.HelmetSourcePath;
            public string swordSource = WardenSilhouetteSetup.SwordSourcePath;
            public string originalHelmet = WardenSilhouetteSetup.ReferenceHelmetPath;
            public string originalSword = WardenSilhouetteSetup.ReferenceSwordPath;
            public string selectedHelmet = WardenSilhouetteSetup.HelmetMeshPath;
            public string selectedSword = WardenSilhouetteSetup.SwordMeshPath;
            public string originalBlade = OriginalBladePath;
            public string selectedBlade = SelectedBladePath;
            public string license = WardenSilhouetteSetup.LicensePath;
            public float swordSourceYaw = WardenSilhouetteSetup.SwordSourceYaw;
            public string scope = "Existing same-Avatar static meshes and sword slot 0 only; no natural contact, network runtime, frame performance or gameplay acceptance.";
        }
        public sealed class Result
        {
            public int meshChanges, materialChanges;
            public string[] matchedPaths = new string[0];
            public SourceDescription source = new SourceDescription();
        }

        public static Result Apply(GameObject root)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Selected Warden presentation requires idle Edit Mode.");
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (EditorUtility.IsPersistent(root)) throw new InvalidOperationException("Instantiate or load owned prefab contents before selection; source assets cannot be changed.");

            var result = new Result();
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            Transform helmetRoot = Unique(transforms, WardenSilhouetteSetup.HelmetNode);
            MeshFilter helmet = helmetRoot == null ? null : SingleFilter(helmetRoot);
            Transform weaponRoot = Unique(transforms, WardenSilhouetteSetup.SwordNode);
            if (weaponRoot == null)
            {
                UnityEngine.Object original = PrefabUtility.GetCorrespondingObjectFromSource(root);
                if (root.name == CanonicalSwordName || (original != null && AssetDatabase.GetAssetPath(original) == WardenSilhouetteSetup.CanonicalWeaponPath))
                    weaponRoot = root.transform;
            }
            MeshFilter sword = null;
            if (weaponRoot != null)
            {
                Transform model = weaponRoot.Find("Model");
                if (model == null) throw new InvalidDataException("Missing exact Warden sword Model child.");
                sword = SingleFilter(model);
            }
            if (helmet == null && sword == null) return result;

            // Validate EVERY target before changing a single mesh/material reference.
            // Exact sources prevent similarly named unrelated props being adapted.
            if (helmet != null) ValidateTarget(helmet, 1, WardenSilhouetteSetup.ReferenceHelmetPath, WardenSilhouetteSetup.HelmetMeshPath);
            if (sword != null) ValidateTarget(sword, 3, WardenSilhouetteSetup.ReferenceSwordPath, WardenSilhouetteSetup.SwordMeshPath);
            Material selectedBlade = null;
            if (sword != null)
            {
                string first = AssetDatabase.GetAssetPath(sword.GetComponent<MeshRenderer>().sharedMaterials[0]);
                if (first != OriginalBladePath && first != SelectedBladePath)
                    throw new InvalidDataException("Unknown Warden sword first material slot: " + first);
                selectedBlade = AssetDatabase.LoadAssetAtPath<Material>(SelectedBladePath);
                if (selectedBlade == null || selectedBlade.shader == null || selectedBlade.shader.name != "Universal Render Pipeline/Lit")
                    throw new InvalidDataException("Prepare/review the existing URP Warden metal material first: " + SelectedBladePath);
            }
            var prepared = new WardenSilhouetteSetup.PreparedMeshes {
                helmet = RequiredMesh(WardenSilhouetteSetup.HelmetMeshPath, 610, 1),
                sword = RequiredMesh(WardenSilhouetteSetup.SwordMeshPath, 417, 3)
            };
            WardenSilhouetteSetup.ApplyResult mapped = WardenSilhouetteSetup.ApplyPrepared(root, prepared);
            result.meshChanges = mapped.changedMeshFilters;
            result.matchedPaths = mapped.matchedFilters;
            if (sword != null)
            {
                MeshRenderer renderer = sword.GetComponent<MeshRenderer>();
                Material[] slots = renderer.sharedMaterials;
                if (slots[0] != selectedBlade)
                {
                    slots[0] = selectedBlade;
                    renderer.sharedMaterials = slots;
                    if (PrefabUtility.IsPartOfPrefabInstance(renderer)) PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    result.materialChanges = 1;
                }
            }
            return result;
        }

        static Transform Unique(Transform[] transforms, string name)
        {
            Transform[] found = transforms.Where(t => t.name == name).ToArray();
            if (found.Length > 1) throw new InvalidDataException("Ambiguous exact Warden presentation node: " + name);
            return found.Length == 0 ? null : found[0];
        }
        static MeshFilter SingleFilter(Transform transform)
        {
            MeshFilter[] filters = transform.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToArray();
            if (filters.Length != 1) throw new InvalidDataException("Expected one existing static mesh under " + transform.name);
            return filters[0];
        }
        static void ValidateTarget(MeshFilter filter, int slots, string original, string selected)
        {
            string meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if ((meshPath != original && meshPath != selected) || renderer == null || renderer.sharedMaterials.Length != slots)
                throw new InvalidDataException("Unknown Warden mesh source/material-slot contract at " + filter.name + ": " + meshPath);
        }
        static Mesh RequiredMesh(string path, int vertices, int slots)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null || mesh.vertexCount != vertices || mesh.subMeshCount != slots)
                throw new InvalidDataException("Prepare/review the selected owned Warden mesh first: " + path);
            return mesh;
        }
    }
}
