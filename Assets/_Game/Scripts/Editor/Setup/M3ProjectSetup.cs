using System.IO;
using System.Collections.Generic;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;

namespace Emberfall.Editor.Setup
{
    public static class M3ProjectSetup
    {
        private const string CombatGymPath = "Assets/_Game/Scenes/90_CombatGym.unity";
        private const string EmberValleyPath = "Assets/_Game/Scenes/10_EmberValley.unity";
        private const string AnimationSetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        private const string CharacterPrefabPath = "Assets/_Game/Prefabs/Characters/P_Player_UniversalBase_Male.prefab";
        private const string EnemyDefinitionsPath = "Assets/_Game/Data/M2/enemies.v1.json";
        private const string FogwalkerMaterialPath =
            "Assets/_Game/Art/Materials/Character/M_Character_EnemyFogwalker.mat";
        private const string RunePriestMaterialPath =
            "Assets/_Game/Art/Materials/Character/M_Character_RunePriest.mat";
        private const string WoodenShieldPath =
            "Assets/ThirdParty/Quaternius/EmberfallArtBaseline/FantasyProps/Exports/FBX/Shield_Wooden.fbx";

        [MenuItem("Emberfall/Setup/Apply M3 Ranged Enemy Slice")]
        public static void Apply()
        {
            ApplyWithCharacterVisuals(null, null, null, null);
        }

