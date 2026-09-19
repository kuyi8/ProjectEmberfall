using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    public static class M3ArtReviewTool
    {
        private const string OutputRoot = "Builds/ArtReview/0.4.2.3";
        private const string PlayerPrefab = "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab";
        private const string PeasantPrefab = "Assets/_Game/Prefabs/Characters/M3Art/P_Enemy_Peasant.prefab";
        private const string RuneAcolytePrefab = "Assets/_Game/Prefabs/Characters/M3Art/P_Enemy_RuneAcolyte.prefab";
        private const string OutfitRoot = "Assets/ThirdParty/Quaternius/EmberfallArtBaseline/CharacterOutfits";
        private const string RangerModel = OutfitRoot + "/Exports/FBX (Unity)/Outfits/Male_Ranger.fbx";
        private const string PeasantModel = OutfitRoot + "/Exports/FBX (Unity)/Outfits/Male_Peasant.fbx";
        private const string BaseMaleModel = "Assets/_Game/Art/Universal Base Characters[Standard]/Universal Base Characters[Standard]/Base Characters/Unity/Superhero_Male_FullBody.fbx";
        private const string NatureRoot = "Assets/ThirdParty/Quaternius/EmberfallArtBaseline/StylizedNature";
        private const string VillageRoot = "Assets/ThirdParty/Quaternius/EmberfallArtBaseline/MedievalVillage";
        private const string PropsRoot = "Assets/ThirdParty/Quaternius/EmberfallArtBaseline/FantasyProps";

        [MenuItem("Emberfall/Review/Capture M3 Art Candidates")]
        public static void Capture()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                throw new InvalidOperationException(
                    "M3 art review requires a graphics device. Run batchmode without -nographics so rendered evidence is valid.");
            }

            string absoluteOutput = Path.GetFullPath(OutputRoot);
            Directory.CreateDirectory(absoluteOutput);

            CaptureCharacters(Path.Combine(absoluteOutput, "characters-front-player-melee-ranged.png"), 0f);
            CaptureCharacters(Path.Combine(absoluteOutput, "characters-back-player-melee-ranged.png"), 180f);
            CaptureHeadNeckDetail(Path.Combine(absoluteOutput, "characters-head-neck-detail.png"));
            CaptureEnvironment(Path.Combine(absoluteOutput, "environment-style-contact-sheet.png"));
            CaptureSceneView(
                Path.Combine(absoluteOutput, "ember-valley-camp-and-forest.png"),
                new Vector3(0f, 5.8f, -13f),
                new Vector3(0f, 1.3f, 14f),
                52f);
            CaptureSceneView(
                Path.Combine(absoluteOutput, "ember-valley-forest-route.png"),
                new Vector3(0f, 6.8f, 5f),
                new Vector3(0f, 1.1f, 30f),
                50f);
            CaptureSceneView(
                Path.Combine(absoluteOutput, "ember-valley-courtyard.png"),
                new Vector3(17f, 7.5f, 34f),
                new Vector3(31f, 1.2f, 46f),
                52f);
            CaptureSceneView(
                Path.Combine(absoluteOutput, "ember-valley-sanctum-and-warden.png"),
                new Vector3(31f, 8.2f, 41f),
                new Vector3(47f, 1.2f, 20f),
                54f);
            CaptureSceneView(
                Path.Combine(absoluteOutput, "ember-valley-return-route.png"),
                new Vector3(43f, 6.6f, 2f),
                new Vector3(22f, 1.1f, 10f),
                50f);
            AssetDatabase.Refresh();
            Debug.Log($"EMBERFALL_ART_REVIEW_COMPLETE output={absoluteOutput}");
        }

        [MenuItem("Emberfall/Review/Audit Character Source Hierarchies")]
        public static void AuditCharacterSources()
        {
            ReportAssetHierarchy(RangerModel, "ranger-outfit");
            ReportAssetHierarchy(PeasantModel, "peasant-outfit");
            ReportAssetHierarchy(BaseMaleModel, "base-male-fullbody");
            Debug.Log("EMBERFALL_CHARACTER_SOURCE_AUDIT_COMPLETE");
        }

        private static void ReportAssetHierarchy(string path, string label)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
            {
                throw new FileNotFoundException("Character audit source is missing.", path);
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not instantiate character audit source: {path}");
            }

            try
            {
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    string rendererPath = AnimationUtility.CalculateTransformPath(renderer.transform, instance.transform);
                    string meshName = renderer switch
                    {
                        SkinnedMeshRenderer skinned => skinned.sharedMesh != null ? skinned.sharedMesh.name : "<none>",
                        MeshRenderer => renderer.GetComponent<MeshFilter>()?.sharedMesh?.name ?? "<none>",
                        _ => "<none>"
                    };
                    Debug.Log(
                        $"EMBERFALL_CHARACTER_SOURCE label={label} renderer={rendererPath} " +
                        $"type={renderer.GetType().Name} mesh={meshName} materials={renderer.sharedMaterials.Length}");
                    if (label == "base-male-fullbody" && renderer is SkinnedMeshRenderer bodyRenderer &&
                        bodyRenderer.sharedMesh != null && bodyRenderer.sharedMesh.name == "SuperHero_Male")
                    {
                        Transform head = instance.GetComponentInChildren<Animator>(true)
                            ?.GetBoneTransform(HumanBodyBones.Head);
                        int headIndex = Array.IndexOf(bodyRenderer.bones, head);
                        BoneWeight[] weights = bodyRenderer.sharedMesh.boneWeights;
                        int headWeightedVertices = 0;
                        for (int i = 0; i < weights.Length; i++)
                        {
                            BoneWeight weight = weights[i];
                            if ((weight.boneIndex0 == headIndex && weight.weight0 >= 0.5f) ||
                                (weight.boneIndex1 == headIndex && weight.weight1 >= 0.5f) ||
                                (weight.boneIndex2 == headIndex && weight.weight2 >= 0.5f) ||
                                (weight.boneIndex3 == headIndex && weight.weight3 >= 0.5f))
                            {
                                headWeightedVertices++;
                            }
                        }
                        Debug.Log(
                            $"EMBERFALL_CHARACTER_HEAD_AUDIT bone={head?.name ?? "<none>"} " +
                            $"boneIndex={headIndex} vertices={weights.Length} headWeightedVertices={headWeightedVertices}");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void CaptureSceneView(
            string outputPath,
            Vector3 cameraPosition,
            Vector3 lookAt,
            float fieldOfView)
        {
            Scene scene = EditorSceneManager.OpenScene(
                "Assets/_Game/Scenes/10_EmberValley.unity",
                OpenSceneMode.Single);
            CaptureScene(scene, outputPath, cameraPosition, lookAt, fieldOfView);
        }

        private static void CaptureCharacters(string outputPath, float yaw)
        {
            Scene scene = CreateReviewScene(new Color(0.045f, 0.06f, 0.075f));
            GameObject player = InstantiateRequired(PlayerPrefab, "A_Player_Ranger");
            GameObject peasant = InstantiateRequired(PeasantPrefab, "B_Fogwalker_Complete");
            GameObject runeAcolyte = InstantiateRequired(RuneAcolytePrefab, "C_RuneAcolyte_Complete");

            FitAndPlace(player, new Vector3(-2.45f, 0f, 0f), 2.05f, yaw);
            FitAndPlace(peasant, Vector3.zero, 2.05f, yaw);
            FitAndPlace(runeAcolyte, new Vector3(2.45f, 0f, 0f), 2.05f, yaw);

            ReportModel(player, "player-ranger-final");
            ReportModel(peasant, "fogwalker-complete-final");
            ReportModel(runeAcolyte, "rune-acolyte-complete-final");
            CaptureScene(scene, outputPath, new Vector3(0f, 1.25f, -8.3f), new Vector3(0f, 1.05f, 0f), 40f);
        }

        private static void CaptureHeadNeckDetail(string outputPath)
        {
            Scene scene = CreateReviewScene(new Color(0.045f, 0.06f, 0.075f));
            GameObject player = InstantiateRequired(PlayerPrefab, "A_Player_Ranger_HeadDetail");
            GameObject peasant = InstantiateRequired(PeasantPrefab, "B_Fogwalker_HeadDetail");
            FitAndPlace(player, new Vector3(-0.78f, 0f, 0f), 2.05f, 0f);
            FitAndPlace(peasant, new Vector3(0.78f, 0f, 0f), 2.05f, 0f);
            CaptureScene(scene, outputPath, new Vector3(0f, 1.62f, -3.5f), new Vector3(0f, 1.58f, 0f), 34f);
        }

        private static void CaptureEnvironment(string outputPath)
        {
            Scene scene = CreateReviewScene(new Color(0.055f, 0.07f, 0.08f));
            string[] paths =
            {
                NatureRoot + "/FBX (Unity)/CommonTree_1.fbx",
                NatureRoot + "/FBX (Unity)/TwistedTree_1.fbx",
                VillageRoot + "/FBX/Wall_UnevenBrick_Door_Round.fbx",
                PropsRoot + "/Exports/FBX/Stall_Cart_Empty.fbx",
                PropsRoot + "/Exports/FBX/Banner_1.fbx"
            };
            float[] heights = { 3.3f, 3.3f, 2.6f, 1.8f, 2.5f };
            for (int i = 0; i < paths.Length; i++)
            {
                GameObject candidate = InstantiateRequired(paths[i], $"EnvironmentCandidate_{i:00}");
                FitAndPlace(candidate, new Vector3(-5.2f + (i * 2.6f), 0f, 0f), heights[i]);
                ApplySourceTexturePreviewMaterials(candidate);
                ReportModel(candidate, Path.GetFileNameWithoutExtension(paths[i]));
            }

            CaptureScene(scene, outputPath, new Vector3(0f, 1.8f, -12.5f), new Vector3(0f, 1.35f, 0f), 42f);
        }

        private static Scene CreateReviewScene(Color background)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.36f, 0.4f);

            GameObject key = new GameObject("Key Light", typeof(Light));
            key.transform.rotation = Quaternion.Euler(38f, -34f, 0f);
            Light keyLight = key.GetComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, 0.84f, 0.67f);
            keyLight.intensity = 1.25f;

            GameObject rim = new GameObject("Rim Light", typeof(Light));
            rim.transform.SetPositionAndRotation(new Vector3(1f, 4f, 3f), Quaternion.Euler(28f, 160f, 0f));
            Light rimLight = rim.GetComponent<Light>();
            rimLight.type = LightType.Directional;
            rimLight.color = new Color(0.35f, 0.58f, 1f);
            rimLight.intensity = 0.75f;

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Neutral Scale Floor";
            floor.transform.localScale = new Vector3(2.2f, 1f, 1.1f);
            floor.GetComponent<Renderer>().sharedMaterial = CreateTemporaryMaterial("ReviewFloor", new Color(0.12f, 0.14f, 0.16f), 0.05f);

            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            Camera.main?.gameObject.SetActive(false);
            return scene;
        }

        private static void CaptureScene(Scene scene, string outputPath, Vector3 cameraPosition, Vector3 lookAt, float fieldOfView)
        {
            GameObject cameraObject = new GameObject("Review Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.045f, 0.06f, 0.075f);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.transform.position = cameraPosition;
            camera.transform.rotation = Quaternion.LookRotation(lookAt - cameraPosition, Vector3.up);

            const int width = 1600;
            const int height = 900;
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            Texture2D image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(outputPath, image.EncodeToPNG());
                Debug.Log($"EMBERFALL_ART_REVIEW_IMAGE path={outputPath}");
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static GameObject InstantiateRequired(string path, string name)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
            {
                throw new FileNotFoundException("Art review candidate is missing.", path);
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not instantiate art review candidate: {path}");
            }

            instance.name = name;
            foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
            {
                animator.enabled = false;
            }
            return instance;
        }

        private static void FitAndPlace(GameObject instance, Vector3 groundPosition, float targetHeight, float yaw = 180f)
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, yaw, 0f));
            instance.transform.localScale = Vector3.one;
            Bounds bounds = CalculateBounds(instance);
            if (bounds.size.y <= 0.001f)
            {
                throw new InvalidDataException($"Candidate has no renderer bounds: {instance.name}");
            }

            float scale = targetHeight / bounds.size.y;
            instance.transform.localScale = Vector3.one * scale;
            bounds = CalculateBounds(instance);
            instance.transform.position = groundPosition + new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        }

        private static void ApplyOutfitPreviewMaterials(GameObject root, OutfitKind kind)
        {
            Texture2D body = LoadTexture(OutfitRoot + "/Textures/Base/T_Regular_Male_Dark_BaseColor.png");
            string folder = kind == OutfitKind.Ranger ? "Ranger" : "Peasant";
            string prefix = kind == OutfitKind.Ranger ? "T_Ranger" : "T_Peasant";
            Texture2D outfit = LoadTexture($"{OutfitRoot}/Textures/{folder}/{prefix}_BaseColor.png");
            Texture2D normal = LoadTexture($"{OutfitRoot}/Textures/{folder}/{prefix}_Normal.png");

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] replacements = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < replacements.Length; i++)
                {
                    string materialName = renderer.sharedMaterials[i] != null
                        ? renderer.sharedMaterials[i].name.ToLowerInvariant()
                        : string.Empty;
                    bool bodySlot = materialName.Contains("base") || materialName.Contains("body") || materialName.Contains("skin");
                    replacements[i] = CreateTemporaryTexturedMaterial(
                        $"Review_{kind}_{i}",
                        bodySlot ? body : outfit,
                        bodySlot ? null : normal,
                        Color.white);
                }

                renderer.sharedMaterials = replacements;
            }
        }

        private static void ApplySourceTexturePreviewMaterials(GameObject root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] replacements = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < replacements.Length; i++)
                {
                    Material source = renderer.sharedMaterials[i];
                    Texture texture = null;
                    Color tint = Color.white;
                    if (source != null)
                    {
                        if (source.HasProperty("_BaseMap")) texture = source.GetTexture("_BaseMap");
                        if (texture == null && source.HasProperty("_MainTex")) texture = source.GetTexture("_MainTex");
                        if (source.HasProperty("_Color")) tint = source.GetColor("_Color");
                    }

                    replacements[i] = CreateTemporaryTexturedMaterial($"Review_Source_{i}", texture, null, tint);
                }

                renderer.sharedMaterials = replacements;
            }
        }

        private static Material CreateTemporaryTexturedMaterial(string name, Texture texture, Texture normal, Color tint)
        {
            Material material = CreateTemporaryMaterial(name, tint, 0.08f);
            material.SetTexture("_BaseMap", texture);
            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }
            return material;
        }

        private static Material CreateTemporaryMaterial(string name, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("URP Lit shader is unavailable for art review.");
            }

            var material = new Material(shader)
            {
                name = name,
                color = color,
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private static Texture2D LoadTexture(string path)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                throw new FileNotFoundException("Art review texture is missing.", path);
            }
            return texture;
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void ReportModel(GameObject root, string label)
        {
            Animator animator = root.GetComponentInChildren<Animator>(true);
            Bounds bounds = CalculateBounds(root);
            var materials = new HashSet<string>(StringComparer.Ordinal);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null) materials.Add(material.name);
                }
            }

            Debug.Log(
                $"EMBERFALL_ART_MODEL label={label} renderers={root.GetComponentsInChildren<Renderer>(true).Length} " +
                $"height={bounds.size.y:F3} humanoid={(animator != null && animator.isHuman)} " +
                $"avatarValid={(animator != null && animator.avatar != null && animator.avatar.isValid)} " +
                $"materials=[{string.Join(",", materials)}]");
        }

        private enum OutfitKind
        {
            Ranger,
            Peasant
        }
    }
}
