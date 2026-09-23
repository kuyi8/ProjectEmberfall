using System;
using System.IO;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Interaction;
using Emberfall.Gameplay.Movement;
using Emberfall.Gameplay.Targeting;
using Emberfall.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    public static class M1ProjectSetup
    {
        private const string CombatGymPath = "Assets/_Game/Scenes/90_CombatGym.unity";
        private const string CombatGymNavMeshPath = "Assets/_Game/Settings/Navigation/CombatGymNavMesh.asset";
        private const string EnemyDefinitionsPath = "Assets/_Game/Data/M2/enemies.v1.json";
        private const string InputAssetPath = "Assets/_Game/Settings/PlayerControls.asset";
        private const string TuningAssetPath = "Assets/_Game/Settings/CombatTuning_M1.asset";
        private const string MaterialFolder = "Assets/_Game/Art/Materials/M1";
        private const string CharacterMaterialFolder = "Assets/_Game/Art/Materials/Character";
        private const string CharacterPrefabPath = "Assets/_Game/Prefabs/Characters/P_Player_UniversalBase_Male.prefab";
        private const string CharacterSourceRoot = "Assets/_Game/Art/Universal Base Characters[Standard]/Universal Base Characters[Standard]";
        private const string CharacterSourcePath = CharacterSourceRoot + "/Base Characters/Unity/Superhero_Male_FullBody.fbx";
        private const string HairSourcePath = CharacterSourceRoot + "/Hairstyles/Origin at 0/FBX (Unity)/Hair_SimpleParted.fbx";
        private const string CharacterTextureRoot = CharacterSourceRoot + "/Base Characters/Textures";
        private const string MaleBaseTexturePath = CharacterTextureRoot + "/T_Superhero_Male_Dark.png";
        private const string MaleNormalTexturePath = CharacterTextureRoot + "/T_Superhero_Male_Normal.png";
        private const string EyeBaseTexturePath = CharacterTextureRoot + "/T_Eye_Brown.png";
        private const string EyeNormalTexturePath = CharacterTextureRoot + "/T_Eye_Normal.png";
        private const string HairBaseTexturePath = CharacterTextureRoot + "/T_Hair_1_BaseColor.png";
        private const string HairNormalTexturePath = CharacterTextureRoot + "/T_Hair_1_Normal.png";

        [MenuItem("Emberfall/Setup/Apply M1 Combat Gym Setup")]
        public static void Apply()
        {
            ApplyWithCharacterVisuals(null, null);
        }

        internal static void ApplyWithCharacterVisuals(GameObject playerVisualOverride, GameObject meleeVisualOverride)
        {
            M0ProjectSetup.Apply();
            EnsureDirectories();
            InputActionAsset inputActions = EnsureInputActions();
            CombatTuningAsset tuning = EnsureCombatTuning();
            PlayerAnimationSet animationSet = M1AnimationSetup.Apply();
            GameObject defaultVisualPrefab = EnsureCharacterVisualPrefab(animationSet);
            GameObject playerVisualPrefab = playerVisualOverride != null ? playerVisualOverride : defaultVisualPrefab;
            GameObject meleeVisualPrefab = meleeVisualOverride != null ? meleeVisualOverride : defaultVisualPrefab;
            CreateCombatGym(
                inputActions,
                tuning,
                animationSet,
                playerVisualPrefab,
                meleeVisualPrefab,
                meleeVisualOverride != null);
            ConfigureBuildSettings();
            PlayerSettings.bundleVersion = "0.2.5";

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Project Emberfall M1 combat gym setup completed.");
        }

        internal static void EnsureDirectories()
        {
            Directory.CreateDirectory(MaterialFolder);
            Directory.CreateDirectory(CharacterMaterialFolder);
            Directory.CreateDirectory("Assets/_Game/Prefabs/Characters");
            Directory.CreateDirectory("Assets/_Game/Prefabs/Combat");
            Directory.CreateDirectory("Assets/_Game/Settings/Navigation");
        }

        internal static InputActionAsset EnsureInputActions()
        {
            InputActionAsset existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (existing != null)
            {
                InputActionMap existingPlayerMap = existing.FindActionMap("Player", true);
                EnsureSwitchTargetAction(existingPlayerMap);
                EnsureButtonAction(existingPlayerMap, "Pause", "<Keyboard>/escape", "<Gamepad>/start");
                EnsureButtonAction(existingPlayerMap, "Guide", "<Keyboard>/f1", "<Gamepad>/select");
                EnsureButtonAction(existingPlayerMap, "Interact", "<Keyboard>/e", "<Gamepad>/buttonSouth");
                EnsureButtonAction(existingPlayerMap, "Guard", "<Keyboard>/q", "<Gamepad>/leftShoulder");
                EnsureButtonAction(existingPlayerMap, "Heal", "<Keyboard>/r", "<Gamepad>/dpad/up");
                EnsureButtonAction(existingPlayerMap, "RangedAttack", "<Keyboard>/f", "<Gamepad>/rightShoulder");
                EnsureButtonAction(existingPlayerMap, "Sweep", "<Keyboard>/v", "<Gamepad>/dpad/left");
                EnsureButtonAction(existingPlayerMap, "InputOverlay", "<Keyboard>/f2", "<Gamepad>/dpad/down");
                ReplaceBindingPath(
                    existingPlayerMap.FindAction("InputOverlay", true),
                    "<Gamepad>/leftStickPress",
                    "<Gamepad>/dpad/down");
                EnsureButtonAction(existingPlayerMap, "Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap player = asset.AddActionMap("Player");

            InputAction move = player.AddAction("Move", InputActionType.Value);
            move.expectedControlType = "Vector2";
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            move.AddBinding("<Gamepad>/leftStick");

            InputAction look = player.AddAction("Look", InputActionType.Value);
            look.expectedControlType = "Vector2";
            look.AddBinding("<Mouse>/delta");
            look.AddBinding("<Gamepad>/rightStick");

            AddButton(player, "LightAttack", "<Mouse>/leftButton", "<Gamepad>/buttonWest");
            AddButton(player, "HeavyAttack", "<Mouse>/rightButton", "<Gamepad>/buttonNorth");
            AddButton(player, "RangedAttack", "<Keyboard>/f", "<Gamepad>/rightShoulder");
            AddButton(player, "Sweep", "<Keyboard>/v", "<Gamepad>/dpad/left");
            AddButton(player, "Dodge", "<Keyboard>/space", "<Gamepad>/buttonEast");
            AddButton(player, "Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            AddButton(player, "Guard", "<Keyboard>/q", "<Gamepad>/leftShoulder");
            AddButton(player, "Heal", "<Keyboard>/r", "<Gamepad>/dpad/up");
            AddButton(player, "LockOn", "<Mouse>/middleButton", "<Gamepad>/rightStickPress");
            AddButton(player, "Pause", "<Keyboard>/escape", "<Gamepad>/start");
            AddButton(player, "Guide", "<Keyboard>/f1", "<Gamepad>/select");
            AddButton(player, "Interact", "<Keyboard>/e", "<Gamepad>/buttonSouth");
            AddButton(player, "InputOverlay", "<Keyboard>/f2", "<Gamepad>/dpad/down");
            EnsureSwitchTargetAction(player);

            AssetDatabase.CreateAsset(asset, InputAssetPath);
            return asset;
        }

        private static void AddButton(InputActionMap map, string name, string keyboardPath, string gamepadPath)
        {
            InputAction action = map.AddAction(name, InputActionType.Button);
            action.expectedControlType = "Button";
            action.AddBinding(keyboardPath);
            action.AddBinding(gamepadPath);
        }

        private static void EnsureSwitchTargetAction(InputActionMap map)
        {
            if (map.FindAction("SwitchTarget", false) != null)
            {
                return;
            }

            InputAction action = map.AddAction("SwitchTarget", InputActionType.Value);
            action.expectedControlType = "Axis";
            action.AddBinding("<Mouse>/scroll/y");
            action.AddBinding("<Gamepad>/rightStick/x");
        }

        private static void EnsureButtonAction(InputActionMap map, string name, string keyboardPath, string gamepadPath)
        {
            if (map.FindAction(name, false) == null)
            {
                AddButton(map, name, keyboardPath, gamepadPath);
            }
        }

        private static void ReplaceBindingPath(InputAction action, string oldPath, string newPath)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (string.Equals(action.bindings[i].path, oldPath, StringComparison.OrdinalIgnoreCase))
                    action.ChangeBinding(i).WithPath(newPath);
            }
        }

        internal static CombatTuningAsset EnsureCombatTuning()
        {
            CombatTuningAsset existing = AssetDatabase.LoadAssetAtPath<CombatTuningAsset>(TuningAssetPath);
            if (existing != null)
            {
                ConfigureGuardAndPostureTuning(existing);
                return existing;
            }

            var tuning = ScriptableObject.CreateInstance<CombatTuningAsset>();
            AssetDatabase.CreateAsset(tuning, TuningAssetPath);
            ConfigureGuardAndPostureTuning(tuning);
            return tuning;
        }

        private static void ConfigureGuardAndPostureTuning(CombatTuningAsset tuning)
        {
            var serialized = new SerializedObject(tuning);
            serialized.FindProperty("_maxPosture").floatValue = 100f;
            serialized.FindProperty("_postureRegenPerSecond").floatValue = 34f;
            serialized.FindProperty("_postureRegenDelay").floatValue = 1.2f;
            serialized.FindProperty("_guardDamageReduction").floatValue = 0.72f;
            serialized.FindProperty("_perfectGuardWindow").floatValue = 0.20f;
            serialized.FindProperty("_perfectGuardPostureMultiplier").floatValue = 0.15f;
            serialized.FindProperty("_perfectGuardCounterPostureDamage").floatValue = 60f;
            serialized.FindProperty("_guardBreakDuration").floatValue = 0.78f;
            serialized.FindProperty("_healingFlaskCharges").intValue = 2;
            serialized.FindProperty("_healDuration").floatValue = 1.05f;
            serialized.FindProperty("_healResolveTime").floatValue = 0.78f;
            serialized.FindProperty("_healHealthFraction").floatValue = 0.45f;
            serialized.FindProperty("_perfectDodgeWindow").floatValue = 0.18f;
            serialized.FindProperty("_perfectDodgeStaminaRestore").floatValue = 20f;
            serialized.FindProperty("_perfectDodgeDamageBonus").floatValue = 12f;
            serialized.FindProperty("_perfectDodgePostureBonus").floatValue = 18f;
            serialized.FindProperty("_sprintWarmupSeconds").floatValue = 0.5f;
            serialized.FindProperty("_sprintStaminaPerSecond").floatValue = 12f;
            serialized.FindProperty("_executionStaminaCost").floatValue = 22f;
            serialized.FindProperty("_executionDuration").floatValue = 0.72f;
            serialized.FindProperty("_executionResolveTime").floatValue = 0.32f;
            serialized.FindProperty("_sweepDamage").floatValue = 44f;
            serialized.FindProperty("_sweepStaminaCost").floatValue = 30f;
            serialized.FindProperty("_sweepCooldown").floatValue = 4.5f;
            serialized.FindProperty("_sweepDuration").floatValue = 0.82f;
            serialized.FindProperty("_sweepDamageOpen").floatValue = 0.24f;
            serialized.FindProperty("_sweepDamageClose").floatValue = 0.48f;
            serialized.FindProperty("_sweepRadius").floatValue = 2.7f;
            serialized.FindProperty("_sweepAngle").floatValue = 240f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tuning);
        }

        private static void CreateCombatGym(
            InputActionAsset inputActions,
            CombatTuningAsset tuning,
            PlayerAnimationSet animationSet,
            GameObject playerVisualPrefab,
            GameObject meleeVisualPrefab,
            bool preserveMeleeMaterials)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.27f, 0.34f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.055f, 0.075f, 0.09f);
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 18f;
            RenderSettings.fogEndDistance = 42f;

            Material groundMaterial = EnsureMaterial("M_GrayboxGround", new Color(0.17f, 0.22f, 0.25f), 0.15f);
            Material wallMaterial = EnsureMaterial("M_GrayboxStone", new Color(0.27f, 0.31f, 0.34f), 0.08f);
            Material accentMaterial = EnsureMaterial("M_EmberAccent", new Color(0.93f, 0.32f, 0.08f), 0.25f);
            Material weaponMaterial = EnsureMaterial("M_Weapon", new Color(0.78f, 0.83f, 0.86f), 0.75f);
            Material weaponGripMaterial = EnsureMaterial("M_WeaponGrip", new Color(0.09f, 0.055f, 0.035f), 0.18f);
            Material dummyMaterial = EnsureMaterial("M_Dummy", new Color(0.63f, 0.29f, 0.16f), 0.18f);
            Material hazardMaterial = EnsureMaterial("M_Hazard", new Color(0.75f, 0.06f, 0.025f), 0.3f);
            Material checkpointMaterial = EnsureMaterial("M_Checkpoint", new Color(0.12f, 0.78f, 0.72f), 0.55f);
            Material enemyMaterial = EnsureTexturedMaterial(
                "M_Character_EnemyFogwalker",
                MaleBaseTexturePath,
                MaleNormalTexturePath,
                0.12f);
            enemyMaterial.color = new Color(0.48f, 0.62f, 0.5f);
            EditorUtility.SetDirty(enemyMaterial);

            TextAsset enemyDefinitions = AssetDatabase.LoadAssetAtPath<TextAsset>(EnemyDefinitionsPath);
            if (enemyDefinitions == null)
            {
                throw new FileNotFoundException("M2 enemy definition JSON is missing.", EnemyDefinitionsPath);
            }

            CreateEnvironment(groundMaterial, wallMaterial, accentMaterial);
            CreateCombatGymNavMesh();

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(ThirdPersonCameraRig));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 4.2f, -8f), Quaternion.Euler(18f, 0f, 0f));
            Camera gameplayCamera = cameraObject.GetComponent<Camera>();
            gameplayCamera.fieldOfView = 58f;
            gameplayCamera.clearFlags = CameraClearFlags.SolidColor;
            gameplayCamera.backgroundColor = RenderSettings.fogColor;

            GameObject player = CreatePlayer(
                inputActions,
                tuning,
                animationSet,
                playerVisualPrefab,
                weaponMaterial,
                weaponGripMaterial,
                accentMaterial,
                cameraObject.transform);
            PlayerInputReader input = player.GetComponent<PlayerInputReader>();
            PlayerCombatActor combat = player.GetComponent<PlayerCombatActor>();
            LockOnTargeting targeting = player.GetComponent<LockOnTargeting>();
            ThirdPersonMotor motor = player.GetComponent<ThirdPersonMotor>();
            cameraObject.GetComponent<ThirdPersonCameraRig>().Configure(player.transform, input, targeting);

            CreateDummy(new Vector3(0f, 1f, 2.4f), dummyMaterial, "TrainingDummy_Close");
            CreateDummy(new Vector3(-4.2f, 1f, 5.4f), dummyMaterial, "TrainingDummy_Left");
            CreateDummy(new Vector3(4.2f, 1f, 5.4f), dummyMaterial, "TrainingDummy_Right");
            CreateMeleeEnemy(
                new Vector3(0f, 0f, 7.2f),
                player.GetComponent<PlayerCombatActor>(),
                enemyDefinitions,
                meleeVisualPrefab,
                animationSet,
                enemyMaterial,
                weaponMaterial,
                weaponGripMaterial,
                accentMaterial,
                preserveVisualMaterials: preserveMeleeMaterials);
            CreateHazard(new Vector3(-6f, 0.18f, -1f), hazardMaterial);
            CreateCheckpoint(new Vector3(1.25f, 0f, -2.5f), checkpointMaterial);
            CreateVoidExecutionVolume(new Vector3(0f, -12f, 8f), new Vector3(120f, 2f, 120f));

            var overlay = new GameObject("[UI] Combat Debug Overlay", typeof(CombatDebugOverlay));
            overlay.GetComponent<CombatDebugOverlay>().Configure(combat, motor, targeting, gameplayCamera, input);
            var inputOverlay = new GameObject("[UI] Input Telemetry Overlay", typeof(InputTelemetryOverlay));
            inputOverlay.GetComponent<InputTelemetryOverlay>().Configure(input);

            EditorSceneManager.SaveScene(scene, CombatGymPath);
        }

        internal static GameObject CreatePlayer(
            InputActionAsset inputActions,
            CombatTuningAsset tuning,
            PlayerAnimationSet animationSet,
            GameObject characterVisualPrefab,
            Material weaponMaterial,
            Material weaponGripMaterial,
            Material weaponGuardMaterial,
            Transform cameraTransform)
        {
            var player = new GameObject(
                "Player",
                typeof(CharacterController),
                typeof(PlayerInputReader),
                typeof(LockOnTargeting),
                typeof(PlayerCombatActor),
                typeof(ThirdPersonMotor),
                typeof(PlayerInteractor),
                typeof(PlayerAnimationPresenter),
                typeof(PerfectDefenseFeedbackPresenter));
            player.layer = 2;
            player.transform.position = new Vector3(0f, 1.05f, -3.5f);

            CharacterController controller = player.GetComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.42f;
            controller.center = Vector3.zero;
            controller.stepOffset = 0.32f;

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(characterVisualPrefab, player.transform);
            visual.name = "CharacterVisual";
            SetLayerRecursively(visual, 2);
            Renderer bodyRenderer = FindLargestRenderer(visual);
            if (bodyRenderer == null)
            {
                throw new InvalidOperationException("The generated character visual does not contain a renderer.");
            }

            Animator animator = visual.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                throw new InvalidOperationException("The generated character visual does not contain a valid Humanoid Animator.");
            }

            var aimPoint = new GameObject("AimPoint").transform;
            aimPoint.SetParent(player.transform, false);
            aimPoint.localPosition = new Vector3(0f, 0.65f, 0f);

            var attackOrigin = new GameObject("AttackOrigin").transform;
            attackOrigin.SetParent(player.transform, false);
            attackOrigin.localPosition = new Vector3(0f, 0.35f, 1.05f);

            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (rightHand == null)
            {
                throw new InvalidOperationException("The Humanoid avatar has no right-hand bone for the weapon socket.");
            }

            Transform weaponSocket = CreateWorldScaleSocket(
                rightHand,
                "WeaponSocket_RightHand",
                new Vector3(0f, 0.015f, 0f),
                Quaternion.identity);
            if (!HasVisibleEmbeddedWeapon(visual, "Warrior_Sword"))
            {
                CreateSword(weaponSocket, weaponMaterial, weaponGripMaterial, weaponGuardMaterial);
            }

            PlayerInputReader input = player.GetComponent<PlayerInputReader>();
            input.Configure(inputActions);
            LockOnTargeting targeting = player.GetComponent<LockOnTargeting>();
            targeting.Configure(input, cameraTransform);
            PlayerCombatActor combat = player.GetComponent<PlayerCombatActor>();
            combat.Configure(input, tuning, attackOrigin, aimPoint, controller, bodyRenderer);
            ThirdPersonMotor motor = player.GetComponent<ThirdPersonMotor>();
            motor.Configure(input, combat, targeting, cameraTransform, controller);
            player.GetComponent<PlayerInteractor>().Configure(input, player.transform, combat);
            player.GetComponent<PlayerAnimationPresenter>().Configure(animator, combat, motor, animationSet);
            player.GetComponent<PerfectDefenseFeedbackPresenter>().Configure(combat, animator);
            return player;
        }

        internal static GameObject EnsureCharacterVisualPrefab(PlayerAnimationSet animationSet)
        {
            EnsureHumanoidModel(CharacterSourcePath);
            GameObject characterSource = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSourcePath);
            if (characterSource == null)
            {
                throw new FileNotFoundException("Character source FBX is missing.", CharacterSourcePath);
            }

            Material bodyMaterial = EnsureTexturedMaterial(
                "M_Character_Male_Dark",
                MaleBaseTexturePath,
                MaleNormalTexturePath,
                0.22f);
            Material eyeMaterial = EnsureTexturedMaterial(
                "M_Character_Eye_Brown",
                EyeBaseTexturePath,
                EyeNormalTexturePath,
                0.55f);
            Material hairMaterial = EnsureTexturedMaterial(
                "M_Character_Hair",
                HairBaseTexturePath,
                HairNormalTexturePath,
                0.18f);

            var root = new GameObject("PlayerVisual_UniversalBase");
            GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(characterSource, root.transform);
            body.name = "Superhero_Male_FullBody";
            ApplyBodyMaterials(body, bodyMaterial, eyeMaterial);

            Bounds sourceBounds = CalculateBounds(body);
            if (sourceBounds.size.y <= Mathf.Epsilon)
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw new InvalidOperationException("Character source FBX has no usable renderer bounds.");
            }

            float scale = 1.9f / sourceBounds.size.y;
            Vector3 normalizedPosition = new Vector3(
                -sourceBounds.center.x * scale,
                -1f - sourceBounds.min.y * scale,
                -sourceBounds.center.z * scale);
            body.transform.localScale = Vector3.one * scale;
            body.transform.localPosition = normalizedPosition;
            body.transform.localRotation = Quaternion.identity;

            Animator animator = body.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw new InvalidOperationException("Character source does not expose a valid Humanoid Animator.");
            }

            animator.runtimeAnimatorController = animationSet.Controller;
            animator.applyRootMotion = false;

            GameObject hairSource = AssetDatabase.LoadAssetAtPath<GameObject>(HairSourcePath);
            if (hairSource != null)
            {
                GameObject hair = (GameObject)PrefabUtility.InstantiatePrefab(hairSource, root.transform);
                hair.name = "Hair_SimpleParted";
                hair.transform.localScale = body.transform.localScale;
                hair.transform.localPosition = body.transform.localPosition;
                hair.transform.localRotation = body.transform.localRotation;
                foreach (Renderer renderer in hair.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        materials[i] = hairMaterial;
                    }

                    renderer.sharedMaterials = materials;
                }

                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    hair.transform.SetParent(head, true);
                }
            }

            SetLayerRecursively(root, 2);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, CharacterPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null)
            {
                throw new InvalidOperationException("Failed to save the project-owned character visual prefab.");
            }

            return prefab;
        }

        internal static void CreateSword(
            Transform socket,
            Material bladeMaterial,
            Material gripMaterial,
            Material guardMaterial)
        {
            var weaponRoot = new GameObject("Sword_Practice").transform;
            weaponRoot.SetParent(socket, false);

            CreatePrimitiveChild(
                weaponRoot,
                PrimitiveType.Cylinder,
                "Grip",
                new Vector3(0f, 0.12f, 0f),
                new Vector3(0.045f, 0.12f, 0.045f),
                gripMaterial,
                false);
            CreatePrimitiveChild(
                weaponRoot,
                PrimitiveType.Cube,
                "Guard",
                new Vector3(0f, 0.26f, 0f),
                new Vector3(0.29f, 0.04f, 0.055f),
                guardMaterial,
                false);
            CreatePrimitiveChild(
                weaponRoot,
                PrimitiveType.Cube,
                "Blade",
                new Vector3(0f, 0.76f, 0f),
                new Vector3(0.075f, 0.96f, 0.03f),
                bladeMaterial,
                false);
        }

        /// <summary>
        /// Creates an art-only attachment socket whose children keep metre-based dimensions even
        /// when an imported FBX exposes compensated or non-unit bone scales.
        /// </summary>
        internal static Transform CreateWorldScaleSocket(
            Transform bone,
            string name,
            Vector3 worldOffsetInBoneAxes,
            Quaternion localRotation)
        {
            if (bone == null) throw new ArgumentNullException(nameof(bone));

            var socket = new GameObject(name).transform;
            socket.SetParent(bone, false);
            socket.localPosition = Vector3.zero;
            socket.localRotation = localRotation;
            socket.localScale = Vector3.one;

            Vector3 inheritedScale = socket.lossyScale;
            if (Mathf.Abs(inheritedScale.x) <= 0.0001f ||
                Mathf.Abs(inheritedScale.y) <= 0.0001f ||
                Mathf.Abs(inheritedScale.z) <= 0.0001f)
            {
                UnityEngine.Object.DestroyImmediate(socket.gameObject);
                throw new InvalidOperationException($"Bone '{bone.name}' has a zero scale axis.");
            }

            socket.localScale = new Vector3(
                socket.localScale.x / inheritedScale.x,
                socket.localScale.y / inheritedScale.y,
                socket.localScale.z / inheritedScale.z);
            socket.position = bone.position + (bone.rotation * worldOffsetInBoneAxes);
            return socket;
        }

        internal static bool HasVisibleEmbeddedWeapon(GameObject visual, string weaponName)
        {
            if (visual == null || string.IsNullOrWhiteSpace(weaponName)) return false;
            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled &&
                    renderer.gameObject.activeInHierarchy &&
                    renderer.transform.name.IndexOf(weaponName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureHumanoidModel(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not ModelImporter importer)
            {
                throw new FileNotFoundException("Character model importer is missing.", assetPath);
            }

            if (importer.animationType != ModelImporterAnimationType.Human ||
                importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();
            }
        }

        private static void ApplyBodyMaterials(GameObject body, Material bodyMaterial, Material eyeMaterial)
        {
            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string slotName = materials[i] != null ? materials[i].name : string.Empty;
                    materials[i] = slotName.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0
                        ? eyeMaterial
                        : bodyMaterial;
                }

                renderer.sharedMaterials = materials;
            }
        }

        private static Bounds CalculateBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds();
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        internal static Renderer FindLargestRenderer(GameObject root)
        {
            Renderer largest = null;
            float largestVolume = -1f;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Vector3 size = renderer.bounds.size;
                float volume = size.x * size.y * size.z;
                if (volume > largestVolume)
                {
                    largest = renderer;
                    largestVolume = volume;
                }
            }

            return largest;
        }

        internal static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        private static void CreateDummy(Vector3 position, Material material, string name)
        {
            GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            dummy.name = name;
            dummy.transform.position = position;
            dummy.GetComponent<Renderer>().sharedMaterial = material;

            var aimPoint = new GameObject("AimPoint").transform;
            aimPoint.SetParent(dummy.transform, false);
            aimPoint.localPosition = new Vector3(0f, 0.65f, 0f);

            TrainingDummy component = dummy.AddComponent<TrainingDummy>();
            component.Configure(aimPoint, dummy.GetComponent<Renderer>(), dummy.GetComponent<Collider>());

            GameObject baseObject = CreatePrimitiveChild(
                dummy.transform,
                PrimitiveType.Cylinder,
                "Base",
                new Vector3(0f, -1f, 0f),
                new Vector3(0.75f, 0.08f, 0.75f),
                material,
                false);
            baseObject.name = "Base";
        }

        private static void CreateHazard(Vector3 position, Material material)
        {
            GameObject hazard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hazard.name = "DamageHazard_Red";
            hazard.transform.position = position;
            hazard.transform.localScale = new Vector3(3f, 0.35f, 3f);
            hazard.GetComponent<Renderer>().sharedMaterial = material;
            BoxCollider collider = hazard.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            Rigidbody body = hazard.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            hazard.AddComponent<DamagePulseHazard>();
        }

        internal static GameObject CreateVoidExecutionVolume(Vector3 position, Vector3 size)
        {
            var execution = new GameObject("[Gameplay] Void Execution Volume", typeof(BoxCollider), typeof(Rigidbody));
            execution.transform.position = position;
            BoxCollider volume = execution.GetComponent<BoxCollider>();
            volume.size = size;
            volume.isTrigger = true;
            Rigidbody body = execution.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            execution.AddComponent<VoidExecutionVolume>().Configure(volume);
            return execution;
        }

        private static void CreateCheckpoint(Vector3 position, Material material)
        {
            var checkpoint = new GameObject("Checkpoint_Training", typeof(CheckpointInteractable));
            checkpoint.transform.position = position;
            CreatePrimitiveChild(
                checkpoint.transform,
                PrimitiveType.Cylinder,
                "Pedestal",
                new Vector3(0f, 0.12f, 0f),
                new Vector3(0.65f, 0.12f, 0.65f),
                material,
                true);
            CreatePrimitiveChild(
                checkpoint.transform,
                PrimitiveType.Cube,
                "Beacon",
                new Vector3(0f, 0.85f, 0f),
                new Vector3(0.18f, 1.35f, 0.18f),
                material,
                false);

            var spawnPoint = new GameObject("RespawnPoint").transform;
            spawnPoint.SetParent(checkpoint.transform, false);
            spawnPoint.localPosition = new Vector3(1.1f, 1.05f, 0f);
            checkpoint.GetComponent<CheckpointInteractable>().Configure("checkpoint:combat-gym", spawnPoint);
        }

        private static void CreateCombatGymNavMesh()
        {
            var navigation = new GameObject("[Navigation] Combat Gym", typeof(NavMeshSurface));
            NavMeshSurface surface = navigation.GetComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            if (surface.navMeshData == null)
            {
                throw new InvalidOperationException("Combat Gym NavMesh baking produced no data.");
            }

            if (AssetDatabase.LoadAssetAtPath<NavMeshData>(CombatGymNavMeshPath) != null)
            {
                AssetDatabase.DeleteAsset(CombatGymNavMeshPath);
            }

            AssetDatabase.CreateAsset(surface.navMeshData, CombatGymNavMeshPath);
            EditorUtility.SetDirty(surface);
        }

        internal static GameObject CreateMeleeEnemy(
            Vector3 position,
            PlayerCombatActor player,
            TextAsset enemyDefinitions,
            GameObject characterVisualPrefab,
            PlayerAnimationSet animationSet,
            Material enemyMaterial,
            Material weaponMaterial,
            Material weaponGripMaterial,
            Material weaponGuardMaterial,
            string enemyName = "Enemy_Fogwalker",
            bool autoResetAfterDelay = true,
            bool preserveVisualMaterials = false)
        {
            var enemy = new GameObject(
                enemyName,
                typeof(NavMeshAgent),
                typeof(CapsuleCollider),
                typeof(MeleeEnemyActor),
                typeof(MeleeEnemyAnimationPresenter));
            enemy.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));

            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            agent.radius = 0.42f;
            agent.height = 2f;
            agent.acceleration = 12f;
            agent.autoBraking = true;
            agent.avoidancePriority = 45;

            CapsuleCollider collider = enemy.GetComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 1.05f, 0f);
            collider.height = 2f;
            collider.radius = 0.42f;

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(characterVisualPrefab, enemy.transform);
            visual.name = "EnemyVisual_Fogwalker";
            visual.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            SetLayerRecursively(visual, 0);

            Renderer bodyRenderer = FindLargestRenderer(visual);
            if (bodyRenderer == null)
            {
                throw new InvalidOperationException("Fogwalker visual contains no renderer.");
            }

            if (!preserveVisualMaterials)
            {
                bodyRenderer.sharedMaterial = enemyMaterial;
            }
            Animator animator = visual.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                throw new InvalidOperationException("Fogwalker visual does not contain a valid Humanoid Animator.");
            }

            var aimPoint = new GameObject("AimPoint").transform;
            aimPoint.SetParent(enemy.transform, false);
            aimPoint.localPosition = new Vector3(0f, 1.7f, 0f);

            var attackOrigin = new GameObject("AttackOrigin").transform;
            attackOrigin.SetParent(enemy.transform, false);
            attackOrigin.localPosition = new Vector3(0f, 1.35f, 1.02f);

            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (rightHand != null)
            {
                Transform socket = CreateWorldScaleSocket(
                    rightHand,
                    "WeaponSocket_RightHand",
                    new Vector3(0f, 0.015f, 0f),
                    Quaternion.identity);
                CreateSword(socket, weaponMaterial, weaponGripMaterial, weaponGuardMaterial);
            }

            MeleeEnemyActor actor = enemy.GetComponent<MeleeEnemyActor>();
            actor.Configure(
                enemyDefinitions,
                "enemy:fogwalker",
                player,
                agent,
                aimPoint,
                attackOrigin,
                bodyRenderer,
                collider);
            actor.SetAutoResetAfterDelay(autoResetAfterDelay);
            enemy.GetComponent<MeleeEnemyAnimationPresenter>().Configure(animator, actor, animationSet);
            return enemy;
        }

        internal static GameObject CreateRangedEnemy(
            Vector3 position,
            PlayerCombatActor player,
            TextAsset enemyDefinitions,
            GameObject characterVisualPrefab,
            PlayerAnimationSet animationSet,
            Material enemyMaterial,
            Material projectileMaterial,
            Material staffMaterial,
            string enemyName = "Enemy_RunePriest",
            bool autoResetAfterDelay = true,
            bool preserveVisualMaterials = false)
        {
            var enemy = new GameObject(
                enemyName,
                typeof(NavMeshAgent),
                typeof(CapsuleCollider),
                typeof(RangedEnemyActor),
                typeof(RangedEnemyAnimationPresenter));
            enemy.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));

            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            agent.radius = 0.42f;
            agent.height = 2f;
            agent.acceleration = 13f;
            agent.autoBraking = true;
            agent.avoidancePriority = 55;

            CapsuleCollider collider = enemy.GetComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 1.05f, 0f);
            collider.height = 2f;
            collider.radius = 0.42f;

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(characterVisualPrefab, enemy.transform);
            visual.name = "EnemyVisual_RunePriest";
            visual.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            SetLayerRecursively(visual, 0);

            Renderer bodyRenderer = FindLargestRenderer(visual);
            if (bodyRenderer == null)
            {
                throw new InvalidOperationException("Rune priest visual contains no renderer.");
            }

            if (!preserveVisualMaterials)
            {
                bodyRenderer.sharedMaterial = enemyMaterial;
            }
            Animator animator = visual.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                throw new InvalidOperationException("Rune priest visual does not contain a valid Humanoid Animator.");
            }

            var aimPoint = new GameObject("AimPoint").transform;
            aimPoint.SetParent(enemy.transform, false);
            aimPoint.localPosition = new Vector3(0f, 1.7f, 0f);

            var castOrigin = new GameObject("CastOrigin").transform;
            castOrigin.SetParent(enemy.transform, false);
            castOrigin.localPosition = new Vector3(0f, 1.42f, 0.82f);

            GameObject telegraph = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            telegraph.name = "CastTelegraph";
            telegraph.transform.SetParent(castOrigin, false);
            telegraph.transform.localScale = Vector3.one * 0.42f;
            telegraph.GetComponent<Renderer>().sharedMaterial = projectileMaterial;
            UnityEngine.Object.DestroyImmediate(telegraph.GetComponent<Collider>());

            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (leftHand != null && !HasVisibleEmbeddedWeapon(visual, "Wizard_Staff"))
            {
                Transform socket = CreateWorldScaleSocket(
                    leftHand,
                    "StaffSocket_LeftHand",
                    Vector3.zero,
                    Quaternion.Euler(0f, 0f, 8f));
                GameObject staff = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                staff.name = "RuneStaff_Placeholder";
                staff.transform.SetParent(socket, false);
                staff.transform.localPosition = new Vector3(0f, 0.42f, 0f);
                staff.transform.localRotation = Quaternion.identity;
                staff.transform.localScale = new Vector3(0.055f, 0.72f, 0.055f);
                staff.GetComponent<Renderer>().sharedMaterial = staffMaterial;
                UnityEngine.Object.DestroyImmediate(staff.GetComponent<Collider>());
            }

            RangedEnemyActor actor = enemy.GetComponent<RangedEnemyActor>();
            actor.Configure(
                enemyDefinitions,
                "enemy:rune-priest",
                player,
                agent,
                aimPoint,
                castOrigin,
                telegraph.transform,
                bodyRenderer,
                collider,
                projectileMaterial);
            actor.SetAutoResetAfterDelay(autoResetAfterDelay);
            enemy.GetComponent<RangedEnemyAnimationPresenter>().Configure(animator, actor, animationSet);
            return enemy;
        }

        private static void CreateEnvironment(Material ground, Material wall, Material accent)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground_Graybox";
            floor.transform.localScale = new Vector3(2.4f, 1f, 2.4f);
            floor.GetComponent<Renderer>().sharedMaterial = ground;

            CreateCube("Wall_North", new Vector3(0f, 2f, 12f), new Vector3(24f, 4f, 0.6f), wall);
            CreateCube("Wall_South", new Vector3(0f, 2f, -12f), new Vector3(24f, 4f, 0.6f), wall);
            CreateCube("Wall_East", new Vector3(12f, 2f, 0f), new Vector3(0.6f, 4f, 24f), wall);
            CreateCube("Wall_West", new Vector3(-12f, 2f, 0f), new Vector3(0.6f, 4f, 24f), wall);

            for (int i = -2; i <= 2; i++)
            {
                CreateCube($"Pillar_{i + 3:00}", new Vector3(i * 4.2f, 1.4f, 8.5f), new Vector3(0.8f, 2.8f, 0.8f), wall);
            }

            CreateCube("EmberMonolith", new Vector3(0f, 2.2f, 10.7f), new Vector3(1.4f, 4.4f, 0.55f), accent);

            var lightObject = new GameObject("Directional Light", typeof(Light));
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.86f, 0.72f);
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            var emberLightObject = new GameObject("Ember Point Light", typeof(Light));
            Light emberLight = emberLightObject.GetComponent<Light>();
            emberLight.type = LightType.Point;
            emberLight.color = new Color(1f, 0.24f, 0.06f);
            emberLight.range = 12f;
            emberLight.intensity = 3.5f;
            emberLight.shadows = LightShadows.Soft;
            emberLightObject.transform.position = new Vector3(0f, 3.5f, 9.5f);
        }

        internal static GameObject CreateCube(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static GameObject CreatePrimitiveChild(
            Transform parent,
            PrimitiveType primitiveType,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool keepCollider)
        {
            GameObject child = GameObject.CreatePrimitive(primitiveType);
            child.name = name;
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            child.transform.localScale = localScale;
            child.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
            {
                UnityEngine.Object.DestroyImmediate(child.GetComponent<Collider>());
            }

            return child;
        }

        internal static Material EnsureMaterial(string name, Color color, float smoothness)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader) { name = name, color = color };
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        internal static Material EnsureTexturedMaterial(
            string name,
            string baseTexturePath,
            string normalTexturePath,
            float smoothness)
        {
            Texture2D baseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(baseTexturePath);
            Texture2D normalTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalTexturePath);
            if (baseTexture == null || normalTexture == null)
            {
                throw new FileNotFoundException($"Character texture is missing for material {name}.");
            }

            EnsureNormalTexture(normalTexturePath);
            string path = $"{CharacterMaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", baseTexture);
            material.SetTexture("_BumpMap", normalTexture);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_BumpScale", 1f);
            material.SetFloat("_Smoothness", smoothness);
            material.color = Color.white;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureNormalTexture(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer &&
                importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }

        internal static void ConfigureBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene("Assets/_Game/Scenes/00_Bootstrap.unity", true),
                new EditorBuildSettingsScene("Assets/_Game/Scenes/01_MainMenu.unity", true),
            };
            const string emberValleyPath = "Assets/_Game/Scenes/10_EmberValley.unity";
            if (File.Exists(emberValleyPath))
            {
                scenes.Add(new EditorBuildSettingsScene(emberValleyPath, true));
            }

            scenes.Add(new EditorBuildSettingsScene(CombatGymPath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
