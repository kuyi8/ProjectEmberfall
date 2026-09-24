using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Emberfall.Tests.EditMode
{
    public sealed class VisualFoundationAssetTests
    {
        [Test]
        public void BuiltinContent_StillSupportsTheCurrentClient_WithoutChangingPayloadVersion()
        {
            var manifest = Emberfall.Infrastructure.Content.ContentPackageManifestJson.Deserialize(
                System.IO.File.ReadAllText("Assets/StreamingAssets/BuiltinContent/manifest.json"));
            var client = new Emberfall.Core.Content.SemanticVersion(0, 9, 0);
            Assert.That(manifest.MinClientVersion.CompareTo(client), Is.LessThanOrEqualTo(0));
            Assert.That(manifest.MaxClientVersion.CompareTo(client), Is.GreaterThanOrEqualTo(0));
            Assert.That(manifest.ContentVersion.ToString(), Is.EqualTo("0.8.11"));
            Assert.That(manifest.SchemaVersion, Is.EqualTo(1));
        }

        [Test]
        public void Profile_ContainsActiveGradingBloomAndVignette_NotFakeFogOrSsaoVolumes()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Game/Settings/PP_Emberfall_Global.asset");
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.components.Count, Is.EqualTo(5));
            Assert.That(profile.TryGet(out Tonemapping tone), Is.True);
            Assert.That(tone.mode.overrideState, Is.True);
            Assert.That(tone.mode.value, Is.EqualTo(TonemappingMode.Neutral));
            Assert.That(profile.TryGet(out Bloom bloom), Is.True);
            Assert.That(bloom.IsActive(), Is.True);
            Assert.That(bloom.threshold.value, Is.EqualTo(0.95f));
            Assert.That(bloom.intensity.value, Is.EqualTo(0.35f));
            Assert.That(profile.TryGet(out ColorAdjustments color), Is.True);
            Assert.That(color.postExposure.value, Is.EqualTo(0.7f));
            Assert.That(color.contrast.value, Is.EqualTo(2f));
            Assert.That(color.saturation.value, Is.EqualTo(25f));
            Assert.That(profile.TryGet(out ShadowsMidtonesHighlights tones), Is.True);
            Assert.That(tones.IsActive(), Is.True);
            Assert.That(profile.TryGet(out Vignette vignette), Is.True);
            Assert.That(vignette.intensity.value, Is.Zero);
            foreach (var component in profile.components)
            {
                Assert.That(component.active, Is.True, component.name + " must be active");
                // Hidden VolumeComponents can report IsSubAsset=false; verify actual serialized identity instead.
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out string guid, out long localId), Is.True);
                Assert.That(guid, Is.EqualTo(AssetDatabase.AssetPathToGUID("Assets/_Game/Settings/PP_Emberfall_Global.asset")));
                Assert.That(localId, Is.Not.EqualTo(0).And.Not.EqualTo(11400000));
            }
        }

        [Test]
        public void Renderer_HasPackagedPostProcessResourcesAndOneMediumSsaoFeature()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/_Game/Settings/URP_PC_Renderer.asset");
            Assert.That(renderer.postProcessData, Is.Not.Null);
            Assert.That(renderer.postProcessData.shaders.uberPostPS, Is.Not.Null);
            var features = renderer.rendererFeatures.Where(x => x != null && x.name == "Emberfall SSAO").ToArray();
            Assert.That(features.Length, Is.EqualTo(1));
            Assert.That(features[0].isActive, Is.False, "Keep the measured SSAO-off fallback until Harness accepts its cost.");
            var serialized = new SerializedObject(features[0]);
            Assert.That(serialized.FindProperty("m_Shader").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("m_BlueNoise256Textures").arraySize, Is.EqualTo(7));
            var settings = serialized.FindProperty("m_Settings");
            Assert.That(settings.FindPropertyRelative("Radius").floatValue, Is.EqualTo(0.4f));
            Assert.That(settings.FindPropertyRelative("Samples").enumValueIndex, Is.EqualTo(1));
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/_Game/Settings/URP_PC.asset");
            Assert.That(pipeline.supportsHDR, Is.True);
            Assert.That(pipeline.renderScale, Is.EqualTo(1f));
        }

        [TestCase("10_EmberValley")]
        [TestCase("20_Sanctum")]
        [TestCase("90_CombatGym")]
        [TestCase("91_NetworkGym")]
        public void GameplayScene_HasOneSharedGlobalVolumeAndApprovedBrightEnvironment(string sceneName)
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene($"Assets/_Game/Scenes/{sceneName}.unity", OpenSceneMode.Single);
                var roots = scene.GetRootGameObjects();
                var volumes = roots.SelectMany(x => x.GetComponentsInChildren<Volume>(true)).ToArray();
                Assert.That(volumes.Length, Is.EqualTo(1));
                Assert.That(volumes[0].isGlobal, Is.True);
                Assert.That(volumes[0].weight, Is.EqualTo(1f));
                Assert.That(AssetDatabase.GetAssetPath(volumes[0].sharedProfile), Does.EndWith("PP_Emberfall_Global.asset"));
                Assert.That(volumes[0].GetComponent<Collider>(), Is.Null);
                Assert.That(RenderSettings.fog, Is.False);
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(UnityEngine.Rendering.AmbientMode.Trilight));
                Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(new Color(0.55f, 0.62f, 0.72f)));
                Assert.That(RenderSettings.ambientEquatorColor, Is.EqualTo(new Color(0.4f, 0.42f, 0.42f)));
                Assert.That(RenderSettings.ambientGroundColor, Is.EqualTo(new Color(0.24f, 0.22f, 0.2f)));
                foreach (var camera in roots.SelectMany(x => x.GetComponentsInChildren<Camera>(true))) CheckCamera(camera);
            }
            finally
            {
                if (setup.Any(x => x.isLoaded && x.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void NetworkOwnerCamera_HasPostProcessingEvenWhilePrefabCameraIsInactive()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Resources/Networking/P_M5_NetworkGymPlayer.prefab");
            var cameras = prefab.GetComponentsInChildren<Camera>(true);
            Assert.That(cameras.Length, Is.EqualTo(1));
            Assert.That(cameras[0].gameObject.activeSelf, Is.False);
            CheckCamera(cameras[0]);
        }

        private static void CheckCamera(Camera camera)
        {
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            Assert.That(data, Is.Not.Null, camera.name);
            Assert.That(data.renderPostProcessing, Is.True, camera.name);
            Assert.That(data.volumeLayerMask.value & 1, Is.EqualTo(1));
            Assert.That(camera.allowHDR, Is.True);
            Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(camera.backgroundColor, Is.EqualTo(new Color(0.45f, 0.68f, 0.85f)));
        }
    }
}
