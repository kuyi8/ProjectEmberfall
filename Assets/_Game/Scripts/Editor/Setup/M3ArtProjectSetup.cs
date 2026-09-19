using System;
using System.Collections.Generic;
using System.IO;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    public static class M3ArtProjectSetup
    {
        private const string CombatGymPath = "Assets/_Game/Scenes/90_CombatGym.unity";
        private const string EmberValleyPath = "Assets/_Game/Scenes/10_EmberValley.unity";
        private const string SourceRoot = "Assets/ThirdParty/Quaternius/EmberfallArtBaseline";
        private const string NatureRoot = SourceRoot + "/StylizedNature";
        private const string VillageRoot = SourceRoot + "/MedievalVillage";
        private const string PropsRoot = SourceRoot + "/FantasyProps";
        private const string MaterialRoot = "Assets/_Game/Art/Materials/M3Art";
        private const string CharacterMaterialRoot = "Assets/_Game/Art/Materials/Character/M3Art";
        private const string CharacterDerivativeRoot = "Assets/_Game/Art/Characters/M3Art/Derived";
        private const string CharacterPrefabRoot = "Assets/_Game/Prefabs/Characters/M3Art";
        private const string MeshRoot = "Assets/_Game/Art/Meshes/M3Art";
        private const string RuneMeshPath = MeshRoot + "/M_RuneOctahedron.asset";
        private const string RuneRingMeshPath = MeshRoot + "/M_RuneHighlightRing.asset";
        private const string AnimationSetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        private const string OutfitSourceRoot = SourceRoot + "/CharacterOutfits";
        private const string RangerSource = OutfitSourceRoot + "/Exports/FBX (Unity)/Outfits/Male_Ranger.fbx";
        private const string PeasantSource = OutfitSourceRoot + "/Exports/FBX (Unity)/Outfits/Male_Peasant.fbx";
        private const string RangerDerived = CharacterDerivativeRoot + "/Male_Ranger_Emberfall.fbx";
        private const string PeasantDerived = CharacterDerivativeRoot + "/Male_Peasant_Emberfall.fbx";
        private const string RangerPrefab = CharacterPrefabRoot + "/P_Player_Ranger.prefab";
        private const string PeasantPrefab = CharacterPrefabRoot + "/P_Enemy_Peasant.prefab";
        private const string RuneAcolytePrefab = CharacterPrefabRoot + "/P_Enemy_RuneAcolyte.prefab";
        private const string BaseCharacterRoot = "Assets/_Game/Art/Universal Base Characters[Standard]/Universal Base Characters[Standard]/Base Characters";
        private const string BaseMaleSource = BaseCharacterRoot + "/Unity/Superhero_Male_FullBody.fbx";
        private const string BaseMaleHeadMeshPath = MeshRoot + "/M_BaseMale_Head.asset";
        private const string BaseMaleNeckMeshPath = MeshRoot + "/M_BaseMale_Neck.asset";
        private const string BaseMaleEyesMeshPath = MeshRoot + "/M_BaseMale_Eyes.asset";
        private const string BaseMaleBrowsMeshPath = MeshRoot + "/M_BaseMale_Brows.asset";

        private const string CommonTree1 = NatureRoot + "/FBX (Unity)/CommonTree_1.fbx";
        private const string CommonTree2 = NatureRoot + "/FBX (Unity)/CommonTree_2.fbx";
        private const string CommonTree3 = NatureRoot + "/FBX (Unity)/CommonTree_3.fbx";
        private const string DeadTree1 = NatureRoot + "/FBX (Unity)/DeadTree_1.fbx";
        private const string TwistedTree1 = NatureRoot + "/FBX (Unity)/TwistedTree_1.fbx";
        private const string Bush = NatureRoot + "/FBX (Unity)/Bush_Common.fbx";
        private const string BushFlowers = NatureRoot + "/FBX (Unity)/Bush_Common_Flowers.fbx";
        private const string Fern = NatureRoot + "/FBX (Unity)/Fern_1.fbx";
        private const string Rock1 = NatureRoot + "/FBX (Unity)/Rock_Medium_1.fbx";
        private const string Rock2 = NatureRoot + "/FBX (Unity)/Rock_Medium_2.fbx";
        private const string PathRock1 = NatureRoot + "/FBX (Unity)/RockPath_Round_Small_1.fbx";
        private const string PathRock2 = NatureRoot + "/FBX (Unity)/RockPath_Round_Thin.fbx";
        private const string PathRock3 = NatureRoot + "/FBX (Unity)/RockPath_Round_Wide.fbx";

        private const string VillageWall = VillageRoot + "/FBX/Wall_UnevenBrick_Straight.fbx";
        private const string PlasterWall = VillageRoot + "/FBX/Wall_Plaster_WoodGrid.fbx";
        // Wall_Arch.fbx is an empty organizational node in the Standard export.
        // The round-door wall contains the actual arch mesh and is safe to instantiate.
        private const string VillageArch = VillageRoot + "/FBX/Wall_UnevenBrick_Door_Round.fbx";
        private const string VillageFence = VillageRoot + "/FBX/Prop_WoodenFence_Extension1.fbx";

        private const string Barrel = PropsRoot + "/Exports/FBX/Barrel.fbx";
        private const string Bench = PropsRoot + "/Exports/FBX/Bench.fbx";
        private const string Crate = PropsRoot + "/Exports/FBX/Crate_Wooden.fbx";
        private const string Banner = PropsRoot + "/Exports/FBX/Banner_1.fbx";
        private const string Cart = PropsRoot + "/Exports/FBX/Stall_Cart_Empty.fbx";
        private const string Workbench = PropsRoot + "/Exports/FBX/Workbench.fbx";

        [MenuItem("Emberfall/Setup/Apply M3 Art Baseline")]
        public static void Apply()
        {
            ValidateSourceLicenses();
            ConfigureTextureImports();
            PlayerAnimationSet animationSet = M1AnimationSetup.Apply();
            CharacterVisuals characterVisuals = EnsureCharacterVisuals(animationSet);
            ApplyWithCharacterVisuals(
                characterVisuals.Player,
                characterVisuals.MeleeEnemy,
                characterVisuals.RangedEnemy,
                null,
                characterVisuals.MeleeEnemy);
        }

        internal static void ApplyWithCharacterVisuals(
            GameObject playerVisual,
            GameObject meleeVisual,
            GameObject rangedVisual,
            GameObject wardenVisual,
            GameObject scoutVisual)
        {
            ValidateSourceLicenses();
            ConfigureTextureImports();
            M3ProjectSetup.ApplyWithCharacterVisuals(
                playerVisual,
                meleeVisual,
                rangedVisual,
                wardenVisual);
            ArtPalette palette = EnsurePalette();
            DecorateCombatGym(palette);
            DecorateEmberValley(palette, scoutVisual != null ? scoutVisual : meleeVisual);
            PlayerSettings.bundleVersion = "0.5.0";
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Project Emberfall M3 art baseline completed with CC0 Quaternius assets.");
        }

        private static void DecorateCombatGym(ArtPalette palette)
        {
            Scene scene = EditorSceneManager.OpenScene(CombatGymPath, OpenSceneMode.Single);
            Transform root = new GameObject("[Art] Combat Gym Baseline").transform;

            string[] wallModels = { VillageWall, VillageWall, VillageArch, VillageWall, VillageWall };
            for (int i = 0; i < wallModels.Length; i++)
            {
                float x = -9.6f + (i * 4.8f);
                CreateFitted(root, $"NorthRuins_{i:00}", wallModels[i], new Vector3(x, 0f, 11.65f), 180f, 3.8f, palette, ArtKind.Village);
                CreateFitted(root, $"SouthRuins_{i:00}", wallModels[i], new Vector3(-x, 0f, -11.65f), 0f, 3.8f, palette, ArtKind.Village);
            }

            for (int i = 0; i < 5; i++)
            {
                float z = -9.6f + (i * 4.8f);
                CreateFitted(root, $"EastRuins_{i:00}", i == 2 ? VillageArch : VillageWall, new Vector3(11.65f, 0f, z), 270f, 3.8f, palette, ArtKind.Village);
                CreateFitted(root, $"WestRuins_{i:00}", i == 2 ? VillageArch : VillageWall, new Vector3(-11.65f, 0f, -z), 90f, 3.8f, palette, ArtKind.Village);
            }

            CreateFitted(root, "GymTree_West", TwistedTree1, new Vector3(-10.4f, 0f, 8.7f), 28f, 6.2f, palette, ArtKind.Nature);
            CreateFitted(root, "GymTree_East", DeadTree1, new Vector3(10.2f, 0f, 8.5f), -20f, 5.8f, palette, ArtKind.Nature);
            CreateFitted(root, "GymBanner_Frame", Banner, new Vector3(0f, 0f, 11.1f), 180f, 3.2f, palette, ArtKind.Props);
            CreateFitted(root, "GymCrate", Crate, new Vector3(-9.8f, 0f, -9.3f), 18f, 1f, palette, ArtKind.Props);
            CreateFitted(root, "GymBarrel", Barrel, new Vector3(-8.6f, 0f, -9.5f), -12f, 1.05f, palette, ArtKind.Props);

            EditorSceneManager.SaveScene(scene, CombatGymPath);
        }

        private static void DecorateEmberValley(ArtPalette palette, GameObject scoutVisualPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(EmberValleyPath, OpenSceneMode.Single);
            Transform root = new GameObject("[Art] Ember Valley Baseline").transform;

            HidePlaceholderTreeRenderers();
            TuneEmberValleyLighting();
            ReplaceCriticalRoutePlaceholders(root, scoutVisualPrefab, palette);
            CreateCamp(root, palette);
            CreateForest(root, palette);
            DecorateForestGameplay(root, palette);
            CreateRoutePath(root, palette);
            CreateBridge(root, palette);
            CreateCourtyard(root, palette);
            CreateWardenArena(root, palette);
            CreateReturnRouteShell(root, palette);
            Transform supportRoot = new GameObject("[Gameplay] Terrain Support Proxies").transform;
            CreateGroundingAndCliffShell(root, supportRoot, palette);
            ApplySurfaceMaterials(palette);
            CreateDistantGround(root, palette);
            MarkCameraOccluders(root);

            EditorSceneManager.SaveScene(scene, EmberValleyPath);
        }

        private static CharacterVisuals EnsureCharacterVisuals(PlayerAnimationSet animationSet)
        {
            Directory.CreateDirectory(CharacterDerivativeRoot);
            Directory.CreateDirectory(CharacterPrefabRoot);
            Directory.CreateDirectory(CharacterMaterialRoot);
            AssetDatabase.Refresh();

            EnsureDerivedHumanoid(RangerSource, RangerDerived);
            EnsureDerivedHumanoid(PeasantSource, PeasantDerived);
            CharacterHeadMeshes headMeshes = EnsureBaseHeadMeshes();

            Material rangerBody = EnsureCharacterMaterial(
                "M_Ranger_Body",
                OutfitSourceRoot + "/Textures/Base/T_Regular_Male_Dark_BaseColor.png",
                OutfitSourceRoot + "/Textures/Base/T_Regular_Male_Normal.png",
                new Color(0.9f, 0.93f, 0.9f));
            Material rangerOutfit = EnsureCharacterMaterial(
                "M_Ranger_Outfit",
                OutfitSourceRoot + "/Textures/Ranger/T_Ranger_BaseColor.png",
                OutfitSourceRoot + "/Textures/Ranger/T_Ranger_Normal.png",
                new Color(0.72f, 0.82f, 0.7f));
            Material peasantBody = EnsureCharacterMaterial(
                "M_Fogwalker_Body",
                OutfitSourceRoot + "/Textures/Base/T_Regular_Male_Dark_BaseColor.png",
                OutfitSourceRoot + "/Textures/Base/T_Regular_Male_Normal.png",
                new Color(0.62f, 0.7f, 0.66f));
            Material peasantOutfit = EnsureCharacterMaterial(
                "M_Fogwalker_Outfit",
                OutfitSourceRoot + "/Textures/Peasant/T_Peasant_BaseColor.png",
                OutfitSourceRoot + "/Textures/Peasant/T_Peasant_Normal.png",
                new Color(0.48f, 0.56f, 0.5f));
            Material rangerFace = EnsureCharacterMaterial(
                "M_Ranger_Face",
                BaseCharacterRoot + "/Textures/T_Superhero_Male_Ligh.png",
                BaseCharacterRoot + "/Textures/Normals Unity - Godot/T_Superhero_Male_Normal.png",
                new Color(1f, 0.9f, 0.78f));
            Material rangerNeck = EnsureSolidCharacterMaterial(
                "M_Ranger_Neck",
                new Color(0.22f, 0.13f, 0.09f),
                0.12f);
            Material fogwalkerFace = EnsureCharacterMaterial(
                "M_Fogwalker_Face",
                BaseCharacterRoot + "/Textures/T_Superhero_Male_Ligh.png",
                BaseCharacterRoot + "/Textures/Normals Unity - Godot/T_Superhero_Male_Normal.png",
                new Color(0.64f, 0.76f, 0.68f));
            Material fogwalkerNeck = EnsureSolidCharacterMaterial(
                "M_Fogwalker_Neck",
                new Color(0.10f, 0.08f, 0.06f),
                0.1f);
            Material characterEyes = EnsureCharacterMaterial(
                "M_Character_Eyes",
                BaseCharacterRoot + "/Textures/T_Eye_Brown.png",
                BaseCharacterRoot + "/Textures/Normals Unity - Godot/T_Eye_Normal.png",
                Color.white);
            Material characterBrows = EnsureCharacterMaterial(
                "M_Character_Brows",
                BaseCharacterRoot + "/Textures/T_Hair_1_BaseColor.png",
                BaseCharacterRoot + "/Textures/Normals Unity - Godot/T_Hair_1_Normal.png",
                new Color(0.22f, 0.16f, 0.11f));
            Material runeBody = EnsureCharacterMaterial(
                "M_RuneAcolyte_Body",
                OutfitSourceRoot + "/Textures/Base/T_Regular_Male_Dark_BaseColor.png",
                OutfitSourceRoot + "/Textures/Base/T_Regular_Male_Normal.png",
                new Color(0.64f, 0.58f, 0.72f));
            Material runeOutfit = EnsureCharacterMaterial(
                "M_RuneAcolyte_Outfit",
                OutfitSourceRoot + "/Textures/Ranger/T_Ranger_BaseColor.png",
                OutfitSourceRoot + "/Textures/Ranger/T_Ranger_Normal.png",
                new Color(0.42f, 0.24f, 0.62f));
            Material runeFace = EnsureCharacterMaterial(
                "M_RuneAcolyte_Face",
                BaseCharacterRoot + "/Textures/T_Superhero_Male_Ligh.png",
                BaseCharacterRoot + "/Textures/Normals Unity - Godot/T_Superhero_Male_Normal.png",
                new Color(0.52f, 0.42f, 0.68f));
            Material runeNeck = EnsureSolidCharacterMaterial(
                "M_RuneAcolyte_Neck",
                new Color(0.12f, 0.08f, 0.16f),
                0.1f);

            return new CharacterVisuals
            {
                Player = EnsureCharacterPrefab(
                    "PlayerVisual_Ranger",
                    RangerDerived,
                    RangerPrefab,
                    animationSet,
                    rangerBody,
                    rangerOutfit,
                    headMeshes,
                    rangerNeck,
                    rangerFace,
                    characterEyes,
                    characterBrows),
                MeleeEnemy = EnsureCharacterPrefab(
                    "EnemyVisual_Peasant",
                    PeasantDerived,
                    PeasantPrefab,
                    animationSet,
                    peasantBody,
                    peasantOutfit,
                    headMeshes,
                    fogwalkerNeck,
                    fogwalkerFace,
                    characterEyes,
                    characterBrows),
                RangedEnemy = EnsureCharacterPrefab(
                    "EnemyVisual_RuneAcolyte",
                    RangerDerived,
                    RuneAcolytePrefab,
                    animationSet,
                    runeBody,
                    runeOutfit,
                    headMeshes,
                    runeNeck,
                    runeFace,
                    characterEyes,
                    characterBrows)
            };
        }

        private static void EnsureDerivedHumanoid(string sourcePath, string derivedPath)
        {
            string sourceAbsolute = Path.GetFullPath(sourcePath);
            string derivedAbsolute = Path.GetFullPath(derivedPath);
            FileInfo source = new FileInfo(sourceAbsolute);
            FileInfo derived = new FileInfo(derivedAbsolute);
            if (!derived.Exists || derived.Length != source.Length || derived.LastWriteTimeUtc != source.LastWriteTimeUtc)
            {
                File.Copy(sourceAbsolute, derivedAbsolute, true);
                File.SetLastWriteTimeUtc(derivedAbsolute, source.LastWriteTimeUtc);
            }

            AssetDatabase.ImportAsset(derivedPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(derivedPath) is not ModelImporter importer)
            {
                throw new InvalidDataException($"Derived character is not a model asset: {derivedPath}");
            }

            bool changed = importer.animationType != ModelImporterAnimationType.Human ||
                           importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
                           importer.importAnimation;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        private static GameObject EnsureCharacterPrefab(
            string rootName,
            string modelPath,
            string prefabPath,
            PlayerAnimationSet animationSet,
            Material bodyMaterial,
            Material outfitMaterial,
            CharacterHeadMeshes headMeshes,
            Material neckMaterial,
            Material faceMaterial,
            Material eyeMaterial,
            Material browMaterial)
        {
            GameObject source = LoadRequired<GameObject>(modelPath);
            var root = new GameObject(rootName);
            GameObject body = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject;
            if (body == null)
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw new InvalidOperationException($"Could not instantiate derived character: {modelPath}");
            }

            body.name = Path.GetFileNameWithoutExtension(modelPath);
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string sourceName = materials[i] != null ? materials[i].name.ToLowerInvariant() : string.Empty;
                    bool bodySlot = sourceName.Contains("base") || sourceName.Contains("body") || sourceName.Contains("skin");
                    materials[i] = bodySlot ? bodyMaterial : outfitMaterial;
                }
                renderer.sharedMaterials = materials;
            }

            Bounds sourceBounds = GetRendererBounds(body);
            float scale = 1.9f / sourceBounds.size.y;
            body.transform.localScale = Vector3.one * scale;
            body.transform.localPosition = new Vector3(
                -sourceBounds.center.x * scale,
                -1f - sourceBounds.min.y * scale,
                -sourceBounds.center.z * scale);
            body.transform.localRotation = Quaternion.identity;

            Animator animator = body.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw new InvalidOperationException($"Derived character does not expose a valid Humanoid Avatar: {modelPath}");
            }

            animator.runtimeAnimatorController = animationSet.Controller;
            animator.applyRootMotion = false;
            AttachCompleteHead(animator, headMeshes, neckMaterial, faceMaterial, eyeMaterial, browMaterial);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Could not save project character prefab: {prefabPath}");
            }
            return prefab;
        }

        private static void AttachCompleteHead(
            Animator animator,
            CharacterHeadMeshes meshes,
            Material neckMaterial,
            Material faceMaterial,
            Material eyeMaterial,
            Material browMaterial)
        {
            Transform headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (headBone == null || neckBone == null)
            {
                throw new InvalidOperationException("Character Humanoid has no head/neck bones for the complete head composition.");
            }

            CreateHeadPart(neckBone, "CompleteHead_Neck", meshes.Neck, neckMaterial);
            CreateHeadPart(headBone, "CompleteHead_Face", meshes.Face, faceMaterial);
            CreateHeadPart(headBone, "CompleteHead_Eyes", meshes.Eyes, eyeMaterial);
            CreateHeadPart(headBone, "CompleteHead_Brows", meshes.Brows, browMaterial);
        }

        private static void CreateHeadPart(Transform parent, string name, Mesh mesh, Material material)
        {
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(parent, false);
            part.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static CharacterHeadMeshes EnsureBaseHeadMeshes()
        {
            Directory.CreateDirectory(MeshRoot);
            GameObject source = LoadRequired<GameObject>(BaseMaleSource);
            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not instantiate complete base character: {BaseMaleSource}");
            }

            try
            {
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                Transform headBone = animator?.GetBoneTransform(HumanBodyBones.Head);
                Transform neckBone = animator?.GetBoneTransform(HumanBodyBones.Neck);
                if (headBone == null || neckBone == null)
                {
                    throw new InvalidOperationException("Complete base character has no Humanoid head/neck bones.");
                }

                SkinnedMeshRenderer body = FindSkinnedRenderer(instance, "SuperHero_Male");
                SkinnedMeshRenderer eyes = FindSkinnedRenderer(instance, "Eyes");
                SkinnedMeshRenderer brows = FindSkinnedRenderer(instance, "Eyebrows");
                int headIndex = Array.IndexOf(body.bones, headBone);
                int neckIndex = Array.IndexOf(body.bones, neckBone);
                if (headIndex < 0 || neckIndex < 0)
                {
                    throw new InvalidOperationException("Complete base character head/neck bones are not referenced by its body mesh.");
                }

                BoneWeight[] weights = body.sharedMesh.boneWeights;
                var includeHead = new bool[weights.Length];
                var includeNeck = new bool[weights.Length];
                for (int i = 0; i < weights.Length; i++)
                {
                    float headWeight = GetBoneWeight(weights[i], headIndex);
                    float neckWeight = GetBoneWeight(weights[i], neckIndex);
                    includeHead[i] = headWeight >= 0.5f;
                    includeNeck[i] = headWeight < 0.5f && neckWeight >= 0.25f;
                }

                Mesh neckMesh = SaveGeneratedMesh(
                    BaseMaleNeckMeshPath,
                    ExtractBoneLocalMesh(body, neckBone, includeNeck, "M_BaseMale_Neck"));
                Mesh faceMesh = SaveGeneratedMesh(
                    BaseMaleHeadMeshPath,
                    ExtractBoneLocalMesh(body, headBone, includeHead, "M_BaseMale_Head"));
                Mesh eyeMesh = SaveGeneratedMesh(
                    BaseMaleEyesMeshPath,
                    ExtractBoneLocalMesh(eyes, headBone, null, "M_BaseMale_Eyes"));
                Mesh browMesh = SaveGeneratedMesh(
                    BaseMaleBrowsMeshPath,
                    ExtractBoneLocalMesh(brows, headBone, null, "M_BaseMale_Brows"));
                return new CharacterHeadMeshes(neckMesh, faceMesh, eyeMesh, browMesh);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static SkinnedMeshRenderer FindSkinnedRenderer(GameObject root, string rendererName)
        {
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.name == rendererName && renderer.sharedMesh != null)
                {
                    return renderer;
                }
            }

            throw new InvalidDataException($"Complete base character renderer is missing: {rendererName}");
        }

        private static float GetBoneWeight(BoneWeight weight, int boneIndex)
        {
            float total = 0f;
            if (weight.boneIndex0 == boneIndex) total += weight.weight0;
            if (weight.boneIndex1 == boneIndex) total += weight.weight1;
            if (weight.boneIndex2 == boneIndex) total += weight.weight2;
            if (weight.boneIndex3 == boneIndex) total += weight.weight3;
            return total;
        }

        private static Mesh ExtractBoneLocalMesh(
            SkinnedMeshRenderer source,
            Transform targetBone,
            bool[] includedVertices,
            string meshName)
        {
            var baked = new Mesh();
            source.BakeMesh(baked);
            Vector3[] vertices = baked.vertices;
            Vector3[] normals = baked.normals;
            Vector4[] tangents = baked.tangents;
            Vector2[] uv = baked.uv;
            int[] triangles = baked.triangles;

            if (includedVertices != null && includedVertices.Length != vertices.Length)
            {
                UnityEngine.Object.DestroyImmediate(baked);
                throw new InvalidDataException($"Bone selection does not match baked vertex count for {source.name}.");
            }

            var remap = new Dictionary<int, int>();
            var outputVertices = new List<Vector3>();
            var outputNormals = new List<Vector3>();
            var outputTangents = new List<Vector4>();
            var outputUv = new List<Vector2>();
            var outputTriangles = new List<int>();

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (includedVertices != null &&
                    (!includedVertices[a] || !includedVertices[b] || !includedVertices[c]))
                {
                    continue;
                }

                outputTriangles.Add(AddHeadVertex(a));
                outputTriangles.Add(AddHeadVertex(b));
                outputTriangles.Add(AddHeadVertex(c));
            }

            int AddHeadVertex(int sourceIndex)
            {
                if (remap.TryGetValue(sourceIndex, out int existing))
                {
                    return existing;
                }

                int index = outputVertices.Count;
                remap.Add(sourceIndex, index);
                Vector3 worldPosition = source.transform.TransformPoint(vertices[sourceIndex]);
                outputVertices.Add(targetBone.InverseTransformPoint(worldPosition));

                if (normals.Length == vertices.Length)
                {
                    Vector3 worldNormal = source.transform.TransformDirection(normals[sourceIndex]);
                    outputNormals.Add(targetBone.InverseTransformDirection(worldNormal).normalized);
                }

                if (tangents.Length == vertices.Length)
                {
                    Vector4 tangent = tangents[sourceIndex];
                    Vector3 worldTangent = source.transform.TransformDirection(
                        new Vector3(tangent.x, tangent.y, tangent.z));
                    Vector3 localTangent = targetBone.InverseTransformDirection(worldTangent).normalized;
                    outputTangents.Add(new Vector4(localTangent.x, localTangent.y, localTangent.z, tangent.w));
                }

                outputUv.Add(uv.Length == vertices.Length ? uv[sourceIndex] : Vector2.zero);
                return index;
            }

            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(outputVertices);
            mesh.SetTriangles(outputTriangles, 0);
            mesh.SetUVs(0, outputUv);
            if (outputNormals.Count == outputVertices.Count) mesh.SetNormals(outputNormals);
            else mesh.RecalculateNormals();
            if (outputTangents.Count == outputVertices.Count) mesh.SetTangents(outputTangents);
            mesh.RecalculateBounds();
            UnityEngine.Object.DestroyImmediate(baked);

            if (mesh.vertexCount == 0 || outputTriangles.Count == 0)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw new InvalidDataException($"Generated character part is empty: {meshName}");
            }

            return mesh;
        }

        private static Mesh SaveGeneratedMesh(string path, Mesh generated)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            existing.name = generated.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(generated);
            return existing;
        }

        private static Material EnsureCharacterMaterial(
            string name,
            string albedoPath,
            string normalPath,
            Color tint)
        {
            string path = $"{CharacterMaterialRoot}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", LoadRequired<Texture2D>(albedoPath));
            material.SetTexture("_BumpMap", LoadRequired<Texture2D>(normalPath));
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Smoothness", 0.16f);
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureSolidCharacterMaterial(string name, Color color, float smoothness)
        {
            string path = $"{CharacterMaterialRoot}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", null);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void TuneEmberValleyLighting()
        {
            RenderSettings.ambientLight = new Color(0.3f, 0.34f, 0.325f);
            RenderSettings.fogColor = new Color(0.09f, 0.115f, 0.105f);
            RenderSettings.fogStartDistance = 22f;
            RenderSettings.fogEndDistance = 68f;
            Light directional = UnityEngine.Object.FindObjectOfType<Light>();
            if (directional != null && directional.type == LightType.Directional)
            {
                directional.color = new Color(1f, 0.86f, 0.72f);
                directional.intensity = 1.55f;
                directional.shadowStrength = 0.68f;
            }
        }

        private static void ReplaceCriticalRoutePlaceholders(
            Transform artRoot,
            GameObject scoutVisualPrefab,
            ArtPalette palette)
        {
            GameObject scout = GameObject.Find("NPC_CampScout");
            if (scout != null)
            {
                Renderer capsule = scout.GetComponent<Renderer>();
                if (capsule != null) capsule.enabled = false;
                GameObject oldMarker = GameObject.Find("Scout_EmberMarker");
                if (oldMarker != null && oldMarker.TryGetComponent(out Renderer oldMarkerRenderer))
                {
                    oldMarkerRenderer.enabled = false;
                }
                GameObject visual = PrefabUtility.InstantiatePrefab(scoutVisualPrefab, scout.transform) as GameObject;
                if (visual != null)
                {
                    visual.name = "ScoutVisual_Peasant";
                    visual.transform.localPosition = Vector3.zero;
                    visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
                    {
                        UnityEngine.Object.DestroyImmediate(collider);
                    }
                }

                CreateQuestMarker(scout, palette.RouteGlow);
            }

            foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (renderer.gameObject.name.StartsWith("CourtyardPillar_", StringComparison.Ordinal) ||
                    renderer.gameObject.name == "Camp_EmberStone")
                {
                    renderer.enabled = false;
                }
            }

            string[] seals = { "Seal_Forest", "Seal_Bridge", "Seal_Courtyard" };
            for (int i = 0; i < seals.Length; i++)
            {
                GameObject seal = GameObject.Find(seals[i]);
                if (seal != null)
                {
                    CreateFitted(
                        artRoot,
                        $"{seals[i]}_StoneShell",
                        i % 2 == 0 ? Rock1 : Rock2,
                        new Vector3(seal.transform.position.x, 0f, seal.transform.position.z),
                        25f + (i * 47f),
                        1.45f,
                        palette,
                        ArtKind.Nature);
                    AttachRouteGlow(seal, palette.RouteGlow, palette.VillageStone, 0.24f);
                }
            }

            AttachBarrierFacade("GateBlocker_Sanctum", VillageArch, 4.2f, palette);
            AttachBarrierFacade("GateBlocker_ReturnShortcut", VillageFence, 2.2f, palette);
            AttachBarrierFacade("GateBlocker_WardenEncounter", VillageFence, 2.8f, palette);
            AttachRouteGlow(GameObject.Find("Checkpoint_Courtyard"), palette.CheckpointGlow, palette.VillageStone, 0.22f);
            AttachRouteGlow(GameObject.Find("GateControl_Sanctum"), palette.RouteGlow, palette.VillageStone, 0.22f);
        }

        private static void CreateQuestMarker(GameObject scout, Material material)
        {
            Mesh runeMesh = EnsureRuneMesh();
            var marker = new GameObject("Scout_QuestMarker", typeof(MeshFilter), typeof(MeshRenderer));
            marker.transform.SetParent(scout.transform, false);
            marker.transform.localPosition = new Vector3(0f, 1.58f, 0f);
            marker.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            marker.transform.localScale = Vector3.one * 0.23f;
            marker.GetComponent<MeshFilter>().sharedMesh = runeMesh;
            Renderer renderer = marker.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            M2RouteInteractable interactable = scout.GetComponent<M2RouteInteractable>();
            if (interactable != null)
            {
                interactable.SetArtIndicator(renderer);
            }

            Light light = CreateHighlightLight(marker.transform, material.color, new Vector3(0f, 0.2f, 0f));
            marker.AddComponent<M2QuestHighlightPresenter>().Configure(renderer, null, light);
        }

        private static void AttachRouteGlow(
            GameObject owner,
            Material glowMaterial,
            Material pedestalMaterial,
            float worldScale)
        {
            if (owner == null) return;
            Renderer sourceRenderer = owner.GetComponent<Renderer>();
            float groundY = sourceRenderer != null ? sourceRenderer.bounds.min.y : owner.transform.position.y;
            Vector3 position = new Vector3(owner.transform.position.x, groundY + 0.62f, owner.transform.position.z);
            if (sourceRenderer != null) sourceRenderer.enabled = false;

            GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = owner.name + "_RunePedestal";
            pedestal.transform.position = new Vector3(position.x, groundY + 0.11f, position.z);
            pedestal.transform.localScale = new Vector3(0.58f, 0.11f, 0.58f);
            pedestal.GetComponent<Renderer>().sharedMaterial = pedestalMaterial;
            UnityEngine.Object.DestroyImmediate(pedestal.GetComponent<Collider>());
            pedestal.transform.SetParent(owner.transform, true);

            var glow = new GameObject(owner.name + "_RuneCore", typeof(MeshFilter), typeof(MeshRenderer));
            glow.transform.position = position;
            glow.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            glow.transform.localScale = Vector3.one * worldScale;
            glow.GetComponent<MeshFilter>().sharedMesh = EnsureRuneMesh();
            Renderer glowRenderer = glow.GetComponent<Renderer>();
            glowRenderer.sharedMaterial = glowMaterial;
            glow.transform.SetParent(owner.transform, true);

            var ring = new GameObject(owner.name + "_HighlightRing", typeof(MeshFilter), typeof(MeshRenderer));
            ring.transform.position = new Vector3(position.x, groundY + 0.025f, position.z);
            ring.transform.localScale = Vector3.one * Mathf.Max(0.8f, worldScale * 4.4f);
            ring.GetComponent<MeshFilter>().sharedMesh = EnsureRuneRingMesh();
            Renderer ringRenderer = ring.GetComponent<Renderer>();
            ringRenderer.sharedMaterial = glowMaterial;
            ring.transform.SetParent(owner.transform, true);

            Light light = CreateHighlightLight(owner.transform, glowMaterial.color, new Vector3(0f, 1.1f, 0f));
            glow.AddComponent<M2QuestHighlightPresenter>().Configure(glowRenderer, ringRenderer, light);
            M2RouteInteractable interactable = owner.GetComponent<M2RouteInteractable>();
            if (interactable != null)
            {
                interactable.SetArtIndicator(glowRenderer);
            }
        }

        private static Light CreateHighlightLight(Transform parent, Color color, Vector3 localPosition)
        {
            var lightObject = new GameObject("QuestHighlightLight", typeof(Light));
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = localPosition;
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = 4.5f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
            return light;
        }

        private static Mesh EnsureRuneMesh()
        {
            Directory.CreateDirectory(MeshRoot);
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(RuneMeshPath);
            if (existing != null)
            {
                return existing;
            }

            var mesh = new Mesh { name = "M_RuneOctahedron" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(0f, -1f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(-1f, 0f, 0f),
                new Vector3(0f, 0f, 1f),
                new Vector3(0f, 0f, -1f)
            };
            mesh.triangles = new[]
            {
                0, 4, 2, 0, 3, 4, 0, 5, 3, 0, 2, 5,
                1, 2, 4, 1, 4, 3, 1, 3, 5, 1, 5, 2
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, RuneMeshPath);
            return mesh;
        }

        private static Mesh EnsureRuneRingMesh()
        {
            Directory.CreateDirectory(MeshRoot);
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(RuneRingMeshPath);
            if (existing != null)
            {
                return existing;
            }

            const int segments = 32;
            const float innerRadius = 0.72f;
            const float outerRadius = 1f;
            var vertices = new Vector3[segments * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = (Mathf.PI * 2f * i) / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * innerRadius;
                vertices[(i * 2) + 1] = direction * outerRadius;

                int next = (i + 1) % segments;
                int triangle = i * 6;
                triangles[triangle] = i * 2;
                triangles[triangle + 1] = next * 2;
                triangles[triangle + 2] = (i * 2) + 1;
                triangles[triangle + 3] = (i * 2) + 1;
                triangles[triangle + 4] = next * 2;
                triangles[triangle + 5] = (next * 2) + 1;
            }

            var mesh = new Mesh { name = "M_RuneHighlightRing" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, RuneRingMeshPath);
            return mesh;
        }

        private static void AttachBarrierFacade(string blockerName, string modelPath, float targetHeight, ArtPalette palette)
        {
            GameObject blocker = GameObject.Find(blockerName);
            if (blocker == null)
            {
                foreach (Transform candidate in UnityEngine.Object.FindObjectsOfType<Transform>(true))
                {
                    if (candidate.name != blockerName) continue;
                    blocker = candidate.gameObject;
                    break;
                }
            }
            if (blocker == null) return;
            Renderer renderer = blocker.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
            GameObject facade = CreateFitted(
                blocker.transform,
                blockerName + "_ArtFacade",
                modelPath,
                new Vector3(blocker.transform.position.x, 0f, blocker.transform.position.z),
                blocker.transform.eulerAngles.y,
                targetHeight,
                palette,
                ArtKind.Village,
                false);
            facade.AddComponent<CameraOccluder>();
        }

        private static void CreateRoutePath(Transform root, ArtPalette palette)
        {
            Vector3[] points =
            {
                new Vector3(0f, 0f, 8f), new Vector3(-0.8f, 0f, 14f), new Vector3(0.7f, 0f, 20f),
                new Vector3(-0.4f, 0f, 27f), new Vector3(0.5f, 0f, 34f), new Vector3(3.5f, 0f, 40f),
                new Vector3(9f, 0f, 44f), new Vector3(16f, 0f, 45f), new Vector3(23f, 0f, 46f),
                new Vector3(30f, 0f, 45f), new Vector3(35f, 0f, 40f), new Vector3(39f, 0f, 34f),
                new Vector3(42f, 0f, 28f), new Vector3(45f, 0f, 22f), new Vector3(46f, 0f, 16f)
            };
            string[] models = { PathRock1, PathRock2, PathRock3 };
            for (int i = 0; i < points.Length; i++)
            {
                CreateFitted(root, $"RouteStone_{i:00}", models[i % models.Length], points[i], i * 37f, 0.09f, palette, ArtKind.Nature);
            }
        }

        private static void CreateCamp(Transform root, ArtPalette palette)
        {
            for (int i = 0; i < 4; i++)
            {
                CreateFitted(root, $"CampWall_{i:00}", PlasterWall, new Vector3(-6f + (i * 4f), 0f, -8.65f), 180f, 3.25f, palette, ArtKind.Village);
            }

            CreateFitted(root, "CampCart", Cart, new Vector3(-5.2f, 0f, -3.8f), 32f, 1.8f, palette, ArtKind.Props);
            CreateFitted(root, "CampWorkbench", Workbench, new Vector3(5.2f, 0f, -5.8f), -18f, 1.2f, palette, ArtKind.Props);
            CreateFitted(root, "CampBench", Bench, new Vector3(3.8f, 0f, 3.8f), -130f, 0.9f, palette, ArtKind.Props);
            CreateFitted(root, "CampBarrel_A", Barrel, new Vector3(-3.7f, 0f, -6.7f), 12f, 1.05f, palette, ArtKind.Props);
            CreateFitted(root, "CampBarrel_B", Barrel, new Vector3(-2.5f, 0f, -6.5f), -9f, 0.95f, palette, ArtKind.Props);
            CreateFitted(root, "CampCrate_A", Crate, new Vector3(6.4f, 0f, -6.1f), 8f, 1f, palette, ArtKind.Props);
            CreateFitted(root, "CampFence_West", VillageFence, new Vector3(-8.3f, 0f, -1f), 90f, 1.25f, palette, ArtKind.Village);
            CreateFitted(root, "CampTree", CommonTree1, new Vector3(-9.4f, 0f, 5.8f), 21f, 5.4f, palette, ArtKind.Nature);
        }

        private static void CreateForest(Transform root, ArtPalette palette)
        {
            string[] trees = { CommonTree1, CommonTree2, CommonTree3, TwistedTree1, DeadTree1 };
            for (int i = 0; i < 9; i++)
            {
                float z = 13f + (i * 4.1f);
                float height = 4.55f + ((i % 3) * 0.38f);
                CreateFitted(root, $"ForestTree_West_{i:00}", trees[i % trees.Length], new Vector3(-13.2f - ((i % 2) * 0.8f), 0f, z), i * 31f, height, palette, ArtKind.Nature);
                CreateFitted(root, $"ForestTree_East_{i:00}", trees[(i + 2) % trees.Length], new Vector3(13.2f + ((i % 2) * 0.8f), 0f, z), -i * 27f, height + 0.2f, palette, ArtKind.Nature);

                if (i % 2 == 0)
                {
                    CreateFitted(root, $"ForestBush_West_{i:00}", i % 4 == 0 ? BushFlowers : Bush, new Vector3(-9.8f, 0f, z + 1.2f), i * 19f, 0.95f, palette, ArtKind.Nature);
                    CreateFitted(root, $"ForestRock_East_{i:00}", i % 4 == 0 ? Rock1 : Rock2, new Vector3(9.6f, 0f, z - 0.8f), i * 23f, 0.78f, palette, ArtKind.Nature);
                }
            }

            CreateFitted(root, "ForestFern_A", Fern, new Vector3(-6.7f, 0f, 28f), 18f, 0.7f, palette, ArtKind.Nature);
            CreateFitted(root, "ForestFern_B", Fern, new Vector3(6.9f, 0f, 36f), -34f, 0.68f, palette, ArtKind.Nature);
        }

        private static void DecorateForestGameplay(Transform root, ArtPalette palette)
        {
            HideGameplayProxy("CoverProxy_Forest_West");
            HideGameplayProxy("CoverProxy_Forest_East");
            HideGameplayProxy("SupplyCache_ForestBranch");

            CreateFitted(root, "ForestCoverRock_West", Rock1, new Vector3(-2.8f, 0f, 31.7f), 18f, 2.25f, palette, ArtKind.Nature);
            CreateFitted(root, "ForestCoverRock_East", Rock2, new Vector3(3.4f, 0f, 35.4f), -27f, 2.4f, palette, ArtKind.Nature);
            GameObject cacheArt = CreateFitted(root, "ForestSupplyCache_Art", Crate, new Vector3(-6.7f, 0f, 34.5f), 22f, 1.15f, palette, ArtKind.Props);
            GameObject cacheOwner = GameObject.Find("SupplyCache_ForestBranch");
            if (cacheOwner != null)
            {
                cacheArt.transform.SetParent(cacheOwner.transform, true);
            }
            CreateFitted(root, "SupplyBranchStone_A", PathRock2, new Vector3(-2.4f, 0f, 31.5f), 28f, 0.1f, palette, ArtKind.Nature);
            CreateFitted(root, "SupplyBranchStone_B", PathRock1, new Vector3(-4.4f, 0f, 33f), -18f, 0.1f, palette, ArtKind.Nature);
            CreateFitted(root, "SupplyBranchStone_C", PathRock3, new Vector3(-6.2f, 0f, 34.1f), 42f, 0.1f, palette, ArtKind.Nature);
            AttachBarrierFacade("GateBlocker_ForestShortcut", VillageFence, 2.2f, palette);
        }

        private static void HideGameplayProxy(string name)
        {
            GameObject proxy = GameObject.Find(name);
            if (proxy != null && proxy.TryGetComponent(out Renderer renderer))
            {
                renderer.enabled = false;
            }
        }

        private static void CreateBridge(Transform root, ArtPalette palette)
        {
            for (int i = 0; i < 4; i++)
            {
                float x = 7f + (i * 3.5f);
                CreateFitted(root, $"BridgeFence_North_{i:00}", VillageFence, new Vector3(x, 0f, 47.1f), 0f, 1.15f, palette, ArtKind.Village);
                CreateFitted(root, $"BridgeFence_South_{i:00}", VillageFence, new Vector3(x, 0f, 40.9f), 180f, 1.15f, palette, ArtKind.Village);
            }

            CreateFitted(root, "BridgeDeadTree", DeadTree1, new Vector3(12f, 0f, 53f), 47f, 5.3f, palette, ArtKind.Nature);
        }

        private static void CreateCourtyard(Transform root, ArtPalette palette)
        {
            for (int i = 0; i < 5; i++)
            {
                CreateFitted(root, $"CourtyardWall_{i:00}", i == 2 ? VillageArch : VillageWall, new Vector3(18f + (i * 4f), 0f, 53.65f), 180f, 3.5f, palette, ArtKind.Village);
            }

            CreateFitted(root, "CourtyardBannerFrame", Banner, new Vector3(30f, 0f, 53.1f), 180f, 3.25f, palette, ArtKind.Props);
            CreateFitted(root, "CourtyardTree", TwistedTree1, new Vector3(12.5f, 0f, 50.5f), 12f, 5.5f, palette, ArtKind.Nature);
            CreateFitted(root, "CourtyardCrate", Crate, new Vector3(34f, 0f, 49f), 22f, 1f, palette, ArtKind.Props);
            CreateFitted(root, "SanctumArch", VillageArch, new Vector3(40f, 0f, 32.5f), 0f, 4.2f, palette, ArtKind.Village);
            for (int i = 0; i < 5; i++)
            {
                CreateFitted(root, $"CourtyardParapet_South_{i:00}", VillageWall, new Vector3(18f + (i * 4f), 0f, 34.25f), 0f, 1.25f, palette, ArtKind.Village);
            }

            for (int i = 0; i < 4; i++)
            {
                CreateFitted(root, $"CourtyardParapet_West_{i:00}", VillageWall, new Vector3(15.25f, 0f, 38f + (i * 4f)), 90f, 1.25f, palette, ArtKind.Village);
            }
        }

        private static void CreateWardenArena(Transform root, ArtPalette palette)
        {
            for (int i = 0; i < 5; i++)
            {
                CreateFitted(root, $"WardenEastWall_{i:00}", VillageWall, new Vector3(56.65f, 0f, 12f + (i * 4f)), 270f, 4f, palette, ArtKind.Village);
                CreateFitted(root, $"WardenSouthWall_{i:00}", VillageWall, new Vector3(38f + (i * 4f), 0f, 9.35f), 0f, 4f, palette, ArtKind.Village);
            }

            CreateFitted(root, "WardenTree", DeadTree1, new Vector3(59.5f, 0f, 26f), -32f, 5.8f, palette, ArtKind.Nature);
            CreateFitted(root, "WardenRock_A", Rock1, new Vector3(39f, 0f, 27f), 18f, 1.4f, palette, ArtKind.Nature);
            CreateFitted(root, "WardenRock_B", Rock2, new Vector3(52f, 0f, 13f), -20f, 1.1f, palette, ArtKind.Nature);
            for (int i = 0; i < 5; i++)
            {
                CreateFitted(root, $"WardenNorthWall_{i:00}", VillageWall, new Vector3(38f + (i * 4f), 0f, 30.65f), 180f, 3.2f, palette, ArtKind.Village);
                CreateFitted(root, $"WardenWestWall_{i:00}", VillageWall, new Vector3(35.35f, 0f, 12f + (i * 4f)), 90f, 3.2f, palette, ArtKind.Village);
            }
        }

        private static void CreateReturnRouteShell(Transform root, ArtPalette palette)
        {
            for (int i = 0; i < 7; i++)
            {
                float x = 17f + (i * 3.7f);
                CreateFitted(root, $"ReturnFence_North_{i:00}", VillageFence, new Vector3(x, 0f, 13.3f), 0f, 1.05f, palette, ArtKind.Village);
                CreateFitted(root, $"ReturnFence_South_{i:00}", VillageFence, new Vector3(x, 0f, 6.7f), 180f, 1.05f, palette, ArtKind.Village);
            }

            CreateFitted(root, "ReturnRouteRock_A", Rock1, new Vector3(22f, 0f, 14.2f), 22f, 1.05f, palette, ArtKind.Nature);
            CreateFitted(root, "ReturnRouteRock_B", Rock2, new Vector3(33f, 0f, 5.9f), -18f, 0.9f, palette, ArtKind.Nature);
        }

        private static void CreateGroundingAndCliffShell(
            Transform root,
            Transform supportRoot,
            ArtPalette palette)
        {
            CreateVisualShellWithSupport(root, supportRoot, "ForestShoulder_West", new Vector3(-14f, -0.45f, 29f), new Vector3(10f, 0.9f, 40f), palette.RouteEarth);
            CreateVisualShellWithSupport(root, supportRoot, "ForestShoulder_East", new Vector3(14f, -0.45f, 29f), new Vector3(10f, 0.9f, 40f), palette.RouteEarth);
            CreateVisualShellWithSupport(root, supportRoot, "CampTreeBerm", new Vector3(-11.5f, -0.32f, 5.5f), new Vector3(5f, 0.64f, 7f), palette.RouteEarth);

            CreateVisualShellWithSupport(root, supportRoot, "BridgeCliffShell", new Vector3(12f, -1.3f, 44f), new Vector3(10f, 2.2f, 11f), palette.DistantGround);
            CreateVisualShellWithSupport(root, supportRoot, "CourtyardCliffShell", new Vector3(26f, -1.3f, 44f), new Vector3(26f, 2.2f, 24f), palette.DistantGround);
            CreateVisualShellWithSupport(root, supportRoot, "SanctumCliffShell", new Vector3(39f, -1.3f, 32.5f), new Vector3(10f, 2.2f, 7f), palette.DistantGround);
            CreateVisualShellWithSupport(root, supportRoot, "WardenCliffShell", new Vector3(46f, -1.3f, 20f), new Vector3(26f, 2.2f, 26f), palette.DistantGround);
            CreateVisualShellWithSupport(root, supportRoot, "ReturnCliffShell", new Vector3(22f, -1.3f, 10f), new Vector3(30f, 2.2f, 11f), palette.DistantGround);

            CreateFitted(root, "ForestGroundRock_West_A", Rock1, new Vector3(-8.8f, 0f, 18f), 18f, 1.15f, palette, ArtKind.Nature);
            CreateFitted(root, "ForestGroundRock_East_A", Rock2, new Vector3(8.7f, 0f, 25f), -24f, 1.05f, palette, ArtKind.Nature);
            CreateFitted(root, "ForestGroundRock_West_B", Rock2, new Vector3(-9.2f, 0f, 39f), 41f, 0.95f, palette, ArtKind.Nature);
            CreateFitted(root, "CourtyardEdgeRock", Rock1, new Vector3(14.5f, 0f, 36f), 12f, 1.3f, palette, ArtKind.Nature);
            CreateFitted(root, "WardenEdgeRock", Rock2, new Vector3(58f, 0f, 17f), -31f, 1.25f, palette, ArtKind.Nature);
        }

        private static void CreateVisualShellBlock(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.SetPositionAndRotation(position, Quaternion.identity);
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(block.GetComponent<Collider>());
            SetStaticRecursively(block);
        }

        private static void CreateVisualShellWithSupport(
            Transform visualRoot,
            Transform supportRoot,
            string name,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            CreateVisualShellBlock(visualRoot, name, position, scale, material);
            var proxy = new GameObject(name + "_TerrainSupportProxy", typeof(BoxCollider));
            proxy.transform.SetParent(supportRoot, false);
            proxy.transform.SetPositionAndRotation(position, Quaternion.identity);
            proxy.transform.localScale = scale;
            SetStaticRecursively(proxy);
        }

        private static void ApplySurfaceMaterials(ArtPalette palette)
        {
            string[] stoneSurfaces = { "Path_Bridge", "Zone_Courtyard", "Path_Sanctum", "Zone_Warden" };
            foreach (string name in stoneSurfaces)
            {
                GameObject surface = GameObject.Find(name);
                if (surface != null && surface.TryGetComponent(out Renderer renderer))
                {
                    renderer.sharedMaterial = palette.RouteStone;
                }
            }

            string[] earthSurfaces = { "Zone_Camp", "Path_Forest", "Zone_Forest", "Path_Return" };
            foreach (string name in earthSurfaces)
            {
                GameObject surface = GameObject.Find(name);
                if (surface != null && surface.TryGetComponent(out Renderer renderer))
                {
                    renderer.sharedMaterial = palette.RouteEarth;
                }
            }

            string[] walls = { "Camp_BackWall", "Courtyard_NorthWall", "Warden_EastWall", "Warden_SouthWall" };
            foreach (string name in walls)
            {
                GameObject wall = GameObject.Find(name);
                if (wall != null && wall.TryGetComponent(out Renderer renderer))
                {
                    renderer.sharedMaterial = palette.VillageStone;
                }
            }
        }

        private static void CreateDistantGround(Transform root, ArtPalette palette)
        {
            GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backdrop.name = "ValleyFloor_VisualOnly";
            backdrop.transform.SetParent(root, false);
            backdrop.transform.SetPositionAndRotation(new Vector3(24f, -5.2f, 24f), Quaternion.identity);
            backdrop.transform.localScale = new Vector3(150f, 1f, 150f);
            backdrop.GetComponent<Renderer>().sharedMaterial = palette.DistantGround;
            UnityEngine.Object.DestroyImmediate(backdrop.GetComponent<Collider>());
            SetStaticRecursively(backdrop);
        }

        private static void MarkCameraOccluders(Transform root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (!IsCameraOccluderName(child.name) ||
                    child.GetComponentInParent<CameraOccluder>() != null)
                {
                    continue;
                }

                child.gameObject.AddComponent<CameraOccluder>();
            }
        }

        private static bool IsCameraOccluderName(string name)
        {
            return name.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Arch", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Facade", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("BannerFrame", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static GameObject CreateFitted(
            Transform parent,
            string name,
            string modelPath,
            Vector3 groundPosition,
            float yaw,
            float targetHeight,
            ArtPalette palette,
            ArtKind kind,
            bool markStatic = true)
        {
            GameObject source = LoadRequired<GameObject>(modelPath);
            GameObject instance = PrefabUtility.InstantiatePrefab(source, parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not instantiate art model: {modelPath}");
            }

            instance.name = name;
            instance.transform.SetPositionAndRotation(groundPosition, Quaternion.Euler(0f, yaw, 0f));
            instance.transform.localScale = Vector3.one;
            ReplaceMaterials(instance, palette, kind);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            Bounds bounds = GetRendererBounds(instance);
            if (bounds.size.y <= 0.001f)
            {
                throw new InvalidDataException($"Art model has no measurable renderer bounds: {modelPath}");
            }

            float scale = Mathf.Clamp(targetHeight / bounds.size.y, 0.05f, 20f);
            instance.transform.localScale = Vector3.one * scale;
            bounds = GetRendererBounds(instance);
            instance.transform.position += Vector3.up * (groundPosition.y - bounds.min.y);
            if (markStatic)
            {
                SetStaticRecursively(instance);
            }
            return instance;
        }

        private static Bounds GetRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static void ReplaceMaterials(GameObject root, ArtPalette palette, ArtKind kind)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string sourceName = materials[i] != null ? materials[i].name.ToLowerInvariant() : string.Empty;
                    materials[i] = ResolveMaterial(sourceName, palette, kind);
                }

                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static Material ResolveMaterial(string sourceName, ArtPalette palette, ArtKind kind)
        {
            switch (kind)
            {
                case ArtKind.Nature:
                    if (sourceName.Contains("bark")) return palette.NatureBark;
                    if (sourceName.Contains("rock") || sourceName.Contains("pebble")) return palette.NatureRock;
                    if (sourceName.Contains("grass") || sourceName.Contains("flower") || sourceName.Contains("fern")) return palette.NatureGrass;
                    return palette.NatureLeaves;
                case ArtKind.Village:
                    if (sourceName.Contains("wood")) return palette.VillageWood;
                    if (sourceName.Contains("plaster")) return palette.VillagePlaster;
                    return palette.VillageStone;
                case ArtKind.Props:
                    if (sourceName.Contains("metal")) return palette.PropMetal;
                    if (sourceName.Contains("cloth")) return palette.PropCloth;
                    return palette.PropWood;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static void SetStaticRecursively(GameObject root)
        {
            StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, flags);
            }
        }

        private static void HidePlaceholderTreeRenderers()
        {
            foreach (Renderer renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (renderer.gameObject.name.StartsWith("Tree_West_", StringComparison.Ordinal) ||
                    renderer.gameObject.name.StartsWith("Tree_East_", StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                }
            }
        }

        private static ArtPalette EnsurePalette()
        {
            Directory.CreateDirectory(MaterialRoot);
            AssetDatabase.Refresh();
            return new ArtPalette
            {
                NatureBark = EnsureMaterial("M_Art_Nature_Bark", NatureRoot + "/Textures/Bark_NormalTree.png", NatureRoot + "/Textures/Bark_NormalTree_Normal.png", new Color(0.62f, 0.55f, 0.48f), 0.08f),
                NatureLeaves = EnsureMaterial("M_Art_Nature_Leaves", NatureRoot + "/Textures/Leaves_NormalTree.png", null, new Color(0.32f, 0.52f, 0.39f), 0.04f, true),
                NatureGrass = EnsureMaterial("M_Art_Nature_Grass", NatureRoot + "/Textures/Grass.png", null, new Color(0.3f, 0.49f, 0.36f), 0.03f, true),
                NatureRock = EnsureMaterial("M_Art_Nature_Rock", NatureRoot + "/Textures/Rocks_Diffuse.png", null, new Color(0.57f, 0.62f, 0.6f), 0.08f),
                RouteEarth = EnsureTiledMaterial("M_Art_RouteEarth", NatureRoot + "/Textures/Rocks_Diffuse.png", new Color(0.27f, 0.34f, 0.29f), 0.04f, new Vector2(5f, 5f)),
                RouteStone = EnsureTiledMaterial("M_Art_RouteStone", VillageRoot + "/Textures/T_UnevenBrick_BaseColor.png", new Color(0.5f, 0.56f, 0.53f), 0.08f, new Vector2(4f, 4f)),
                DistantGround = EnsureTiledMaterial("M_Art_DistantGround", NatureRoot + "/Textures/Rocks_Diffuse.png", new Color(0.2f, 0.26f, 0.23f), 0.01f, new Vector2(20f, 20f)),
                VillageStone = EnsureMaterial("M_Art_Village_Stone", VillageRoot + "/Textures/T_UnevenBrick_BaseColor.png", VillageRoot + "/Textures/T_UnevenBrick_Normal.png", new Color(0.7f, 0.76f, 0.73f), 0.1f),
                VillageWood = EnsureMaterial("M_Art_Village_Wood", VillageRoot + "/Textures/T_WoodTrim_BaseColor.png", VillageRoot + "/Textures/T_WoodTrim_Normal.png", new Color(0.72f, 0.66f, 0.58f), 0.12f),
                VillagePlaster = EnsureMaterial("M_Art_Village_Plaster", VillageRoot + "/Textures/T_Plaster_BaseColor.png", VillageRoot + "/Textures/T_Plaster_Normal.png", new Color(0.68f, 0.72f, 0.66f), 0.09f),
                PropWood = EnsureMaterial("M_Art_Props_Wood", PropsRoot + "/Textures/T_Trim_Furniture_BaseColor.png", PropsRoot + "/Textures/T_Trim_Furniture_Normal.png", new Color(0.74f, 0.68f, 0.58f), 0.15f),
                PropMetal = EnsureMaterial("M_Art_Props_Metal", PropsRoot + "/Textures/T_Trim_Metal_BaseColor.png", PropsRoot + "/Textures/T_Trim_Metal_Normal.png", new Color(0.72f, 0.76f, 0.78f), 0.42f),
                PropCloth = EnsureMaterial("M_Art_Props_Cloth", PropsRoot + "/Textures/T_Trim_Cloth_BaseColor.png", PropsRoot + "/Textures/T_Trim_Cloth_Normal.png", new Color(0.72f, 0.3f, 0.18f), 0.04f),
                RouteGlow = EnsureGlowMaterial("M_Art_RouteGlow", new Color(1f, 0.34f, 0.045f), new Color(3.2f, 0.55f, 0.06f)),
                CheckpointGlow = EnsureGlowMaterial("M_Art_CheckpointGlow", new Color(0.08f, 0.7f, 0.62f), new Color(0.08f, 2.1f, 1.55f))
            };
        }

        private static Material EnsureTiledMaterial(
            string name,
            string albedoPath,
            Color tint,
            float smoothness,
            Vector2 tiling)
        {
            Material material = EnsureMaterial(name, albedoPath, null, tint, smoothness);
            material.SetTextureScale("_BaseMap", tiling);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureGlowMaterial(string name, Color color, Color emission)
        {
            string path = $"{MaterialRoot}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetColor("_EmissionColor", emission);
            material.SetFloat("_Smoothness", 0.72f);
            material.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureMaterial(string name, string albedoPath, string normalPath, Color tint, float smoothness, bool alphaClip = false)
        {
            string path = $"{MaterialRoot}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                {
                    throw new InvalidOperationException("URP Lit shader is unavailable.");
                }

                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", LoadRequired<Texture2D>(albedoPath));
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Smoothness", smoothness);
            if (!string.IsNullOrWhiteSpace(normalPath))
            {
                material.SetTexture("_BumpMap", LoadRequired<Texture2D>(normalPath));
                material.EnableKeyword("_NORMALMAP");
            }

            if (alphaClip)
            {
                material.SetFloat("_AlphaClip", 1f);
                material.SetFloat("_Cutoff", 0.42f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureTextureImports()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { SourceRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                {
                    continue;
                }

                bool isNormal = path.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                path.IndexOf("Normals-UnrealEngine", StringComparison.OrdinalIgnoreCase) < 0;
                bool alpha = path.IndexOf("Leaves", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             path.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             path.IndexOf("Flower", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             path.IndexOf("Vine", StringComparison.OrdinalIgnoreCase) >= 0;
                bool changed = importer.maxTextureSize != 1024 ||
                               importer.textureCompression != TextureImporterCompression.Compressed ||
                               importer.textureType != (isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default) ||
                               importer.alphaIsTransparency != alpha;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.alphaIsTransparency = alpha;
                if (changed)
                {
                    importer.SaveAndReimport();
                }
            }
        }

        private static void ValidateSourceLicenses()
        {
            string[] licenses =
            {
                NatureRoot + "/License_Standard.txt",
                VillageRoot + "/License_Standard.txt",
                PropsRoot + "/License_Standard.txt",
                SourceRoot + "/CharacterOutfits/License_Standard.txt",
                "Assets/_Game/Art/Universal Base Characters[Standard]/Universal Base Characters[Standard]/License_Standard.txt"
            };
            foreach (string path in licenses)
            {
                TextAsset license = LoadRequired<TextAsset>(path);
                if (license.text.IndexOf("CC0 1.0", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    throw new InvalidDataException($"Art source does not declare CC0 1.0: {path}");
                }
            }
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException($"Required M3 art asset is missing: {path}", path);
            }

            return asset;
        }

        private sealed class ArtPalette
        {
            public Material NatureBark;
            public Material NatureLeaves;
            public Material NatureGrass;
            public Material NatureRock;
            public Material RouteEarth;
            public Material RouteStone;
            public Material DistantGround;
            public Material VillageStone;
            public Material VillageWood;
            public Material VillagePlaster;
            public Material PropWood;
            public Material PropMetal;
            public Material PropCloth;
            public Material RouteGlow;
            public Material CheckpointGlow;
        }

        private sealed class CharacterVisuals
        {
            public GameObject Player;
            public GameObject MeleeEnemy;
            public GameObject RangedEnemy;
        }

        private sealed class CharacterHeadMeshes
        {
            public CharacterHeadMeshes(Mesh neck, Mesh face, Mesh eyes, Mesh brows)
            {
                Neck = neck;
                Face = face;
                Eyes = eyes;
                Brows = brows;
            }

            public Mesh Neck { get; }
            public Mesh Face { get; }
            public Mesh Eyes { get; }
            public Mesh Brows { get; }
        }

        private enum ArtKind
        {
            Nature,
            Village,
            Props
        }
    }
}
