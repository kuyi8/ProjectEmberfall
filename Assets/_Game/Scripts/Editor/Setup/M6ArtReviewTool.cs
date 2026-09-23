using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    /// <summary>
    /// Renders imported art under one neutral lighting rig so selection is based on
    /// geometry and actual Unity materials rather than filenames or store thumbnails.
    /// </summary>
    public static class M6ArtReviewTool
    {
        private const string OutputRoot = "Builds/ArtReview/0.6.0-candidates";
        private const string IntegratedOutputRoot = "Builds/ArtReview/0.6.2-integrated";
        private const string WeaponDetailOutputRoot = "Builds/ArtReview/0.6.2-weapon-candidates";
        private const string AnimationOutputRoot = "Builds/ArtReview/0.6.3-animation";
        private const string CombatVfxOutputRoot = "Builds/ArtReview/0.6.4-vfx-candidates";
        private const string SwordTrailOutputRoot = "Builds/ArtReview/0.6.5-sword-trail";
        private const string RangedCombatOutputRoot = "Builds/ArtReview/0.6.6-ranged-combat";
        private const string WardenVfxOutputRoot = "Builds/ArtReview/0.6.8-warden-vfx-candidates";
        private const string WardenVfxIntegratedOutputRoot = "Builds/ArtReview/0.6.8-warden-vfx-integrated";
        private const string PlayerCompatibilityOutputRoot = "Builds/ArtReview/0.7.8-player-model-candidates";
        private const string PlayerIntegratedOutputRoot = "Builds/ArtReview/0.7.8-player-model-integrated";
        private const string ScorchedCandidateOutputRoot = "Builds/ArtReview/0.8.0-scorched-candidates";
        private const string EmberValleyScenePath = "Assets/_Game/Scenes/10_EmberValley.unity";
        private const string DownloadRoot = "Assets/_Game/Art/DownloadResources/UnityFreeAssets";
        private const string CharacterRoot = DownloadRoot + "/03_Character_Kit";
        private const string WardenRoot = DownloadRoot + "/01_Warden_Boss/Knight Character by @Quaternius/FBX";
        private const string WeaponRoot = DownloadRoot + "/04_Weapons/RPG Pack/FBX";
        private const string KayKitGearRoot = CharacterRoot +
            "/KayKit_Skeletons/addons/kaykit_character_pack_skeletons/Assets/fbx";
        private const string DungeonRoot = DownloadRoot + "/07_Environment_Ruins/Updated_Modular_Dungeon/FBX";
        private const string KayKitAdventurerRoot = CharacterRoot +
            "/KayKit_Adventurers/addons/kaykit_character_pack_adventures/Characters/fbx";
        private const int ReviewLayer = 31;

        public static void PrepareSelectedAndCapture()
        {
            PrepareSelectedModelImporters();
            CaptureImportedCandidates();
        }

        [MenuItem("Emberfall/Review/Capture M5c Scorched Model Candidates")]
        public static void CaptureM5cScorchedCandidates()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("M5c character review requires a graphics device.");

            string knightPath = KayKitAdventurerRoot + "/Knight.fbx";
            string barbarianPath = KayKitAdventurerRoot + "/Barbarian.fbx";
            string currentGuardPath =
                "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_FogwalkerSkeleton.prefab";
            string wardenPath =
                "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Boss_EmberWarden.prefab";
            string licensePath = CharacterRoot + "/KayKit_Adventurers/LICENSE.txt";
            if (!File.Exists(licensePath))
                throw new FileNotFoundException("KayKit Adventurers CC0 license is missing.", licensePath);

            ConfigureImporter(knightPath, true);
            ConfigureImporter(barbarianPath, true);
            string absoluteOutput = Path.GetFullPath(ScorchedCandidateOutputRoot);
            Directory.CreateDirectory(absoluteOutput);

            CaptureLineup(
                Path.Combine(absoluteOutput, "01-neutral-front.png"),
                new[]
                {
                    Candidate.Character("Current_RuinGuard", currentGuardPath, null),
                    Candidate.Character("KayKit_Knight", knightPath, null),
                    Candidate.Character("KayKit_Barbarian", barbarianPath, null),
                    Candidate.Character("Current_Warden", wardenPath, null)
                },
                0f,
                2.05f,
                new Vector3(0f, 1.4f, -11.4f),
                new Vector3(0f, 1.02f, 0f),
                38f);
            CaptureLineup(
                Path.Combine(absoluteOutput, "02-neutral-three-quarter.png"),
                new[]
                {
                    Candidate.Character("Current_RuinGuard", currentGuardPath, null),
                    Candidate.Character("KayKit_Knight", knightPath, null),
                    Candidate.Character("KayKit_Barbarian", barbarianPath, null),
                    Candidate.Character("Current_Warden", wardenPath, null)
                },
                28f,
                2.05f,
                new Vector3(0f, 1.45f, -11.4f),
                new Vector3(0f, 1.02f, 0f),
                38f);

            CaptureM5cCandidateInCourtyard(
                Path.Combine(absoluteOutput, "03-courtyard-knight.png"), knightPath, "KayKit_Knight");
            CaptureM5cCandidateInCourtyard(
                Path.Combine(absoluteOutput, "04-courtyard-barbarian.png"), barbarianPath, "KayKit_Barbarian");

            AssetDatabase.Refresh();
            Debug.Log(
                $"EMBERFALL_M5C_SCORCHED_REVIEW_COMPLETE output={absoluteOutput} " +
                "license=CC0 candidates=knight,barbarian,current-guard,current-warden");
        }

        private static void CaptureM5cCandidateInCourtyard(
            string outputPath,
            string candidatePath,
            string candidateName)
        {
            EditorSceneManager.OpenScene(EmberValleyScenePath, OpenSceneMode.Single);
            foreach (CombatTarget target in UnityEngine.Object.FindObjectsOfType<CombatTarget>(true))
            {
                if (target.name.IndexOf("Courtyard", StringComparison.OrdinalIgnoreCase) >= 0)
                    target.gameObject.SetActive(false);
            }

            GameObject candidate = InstantiateRequired(candidatePath, candidateName);
            ApplyNeutralUrpMaterials(candidate, null);
            SetLayerRecursively(candidate.transform, 0);
            FitAndPlace(candidate, new Vector3(29f, 0f, 45f), 2.05f, 198f, true);
            CaptureLoadedScene(
                outputPath,
                new Vector3(28.5f, 2.65f, 37.5f),
                new Vector3(29f, 1.05f, 45f),
                42f);
            UnityEngine.Object.DestroyImmediate(candidate);
        }

        [MenuItem("Emberfall/Review/Capture Player Model Animation Compatibility")]
        public static void CapturePlayerModelAnimationCompatibility()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Player animation review requires a graphics device.");

            string absoluteOutput = Path.GetFullPath(PlayerCompatibilityOutputRoot);
            Directory.CreateDirectory(absoluteOutput);
            GameObject warrior = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Player_Warrior.prefab");
            GameObject ranger = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab");
            if (warrior == null || ranger == null)
                throw new FileNotFoundException("Player model review prefabs are missing.");
            AnimationClip walk = FindSourceClip(M1AnimationSetup.Library1Path, "Rig|Walk_Loop");
            AnimationClip sprint = FindSourceClip(M1AnimationSetup.Library1Path, "Rig|Sprint_Loop");
            AnimationClip dodge = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/_Game/Art/Animations/Player/Derived/A_Player_Dodge_InPlace.anim");
            AnimationClip attack = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/_Game/Art/Animations/Player/Derived/A_Player_LightAttack1_InPlace.anim");
            if (dodge == null || attack == null)
                throw new FileNotFoundException("Player derived animation clips are missing.");

            CapturePlayerCandidate(absoluteOutput, "warrior", warrior, walk, sprint, dodge, attack);
            CapturePlayerCandidate(absoluteOutput, "ranger", ranger, walk, sprint, dodge, attack);
            AssetDatabase.Refresh();
            Debug.Log($"EMBERFALL_PLAYER_MODEL_REVIEW_COMPLETE output={absoluteOutput}");
        }

        private static void CapturePlayerCandidate(
            string absoluteOutput,
            string label,
            GameObject prefab,
            AnimationClip walk,
            AnimationClip sprint,
            AnimationClip dodge,
            AnimationClip attack)
        {
            CaptureActorCloseup(Path.Combine(absoluteOutput, $"{label}-01-walk.png"),
                prefab, 1f, walk, 0.35f);
            CaptureActorCloseup(Path.Combine(absoluteOutput, $"{label}-02-sprint.png"),
                prefab, 1f, sprint, 0.42f);
            CaptureActorCloseup(Path.Combine(absoluteOutput, $"{label}-03-dodge.png"),
                prefab, 1f, dodge, 0.55f);
            CaptureActorCloseup(Path.Combine(absoluteOutput, $"{label}-04-light-attack.png"),
                prefab, 1f, attack, 0.52f);
        }

        [MenuItem("Emberfall/Review/Capture Integrated Player Model")]
        public static void CaptureIntegratedPlayerModel()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Integrated player review requires a graphics device.");

            string absoluteOutput = Path.GetFullPath(PlayerIntegratedOutputRoot);
            Directory.CreateDirectory(absoluteOutput);
            EditorSceneManager.OpenScene(EmberValleyScenePath, OpenSceneMode.Single);
            CaptureLoadedScene(
                Path.Combine(absoluteOutput, "01-gameplay-camera-scale.png"),
                new Vector3(0f, 4.7f, -9.4f),
                new Vector3(0f, 1.05f, -2.6f),
                48f);

            PlayerCombatActor player = UnityEngine.Object.FindObjectOfType<PlayerCombatActor>(true);
            if (player == null) throw new InvalidDataException("Integrated scene has no player actor.");
            CaptureActorCloseup(
                Path.Combine(absoluteOutput, "02-player-equipped-idle.png"),
                player.gameObject,
                -1f);
            AnimationClip attack = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/_Game/Art/Animations/Player/Derived/A_Player_LightAttack1_InPlace.anim");
            if (attack == null) throw new FileNotFoundException("Player light attack clip is missing.");
            CaptureActorCloseup(
                Path.Combine(absoluteOutput, "03-player-equipped-light-attack.png"),
                player.gameObject,
                -1f,
                attack,
                0.52f);

            AssetDatabase.Refresh();
            Debug.Log($"EMBERFALL_PLAYER_MODEL_INTEGRATED_REVIEW_COMPLETE output={absoluteOutput}");
        }

        [MenuItem("Emberfall/Review/Capture M6 Imported Art Candidates")]
        public static void CaptureImportedCandidates()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                throw new InvalidOperationException(
                    "M6 art review requires a graphics device; do not use -nographics.");
            }

            string absoluteOutput = Path.GetFullPath(OutputRoot);
            Directory.CreateDirectory(absoluteOutput);

            CaptureLineup(
                Path.Combine(absoluteOutput, "01-character-lineup-front.png"),
                new[]
                {
                    Candidate.Character(
                        "Player_Warrior",
                        CharacterRoot + "/RPG Characters - Nov 2020/FBX/Warrior.fbx",
                        CharacterRoot + "/RPG Characters - Nov 2020/Textures/Warrior_Texture.png"),
                    Candidate.Character(
                        "Melee_Skeleton",
                        CharacterRoot + "/KayKit_Skeletons/addons/kaykit_character_pack_skeletons/Characters/fbx/Skeleton_Warrior.fbx",
                        null),
                    Candidate.Character(
                        "Ranged_Wizard",
                        CharacterRoot + "/RPG Characters - Nov 2020/FBX/Wizard.fbx",
                        CharacterRoot + "/RPG Characters - Nov 2020/Textures/Wizard_Texture.png"),
                    Candidate.Character("Boss_AnimatedKnight", WardenRoot + "/KnightCharacter.fbx", null)
                },
                0f,
                2.05f,
                new Vector3(0f, 1.3f, -10.2f),
                new Vector3(0f, 1.05f, 0f),
                38f);

            CaptureLineup(
                Path.Combine(absoluteOutput, "02-character-lineup-back.png"),
                new[]
                {
                    Candidate.Character(
                        "Player_Warrior",
                        CharacterRoot + "/RPG Characters - Nov 2020/FBX/Warrior.fbx",
                        CharacterRoot + "/RPG Characters - Nov 2020/Textures/Warrior_Texture.png"),
                    Candidate.Character(
                        "Melee_Skeleton",
                        CharacterRoot + "/KayKit_Skeletons/addons/kaykit_character_pack_skeletons/Characters/fbx/Skeleton_Warrior.fbx",
                        null),
                    Candidate.Character(
                        "Ranged_Wizard",
                        CharacterRoot + "/RPG Characters - Nov 2020/FBX/Wizard.fbx",
                        CharacterRoot + "/RPG Characters - Nov 2020/Textures/Wizard_Texture.png"),
                    Candidate.Character("Boss_AnimatedKnight", WardenRoot + "/KnightCharacter.fbx", null)
                },
                180f,
                2.05f,
                new Vector3(0f, 1.3f, -10.2f),
                new Vector3(0f, 1.05f, 0f),
                38f);

            CaptureLineup(
                Path.Combine(absoluteOutput, "03-weapon-lineup.png"),
                new[]
                {
                    Candidate.Prop("Player_Sword", WardenRoot + "/Sword.fbx"),
                    Candidate.Prop("Boss_Katana", WardenRoot + "/Katana.fbx"),
                    Candidate.Prop("Guard_Shield", WeaponRoot + "/Shield.fbx"),
                    Candidate.Prop("Priest_Staff", WeaponRoot + "/Staff.fbx")
                },
                25f,
                2.1f,
                new Vector3(0f, 1.8f, -11.2f),
                new Vector3(0f, 1.1f, 0f),
                38f);

            CaptureLineup(
                Path.Combine(absoluteOutput, "04-dungeon-module-lineup.png"),
                new[]
                {
                    Candidate.Prop("Dungeon_Arch", DungeonRoot + "/Arch.fbx"),
                    Candidate.Prop("Dungeon_Column", DungeonRoot + "/Column.fbx"),
                    Candidate.Prop("Dungeon_Wall", DungeonRoot + "/Wall_Modular.fbx"),
                    Candidate.Prop("Dungeon_Statue", DungeonRoot + "/Statue_Horse.fbx")
                },
                205f,
                3.3f,
                new Vector3(0f, 2.3f, -14.5f),
                new Vector3(0f, 1.55f, 0f),
                40f);

            AssetDatabase.Refresh();
            Debug.Log($"EMBERFALL_M6_ART_REVIEW_COMPLETE output={absoluteOutput}");
        }

        [MenuItem("Emberfall/Review/Capture M6 Weapon Candidates")]
        public static void CaptureWeaponCandidates()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("M6 weapon review requires a graphics device.");

            string[] paths =
            {
                WardenRoot + "/Sword.fbx",
                WardenRoot + "/ShortSword.fbx",
                WardenRoot + "/Katana.fbx",
                KayKitGearRoot + "/Skeleton_Axe.fbx",
                KayKitGearRoot + "/Skeleton_Shield_Large_A.fbx",
                KayKitGearRoot + "/Skeleton_Shield_Small_A.fbx",
                WeaponRoot + "/Shield.fbx"
            };
            foreach (string path in paths) ConfigureImporter(path, false);

            string absoluteOutput = Path.GetFullPath(WeaponDetailOutputRoot);
            Directory.CreateDirectory(absoluteOutput);
            CaptureLineup(
                Path.Combine(absoluteOutput, "01-weapon-detail-lineup.png"),
                new[]
                {
                    Candidate.Prop("Warden_Sword", paths[0]),
                    Candidate.Prop("Skeleton_ShortSword", paths[1]),
                    Candidate.Prop("Warden_Katana", paths[2]),
                    Candidate.Prop("Skeleton_Axe", paths[3]),
                    Candidate.Prop("Skeleton_Shield_Large", paths[4]),
                    Candidate.Prop("Skeleton_Shield_Small", paths[5]),
                    Candidate.Prop("RPG_Shield", paths[6])
                },
                25f,
                1.25f,
                new Vector3(0f, 1.3f, -9.8f),
                new Vector3(0f, 0.72f, 0f),
                34f);

            Scene shieldScene = CreateReviewScene();
            string[] shieldLabels = { "Skeleton_Large", "Skeleton_Small", "RPG_Shield" };
            for (int i = 0; i < 3; i++)
            {
                GameObject instance = InstantiateRequired(paths[i + 4], shieldLabels[i]);
                ApplyNeutralUrpMaterials(instance, null);
                FitAndPlace(
                    instance,
                    new Vector3((i - 1) * 2.1f, 0.25f, 0f),
                    1.25f,
                    new Vector3(90f, 25f, 0f),
                    false);
            }
            CaptureScene(
                shieldScene,
                Path.Combine(absoluteOutput, "02-shield-fronts.png"),
                new Vector3(0f, 1.2f, -7.5f),
                new Vector3(0f, 0.85f, 0f),
                32f);
            Debug.Log($"EMBERFALL_M6_WEAPON_REVIEW_COMPLETE output={absoluteOutput}");
        }

        [MenuItem("Emberfall/Review/Capture M6 Integrated Scene")]
        public static void CaptureIntegratedScene()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                throw new InvalidOperationException(
                    "M6 integrated scene review requires a graphics device; do not use -nographics.");
            }

            string absoluteOutput = Path.GetFullPath(IntegratedOutputRoot);
            Directory.CreateDirectory(absoluteOutput);
            Scene scene = EditorSceneManager.OpenScene(EmberValleyScenePath, OpenSceneMode.Single);
            CaptureLoadedScene(Path.Combine(absoluteOutput, "01-camp-player.png"),
                new Vector3(0f, 5.8f, -13f), new Vector3(0f, 1.15f, 1.5f), 46f);
            CaptureLoadedScene(Path.Combine(absoluteOutput, "02-forest-combat.png"),
                new Vector3(-11f, 7.2f, 27f), new Vector3(0f, 1.2f, 36f), 45f);
            CaptureLoadedScene(Path.Combine(absoluteOutput, "03-courtyard-combat.png"),
                new Vector3(17f, 8.5f, 58f), new Vector3(28f, 1.2f, 44f), 48f);
            CaptureLoadedScene(Path.Combine(absoluteOutput, "04-warden-arena.png"),
                new Vector3(34f, 8.2f, 4f), new Vector3(46f, 1.2f, 20f), 46f);
            CaptureIntegratedCharacterLineup(Path.Combine(absoluteOutput, "05-character-prefabs.png"));
            scene = EditorSceneManager.OpenScene(EmberValleyScenePath, OpenSceneMode.Single);
            ShieldEnemyActor guard = UnityEngine.Object.FindObjectOfType<ShieldEnemyActor>(true);
            WardenActor warden = UnityEngine.Object.FindObjectOfType<WardenActor>(true);
            CaptureActorCloseup(Path.Combine(absoluteOutput, "06-ruin-guard-equipment.png"), guard.gameObject);
            CaptureActorCloseup(Path.Combine(absoluteOutput, "07-warden-equipment.png"), warden.gameObject, -1f);
            Debug.Log($"EMBERFALL_M6_INTEGRATED_REVIEW_COMPLETE scene={scene.path} output={absoluteOutput}");
        }

        [MenuItem("Emberfall/Review/Capture 0.8.10 Arena Expansion")]
        public static void CaptureM5dArenaExpansion()
        {
            M5dArenaRepair.Apply();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("0.8.10 arena review requires a graphics device.");

            string absoluteOutput = Path.GetFullPath("Builds/ArtReview/0.8.10a");
            Directory.CreateDirectory(absoluteOutput);
            Scene scene = EditorSceneManager.OpenScene(EmberValleyScenePath, OpenSceneMode.Single);
            CaptureLoadedScene(Path.Combine(absoluteOutput, "01-forest-boundaries.png"),
                new Vector3(-10f, 9f, 22f), new Vector3(0f, 0.8f, 35f), 52f);
            CaptureLoadedScene(Path.Combine(absoluteOutput, "02-bridge-boundaries.png"),
                new Vector3(1f, 10f, 55f), new Vector3(13f, 0.8f, 44.8f), 50f);
            CaptureLoadedScene(Path.Combine(absoluteOutput, "03-courtyard-boundaries.png"),
                new Vector3(15f, 11f, 60f), new Vector3(29f, 0.8f, 45f), 52f);
            CaptureLoadedScene(Path.Combine(absoluteOutput, "04-pre-sanctum-boundaries.png"),
                new Vector3(27f, 9f, 42f), new Vector3(39f, 0.8f, 31f), 48f);
            CaptureLoadedScene(Path.Combine(absoluteOutput, "05-forest-player-height.png"),
                new Vector3(0f, 3.7f, 25f), new Vector3(1f, 1f, 36f), 62f);
            CaptureLoadedScene(Path.Combine(absoluteOutput, "06-bridge-player-height.png"),
                new Vector3(6.8f, 3.7f, 44.5f), new Vector3(15f, 1f, 45f), 62f);
            Debug.Log($"EMBERFALL_M5D_ARENA_REVIEW_COMPLETE scene={scene.path} output={absoluteOutput}");
        }

        [MenuItem("Emberfall/Review/Capture M5c Scorched Elite Integrated")]
        public static void CaptureM5cScorchedEliteIntegrated()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("M5c integrated art review requires a graphics device.");

            string absoluteOutput = Path.GetFullPath("Builds/ArtReview/0.8.0-scorched-integrated");
            Directory.CreateDirectory(absoluteOutput);
            Scene scene = EditorSceneManager.OpenScene(EmberValleyScenePath, OpenSceneMode.Single);
            CaptureLoadedScene(
                Path.Combine(absoluteOutput, "01-courtyard-gameplay-scale.png"),
                new Vector3(17f, 8.5f, 58f),
                new Vector3(28f, 1.2f, 44f),
                48f);
            scene = EditorSceneManager.OpenScene(EmberValleyScenePath, OpenSceneMode.Single);
            ShieldEnemyActor elite = UnityEngine.Object.FindObjectOfType<ShieldEnemyActor>(true);
            AnimationClip guardClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/_Game/Art/Animations/Player/Derived/A_Enemy_ShieldGuard_InPlace.anim");
            CaptureActorCloseup(
                Path.Combine(absoluteOutput, "02-scorched-elite-guard-closeup.png"),
                elite.gameObject,
                1f,
                guardClip,
                0.32f);
            Debug.Log($"EMBERFALL_M5C_SCORCHED_INTEGRATED_REVIEW_COMPLETE scene={scene.path} output={absoluteOutput}");
        }

        [MenuItem("Emberfall/Review/Capture M6 Shield Guard Poses")]
        public static void CaptureShieldGuardPoses()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("M6 animation review requires a graphics device.");

            string absoluteOutput = Path.GetFullPath(AnimationOutputRoot);
            Directory.CreateDirectory(absoluteOutput);
            Scene scene = EditorSceneManager.OpenScene(EmberValleyScenePath, OpenSceneMode.Single);
            ShieldEnemyActor guard = UnityEngine.Object.FindObjectOfType<ShieldEnemyActor>(true);
            WardenActor warden = UnityEngine.Object.FindObjectOfType<WardenActor>(true);
            AnimationClip shieldGuard = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/_Game/Art/Animations/Player/Derived/A_Enemy_ShieldGuard_InPlace.anim");
            AnimationClip swordBlock = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/_Game/Art/Animations/Player/Derived/A_Player_Guard_InPlace.anim");
            AnimationClip shieldBash = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/_Game/Art/Animations/Player/Derived/A_Enemy_ShieldBash_InPlace.anim");
            if (shieldGuard == null) throw new FileNotFoundException("Derived shield guard clip is missing.");
            CaptureActorCloseup(
                Path.Combine(absoluteOutput, "01-ruin-guard-guard-pose.png"),
                guard.gameObject,
                1f,
                shieldGuard,
                0.32f);
            CaptureActorCloseup(
                Path.Combine(absoluteOutput, "03-warden-sword-block-pose.png"),
                warden.gameObject,
                -1f,
                swordBlock,
                0.36f);
            CaptureActorCloseup(
                Path.Combine(absoluteOutput, "04-warden-shield-action-018.png"),
                warden.gameObject,
                -1f,
                shieldBash,
                0.18f);
            CaptureActorCloseup(
                Path.Combine(absoluteOutput, "05-warden-shield-action-038.png"),
                warden.gameObject,
                -1f,
                shieldBash,
                0.38f);
            CaptureActorCloseup(
                Path.Combine(absoluteOutput, "06-warden-shield-action-058.png"),
                warden.gameObject,
                -1f,
                shieldBash,
                0.58f);
            CaptureActorCloseup(
                Path.Combine(absoluteOutput, "02-warden-guard-pose.png"),
                warden.gameObject,
                -1f,
                shieldGuard,
                0.32f);
            Debug.Log($"EMBERFALL_M6_SHIELD_GUARD_REVIEW_COMPLETE scene={scene.path} output={absoluteOutput}");
        }

        [MenuItem("Emberfall/Review/Capture M6 Combat VFX Candidates")]
        public static void CaptureCombatVfxCandidates()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("M6 VFX review requires a graphics device.");

            string root =
                "Assets/_Game/Art/DownloadResources/Hovl Studio/Magic effects pack/Prefabs";
            (string Name, string Path, float Time, float Scale)[] candidates =
            {
                ("01-steel-sparks", root + "/Sparks/Sparks explode white.prefab", 0.12f, 0.65f),
                ("02-guard-sparks", root + "/Sparks/Sparks explode yellow.prefab", 0.12f, 0.65f),
                ("03-ember-sparks", root + "/Sparks/Sparks explode red.prefab", 0.12f, 0.65f),
                ("04-stone-hit", root + "/Hits and explosions/Stones hit.prefab", 0.18f, 0.65f),
                ("05-holy-hit", root + "/Hits and explosions/Holy hit.prefab", 0.18f, 0.65f),
                ("06-charge-slash-red", root + "/Slash effects/Charge slash red.prefab", 0.2f, 0.55f)
            };

            string absoluteOutput = Path.GetFullPath(CombatVfxOutputRoot);
            Directory.CreateDirectory(absoluteOutput);
            foreach ((string name, string path, float time, float scale) in candidates)
            {
                Scene scene = CreateReviewScene();
                GameObject instance = InstantiateRequired(path, name);
                instance.transform.SetPositionAndRotation(new Vector3(0f, 1.05f, 0f), Quaternion.identity);
                instance.transform.localScale = Vector3.one * scale;
                foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.gameObject.SetActive(true);
                    particles.Simulate(time, true, true, true);
                    particles.Pause(true);
                }

                CaptureScene(
                    scene,
                    Path.Combine(absoluteOutput, name + ".png"),
                    new Vector3(0f, 1.55f, -4.6f),
                    new Vector3(0f, 1.05f, 0f),
                    34f);
            }

            Debug.Log($"EMBERFALL_M6_VFX_REVIEW_COMPLETE output={absoluteOutput}");
        }

        [MenuItem("Emberfall/Review/Capture M6 Warden VFX Candidates")]
        public static void CaptureWardenVfxCandidates()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("M6 Warden VFX review requires a graphics device.");

            string root =
                "Assets/_Game/Art/DownloadResources/Hovl Studio/Magic effects pack/Prefabs";
            (string Name, string Path, float Time, float Scale, Vector3 Position)[] candidates =
            {
                ("01-phase-debuff", root + "/Character auras/Debuff.prefab", 0.7f, 0.8f, new Vector3(0f, 0.05f, 0f)),
                ("02-phase-magic-circle", root + "/Magic circles/Magic circle 2.prefab", 0.7f, 1.1f, new Vector3(0f, 0.05f, 0f)),
                ("03-phase-red-energy", root + "/AoE effects/Red energy explosion.prefab", 0.3f, 0.9f, new Vector3(0f, 0.05f, 0f)),
                ("04-cleave-charge-purple", root + "/Slash effects/Charge slash purple.prefab", 0.22f, 0.72f, new Vector3(0f, 1.05f, 0f)),
                ("05-cleave-charge-red", root + "/Slash effects/Charge slash red.prefab", 0.22f, 0.72f, new Vector3(0f, 1.05f, 0f)),
                ("06-cleave-aoe-orange", root + "/AoE effects/AoE slash orange.prefab", 0.22f, 0.8f, new Vector3(0f, 0.06f, 0f)),
                ("07-blast-ground-aoe", root + "/AoE effects/Ground AOE explosion.prefab", 0.24f, 0.85f, new Vector3(0f, 0.06f, 0f)),
                ("08-blast-red-energy", root + "/AoE effects/Red energy explosion.prefab", 0.24f, 0.8f, new Vector3(0f, 0.06f, 0f)),
                ("09-blast-explosion", root + "/Hits and explosions/Explosion.prefab", 0.18f, 0.8f, new Vector3(0f, 0.06f, 0f))
                ,("10-phase-magic-circle-early", root + "/Magic circles/Magic circle 2.prefab", 0.22f, 1.1f, new Vector3(0f, 0.05f, 0f))
                ,("11-phase-magic-circle-late", root + "/Magic circles/Magic circle 2.prefab", 1.3f, 1.1f, new Vector3(0f, 0.05f, 0f))
                ,("12-cleave-aoe-orange-early", root + "/AoE effects/AoE slash orange.prefab", 0.1f, 0.8f, new Vector3(0f, 0.06f, 0f))
                ,("13-cleave-aoe-orange-late", root + "/AoE effects/AoE slash orange.prefab", 0.38f, 0.8f, new Vector3(0f, 0.06f, 0f))
                ,("14-blast-red-energy-mid", root + "/AoE effects/Red energy explosion.prefab", 0.42f, 0.8f, new Vector3(0f, 0.06f, 0f))
                ,("15-blast-red-energy-late", root + "/AoE effects/Red energy explosion.prefab", 0.72f, 0.8f, new Vector3(0f, 0.06f, 0f))
                ,("16-blast-ground-aoe-early", root + "/AoE effects/Ground AOE explosion.prefab", 0.12f, 0.85f, new Vector3(0f, 0.06f, 0f))
                ,("17-blast-ground-aoe-late", root + "/AoE effects/Ground AOE explosion.prefab", 0.46f, 0.85f, new Vector3(0f, 0.06f, 0f))
            };

            string absoluteOutput = Path.GetFullPath(WardenVfxOutputRoot);
            Directory.CreateDirectory(absoluteOutput);
            foreach ((string name, string path, float time, float scale, Vector3 position) in candidates)
            {
                Scene scene = CreateReviewScene();
                GameObject instance = InstantiateRequired(path, name);
                instance.transform.SetPositionAndRotation(position, Quaternion.identity);
                instance.transform.localScale = Vector3.one * scale;
                foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.gameObject.SetActive(true);
                    particles.Simulate(time, true, true, true);
                    particles.Pause(true);
                }

                CaptureScene(
                    scene,
                    Path.Combine(absoluteOutput, name + ".png"),
                    new Vector3(0f, 2.2f, -5.6f),
                    new Vector3(0f, 0.85f, 0f),
                    38f);
            }

            Debug.Log($"EMBERFALL_M6_WARDEN_VFX_REVIEW_COMPLETE output={absoluteOutput}");
        }

        [MenuItem("Emberfall/Review/Capture M6 Integrated Warden VFX")]
        public static void CaptureIntegratedWardenVfx()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Integrated Warden VFX review requires a graphics device.");

            string absoluteOutput = Path.GetFullPath(WardenVfxIntegratedOutputRoot);
            Directory.CreateDirectory(absoluteOutput);
            (string Name, string PrefabPath, float Time, float ForwardOffset)[] captures =
            {
                ("01-phase-transition", "Assets/_Game/Prefabs/VFX/M6Art/P_M6_Warden_PhaseTransition.prefab", 0.78f, 0f),
                ("02-rune-cleave", "Assets/_Game/Prefabs/VFX/M6Art/P_M6_Warden_RuneCleave.prefab", 0.18f, 0.45f),
                ("03-delayed-blast", "Assets/_Game/Prefabs/VFX/M6Art/P_M6_Warden_DelayedBlast.prefab", 0.24f, 4.2f)
            };

            foreach ((string name, string prefabPath, float time, float forwardOffset) in captures)
            {
                Scene scene = EditorSceneManager.OpenScene(EmberValleyScenePath, OpenSceneMode.Single);
                WardenActor warden = UnityEngine.Object.FindObjectOfType<WardenActor>(true);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (warden == null || prefab == null)
                    throw new InvalidDataException($"Integrated Warden VFX dependency is missing: {name}");

                GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance == null) throw new InvalidOperationException($"Could not instantiate {prefabPath}");
                Vector3 effectPosition = warden.transform.position + (warden.transform.forward * forwardOffset);
                instance.transform.SetPositionAndRotation(effectPosition, warden.transform.rotation);
                foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.gameObject.SetActive(true);
                    particles.Simulate(time, true, true, true);
                    particles.Pause(true);
                }

                Vector3 focus = effectPosition + Vector3.up * (forwardOffset > 2f ? 0.65f : 1f);
                Vector3 cameraPosition = effectPosition - (warden.transform.forward * 5.8f) +
                    (warden.transform.right * 2.7f) + (Vector3.up * 3.1f);
                CaptureLoadedScene(
                    Path.Combine(absoluteOutput, name + ".png"),
                    cameraPosition,
                    focus,
                    42f);
                UnityEngine.Object.DestroyImmediate(instance);
            }

            Debug.Log($"EMBERFALL_M6_WARDEN_VFX_INTEGRATED_REVIEW_COMPLETE output={absoluteOutput}");
        }

        [MenuItem("Emberfall/Review/Capture M6 Player Sword Trail")]
        public static void CapturePlayerSwordTrail()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("M6 sword trail review requires a graphics device.");

            string absoluteOutput = Path.GetFullPath(SwordTrailOutputRoot);
            Directory.CreateDirectory(absoluteOutput);
            Scene sourceScene = EditorSceneManager.OpenScene(EmberValleyScenePath, OpenSceneMode.Single);
            PlayerCombatActor source = UnityEngine.Object.FindObjectOfType<PlayerCombatActor>(true);
            if (source == null) throw new InvalidDataException("Player combat actor is missing from Ember Valley.");

            Scene reviewScene = CreateReviewScene();
            GameObject clone = UnityEngine.Object.Instantiate(source.gameObject);
            clone.name = "Player_SwordTrailReview";
            SceneManager.MoveGameObjectToScene(clone, reviewScene);
            clone.transform.SetPositionAndRotation(new Vector3(0f, 1.05f, 0f), Quaternion.identity);
            SetLayerRecursively(clone.transform, ReviewLayer);
            foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;

            Animator animator = clone.GetComponentInChildren<Animator>(true);
            Transform bladeRoot = clone.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "SwordTrailRoot_M6");
            Transform bladeTip = clone.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "SwordTrailTip_M6");
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/_Game/Art/Animations/Player/Derived/A_Player_LightAttack1_InPlace.anim");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/_Game/Art/Materials/M6Art/M_M6_SwordTrail.mat");
            if (animator == null || bladeRoot == null || bladeTip == null || clip == null || material == null)
                throw new InvalidDataException("Sword trail review dependencies are incomplete.");

            float[] samples = { 0.16f, 0.24f, 0.32f, 0.40f, 0.48f, 0.56f, 0.64f };
            var vertices = new Vector3[samples.Length * 2];
            var uvs = new Vector2[samples.Length * 2];
            var colors = new Color[samples.Length * 2];
            var triangles = new int[(samples.Length - 1) * 6];
            for (int i = 0; i < samples.Length; i++)
            {
                clip.SampleAnimation(animator.gameObject, clip.length * samples[i]);
                vertices[i * 2] = Vector3.Lerp(bladeRoot.position, bladeTip.position, 0.18f);
                vertices[(i * 2) + 1] = bladeTip.position;
                float progress = i / (float)(samples.Length - 1);
                float alpha = Mathf.Lerp(0.04f, 1f, progress * progress);
                uvs[i * 2] = new Vector2(progress, 0f);
                uvs[(i * 2) + 1] = new Vector2(progress, 1f);
                colors[i * 2] = new Color(1f, 1f, 1f, alpha);
                colors[(i * 2) + 1] = new Color(1f, 1f, 1f, alpha);
                if (i >= samples.Length - 1) continue;
                int vertex = i * 2;
                int triangle = i * 6;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 1;
                triangles[triangle + 2] = vertex + 2;
                triangles[triangle + 3] = vertex + 1;
                triangles[triangle + 4] = vertex + 3;
                triangles[triangle + 5] = vertex + 2;
            }

            var ribbonObject = new GameObject("SwordTrail_Preview", typeof(MeshFilter), typeof(MeshRenderer));
            ribbonObject.layer = ReviewLayer;
            SceneManager.MoveGameObjectToScene(ribbonObject, reviewScene);
            var mesh = new Mesh { name = "SwordTrail_PreviewMesh", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            ribbonObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = ribbonObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 12;
            float bladeLength = Vector3.Distance(bladeRoot.position, bladeTip.position);

            CaptureScene(
                reviewScene,
                Path.Combine(absoluteOutput, "01-player-light-attack-trail.png"),
                new Vector3(3.2f, 1.75f, -4.2f),
                new Vector3(0f, 1.05f, 0f),
                34f);
            UnityEngine.Object.DestroyImmediate(mesh);
            Debug.Log(
                $"EMBERFALL_M6_SWORD_TRAIL_REVIEW_COMPLETE source={sourceScene.path} " +
                $"bladeLength={bladeLength:F3} output={absoluteOutput}");
        }

        [MenuItem("Emberfall/Review/Capture M6 Ranged Combat Candidates")]
        public static void CaptureRangedCombatCandidates()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("M6 ranged-combat review requires a graphics device.");

            string absoluteOutput = Path.GetFullPath(RangedCombatOutputRoot);
            Directory.CreateDirectory(absoluteOutput);

            string kayKitDagger = CharacterRoot +
                "/KayKit_Adventurers/addons/kaykit_character_pack_adventures/Assets/fbx/dagger.fbx";
            string rpgDagger = DownloadRoot + "/04_Weapons/RPG Pack/FBX/Dagger.fbx";
            string itemDagger = DownloadRoot +
                "/09_Nature_Props/Ultimate RPG Items Pack - Aug 2019/FBX/Dagger.fbx";
            foreach (string path in new[] { kayKitDagger, rpgDagger, itemDagger })
                ConfigureImporter(path, false);

            CaptureLineup(
                Path.Combine(absoluteOutput, "01-throwing-knife-candidates.png"),
                new[]
                {
                    Candidate.Prop("KayKit", kayKitDagger),
                    Candidate.Prop("RPG_Pack", rpgDagger),
                    Candidate.Prop("Items_Pack", itemDagger)
                },
                28f,
                0.65f,
                new Vector3(0f, 0.9f, -5.8f),
                new Vector3(0f, 0.38f, 0f),
                30f);

            Scene scene = EditorSceneManager.OpenScene(EmberValleyScenePath, OpenSceneMode.Single);
            PlayerCombatActor player = UnityEngine.Object.FindObjectOfType<PlayerCombatActor>(true);
            WardenActor warden = UnityEngine.Object.FindObjectOfType<WardenActor>(true);
            if (player == null || warden == null)
                throw new InvalidDataException("Player or Warden is missing from Ember Valley.");

            string library = M1AnimationSetup.Library2Path;
            AnimationClip throwClip = FindSourceClip(library, "Armature|OverhandThrow");
            AnimationClip shieldDash = FindSourceClip(library, "Armature|Shield_Dash_RM");
            AnimationClip swordDash = FindSourceClip(library, "Armature|Sword_Dash_RM");
            CaptureActorCloseup(Path.Combine(absoluteOutput, "02-player-overhand-throw-045.png"),
                player.gameObject, 1f, throwClip, 0.45f);
            CaptureActorCloseup(Path.Combine(absoluteOutput, "03-warden-shield-dash-032.png"),
                warden.gameObject, -1f, shieldDash, 0.32f);
            CaptureActorCloseup(Path.Combine(absoluteOutput, "04-warden-shield-dash-055.png"),
                warden.gameObject, -1f, shieldDash, 0.55f);
            CaptureActorCloseup(Path.Combine(absoluteOutput, "05-warden-sword-dash-032.png"),
                warden.gameObject, -1f, swordDash, 0.32f);
            CaptureActorCloseup(Path.Combine(absoluteOutput, "06-warden-sword-dash-055.png"),
                warden.gameObject, -1f, swordDash, 0.55f);

            AssetDatabase.Refresh();
            Debug.Log($"EMBERFALL_M6_RANGED_COMBAT_REVIEW_COMPLETE scene={scene.path} output={absoluteOutput}");
        }

        private static AnimationClip FindSourceClip(string path, string name)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate => candidate.name == name);
            if (clip == null) throw new FileNotFoundException($"Animation clip '{name}' is missing.", path);
            return clip;
        }

        private static void CaptureActorCloseup(
            string outputPath,
            GameObject actor,
            float facingSign = 1f,
            AnimationClip sampleClip = null,
            float normalizedTime = 0f)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            Scene reviewScene = CreateReviewScene();
            GameObject clone = UnityEngine.Object.Instantiate(actor);
            clone.name = actor.name + "_EquipmentReview";
            SceneManager.MoveGameObjectToScene(clone, reviewScene);
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            SetLayerRecursively(clone.transform, ReviewLayer);

            Animator animator = clone.GetComponentInChildren<Animator>(true);
            bool sampling = sampleClip != null;
            if (sampling)
            {
                if (animator == null) throw new InvalidDataException($"Actor has no Animator: {actor.name}");
                AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(animator.gameObject, sampleClip,
                    Mathf.Clamp01(normalizedTime) * sampleClip.length);
                AnimationMode.EndSampling();
            }

            Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.enabled &&
                                   !renderer.name.Contains("Telegraph") &&
                                   !renderer.name.Contains("Indicator") &&
                                   !renderer.name.Contains("Warning"))
                .ToArray();
            if (renderers.Length == 0)
                throw new InvalidDataException($"Actor has no reviewable renderers: {actor.name}");

            foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true)
                         .Except(renderers))
                renderer.enabled = false;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            clone.transform.position += Vector3.up * -bounds.min.y;
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            Transform visual = animator != null ? animator.transform : clone.transform;
            Vector3 viewDirection = (visual.forward * facingSign + visual.right * 0.55f).normalized;
            float distance = Mathf.Max(3.2f, Mathf.Max(bounds.size.x, bounds.size.y) * 2.05f);
            Vector3 lookAt = bounds.center + Vector3.up * (bounds.size.y * 0.04f);
            try
            {
                CaptureScene(
                    reviewScene,
                    outputPath,
                    lookAt + viewDirection * distance,
                    lookAt,
                    34f,
                    !sampling);
            }
            finally
            {
                if (sampling)
                {
                    AnimationMode.StopAnimationMode();
                    CloseReviewScene(reviewScene);
                }
            }
        }

        private static void CaptureIntegratedCharacterLineup(string outputPath)
        {
            Scene scene = CreateReviewScene();
            string[] paths =
            {
                "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab",
                "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_FogwalkerSkeleton.prefab",
                "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_RunePriest.prefab",
                "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Boss_EmberWarden.prefab"
            };
            string[] names = { "Player", "Fogwalker", "RunePriest", "EmberWarden" };
            const float spacing = 2.8f;
            float start = -0.5f * spacing * (paths.Length - 1);
            for (int i = 0; i < paths.Length; i++)
            {
                GameObject instance = InstantiateRequired(paths[i], names[i]);
                FitAndPlace(instance, new Vector3(start + (i * spacing), 0f, 0f), 2.15f, 0f, true);
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    animator.Rebind();
                    animator.Update(0f);
                }
            }
            CaptureScene(scene, outputPath,
                new Vector3(0f, 1.45f, -11.5f), new Vector3(0f, 1.05f, 0f), 38f);
        }

        private static void CaptureLoadedScene(
            string outputPath,
            Vector3 cameraPosition,
            Vector3 lookAt,
            float fieldOfView)
        {
            GameObject cameraObject = new GameObject("M6 Integrated Review Camera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.035f, 0.047f, 0.06f);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.04f;
            camera.farClipPlane = 180f;
            camera.cullingMask = ~0;
            camera.transform.SetPositionAndRotation(
                cameraPosition,
                Quaternion.LookRotation(lookAt - cameraPosition, Vector3.up));

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
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void PrepareSelectedModelImporters()
        {
            string[] characterPaths =
            {
                CharacterRoot + "/RPG Characters - Nov 2020/FBX/Warrior.fbx",
                CharacterRoot + "/KayKit_Skeletons/addons/kaykit_character_pack_skeletons/Characters/fbx/Skeleton_Warrior.fbx",
                CharacterRoot + "/RPG Characters - Nov 2020/FBX/Wizard.fbx",
                WardenRoot + "/KnightCharacter.fbx"
            };
            string[] propPaths =
            {
                WardenRoot + "/Sword.fbx",
                WardenRoot + "/Katana.fbx",
                WardenRoot + "/Helmet2.fbx",
                WardenRoot + "/ShoulderPads.fbx",
                WeaponRoot + "/Shield.fbx",
                WeaponRoot + "/Staff.fbx",
                DungeonRoot + "/Arch.fbx",
                DungeonRoot + "/Column.fbx",
                DungeonRoot + "/Wall_Modular.fbx",
                DungeonRoot + "/Floor_Modular.fbx",
                DungeonRoot + "/Statue_Horse.fbx",
                DungeonRoot + "/Torch.fbx"
            };

            foreach (string path in characterPaths) ConfigureImporter(path, true);
            foreach (string path in propPaths) ConfigureImporter(path, false);
        }

        private static void ConfigureImporter(string path, bool humanoid)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new FileNotFoundException("Selected model importer is missing.", path);
            bool dirty = false;
            if (!importer.bakeAxisConversion)
            {
                importer.bakeAxisConversion = true;
                dirty = true;
            }
            if (humanoid && importer.animationType != ModelImporterAnimationType.Human)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                dirty = true;
            }
            if (!humanoid && importer.importAnimation)
            {
                importer.importAnimation = false;
                dirty = true;
            }
            if (dirty) importer.SaveAndReimport();
        }

        private static void CaptureLineup(
            string outputPath,
            IReadOnlyList<Candidate> candidates,
            float yaw,
            float targetHeight,
            Vector3 cameraPosition,
            Vector3 lookAt,
            float fieldOfView)
        {
            Scene scene = CreateReviewScene();
            float spacing = targetHeight * 1.32f;
            float start = -0.5f * spacing * (candidates.Count - 1);
            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                GameObject instance = InstantiateRequired(candidate.AssetPath, candidate.Label);
                ApplyNeutralUrpMaterials(instance, candidate.TexturePath);
                FitAndPlace(
                    instance,
                    new Vector3(start + (i * spacing), 0f, 0f),
                    targetHeight,
                    yaw,
                    candidate.UseHeightForScale);
                ReportModel(instance, candidate);
            }

            CaptureScene(scene, outputPath, cameraPosition, lookAt, fieldOfView);
        }

        private static Scene CreateReviewScene()
        {
            Scene active = SceneManager.GetActiveScene();
            if (!active.IsValid() || string.IsNullOrEmpty(active.path))
            {
                EditorSceneManager.OpenScene(
                    "Assets/_Game/Scenes/10_EmberValley.unity",
                    OpenSceneMode.Single);
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.3f, 0.36f);
            RenderSettings.fog = false;
            RenderSettings.skybox = null;

            GameObject key = new GameObject("Key Light", typeof(Light));
            key.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
            Light keyLight = key.GetComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(1f, 0.83f, 0.66f);
            keyLight.intensity = 1.2f;
            keyLight.cullingMask = 1 << ReviewLayer;

            GameObject rim = new GameObject("Rim Light", typeof(Light));
            rim.transform.rotation = Quaternion.Euler(24f, 148f, 0f);
            Light rimLight = rim.GetComponent<Light>();
            rimLight.type = LightType.Directional;
            rimLight.color = new Color(0.35f, 0.58f, 1f);
            rimLight.intensity = 0.8f;
            rimLight.cullingMask = 1 << ReviewLayer;

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Neutral Scale Floor";
            floor.layer = ReviewLayer;
            floor.transform.localScale = new Vector3(2.6f, 1f, 1.15f);
            floor.GetComponent<Renderer>().sharedMaterial = CreateMaterial(
                "M6_ReviewFloor", new Color(0.105f, 0.12f, 0.14f), null, 0.05f);
            return scene;
        }

        private static GameObject InstantiateRequired(string path, string name)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) throw new FileNotFoundException("Art candidate is missing.", path);
            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null) throw new InvalidOperationException($"Could not instantiate candidate: {path}");
            instance.name = name;
            SetLayerRecursively(instance.transform, ReviewLayer);
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            return instance;
        }

        private static void ApplyNeutralUrpMaterials(GameObject root, string texturePath)
        {
            Texture2D forcedTexture = string.IsNullOrWhiteSpace(texturePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] sourceMaterials = renderer.sharedMaterials;
                Material[] replacements = new Material[sourceMaterials.Length];
                for (int i = 0; i < replacements.Length; i++)
                {
                    Material source = sourceMaterials[i];
                    Texture texture = forcedTexture;
                    Color color = Color.white;
                    if (source != null)
                    {
                        if (texture == null && source.HasProperty("_BaseMap")) texture = source.GetTexture("_BaseMap");
                        if (texture == null && source.HasProperty("_MainTex")) texture = source.GetTexture("_MainTex");
                        if (source.HasProperty("_BaseColor")) color = source.GetColor("_BaseColor");
                        else if (source.HasProperty("_Color")) color = source.GetColor("_Color");
                    }

                    if (forcedTexture != null) color = Color.white;
                    replacements[i] = CreateMaterial($"Review_{root.name}_{i}", color, texture, 0.12f);
                }
                renderer.sharedMaterials = replacements;
            }
        }

        private static Material CreateMaterial(string name, Color color, Texture texture, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is unavailable.");
            Material material = new Material(shader)
            {
                name = name,
                color = color,
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetFloat("_Smoothness", smoothness);
            if (texture != null) material.SetTexture("_BaseMap", texture);
            return material;
        }

        private static void FitAndPlace(
            GameObject instance,
            Vector3 groundPosition,
            float targetSize,
            float yaw,
            bool useHeightForScale)
        {
            FitAndPlace(
                instance,
                groundPosition,
                targetSize,
                new Vector3(0f, yaw, 0f),
                useHeightForScale);
        }

        private static void FitAndPlace(
            GameObject instance,
            Vector3 groundPosition,
            float targetSize,
            Vector3 euler,
            bool useHeightForScale)
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(euler));
            instance.transform.localScale = Vector3.one;
            Bounds bounds = CalculateBounds(instance);
            if (bounds.size.y <= 0.001f)
                throw new InvalidDataException($"Candidate has no usable renderer bounds: {instance.name}");
            float sourceSize = useHeightForScale
                ? bounds.size.y
                : Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            instance.transform.localScale = Vector3.one * (targetSize / sourceSize);
            bounds = CalculateBounds(instance);
            instance.transform.position = groundPosition + new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void ReportModel(GameObject instance, Candidate candidate)
        {
            Bounds bounds = CalculateBounds(instance);
            int rendererCount = instance.GetComponentsInChildren<Renderer>(true).Length;
            int materialSlots = 0;
            int vertices = 0;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                materialSlots += renderer.sharedMaterials.Length;
                Mesh mesh = renderer is SkinnedMeshRenderer skinned
                    ? skinned.sharedMesh
                    : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null) vertices += mesh.vertexCount;
            }

            Animator animator = instance.GetComponentInChildren<Animator>(true);
            string materialNames = string.Join(",", instance.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .Select(material => material.name)
                .Distinct());
            Debug.Log(
                $"EMBERFALL_M6_CANDIDATE label={candidate.Label} path={candidate.AssetPath} " +
                $"size={bounds.size:F3} renderers={rendererCount} materialSlots={materialSlots} vertices={vertices} " +
                $"humanoid={(animator != null && animator.isHuman)} materials=[{materialNames}]");
        }

        private static void CaptureScene(
            Scene scene,
            string outputPath,
            Vector3 cameraPosition,
            Vector3 lookAt,
            float fieldOfView,
            bool closeScene = true)
        {
            GameObject cameraObject = new GameObject("Review Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.047f, 0.06f);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.04f;
            camera.farClipPlane = 120f;
            camera.cullingMask = 1 << ReviewLayer;
            camera.transform.SetPositionAndRotation(
                cameraPosition,
                Quaternion.LookRotation(lookAt - cameraPosition, Vector3.up));

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
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
                if (closeScene) CloseReviewScene(scene);
            }
        }

        private static void CloseReviewScene(Scene scene)
        {
            Scene fallback = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene candidate = SceneManager.GetSceneAt(i);
                if (candidate.IsValid() && candidate != scene)
                {
                    fallback = candidate;
                    break;
                }
            }
            if (fallback.IsValid()) SceneManager.SetActiveScene(fallback);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }

        private sealed class Candidate
        {
            private Candidate(string label, string assetPath, string texturePath, bool useHeightForScale)
            {
                Label = label;
                AssetPath = assetPath;
                TexturePath = texturePath;
                UseHeightForScale = useHeightForScale;
            }

            public string Label { get; }
            public string AssetPath { get; }
            public string TexturePath { get; }
            public bool UseHeightForScale { get; }

            public static Candidate Character(string label, string assetPath, string texturePath)
            {
                return new Candidate(label, assetPath, texturePath, true);
            }

            public static Candidate Prop(string label, string assetPath)
            {
                return new Candidate(label, assetPath, null, false);
            }
        }
    }
}
