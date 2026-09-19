using System;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Targeting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    /// <summary>
    /// Builds project-owned presentation prefabs from the selected imported art.
    /// Gameplay colliders, navigation, damage and root motion remain authoritative elsewhere.
    /// </summary>
    public static class M6ArtProjectSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/10_EmberValley.unity";
        private const string AnimationSetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        private const string DownloadRoot = "Assets/_Game/Art/DownloadResources/UnityFreeAssets";
        private const string CharacterRoot = DownloadRoot + "/03_Character_Kit";
        private const string WardenRoot = DownloadRoot + "/01_Warden_Boss/Knight Character by @Quaternius/FBX";
        private const string DungeonRoot = DownloadRoot + "/07_Environment_Ruins/Updated_Modular_Dungeon/FBX";
        private const string MaterialRoot = "Assets/_Game/Art/Materials/M6Art";
        private const string PrefabRoot = "Assets/_Game/Prefabs/Characters/M6Art";
        private const string WeaponPrefabRoot = "Assets/_Game/Prefabs/Weapons/M6Art";
        private const string VfxPrefabRoot = "Assets/_Game/Prefabs/VFX/M6Art";
        private const string CombatGymPath = "Assets/_Game/Scenes/90_CombatGym.unity";
        private const string NetworkGymPath = "Assets/_Game/Scenes/91_NetworkGym.unity";
        private const string HovlVfxRoot =
            "Assets/_Game/Art/DownloadResources/Hovl Studio/Magic effects pack/Prefabs";

        private const string SelectedPlayerPrefab = "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab";
        private const string SelectedPlayerSource = "Assets/_Game/Art/Characters/M3Art/Derived/Male_Ranger_Emberfall.fbx";
        private const string SelectedScoutPrefab = "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Player_Warrior.prefab";
        private const string KayKitMeleeSource = CharacterRoot + "/KayKit_Skeletons/addons/kaykit_character_pack_skeletons/Characters/fbx/Skeleton_Warrior.fbx";
        private const string MeleeSource = KayKitMeleeSource;
        private const string RangedSource = CharacterRoot + "/RPG Characters - Nov 2020/FBX/Wizard.fbx";
        private const string RangedTexture = CharacterRoot + "/RPG Characters - Nov 2020/Textures/Wizard_Texture.png";
        private const string WardenSource = WardenRoot + "/KnightCharacter.fbx";
        private const string SkeletonShortSwordSource = WardenRoot + "/ShortSword.fbx";
        private const string WardenRuneSwordSource = WardenRoot + "/Katana.fbx";
        private const string KayKitGearRoot = CharacterRoot +
            "/KayKit_Skeletons/addons/kaykit_character_pack_skeletons/Assets/fbx";
        private const string SkeletonShieldSource = KayKitGearRoot + "/Skeleton_Shield_Large_A.fbx";
        private const string WardenShieldSource = KayKitGearRoot + "/Skeleton_Shield_Small_A.fbx";
        private const string SkeletonGearTexture = KayKitGearRoot + "/skeleton_texture.png";
        private const string ThrowingKnifeSource = CharacterRoot +
            "/KayKit_Adventurers/addons/kaykit_character_pack_adventures/Assets/fbx/dagger.fbx";
        private const string ScorchedKnightSource = CharacterRoot +
            "/KayKit_Adventurers/addons/kaykit_character_pack_adventures/Characters/fbx/Knight.fbx";
        private const string ScorchedKnightTexture = CharacterRoot +
            "/KayKit_Adventurers/addons/kaykit_character_pack_adventures/Characters/fbx/knight_texture.png";
        private const string ScorchedPrefabRoot = "Assets/_Game/Prefabs/Characters/M5c";
        public const string ScorchedKnightPrefabPath =
            ScorchedPrefabRoot + "/P_M5c_Enemy_RuinGuardScorched_Knight.prefab";

        [MenuItem("Emberfall/Setup/Apply M6 Imported Art Upgrade")]
        public static void Apply()
        {
            ValidateLocalLicenses();
            Directory.CreateDirectory(MaterialRoot);
            Directory.CreateDirectory(PrefabRoot);
            Directory.CreateDirectory(WeaponPrefabRoot);
            Directory.CreateDirectory(VfxPrefabRoot);

            PlayerAnimationSet animationSet = M1AnimationSetup.Apply();
            Material boneMaterial = EnsureMaterial(
                "M_M6_FogwalkerBone", new Color(0.56f, 0.59f, 0.5f), 0.08f, 0f);
            Material priestMaterial = EnsureTexturedMaterial(
                "M_M6_RunePriest", RangedTexture, new Color(0.58f, 0.48f, 0.72f), 0.16f, 0.04f);
            Material wardenMetal = EnsureMaterial(
                "M_M6_WardenMetal", new Color(0.105f, 0.13f, 0.16f), 0.55f, 0.75f);
            Material wardenCloth = EnsureMaterial(
                "M_M6_WardenCloth", new Color(0.12f, 0.055f, 0.06f), 0.14f, 0f);
            Material wardenRune = EnsureMaterial(
                "M_M6_WardenRune", new Color(0.12f, 0.015f, 0.02f), 0.35f, 0.3f,
                new Color(0.35f, 0.015f, 0.008f));
            Material weaponSteel = EnsureMaterial(
                "M_M6_WeaponSteel", new Color(0.43f, 0.49f, 0.52f), 0.62f, 0.82f);
            Material weaponLeather = EnsureMaterial(
                "M_M6_WeaponLeather", new Color(0.12f, 0.052f, 0.025f), 0.16f, 0f);
            Material weaponBrass = EnsureMaterial(
                "M_M6_WeaponBrass", new Color(0.48f, 0.27f, 0.065f), 0.48f, 0.72f);
            Material wardenBlade = EnsureMaterial(
                "M_M6_WardenBlade", new Color(0.23f, 0.26f, 0.28f), 0.7f, 0.9f);
            Material skeletonGear = EnsureTexturedMaterial(
                "M_M6_SkeletonGear", SkeletonGearTexture, Color.white, 0.18f, 0.08f);
            Material wardenShieldMaterial = EnsureTexturedMaterial(
                "M_M6_WardenShield", SkeletonGearTexture, new Color(0.48f, 0.42f, 0.42f), 0.34f, 0.42f);
            Material swordTrailMaterial = EnsureTransparentUnlitMaterial(
                "M_M6_SwordTrail", new Color(0.56f, 0.86f, 1f, 0.52f));
            Material throwingKnifeTrailMaterial = EnsureProjectileTrailMaterial(
                "M_M6_ThrowingKnifeTrail", new Color(0.24f, 0.72f, 1f, 0.52f));
            Material wardenChargeTrailMaterial = EnsureProjectileTrailMaterial(
                "M_M6_WardenChargeTrail", new Color(1f, 0.12f, 0.025f, 0.62f));

            GameObject player = LoadRequired<GameObject>(SelectedPlayerPrefab);
            GameObject scout = LoadRequired<GameObject>(SelectedScoutPrefab);
            GameObject melee = CreateCharacterPrefab(
                "P_M6_Enemy_FogwalkerSkeleton", MeleeSource, animationSet, boneMaterial, null, false);
            GameObject ranged = CreateCharacterPrefab(
                "P_M6_Enemy_RunePriest", RangedSource, animationSet, priestMaterial, null, false);
            GameObject warden = CreateCharacterPrefab(
                "P_M6_Boss_EmberWarden", WardenSource, animationSet, wardenCloth,
                new[] { wardenMetal, wardenCloth, wardenRune }, true);

            GameObject skeletonSword = CreateCanonicalSwordPrefab(
                "P_M6_Skeleton_ShortSword", SkeletonShortSwordSource, 0.98f, 0.11f,
                weaponSteel, weaponLeather, weaponBrass, weaponSteel);
            GameObject wardenSword = CreateCanonicalSwordPrefab(
                "P_M6_Warden_RuneSword", WardenRuneSwordSource, 1.22f, 0.14f,
                wardenBlade, weaponLeather, weaponBrass);
            GameObject skeletonShield = CreateCanonicalShieldPrefab(
                "P_M6_RuinGuard_SkullShield", SkeletonShieldSource, 0.96f, skeletonGear);
            GameObject wardenShield = CreateCanonicalShieldPrefab(
                "P_M6_Warden_RoundShield", WardenShieldSource, 1.04f, wardenShieldMaterial);
            GameObject throwingKnife = CreateThrowingKnifePrefab(
                "P_M6_Player_ThrowingKnife", ThrowingKnifeSource, 0.42f,
                weaponSteel, weaponLeather, throwingKnifeTrailMaterial);
            GameObject steelImpact = CreateCombatVfxVariant(
                "P_M6_Impact_Steel",
                HovlVfxRoot + "/Sparks/Sparks explode white.prefab",
                0.5f);
            GameObject guardImpact = CreateCombatVfxVariant(
                "P_M6_Impact_Guard",
                HovlVfxRoot + "/Sparks/Sparks explode yellow.prefab",
                0.55f);
            GameObject emberImpact = CreateCombatVfxVariant(
                "P_M6_Impact_Ember",
                HovlVfxRoot + "/Sparks/Sparks explode red.prefab",
                0.62f);
            GameObject wardenPhaseTransition = CreateWardenVfxVariant(
                "P_M6_Warden_PhaseTransition",
                HovlVfxRoot + "/Magic circles/Magic circle 2.prefab",
                0.85f,
                true,
                false);
            GameObject wardenRuneCleave = CreateWardenVfxVariant(
                "P_M6_Warden_RuneCleave",
                HovlVfxRoot + "/AoE effects/AoE slash orange.prefab",
                1.6f,
                false,
                false);
            GameObject wardenDelayedBlast = CreateWardenVfxVariant(
                "P_M6_Warden_DelayedBlast",
                HovlVfxRoot + "/AoE effects/Ground AOE explosion.prefab",
                0.78f,
                false,
                true);

            M3ArtProjectSetup.ApplyWithCharacterVisuals(player, melee, ranged, warden, scout);
            PreserveOptionalSceneInBuildSettings(NetworkGymPath);
            AddDungeonPresentation(wardenMetal, wardenCloth, wardenRune);
            ReplaceCombatEquipment(ScenePath, skeletonSword, wardenSword, skeletonShield, wardenShield);
            ReplaceCombatEquipment(CombatGymPath, skeletonSword, wardenSword, skeletonShield, wardenShield);
            BindCombatVfx(ScenePath, steelImpact, guardImpact, emberImpact);
            BindCombatVfx(CombatGymPath, steelImpact, guardImpact, emberImpact);
            BindSwordTrail(ScenePath, swordTrailMaterial);
            BindSwordTrail(CombatGymPath, swordTrailMaterial);
            BindThrowingKnife(ScenePath, throwingKnife);
            BindThrowingKnife(CombatGymPath, throwingKnife);
            BindWardenChargeTrail(ScenePath, wardenChargeTrailMaterial);
            BindWardenCombatVfx(
                ScenePath,
                wardenPhaseTransition,
                wardenRuneCleave,
                wardenDelayedBlast);

            // Reapplying presentation assets must not downgrade the current integrated build version.
            PlayerSettings.bundleVersion = M5NetworkingProjectSetup.Version;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("EMBERFALL_M6_ART_SETUP_COMPLETE version=0.7.8 player=standard-proportion-ranger");
        }

        private static void PreserveOptionalSceneInBuildSettings(string scenePath)
        {
            if (!File.Exists(scenePath)) return;
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Any(scene => scene.path == scenePath && scene.enabled)) return;
            var updated = scenes.ToList();
            int index = updated.FindIndex(scene => scene.path == scenePath);
            if (index >= 0) updated[index] = new EditorBuildSettingsScene(scenePath, true);
            else updated.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = updated.ToArray();
        }

        public static void ReportCharacterRigs()
        {
            string[] paths = { SelectedPlayerSource, MeleeSource, RangedSource, WardenSource };
            foreach (string path in paths)
            {
                GameObject asset = LoadRequired<GameObject>(path);
                string hierarchy = string.Join(", ", asset.GetComponentsInChildren<Transform>(true)
                    .Select(item => item.name));
                string avatars = string.Join(", ", AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<Avatar>()
                    .Select(item => $"{item.name}:valid={item.isValid}:human={item.isHuman}"));
                Debug.Log($"EMBERFALL_M6_RIG path={path}\navatars=[{avatars}]\nhierarchy=[{hierarchy}]");
            }
        }

        public static void ValidateRpgCandidateRigs()
        {
            string basePath = CharacterRoot + "/RPG Characters - Nov 2020/FBX/";
            foreach (string name in new[] { "Warrior", "Ranger", "Rogue", "Monk", "Wizard" })
            {
                string path = basePath + name + ".fbx";
                try
                {
                    EnsureHumanoidImporter(path);
                    Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                    Debug.Log($"EMBERFALL_M6_RPG_RIG name={name} valid={avatar != null && avatar.isValid} human={avatar != null && avatar.isHuman}");
                }
                catch (Exception exception)
                {
                    Debug.LogError($"EMBERFALL_M6_RPG_RIG name={name} exception={exception.Message}");
                }
            }
        }

        public static GameObject CreateM5cScorchedEliteVisual()
        {
            string licensePath = CharacterRoot + "/KayKit_Adventurers/LICENSE.txt";
            if (!File.Exists(licensePath))
                throw new FileNotFoundException("KayKit Adventurers CC0 license is missing.", licensePath);

            Directory.CreateDirectory(MaterialRoot);
            Directory.CreateDirectory(ScorchedPrefabRoot);
            PlayerAnimationSet animationSet = M1AnimationSetup.Apply();
            Material knightMaterial = EnsureTexturedMaterial(
                "M_M5c_ScorchedKnight",
                ScorchedKnightTexture,
                new Color(1f, 0.82f, 0.72f),
                0.28f,
                0.18f);
            Material emberMaterial = EnsureMaterial(
                "M_M5c_ScorchedEmber",
                new Color(0.5f, 0.055f, 0.012f),
                0.34f,
                0.12f,
                new Color(2.2f, 0.16f, 0.02f));

            CreateCharacterPrefab(
                "P_M5c_Enemy_RuinGuardScorched_Knight",
                ScorchedKnightSource,
                animationSet,
                knightMaterial,
                null,
                false,
                ScorchedPrefabRoot);

            GameObject root = PrefabUtility.LoadPrefabContents(ScorchedKnightPrefabPath);
            try
            {
                Animator animator = root.GetComponentInChildren<Animator>(true);
                if (animator == null || !animator.isHuman)
                    throw new InvalidDataException("Scorched Knight has no valid Humanoid Animator.");

                Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (rightHand == null || leftHand == null)
                    throw new InvalidDataException("Scorched Knight hand bones are unavailable.");

                Transform swordSocket = M1ProjectSetup.CreateWorldScaleSocket(
                    rightHand,
                    "WeaponSocket_RightHand",
                    new Vector3(0f, 0.015f, 0f),
                    Quaternion.identity);
                GameObject sword = InstantiateGear(
                    LoadRequired<GameObject>(WeaponPrefabRoot + "/P_M6_Skeleton_ShortSword.prefab"),
                    swordSocket,
                    "Sword_M5c_Scorched_Equipped");

                Transform shieldSocket = M1ProjectSetup.CreateWorldScaleSocket(
                    leftHand,
                    "ShieldSocket_LeftHand",
                    new Vector3(0.02f, 0.05f, 0.03f),
                    Quaternion.Euler(5f, 92f, 88f));
                GameObject shield = InstantiateGear(
                    LoadRequired<GameObject>(WeaponPrefabRoot + "/P_M6_RuinGuard_SkullShield.prefab"),
                    shieldSocket,
                    "Shield_Wooden_Equipped");

                Transform chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ??
                                  animator.GetBoneTransform(HumanBodyBones.Chest);
                if (chest != null)
                {
                    Transform emberSocket = M1ProjectSetup.CreateWorldScaleSocket(
                        chest,
                        "ScorchedCoreSocket",
                        new Vector3(0f, 0.05f, 0.16f),
                        Quaternion.identity);
                    GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    core.name = "ScorchedCore";
                    core.transform.SetParent(emberSocket, false);
                    core.transform.localScale = Vector3.one * 0.13f;
                    core.GetComponent<Renderer>().sharedMaterial = emberMaterial;
                    UnityEngine.Object.DestroyImmediate(core.GetComponent<Collider>());
                }

                M1ProjectSetup.SetLayerRecursively(root, 0);
                PrefabUtility.SaveAsPrefabAsset(root, ScorchedKnightPrefabPath);
                EditorUtility.SetDirty(sword);
                EditorUtility.SetDirty(shield);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            return LoadRequired<GameObject>(ScorchedKnightPrefabPath);
        }

        private static GameObject CreateCharacterPrefab(
            string prefabName,
            string sourcePath,
            PlayerAnimationSet animationSet,
            Material primaryMaterial,
            Material[] slotMaterials,
            bool addWardenArmor,
            string outputRoot = PrefabRoot)
        {
            EnsureHumanoidImporter(sourcePath);
            GameObject source = LoadRequired<GameObject>(sourcePath);
            var root = new GameObject(prefabName);
            GameObject model = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject;
            if (model == null) throw new InvalidOperationException($"Could not instantiate character source: {sourcePath}");
            model.name = "Model";

            Animator animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null) throw new InvalidDataException($"Character has no Animator: {sourcePath}");
            Avatar sourceAvatar = AssetDatabase.LoadAllAssetsAtPath(sourcePath)
                .OfType<Avatar>()
                .FirstOrDefault(candidate => candidate.isValid && candidate.isHuman);
            if (sourceAvatar != null) animator.avatar = sourceAvatar;
            animator.runtimeAnimatorController = animationSet.Controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            if (!animator.isHuman)
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw new InvalidDataException($"Character does not expose a valid Humanoid avatar: {sourcePath}");
            }

            ApplyCharacterMaterials(model, primaryMaterial, slotMaterials);
            NormalizeCharacter(model, addWardenArmor ? 2.15f : 1.9f);
            if (addWardenArmor)
            {
                AddAlignedArmorPart(root.transform, animator, WardenRoot + "/Helmet2.fbx",
                    HumanBodyBones.Head, "Helmet_Closed", 0.76f, new Vector3(0f, 0.27f, 0f), slotMaterials[0]);
                AddAlignedArmorPart(root.transform, animator, WardenRoot + "/ShoulderPads.fbx",
                    HumanBodyBones.UpperChest, "ShoulderPads", 0.95f, new Vector3(0f, 0.06f, 0f), slotMaterials[0]);
            }

            M1ProjectSetup.SetLayerRecursively(root, 0);
            string prefabPath = $"{outputRoot}/{prefabName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) throw new IOException($"Could not save character prefab: {prefabPath}");
            return prefab;
        }

        private static void AddAlignedArmorPart(
            Transform visualRoot,
            Animator animator,
            string sourcePath,
            HumanBodyBones followBone,
            string name,
            float targetMaxSize,
            Vector3 worldOffset,
            Material wardenMaterial)
        {
            GameObject source = LoadRequired<GameObject>(sourcePath);
            GameObject part = PrefabUtility.InstantiatePrefab(source, visualRoot) as GameObject;
            if (part == null) throw new InvalidOperationException($"Could not instantiate armor: {sourcePath}");
            part.name = name;
            part.transform.localPosition = Vector3.zero;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = Vector3.one;
            foreach (Animator nested in part.GetComponentsInChildren<Animator>(true))
                UnityEngine.Object.DestroyImmediate(nested);
            foreach (Collider collider in part.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = Enumerable.Repeat(
                    wardenMaterial, renderer.sharedMaterials.Length).ToArray();
            }

            Transform bone = animator.GetBoneTransform(followBone);
            if (bone == null) throw new InvalidDataException($"Warden is missing bone {followBone}.");
            Bounds bounds = CalculateBounds(part);
            float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (maxSize <= 0.001f) throw new InvalidDataException($"Armor has no usable renderer bounds: {sourcePath}");
            part.transform.localScale = Vector3.one * (targetMaxSize / maxSize);
            bounds = CalculateBounds(part);
            part.transform.position += bone.position + worldOffset - bounds.center;
            part.transform.SetParent(bone, true);
        }

        private static void NormalizeCharacter(GameObject model, float targetHeight)
        {
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            Bounds bounds = CalculateBounds(model);
            if (bounds.size.y <= 0.001f) throw new InvalidDataException("Character has no usable renderer bounds.");
            float scale = targetHeight / bounds.size.y;
            model.transform.localScale = Vector3.one * scale;
            bounds = CalculateBounds(model);
            model.transform.localPosition = new Vector3(-bounds.center.x, -1f - bounds.min.y, -bounds.center.z);
        }

        private static void ApplyCharacterMaterials(GameObject model, Material primary, Material[] slots)
        {
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] source = renderer.sharedMaterials;
                Material[] replacements = new Material[source.Length];
                for (int i = 0; i < replacements.Length; i++)
                {
                    replacements[i] = slots != null && slots.Length > 0
                        ? slots[Math.Min(i, slots.Length - 1)]
                        : primary;
                }
                renderer.sharedMaterials = replacements;
            }
        }

        private static void AddDungeonPresentation(Material metal, Material cloth, Material rune)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject previous = GameObject.Find("[Art] M6 Imported Upgrade");
            if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
            Transform root = new GameObject("[Art] M6 Imported Upgrade").transform;

            Material coolStone = EnsureMaterial(
                "M_M6_DungeonStone", new Color(0.18f, 0.22f, 0.23f), 0.14f, 0f);
            Material deepStone = EnsureMaterial(
                "M_M6_DungeonStoneDeep", new Color(0.075f, 0.095f, 0.105f), 0.1f, 0f);

            Vector3[] wardenColumns =
            {
                new Vector3(37.4f, 0f, 11.2f), new Vector3(54.6f, 0f, 11.2f),
                new Vector3(37.4f, 0f, 28.8f), new Vector3(54.6f, 0f, 28.8f)
            };
            for (int i = 0; i < wardenColumns.Length; i++)
            {
                CreateFitted(root, $"WardenColumn_{i:00}", DungeonRoot + "/Column.fbx",
                    wardenColumns[i], new Vector3(-90f, i * 90f, 0f), 4.6f, coolStone, deepStone);
            }

            GameObject sanctumArch = CreateFitted(root, "SanctumDungeonArch", DungeonRoot + "/Arch.fbx",
                new Vector3(40f, 0f, 32.15f), new Vector3(-90f, 0f, 0f), 4.8f, coolStone, deepStone);
            BindPresentationToBarrier(sanctumArch, "GateBlocker_Sanctum");
            CreateFitted(root, "WardenGateArch", DungeonRoot + "/Arch.fbx",
                new Vector3(46f, 0f, 9.65f), new Vector3(-90f, 180f, 0f), 4.8f, coolStone, deepStone);
            CreateFitted(root, "WardenHorseStatue_West", DungeonRoot + "/Statue_Horse.fbx",
                new Vector3(39.2f, 0f, 27.8f), new Vector3(0f, 150f, 0f), 3.5f, coolStone, deepStone);
            CreateFitted(root, "WardenHorseStatue_East", DungeonRoot + "/Statue_Horse.fbx",
                new Vector3(52.8f, 0f, 27.8f), new Vector3(0f, 210f, 0f), 3.5f, coolStone, deepStone);

            CreateRuneBrazier(root, "WardenBrazier_West", new Vector3(38.4f, 0f, 14f), rune, metal);
            CreateRuneBrazier(root, "WardenBrazier_East", new Vector3(53.6f, 0f, 14f), rune, metal);
            CreateRuneBrazier(root, "CourtyardBrazier", new Vector3(30f, 0f, 52.2f), rune, metal);

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void BindPresentationToBarrier(GameObject presentation, string blockerName)
        {
            GameObject blocker = UnityEngine.Object.FindObjectsOfType<Transform>(true)
                .FirstOrDefault(item => item.gameObject.scene.IsValid() && item.name == blockerName)
                ?.gameObject;
            if (blocker == null)
                throw new InvalidOperationException($"Required barrier '{blockerName}' was not found.");

            presentation.transform.SetParent(blocker.transform, true);
        }

        private static GameObject CreateFitted(
            Transform parent,
            string name,
            string sourcePath,
            Vector3 position,
            Vector3 euler,
            float targetHeight,
            params Material[] materials)
        {
            GameObject source = LoadRequired<GameObject>(sourcePath);
            GameObject instance = PrefabUtility.InstantiatePrefab(source, parent) as GameObject;
            if (instance == null) throw new InvalidOperationException($"Could not instantiate environment source: {sourcePath}");
            instance.name = name;
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(euler));
            instance.transform.localScale = Vector3.one;
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            ApplyEnvironmentMaterials(instance, materials);
            Bounds bounds = CalculateBounds(instance);
            if (bounds.size.y <= 0.001f) throw new InvalidDataException($"Environment source has no height: {sourcePath}");
            instance.transform.localScale *= targetHeight / bounds.size.y;
            bounds = CalculateBounds(instance);
            instance.transform.position += Vector3.up * (position.y - bounds.min.y);
            return instance;
        }

        private static void ApplyEnvironmentMaterials(GameObject instance, Material[] materials)
        {
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++) slots[i] = materials[i % materials.Length];
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static GameObject CreateCanonicalSwordPrefab(
            string prefabName,
            string sourcePath,
            float targetLength,
            float gripInset,
            params Material[] materials)
        {
            GameObject root = new GameObject(prefabName);
            GameObject model = PrefabUtility.InstantiatePrefab(LoadRequired<GameObject>(sourcePath), root.transform)
                as GameObject;
            if (model == null) throw new InvalidOperationException($"Could not instantiate weapon: {sourcePath}");
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            model.transform.localScale = Vector3.one;
            StripGearComponents(model);
            ApplySlotMaterials(model, materials);

            Bounds bounds = CalculateBounds(model);
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (longest <= 0.001f) throw new InvalidDataException($"Weapon has no usable bounds: {sourcePath}");
            model.transform.localScale *= targetLength / longest;
            bounds = CalculateBounds(root);
            if (bounds.size.y < targetLength * 0.8f)
                throw new InvalidDataException($"Weapon did not align to local Y: {sourcePath}, size={bounds.size}");
            model.transform.position += new Vector3(
                -bounds.center.x,
                -gripInset - bounds.min.y,
                -bounds.center.z);

            M1ProjectSetup.SetLayerRecursively(root, 0);
            string path = $"{WeaponPrefabRoot}/{prefabName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) throw new IOException($"Could not save weapon prefab: {path}");
            return prefab;
        }

        private static GameObject CreateCanonicalShieldPrefab(
            string prefabName,
            string sourcePath,
            float targetDiameter,
            Material material)
        {
            GameObject root = new GameObject(prefabName);
            GameObject model = PrefabUtility.InstantiatePrefab(LoadRequired<GameObject>(sourcePath), root.transform)
                as GameObject;
            if (model == null) throw new InvalidOperationException($"Could not instantiate shield: {sourcePath}");
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            model.transform.localScale = Vector3.one;
            StripGearComponents(model);
            ApplySlotMaterials(model, material);

            Bounds bounds = CalculateBounds(root);
            float faceSize = Mathf.Max(bounds.size.x, bounds.size.y);
            if (faceSize <= 0.001f) throw new InvalidDataException($"Shield has no usable bounds: {sourcePath}");
            model.transform.localScale *= targetDiameter / faceSize;
            bounds = CalculateBounds(root);
            model.transform.position -= bounds.center;

            M1ProjectSetup.SetLayerRecursively(root, 0);
            string path = $"{WeaponPrefabRoot}/{prefabName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) throw new IOException($"Could not save shield prefab: {path}");
            return prefab;
        }

        private static GameObject CreateThrowingKnifePrefab(
            string prefabName,
            string sourcePath,
            float targetLength,
            Material steel,
            Material grip,
            Material trailMaterial)
        {
            GameObject root = new GameObject(
                prefabName,
                typeof(TrailRenderer),
                typeof(BoxCollider),
                typeof(PlayerThrowingKnifeProjectile));
            GameObject model = PrefabUtility.InstantiatePrefab(
                LoadRequired<GameObject>(sourcePath), root.transform) as GameObject;
            if (model == null) throw new InvalidOperationException($"Could not instantiate throwing knife: {sourcePath}");
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            StripGearComponents(model);
            ApplySlotMaterials(model, steel, grip);

            Bounds bounds = CalculateBounds(model);
            Vector3 size = bounds.size;
            if (Mathf.Max(size.x, size.y, size.z) <= 0.001f)
                throw new InvalidDataException($"Throwing knife has no usable bounds: {sourcePath}");
            if (size.x >= size.y && size.x >= size.z)
                model.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            else if (size.y >= size.z)
                model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            bounds = CalculateBounds(model);
            float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            model.transform.localScale *= targetLength / longest;
            bounds = CalculateBounds(model);
            model.transform.position -= bounds.center;
            bounds = CalculateBounds(model);

            BoxCollider hitbox = root.GetComponent<BoxCollider>();
            hitbox.isTrigger = true;
            hitbox.center = root.transform.InverseTransformPoint(bounds.center);
            hitbox.size = new Vector3(
                Mathf.Max(0.045f, bounds.size.x * 0.88f),
                Mathf.Max(0.035f, bounds.size.y * 0.82f),
                Mathf.Max(0.12f, bounds.size.z * 0.92f));
            hitbox.enabled = false;

            TrailRenderer trail = root.GetComponent<TrailRenderer>();
            trail.time = 0.18f;
            trail.minVertexDistance = 0.045f;
            trail.widthMultiplier = 1f;
            trail.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0.055f),
                new Keyframe(0.55f, 0.032f),
                new Keyframe(1f, 0f));
            trail.colorGradient = new Gradient
            {
                colorKeys = new[]
                {
                    new GradientColorKey(new Color(0.66f, 0.9f, 1f), 0f),
                    new GradientColorKey(new Color(0.18f, 0.55f, 1f), 1f)
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0.8f, 0f),
                    new GradientAlphaKey(0f, 1f)
                }
            };
            trail.sharedMaterial = trailMaterial;
            trail.alignment = LineAlignment.View;
            trail.textureMode = LineTextureMode.Stretch;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            root.GetComponent<PlayerThrowingKnifeProjectile>().Configure(trail, hitbox);

            M1ProjectSetup.SetLayerRecursively(root, 0);
            string path = $"{WeaponPrefabRoot}/{prefabName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) throw new IOException($"Could not save throwing-knife prefab: {path}");
            return prefab;
        }

        private static void BindThrowingKnife(string scenePath, GameObject projectilePrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            PlayerCombatActor actor = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<PlayerCombatActor>(true))
                .SingleOrDefault();
            if (actor == null)
                throw new InvalidDataException($"Scene has no unique player combat actor: {scenePath}");

            LockOnTargeting targeting = actor.GetComponent<LockOnTargeting>();
            if (targeting == null) throw new InvalidDataException($"Player has no lock-on adapter: {scenePath}");
            Transform launchOrigin = actor.transform.Find("ThrowingKnifeOrigin_M6");
            if (launchOrigin == null)
            {
                launchOrigin = new GameObject("ThrowingKnifeOrigin_M6").transform;
                launchOrigin.SetParent(actor.transform, false);
            }
            launchOrigin.localPosition = new Vector3(0.28f, 0.62f, 0.52f);
            launchOrigin.localRotation = Quaternion.identity;

            PlayerThrowingKnifeLauncher launcher = actor.GetComponent<PlayerThrowingKnifeLauncher>();
            if (launcher == null) launcher = actor.gameObject.AddComponent<PlayerThrowingKnifeLauncher>();
            launcher.Configure(actor, targeting, launchOrigin, projectilePrefab);
            EditorUtility.SetDirty(launcher);
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void BindWardenChargeTrail(string scenePath, Material trailMaterial)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            WardenActor warden = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<WardenActor>(true))
                .SingleOrDefault();
            if (warden == null)
                throw new InvalidDataException($"Scene has no unique Warden for charge trail: {scenePath}");

            WardenChargeTrailPresenter presenter = warden.GetComponent<WardenChargeTrailPresenter>();
            if (presenter == null) presenter = warden.gameObject.AddComponent<WardenChargeTrailPresenter>();
            presenter.Configure(warden, trailMaterial);
            EditorUtility.SetDirty(presenter);
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static GameObject CreateCombatVfxVariant(
            string prefabName,
            string sourcePath,
            float scale)
        {
            GameObject root = new GameObject(prefabName);
            GameObject source = PrefabUtility.InstantiatePrefab(
                LoadRequired<GameObject>(sourcePath), root.transform) as GameObject;
            if (source == null) throw new InvalidOperationException($"Could not instantiate VFX: {sourcePath}");
            source.name = "Effect";
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
            source.transform.localScale = Vector3.one * scale;
            foreach (Collider collider in source.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (MonoBehaviour behaviour in source.GetComponentsInChildren<MonoBehaviour>(true))
                UnityEngine.Object.DestroyImmediate(behaviour);

            M1ProjectSetup.SetLayerRecursively(root, 0);
            string path = $"{VfxPrefabRoot}/{prefabName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) throw new IOException($"Could not save VFX prefab: {path}");
            return prefab;
        }

        private static GameObject CreateWardenVfxVariant(
            string prefabName,
            string sourcePath,
            float scale,
            bool loop,
            bool playOnAwake)
        {
            GameObject root = new GameObject(prefabName);
            GameObject source = PrefabUtility.InstantiatePrefab(
                LoadRequired<GameObject>(sourcePath), root.transform) as GameObject;
            if (source == null) throw new InvalidOperationException($"Could not instantiate Warden VFX: {sourcePath}");
            source.name = "Effect";
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
            source.transform.localScale = Vector3.one * scale;
            foreach (Collider collider in source.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (MonoBehaviour behaviour in source.GetComponentsInChildren<MonoBehaviour>(true))
                UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (ParticleSystem particle in source.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particle.main;
                main.loop = loop;
                main.playOnAwake = playOnAwake;
            }

            M1ProjectSetup.SetLayerRecursively(root, 0);
            string path = $"{VfxPrefabRoot}/{prefabName}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) throw new IOException($"Could not save Warden VFX prefab: {path}");
            return prefab;
        }

        private static void BindWardenCombatVfx(
            string scenePath,
            GameObject phaseTransitionPrefab,
            GameObject runeCleavePrefab,
            GameObject delayedBlastPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            WardenActor warden = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<WardenActor>(true))
                .SingleOrDefault();
            if (warden == null)
                throw new InvalidDataException($"Scene has no unique Warden for combat VFX: {scenePath}");

            WardenCombatVfxPresenter presenter = warden.GetComponent<WardenCombatVfxPresenter>();
            if (presenter == null) presenter = warden.gameObject.AddComponent<WardenCombatVfxPresenter>();
            presenter.Configure(warden, phaseTransitionPrefab, runeCleavePrefab);
            warden.ConfigureDelayedBlastVfx(delayedBlastPrefab);
            EditorUtility.SetDirty(presenter);
            EditorUtility.SetDirty(warden);
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void BindCombatVfx(
            string scenePath,
            GameObject steelImpact,
            GameObject guardImpact,
            GameObject emberImpact)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            PlayerCombatActor[] actors = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<PlayerCombatActor>(true))
                .ToArray();
            if (actors.Length == 0)
                throw new InvalidDataException($"Scene has no player combat actor for VFX binding: {scenePath}");

            foreach (PlayerCombatActor actor in actors)
            {
                CombatImpactVfxPresenter presenter = actor.GetComponent<CombatImpactVfxPresenter>();
                if (presenter == null) presenter = actor.gameObject.AddComponent<CombatImpactVfxPresenter>();
                presenter.Configure(actor, steelImpact, guardImpact, emberImpact);
                EditorUtility.SetDirty(presenter);
            }
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void BindSwordTrail(string scenePath, Material trailMaterial)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            PlayerCombatActor[] actors = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<PlayerCombatActor>(true))
                .ToArray();
            if (actors.Length == 0)
                throw new InvalidDataException($"Scene has no player combat actor for sword trail: {scenePath}");

            foreach (PlayerCombatActor actor in actors)
            {
                Transform[] hierarchy = actor.GetComponentsInChildren<Transform>(true);
                Animator animator = actor.GetComponentInChildren<Animator>(true);
                Transform rightHand = animator != null && animator.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.RightHand)
                    : null;
                Transform swordVisual = hierarchy.FirstOrDefault(item =>
                    item.name == "Sword_M6_Player_Equipped" || item.name == "Warrior_Sword");
                if (rightHand == null || swordVisual == null ||
                    swordVisual.GetComponentsInChildren<Renderer>(true).Length == 0)
                    throw new InvalidDataException($"Player sword trace anchors are invalid in {scenePath}.");

                Bounds swordBounds = CalculateBounds(swordVisual.gameObject);
                Vector3 bladeRootPosition = swordVisual.position;
                Vector3 tipPosition = FindFarthestBoundsCorner(swordBounds, bladeRootPosition);
                if (Vector3.Distance(bladeRootPosition, tipPosition) < 0.35f ||
                    Vector3.Distance(bladeRootPosition, tipPosition) > 2f)
                    throw new InvalidDataException(
                        $"Player sword render bounds are outside the supported scale in {scenePath}: {swordBounds.size}.");

                Transform bladeRoot = EnsureWorldAnchor(rightHand, "SwordTrailRoot_M6", bladeRootPosition);
                Transform bladeTip = EnsureWorldAnchor(rightHand, "SwordTrailTip_M6", tipPosition);

                SwordTrailPresenter presenter = actor.GetComponent<SwordTrailPresenter>();
                if (presenter == null) presenter = actor.gameObject.AddComponent<SwordTrailPresenter>();
                presenter.Configure(actor, bladeRoot, bladeTip, trailMaterial);
                EditorUtility.SetDirty(presenter);
            }
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static Transform EnsureWorldAnchor(Transform parent, string name, Vector3 worldPosition)
        {
            Transform anchor = parent.Find(name);
            if (anchor == null)
            {
                anchor = new GameObject(name).transform;
                anchor.SetParent(parent, false);
            }
            anchor.position = worldPosition;
            anchor.localRotation = Quaternion.identity;
            anchor.localScale = Vector3.one;
            return anchor;
        }

        private static Vector3 FindFarthestBoundsCorner(Bounds bounds, Vector3 origin)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            Vector3 farthest = min;
            float farthestDistance = -1f;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 corner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y,
                    z == 0 ? min.z : max.z);
                float distance = (corner - origin).sqrMagnitude;
                if (distance <= farthestDistance) continue;
                farthestDistance = distance;
                farthest = corner;
            }
            return farthest;
        }

        private static void StripGearComponents(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
                UnityEngine.Object.DestroyImmediate(animator);
        }

        private static void ApplySlotMaterials(GameObject root, params Material[] materials)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++) slots[i] = materials[i % materials.Length];
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        internal static void ReplaceCombatEquipment(
            string scenePath,
            GameObject skeletonSword,
            GameObject wardenSword,
            GameObject skeletonShield,
            GameObject wardenShield)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Transform[] transforms = UnityEngine.Object.FindObjectsOfType<Transform>(true)
                .Where(item => item.gameObject.scene == scene)
                .ToArray();

            Transform[] practiceSwords = transforms
                .Where(item => item.name == "Sword_Practice")
                .ToArray();
            foreach (Transform practiceSword in practiceSwords)
            {
                Transform socket = practiceSword.parent;
                PlayerCombatActor player = practiceSword.GetComponentInParent<PlayerCombatActor>();
                WardenActor warden = practiceSword.GetComponentInParent<WardenActor>();
                GameObject prefab = warden != null ? wardenSword : skeletonSword;
                UnityEngine.Object.DestroyImmediate(practiceSword.gameObject);
                GameObject instance = InstantiateGear(prefab, socket,
                    warden != null
                        ? "Sword_M6_Warden_Equipped"
                        : player != null
                            ? "Sword_M6_Player_Equipped"
                            : "Sword_M6_Skeleton_Equipped");
                if (player != null) M1ProjectSetup.SetLayerRecursively(instance, player.gameObject.layer);
                if (warden != null) SetRendererReference(warden, "_weaponRenderer", instance);
            }

            foreach (ShieldEnemyActor guard in UnityEngine.Object.FindObjectsOfType<ShieldEnemyActor>(true)
                         .Where(item => item.gameObject.scene == scene))
            {
                Transform oldShield = guard.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item => item.name == "Shield_Wooden_Equipped");
                if (oldShield == null) continue;
                Transform socket = oldShield.parent;
                UnityEngine.Object.DestroyImmediate(oldShield.gameObject);
                GameObject instance = InstantiateGear(
                    skeletonShield, socket, "Shield_Wooden_Equipped");
                SetRendererReference(guard, "_shieldRenderer", instance);
            }

            foreach (WardenActor warden in UnityEngine.Object.FindObjectsOfType<WardenActor>(true)
                         .Where(item => item.gameObject.scene == scene))
            {
                Transform oldShield = warden.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item => item.name == "Shield_Warden_Equipped");
                if (oldShield == null) continue;
                Transform socket = oldShield.parent;
                UnityEngine.Object.DestroyImmediate(oldShield.gameObject);
                GameObject instance = InstantiateGear(
                    wardenShield, socket, "Shield_Warden_Equipped");
                SetRendererReference(warden, "_shieldRenderer", instance);
            }

            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static GameObject InstantiateGear(GameObject prefab, Transform socket, string name)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, socket) as GameObject;
            if (instance == null) throw new InvalidOperationException($"Could not instantiate gear prefab: {prefab.name}");
            instance.name = name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        private static void SetRendererReference(UnityEngine.Object actor, string fieldName, GameObject gear)
        {
            Renderer renderer = gear.GetComponentInChildren<Renderer>(true);
            if (renderer == null) throw new InvalidDataException($"Gear has no renderer: {gear.name}");
            var serialized = new SerializedObject(actor);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null) throw new MissingFieldException(actor.GetType().Name, fieldName);
            property.objectReferenceValue = renderer;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(actor);
        }

        private static void CreateRuneBrazier(
            Transform parent,
            string name,
            Vector3 position,
            Material rune,
            Material metal)
        {
            GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = name;
            pedestal.transform.SetParent(parent, false);
            pedestal.transform.position = position + new Vector3(0f, 0.45f, 0f);
            pedestal.transform.localScale = new Vector3(0.34f, 0.45f, 0.34f);
            pedestal.GetComponent<Renderer>().sharedMaterial = metal;
            UnityEngine.Object.DestroyImmediate(pedestal.GetComponent<Collider>());

            GameObject flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flame.name = "RuneFlame";
            flame.transform.SetParent(pedestal.transform, false);
            flame.transform.localPosition = new Vector3(0f, 0.82f, 0f);
            flame.transform.localScale = new Vector3(0.45f, 0.7f, 0.45f);
            flame.GetComponent<Renderer>().sharedMaterial = rune;
            UnityEngine.Object.DestroyImmediate(flame.GetComponent<Collider>());

            Light light = flame.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.12f, 0.055f);
            light.intensity = 2.2f;
            light.range = 6f;
            light.shadows = LightShadows.None;
        }

        private static Material EnsureTexturedMaterial(
            string name,
            string texturePath,
            Color tint,
            float smoothness,
            float metallic)
        {
            Texture2D texture = LoadRequired<Texture2D>(texturePath);
            Material material = EnsureMaterial(name, tint, smoothness, metallic);
            material.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureMaterial(
            string name,
            Color color,
            float smoothness,
            float metallic,
            Color? emission = null)
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
            material.color = color;
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureTransparentUnlitMaterial(string name, Color color)
        {
            string path = $"{MaterialRoot}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Emberfall/VFX/SwordTrail");
            if (shader == null) throw new InvalidOperationException("Project sword trail shader is unavailable.");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", color);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureProjectileTrailMaterial(string name, Color color)
        {
            string path = $"{MaterialRoot}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Emberfall/VFX/ProjectileTrail");
            if (shader == null) throw new InvalidOperationException("Project projectile trail shader is unavailable.");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureHumanoidImporter(string sourcePath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
            if (importer == null) throw new FileNotFoundException("Character importer is missing.", sourcePath);
            bool dirty = false;
            if (!importer.bakeAxisConversion)
            {
                importer.bakeAxisConversion = true;
                dirty = true;
            }
            if (importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                dirty = true;
            }
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                dirty = true;
            }
            HumanDescription description = importer.humanDescription;
            HumanBone[] mapping = BuildHumanoidMapping(sourcePath);
            if (!SameHumanoidMapping(description.human, mapping))
            {
                description.human = mapping;
                importer.humanDescription = description;
                dirty = true;
            }
            if (dirty) importer.SaveAndReimport();
        }

        private static HumanBone[] BuildHumanoidMapping(string sourcePath)
        {
            bool kayKit = sourcePath.IndexOf("/KayKit_", StringComparison.OrdinalIgnoreCase) >= 0;
            bool knight = sourcePath == WardenSource;
            // Quaternius' RPG/Knight rigs branch both legs from Body; their Hips bone only
            // starts the torso chain. Unity requires the humanoid Hips mapping to be an
            // ancestor of both upper legs, so Body is the correct semantic root here.
            string hips = kayKit ? "hips" : "Body";
            string spine = kayKit ? "spine" : "Hips";
            string chest = kayKit ? "chest" : "Abdomen";
            string head = kayKit ? "head" : "Head";
            string leftUpperArm = kayKit ? "upperarm.l" : "UpperArm.L";
            string leftLowerArm = kayKit ? "lowerarm.l" : "LowerArm.L";
            string leftHand = kayKit ? "hand.l" : knight ? "Palm.L" : "Fist.L";
            string rightUpperArm = kayKit ? "upperarm.r" : "UpperArm.R";
            string rightLowerArm = kayKit ? "lowerarm.r" : "LowerArm.R";
            string rightHand = kayKit ? "hand.r" : knight ? "Palm.R" : "Fist.R";
            string leftUpperLeg = kayKit ? "upperleg.l" : "UpperLeg.L";
            string leftLowerLeg = kayKit ? "lowerleg.l" : "LowerLeg.L";
            // Foot.L/Foot.R are detached control bones in these source FBXs; the lower-leg
            // end bones are the deform-chain descendants required by Unity's Humanoid rig.
            string leftFoot = kayKit ? "foot.l" : "LowerLeg.L_end";
            string rightUpperLeg = kayKit ? "upperleg.r" : "UpperLeg.R";
            string rightLowerLeg = kayKit ? "lowerleg.r" : "LowerLeg.R";
            string rightFoot = kayKit ? "foot.r" : "LowerLeg.R_end";

            var bones = new System.Collections.Generic.List<HumanBone>
            {
                Human("Hips", hips),
                Human("Spine", spine),
                Human("Chest", chest),
                Human("Head", head),
                Human("LeftUpperArm", leftUpperArm),
                Human("LeftLowerArm", leftLowerArm),
                Human("LeftHand", leftHand),
                Human("RightUpperArm", rightUpperArm),
                Human("RightLowerArm", rightLowerArm),
                Human("RightHand", rightHand),
                Human("LeftUpperLeg", leftUpperLeg),
                Human("LeftLowerLeg", leftLowerLeg),
                Human("LeftFoot", leftFoot),
                Human("RightUpperLeg", rightUpperLeg),
                Human("RightLowerLeg", rightLowerLeg),
                Human("RightFoot", rightFoot)
            };
            if (!kayKit)
            {
                bones.Add(Human("UpperChest", "Torso"));
                bones.Add(Human("Neck", "Neck"));
                bones.Add(Human("LeftShoulder", "Shoulder.L"));
                bones.Add(Human("RightShoulder", "Shoulder.R"));
            }
            else
            {
                bones.Add(Human("LeftToes", "toes.l"));
                bones.Add(Human("RightToes", "toes.r"));
            }
            return bones.ToArray();
        }

        private static HumanBone Human(string humanName, string boneName)
        {
            return new HumanBone
            {
                humanName = humanName,
                boneName = boneName,
                limit = new HumanLimit { useDefaultValues = true }
            };
        }

        private static bool SameHumanoidMapping(HumanBone[] current, HumanBone[] expected)
        {
            if (current == null || current.Length != expected.Length) return false;
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].humanName != expected[i].humanName || current[i].boneName != expected[i].boneName)
                    return false;
            }
            return true;
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void ValidateLocalLicenses()
        {
            string[] required =
            {
                CharacterRoot + "/RPG Characters - Nov 2020/License.txt",
                CharacterRoot + "/Animated Monster Pack by @Quaternius/License.txt",
                CharacterRoot + "/KayKit_Skeletons/LICENSE.txt",
                DownloadRoot + "/07_Environment_Ruins/Updated_Modular_Dungeon/License.txt"
            };
            foreach (string path in required)
            {
                if (!File.Exists(path)) throw new FileNotFoundException("Selected CC0 license is missing.", path);
            }
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException($"Required M6 asset is missing: {path}", path);
            return asset;
        }
    }
}
