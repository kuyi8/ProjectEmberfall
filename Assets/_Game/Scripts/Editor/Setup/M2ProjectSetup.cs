using System;
using System.IO;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Interaction;
using Emberfall.Gameplay.Movement;
using Emberfall.Gameplay.Targeting;
using Emberfall.UI;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    public static class M2ProjectSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/10_EmberValley.unity";
        private const string NavMeshPath = "Assets/_Game/Settings/Navigation/EmberValleyNavMesh.asset";
        private const string TuningPath = "Assets/_Game/Settings/CombatTuning_M1.asset";
        private const string InputPath = "Assets/_Game/Settings/PlayerControls.asset";
        private const string AnimationSetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        private const string CharacterPrefabPath = "Assets/_Game/Prefabs/Characters/P_Player_UniversalBase_Male.prefab";
        private const string EnemyDefinitionsPath = "Assets/_Game/Data/M2/enemies.v1.json";
        private const string QuestDefinitionsPath = "Assets/_Game/Data/M2/quests.v1.json";
        private const string LocalizedTextsPath = "Assets/_Game/Data/M2/texts.zh-CN.v1.json";
        private const string WoodenShieldPath =
            "Assets/ThirdParty/Quaternius/EmberfallArtBaseline/FantasyProps/Exports/FBX/Shield_Wooden.fbx";

        [MenuItem("Emberfall/Setup/Apply M2 Ember Valley Setup")]
        public static void Apply()
        {
            ApplyWithCharacterVisuals(null, null, null);
        }

        internal static void ApplyWithCharacterVisuals(
            GameObject playerVisualOverride,
            GameObject meleeVisualOverride,
            GameObject wardenVisualOverride)
        {
            M1ProjectSetup.ApplyWithCharacterVisuals(playerVisualOverride, meleeVisualOverride);
            Directory.CreateDirectory("Assets/_Game/Settings/Navigation");

            InputActionAsset inputActions = LoadRequired<InputActionAsset>(InputPath);
            CombatTuningAsset tuning = LoadRequired<CombatTuningAsset>(TuningPath);
            PlayerAnimationSet animationSet = LoadRequired<PlayerAnimationSet>(AnimationSetPath);
            GameObject defaultCharacterPrefab = LoadRequired<GameObject>(CharacterPrefabPath);
            GameObject playerCharacterPrefab = playerVisualOverride != null ? playerVisualOverride : defaultCharacterPrefab;
            GameObject meleeCharacterPrefab = meleeVisualOverride != null ? meleeVisualOverride : defaultCharacterPrefab;
            GameObject wardenCharacterPrefab = wardenVisualOverride != null
                ? wardenVisualOverride
                : meleeCharacterPrefab;
            TextAsset enemyDefinitions = LoadRequired<TextAsset>(EnemyDefinitionsPath);
            TextAsset questDefinitions = LoadRequired<TextAsset>(QuestDefinitionsPath);
            TextAsset localizedTexts = LoadRequired<TextAsset>(LocalizedTextsPath);

            Material ground = M1ProjectSetup.EnsureMaterial("M_M2ForestGround", new Color(0.105f, 0.17f, 0.145f), 0.08f);
            Material stone = M1ProjectSetup.EnsureMaterial("M_M2Ruins", new Color(0.24f, 0.29f, 0.28f), 0.1f);
            Material camp = M1ProjectSetup.EnsureMaterial("M_M2Camp", new Color(0.31f, 0.20f, 0.12f), 0.14f);
            Material accent = M1ProjectSetup.EnsureMaterial("M_EmberAccent", new Color(0.93f, 0.32f, 0.08f), 0.25f);
            Material checkpoint = M1ProjectSetup.EnsureMaterial("M_Checkpoint", new Color(0.12f, 0.78f, 0.72f), 0.55f);
            Material weapon = M1ProjectSetup.EnsureMaterial("M_Weapon", new Color(0.78f, 0.83f, 0.86f), 0.75f);
            Material grip = M1ProjectSetup.EnsureMaterial("M_WeaponGrip", new Color(0.09f, 0.055f, 0.035f), 0.18f);
            Material enemy = LoadRequired<Material>("Assets/_Game/Art/Materials/Character/M_Character_EnemyFogwalker.mat");

            CreateScene(
                inputActions,
                tuning,
                animationSet,
                playerCharacterPrefab,
                meleeCharacterPrefab,
                meleeVisualOverride != null,
                wardenCharacterPrefab,
                wardenVisualOverride != null || meleeVisualOverride != null,
                enemyDefinitions,
                questDefinitions,
                localizedTexts,
                ground,
                stone,
                camp,
                accent,
                checkpoint,
                weapon,
                grip,
                enemy);

            M1ProjectSetup.ConfigureBuildSettings();
            PlayerSettings.bundleVersion = "0.3.0";
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Project Emberfall M2 Ember Valley setup completed.");
        }

        private static void CreateScene(
            InputActionAsset inputActions,
            CombatTuningAsset tuning,
            PlayerAnimationSet animationSet,
            GameObject playerCharacterPrefab,
            GameObject meleeCharacterPrefab,
            bool preserveMeleeMaterials,
            GameObject wardenCharacterPrefab,
            bool preserveWardenMaterials,
            TextAsset enemyDefinitions,
            TextAsset questDefinitions,
            TextAsset localizedTexts,
            Material ground,
            Material stone,
            Material camp,
            Material accent,
            Material checkpointMaterial,
            Material weapon,
            Material grip,
            Material enemyMaterial)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureAtmosphere();
            CreateEnvironment(ground, stone, camp, accent);
            BuildNavMesh();

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(ThirdPersonCameraRig));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 5.2f, -10f), Quaternion.Euler(20f, 0f, 0f));
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 58f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = RenderSettings.fogColor;

            GameObject player = M1ProjectSetup.CreatePlayer(
                inputActions,
                tuning,
                animationSet,
                playerCharacterPrefab,
                weapon,
                grip,
                accent,
                cameraObject.transform);
            player.transform.SetPositionAndRotation(new Vector3(0f, 1.05f, -4f), Quaternion.identity);
            PlayerInputReader input = player.GetComponent<PlayerInputReader>();
            PlayerCombatActor combat = player.GetComponent<PlayerCombatActor>();
            PlayerInteractor interactor = player.GetComponent<PlayerInteractor>();
            ThirdPersonCameraRig cameraRig = cameraObject.GetComponent<ThirdPersonCameraRig>();
            cameraRig.Configure(
                player.transform,
                input,
                player.GetComponent<LockOnTargeting>());

            var flowObject = new GameObject("[Application] M2 Route Flow", typeof(M2RouteFlowController));
            M2RouteFlowController flow = flowObject.GetComponent<M2RouteFlowController>();

            Transform campSpawn = new GameObject("CheckpointSpawn_Camp").transform;
            campSpawn.SetPositionAndRotation(new Vector3(0f, 1.05f, -4f), Quaternion.identity);

            CreateScout(flow, camp, accent);
            CreateSeal(flow, "Seal_Forest", "seal:forest", new Vector3(0f, 0f, 25f), accent);
            CreateSeal(flow, "Seal_Bridge", "seal:bridge", new Vector3(10f, 0f, 44f), accent);
            CreateSeal(flow, "Seal_Courtyard", "seal:courtyard", new Vector3(29f, 0f, 46f), accent);
            CreateCheckpoint(flow, "checkpoint:courtyard", new Vector3(31f, 0f, 38f), checkpointMaterial);
            CreateSanctumGate(flow, stone, accent);
            GameObject wardenEntranceBarrier = CreateWardenEntranceBarrier(stone, accent);
            CreateReturnBarrier(flow, stone);
            M1ProjectSetup.CreateVoidExecutionVolume(
                new Vector3(24f, -12f, 24f),
                new Vector3(180f, 2f, 180f));

            M1ProjectSetup.CreateMeleeEnemy(
                new Vector3(0f, 0f, 35f), combat, enemyDefinitions, meleeCharacterPrefab, animationSet,
                enemyMaterial, weapon, grip, accent, "Enemy_Fogwalker_Forest", false, preserveMeleeMaterials);
            M1ProjectSetup.CreateMeleeEnemy(
                new Vector3(24f, 0f, 42f), combat, enemyDefinitions, meleeCharacterPrefab, animationSet,
                enemyMaterial, weapon, grip, accent, "Enemy_Fogwalker_Courtyard", false, preserveMeleeMaterials);
            WardenActor warden = CreateWarden(
                new Vector3(46f, 0f, 20f), combat, enemyDefinitions, wardenCharacterPrefab,
                animationSet, enemyMaterial, weapon, grip, accent, preserveWardenMaterials);

            flow.Configure(questDefinitions, localizedTexts, combat, campSpawn, warden, wardenEntranceBarrier);
            var hudObject = new GameObject("[UI] M2 Route HUD", typeof(M2RouteHud));
            hudObject.GetComponent<M2RouteHud>().Configure(
                flow,
                combat,
                interactor,
                input,
                player.GetComponent<LockOnTargeting>(),
                cameraRig);
            var inputOverlay = new GameObject("[UI] Input Telemetry Overlay", typeof(InputTelemetryOverlay));
            inputOverlay.GetComponent<InputTelemetryOverlay>().Configure(input);

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void ConfigureAtmosphere()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.22f, 0.21f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.045f, 0.075f, 0.07f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 16f;
            RenderSettings.fogEndDistance = 52f;

            var lightObject = new GameObject("Directional Light", typeof(Light));
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.88f, 0.91f, 0.82f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(52f, -38f, 0f);
        }

        private static void CreateEnvironment(Material ground, Material stone, Material camp, Material accent)
        {
            CreateFloor("Zone_Camp", new Vector3(0f, -0.25f, 0f), new Vector3(18f, 0.5f, 18f), camp);
            // Route pieces meet at their boundaries instead of overlapping coplanar top faces.
            // This keeps the greybox/NavMesh authority while removing the Z-fighting seen in playtest video.
            CreateFloor("Path_Forest", new Vector3(0f, -0.25f, 19f), new Vector3(10f, 0.5f, 20f), ground);
            CreateFloor("Zone_Forest", new Vector3(0f, -0.25f, 38f), new Vector3(18f, 0.5f, 18f), ground);
            CreateFloor("Path_Bridge", new Vector3(12f, -0.25f, 44f), new Vector3(6f, 0.5f, 7f), stone);
            CreateFloor("Zone_Courtyard", new Vector3(26f, -0.25f, 44f), new Vector3(22f, 0.5f, 20f), stone);
            CreateFloor("Path_Sanctum", new Vector3(39f, -0.25f, 32.5f), new Vector3(8f, 0.5f, 3f), stone);
            CreateFloor("Zone_Warden", new Vector3(46f, -0.25f, 20f), new Vector3(22f, 0.5f, 22f), stone);
            CreateFloor("Path_Return", new Vector3(22f, -0.25f, 10f), new Vector3(26f, 0.5f, 7f), ground);

            M1ProjectSetup.CreateCube("Camp_BackWall", new Vector3(0f, 1.5f, -9f), new Vector3(18f, 3f, 0.6f), stone);
            M1ProjectSetup.CreateCube("Courtyard_NorthWall", new Vector3(26f, 1.5f, 54f), new Vector3(22f, 3f, 0.6f), stone);
            M1ProjectSetup.CreateCube("Warden_EastWall", new Vector3(57f, 2f, 20f), new Vector3(0.7f, 4f, 22f), stone);
            M1ProjectSetup.CreateCube("Warden_SouthWall", new Vector3(46f, 2f, 9f), new Vector3(22f, 4f, 0.7f), stone);

            for (int i = 0; i < 10; i++)
            {
                float z = 13f + (i * 4.1f);
                CreateTree($"Tree_West_{i:00}", new Vector3(-6.5f - (i % 2), 0f, z), ground);
                CreateTree($"Tree_East_{i:00}", new Vector3(6.5f + (i % 2), 0f, z), ground);
            }

            for (int i = 0; i < 5; i++)
            {
                M1ProjectSetup.CreateCube(
                    $"CourtyardPillar_{i:00}",
                    new Vector3(18f + (i * 4f), 1.6f, 50f),
                    new Vector3(0.9f, 3.2f, 0.9f),
                    stone);
            }

            var beacon = new GameObject("Camp_EmberBeacon", typeof(Light));
            beacon.transform.position = new Vector3(0f, 2.4f, 2f);
            Light ember = beacon.GetComponent<Light>();
            ember.type = LightType.Point;
            ember.color = new Color(1f, 0.28f, 0.06f);
            ember.range = 11f;
            ember.intensity = 3.2f;
            M1ProjectSetup.CreateCube("Camp_EmberStone", new Vector3(0f, 0.75f, 2f), new Vector3(1.1f, 1.5f, 1.1f), accent);
        }

        private static void CreateScout(M2RouteFlowController flow, Material body, Material accent)
        {
            GameObject scout = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            scout.name = "NPC_CampScout";
            scout.transform.position = new Vector3(3.2f, 1f, 0.5f);
            scout.GetComponent<Renderer>().sharedMaterial = body;
            GameObject marker = M1ProjectSetup.CreateCube(
                "Scout_EmberMarker",
                new Vector3(3.2f, 2.45f, 0.5f),
                new Vector3(0.28f, 0.28f, 0.28f),
                accent);
            marker.transform.SetParent(scout.transform, true);
            M2RouteInteractable interactable = scout.AddComponent<M2RouteInteractable>();
            interactable.Configure(flow, M2RouteRole.Scout, "npc:camp-scout", indicator: marker.GetComponent<Renderer>());
        }

        private static void CreateSeal(
            M2RouteFlowController flow,
            string name,
            string stableId,
            Vector3 position,
            Material material)
        {
            GameObject seal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            seal.name = name;
            seal.transform.position = position + Vector3.up;
            seal.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
            seal.GetComponent<Renderer>().sharedMaterial = material;
            M2RouteInteractable interactable = seal.AddComponent<M2RouteInteractable>();
            interactable.Configure(flow, M2RouteRole.Seal, stableId, indicator: seal.GetComponent<Renderer>());
        }

        private static void CreateCheckpoint(
            M2RouteFlowController flow,
            string stableId,
            Vector3 position,
            Material material)
        {
            GameObject checkpoint = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            checkpoint.name = "Checkpoint_Courtyard";
            checkpoint.transform.position = position + (Vector3.up * 0.35f);
            checkpoint.transform.localScale = new Vector3(0.8f, 0.35f, 0.8f);
            checkpoint.GetComponent<Renderer>().sharedMaterial = material;
            Transform spawn = new GameObject("SpawnPoint").transform;
            spawn.SetParent(checkpoint.transform, true);
            spawn.SetPositionAndRotation(position + new Vector3(1.5f, 1.05f, 0f), Quaternion.Euler(0f, 90f, 0f));
            M2RouteInteractable interactable = checkpoint.AddComponent<M2RouteInteractable>();
            interactable.Configure(flow, M2RouteRole.Checkpoint, stableId, spawn, indicator: checkpoint.GetComponent<Renderer>());
        }

        private static void CreateSanctumGate(M2RouteFlowController flow, Material stone, Material accent)
        {
            GameObject blocker = M1ProjectSetup.CreateCube(
                "GateBlocker_Sanctum",
                new Vector3(40f, 1.5f, 32.5f),
                new Vector3(8f, 3f, 0.75f),
                stone);
            NavMeshObstacle obstacle = blocker.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;
            obstacle.carving = true;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "GateControl_Sanctum";
            // Keep the control on the courtyard side of the threshold so the player and NavMesh start
            // remain on authored gameplay ground after the non-overlapping route geometry pass.
            marker.transform.position = new Vector3(36.8f, 0.75f, 35f);
            marker.transform.localScale = new Vector3(0.72f, 0.75f, 0.72f);
            marker.GetComponent<Renderer>().sharedMaterial = accent;
            M2RouteInteractable interactable = marker.AddComponent<M2RouteInteractable>();
            interactable.Configure(
                flow,
                M2RouteRole.SanctumGate,
                "gate:sanctum",
                gateBlocker: blocker,
                indicator: marker.GetComponent<Renderer>());
        }

        private static void CreateReturnBarrier(M2RouteFlowController flow, Material stone)
        {
            GameObject blocker = M1ProjectSetup.CreateCube(
                "GateBlocker_ReturnShortcut",
                new Vector3(12f, 1.5f, 10f),
                new Vector3(0.75f, 3f, 7f),
                stone);
            NavMeshObstacle obstacle = blocker.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;
            obstacle.carving = true;

            var controller = new GameObject("[Flow] Return Shortcut Barrier", typeof(M2StageBarrier));
            controller.GetComponent<M2StageBarrier>().Configure(flow, Emberfall.Quests.Domain.MainQuestStage.ReturnToScout, blocker);
        }

        private static GameObject CreateWardenEntranceBarrier(Material stone, Material accent)
        {
            GameObject blocker = M1ProjectSetup.CreateCube(
                "GateBlocker_WardenEncounter",
                new Vector3(39f, 1.5f, 31f),
                new Vector3(8f, 3f, 0.55f),
                stone);
            var obstacle = blocker.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;
            obstacle.carving = true;

            for (int i = -2; i <= 2; i++)
            {
                GameObject ember = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                ember.name = $"WardenSeal_Ember_{i + 2:00}";
                ember.transform.SetParent(blocker.transform, false);
                ember.transform.localPosition = new Vector3(i * 0.18f, 0f, -0.62f);
                ember.transform.localScale = Vector3.one * 0.09f;
                ember.GetComponent<Renderer>().sharedMaterial = accent;
                UnityEngine.Object.DestroyImmediate(ember.GetComponent<Collider>());
            }

            blocker.SetActive(false);
            return blocker;
        }

        private static WardenActor CreateWarden(
            Vector3 position,
            PlayerCombatActor player,
            TextAsset enemyDefinitions,
            GameObject characterPrefab,
            PlayerAnimationSet animationSet,
            Material bodyMaterial,
            Material metalMaterial,
            Material gripMaterial,
            Material accentMaterial,
            bool preserveVisualMaterials)
        {
            var root = new GameObject(
                "Enemy_EmberWarden",
                typeof(NavMeshAgent),
                typeof(CapsuleCollider),
                typeof(WardenActor),
                typeof(WardenAnimationPresenter),
                typeof(AudioSource),
                typeof(WardenAudioPresenter));
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));

            NavMeshAgent agent = root.GetComponent<NavMeshAgent>();
            agent.radius = 0.52f;
            agent.height = 2.25f;
            agent.acceleration = 15f;
            agent.autoBraking = true;
            agent.avoidancePriority = 18;

            CapsuleCollider bodyCollider = root.GetComponent<CapsuleCollider>();
            bodyCollider.center = new Vector3(0f, 1.12f, 0f);
            bodyCollider.height = 2.25f;
            bodyCollider.radius = 0.52f;

            GameObject visual = PrefabUtility.InstantiatePrefab(characterPrefab, root.transform) as GameObject;
            if (visual == null) throw new InvalidDataException("Could not instantiate Warden visual.");
            visual.name = "EnemyVisual_EmberWarden";
            visual.transform.localPosition = new Vector3(0f, 1.08f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * 1.12f;
            M1ProjectSetup.SetLayerRecursively(visual, 0);

            Renderer bodyRenderer = M1ProjectSetup.FindLargestRenderer(visual);
            if (bodyRenderer == null) throw new InvalidDataException("Warden visual contains no renderer.");
            if (!preserveVisualMaterials) bodyRenderer.sharedMaterial = bodyMaterial;
            Animator animator = visual.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
                throw new InvalidDataException("Warden visual has no valid Humanoid Animator.");

            var aimPoint = new GameObject("AimPoint").transform;
            aimPoint.SetParent(root.transform, false);
            aimPoint.localPosition = new Vector3(0f, 1.88f, 0f);
            var attackOrigin = new GameObject("AttackOrigin").transform;
            attackOrigin.SetParent(root.transform, false);
            attackOrigin.localPosition = new Vector3(0f, 1.38f, 1.18f);

            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Renderer weaponRenderer = null;
            if (rightHand != null)
            {
                Transform socket = M1ProjectSetup.CreateWorldScaleSocket(
                    rightHand,
                    "WeaponSocket_RightHand",
                    new Vector3(0f, 0.015f, 0f),
                    Quaternion.identity);
                M1ProjectSetup.CreateSword(socket, metalMaterial, gripMaterial, accentMaterial);
                Transform blade = socket.Find("Sword_Practice/Blade");
                if (blade != null) weaponRenderer = blade.GetComponent<Renderer>();
            }

            Renderer shieldRenderer = AttachWardenShield(animator, gripMaterial, metalMaterial);
            GameObject telegraph = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            telegraph.name = "WardenAttackTelegraph";
            telegraph.transform.SetParent(root.transform, false);
            telegraph.transform.localPosition = new Vector3(0f, 0.045f, 1.2f);
            telegraph.GetComponent<Renderer>().sharedMaterial = accentMaterial;
            UnityEngine.Object.DestroyImmediate(telegraph.GetComponent<Collider>());

            var phaseLightObject = new GameObject("WardenRuneLight", typeof(Light));
            phaseLightObject.transform.SetParent(root.transform, false);
            phaseLightObject.transform.localPosition = new Vector3(0f, 1.45f, 0.15f);
            Light phaseLight = phaseLightObject.GetComponent<Light>();
            phaseLight.type = LightType.Point;
            phaseLight.range = 7f;
            phaseLight.intensity = 0f;
            phaseLight.shadows = LightShadows.None;

            WardenActor actor = root.GetComponent<WardenActor>();
            actor.Configure(
                enemyDefinitions, "boss:ember-warden", player, agent, aimPoint, attackOrigin,
                bodyRenderer, shieldRenderer, weaponRenderer, telegraph.GetComponent<Renderer>(),
                phaseLight, bodyCollider);
            root.GetComponent<WardenAnimationPresenter>().Configure(animator, actor, animationSet);
            root.GetComponent<WardenAudioPresenter>().Configure(actor, root.GetComponent<AudioSource>());
            return actor;
        }

        private static Renderer AttachWardenShield(Animator animator, Material wood, Material metal)
        {
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (leftHand == null) throw new InvalidDataException("Warden has no left-hand bone.");
            Transform socket = M1ProjectSetup.CreateWorldScaleSocket(
                leftHand,
                "ShieldSocket_LeftHand",
                new Vector3(0.02f, 0.05f, 0.03f),
                Quaternion.Euler(5f, 92f, 88f));

            GameObject source = LoadRequired<GameObject>(WoodenShieldPath);
            GameObject shield = PrefabUtility.InstantiatePrefab(source, socket) as GameObject;
            if (shield == null) throw new InvalidDataException("Could not instantiate Warden shield.");
            shield.name = "Shield_Warden_Equipped";
            shield.transform.localPosition = Vector3.zero;
            shield.transform.localRotation = Quaternion.identity;
            shield.transform.localScale = Vector3.one * 0.82f;
            foreach (Collider collider in shield.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);

            Renderer primary = null;
            foreach (Renderer renderer in shield.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    string name = slots[i] != null ? slots[i].name : string.Empty;
                    slots[i] = name.IndexOf("metal", StringComparison.OrdinalIgnoreCase) >= 0 ? metal : wood;
                }
                renderer.sharedMaterials = slots;
                if (primary == null) primary = renderer;
            }
            return primary;
        }

        private static void CreateFloor(string name, Vector3 position, Vector3 scale, Material material)
        {
            M1ProjectSetup.CreateCube(name, position, scale, material);
        }

        private static void CreateTree(string name, Vector3 position, Material material)
        {
            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = name;
            trunk.transform.position = position + (Vector3.up * 2f);
            trunk.transform.localScale = new Vector3(0.55f, 2f, 0.55f);
            trunk.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void BuildNavMesh()
        {
            var navigation = new GameObject("[Navigation] Ember Valley", typeof(NavMeshSurface));
            NavMeshSurface surface = navigation.GetComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            if (surface.navMeshData == null)
            {
                throw new InvalidOperationException("Ember Valley NavMesh baking produced no data.");
            }

            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath) != null)
            {
                AssetDatabase.DeleteAsset(NavMeshPath);
            }

            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshPath);
            EditorUtility.SetDirty(surface);
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException($"Required M2 asset is missing: {path}", path);
            }

            return asset;
        }
    }
}
