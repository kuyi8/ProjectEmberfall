using System;
using System.IO;
using UnityEngine;

namespace Emberfall.Infrastructure.Storage
{
    public static class ApplicationStoragePaths
    {
        /// <summary>
        /// Returns Unity's persistent root, with a deterministic Windows LocalLow fallback for
        /// rare standalone launches where Unity reports an empty path during early bootstrap.
        /// </summary>
        public static string PersistentDataRoot
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(UnityEngine.Application.persistentDataPath))
                    return Path.GetFullPath(UnityEngine.Application.persistentDataPath);

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                DirectoryInfo appData = string.IsNullOrWhiteSpace(local) ? null : Directory.GetParent(local);
                if (appData != null)
                {
                    return Path.GetFullPath(Path.Combine(
                        appData.FullName,
                        "LocalLow",
                        UnityEngine.Application.companyName,
                        UnityEngine.Application.productName));
                }

                string profile = Environment.GetEnvironmentVariable("USERPROFILE");
                if (!string.IsNullOrWhiteSpace(profile))
                {
                    return Path.GetFullPath(Path.Combine(
                        profile,
                        "AppData",
                        "LocalLow",
                        UnityEngine.Application.companyName,
                        UnityEngine.Application.productName));
                }
#endif
                string playerRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
                if (!string.IsNullOrWhiteSpace(playerRoot))
                    return Path.GetFullPath(Path.Combine(playerRoot, "UserData"));
                throw new InvalidOperationException("A persistent application data root could not be resolved.");
            }
        }
    }
}
