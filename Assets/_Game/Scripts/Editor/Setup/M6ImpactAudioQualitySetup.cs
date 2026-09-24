using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    public static class M6ImpactAudioQualitySetup
    {
        public const string EvidencePath = "Builds/TestResults/0.9.2-audio-measurements.json";
        private const string SourceRoot = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/10_Audio/ImpactSounds/Audio/";
        private const string DerivedRoot = "Assets/_Game/Audio/Combat/Derived";
        private static readonly string[] Fields = { "_flesh", "_metal", "_heavyFlesh", "_heavyMetal", "_guardBreak", "_execution" };
        private static readonly string[] Labels = { "LightFlesh", "LightMetal", "HeavyFlesh", "HeavyMetal", "GuardBreak", "Execution" };
        private static readonly string[] Families = { "impactSoft_medium", "impactMetal_light", "impactPunch_medium",
            "impactMetal_heavy", "impactPlate_heavy", "impactPunch_heavy" };

        [Serializable]
        public sealed class Row
        {
            public string slot, source, sourceSha256, sourceMetaSha256, selected, selectedSha256;
            public int variant, trimmedFrames;
            public ImpactAudioMeasurements.Metrics before, after;
            public float gain;
            public double compensatedRmsDbfs;
            public bool sourceUnchanged;
        }

        [Serializable]
        private sealed class Evidence
        {
            public string generatedUtc = DateTime.UtcNow.ToString("O");
            public string onsetDefinition = "First abs(sample) > 0.01 (-40 dBFS), interleaved sample index / channels.";
            public string levelDefinition = "Whole-clip RMS; gain excludes shared AudioSource volume 0.6 and spatial attenuation. Not LUFS or listening approval.";
            public string license = "Kenney Impact Sounds 1.0 / CC0; original files and importers unchanged.";
            public Row[] rows;
        }

        [MenuItem("Emberfall/Setup/Apply 0.9.2 Impact Audio Quality")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            var asset = AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>(M6CombatFeedbackSetup.AudioPath);
            if (asset == null) throw new InvalidOperationException("Create the combat feedback AudioSet first.");
            var serialized = new SerializedObject(asset);
            var slots = serialized.FindProperty("_slots");
            bool upgrade = slots.arraySize == 0;
            if (!upgrade && slots.arraySize != CombatImpactAudioSet.SlotCount)
                throw new InvalidOperationException("Incomplete authored bank; refusing to replace it.");

            // Validate the complete legacy selection before any derived file or configuration write.
            if (upgrade)
                for (int slot = 0; slot < Fields.Length; slot++)
                    if (AssetDatabase.GetAssetPath(serialized.FindProperty(Fields[slot]).objectReferenceValue) != SourceRoot + Families[slot] + "_000.ogg")
                        throw new InvalidOperationException("Custom legacy selection found. Preserve it and review the upgrade manually: " + Fields[slot]);

            var rows = new List<Row>();
            var clips = new List<AudioClip>();
            for (int slot = 0; slot < Families.Length; slot++)
            {
                if (!upgrade && slots.GetArrayElementAtIndex(slot).FindPropertyRelative("variants").arraySize != 3)
                    throw new InvalidOperationException("Authored variant count changed; review the audio evidence recipe, do not overwrite.");
                for (int variant = 0; variant < 3; variant++)
                {
                    string path = SourceRoot + Families[slot] + $"_{variant:000}.ogg";
                    var source = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    var row = new Row { slot = Labels[slot], variant = variant, source = path,
                        sourceSha256 = Hash(path), sourceMetaSha256 = Hash(path + ".meta") };
                    float[] pcm = ImpactAudioMeasurements.Read(source);
                    row.before = ImpactAudioMeasurements.Measure(pcm, source.channels, source.frequency);
                    if (row.before.onsetFrame < 0) throw new InvalidOperationException("No audible onset: " + path);
                    row.trimmedFrames = ImpactAudioMeasurements.TrimFrames(row.before);
                    string selected = row.trimmedFrames == 0 ? path : DerivedRoot + "/" + source.name + "_Onset.wav";
                    if (upgrade && row.trimmedFrames > 0) WriteDerived(selected, pcm, row.trimmedFrames, source.channels, source.frequency);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(selected);
                    if (!upgrade)
                    {
                        var authored = slots.GetArrayElementAtIndex(slot).FindPropertyRelative("variants").GetArrayElementAtIndex(variant);
                        clip = authored.FindPropertyRelative("clip").objectReferenceValue as AudioClip;
                        if (AssetDatabase.GetAssetPath(clip) != selected)
                            throw new InvalidOperationException("Custom bank selection preserved. Update the measurement recipe before auditing: " + Labels[slot]);
                        row.gain = authored.FindPropertyRelative("gain").floatValue;
                    }
                    row.selected = selected;
                    row.selectedSha256 = Hash(selected);
                    row.after = ImpactAudioMeasurements.Measure(ImpactAudioMeasurements.Read(clip), clip.channels, clip.frequency);
                    if (row.after.onsetFrame < 0 || row.after.onsetMs > 10d)
                        throw new InvalidOperationException("Selected onset gate failed: " + selected);
                    rows.Add(row); clips.Add(clip);
                }
            }

            // Attenuation only: pair Light Flesh/Metal, pair Heavy Flesh/Metal, then each special grade.
            for (int start = 0; start < rows.Count;)
            {
                int count = start < 12 ? 6 : 3;
                double target = rows.Skip(start).Take(count).Min(row => row.after.rmsDbfs);
                for (int i = start; i < start + count; i++)
                {
                    Row row = rows[i];
                    if (upgrade) row.gain = ImpactAudioMeasurements.Attenuation(row.after.rmsDbfs, target);
                    if (row.gain <= 0f || row.gain > 1f) throw new InvalidOperationException("Invalid authored gain: " + row.slot);
                    row.compensatedRmsDbfs = row.after.rmsDbfs + ImpactAudioMeasurements.Db(row.gain);
                    row.sourceUnchanged = Hash(row.source) == row.sourceSha256 && Hash(row.source + ".meta") == row.sourceMetaSha256;
                    if (!row.sourceUnchanged) throw new InvalidOperationException("Original changed: " + row.source);
                }
                double spread = rows.Skip(start).Take(count).Max(row => row.compensatedRmsDbfs) - rows.Skip(start).Take(count).Min(row => row.compensatedRmsDbfs);
                if (spread > 2d) throw new InvalidOperationException("Compensated RMS spread exceeds 2 dB.");
                start += count;
            }

            if (upgrade)
            {
                slots.arraySize = CombatImpactAudioSet.SlotCount;
                for (int slot = 0; slot < Fields.Length; slot++)
                {
                    var entry = slots.GetArrayElementAtIndex(slot);
                    entry.FindPropertyRelative("label").stringValue = Labels[slot];
                    var variants = entry.FindPropertyRelative("variants");
                    variants.arraySize = 3;
                    for (int v = 0; v < 3; v++)
                    {
                        var entryVariant = variants.GetArrayElementAtIndex(v);
                        entryVariant.FindPropertyRelative("clip").objectReferenceValue = clips[slot * 3 + v];
                        entryVariant.FindPropertyRelative("gain").floatValue = rows[slot * 3 + v].gain;
                    }
                    serialized.FindProperty(Fields[slot]).objectReferenceValue = null;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(EvidencePath));
            File.WriteAllText(EvidencePath, JsonUtility.ToJson(new Evidence { rows = rows.ToArray() }, true));
            Debug.Log($"[M6_AUDIO_QUALITY] upgrade={upgrade} clips={rows.Count} trimmed={rows.Count(r => r.trimmedFrames > 0)} sourceUnchanged=true evidence={EvidencePath} listening=pending");
        }

        public static void EnsureConfigured(CombatImpactAudioSet asset)
        {
            if (new SerializedObject(asset).FindProperty("_slots").arraySize == 0) Apply();
        }

        private static void WriteDerived(string path, float[] samples, int trimFrames, int channels, int frequency)
        {
            if (!path.StartsWith(DerivedRoot + "/", StringComparison.Ordinal) || path.Contains(".."))
                throw new InvalidOperationException("Derived path must stay under project-owned audio.");
            int offset = checked(trimFrames * channels), count = samples.Length - offset;
            Directory.CreateDirectory(DerivedRoot);
            // Content-address check makes rerunning an interrupted upgrade non-destructive to authored derivatives.
            byte[] wav;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                    writer.Write((short)1); writer.Write((short)channels); writer.Write(frequency);
                    writer.Write(frequency * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                    for (int i = offset; i < samples.Length; i++) writer.Write((short)Math.Round(Mathf.Clamp(samples[i], -1f, 1f) * 32767d));
                }
                wav = stream.ToArray();
            }
            if (File.Exists(path))
            {
                if (!File.ReadAllBytes(path).SequenceEqual(wav)) throw new InvalidOperationException("Existing derivative differs; refusing overwrite: " + path);
            }
            else File.WriteAllBytes(path, wav);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
        }

        private static string Hash(string path)
        {
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }
    }
}