        internal static void ApplyWithCharacterVisuals(
            GameObject playerVisualOverride,
            GameObject meleeVisualOverride,
            GameObject rangedVisualOverride,
            GameObject wardenVisualOverride)
        {
            M2ProjectSetup.ApplyWithCharacterVisuals(
                playerVisualOverride,
                meleeVisualOverride,
                wardenVisualOverride);

            PlayerAnimationSet animationSet = LoadRequired<PlayerAnimationSet>(AnimationSetPath);
            GameObject characterPrefab = rangedVisualOverride != null
                ? rangedVisualOverride
                : LoadRequired<GameObject>(CharacterPrefabPath);
            TextAsset enemyDefinitions = LoadRequired<TextAsset>(EnemyDefinitionsPath);
            Material runePriest = EnsureRunePriestMaterial();
            Material projectile = M1ProjectSetup.EnsureMaterial(
                "M_RuneProjectile",
                new Color(0.46f, 0.12f, 0.92f),
                0.7f);
            projectile.EnableKeyword("_EMISSION");
            projectile.SetColor("_EmissionColor", new Color(1.3f, 0.22f, 2.4f));
            EditorUtility.SetDirty(projectile);
            Material neutralShrine = M1ProjectSetup.EnsureMaterial(
                "M_NeutralPostureShrine",
                new Color(0.08f, 0.7f, 0.88f),
                0.58f);
            neutralShrine.EnableKeyword("_EMISSION");
            neutralShrine.SetColor("_EmissionColor", new Color(0.08f, 1.05f, 1.35f));
            EditorUtility.SetDirty(neutralShrine);
            Material neutralWarning = M1ProjectSetup.EnsureMaterial(
                "M_NeutralPostureWarning",
                new Color(0.12f, 0.78f, 1f),
                0.42f);
            neutralWarning.EnableKeyword("_EMISSION");
            neutralWarning.SetColor("_EmissionColor", new Color(0.12f, 1.15f, 1.5f));
            EditorUtility.SetDirty(neutralWarning);
            Material staff = M1ProjectSetup.EnsureMaterial(
                "M_RuneStaff",
                new Color(0.13f, 0.075f, 0.19f),
                0.28f);
            Material shield = M1ProjectSetup.EnsureMaterial(
                "M_RuinGuard_Shield",
                new Color(0.31f, 0.17f, 0.08f),
                0.28f);
            Material shieldMetal = M1ProjectSetup.EnsureMaterial(
                "M_RuinGuard_Metal",
                new Color(0.32f, 0.38f, 0.41f),
                0.58f);
            Material fogwalker = LoadRequired<Material>(FogwalkerMaterialPath);
            Material meleeWeapon = M1ProjectSetup.EnsureMaterial(
                "M_Weapon",
                new Color(0.34f, 0.38f, 0.42f),
                0.68f);
            Material meleeGrip = M1ProjectSetup.EnsureMaterial(
                "M_WeaponGrip",
                new Color(0.13f, 0.075f, 0.045f),
                0.24f);
            Material meleeAccent = M1ProjectSetup.EnsureMaterial(
                "M_CombatAccent",
                new Color(0.9f, 0.18f, 0.045f),
                0.55f);

            AddRunePriestToScene(
                CombatGymPath,
                new Vector3(-7f, 0f, 7.2f),
                "Enemy_RunePriest_Gym",
                true,
                enemyDefinitions,
                characterPrefab,
                animationSet,
                runePriest,
                projectile,
                staff,
                rangedVisualOverride != null);

            GameObject shieldVisual = meleeVisualOverride != null
                ? meleeVisualOverride
                : LoadRequired<GameObject>(CharacterPrefabPath);
            AddShieldGuardToScene(
                CombatGymPath,
                new Vector3(7.2f, 0f, 6.6f),
                "Enemy_RuinGuard_Gym",
                true,
                null,
                enemyDefinitions,
                shieldVisual,
                animationSet,
                shield,
                shieldMetal,
                meleeVisualOverride != null);
            AddShieldGuardToScene(
                EmberValleyPath,
                new Vector3(24f, 0f, 42f),
                "Enemy_RuinGuard_Courtyard",
                false,
                "Enemy_Fogwalker_Courtyard",
                enemyDefinitions,
                shieldVisual,
                animationSet,
                shield,
                shieldMetal,
                meleeVisualOverride != null);
            AddMeleeEnemyToScene(
                EmberValleyPath,
                new Vector3(29.5f, 0f, 44.5f),
                "Enemy_Fogwalker_Courtyard_Support",
                false,
                enemyDefinitions,
                shieldVisual,
                animationSet,
                fogwalker,
                meleeWeapon,
                meleeGrip,
                meleeAccent,
                meleeVisualOverride != null);
            AddRunePriestToScene(
                EmberValleyPath,
                new Vector3(6.8f, 0f, 40.5f),
                "Enemy_RunePriest_Forest",
                false,
                enemyDefinitions,
                characterPrefab,
                animationSet,
                runePriest,
                projectile,
                staff,
                rangedVisualOverride != null);
            AddRunePriestToScene(
                EmberValleyPath,
                new Vector3(34f, 0f, 48.5f),
                "Enemy_RunePriest_Courtyard",
                false,
                enemyDefinitions,
                characterPrefab,
                animationSet,
                runePriest,
                projectile,
                staff,
                rangedVisualOverride != null);

            Material forestEmber = M1ProjectSetup.EnsureMaterial(
                "M_ForestRune_Ember",
                new Color(1f, 0.22f, 0.035f),
                0.62f);
            Material forestGuard = M1ProjectSetup.EnsureMaterial(
                "M_ForestRune_Guard",
                new Color(0.08f, 0.7f, 0.88f),
                0.62f);
            Material forestSupply = M1ProjectSetup.EnsureMaterial(
                "M_ForestSupply",
                new Color(0.72f, 0.52f, 0.18f),
                0.28f);
            Material forestCover = M1ProjectSetup.EnsureMaterial(
                "M_ForestCoverProxy",
                new Color(0.19f, 0.25f, 0.22f),
                0.08f);
            AddForestGameplayTemplate(
                forestEmber,
                forestGuard,
                forestSupply,
                forestCover);
            AddCombatCoordinationAndTacticalShrine(neutralShrine, neutralWarning);

            PlayerSettings.bundleVersion = "0.5.0";
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Project Emberfall M3 ranged enemy slice completed.");
        }

        private static void AddMeleeEnemyToScene(
            string scenePath,
            Vector3 position,
            string enemyName,
            bool autoResetAfterDelay,
            TextAsset enemyDefinitions,
            GameObject characterPrefab,
            PlayerAnimationSet animationSet,
            Material enemyMaterial,
            Material weaponMaterial,
            Material gripMaterial,
            Material accentMaterial,
            bool preserveVisualMaterials)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            if (player == null) throw new InvalidDataException($"Scene has no configured player: {scenePath}");

