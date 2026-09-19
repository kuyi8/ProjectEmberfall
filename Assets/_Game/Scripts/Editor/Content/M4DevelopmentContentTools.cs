using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emberfall.Core.Content;
using Emberfall.Core.Diagnostics;
using Emberfall.Core.Identifiers;
using Emberfall.Infrastructure.Content;
using Emberfall.Infrastructure.Diagnostics;
using Emberfall.Infrastructure.Scripting;
using Emberfall.Infrastructure.Storage;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Editor.Content
{
    [InitializeOnLoad]
    public static class M4DevelopmentContentTools
    {
        public const string LocalEndpoint = "http://127.0.0.1:18752/0.5.5/";
        private const string ServerAssetRoot = "Builds/ContentServer";
        private const string ClientVersion = "0.5.4";
        private const string PatchVersion = "0.5.5";
        private const string PrivateKeyRelativePath = "Library/EmberfallM4/dev-signing-key.json";
        private const string PublicKeyAssetPath = "Assets/StreamingAssets/ContentTrust/dev-public-key.json";
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);
        private static HttpListener _listener;
        private static CancellationTokenSource _serverCancellation;

        static M4DevelopmentContentTools()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopServer;
            EditorApplication.quitting += StopServer;
        }

        [MenuItem("Emberfall/M4/Prepare Signed 0.5.5 Development Patch")]
        public static void PrepareSignedDevelopmentPatch()
        {
            string projectRoot = GetProjectRoot();
            RSAParameters key = LoadOrCreatePrivateKey(projectRoot);
            WritePublicTrust(projectRoot, key);

            string patchRoot = Path.Combine(projectRoot, ServerAssetRoot, PatchVersion);
            if (Directory.Exists(patchRoot)) Directory.Delete(patchRoot, true);
            Directory.CreateDirectory(Path.Combine(patchRoot, "Data"));
            var files = new List<ContentPackageFile>();
            CopyData(
                projectRoot,
                patchRoot,
                "enemies.v1.json",
                files,
                text => ReplaceRequired(text, "\"maximumHealth\": 165.0", "\"maximumHealth\": 145.0"));
            CopyData(projectRoot, patchRoot, "quests.v1.json", files);
            CopyData(projectRoot, patchRoot, "texts.zh-CN.v1.json", files);
            CopyLua(
                projectRoot,
                patchRoot,
                "quest_forest_condition.lua",
                files,
                text => ReplaceRequired(
                    text,
                    "text:quest.forest-seal.ready",
                    "text:quest.forest-seal.ready-patch"));
            CopyLua(projectRoot, patchRoot, "encounter_forest_orchestration.lua", files);

            var unsigned = new ContentPackageManifest(
                1,
                new ContentId("package:emberfall.development"),
                SemanticVersion.Parse(PatchVersion),
                new SemanticVersion(0, 5, 0),
                new SemanticVersion(0, 6, 999),
                RsaContentPackageSignatureVerifier.Algorithm,
                string.Empty,
                files);
            byte[] signature;
            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(key);
                signature = rsa.SignData(
                    ContentPackageSignaturePayload.Build(unsigned),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
            }

            var signed = new ContentPackageManifest(
                unsigned.SchemaVersion,
                unsigned.PackageId,
                unsigned.ContentVersion,
                unsigned.MinClientVersion,
                unsigned.MaxClientVersion,
                unsigned.SignatureAlgorithm,
                Convert.ToBase64String(signature),
                unsigned.Files);
            File.WriteAllText(
                Path.Combine(patchRoot, "manifest.json"),
                ContentPackageManifestJson.Serialize(signed),
                Utf8WithoutBom);

            PlayerSettings.bundleVersion = ClientVersion;
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log(
                $"[M4_SIGNED_PATCH_READY] Signed patch {PatchVersion} is at '{patchRoot}'. " +
                $"The private development key remains under Library; serve it from {LocalEndpoint}.");
        }

        [MenuItem("Emberfall/M4/Start Local Content Server")]
        public static void StartServer()
        {
            if (_listener != null && _listener.IsListening)
            {
                Debug.Log($"[M4_LOCAL_SERVER_READY] Local content server is already listening at {LocalEndpoint}.");
                return;
            }

            string serverRoot = Path.Combine(GetProjectRoot(), ServerAssetRoot);
            if (!File.Exists(Path.Combine(serverRoot, PatchVersion, "manifest.json")))
                throw new FileNotFoundException("Prepare the signed development patch before starting the server.");

            _serverCancellation = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:18752/");
            _listener.Start();
            _ = Task.Run(() => ServeLoopAsync(serverRoot, _serverCancellation.Token));
            Debug.Log($"[M4_LOCAL_SERVER_READY] Serving '{serverRoot}' at {LocalEndpoint}.");
        }

        [MenuItem("Emberfall/M4/Stop Local Content Server")]
        public static void StopServer()
        {
            _serverCancellation?.Cancel();
            _serverCancellation?.Dispose();
            _serverCancellation = null;
            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                    _listener.Close();
                }
                catch (ObjectDisposedException)
                {
                }
                _listener = null;
            }
        }

        [MenuItem("Emberfall/M4/Install Local Patch For Next Launch")]
        public static async void InstallLocalPatchForNextLaunch()
        {
            try
            {
                string projectRoot = GetProjectRoot();
                string publicKeyPath = Path.Combine(projectRoot, PublicKeyAssetPath.Replace('/', Path.DirectorySeparatorChar));
                IContentPackageSignatureVerifier verifier = ContentTrustSettingsJson.DeserializeVerifier(
                    File.ReadAllText(publicKeyPath, Encoding.UTF8));
                SemanticVersion activeVersion = ContentPackageRuntime.IsInitialized
                    ? ContentPackageRuntime.Current.Snapshot.Manifest.ContentVersion
                    : SemanticVersion.Parse(ClientVersion);
                using var transport = new HttpContentPackageTransport(TimeSpan.FromSeconds(10), true);
                var updater = new ContentPackageUpdater(
                    Path.Combine(ApplicationStoragePaths.PersistentDataRoot, "Content"),
                    SemanticVersion.Parse(PlayerSettings.bundleVersion),
                    activeVersion,
                    transport,
                    verifier,
                    new UnityGameLogger(),
                    preflight: new GameContentPackagePreflight());
                ContentUpdateResult result = await updater.DownloadAndActivateAsync(
                    new Uri(LocalEndpoint),
                    CancellationToken.None);
                Debug.Log($"[M4_LOCAL_INSTALL_RESULT] {result.Status}: {result.Message}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public static void ValidateLocalPipeline()
        {
            string projectRoot = GetProjectRoot();
            string sandboxRoot = Path.Combine(
                projectRoot,
                "Builds",
                "Windows",
                PatchVersion,
                "Verification",
                "LocalContentSandbox");
            if (Directory.Exists(sandboxRoot)) Directory.Delete(sandboxRoot, true);

            StartServer();
            try
            {
                string publicKeyPath = Path.Combine(projectRoot, PublicKeyAssetPath.Replace('/', Path.DirectorySeparatorChar));
                IContentPackageSignatureVerifier verifier = ContentTrustSettingsJson.DeserializeVerifier(
                    File.ReadAllText(publicKeyPath, Encoding.UTF8));
                using var transport = new HttpContentPackageTransport(TimeSpan.FromSeconds(10), true);
                var updater = new ContentPackageUpdater(
                    sandboxRoot,
                    SemanticVersion.Parse(ClientVersion),
                    SemanticVersion.Parse(ClientVersion),
                    transport,
                    verifier,
                    new UnityGameLogger(),
                    preflight: new GameContentPackagePreflight());
                ContentUpdateResult result = updater.DownloadAndActivateAsync(
                    new Uri(LocalEndpoint),
                    CancellationToken.None).GetAwaiter().GetResult();
                if (result.Status != ContentUpdateStatus.ActivatedForNextLaunch)
                    throw new InvalidOperationException($"Local content pipeline validation failed: {result.Message}");
                Debug.Log(
                    $"[M4_LOCAL_PIPELINE_VALID] HTTP download, RSA verification, staging install and current activation passed for {result.Version}.");
            }
            finally
            {
                StopServer();
            }
        }

        public static void InstallLocalPatchForPlayerVerification()
        {
            StartServer();
            try
            {
                string projectRoot = GetProjectRoot();
                string publicKeyPath = Path.Combine(projectRoot, PublicKeyAssetPath.Replace('/', Path.DirectorySeparatorChar));
                IContentPackageSignatureVerifier verifier = ContentTrustSettingsJson.DeserializeVerifier(
                    File.ReadAllText(publicKeyPath, Encoding.UTF8));
                using var transport = new HttpContentPackageTransport(TimeSpan.FromSeconds(10), true);
                var updater = new ContentPackageUpdater(
                    Path.Combine(ApplicationStoragePaths.PersistentDataRoot, "Content"),
                    SemanticVersion.Parse(ClientVersion),
                    SemanticVersion.Parse(ClientVersion),
                    transport,
                    verifier,
                    new UnityGameLogger(),
                    preflight: new GameContentPackagePreflight());
                ContentUpdateResult result = updater.DownloadAndActivateAsync(
                    new Uri(LocalEndpoint),
                    CancellationToken.None).GetAwaiter().GetResult();
                if (!result.IsSuccess)
                    throw new InvalidOperationException($"Player patch installation failed: {result.Message}");
                Debug.Log($"[M4_PLAYER_PATCH_READY] {result.Status}: {result.Message}");
            }
            finally
            {
                StopServer();
            }
        }

        private static async Task ServeLoopAsync(string serverRoot, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception exception) when (exception is HttpListenerException || exception is ObjectDisposedException)
                {
                    return;
                }

                try
                {
                    ServeFile(serverRoot, context);
                }
                catch
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                }
                finally
                {
                    context.Response.Close();
                }
            }
        }

        private static void ServeFile(string serverRoot, HttpListenerContext context)
        {
            if (!string.Equals(context.Request.HttpMethod, "GET", StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            string relative = Uri.UnescapeDataString(context.Request.Url?.AbsolutePath ?? string.Empty)
                .TrimStart('/')
                .Replace('/', Path.DirectorySeparatorChar);
            string fullRoot = Path.GetFullPath(serverRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(serverRoot, relative));
            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentLength64 = bytes.LongLength;
            context.Response.ContentType = fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? "application/json; charset=utf-8"
                : "application/octet-stream";
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        private static RSAParameters LoadOrCreatePrivateKey(string projectRoot)
        {
            string path = Path.Combine(projectRoot, PrivateKeyRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                RSAParameters existing = DeserializePrivateKey(File.ReadAllText(path, Encoding.UTF8));
                if (existing.Modulus != null && existing.Modulus.Length >= 256) return existing;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            RSAParameters key;
            using (RSA rsa = new RSACryptoServiceProvider(2048))
            {
                key = rsa.ExportParameters(true);
            }
            File.WriteAllText(path, SerializePrivateKey(key), Utf8WithoutBom);
            return key;
        }

        private static void WritePublicTrust(string projectRoot, RSAParameters key)
        {
            string path = Path.Combine(projectRoot, PublicKeyAssetPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                ContentTrustSettingsJson.Serialize(
                    Convert.ToBase64String(key.Modulus),
                    Convert.ToBase64String(key.Exponent)),
                Utf8WithoutBom);
        }

        private static void CopyData(
            string projectRoot,
            string patchRoot,
            string fileName,
            ICollection<ContentPackageFile> files,
            Func<string, string> transform = null)
        {
            string source = Path.Combine(projectRoot, "Assets", "_Game", "Data", "M2", fileName);
            byte[] bytes = transform == null
                ? File.ReadAllBytes(source)
                : Utf8WithoutBom.GetBytes(transform(File.ReadAllText(source, Encoding.UTF8)));
            string packagePath = $"Data/{fileName}";
            File.WriteAllBytes(Path.Combine(patchRoot, "Data", fileName), bytes);
            files.Add(new ContentPackageFile(
                packagePath,
                ContentFileKind.Json,
                bytes.LongLength,
                ContentPackageValidator.ComputeSha256Hex(bytes)));
        }

        private static void CopyLua(
            string projectRoot,
            string patchRoot,
            string fileName,
            ICollection<ContentPackageFile> files,
            Func<string, string> transform = null)
        {
            string source = Path.Combine(projectRoot, "Assets", "_Game", "Data", "M4", "Scripts", fileName);
            byte[] bytes = transform == null
                ? File.ReadAllBytes(source)
                : Utf8WithoutBom.GetBytes(transform(File.ReadAllText(source, Encoding.UTF8)));
            string packagePath = $"Scripts/{fileName}";
            string destination = Path.Combine(patchRoot, "Scripts", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? patchRoot);
            File.WriteAllBytes(destination, bytes);
            files.Add(new ContentPackageFile(
                packagePath,
                ContentFileKind.Lua,
                bytes.LongLength,
                ContentPackageValidator.ComputeSha256Hex(bytes)));
        }

        private static string ReplaceRequired(string source, string oldValue, string newValue)
        {
            string transformed = source.Replace(oldValue, newValue);
            if (string.Equals(source, transformed, StringComparison.Ordinal))
                throw new InvalidOperationException($"Development patch transform could not find '{oldValue}'.");
            return transformed;
        }

        private static string GetProjectRoot() =>
            Directory.GetParent(UnityEngine.Application.dataPath)?.FullName ??
            throw new InvalidOperationException("Project root could not be resolved.");

        private static string SerializePrivateKey(RSAParameters key) => JsonUtility.ToJson(new PrivateKeyDto(key), true);

        private static RSAParameters DeserializePrivateKey(string json)
        {
            PrivateKeyDto dto = JsonUtility.FromJson<PrivateKeyDto>(json) ??
                                throw new FormatException("Development private key is malformed.");
            return dto.ToParameters();
        }

        [Serializable]
        private sealed class PrivateKeyDto
        {
            public string modulus;
            public string exponent;
            public string d;
            public string p;
            public string q;
            public string dp;
            public string dq;
            public string inverseQ;

            public PrivateKeyDto()
            {
            }

            public PrivateKeyDto(RSAParameters key)
            {
                modulus = Convert.ToBase64String(key.Modulus);
                exponent = Convert.ToBase64String(key.Exponent);
                d = Convert.ToBase64String(key.D);
                p = Convert.ToBase64String(key.P);
                q = Convert.ToBase64String(key.Q);
                dp = Convert.ToBase64String(key.DP);
                dq = Convert.ToBase64String(key.DQ);
                inverseQ = Convert.ToBase64String(key.InverseQ);
            }

            public RSAParameters ToParameters() => new RSAParameters
            {
                Modulus = Convert.FromBase64String(modulus),
                Exponent = Convert.FromBase64String(exponent),
                D = Convert.FromBase64String(d),
                P = Convert.FromBase64String(p),
                Q = Convert.FromBase64String(q),
                DP = Convert.FromBase64String(dp),
                DQ = Convert.FromBase64String(dq),
                InverseQ = Convert.FromBase64String(inverseQ)
            };
        }
    }
}
