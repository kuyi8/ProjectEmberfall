using System;
using System.Collections;
using System.IO;
using System.Text;
using Emberfall.Core.Content;
using Emberfall.Core.Diagnostics;
using Emberfall.Infrastructure.Content;
using Emberfall.Infrastructure.Diagnostics;
using Emberfall.Infrastructure.Scenes;
using Emberfall.Infrastructure.Scripting;
using Emberfall.Infrastructure.Storage;
using UnityEngine;

namespace Emberfall.Infrastructure.Bootstrap
{
    [DefaultExecutionOrder(-10000)]
    public sealed class AppBootstrap : MonoBehaviour
    {
        private static AppBootstrap _current;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private IGameLogger _logger;
        private bool _startedInBootstrapScene;

        public static bool IsInitialized => _current != null;

        private void Awake()
        {
            if (_current != null && _current != this)
            {
                Destroy(gameObject);
                return;
            }

            _startedInBootstrapScene = gameObject.scene.name == SceneNames.Bootstrap;
            _current = this;
            DontDestroyOnLoad(gameObject);

            _logger = new UnityGameLogger();
            _logger.Log(GameLogLevel.Info, "Project Emberfall application bootstrap initialized.");
            InitializeContentPackage();
        }

        private void InitializeContentPackage()
        {
            if (ContentPackageRuntime.IsInitialized) return;

            SemanticVersion clientVersion;
            if (!SemanticVersion.TryParse(UnityEngine.Application.version, out clientVersion))
            {
                clientVersion = new SemanticVersion(0, 5, 0);
                _logger.Log(
                    GameLogLevel.Warning,
                    $"Application.version '{UnityEngine.Application.version}' is not semantic; using {clientVersion} for content compatibility.");
            }

            try
            {
                string builtinRoot = Path.Combine(UnityEngine.Application.streamingAssetsPath, "BuiltinContent");
                string localContentRoot = ResolveLocalContentRoot();
                _logger.Log(
                    GameLogLevel.Info,
                    $"Content roots: builtin='{builtinRoot}', local='{localContentRoot}'.");
                IContentPackageSignatureVerifier signatureVerifier = LoadContentSignatureVerifier();
                var resolver = new LocalContentPackageResolver(
                    builtinRoot,
                    localContentRoot,
                    clientVersion,
                    _logger,
                    signatureVerifier,
                    preflight: new GameContentPackagePreflight());
                ContentPackageSelection selection = resolver.Resolve();
                ContentPackageRuntime.Initialize(selection);
                _logger.Log(
                    GameLogLevel.Info,
                    $"Content bootstrap selected {selection.Snapshot.Manifest.ContentVersion} ({selection.Source}). " +
                    "The immutable package snapshot is the sole runtime authority for JSON and restricted Lua content.");
            }
            catch (Exception exception)
            {
                _logger.Log(
                    GameLogLevel.Error,
                    "Built-in content package could not be initialized; gameplay content will fail closed.",
                    exception);
            }
        }

        private static string ResolveLocalContentRoot()
        {
            const string prefix = "-emberfall-content-root=";
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string value = argument.Substring(prefix.Length);
                    if (string.IsNullOrWhiteSpace(value))
                        throw new ArgumentException("The content root command-line override is empty.");
                    return Path.GetFullPath(value);
                }
            }
            return Path.Combine(ApplicationStoragePaths.PersistentDataRoot, "Content");
        }

        private IContentPackageSignatureVerifier LoadContentSignatureVerifier()
        {
            string trustPath = Path.Combine(
                UnityEngine.Application.streamingAssetsPath,
                "ContentTrust",
                "dev-public-key.json");
            if (!File.Exists(trustPath))
            {
                _logger.Log(
                    GameLogLevel.Warning,
                    "No content trust public key is packaged; local patches will be rejected and built-in content remains available.");
                return null;
            }

            string json = StrictUtf8.GetString(File.ReadAllBytes(trustPath));
            return ContentTrustSettingsJson.DeserializeVerifier(json);
        }

        private IEnumerator Start()
        {
            if (!_startedInBootstrapScene)
            {
                yield break;
            }

            var sceneFlow = new SceneFlowService();
            yield return sceneFlow.LoadAsync(SceneNames.MainMenu);
        }

        private void OnDestroy()
        {
            if (_current == this)
            {
                _current = null;
            }
        }
    }
}