            M1ProjectSetup.CreateMeleeEnemy(
                position,
                player,
                enemyDefinitions,
                characterPrefab,
                animationSet,
                enemyMaterial,
                weaponMaterial,
                gripMaterial,
                accentMaterial,
                enemyName,
                autoResetAfterDelay,
                preserveVisualMaterials);
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void AddCombatCoordinationAndTacticalShrine(
            Material shrineMaterial,
            Material warningMaterial)
        {
            Scene scene = EditorSceneManager.OpenScene(EmberValleyPath, OpenSceneMode.Single);
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            MeleeEnemyActor forestMelee = RequireComponent<MeleeEnemyActor>("Enemy_Fogwalker_Forest");
            RangedEnemyActor forestRanged = RequireComponent<RangedEnemyActor>("Enemy_RunePriest_Forest");
            ShieldEnemyActor courtyardShield = RequireComponent<ShieldEnemyActor>("Enemy_RuinGuard_Courtyard");
            MeleeEnemyActor courtyardMelee = RequireComponent<MeleeEnemyActor>("Enemy_Fogwalker_Courtyard_Support");
            RangedEnemyActor courtyardRanged = RequireComponent<RangedEnemyActor>("Enemy_RunePriest_Courtyard");
            if (player == null) throw new InvalidDataException("Ember Valley player is missing.");

            var forestCoordinatorObject = new GameObject(
                "[AI] Forest Combat Coordination",
                typeof(CombatEncounterCoordinator));
            forestCoordinatorObject.GetComponent<CombatEncounterCoordinator>().Configure(
                player,
                new[] { forestMelee },
                System.Array.Empty<ShieldEnemyActor>(),
                new[] { forestRanged },
                1,
                false,
                new Vector3(0f, 0f, 35f),
                new Vector2(8.5f, 8.5f),
                3.7f);

            var courtyardCoordinatorObject = new GameObject(
                "[AI] Courtyard Combat Coordination",
                typeof(CombatEncounterCoordinator));
            courtyardCoordinatorObject.GetComponent<CombatEncounterCoordinator>().Configure(
                player,
                new[] { courtyardMelee },
                new[] { courtyardShield },
                new[] { courtyardRanged },
                1,
                true,
                new Vector3(29f, 0f, 45f),
                new Vector2(9f, 7.5f),
                4.25f);

            CreateTacticalPostureShrine(player, shrineMaterial, warningMaterial);
            EditorSceneManager.SaveScene(scene, EmberValleyPath);
        }

        private static void CreateTacticalPostureShrine(
            PlayerCombatActor player,
            Material coreMaterial,
            Material warningMaterial)
        {
            var root = new GameObject(
                "TacticalPostureShrine_Courtyard",
                typeof(SphereCollider),
                typeof(TacticalPostureShrine));
            root.transform.position = new Vector3(28.5f, 0f, 44f);
            SphereCollider interactionCollider = root.GetComponent<SphereCollider>();
            interactionCollider.center = new Vector3(0f, 0.7f, 0f);
            interactionCollider.radius = 1.15f;

            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            core.name = "ShrineCore";
            core.transform.SetParent(root.transform, false);
            core.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            core.transform.localScale = new Vector3(1.15f, 0.45f, 1.15f);
            core.GetComponent<Renderer>().sharedMaterial = coreMaterial;
            Object.DestroyImmediate(core.GetComponent<Collider>());

            var warningSegments = new List<Renderer>();
            for (int i = 0; i < 14; i++)
            {
                GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"NeutralPostureWarning_{i:00}";
                segment.transform.SetParent(root.transform, false);
                segment.GetComponent<Renderer>().sharedMaterial = warningMaterial;
                Object.DestroyImmediate(segment.GetComponent<Collider>());
                warningSegments.Add(segment.GetComponent<Renderer>());
            }

            var lightObject = new GameObject("ShrineWarningLight", typeof(Light));
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            Light warningLight = lightObject.GetComponent<Light>();
            warningLight.type = LightType.Point;
            warningLight.range = 7f;
            warningLight.shadows = LightShadows.None;

            for (int i = 0; i < 3; i++)
            {
                float radians = i * Mathf.PI * 2f / 3f;
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = $"ShrineMarker_{i:00}";
                marker.transform.SetParent(root.transform, false);
                marker.transform.localPosition = new Vector3(Mathf.Cos(radians) * 0.92f, 1f, Mathf.Sin(radians) * 0.92f);
                marker.transform.localScale = new Vector3(0.18f, 1.25f, 0.18f);
                marker.transform.localRotation = Quaternion.Euler(0f, -i * 120f, 18f);
                marker.GetComponent<Renderer>().sharedMaterial = warningMaterial;
                Object.DestroyImmediate(marker.GetComponent<Collider>());
            }

            root.GetComponent<TacticalPostureShrine>().Configure(
                player,
                core.GetComponent<Renderer>(),
                warningSegments.ToArray(),
                warningLight,
                1.1f,
                4.6f,
                52f);
        }

