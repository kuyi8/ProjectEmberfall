using System;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Editor.Content;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Unity;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    /// <summary>Builds the 0.8.8 offline encounter-boundary and pacing-governance pass.</summary>
    public static class M5cRouteEnrichmentProjectSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/10_EmberValley.unity";
        private const string AnimationSetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        private const string EnemyDefinitionsPath = "Assets/_Game/Data/M2/enemies.v1.json";
        private const string MeleePrefabPath = "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_FogwalkerSkeleton.prefab";
        private const string RangedPrefabPath = "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_RunePriest.prefab";
        private const string MaterialRoot = "Assets/_Game/Art/Materials";
        private const string RuneMeshPath = "Assets/_Game/Art/Meshes/M3Art/M_RuneOctahedron.asset";
        private const string RuneRingMeshPath = "Assets/_Game/Art/Meshes/M3Art/M_RuneHighlightRing.asset";
        private const string RouteGlowMaterialPath = "Assets/_Game/Art/Materials/M3Art/M_Art_RouteGlow.mat";
        private static readonly Vector3 BridgeMechanismAPosition = new Vector3(8f, 0.4f, 43.4f);
        private static readonly Vector3 BridgeMechanismBPosition = new Vector3(15.2f, 0.4f, 48.2f);

        [MenuItem("Emberfall/Setup/Apply M5c 0.8.8 Encounter Boundaries")]
        public static void Apply()
        {
            RemovePreviousContent();
            AddNormalShieldGuard();

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            PlayerCombatActor player = UnityEngine.Object.FindObjectOfType<PlayerCombatActor>();
            M2RouteFlowController flow = UnityEngine.Object.FindObjectOfType<M2RouteFlowController>();
            if (player == null || flow == null) throw new InvalidDataException("Ember Valley route dependencies are missing.");

            TextAsset definitions = LoadRequired<TextAsset>(EnemyDefinitionsPath);
            PlayerAnimationSet animations = LoadRequired<PlayerAnimationSet>(AnimationSetPath);
            GameObject meleeVisual = LoadRequired<GameObject>(MeleePrefabPath);
            GameObject rangedVisual = LoadRequired<GameObject>(RangedPrefabPath);
            Material fogwalker = LoadRequired<Material>(MaterialRoot + "/M6Art/M_M6_FogwalkerBone.mat");
            Material priest = LoadRequired<Material>(MaterialRoot + "/M6Art/M_M6_RunePriest.mat");
            Material projectile = LoadRequired<Material>(MaterialRoot + "/M1/M_RuneProjectile.mat");
            Material staff = LoadRequired<Material>(MaterialRoot + "/M1/M_RuneStaff.mat");
            Material weapon = LoadRequired<Material>(MaterialRoot + "/M1/M_Weapon.mat");
            Material grip = LoadRequired<Material>(MaterialRoot + "/M1/M_WeaponGrip.mat");
            Material accent = LoadRequired<Material>(MaterialRoot + "/M1/M_CombatAccent.mat");
            Material stone = M1ProjectSetup.EnsureMaterial("M_RouteEnrichmentStone", new Color(0.16f, 0.19f, 0.2f), 0.2f);
            Material ember = M1ProjectSetup.EnsureMaterial("M_RouteEnrichmentEmber", new Color(0.9f, 0.24f, 0.035f), 0.58f);
            Material supply = M1ProjectSetup.EnsureMaterial("M_RouteEnrichmentSupply", new Color(0.12f, 0.62f, 0.78f), 0.52f);

            var root = new GameObject("[M5c] Route Enrichment 0.8.8");
            CreateWatchtowerBranch(root.transform, flow, stone, ember);
            CreateRouteChoices(root.transform, flow, stone, supply, ember);
            CreateBridgeMechanisms(root.transform, flow, stone, ember);

            RangedEnemyActor bridgeLeft = M1ProjectSetup.CreateRangedEnemy(
                new Vector3(10.1f, 0f, 45.8f), player, definitions, rangedVisual, animations,
                priest, projectile, staff, "Enemy_RunePriest_Bridge_Left", false, true)
                .GetComponent<RangedEnemyActor>();
            RangedEnemyActor bridgeRight = M1ProjectSetup.CreateRangedEnemy(
                new Vector3(13.9f, 0f, 42.2f), player, definitions, rangedVisual, animations,
                priest, projectile, staff, "Enemy_RunePriest_Bridge_Right", false, true)
                .GetComponent<RangedEnemyActor>();
            bridgeLeft.transform.SetParent(root.transform);
            bridgeRight.transform.SetParent(root.transform);

            MeleeEnemyActor preLeft = M1ProjectSetup.CreateMeleeEnemy(
                new Vector3(36.7f, 0f, 31.8f), player, definitions, meleeVisual, animations,
                fogwalker, weapon, grip, accent, "Enemy_Fogwalker_PreSanctum_Left", false, true)
                .GetComponent<MeleeEnemyActor>();
            MeleeEnemyActor preRight = M1ProjectSetup.CreateMeleeEnemy(
                new Vector3(40.7f, 0f, 31.8f), player, definitions, meleeVisual, animations,
                fogwalker, weapon, grip, accent, "Enemy_Fogwalker_PreSanctum_Right", false, true)
                .GetComponent<MeleeEnemyActor>();
            preLeft.transform.SetParent(root.transform);
            preRight.transform.SetParent(root.transform);
            ShieldEnemyActor preGuard = GameObject.Find("Enemy_RuinGuard_PreSanctum")
                ?.GetComponent<ShieldEnemyActor>() ?? throw new InvalidDataException("Pre-sanctum guard was not created.");
            preGuard.transform.SetParent(root.transform);

            ConfigureExistingEncounterBoundaries();
            CompactCourtyardRoster();

            CombatEncounterCoordinator bridge = CreateEncounter(
                "[AI] Broken Bridge Combat Coordination", player,
                Array.Empty<MeleeEnemyActor>(), Array.Empty<ShieldEnemyActor>(),
                new[] { bridgeLeft, bridgeRight }, 1, new Vector3(12.4f, 0f, 44.8f),
                new Vector2(3.2f, 4.2f), "bridge-encounter", 0.4f);
            bridge.transform.SetParent(root.transform);

            CombatEncounterCoordinator preSanctum = CreateEncounter(
                "[AI] Pre-Sanctum Combat Coordination", player,
                new[] { preLeft, preRight }, new[] { preGuard }, Array.Empty<RangedEnemyActor>(),
                1, new Vector3(39f, 0f, 31f), new Vector2(4f, 3f), "pre-sanctum-encounter", 0.5f);
            preSanctum.transform.SetParent(root.transform);
            root.AddComponent<RouteChoiceEncounterModifier>().Configure(flow, preSanctum);

            NavMeshSurface surface = UnityEngine.Object.FindObjectOfType<NavMeshSurface>();
            if (surface == null) throw new InvalidDataException("Ember Valley NavMesh surface is missing.");
            surface.BuildNavMesh();
            if (surface.navMeshData == null) throw new InvalidOperationException("Route enrichment NavMesh bake failed.");

            EditorUtility.SetDirty(flow);
            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            M6ArtProjectSetup.ReplaceCombatEquipment(
                ScenePath,
                LoadRequired<GameObject>("Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Skeleton_ShortSword.prefab"),
                LoadRequired<GameObject>("Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Warden_RuneSword.prefab"),
                LoadRequired<GameObject>("Assets/_Game/Prefabs/Weapons/M6Art/P_M6_RuinGuard_SkullShield.prefab"),
                LoadRequired<GameObject>("Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Warden_RoundShield.prefab"));
            M4BuiltinContentPackager.BuildBuiltinPackage();
            M5NetworkingProjectSetup.Apply();
            PlayerSettings.bundleVersion = M5NetworkingProjectSetup.Version;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "EMBERFALL_M5C_ROUTE_ENRICHMENT_COMPLETE version=0.8.8 " +
                "encounterBounds=forest(0,35;6.3,6.0;0.6),bridge(12.4,44.8;3.2,4.2;0.4)," +
                "courtyard(29,45;7.5,6.2;0.6),pre-sanctum(39,31;4,3;0.5) " +
                "bridgeA=(8,0.4,43.4) courtyardRoster=compacted");
        }

        private static void RemovePreviousContent()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (string name in new[]
                     {
                         "[M5c] Route Enrichment 0.8.6", "[M5c] Route Enrichment 0.8.7",
                         "[M5c] Route Enrichment 0.8.7a", "[M5c] Route Enrichment 0.8.8",
                         "Enemy_RuinGuard_PreSanctum",
                         "Enemy_RunePriest_Bridge_Left", "Enemy_RunePriest_Bridge_Right",
                         "Enemy_Fogwalker_PreSanctum_Left", "Enemy_Fogwalker_PreSanctum_Right"
                     })
            {
                GameObject existing = GameObject.Find(name);
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
            }
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void AddNormalShieldGuard()
        {
            M3ProjectSetup.AddShieldGuardToScene(
                ScenePath,
                new Vector3(38.5f, 0f, 29.8f),
                "Enemy_RuinGuard_PreSanctum",
                false,
                null,
                LoadRequired<TextAsset>(EnemyDefinitionsPath),
                LoadRequired<GameObject>(MeleePrefabPath),
                LoadRequired<PlayerAnimationSet>(AnimationSetPath),
                LoadRequired<Material>(MaterialRoot + "/M1/M_RuinGuard_Shield.mat"),
                LoadRequired<Material>(MaterialRoot + "/M1/M_RuinGuard_Metal.mat"),
                true);
        }

        private static CombatEncounterCoordinator CreateEncounter(
            string name,
            PlayerCombatActor player,
            MeleeEnemyActor[] melee,
            ShieldEnemyActor[] shields,
            RangedEnemyActor[] ranged,
            int quota,
            Vector3 center,
            Vector2 halfExtents,
            string telemetrySegment,
            float telemetryActivationMargin)
        {
            var gameObject = new GameObject(name, typeof(CombatEncounterCoordinator));
            CombatEncounterCoordinator coordinator = gameObject.GetComponent<CombatEncounterCoordinator>();
            coordinator.Configure(
                player, melee, shields, ranged, quota, true, center, halfExtents, 3.8f,
                telemetrySegment, telemetryActivationMargin);
            return coordinator;
        }

        private static void ConfigureExistingEncounterBoundaries()
        {
            CombatEncounterCoordinator[] coordinators = UnityEngine.Object.FindObjectsOfType<CombatEncounterCoordinator>();
            CombatEncounterCoordinator forest = coordinators.Single(item => item.TelemetrySegment == "forest-encounter");
            CombatEncounterCoordinator courtyard = coordinators.Single(item => item.TelemetrySegment == "courtyard-encounter");
            forest.ConfigureTelemetryArena(new Vector3(0f, 0f, 35f), new Vector2(6.3f, 6f), 0.6f);
            courtyard.ConfigureTelemetryArena(new Vector3(29f, 0f, 45f), new Vector2(7.5f, 6.2f), 0.6f);
            EditorUtility.SetDirty(forest);
            EditorUtility.SetDirty(courtyard);
        }

        private static void CompactCourtyardRoster()
        {
            SetActorPosition("Enemy_RuinGuard_Courtyard", new Vector3(26.5f, 0f, 43.5f));
            SetActorPosition("Enemy_Fogwalker_Courtyard_Support", new Vector3(29.5f, 0f, 44.5f));
            SetActorPosition("Enemy_RunePriest_Courtyard", new Vector3(32f, 0f, 46f));
        }

        private static void SetActorPosition(string name, Vector3 position)
        {
            GameObject actor = GameObject.Find(name) ?? throw new InvalidDataException($"Encounter actor is missing: {name}");
            actor.transform.position = position;
            EditorUtility.SetDirty(actor.transform);
        }

        private static void CreateWatchtowerBranch(
            Transform parent,
            M2RouteFlowController flow,
            Material stone,
            Material ember)
        {
            CreateBox("OldWatchtower_Platform", new Vector3(-13f, -0.25f, 38f), new Vector3(8f, 0.5f, 8f), stone, parent);
            CreateBox("OldWatchtower_Base", new Vector3(-14.5f, 0.75f, 39.5f), new Vector3(3f, 1.5f, 3f), stone, parent);
            CreateBox("OldWatchtower_Pillar_A", new Vector3(-15.6f, 2.7f, 40.6f), new Vector3(0.5f, 5.4f, 0.5f), stone, parent);
            CreateBox("OldWatchtower_Pillar_B", new Vector3(-13.4f, 2.7f, 40.6f), new Vector3(0.5f, 5.4f, 0.5f), stone, parent);
            CreateInteraction(
                "OldWatchtower_Discovery", new Vector3(-13.4f, 0.35f, 37.5f),
                flow, RouteEnrichmentInteractionKind.Watchtower, ember, parent);
        }

        private static void CreateRouteChoices(
            Transform parent,
            M2RouteFlowController flow,
            Material stone,
            Material supply,
            Material risk)
        {
            CreateBox("RouteChoice_Dais", new Vector3(17f, -0.15f, 44f), new Vector3(3.8f, 0.3f, 6f), stone, parent);
            CreateInteraction(
                "RouteChoice_Supply", new Vector3(17f, 0.3f, 42.2f),
                flow, RouteEnrichmentInteractionKind.SupplyRoute, supply, parent);
            CreateInteraction(
                "RouteChoice_Risk", new Vector3(17f, 0.3f, 45.8f),
                flow, RouteEnrichmentInteractionKind.RiskRoute, risk, parent);
        }

        private static void CreateBridgeMechanisms(
            Transform parent,
            M2RouteFlowController flow,
            Material stone,
            Material ember)
        {
            CreateBox("BridgeMechanism_A_Base", BridgeMechanismAPosition + new Vector3(0f, -0.2f, 0f), new Vector3(1.4f, 0.4f, 1.4f), stone, parent);
            CreateBridgeMechanism(
                "BridgeMechanism_A", BridgeMechanismAPosition,
                "bridge-mechanism:A", flow, ember, parent);

            CreateBox("BridgeMechanism_B_Base", BridgeMechanismBPosition + new Vector3(0f, -0.2f, 0f), new Vector3(1.4f, 0.4f, 1.4f), stone, parent);
            CreateBridgeMechanism(
                "BridgeMechanism_B", BridgeMechanismBPosition,
                "bridge-mechanism:B", flow, ember, parent);
            CreateBridgeGuidance(parent, flow);
        }

        private static void CreateBridgeMechanism(
            string name,
            Vector3 position,
            string stableId,
            M2RouteFlowController flow,
            Material material,
            Transform parent)
        {
            var root = new GameObject(name, typeof(SphereCollider), typeof(BridgeMechanismInteractable));
            root.transform.SetParent(parent);
            root.transform.position = position;
            SphereCollider collider = root.GetComponent<SphereCollider>();
            collider.radius = 1.25f;
            collider.center = new Vector3(0f, 0.65f, 0f);
            collider.isTrigger = true;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "MechanismMarker";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            marker.transform.localScale = new Vector3(0.48f, 0.65f, 0.48f);
            Renderer renderer = marker.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            M2QuestHighlightPresenter highlight = CreateRouteHighlight(root.transform, name);
            root.GetComponent<BridgeMechanismInteractable>().Configure(flow, stableId, renderer, highlight);
        }

        private static void CreateInteraction(
            string name,
            Vector3 position,
            M2RouteFlowController flow,
            RouteEnrichmentInteractionKind kind,
            Material material,
            Transform parent)
        {
            var root = new GameObject(name, typeof(SphereCollider), typeof(RouteEnrichmentInteractable));
            root.transform.SetParent(parent);
            root.transform.position = position;
            SphereCollider collider = root.GetComponent<SphereCollider>();
            collider.radius = 1.15f;
            collider.center = new Vector3(0f, 0.7f, 0f);
            collider.isTrigger = true;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "InteractionMarker";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            marker.transform.localScale = new Vector3(0.42f, 0.7f, 0.42f);
            Renderer renderer = marker.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            M2QuestHighlightPresenter highlight = CreateRouteHighlight(root.transform, name);
            root.GetComponent<RouteEnrichmentInteractable>().Configure(flow, kind, renderer, highlight);
        }

        private static M2QuestHighlightPresenter CreateRouteHighlight(Transform owner, string name)
        {
            Mesh runeMesh = LoadRequired<Mesh>(RuneMeshPath);
            Mesh ringMesh = LoadRequired<Mesh>(RuneRingMeshPath);
            Material glow = LoadRequired<Material>(RouteGlowMaterialPath);

            var core = new GameObject(name + "_RuneIcon", typeof(MeshFilter), typeof(MeshRenderer));
            core.transform.SetParent(owner, false);
            core.transform.localPosition = new Vector3(0f, 1.72f, 0f);
            core.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            core.transform.localScale = Vector3.one * 0.24f;
            core.GetComponent<MeshFilter>().sharedMesh = runeMesh;
            Renderer coreRenderer = core.GetComponent<Renderer>();
            coreRenderer.sharedMaterial = glow;

            var ring = new GameObject(name + "_HighlightRing", typeof(MeshFilter), typeof(MeshRenderer));
            ring.transform.SetParent(owner, false);
            ring.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            ring.transform.localScale = Vector3.one * 0.9f;
            ring.GetComponent<MeshFilter>().sharedMesh = ringMesh;
            Renderer ringRenderer = ring.GetComponent<Renderer>();
            ringRenderer.sharedMaterial = glow;

            var lightObject = new GameObject(name + "_HighlightLight", typeof(Light));
            lightObject.transform.SetParent(owner, false);
            lightObject.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.48f, 0.08f);
            light.range = 4.2f;
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;

            M2QuestHighlightPresenter presenter = core.AddComponent<M2QuestHighlightPresenter>();
            presenter.Configure(coreRenderer, ringRenderer, light);
            return presenter;
        }

        private static void CreateBridgeGuidance(Transform parent, M2RouteFlowController flow)
        {
            var controller = new GameObject("BridgeMechanism_Guidance", typeof(BridgeMechanismGuidancePresenter));
            controller.transform.SetParent(parent);
            var visualRoot = new GameObject("GuidanceVisual");
            visualRoot.transform.SetParent(controller.transform, false);
            Material glow = LoadRequired<Material>(RouteGlowMaterialPath);
            Vector3 start = new Vector3(BridgeMechanismAPosition.x, 0.055f, BridgeMechanismAPosition.z);
            Vector3 end = new Vector3(BridgeMechanismBPosition.x, 0.055f, BridgeMechanismBPosition.z);
            Vector3 delta = end - start;
            float segmentLength = delta.magnitude / 3f;
            Quaternion rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            for (int i = 0; i < 3; i++)
            {
                GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"GuidanceSegment_{i + 1}";
                segment.transform.SetParent(visualRoot.transform);
                segment.transform.position = Vector3.Lerp(start, end, (i + 0.5f) / 3f);
                segment.transform.rotation = rotation;
                segment.transform.localScale = new Vector3(0.13f, 0.025f, segmentLength * 0.78f);
                segment.GetComponent<Renderer>().sharedMaterial = glow;
                UnityEngine.Object.DestroyImmediate(segment.GetComponent<Collider>());
            }

            controller.GetComponent<BridgeMechanismGuidancePresenter>().Configure(flow, visualRoot);
        }

        private static void CreateBox(
            string name,
            Vector3 position,
            Vector3 scale,
            Material material,
            Transform parent)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent);
            box.transform.position = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static T LoadRequired<T>(string path) where T : UnityEngine.Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException("Required asset is missing.", path);
    }
}
