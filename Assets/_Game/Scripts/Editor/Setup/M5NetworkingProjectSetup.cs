using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Diagnostics;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Interaction;
using Emberfall.Gameplay.Movement;
using Emberfall.Gameplay.Targeting;
using Emberfall.Infrastructure.Build;
using Emberfall.Networking;
using Emberfall.UI;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    public static class M5NetworkingProjectSetup
    {
        public const string Version = "0.8.10";
        public const string ReleaseLabel = "0.8.10b";
        private const string ScenePath = "Assets/_Game/Scenes/91_NetworkGym.unity";
        private const string EmberValleyScenePath = "Assets/_Game/Scenes/10_EmberValley.unity";
        private const string SanctumScenePath = "Assets/_Game/Scenes/20_Sanctum.unity";
        private const string PlayerPrefabPath = "Assets/_Game/Resources/Networking/P_M5_NetworkGymPlayer.prefab";
        private const string EnemyPrefabPath = "Assets/_Game/Resources/Networking/P_M5_NetworkGymEnemy.prefab";
        private const string RangedEnemyPrefabPath = "Assets/_Game/Resources/Networking/P_M5_NetworkRunePriest.prefab";
        private const string ShieldEnemyPrefabPath = "Assets/_Game/Resources/Networking/P_M5_NetworkRuinGuard.prefab";
        private const string WardenPrefabPath = "Assets/_Game/Resources/Networking/P_M5_NetworkWarden.prefab";
        private const string WorldObjectivePrefabPath = "Assets/_Game/Resources/Networking/P_M5_NetworkGymWorldObjective.prefab";
        private const string PlayerVisualPath = "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab";
        private const string EnemyVisualPath = "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_FogwalkerSkeleton.prefab";
        private const string RangedEnemyVisualPath = "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_RunePriest.prefab";
        private const string ShieldEnemyVisualPath =
            "Assets/_Game/Prefabs/Characters/M5c/P_M5c_Enemy_RuinGuardScorched_Knight.prefab";
        private const string WardenVisualPath = "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Boss_EmberWarden.prefab";
        private const string EnemyDataPath = "Assets/_Game/Data/M2/enemies.v1.json";
        private const string QuestDataPath = "Assets/_Game/Data/M2/quests.v1.json";
        private const string CombatTuningPath = "Assets/_Game/Settings/CombatTuning_M1.asset";
        private const string AnimationSetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        private const string ThrowingKnifeVisualPath =
            "Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Player_ThrowingKnife.prefab";
        private const string InputAssetPath = "Assets/_Game/Settings/PlayerControls.asset";
        private const string MaterialFolder = "Assets/_Game/Art/Materials/M5NetworkGym";
        private const string MeshFolder = "Assets/_Game/Art/Meshes/M5NetworkGym";
        private const string PlayerAttackSectorPath = MeshFolder + "/M_PlayerAttackSector.asset";
        private const string SharedRewardMeshPath = MeshFolder + "/M_SharedEmberShard.asset";
        private const string ObjectiveRingMeshPath = MeshFolder + "/M_NetworkObjectiveRing.asset";
        private const string ObjectiveIconMeshPath = MeshFolder + "/M_NetworkObjectiveDiamond.asset";
        private const float StandardVisualGroundOffset = 1.05f;
        private const float WardenVisualGroundOffset = 1.08f;

        [MenuItem("Emberfall/Setup/Apply M5 Network Gym")]
        public static void Apply()
        {
            EnsureDirectories();
            GameObject playerPrefab = CreatePlayerPrefab();
            GameObject enemyPrefab = CreateEnemyPrefab(
                EnemyPrefabPath, EnemyVisualPath, NetworkEnemyArchetype.Fogwalker, "enemy:fogwalker", null);
            GameObject rangedEnemyPrefab = CreateEnemyPrefab(
                RangedEnemyPrefabPath, RangedEnemyVisualPath, NetworkEnemyArchetype.RunePriest, "enemy:rune-priest", null);
            GameObject shieldEnemyPrefab = CreateEnemyPrefab(
                ShieldEnemyPrefabPath, ShieldEnemyVisualPath, NetworkEnemyArchetype.RuinGuard,
                "enemy:ruin-guard-scorched", null);
            GameObject wardenPrefab = CreateWardenPrefab();
            GameObject worldObjectivePrefab = CreateWorldObjectivePrefab();
            CreateNetworkGymScene(playerPrefab, enemyPrefab, worldObjectivePrefab);
            CreateSanctumScene();
            ConfigureEmberValleyNetworkSlice(
                playerPrefab, enemyPrefab, rangedEnemyPrefab, shieldEnemyPrefab, wardenPrefab, worldObjectivePrefab);
            ConfigureBuildSettings();
            PlayerSettings.bundleVersion = Version;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                $"EMBERFALL_M5_NETWORK_GYM_SETUP_COMPLETE version={Version} " +
                "scope=network-gym+ember-valley-forest-world");
        }

        [MenuItem("Emberfall/Build/Build M5 Network Matrix Development Player")]
        public static void BuildNetworkMatrixDevelopmentPlayer()
        {
            Apply();
            string outputPath = $"Builds/Windows/{ReleaseLabel}-Development/ProjectEmberfall.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            var enabledScenes = new List<string>();
            EditorBuildSettingsScene[] configuredScenes = EditorBuildSettings.scenes;
            for (int i = 0; i < configuredScenes.Length; i++)
            {
                if (configuredScenes[i].enabled) enabledScenes.Add(configuredScenes[i].path);
            }

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = enabledScenes.ToArray(),
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.InvalidOperationException($"M5 matrix Development Build failed: {report.summary.result}");

            LogBuildWarningDetails(report);
            Debug.Log(
                $"EMBERFALL_M5_MATRIX_DEVELOPMENT_BUILD_COMPLETE version={ReleaseLabel} " +
                $"bytes={report.summary.totalSize} warnings={report.summary.totalWarnings}");
        }

        [MenuItem("Emberfall/Build/Build M5 Network Release Player")]
        public static void BuildNetworkReleasePlayer()
        {
            Apply();
            string outputPath = $"Builds/Windows/{ReleaseLabel}/ProjectEmberfall.exe";
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            var enabledScenes = new List<string>();
            EditorBuildSettingsScene[] configuredScenes = EditorBuildSettings.scenes;
            for (int i = 0; i < configuredScenes.Length; i++)
            {
                if (configuredScenes[i].enabled) enabledScenes.Add(configuredScenes[i].path);
            }

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = enabledScenes.ToArray(),
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.InvalidOperationException($"M5 Release Build failed: {report.summary.result}");

            LogBuildWarningDetails(report);
            string outputDirectory = Path.GetDirectoryName(outputPath);
            BuildArtifactCleanupResult cleanup = BuildArtifactCleaner.RemoveDoNotShipDirectories(outputDirectory);
            long executableBytes = new FileInfo(outputPath).Length;

            Debug.Log(
                $"EMBERFALL_M5_RELEASE_BUILD_COMPLETE version={ReleaseLabel} " +
                $"exeBytes={executableBytes} directoryBeforeCleanupBytes={cleanup.BytesBefore} " +
                $"deliverableBytes={cleanup.BytesAfter} doNotShipDirectoriesRemoved={cleanup.RemovedDirectories} " +
                $"warnings={report.summary.totalWarnings}");
        }

        private static void LogBuildWarningDetails(BuildReport report)
        {
            int detailedWarnings = 0;
            foreach (BuildStep step in report.steps)
            {
                foreach (BuildStepMessage message in step.messages)
                {
                    if (message.type != LogType.Warning) continue;
                    detailedWarnings++;
                    Debug.LogWarning(
                        $"[EMBERFALL_BUILD_WARNING] step={step.name} content={message.content}");
                }
            }

            Debug.Log(
                $"[EMBERFALL_BUILD_WARNING_DETAILS] summary={report.summary.totalWarnings} " +
                $"detailed={detailedWarnings}");
        }

        private static void EnsureDirectories()
        {
            Directory.CreateDirectory("Assets/_Game/Resources/Networking");
            Directory.CreateDirectory(MaterialFolder);
            Directory.CreateDirectory(MeshFolder);
        }

        private static GameObject CreatePlayerPrefab()
        {
            InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (inputActions == null) throw new FileNotFoundException("Player input asset is missing.", InputAssetPath);

            var root = new GameObject("P_M5_NetworkGymPlayer");
            root.SetActive(false);
            root.AddComponent<NetworkObject>();
            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 1.9f;
            controller.radius = 0.38f;
            controller.center = new Vector3(0f, 0.95f, 0f);
            controller.stepOffset = 0.25f;

            PlayerInputReader input = root.AddComponent<PlayerInputReader>();
            input.Configure(inputActions);
            input.enabled = false;

            GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerVisualPath);
            if (visualPrefab != null)
            {
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, root.transform);
                visual.name = "Ranger_Visual";
                visual.transform.localPosition = new Vector3(0f, StandardVisualGroundOffset, 0f);
                visual.transform.localRotation = Quaternion.identity;
            }
            else
            {
                GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fallback.name = "Fallback_Visual";
                fallback.transform.SetParent(root.transform, false);
                fallback.transform.localPosition = Vector3.up;
                Object.DestroyImmediate(fallback.GetComponent<Collider>());
            }

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Player_NetworkMarker";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.035f, 0f);
            marker.transform.localScale = new Vector3(0.62f, 0.025f, 0.62f);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            Renderer markerRenderer = marker.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkMarker.mat",
                new Color(0.2f, 0.85f, 1f),
                true);

            GameObject attackPulse = new GameObject(
                "Player_AttackWindow", typeof(MeshFilter), typeof(MeshRenderer));
            attackPulse.transform.SetParent(root.transform, false);
            attackPulse.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            attackPulse.transform.localScale = Vector3.one;
            attackPulse.GetComponent<MeshFilter>().sharedMesh = EnsureSectorMesh(
                PlayerAttackSectorPath, 2.45f, 170f, 24);
            Renderer attackRenderer = attackPulse.GetComponent<Renderer>();
            attackRenderer.sharedMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkAttack.mat",
                new Color(1f, 0.68f, 0.12f),
                true);
            attackRenderer.enabled = false;

            GameObject defensePulse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            defensePulse.name = "Player_DefenseWindow";
            defensePulse.transform.SetParent(root.transform, false);
            defensePulse.transform.localPosition = new Vector3(0f, 0.065f, 0f);
            defensePulse.transform.localScale = new Vector3(0.82f, 0.018f, 0.82f);
            Object.DestroyImmediate(defensePulse.GetComponent<Collider>());
            Renderer defenseRenderer = defensePulse.GetComponent<Renderer>();
            defenseRenderer.sharedMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkDefense.mat",
                new Color(0.1f, 0.85f, 1f),
                true);
            defenseRenderer.enabled = false;

            GameObject downedBeacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            downedBeacon.name = "Player_DownedRescueBeacon";
            downedBeacon.transform.SetParent(root.transform, false);
            downedBeacon.transform.localPosition = new Vector3(0f, 0.075f, 0f);
            downedBeacon.transform.localScale = new Vector3(0.88f, 0.025f, 0.88f);
            Object.DestroyImmediate(downedBeacon.GetComponent<Collider>());
            Renderer downedRenderer = downedBeacon.GetComponent<Renderer>();
            downedRenderer.sharedMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkDowned.mat",
                new Color(1f, 0.12f, 0.08f),
                true);
            downedRenderer.enabled = false;

            CombatTuningAsset combatTuning = AssetDatabase.LoadAssetAtPath<CombatTuningAsset>(CombatTuningPath);
            if (combatTuning == null) throw new FileNotFoundException("Combat tuning asset is missing.", CombatTuningPath);
            PlayerAnimationSet animationSet = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(AnimationSetPath);
            if (animationSet == null) throw new FileNotFoundException("Player animation set is missing.", AnimationSetPath);
            GameObject throwingKnifeVisual = AssetDatabase.LoadAssetAtPath<GameObject>(ThrowingKnifeVisualPath);
            if (throwingKnifeVisual == null)
                throw new FileNotFoundException("Throwing knife visual is missing.", ThrowingKnifeVisualPath);
            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null) throw new MissingComponentException("Network player visual has no Animator.");

            var ownerCameraObject = new GameObject(
                "NetworkOwnerCamera", typeof(Camera), typeof(AudioListener), typeof(ThirdPersonCameraRig));
            ownerCameraObject.tag = "MainCamera";
            ownerCameraObject.transform.SetParent(root.transform, false);
            ownerCameraObject.transform.localPosition = new Vector3(0f, 2.8f, -5.8f);
            ownerCameraObject.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
            ThirdPersonCameraRig ownerCameraRig = ownerCameraObject.GetComponent<ThirdPersonCameraRig>();
            LockOnTargeting targeting = root.AddComponent<LockOnTargeting>();
            targeting.Configure(input, ownerCameraObject.transform);
            ownerCameraRig.Configure(root.transform, input, targeting);
            ownerCameraObject.SetActive(false);

            NetworkGymPlayer player = root.AddComponent<NetworkGymPlayer>();
            player.Configure(
                input,
                controller,
                new[] { markerRenderer },
                attackRenderer,
                defenseRenderer,
                downedRenderer,
                combatTuning,
                animator,
                animationSet,
                throwingKnifeVisual,
                ownerCameraRig,
                targeting);
            NetworkRouteHud hud = root.AddComponent<NetworkRouteHud>();
            hud.Configure(player, input, targeting);
            root.SetActive(true);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);
            if (prefab == null) throw new System.InvalidOperationException("Failed to create Network Gym player prefab.");
            return prefab;
        }

        private static GameObject CreateEnemyPrefab(
            string prefabPath,
            string visualPath,
            NetworkEnemyArchetype archetype,
            string enemyId,
            string equipmentPath)
        {
            TextAsset enemyDefinitions = AssetDatabase.LoadAssetAtPath<TextAsset>(EnemyDataPath);
            if (enemyDefinitions == null) throw new FileNotFoundException("Enemy definitions are missing.", EnemyDataPath);

            var root = new GameObject($"P_M5_Network{archetype}");
            root.SetActive(false);
            root.AddComponent<NetworkObject>();
            CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
            collider.height = 1.9f;
            collider.radius = 0.42f;
            collider.center = new Vector3(0f, 0.95f, 0f);

            GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(visualPath);
            if (visualPrefab != null)
            {
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, root.transform);
                visual.name = $"{archetype}_Visual";
                visual.transform.localPosition = new Vector3(0f, StandardVisualGroundOffset, 0f);
                visual.transform.localRotation = Quaternion.identity;
            }
            else
            {
                GameObject fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fallback.name = "Fallback_EnemyVisual";
                fallback.transform.SetParent(root.transform, false);
                fallback.transform.localPosition = Vector3.up;
                Object.DestroyImmediate(fallback.GetComponent<Collider>());
            }

            if (!string.IsNullOrWhiteSpace(equipmentPath))
            {
                GameObject equipmentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(equipmentPath);
                if (equipmentPrefab != null)
                {
                    Animator visualAnimator = root.GetComponentInChildren<Animator>(true);
                    Transform equipmentParent = root.transform;
                    if (visualAnimator != null)
                    {
                        Transform hand = visualAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                        if (hand != null)
                        {
                            equipmentParent = M1ProjectSetup.CreateWorldScaleSocket(
                                hand,
                                "ShieldSocket_LeftHand",
                                Vector3.zero,
                                Quaternion.identity);
                        }
                    }
                    GameObject equipment = (GameObject)PrefabUtility.InstantiatePrefab(equipmentPrefab, equipmentParent);
                    equipment.name = $"{archetype}_Equipment";
                    equipment.transform.localPosition = equipmentParent == root.transform
                        ? new Vector3(-0.42f, 0.92f, 0.12f)
                        : Vector3.zero;
                    equipment.transform.localRotation = equipmentParent == root.transform
                        ? Quaternion.Euler(0f, 0f, 12f)
                        : Quaternion.identity;
                }
            }
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Enemy_StateMarker";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            marker.transform.localScale = new Vector3(0.68f, 0.025f, 0.68f);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            Renderer markerRenderer = marker.GetComponent<Renderer>();
            markerRenderer.sharedMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkEnemy.mat",
                new Color(0.72f, 0.08f, 0.12f),
                true);

            GameObject attackPulse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            attackPulse.name = "Enemy_AttackWindow";
            attackPulse.transform.SetParent(root.transform, false);
            attackPulse.transform.localPosition = new Vector3(0f, 0.055f, 0.85f);
            attackPulse.transform.localScale = new Vector3(1.45f, 0.018f, 1.45f);
            Object.DestroyImmediate(attackPulse.GetComponent<Collider>());
            Renderer attackRenderer = attackPulse.GetComponent<Renderer>();
            attackRenderer.sharedMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkEnemyAttack.mat",
                new Color(1f, 0.08f, 0.02f),
                true);
            attackRenderer.enabled = false;

            NetworkGymEnemy enemy = root.AddComponent<NetworkGymEnemy>();
            PlayerAnimationSet animationSet = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(AnimationSetPath);
            Animator animator = root.GetComponentInChildren<Animator>(true);
            enemy.Configure(enemyDefinitions, archetype, enemyId, markerRenderer, attackRenderer, animator, animationSet);
            Transform aimPoint = new GameObject("NetworkTarget_AimPoint").transform;
            aimPoint.SetParent(root.transform, false);
            aimPoint.localPosition = new Vector3(0f, 1.48f, 0f);
            NetworkCombatTargetProxy targetProxy = root.AddComponent<NetworkCombatTargetProxy>();
            targetProxy.Configure(enemy, aimPoint, ResolveEnemyDisplayName(archetype));
            root.SetActive(true);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            if (prefab == null) throw new System.InvalidOperationException("Failed to create Network Gym enemy prefab.");
            return prefab;
        }

        private static GameObject CreateWardenPrefab()
        {
            TextAsset definitions = AssetDatabase.LoadAssetAtPath<TextAsset>(EnemyDataPath);
            GameObject visualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WardenVisualPath);
            if (definitions == null) throw new FileNotFoundException("Enemy definitions are missing.", EnemyDataPath);
            if (visualPrefab == null) throw new FileNotFoundException("Warden visual is missing.", WardenVisualPath);

            var root = new GameObject("P_M5_NetworkWarden");
            root.SetActive(false);
            root.AddComponent<NetworkObject>();
            CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
            collider.height = 2.25f;
            collider.radius = 0.52f;
            collider.center = new Vector3(0f, 1.12f, 0f);

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(visualPrefab, root.transform);
            visual.name = "Warden_Visual";
            visual.transform.localPosition = new Vector3(0f, WardenVisualGroundOffset, 0f);
            visual.transform.localRotation = Quaternion.identity;

            GameObject stateMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stateMarker.name = "Warden_StateMarker";
            stateMarker.transform.SetParent(root.transform, false);
            stateMarker.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            stateMarker.transform.localScale = new Vector3(1.05f, 0.025f, 1.05f);
            Object.DestroyImmediate(stateMarker.GetComponent<Collider>());
            Renderer stateRenderer = stateMarker.GetComponent<Renderer>();
            stateRenderer.sharedMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkWarden.mat", new Color(0.9f, 0.34f, 0.06f), true);

            GameObject attack = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            attack.name = "Warden_AttackTelegraph";
            attack.transform.SetParent(root.transform, false);
            attack.transform.localPosition = new Vector3(0f, 0.055f, 1.5f);
            attack.transform.localScale = new Vector3(2.5f, 0.018f, 2.5f);
            Object.DestroyImmediate(attack.GetComponent<Collider>());
            Renderer attackRenderer = attack.GetComponent<Renderer>();
            attackRenderer.sharedMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkWardenAttack.mat", new Color(1f, 0.12f, 0.04f), true);
            attackRenderer.enabled = false;

            GameObject phase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            phase.name = "Warden_PhaseIndicator";
            phase.transform.SetParent(root.transform, false);
            phase.transform.localPosition = new Vector3(0f, 0.065f, 0f);
            phase.transform.localScale = new Vector3(3.2f, 0.012f, 3.2f);
            Object.DestroyImmediate(phase.GetComponent<Collider>());
            Renderer phaseRenderer = phase.GetComponent<Renderer>();
            phaseRenderer.sharedMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkWardenPhase.mat", new Color(0.62f, 0.08f, 1f), true);
            phaseRenderer.enabled = false;

            PlayerAnimationSet animationSet = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(AnimationSetPath);
            Animator animator = root.GetComponentInChildren<Animator>(true);
            NetworkWarden warden = root.AddComponent<NetworkWarden>();
            warden.Configure(
                definitions,
                "boss:ember-warden",
                stateRenderer,
                attackRenderer,
                phaseRenderer,
                animator,
                animationSet);
            Transform aimPoint = new GameObject("NetworkTarget_AimPoint").transform;
            aimPoint.SetParent(root.transform, false);
            aimPoint.localPosition = new Vector3(0f, 1.7f, 0f);
            NetworkCombatTargetProxy targetProxy = root.AddComponent<NetworkCombatTargetProxy>();
            targetProxy.Configure(warden, aimPoint, "余烬守望者");

            root.SetActive(true);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, WardenPrefabPath);
            Object.DestroyImmediate(root);
            if (prefab == null) throw new System.InvalidOperationException("Failed to create network Warden prefab.");
            return prefab;
        }

        private static GameObject CreateWorldObjectivePrefab()
        {
            TextAsset questDefinitions = AssetDatabase.LoadAssetAtPath<TextAsset>(QuestDataPath);
            if (questDefinitions == null) throw new FileNotFoundException("Quest definitions are missing.", QuestDataPath);

            Material sealMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkWorldSeal.mat", new Color(0.82f, 0.24f, 1f), true);
            Material gateMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkWorldGate.mat", new Color(0.18f, 0.32f, 0.42f), false);
            Material rewardMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkWorldReward.mat", new Color(1f, 0.62f, 0.08f), true);

            var root = new GameObject("P_M5_NetworkGymWorldObjective");
            root.SetActive(false);
            root.AddComponent<NetworkObject>();

            GameObject sealAnchorObject = new GameObject("Seal_InteractionAnchor");
            sealAnchorObject.transform.SetParent(root.transform, false);
            sealAnchorObject.transform.localPosition = new Vector3(-4f, 0f, -1.5f);

            GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "SharedSeal_Pedestal";
            pedestal.transform.SetParent(root.transform, false);
            pedestal.transform.localPosition = new Vector3(-4f, 0.28f, -1.5f);
            pedestal.transform.localScale = new Vector3(1.05f, 0.28f, 1.05f);
            Object.DestroyImmediate(pedestal.GetComponent<Collider>());
            pedestal.GetComponent<Renderer>().sharedMaterial = gateMaterial;

            GameObject sealMarker = new GameObject(
                "SharedSeal_Highlight",
                typeof(MeshFilter),
                typeof(MeshRenderer));
            sealMarker.name = "SharedSeal_Highlight";
            sealMarker.transform.SetParent(root.transform, false);
            sealMarker.transform.localPosition = new Vector3(-4f, 0.055f, -1.5f);
            sealMarker.GetComponent<MeshFilter>().sharedMesh = EnsureObjectiveRingMesh(ObjectiveRingMeshPath);
            Renderer sealMarkerRenderer = sealMarker.GetComponent<Renderer>();
            sealMarkerRenderer.sharedMaterial = sealMaterial;

            GameObject sealIcon = new GameObject(
                "SharedSeal_HighlightIcon",
                typeof(MeshFilter),
                typeof(MeshRenderer));
            sealIcon.transform.SetParent(sealMarker.transform, false);
            sealIcon.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            sealIcon.transform.localScale = Vector3.one * 0.42f;
            sealIcon.GetComponent<MeshFilter>().sharedMesh = EnsureObjectiveIconMesh(ObjectiveIconMeshPath);
            sealIcon.GetComponent<MeshRenderer>().sharedMaterial = sealMaterial;

            GameObject sealCore = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sealCore.name = "SharedSeal_Core";
            sealCore.transform.SetParent(root.transform, false);
            sealCore.transform.localPosition = new Vector3(-4f, 1.15f, -1.5f);
            sealCore.transform.localScale = Vector3.one * 0.62f;
            Object.DestroyImmediate(sealCore.GetComponent<Collider>());
            sealCore.GetComponent<Renderer>().sharedMaterial = sealMaterial;

            GameObject gateAnchorObject = new GameObject("Gate_InteractionAnchor");
            gateAnchorObject.transform.SetParent(root.transform, false);
            gateAnchorObject.transform.localPosition = new Vector3(0f, 0f, 1.25f);

            GameObject gate = CreateBlock(
                "SharedGate_Blocker", new Vector3(0f, 1.2f, 2.25f), new Vector3(5.2f, 2.4f, 0.42f), gateMaterial);
            gate.transform.SetParent(root.transform, true);

            GameObject rewardAnchorObject = new GameObject("Reward_InteractionAnchor");
            rewardAnchorObject.transform.SetParent(root.transform, false);
            rewardAnchorObject.transform.localPosition = new Vector3(0f, 0f, 4.1f);

            GameObject reward = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            reward.name = "UniqueReward_Ember";
            reward.transform.SetParent(root.transform, false);
            reward.transform.localPosition = new Vector3(0f, 0.82f, 4.1f);
            reward.transform.localScale = Vector3.one * 0.72f;
            Object.DestroyImmediate(reward.GetComponent<Collider>());
            reward.GetComponent<Renderer>().sharedMaterial = rewardMaterial;
            reward.SetActive(false);

            NetworkGymWorldObjective objective = root.AddComponent<NetworkGymWorldObjective>();
            objective.Configure(
                questDefinitions,
                sealAnchorObject.transform,
                gateAnchorObject.transform,
                rewardAnchorObject.transform,
                sealCore,
                gate,
                reward,
                sealMarkerRenderer);

            root.SetActive(true);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, WorldObjectivePrefabPath);
            Object.DestroyImmediate(root);
            if (prefab == null)
                throw new System.InvalidOperationException("Failed to create Network Gym world objective prefab.");
            return prefab;
        }

        private static void CreateNetworkGymScene(
            GameObject playerPrefab,
            GameObject enemyPrefab,
            GameObject worldObjectivePrefab)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "91_NetworkGym";

            Material floorMaterial = EnsureMaterial(MaterialFolder + "/M_NetworkFloor.mat", new Color(0.055f, 0.08f, 0.105f), false);
            Material wallMaterial = EnsureMaterial(MaterialFolder + "/M_NetworkWall.mat", new Color(0.12f, 0.18f, 0.22f), false);
            Material accentMaterial = EnsureMaterial(MaterialFolder + "/M_NetworkAccent.mat", new Color(1f, 0.42f, 0.1f), true);

            CreateBlock("Arena_Floor", new Vector3(0f, -0.25f, 0f), new Vector3(19f, 0.5f, 12f), floorMaterial);
            CreateBlock("Wall_North", new Vector3(0f, 1f, 6f), new Vector3(19f, 2.5f, 0.5f), wallMaterial);
            CreateBlock("Wall_South", new Vector3(0f, 1f, -6f), new Vector3(19f, 2.5f, 0.5f), wallMaterial);
            CreateBlock("Wall_East", new Vector3(9.5f, 1f, 0f), new Vector3(0.5f, 2.5f, 12f), wallMaterial);
            CreateBlock("Wall_West", new Vector3(-9.5f, 1f, 0f), new Vector3(0.5f, 2.5f, 12f), wallMaterial);
            CreateBlock("Sync_Obelisk", new Vector3(7.4f, 1.5f, 2.2f), new Vector3(1.2f, 3f, 1.2f), accentMaterial);
            CreateBlock("Cover_Left", new Vector3(-5.2f, 0.75f, -1.8f), new Vector3(1.4f, 1.5f, 2.2f), wallMaterial);
            CreateBlock("Cover_Right", new Vector3(5.2f, 0.75f, -1.8f), new Vector3(1.4f, 1.5f, 2.2f), wallMaterial);

            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.72f, 0.83f, 1f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            var controllerObject = new GameObject("NetworkGym_Controller");
            NetworkGymSceneController gym = controllerObject.AddComponent<NetworkGymSceneController>();
            gym.Configure(playerPrefab, enemyPrefab, worldObjectivePrefab, new[]
            {
                new Vector3(-1.1f, 0f, -2f),
                new Vector3(1.1f, 0f, -2f)
            }, new Vector3(0f, 0f, -0.2f));

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.18f, 0.24f, 0.3f);
            RenderSettings.ambientEquatorColor = new Color(0.08f, 0.11f, 0.14f);
            RenderSettings.ambientGroundColor = new Color(0.025f, 0.03f, 0.04f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.025f, 0.04f, 0.055f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 12f;
            RenderSettings.fogEndDistance = 32f;

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void CreateSanctumScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "20_Sanctum";
            Vector3 center = new Vector3(1000f, -80f, 1000f);
            Material floor = EnsureMaterial(
                MaterialFolder + "/M_SanctumFloor.mat", new Color(0.055f, 0.07f, 0.08f), false);
            Material wall = EnsureMaterial(
                MaterialFolder + "/M_SanctumWall.mat", new Color(0.12f, 0.14f, 0.17f), false);
            Material rune = EnsureMaterial(
                MaterialFolder + "/M_SanctumRune.mat", new Color(0.68f, 0.1f, 1f), true);

            CreateBlock("Sanctum_ArenaFloor", center + Vector3.down * 0.25f, new Vector3(22f, 0.5f, 22f), floor);
            CreateBlock("Sanctum_WallNorth", center + new Vector3(0f, 2f, 11f), new Vector3(22f, 4f, 0.6f), wall);
            CreateBlock("Sanctum_WallSouth", center + new Vector3(0f, 2f, -11f), new Vector3(22f, 4f, 0.6f), wall);
            CreateBlock("Sanctum_WallEast", center + new Vector3(11f, 2f, 0f), new Vector3(0.6f, 4f, 22f), wall);
            CreateBlock("Sanctum_WallWest", center + new Vector3(-11f, 2f, 0f), new Vector3(0.6f, 4f, 22f), wall);
            CreateBlock("Sanctum_EntranceSeal", center + new Vector3(0f, 1.6f, -9.7f), new Vector3(5f, 3.2f, 0.3f), rune);
            CreateBlock("Sanctum_PillarWest", center + new Vector3(-6.5f, 1.8f, 2f), new Vector3(1.2f, 3.6f, 1.2f), wall);
            CreateBlock("Sanctum_PillarEast", center + new Vector3(6.5f, 1.8f, 2f), new Vector3(1.2f, 3.6f, 1.2f), wall);

            Transform anchors = new GameObject("[Networking] Sanctum Anchors").transform;
            Transform spawnA = new GameObject("NetworkSanctum_PlayerSpawn_A").transform;
            spawnA.SetParent(anchors, false);
            spawnA.position = center + new Vector3(-1.2f, 0f, -6.4f);
            Transform spawnB = new GameObject("NetworkSanctum_PlayerSpawn_B").transform;
            spawnB.SetParent(anchors, false);
            spawnB.position = center + new Vector3(1.2f, 0f, -6.4f);
            Transform warden = new GameObject("NetworkSanctum_WardenSpawn").transform;
            warden.SetParent(anchors, false);
            warden.position = center + new Vector3(0f, 0f, 3.6f);
            warden.rotation = Quaternion.Euler(0f, 180f, 0f);

            var lightObject = new GameObject("Sanctum Fill Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 30f;
            light.intensity = 2.2f;
            light.color = new Color(0.52f, 0.2f, 0.85f);
            lightObject.transform.position = center + new Vector3(0f, 7f, 0f);

            EditorSceneManager.SaveScene(scene, SanctumScenePath);
        }

        private static void ConfigureEmberValleyNetworkSlice(
            GameObject playerPrefab,
            GameObject enemyPrefab,
            GameObject rangedEnemyPrefab,
            GameObject shieldEnemyPrefab,
            GameObject wardenPrefab,
            GameObject worldObjectivePrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(EmberValleyScenePath, OpenSceneMode.Single);
            Transform networkingRoot = FindTransform("[Networking] M5 Ember Valley Slice");
            if (networkingRoot == null)
                networkingRoot = new GameObject("[Networking] M5 Ember Valley Slice").transform;

            Transform campSpawn = FindRequiredTransform("CheckpointSpawn_Camp");
            Transform scoutAnchor = FindRequiredTransform("NPC_CampScout");
            Transform sealAnchor = FindRequiredTransform("Seal_Forest");
            Transform gateAnchor = FindRequiredTransform("GateControl_Sanctum");
            Transform fogwalkerAnchor = FindRequiredTransform("Enemy_Fogwalker_Forest");
            Transform runePriestAnchor = FindRequiredTransform("Enemy_RunePriest_Forest");
            Transform ruinGuardAnchor = FindRequiredTransform("Enemy_RuinGuard_Courtyard");
            Transform bridgeAnchor = FindRequiredTransform("Seal_Bridge");
            Transform courtyardAnchor = FindRequiredTransform("Seal_Courtyard");
            Transform courtyardFogwalkerAnchor = FindRequiredTransform("Enemy_Fogwalker_Courtyard_Support");
            Transform courtyardRunePriestAnchor = FindRequiredTransform("Enemy_RunePriest_Courtyard");
            GameObject sealCore = FindRequiredTransform("Seal_Forest_RuneCore").gameObject;
            GameObject gateBlocker = FindRequiredTransform("GateBlocker_Sanctum").gameObject;
            Renderer sealMarker = FindRequiredTransform("Seal_Forest_HighlightRing").GetComponent<Renderer>();
            if (sealMarker == null)
                throw new System.InvalidOperationException("Seal_Forest_HighlightRing requires a Renderer.");
            MeshFilter sealMarkerFilter = sealMarker.GetComponent<MeshFilter>();
            if (sealMarkerFilter == null) sealMarkerFilter = sealMarker.gameObject.AddComponent<MeshFilter>();
            sealMarkerFilter.sharedMesh = EnsureObjectiveRingMesh(ObjectiveRingMeshPath);
            sealMarker.transform.localScale = Vector3.one;
            Collider sealMarkerCollider = sealMarker.GetComponent<Collider>();
            if (sealMarkerCollider != null) Object.DestroyImmediate(sealMarkerCollider);
            Transform iconTransform = sealMarker.transform.Find("Seal_Forest_HighlightIcon");
            if (iconTransform == null)
            {
                iconTransform = new GameObject(
                    "Seal_Forest_HighlightIcon",
                    typeof(MeshFilter),
                    typeof(MeshRenderer)).transform;
                iconTransform.SetParent(sealMarker.transform, false);
            }
            iconTransform.localPosition = new Vector3(0f, 1.7f, 0f);
            iconTransform.localRotation = Quaternion.identity;
            iconTransform.localScale = Vector3.one * 0.38f;
            iconTransform.GetComponent<MeshFilter>().sharedMesh = EnsureObjectiveIconMesh(ObjectiveIconMeshPath);
            iconTransform.GetComponent<MeshRenderer>().sharedMaterial = sealMarker.sharedMaterial;

            Transform networkSealMarkerTransform = networkingRoot.Find("NetworkSeal_Forest_HighlightRing");
            if (networkSealMarkerTransform == null)
            {
                networkSealMarkerTransform = new GameObject(
                    "NetworkSeal_Forest_HighlightRing",
                    typeof(MeshFilter),
                    typeof(MeshRenderer)).transform;
                networkSealMarkerTransform.SetParent(networkingRoot, false);
            }
            networkSealMarkerTransform.position = sealMarker.transform.position;
            networkSealMarkerTransform.rotation = sealMarker.transform.rotation;
            networkSealMarkerTransform.localScale = Vector3.one;
            MeshFilter networkSealMarkerFilter = networkSealMarkerTransform.GetComponent<MeshFilter>();
            networkSealMarkerFilter.sharedMesh = sealMarkerFilter.sharedMesh;
            MeshRenderer networkSealMarker = networkSealMarkerTransform.GetComponent<MeshRenderer>();
            networkSealMarker.sharedMaterial = sealMarker.sharedMaterial;
            Collider networkSealMarkerCollider = networkSealMarkerTransform.GetComponent<Collider>();
            if (networkSealMarkerCollider != null) Object.DestroyImmediate(networkSealMarkerCollider);
            Transform networkIconTransform = networkSealMarkerTransform.Find("Seal_Forest_HighlightIcon");
            if (networkIconTransform == null)
            {
                networkIconTransform = new GameObject(
                    "Seal_Forest_HighlightIcon",
                    typeof(MeshFilter),
                    typeof(MeshRenderer)).transform;
                networkIconTransform.SetParent(networkSealMarkerTransform, false);
            }
            networkIconTransform.localPosition = new Vector3(0f, 1.7f, 0f);
            networkIconTransform.localRotation = Quaternion.identity;
            networkIconTransform.localScale = Vector3.one * 0.38f;
            networkIconTransform.GetComponent<MeshFilter>().sharedMesh = EnsureObjectiveIconMesh(ObjectiveIconMeshPath);
            networkIconTransform.GetComponent<MeshRenderer>().sharedMaterial = sealMarker.sharedMaterial;
            networkSealMarkerTransform.gameObject.SetActive(false);

            Transform rewardAnchor = networkingRoot.Find("ForestReward_InteractionAnchor");
            if (rewardAnchor == null)
            {
                rewardAnchor = new GameObject("ForestReward_InteractionAnchor").transform;
                rewardAnchor.SetParent(networkingRoot, false);
            }
            Vector3 approachDirection = sealAnchor.position - gateAnchor.position;
            approachDirection.y = 0f;
            if (approachDirection.sqrMagnitude <= 0.0001f) approachDirection = -gateAnchor.forward;
            rewardAnchor.position = gateAnchor.position + (approachDirection.normalized * 2.8f);

            Transform rewardTransform = networkingRoot.Find("ForestReward_Ember");
            GameObject rewardVisual;
            if (rewardTransform == null)
            {
                rewardVisual = new GameObject(
                    "ForestReward_Ember",
                    typeof(MeshFilter),
                    typeof(MeshRenderer),
                    typeof(NetworkRewardPresentation));
                rewardVisual.name = "ForestReward_Ember";
                rewardVisual.transform.SetParent(networkingRoot, false);
            }
            else
            {
                rewardVisual = rewardTransform.gameObject;
            }
            Collider legacyRewardCollider = rewardVisual.GetComponent<Collider>();
            if (legacyRewardCollider != null) Object.DestroyImmediate(legacyRewardCollider);
            MeshFilter rewardFilter = rewardVisual.GetComponent<MeshFilter>();
            if (rewardFilter == null) rewardFilter = rewardVisual.AddComponent<MeshFilter>();
            MeshRenderer rewardRenderer = rewardVisual.GetComponent<MeshRenderer>();
            if (rewardRenderer == null) rewardRenderer = rewardVisual.AddComponent<MeshRenderer>();
            if (rewardVisual.GetComponent<NetworkRewardPresentation>() == null)
                rewardVisual.AddComponent<NetworkRewardPresentation>();
            rewardFilter.sharedMesh = EnsureEmberShardMesh(SharedRewardMeshPath);
            rewardVisual.transform.position = rewardAnchor.position + (Vector3.up * 0.9f);
            rewardVisual.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            rewardVisual.transform.localScale = new Vector3(0.72f, 1.05f, 0.72f);
            rewardRenderer.sharedMaterial = EnsureMaterial(
                MaterialFolder + "/M_NetworkWorldReward.mat",
                new Color(1f, 0.62f, 0.08f),
                true);
            rewardVisual.SetActive(false);

            Vector3 minimum = Vector3.Min(
                campSpawn.position,
                Vector3.Min(sealAnchor.position, Vector3.Min(bridgeAnchor.position, gateAnchor.position)));
            Vector3 maximum = Vector3.Max(
                campSpawn.position,
                Vector3.Max(courtyardRunePriestAnchor.position,
                    Vector3.Max(courtyardAnchor.position, gateAnchor.position)));
            Vector2 boundsCenter = new Vector2((minimum.x + maximum.x) * 0.5f, (minimum.z + maximum.z) * 0.5f);
            Vector2 boundsHalfExtents = new Vector2(
                Mathf.Max(12f, (maximum.x - minimum.x) * 0.5f + 10f),
                Mathf.Max(12f, (maximum.z - minimum.z) * 0.5f + 10f));

            NetworkGymSceneController controller = networkingRoot.GetComponent<NetworkGymSceneController>();
            if (controller == null) controller = networkingRoot.gameObject.AddComponent<NetworkGymSceneController>();
            controller.Configure(
                playerPrefab,
                null,
                worldObjectivePrefab,
                new[]
                {
                    campSpawn.position + (Vector3.left * 0.8f),
                    campSpawn.position + (Vector3.right * 0.8f)
                },
                Vector3.zero,
                false,
                true,
                "ember-valley-forest",
                boundsCenter,
                boundsHalfExtents);
            controller.ConfigureSharedWorldSlice(
                AssetDatabase.LoadAssetAtPath<TextAsset>(QuestDataPath),
                sealAnchor,
                gateAnchor,
                rewardAnchor,
                scoutAnchor,
                sealCore,
                gateBlocker,
                rewardVisual,
                networkSealMarker,
                "quest:forest-seal",
                "seal:forest",
                "reward:forest-coop");
            controller.ConfigureSanctum(wardenPrefab, "20_Sanctum");
            controller.ConfigureEnemyRoster(
                enemyPrefab,
                fogwalkerAnchor.position,
                rangedEnemyPrefab,
                runePriestAnchor.position,
                shieldEnemyPrefab,
                ruinGuardAnchor.position);
            controller.ConfigureAuthoredEncounters(new[]
            {
                new NetworkEncounterPlan(
                    "encounter:fog-forest",
                    sealAnchor.position,
                    8f,
                    new[]
                    {
                        new NetworkEnemySpawnPlan(
                            NetworkEnemyArchetype.Fogwalker,
                            fogwalkerAnchor.position + new Vector3(-2.1f, 0f, -0.8f)),
                        new NetworkEnemySpawnPlan(
                            NetworkEnemyArchetype.Fogwalker,
                            fogwalkerAnchor.position + new Vector3(2.2f, 0f, 1.2f)),
                        new NetworkEnemySpawnPlan(NetworkEnemyArchetype.RunePriest, runePriestAnchor.position)
                    }),
                new NetworkEncounterPlan(
                    "encounter:broken-bridge",
                    new Vector3(10f, 1f, 44f), // Offline seal dressing must not move the existing co-op activation zone.
                    7f,
                    new[]
                    {
                        new NetworkEnemySpawnPlan(NetworkEnemyArchetype.RunePriest, new Vector3(8.2f, 0f, 43.2f)),
                        new NetworkEnemySpawnPlan(NetworkEnemyArchetype.RunePriest, new Vector3(14.2f, 0f, 45.8f))
                    }),
                new NetworkEncounterPlan(
                    "encounter:scorched-courtyard",
                    courtyardAnchor.position,
                    8f,
                    new[]
                    {
                        new NetworkEnemySpawnPlan(NetworkEnemyArchetype.RuinGuard, ruinGuardAnchor.position),
                        new NetworkEnemySpawnPlan(
                            NetworkEnemyArchetype.Fogwalker,
                            courtyardFogwalkerAnchor.position),
                        new NetworkEnemySpawnPlan(
                            NetworkEnemyArchetype.RunePriest,
                            courtyardRunePriestAnchor.position)
                    }),
                new NetworkEncounterPlan(
                    "encounter:pre-sanctum",
                    gateAnchor.position,
                    5.5f,
                    new[]
                    {
                        new NetworkEnemySpawnPlan(NetworkEnemyArchetype.Fogwalker, new Vector3(33.8f, 0f, 32f)),
                        new NetworkEnemySpawnPlan(NetworkEnemyArchetype.Fogwalker, new Vector3(39.4f, 0f, 32.4f))
                    })
            });

            var offlineRoots = new HashSet<GameObject>();
            foreach (PlayerCombatActor actor in Object.FindObjectsOfType<PlayerCombatActor>(true))
                offlineRoots.Add(actor.gameObject);
            foreach (MeleeEnemyActor actor in Object.FindObjectsOfType<MeleeEnemyActor>(true))
                offlineRoots.Add(actor.gameObject);
            foreach (RangedEnemyActor actor in Object.FindObjectsOfType<RangedEnemyActor>(true))
                offlineRoots.Add(actor.gameObject);
            foreach (ShieldEnemyActor actor in Object.FindObjectsOfType<ShieldEnemyActor>(true))
                offlineRoots.Add(actor.gameObject);
            foreach (WardenActor actor in Object.FindObjectsOfType<WardenActor>(true))
                offlineRoots.Add(actor.gameObject);
            ThirdPersonCameraRig cameraRig = Object.FindObjectOfType<ThirdPersonCameraRig>(true);
            if (cameraRig != null) offlineRoots.Add(cameraRig.gameObject);

            var offlineBehaviours = new List<Behaviour>();
            AddIfPresent(offlineBehaviours, Object.FindObjectOfType<M2RouteFlowController>(true));
            AddIfPresent(offlineBehaviours, Object.FindObjectOfType<ForestSealTemplateCoordinator>(true));
            AddIfPresent(offlineBehaviours, Object.FindObjectOfType<M2RouteHud>(true));
            AddIfPresent(offlineBehaviours, Object.FindObjectOfType<InputTelemetryOverlay>(true));
            offlineBehaviours.AddRange(Object.FindObjectsOfType<M2RouteInteractable>(true));
            offlineBehaviours.AddRange(Object.FindObjectsOfType<RouteEnrichmentInteractable>(true));
            offlineBehaviours.AddRange(Object.FindObjectsOfType<BridgeMechanismInteractable>(true));
            offlineBehaviours.AddRange(Object.FindObjectsOfType<BridgeMechanismGuidancePresenter>(true));
            offlineBehaviours.AddRange(Object.FindObjectsOfType<RouteChoiceEncounterModifier>(true));
            offlineBehaviours.AddRange(Object.FindObjectsOfType<CombatEncounterCoordinator>(true));
            offlineBehaviours.AddRange(Object.FindObjectsOfType<EncounterLeash>(true));
            AddIfPresent(offlineBehaviours, Object.FindObjectOfType<TacticalPostureShrine>(true));
            for (int i = 0; i < offlineBehaviours.Count; i++)
            {
                Behaviour behaviour = offlineBehaviours[i];
                if (behaviour != null && behaviour.gameObject != networkingRoot.gameObject)
                    offlineRoots.Add(behaviour.gameObject);
            }

            NetworkEmberValleyModeAdapter modeAdapter =
                networkingRoot.GetComponent<NetworkEmberValleyModeAdapter>();
            if (modeAdapter == null)
                modeAdapter = networkingRoot.gameObject.AddComponent<NetworkEmberValleyModeAdapter>();
            modeAdapter.Configure(offlineRoots.ToArray(), offlineBehaviours.Distinct().ToArray());

            ApplyEmberValleyReadabilityLighting();

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(modeAdapter);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, EmberValleyScenePath);
        }

        private static void AddIfPresent(List<Behaviour> behaviours, Behaviour candidate)
        {
            if (candidate != null) behaviours.Add(candidate);
        }

        private static Transform FindRequiredTransform(string objectName)
        {
            Transform transform = FindTransform(objectName);
            if (transform == null)
                throw new System.InvalidOperationException($"Required Ember Valley object is missing: {objectName}");
            return transform;
        }

        private static Transform FindTransform(string objectName)
        {
            return Object.FindObjectsOfType<Transform>(true)
                .FirstOrDefault(candidate => candidate.name == objectName);
        }

        private static void ConfigureBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool found = false;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path != ScenePath) continue;
                scenes[i] = new EditorBuildSettingsScene(ScenePath, true);
                found = true;
                break;
            }
            if (!found) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            bool emberValleyFound = false;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path != EmberValleyScenePath) continue;
                scenes[i] = new EditorBuildSettingsScene(EmberValleyScenePath, true);
                emberValleyFound = true;
                break;
            }
            if (!emberValleyFound) scenes.Add(new EditorBuildSettingsScene(EmberValleyScenePath, true));
            bool sanctumFound = false;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path != SanctumScenePath) continue;
                scenes[i] = new EditorBuildSettingsScene(SanctumScenePath, true);
                sanctumFound = true;
                break;
            }
            if (!sanctumFound) scenes.Add(new EditorBuildSettingsScene(SanctumScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static GameObject CreateBlock(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetPositionAndRotation(position, Quaternion.identity);
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = material;
            return block;
        }

        private static Material EnsureMaterial(string path, Color color, bool emission)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 1.2f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static string ResolveEnemyDisplayName(NetworkEnemyArchetype archetype) => archetype switch
        {
            NetworkEnemyArchetype.RunePriest => "符文祭司",
            NetworkEnemyArchetype.RuinGuard => "遗迹守卫 · 灼痕",
            _ => "雾行者"
        };

        private static Mesh EnsureEmberShardMesh(string path)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh();
            mesh.Clear();
            mesh.name = Path.GetFileNameWithoutExtension(path);
            mesh.vertices = new[]
            {
                new Vector3(0f, 0.95f, 0f),
                new Vector3(0.48f, 0.08f, 0f),
                new Vector3(0f, 0.02f, 0.4f),
                new Vector3(-0.48f, 0.08f, 0f),
                new Vector3(0f, 0.02f, -0.4f),
                new Vector3(0f, -0.82f, 0f)
            };
            mesh.triangles = new[]
            {
                0, 2, 1,
                0, 3, 2,
                0, 4, 3,
                0, 1, 4,
                5, 1, 2,
                5, 2, 3,
                5, 3, 4,
                5, 4, 1
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Mesh EnsureObjectiveRingMesh(string path)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh();
            mesh.Clear();
            mesh.name = Path.GetFileNameWithoutExtension(path);
            const int segments = 48;
            const float outerRadius = 1.45f;
            const float innerRadius = 1.12f;
            var vertices = new Vector3[(segments + 1) * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float x = Mathf.Cos(angle);
                float z = Mathf.Sin(angle);
                vertices[i * 2] = new Vector3(x * innerRadius, 0f, z * innerRadius);
                vertices[i * 2 + 1] = new Vector3(x * outerRadius, 0f, z * outerRadius);
                if (i == segments) continue;
                int triangle = i * 6;
                int vertex = i * 2;
                triangles[triangle] = vertex;
                triangles[triangle + 1] = vertex + 3;
                triangles[triangle + 2] = vertex + 1;
                triangles[triangle + 3] = vertex;
                triangles[triangle + 4] = vertex + 2;
                triangles[triangle + 5] = vertex + 3;
            }
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Mesh EnsureObjectiveIconMesh(string path)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh();
            mesh.Clear();
            mesh.name = Path.GetFileNameWithoutExtension(path);
            mesh.vertices = new[]
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(0.72f, 0f, 0f),
                new Vector3(0f, 0f, 0.72f),
                new Vector3(-0.72f, 0f, 0f),
                new Vector3(0f, 0f, -0.72f),
                new Vector3(0f, -1f, 0f)
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4,
                5, 1, 2, 5, 2, 3, 5, 3, 4, 5, 4, 1
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static void ApplyEmberValleyReadabilityLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.39f, 0.44f);
            RenderSettings.ambientEquatorColor = new Color(0.21f, 0.235f, 0.265f);
            RenderSettings.ambientGroundColor = new Color(0.11f, 0.12f, 0.14f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.09f, 0.11f, 0.13f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 24f;
            RenderSettings.fogEndDistance = 76f;

            foreach (Light light in Object.FindObjectsOfType<Light>(true))
            {
                if (light.type != LightType.Directional) continue;
                light.intensity = Mathf.Max(light.intensity, 1.25f);
                light.color = Color.Lerp(light.color, new Color(0.92f, 0.94f, 1f), 0.35f);
                EditorUtility.SetDirty(light);
            }
        }

        private static Mesh EnsureSectorMesh(
            string path,
            float radius,
            float angleDegrees,
            int segmentCount)
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh();
            mesh.Clear();
            mesh.name = Path.GetFileNameWithoutExtension(path);

            int segments = Mathf.Max(3, segmentCount);
            float halfAngle = Mathf.Clamp(angleDegrees, 1f, 359f) * 0.5f;
            var vertices = new Vector3[segments + 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 3];
            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0f);
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Lerp(-halfAngle, halfAngle, i / (float)segments) * Mathf.Deg2Rad;
                vertices[i + 1] = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                uvs[i + 1] = new Vector2(i / (float)segments, 1f);
                if (i == segments) continue;
                int triangle = i * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = i + 1;
                triangles[triangle + 2] = i + 2;
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