        private static T RequireComponent<T>(string objectName) where T : Component
        {
            GameObject owner = GameObject.Find(objectName);
            T component = owner != null ? owner.GetComponent<T>() : null;
            if (component == null)
                throw new InvalidDataException($"Required scene component is missing: {objectName}/{typeof(T).Name}");
            return component;
        }

        private static void AddForestGameplayTemplate(
            Material ember,
            Material guard,
            Material supply,
            Material cover)
        {
            Scene scene = EditorSceneManager.OpenScene(EmberValleyPath, OpenSceneMode.Single);
            M2RouteFlowController flow = Object.FindObjectOfType<M2RouteFlowController>();
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            GameObject bearerObject = GameObject.Find("Enemy_Fogwalker_Forest");
            GameObject priestObject = GameObject.Find("Enemy_RunePriest_Forest");
            if (flow == null || player == null || bearerObject == null || priestObject == null)
            {
                throw new InvalidDataException("Forest gameplay template prerequisites are missing.");
            }

            MeleeEnemyActor bearer = bearerObject.GetComponent<MeleeEnemyActor>();
            RangedEnemyActor priest = priestObject.GetComponent<RangedEnemyActor>();
            bearer.SetAutoResetAfterDelay(false);
            priest.SetAutoResetAfterDelay(false);

            var coordinatorObject = new GameObject(
                "[Gameplay] Forest Seal Template",
                typeof(ForestSealTemplateCoordinator));
            ForestSealTemplateCoordinator coordinator =
                coordinatorObject.GetComponent<ForestSealTemplateCoordinator>();

            ForestTemplateInteractable sigil = CreateForestInteractable(
                coordinator,
                ForestTemplateInteractionRole.SigilPickup,
                "Pickup_ForestSigil",
                PrimitiveType.Sphere,
                new Vector3(0f, 0.65f, 37f),
                new Vector3(0.42f, 0.42f, 0.42f),
                ember);
            ForestTemplateInteractable cache = CreateForestInteractable(
                coordinator,
                ForestTemplateInteractionRole.SupplyCache,
                "SupplyCache_ForestBranch",
                PrimitiveType.Cube,
                new Vector3(-6.7f, 0.55f, 34.5f),
                new Vector3(1.1f, 1.1f, 1.1f),
                supply);
            ForestTemplateInteractable emberRune = CreateForestInteractable(
                coordinator,
                ForestTemplateInteractionRole.EmberRune,
                "RuneChoice_Ember",
                PrimitiveType.Cylinder,
                new Vector3(-2.2f, 0.5f, 25f),
                new Vector3(0.65f, 0.5f, 0.65f),
                ember);
            ForestTemplateInteractable guardRune = CreateForestInteractable(
                coordinator,
                ForestTemplateInteractionRole.GuardRune,
                "RuneChoice_Guard",
                PrimitiveType.Cylinder,
                new Vector3(2.2f, 0.5f, 25f),
                new Vector3(0.65f, 0.5f, 0.65f),
                guard);

            CreateForestCover(
                "CoverProxy_Forest_West",
                new Vector3(-2.8f, 1.05f, 31.7f),
                new Vector3(2.8f, 2.1f, 1.6f),
                cover);
            CreateForestCover(
                "CoverProxy_Forest_East",
                new Vector3(3.4f, 1.15f, 35.4f),
                new Vector3(2.5f, 2.3f, 1.8f),
                cover);

            GameObject shortcut = CreateForestCover(
                "GateBlocker_ForestShortcut",
                new Vector3(7.35f, 1.1f, 30.5f),
                new Vector3(0.7f, 2.2f, 4.6f),
                cover);
            GameObject restoredWorld = CreateRestoredForestWorld(ember, guard);

            coordinator.Configure(
                flow,
                player,
                bearer,
                priest,
                sigil,
                cache,
                emberRune,
                guardRune,
                shortcut,
                restoredWorld);
            flow.SetForestTemplate(coordinator);
            EditorSceneManager.SaveScene(scene, EmberValleyPath);
        }

