using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    /// <summary>
    /// Editor-only presentation mapping. Only shared material references are changed on the
    /// supplied hierarchy; callers own prefab/scene saving and production approval.
    /// </summary>
    public static class CharacterPresentationPaletteSetup
    {
        public const string AssetRoot = "Assets/_Game/Art/CharacterPresentationPalette";
        public const string PalettePath = AssetRoot + "/palette.json";
        public const string MaterialRoot = AssetRoot + "/Materials";
        private const string UrpLit = "Universal Render Pipeline/Lit";
        private const string M6Materials = "Assets/_Game/Art/Materials/M6Art/";
        private const string FogwalkerMaterial = M6Materials + "M_M6_FogwalkerBone.mat";

        // Deliberately bounded to existing production character materials. Equipment, skin,
        // ScorchedCore, gameplay indicators, telegraphs and approved VFX are not in this map.
        private static readonly Dictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Bone", FogwalkerMaterial },
            { "Steel", FogwalkerMaterial },
            { "Cloth", FogwalkerMaterial },
            { "Eyes", FogwalkerMaterial },
            { "WardenMetal", M6Materials + "M_M6_WardenMetal.mat" },
            { "WardenCloth", M6Materials + "M_M6_WardenCloth.mat" },
            { "WardenRune", M6Materials + "M_M6_WardenRune.mat" },
            { "RangerOutfit", "Assets/_Game/Art/Materials/Character/M3Art/M_Ranger_Outfit.mat" },
            { "Priest", M6Materials + "M_M6_RunePriest.mat" },
            { "Scorched", M6Materials + "M_M5c_ScorchedKnight.mat" }
        };

        public static string[] SourcePaths => Sources.Values.Distinct().ToArray();

        [Serializable] public sealed class Palette
        {
            public int format;
            public Entry[] entries;
        }

        [Serializable] public sealed class Entry
        {
            public string id;
            public Color baseColor;
            public float smoothness;
            public float metallic;
            public Color emissionColor;
        }

        public static Palette ReadPalette()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(PalettePath);
            if (asset == null) throw new InvalidDataException("Missing authored character palette: " + PalettePath);
            Palette palette = JsonUtility.FromJson<Palette>(asset.text);
            if (palette == null || palette.format != 1 || palette.entries == null || palette.entries.Length != Sources.Count)
                throw new InvalidDataException("Invalid character palette format/entry count.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Entry entry in palette.entries)
            {
                if (entry == null || entry.id == null || !Sources.ContainsKey(entry.id) || !ids.Add(entry.id) ||
                    !ValidColor(entry.baseColor, 1f) || entry.baseColor.a != 1f ||
                    !FiniteRange(entry.smoothness, 0f, 1f) || !FiniteRange(entry.metallic, 0f, 1f) ||
                    !ValidColor(entry.emissionColor, 0.65f) || entry.emissionColor.a != 1f)
                    throw new InvalidDataException("Invalid/duplicate character palette entry: " + entry?.id);
                // Only the bounded eye accent can be continuously emissive. Combat/phase
                // meaning remains with existing Actor/MPB indicators, not idle body glow.
                if (entry.id != "Eyes" && HasEmission(entry.emissionColor))
                    throw new InvalidDataException("Constant emission is reserved for Eyes: " + entry.id);
            }
            return palette;
        }

        /// <summary>Creates/updates only this palette's own URP material assets, preserving GUIDs.</summary>
        public static Material[] EnsureMaterials()
        {
            RequireEditMode();
            Palette palette = ReadPalette();
            // Resolve and validate every source before creating any target assets.
            var sources = palette.entries.ToDictionary(entry => entry.id,
                entry => LoadSourceMaterial(entry.id), StringComparer.Ordinal);
            EnsureFolder(MaterialRoot);
            var result = new Material[palette.entries.Length];
            for (int i = 0; i < palette.entries.Length; i++)
            {
                Entry entry = palette.entries[i];
                Material source = sources[entry.id];
                string path = TargetPath(entry.id);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool created = material == null;
                if (created)
                {
                    if (File.Exists(path)) throw new InvalidDataException("Palette target is not a material: " + path);
                    material = new Material(source) { name = "M_CP_" + entry.id };
                    AssetDatabase.CreateAsset(material, path);
                }
                else
                {
                    if (material.shader != source.shader)
                        throw new InvalidDataException("Palette material shader changed unexpectedly: " + path);
                    ValidatePreservedTextures(source, material, path);
                }

                bool changed = SetColor(material, "_BaseColor", entry.baseColor);
                changed |= SetColor(material, "_Color", entry.baseColor);
                changed |= SetFloat(material, "_Smoothness", entry.smoothness);
                changed |= SetFloat(material, "_Metallic", entry.metallic);
                changed |= SetColor(material, "_EmissionColor", entry.emissionColor);
                bool emissive = HasEmission(entry.emissionColor);
                // URP revalidates _EMISSION from the GI black flag on import. The
                // small eye accent emits visually only: no baked/realtime GI change.
                var giFlags = emissive ? MaterialGlobalIlluminationFlags.None : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                if (material.globalIlluminationFlags != giFlags)
                {
                    material.globalIlluminationFlags = giFlags;
                    changed = true;
                }
                if (material.IsKeywordEnabled("_EMISSION") != emissive)
                {
                    if (emissive) material.EnableKeyword("_EMISSION");
                    else material.DisableKeyword("_EMISSION");
                    changed = true;
                }
                if (created || changed)
                {
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                }
                result[i] = material;
            }
            return result;
        }

        /// <summary>
        /// Applies to a temporary preview, a loaded project-owned prefab, or an explicit scene
        /// root (including legacy material overrides). Does not save the hierarchy or scene.
        /// Returns the number of renderers whose material references actually changed.
        /// </summary>
        public static int Apply(GameObject root)
        {
            ValidateHierarchy(root);
            if (root.GetComponentInChildren<Animator>(true) == null) return 0;
            return ApplyPrepared(root, EnsureMaterials());
        }

        // A migration prepares assets once, then maps many independent roots. Never
        // re-import all materials for every world prop or keep a stale global cache.
        internal static int ApplyPrepared(GameObject root, Material[] materials)
        {
            ValidateHierarchy(root);
            if (materials == null || materials.Length != Sources.Count)
                throw new ArgumentException("Prepare the complete approved palette first.", nameof(materials));
            var targets = materials.ToDictionary(material => AssetDatabase.GetAssetPath(material),
                material => material, StringComparer.Ordinal);
            int changedRenderers = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                // A source metal is also used by world braziers. Shared material identity
                // alone never grants permission to recolor non-character props.
                if (renderer.GetComponentInParent<Animator>() == null) continue;
                Material[] slots = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    string oldPath = slots[i] != null ? AssetDatabase.GetAssetPath(slots[i]) : string.Empty;
                    string id = ResolveRole(oldPath, renderer.name);
                    if (id == null) continue;
                    Material target = targets[TargetPath(id)];
                    if (slots[i] == target) continue;
                    slots[i] = target;
                    changed = true;
                }
                if (!changed) continue;
                renderer.sharedMaterials = slots;
                if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                changedRenderers++;
            }
            return changedRenderers;
        }

        private static void ValidateHierarchy(GameObject root)
        {
            RequireEditMode();
            if (root == null) throw new ArgumentNullException(nameof(root));
            // Never write Renderer references directly into an imported FBX/third-party asset.
            if (EditorUtility.IsPersistent(root))
                throw new InvalidOperationException("Instantiate or load project-owned prefab contents before mapping materials.");
        }

        public static string TargetPath(string id)
        {
            if (id == null || !Sources.ContainsKey(id)) throw new ArgumentException("Unknown palette role.", nameof(id));
            return MaterialRoot + "/M_CP_" + id + ".mat";
        }

        private static string ResolveRole(string path, string rendererName)
        {
            bool fogwalker = path == FogwalkerMaterial || path == TargetPath("Bone") ||
                path == TargetPath("Steel") || path == TargetPath("Cloth") || path == TargetPath("Eyes");
            if (fogwalker)
            {
                // Exact source Renderer names only. Do not infer semantics from arbitrary
                // substrings or rename/re-enable alternate equipment.
                if (rendererName == "Skeleton_Warrior_Helmet") return "Steel";
                if (rendererName == "Skeleton_Warrior_Cloak") return "Cloth";
                if (rendererName == "Skeleton_Warrior_Eyes") return "Eyes";
                return "Bone";
            }
            foreach (KeyValuePair<string, string> source in Sources)
            {
                if (source.Value == FogwalkerMaterial) continue;
                if (path == source.Value || path == TargetPath(source.Key)) return source.Key;
            }
            return null;
        }

        private static Material LoadSourceMaterial(string id)
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(Sources[id]);
            if (source == null || source.shader == null || source.shader.name != UrpLit ||
                !source.HasProperty("_BaseColor") || !source.HasProperty("_Smoothness") ||
                !source.HasProperty("_Metallic") || !source.HasProperty("_EmissionColor"))
                throw new InvalidDataException("Missing/incompatible source URP Lit material: " + Sources[id]);
            return source;
        }

        private static void ValidatePreservedTextures(Material source, Material target, string path)
        {
            foreach (string property in source.GetTexturePropertyNames())
            {
                if (!target.HasProperty(property) || source.GetTexture(property) != target.GetTexture(property) ||
                    source.GetTextureScale(property) != target.GetTextureScale(property) ||
                    source.GetTextureOffset(property) != target.GetTextureOffset(property))
                    throw new InvalidDataException("Palette texture/UV contract changed: " + path + " / " + property);
            }
        }

        private static bool SetColor(Material material, string property, Color value)
        {
            if (!material.HasProperty(property)) return false;
            // Unity's native colour storage/import round-trip shifts these channels by
            // < 1e-6 (measured BaseColor .34 -> .339999974, legacy .339999944).
            // Do not dirty/reimport visually identical assets at every mapping call.
            Color previous = material.GetColor(property);
            if (Mathf.Abs(previous.r - value.r) <= .000001f && Mathf.Abs(previous.g - value.g) <= .000001f &&
                Mathf.Abs(previous.b - value.b) <= .000001f && Mathf.Abs(previous.a - value.a) <= .000001f) return false;
            material.SetColor(property, value);
            return true;
        }

        private static bool SetFloat(Material material, string property, float value)
        {
            if (!material.HasProperty(property) || material.GetFloat(property).Equals(value)) return false;
            material.SetFloat(property, value);
            return true;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent)) throw new InvalidDataException("Invalid palette directory: " + path);
            EnsureFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
                throw new IOException("Could not create palette directory: " + path);
        }

        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Character palette requires an idle, non-playing Editor.");
        }

        private static bool FiniteRange(float value, float min, float max) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;

        private static bool ValidColor(Color value, float max) =>
            FiniteRange(value.r, 0f, max) && FiniteRange(value.g, 0f, max) &&
            FiniteRange(value.b, 0f, max) && FiniteRange(value.a, 0f, 1f);

        private static bool HasEmission(Color color) => color.r > 0f || color.g > 0f || color.b > 0f;
    }
}
