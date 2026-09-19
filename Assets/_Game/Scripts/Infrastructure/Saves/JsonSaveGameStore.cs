using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Emberfall.Infrastructure.Saves
{
    /// <summary>Versioned JSON save store with write-through temp files and recoverable replacement.</summary>
    public sealed class JsonSaveGameStore
    {
        private readonly string _savePath;

        public JsonSaveGameStore(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath))
            {
                throw new ArgumentException("Save path cannot be empty.", nameof(savePath));
            }

            _savePath = Path.GetFullPath(savePath);
        }

        public string SavePath => _savePath;
        public string PreviousPath => _savePath + ".previous";
        public string CorruptPath => _savePath + ".corrupt";

        public void DeleteAllRevisions()
        {
            DeleteIfExists(_savePath);
            DeleteIfExists(PreviousPath);
            DeleteIfExists(CorruptPath);
            DeleteIfExists(_savePath + ".tmp");
        }

        public void Save(SaveGameV1 save)
        {
            if (save == null)
            {
                throw new ArgumentNullException(nameof(save));
            }

            save.Validate();
            string directory = Path.GetDirectoryName(_savePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = _savePath + ".tmp";
            try
            {
                WriteThrough(temporaryPath, JsonUtility.ToJson(save, true));
                if (File.Exists(_savePath))
                {
                    if (File.Exists(PreviousPath))
                    {
                        File.Delete(PreviousPath);
                    }

                    File.Replace(temporaryPath, _savePath, PreviousPath, true);
                }
                else
                {
                    File.Move(temporaryPath, _savePath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        public SaveLoadResult LoadOrCreate(
            Func<SaveGameV1> createDefault,
            Action<SaveGameV1> additionalValidate = null)
        {
            if (createDefault == null)
            {
                throw new ArgumentNullException(nameof(createDefault));
            }

            if (!File.Exists(_savePath))
            {
                return new SaveLoadResult(CreateValidatedDefault(createDefault, additionalValidate), SaveLoadStatus.NewGame);
            }

            try
            {
                string json = File.ReadAllText(_savePath, Encoding.UTF8);
                SaveGameV1 save = JsonUtility.FromJson<SaveGameV1>(json);
                if (save == null)
                {
                    throw new FormatException("Save JSON produced no document.");
                }

                save.Validate();
                additionalValidate?.Invoke(save);
                return new SaveLoadResult(save, SaveLoadStatus.Loaded);
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is ArgumentException ||
                exception is FormatException ||
                exception is NotSupportedException)
            {
                PreserveCorruptSave();
                return new SaveLoadResult(
                    CreateValidatedDefault(createDefault, additionalValidate),
                    SaveLoadStatus.RecoveredCorrupt);
            }
        }

        private static SaveGameV1 CreateValidatedDefault(
            Func<SaveGameV1> createDefault,
            Action<SaveGameV1> additionalValidate)
        {
            SaveGameV1 save = createDefault() ?? throw new InvalidOperationException("Default save factory returned null.");
            save.Validate();
            additionalValidate?.Invoke(save);
            return save;
        }

        private void PreserveCorruptSave()
        {
            if (File.Exists(CorruptPath))
            {
                File.Delete(CorruptPath);
            }

            File.Move(_savePath, CorruptPath);
        }

        private static void WriteThrough(string path, string content)
        {
            using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true);
            writer.Write(content);
            writer.Flush();
            stream.Flush(true);
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
