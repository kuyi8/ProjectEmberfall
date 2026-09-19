using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Infrastructure.Content;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    internal static class M4ContentTestFactory
    {
        internal const string QuestScriptPath = "Scripts/quest_forest_condition.lua";
        internal const string EncounterScriptPath = "Scripts/encounter_forest_orchestration.lua";

        internal static Dictionary<string, byte[]> LoadAuthoredFiles()
        {
            string root = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ??
                          throw new InvalidOperationException("Project root is unavailable.");
            return new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [RuntimeContentPaths.Enemies] = Read(root, "Assets/_Game/Data/M2/enemies.v1.json"),
                [RuntimeContentPaths.Quests] = Read(root, "Assets/_Game/Data/M2/quests.v1.json"),
                [RuntimeContentPaths.SimplifiedChineseTexts] = Read(root, "Assets/_Game/Data/M2/texts.zh-CN.v1.json"),
                [QuestScriptPath] = Read(root, "Assets/_Game/Data/M4/Scripts/quest_forest_condition.lua"),
                [EncounterScriptPath] = Read(root, "Assets/_Game/Data/M4/Scripts/encounter_forest_orchestration.lua")
            };
        }

        internal static ContentPackageSnapshot CreateSnapshot(
            SemanticVersion version,
            Dictionary<string, byte[]> files = null,
            string signatureAlgorithm = "builtin",
            string signature = "trusted")
        {
            files ??= LoadAuthoredFiles();
            var descriptors = new List<ContentPackageFile>(files.Count);
            foreach (KeyValuePair<string, byte[]> pair in files)
            {
                descriptors.Add(new ContentPackageFile(
                    pair.Key,
                    pair.Key.EndsWith(".lua", StringComparison.Ordinal) ? ContentFileKind.Lua : ContentFileKind.Json,
                    pair.Value.LongLength,
                    ContentPackageValidator.ComputeSha256Hex(pair.Value)));
            }
            descriptors.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
            return new ContentPackageSnapshot(
                new ContentPackageManifest(
                    1,
                    new ContentId("package:m4-tests"),
                    version,
                    new SemanticVersion(0, 5, 0),
                    new SemanticVersion(0, 6, 999),
                    signatureAlgorithm,
                    signature,
                    descriptors),
                files);
        }

        internal static void WritePackage(string root, ContentPackageSnapshot snapshot)
        {
            Directory.CreateDirectory(root);
            for (int i = 0; i < snapshot.Manifest.Files.Count; i++)
            {
                ContentPackageFile file = snapshot.Manifest.Files[i];
                string destination = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? root);
                File.WriteAllBytes(destination, snapshot.GetBytes(file.Path));
            }
            File.WriteAllText(
                Path.Combine(root, "manifest.json"),
                ContentPackageManifestJson.Serialize(snapshot.Manifest),
                new UTF8Encoding(false));
        }

        internal static void ReplaceText(
            Dictionary<string, byte[]> files,
            string path,
            string oldValue,
            string newValue)
        {
            string source = Encoding.UTF8.GetString(files[path]);
            string changed = source.Replace(oldValue, newValue);
            if (string.Equals(source, changed, StringComparison.Ordinal))
                throw new InvalidOperationException($"Test mutation could not find '{oldValue}'.");
            files[path] = new UTF8Encoding(false).GetBytes(changed);
        }

        private static byte[] Read(string root, string relativePath) =>
            File.ReadAllBytes(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