        private static ForestTemplateInteractable CreateForestInteractable(
            ForestSealTemplateCoordinator coordinator,
            ForestTemplateInteractionRole role,
            string name,
            PrimitiveType primitive,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            GameObject owner = GameObject.CreatePrimitive(primitive);
            owner.name = name;
            owner.transform.position = position;
            owner.transform.localScale = scale;
            owner.GetComponent<Renderer>().sharedMaterial = material;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = name + "_Marker";
            marker.transform.SetParent(owner.transform, false);
            marker.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            marker.transform.localScale = Vector3.one * 0.5f;
            marker.transform.localRotation = Quaternion.Euler(35f, 45f, 35f);
            marker.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(marker.GetComponent<Collider>());

            var lightObject = new GameObject(name + "_Light", typeof(Light));
            lightObject.transform.SetParent(owner.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 1.25f, 0f);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = material.color;
            light.range = 4.5f;
            light.intensity = 1.8f;
            light.shadows = LightShadows.None;

            ForestTemplateInteractable interactable = owner.AddComponent<ForestTemplateInteractable>();
            interactable.Configure(coordinator, role, marker.transform);
            return interactable;
        }

        private static GameObject CreateForestCover(
            string name,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            GameObject cover = M1ProjectSetup.CreateCube(name, position, scale, material);
            NavMeshObstacle obstacle = cover.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
            return cover;
        }

        private static GameObject CreateRestoredForestWorld(Material ember, Material guard)
        {
            var root = new GameObject("ForestRestoredWorldState");
            Vector3[] positions =
            {
                new Vector3(0f, 2.2f, 25f),
                new Vector3(-5.2f, 2.4f, 32.5f),
                new Vector3(5.4f, 2.4f, 38.5f)
            };
            for (int i = 0; i < positions.Length; i++)
            {
                var lightObject = new GameObject($"RestoredForestLight_{i:00}", typeof(Light));
                lightObject.transform.SetParent(root.transform, false);
                lightObject.transform.position = positions[i];
                Light light = lightObject.GetComponent<Light>();
                light.type = LightType.Point;
                light.color = i == 1 ? guard.color : ember.color;
                light.range = 9f;
                light.intensity = 2.6f;
                light.shadows = LightShadows.Soft;

                GameObject mote = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                mote.name = $"RestoredForestMote_{i:00}";
                mote.transform.SetParent(root.transform, false);
                mote.transform.position = positions[i];
                mote.transform.localScale = Vector3.one * 0.24f;
                mote.GetComponent<Renderer>().sharedMaterial = i == 1 ? guard : ember;
                Object.DestroyImmediate(mote.GetComponent<Collider>());
            }

            return root;
        }

        private static void AddRunePriestToScene(
            string scenePath,
            Vector3 position,
            string enemyName,
            bool autoResetAfterDelay,
            TextAsset enemyDefinitions,
            GameObject characterPrefab,
            PlayerAnimationSet animationSet,
            Material runePriest,
            Material projectile,
            Material staff,
            bool preserveVisualMaterials)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            if (player == null)
            {
                throw new InvalidDataException($"Scene has no configured player: {scenePath}");
            }

            M1ProjectSetup.CreateRangedEnemy(
                position,
                player,
                enemyDefinitions,
                characterPrefab,
                animationSet,
                runePriest,
                projectile,
                staff,
                enemyName,
                autoResetAfterDelay,
                preserveVisualMaterials);
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        internal static void AddShieldGuardToScene(
            string scenePath,
            Vector3 position,
            string enemyName,
            bool autoResetAfterDelay,
            string replacedEnemyName,
            TextAsset enemyDefinitions,
            GameObject characterPrefab,
            PlayerAnimationSet animationSet,
            Material shieldMaterial,
            Material metalMaterial,
            bool preserveVisualMaterials)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            PlayerCombatActor player = Object.FindObjectOfType<PlayerCombatActor>();
            if (player == null)
            {
                throw new InvalidDataException($"Scene has no configured player: {scenePath}");
            }

            if (!string.IsNullOrEmpty(replacedEnemyName))
            {
                GameObject replaced = GameObject.Find(replacedEnemyName);
                if (replaced != null) Object.DestroyImmediate(replaced);
            }

