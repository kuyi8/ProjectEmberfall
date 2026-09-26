using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Emberfall.Editor.Setup
{
    /// <summary>Idempotent presentation-only authoring; never rebuilds geometry or gameplay.</summary>
    public static class M6VisualFoundationSetup
    {
        public const string ProfilePath = "Assets/_Game/Settings/PP_Emberfall_Global.asset";
        public const string RendererPath = "Assets/_Game/Settings/URP_PC_Renderer.asset";
        public const string PipelinePath = "Assets/_Game/Settings/URP_PC.asset";
        public const string PlayerPath = "Assets/_Game/Resources/Networking/P_M5_NetworkGymPlayer.prefab";
        public const string VolumeName = "[Presentation] Emberfall Global Volume";
        private const string Package = "Packages/com.unity.render-pipelines.universal";

        // Look-dev builds consume existing assets verbatim: no Apply(), no shared-asset toggling.
        public static void BuildStyleLabPlayers() => BuildDiagnosticPlayers("StyleLab-Development", "StyleLab-ReleaseGuard");

        // Preserve accepted Release and authored assets while verifying Development-only timing code.
        public static void BuildPerformanceProbePlayers() => BuildDiagnosticPlayers("0.9.0-Development", "Performance-ReleaseGuard");
        public static void BuildCombatPerformancePlayers() => BuildDiagnosticPlayers("0.9.2-Performance-Development", "0.9.2-Performance-ReleaseGuard");
        public static void BuildPriestTimingPlayers()
        {
            BuildDiagnosticPlayers("0.9.3-Priest-Development", "0.9.3-priest");
            var cleanup = Emberfall.Infrastructure.Build.BuildArtifactCleaner.RemoveDoNotShipDirectories(
                System.IO.Path.GetFullPath("Builds/Windows/0.9.3-priest"));
            Debug.Log($"[PRIEST_RELEASE_READY] bytes={cleanup.BytesAfter} files={cleanup.FilesAfter} removedDoNotShip={cleanup.RemovedDirectories}");
        }

        public static void BuildSweepTimingPlayers()
        {
            BuildDiagnosticPlayers("0.9.3-Sweep-Development", "0.9.3-sweep");
            var cleanup = Emberfall.Infrastructure.Build.BuildArtifactCleaner.RemoveDoNotShipDirectories(
                System.IO.Path.GetFullPath("Builds/Windows/0.9.3-sweep"));
            Debug.Log($"[SWEEP_RELEASE_READY] bytes={cleanup.BytesAfter} files={cleanup.FilesAfter} removedDoNotShip={cleanup.RemovedDirectories}");
        }

        public static void BuildSceneBoundsPlayers()
        {
            BuildDiagnosticPlayers("0.9.2-Bounds-Development", "0.9.2-bounds");
            // Existing accepted scene assets are consumed verbatim. Clean only this new release output.
            var cleanup = Emberfall.Infrastructure.Build.BuildArtifactCleaner.RemoveDoNotShipDirectories(
                System.IO.Path.GetFullPath("Builds/Windows/0.9.2-bounds"));
            Debug.Log($"[BOUNDS_RELEASE_READY] bytes={cleanup.BytesAfter} files={cleanup.FilesAfter} removedDoNotShip={cleanup.RemovedDirectories}");
        }

        private static void BuildDiagnosticPlayers(string developmentFolder, string releaseFolder)
        {
            foreach (bool development in new[] { true, false })
            {
                string folder = development ? developmentFolder : releaseFolder;
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray(),
                    locationPathName = $"Builds/Windows/{folder}/ProjectEmberfall.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = development ? BuildOptions.Development : BuildOptions.None
                });
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    throw new InvalidOperationException("Style lab build failed: " + folder);
                Debug.Log($"[STYLE_LAB_BUILD] folder={folder} bytes={report.summary.totalSize} warnings={report.summary.totalWarnings}");
            }
        }

        [MenuItem("Emberfall/Setup/Apply 0.9.0 Visual Foundation")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Visual authoring requires Edit Mode.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            // Do not discard unsaved user edits when invoked from the menu.
            if (!UnityEngine.Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("Scene save cancelled.");
            var profile = ConfigureProfile();
            ConfigureRenderer();
            try
            {
                foreach (string name in new[] { "10_EmberValley", "20_Sanctum", "90_CombatGym", "91_NetworkGym" })
                {
                    string path = $"Assets/_Game/Scenes/{name}.unity";
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) continue;
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var volumes = scene.GetRootGameObjects().Where(x => x.name == VolumeName).ToArray();
                    if (volumes.Length > 1) throw new InvalidOperationException("Duplicate presentation Volume: " + name);
                    var root = volumes.Length == 0 ? new GameObject(VolumeName) : volumes[0];
                    var volume = root.GetComponent<Volume>() ?? root.AddComponent<Volume>();
                    volume.isGlobal = true;
                    volume.priority = 0f;
                    volume.weight = 1f;
                    volume.sharedProfile = profile;
                    // Both additive scenes share the identical full-weight profile: no stacked grading.
                    foreach (var camera in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)))
                        ConfigureCamera(camera);
                    foreach (var light in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Light>(true)))
                    {
                        if (light.type != LightType.Directional) continue;
                        light.shadows = LightShadows.Soft;
                        light.color = new Color(1f, 0.96f, 0.88f);
                        // Frozen pre-style authoring baselines; Apply/Build must never compound 1.2x.
                        if (name == "10_EmberValley") light.intensity = 1.5f;
                        if (name == "90_CombatGym") light.intensity = 1.44f;
                        if (name == "91_NetworkGym") light.intensity = 1.38f;
                        EditorUtility.SetDirty(light);
                    }
                    RenderSettings.ambientMode = AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.72f);
                    RenderSettings.ambientEquatorColor = new Color(0.40f, 0.42f, 0.42f);
                    RenderSettings.ambientGroundColor = new Color(0.24f, 0.22f, 0.20f);
                    RenderSettings.ambientIntensity = 1.5f;
                    RenderSettings.fog = false;
                    RenderSettings.fogMode = FogMode.Linear;
                    RenderSettings.fogColor = new Color(0.105f, 0.14f, 0.155f);
                    RenderSettings.fogStartDistance = name == "20_Sanctum" ? 18f : 28f;
                    RenderSettings.fogEndDistance = name == "20_Sanctum" ? 48f : 88f;
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                var player = PrefabUtility.LoadPrefabContents(PlayerPath);
                try
                {
                    foreach (var camera in player.GetComponentsInChildren<Camera>(true)) ConfigureCamera(camera);
                    PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(player); }
                AssetDatabase.SaveAssets();
            }
            finally
            {
                if (setup.Any(x => x.isLoaded && x.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Debug.Log("EMBERFALL_VISUAL_FOUNDATION_COMPLETE version=0.9.0 style=bright-solid fog=off contactShadows=not-applicable");
        }

        private static VolumeProfile ConfigureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            Component<Tonemapping>(profile).mode.Override(TonemappingMode.Neutral);
            var bloom = Component<Bloom>(profile);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);
            var color = Component<ColorAdjustments>(profile);
            color.postExposure.Override(0.7f);
            color.contrast.Override(2f);
            color.saturation.Override(25f);
            var tones = Component<ShadowsMidtonesHighlights>(profile);
            tones.shadows.Override(new Vector4(0.97f, 1f, 1.035f, 0f));
            tones.highlights.Override(new Vector4(1.035f, 1.01f, 0.97f, 0f));
            var vignette = Component<Vignette>(profile);
            vignette.intensity.Override(0f);
            vignette.smoothness.Override(0.4f);
            foreach (var component in profile.components) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static T Component<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(true);
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true;
            return component;
        }

        public static void ConfigureCamera(Camera camera)
        {
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.45f, 0.68f, 0.85f);
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.volumeLayerMask = 1; // Project global Volume is on Default, including the Owner camera.
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(data);
        }

        private static void ConfigureRenderer()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            pipeline.supportsHDR = true;
            pipeline.colorGradingMode = ColorGradingMode.HighDynamicRange;
            var pipelineData = new SerializedObject(pipeline);
            pipelineData.FindProperty("m_SoftShadowsSupported").boolValue = true;
            pipelineData.ApplyModifiedPropertiesWithoutUndo();
            // Retain existing shadow distance/resolution and render scale; no geometry/culling changes.
            EditorUtility.SetDirty(pipeline);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(Package + "/Runtime/Data/PostProcessData.asset");
            if (renderer.postProcessData == null) throw new InvalidOperationException("URP post-process resources missing.");
            ResourceReloader.TryReloadAllNullIn(renderer, Package);
            var feature = renderer.rendererFeatures.FirstOrDefault(x => x != null && x.name == "Emberfall SSAO");
            if (feature == null)
            {
                var type = typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion", true);
                feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
                feature.name = "Emberfall SSAO";
                AssetDatabase.AddObjectToAsset(feature, renderer);
                renderer.rendererFeatures.Add(feature);
            }
            var serialized = new SerializedObject(feature);
            var settings = serialized.FindProperty("m_Settings");
            settings.FindPropertyRelative("Radius").floatValue = 0.4f;
            settings.FindPropertyRelative("Intensity").floatValue = 0.8f;
            settings.FindPropertyRelative("Samples").enumValueIndex = 1; // Medium: 8 samples.
            settings.FindPropertyRelative("BlurQuality").enumValueIndex = 1; // Medium: Gaussian.
            settings.FindPropertyRelative("Downsample").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            feature.SetActive(false); // Measured fallback: retain the authored feature, disable its default cost.
            feature.Create(); // Loads URP-owned shader/blue-noise references for player builds.
            EditorUtility.SetDirty(feature);
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
        }

        public static void SetSsaoForCaptureBuild(bool active)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var feature = renderer.rendererFeatures.Single(x => x != null && x.name == "Emberfall SSAO");
            feature.SetActive(active);
            EditorUtility.SetDirty(feature);
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
        }
    }
}
