using System;
using Emberfall.AI.Unity;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Project-owned derivatives. Never writes source skeleton/weapon/shared palette assets.</summary>
    public static class SummonedMinionArtSetup
    {
        public const string Folder = "Assets/_Game/Art/SummonedSpirit";
        public const string StylePath = Folder + "/SummonedSpiritStyle.asset";
        public const string SourcePrefab = "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_FogwalkerSkeleton.prefab";

        public static SummonedMinionStyle EnsureAssets()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SummonedMinionStyle>(StylePath);
            if (existing != null)
            {
                if (!existing.IsValid) throw new InvalidOperationException("Existing spirit style is incomplete; inspect rather than overwrite it.");
                return existing;
            }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Game/Art", "SummonedSpirit");
            foreach (string name in new[] { "M_SpiritBone.mat", "M_SpiritSplinter.mat", "SpiritSplinter.asset", "P_SummonedSpirit.prefab" })
                if (AssetDatabase.LoadMainAssetAtPath(Folder + "/" + name) != null)
                    throw new InvalidOperationException("Partial art output preserved; inspect " + name);
            Material source = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/CharacterPresentationPalette/Materials/M_CP_Bone.mat");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
            if (source == null || prefab == null) throw new InvalidOperationException("Missing reviewed local skeleton assets.");
            var body = new Material(source) { name = "M_SpiritBone" };
            body.SetColor("_BaseColor", new Color(.54f, .39f, .78f));
            body.SetColor("_Color", new Color(.54f, .39f, .78f));
            body.EnableKeyword("_EMISSION"); body.SetColor("_EmissionColor", new Color(.08f, .025f, .16f));
            AssetDatabase.CreateAsset(body, Folder + "/M_SpiritBone.mat");
            var splinter = new Material(source) { name = "M_SpiritSplinter" };
            splinter.SetColor("_BaseColor", new Color(.73f, .48f, .95f));
            splinter.SetColor("_Color", new Color(.73f, .48f, .95f));
            splinter.EnableKeyword("_EMISSION"); splinter.SetColor("_EmissionColor", new Color(.8f, .32f, 1.4f));
            AssetDatabase.CreateAsset(splinter, Folder + "/M_SpiritSplinter.mat");
            var mesh = new Mesh { name = "SpiritSplinter" };
            // Uneven jagged splinter, not the symmetrical diamond used by interaction markers.
            mesh.vertices = new[] { new Vector3(.15f,.5f,0), new Vector3(-.1f,-.5f,.02f),
                new Vector3(-.5f,-.1f,0), new Vector3(0,-.18f,.4f), new Vector3(.5f,-.05f,0), new Vector3(0,-.08f,-.4f) };
            mesh.triangles = new[] { 0,3,2, 0,4,3, 0,5,4, 0,2,5, 1,2,3, 1,3,4, 1,4,5, 1,5,2 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, Folder + "/SpiritSplinter.asset");
            GameObject root = null;
            GameObject derived;
            try
            {
                root = Object.Instantiate(prefab); root.name = "P_SummonedSpirit";
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    // Hide derivative accessories only, keeping the full imported rig and source unchanged.
                    if (renderer.name.EndsWith("_Helmet", StringComparison.Ordinal) || renderer.name.EndsWith("_Cloak", StringComparison.Ordinal))
                        renderer.gameObject.SetActive(false);
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) if (materials[i] == source) materials[i] = body;
                    renderer.sharedMaterials = materials;
                }
                derived = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/P_SummonedSpirit.prefab");
            }
            finally { if (root != null) Object.DestroyImmediate(root); }
            if (derived == null) throw new InvalidOperationException("Spirit derivative prefab save failed.");
            var style = ScriptableObject.CreateInstance<SummonedMinionStyle>();
            var serialized = new SerializedObject(style);
            serialized.FindProperty("_visualPrefab").objectReferenceValue = derived;
            serialized.FindProperty("_fragmentMesh").objectReferenceValue = mesh;
            serialized.FindProperty("_fragmentMaterial").objectReferenceValue = splinter;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(style, StylePath); AssetDatabase.SaveAssets();
            return style;
        }
    }
}
