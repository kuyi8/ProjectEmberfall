using System.IO;
using Emberfall.Infrastructure.Bootstrap;
using Emberfall.Infrastructure.Scenes;
using Emberfall.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    public static class M0ProjectSetup
    {
        private const string SceneFolder = "Assets/_Game/Scenes";
        private const string BootstrapPath = SceneFolder + "/00_Bootstrap.unity";
        private const string MainMenuPath = SceneFolder + "/01_MainMenu.unity";
        private const string CombatGymPath = SceneFolder + "/90_CombatGym.unity";
        private const string RendererDataPath = "Assets/_Game/Settings/URP_PC_Renderer.asset";
        private const string PipelineAssetPath = "Assets/_Game/Settings/URP_PC.asset";

        [MenuItem("Emberfall/Setup/Apply M0 Project Setup")]
        public static void Apply()
        {
            EnsureDirectories();
            ConfigureRenderPipeline();
            CreateBootstrapScene();
            CreateMainMenuScene();
            CreateCombatGymScene();
            ConfigureBuildSettings();
            ConfigureProjectSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Project Emberfall M0 project setup completed.");
        }

        private static void EnsureDirectories()
        {
            string[] directories =
            {
                "Assets/_Game/Art",
                "Assets/_Game/Audio",
                "Assets/_Game/Data",
                "Assets/_Game/Prefabs",
                SceneFolder,
                "Assets/_Game/Settings",
                "Assets/_Game/Tests/EditMode",
                "Assets/_Game/Tests/PlayMode",
                "Assets/ThirdParty"
            };

            foreach (string directory in directories)
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static void ConfigureRenderPipeline()
        {
            UniversalRendererData rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererDataPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererDataPath);
            }

            UniversalRenderPipelineAsset pipelineAsset =
                AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipelineAsset == null)
            {
                pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipelineAsset, PipelineAssetPath);
            }

#pragma warning disable CS0618
            GraphicsSettings.renderPipelineAsset = pipelineAsset;
#pragma warning restore CS0618
            QualitySettings.renderPipeline = pipelineAsset;
        }

        private static void CreateBootstrapScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var appRoot = new GameObject("[App]");
            appRoot.AddComponent<AppBootstrap>();
            EditorSceneManager.SaveScene(scene, BootstrapPath);
        }

        private static void CreateMainMenuScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.045f, 0.06f);

            var placeholder = new GameObject("[UI] M0 Main Menu Placeholder");
            placeholder.AddComponent<MainMenuPlaceholder>();

            EditorSceneManager.SaveScene(scene, MainMenuPath);
        }

        private static void CreateCombatGymScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 5f, -8f), Quaternion.Euler(20f, 0f, 0f));

            var lightObject = new GameObject("Directional Light", typeof(Light));
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground_Graybox";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);

            new GameObject("PlayerSpawn").transform.position = Vector3.zero;
            EditorSceneManager.SaveScene(scene, CombatGymPath);
        }

        private static void ConfigureBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootstrapPath, true),
                new EditorBuildSettingsScene(MainMenuPath, true)
            };
        }

        private static void ConfigureProjectSettings()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            VersionControlSettings.mode = "Visible Meta Files";

            PlayerSettings.productName = "Project Emberfall";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        }
    }
}
