using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Infrastructure.Content;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Editor.Content
{
    public static class M4BuiltinContentPackager
    {
        private const string TargetAssetRoot = "Assets/StreamingAssets/BuiltinContent";
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        private static readonly SourceFile[] Sources =
        {
            new SourceFile("Assets/_Game/Data/M2/enemies.v1.json", "Data/enemies.v1.json", ContentFileKind.Json),
            new SourceFile("Assets/_Game/Data/M2/quests.v1.json", "Data/quests.v1.json", ContentFileKind.Json),
            new SourceFile("Assets/_Game/Data/M2/texts.zh-CN.v1.json", "Data/texts.zh-CN.v1.json", ContentFileKind.Json),
            new SourceFile(
                "Assets/_Game/Data/M4/Scripts/quest_forest_condition.lua",
                "Scripts/quest_forest_condition.lua",
                ContentFileKind.Lua),
            new SourceFile(
                "Assets/_Game/Data/M4/Scripts/encounter_forest_orchestration.lua",
                "Scripts/encounter_forest_orchestration.lua",
                ContentFileKind.Lua)
        };

        [MenuItem("Emberfall/M4/Build Built-in Content Package")]
        public static void BuildBuiltinPackage()
        {
            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ??
                                 throw new InvalidOperationException("Project root could not be resolved.");

            string targetRoot = Path.Combine(projectRoot, TargetAssetRoot.Replace('/', Path.DirectorySeparatorChar));
            // Read and validate ALL authored sources before replacing any selected bytes.
            // Preserve existing Unity GUIDs and a recoverable previous package, not DeleteAsset(root).
            var payloads = new byte[Sources.Length][];
            var entries = new List<ContentPackageFile>(Sources.Length);

            for (int i = 0; i < Sources.Length; i++)
            {
                SourceFile source = Sources[i];
                string sourcePath = Path.Combine(projectRoot, source.AssetPath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(sourcePath)) throw new FileNotFoundException("Authored content source is missing.", sourcePath);

                byte[] bytes = File.ReadAllBytes(sourcePath);
                payloads[i] = bytes;
                entries.Add(new ContentPackageFile(
                    source.PackagePath,
                    source.Kind,
                    bytes.LongLength,
                    ContentPackageValidator.ComputeSha256Hex(bytes)));
            }

            var manifest = new ContentPackageManifest(
                ContentPackageValidator.SupportedSchemaVersion,
                new ContentId("package:emberfall.builtin"),
                new SemanticVersion(0, 8, 13),
                new SemanticVersion(0, 5, 0),
                new SemanticVersion(0, 9, 999),
                "builtin",
                "embedded-trusted-content",
                entries);
            var snapshotFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            for (int i = 0; i < Sources.Length; i++) snapshotFiles.Add(Sources[i].PackagePath, payloads[i]);
            if (!new GameContentPackagePreflight().Validate(new ContentPackageSnapshot(manifest, snapshotFiles), out string failure))
                throw new InvalidOperationException(failure);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Content authoring refuses an active Player session.");
            string backupRoot = Path.Combine(projectRoot, "Builds", "ContentBackups", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            if (Directory.Exists(targetRoot))
            {
                foreach (string old in Directory.GetFiles(targetRoot, "*", SearchOption.AllDirectories))
                {
                    string backup = Path.Combine(backupRoot, old.Substring(targetRoot.Length + 1));
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    File.Copy(old, backup, false);
                }
            }
            for (int i = 0; i < Sources.Length; i++)
            {
                string destination = Path.Combine(targetRoot, Sources[i].PackagePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.WriteAllBytes(destination, payloads[i]);
            }
            File.WriteAllText(
                Path.Combine(targetRoot, "manifest.json"),
                ContentPackageManifestJson.Serialize(manifest),
                Utf8WithoutBom);

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log($"[M4_CONTENT_PACKAGE_READY] Built-in package {manifest.ContentVersion} contains {entries.Count} validated files.");
        }

        private readonly struct SourceFile
        {
            public SourceFile(string assetPath, string packagePath, ContentFileKind kind)
            {
                AssetPath = assetPath;
                PackagePath = packagePath;
                Kind = kind;
            }

            public string AssetPath { get; }
            public string PackagePath { get; }
            public ContentFileKind Kind { get; }
        }
    }
}