            var enemy = new GameObject(
                enemyName,
                typeof(NavMeshAgent),
                typeof(CapsuleCollider),
                typeof(ShieldEnemyActor),
                typeof(ShieldEnemyAnimationPresenter));
            enemy.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));

            NavMeshAgent agent = enemy.GetComponent<NavMeshAgent>();
            agent.radius = 0.46f;
            agent.height = 2f;
            agent.acceleration = 11f;
            agent.autoBraking = true;
            agent.avoidancePriority = 38;

            CapsuleCollider bodyCollider = enemy.GetComponent<CapsuleCollider>();
            bodyCollider.center = new Vector3(0f, 1.05f, 0f);
            bodyCollider.height = 2f;
            bodyCollider.radius = 0.46f;

            GameObject visual = PrefabUtility.InstantiatePrefab(characterPrefab, enemy.transform) as GameObject;
            if (visual == null) throw new InvalidDataException("Could not instantiate ruin guard visual.");
            visual.name = "EnemyVisual_RuinGuard";
            visual.transform.localPosition = new Vector3(0f, 1.05f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            M1ProjectSetup.SetLayerRecursively(visual, 0);

            Renderer bodyRenderer = M1ProjectSetup.FindLargestRenderer(visual);
            if (bodyRenderer == null) throw new InvalidDataException("Ruin guard visual contains no renderer.");
            if (!preserveVisualMaterials)
            {
                bodyRenderer.sharedMaterial = metalMaterial;
            }

            Animator animator = visual.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
                throw new InvalidDataException("Ruin guard visual has no valid Humanoid Animator.");

            var aimPoint = new GameObject("AimPoint").transform;
            aimPoint.SetParent(enemy.transform, false);
            aimPoint.localPosition = new Vector3(0f, 1.7f, 0f);
            var attackOrigin = new GameObject("AttackOrigin").transform;
            attackOrigin.SetParent(enemy.transform, false);
            attackOrigin.localPosition = new Vector3(0f, 1.35f, 1.08f);

            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (rightHand != null)
            {
                Transform socket = M1ProjectSetup.CreateWorldScaleSocket(
                    rightHand,
                    "WeaponSocket_RightHand",
                    new Vector3(0f, 0.015f, 0f),
                    Quaternion.identity);
                M1ProjectSetup.CreateSword(socket, metalMaterial, shieldMaterial, metalMaterial);
            }

            Renderer shieldRenderer = AttachWoodenShield(animator, shieldMaterial, metalMaterial);
            ShieldEnemyActor actor = enemy.GetComponent<ShieldEnemyActor>();
            actor.Configure(
                enemyDefinitions,
                "enemy:ruin-guard",
                player,
                agent,
                aimPoint,
                attackOrigin,
                bodyRenderer,
                shieldRenderer,
                bodyCollider);
            actor.SetAutoResetAfterDelay(autoResetAfterDelay);
            enemy.GetComponent<ShieldEnemyAnimationPresenter>().Configure(animator, actor, animationSet);
            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static Renderer AttachWoodenShield(
            Animator animator,
            Material woodMaterial,
            Material metalMaterial)
        {
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (leftHand == null) throw new InvalidDataException("Ruin guard has no left-hand bone.");

            Transform socket = M1ProjectSetup.CreateWorldScaleSocket(
                leftHand,
                "ShieldSocket_LeftHand",
                new Vector3(0.02f, 0.05f, 0.03f),
                Quaternion.Euler(5f, 92f, 88f));

            GameObject source = LoadRequired<GameObject>(WoodenShieldPath);
            GameObject shield = PrefabUtility.InstantiatePrefab(source, socket) as GameObject;
            if (shield == null) throw new InvalidDataException("Could not instantiate wooden shield art.");
            shield.name = "Shield_Wooden_Equipped";
            shield.transform.localPosition = Vector3.zero;
            shield.transform.localRotation = Quaternion.identity;
            shield.transform.localScale = Vector3.one * 0.72f;
            foreach (Collider collider in shield.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            Renderer primary = null;
            foreach (Renderer renderer in shield.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    string sourceName = slots[i] != null ? slots[i].name : string.Empty;
                    slots[i] = sourceName.IndexOf("metal", System.StringComparison.OrdinalIgnoreCase) >= 0
                        ? metalMaterial : woodMaterial;
                }
                renderer.sharedMaterials = slots;
                if (primary == null) primary = renderer;
            }
            return primary;
        }

        private static Material EnsureRunePriestMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(RunePriestMaterialPath);
            if (material == null)
            {
                Material fogwalker = LoadRequired<Material>(FogwalkerMaterialPath);
                material = new Material(fogwalker) { name = "M_Character_RunePriest" };
                AssetDatabase.CreateAsset(material, RunePriestMaterialPath);
            }

            material.color = new Color(0.58f, 0.34f, 0.78f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static T LoadRequired<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new FileNotFoundException($"Required M3 asset is missing: {path}", path);
            }

            return asset;
        }
    }
}
