using System;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    /// <summary>Builds the approved 0.8.10 encounter-space and visible-boundary pass.</summary>
    public static class M5dArenaExpansionProjectSetup
    {
        private const string ScenePath = "Assets/_Game/Scenes/10_EmberValley.unity";
        private const string RootName = "[M5d] Arena Expansion 0.8.10";
        private const string WallPath =
            "Assets/_Game/Art/DownloadResources/UnityFreeAssets/07_Environment_Ruins/Updated_Modular_Dungeon/FBX/Wall_Modular.fbx";
        private const string FencePath =
            "Assets/_Game/Art/DownloadResources/UnityFreeAssets/07_Environment_Ruins/Updated_Modular_Dungeon/FBX/Fence_Straight_Modular.fbx";

        private readonly struct ArenaSpec
        {
            public ArenaSpec(string segment, Vector3 center, Vector2 halfExtents, float margin)
            {
                Segment = segment;
                Center = center;
                HalfExtents = halfExtents;
                Margin = margin;
            }

            public string Segment { get; }
            public Vector3 Center { get; }
            public Vector2 HalfExtents { get; }
            public float Margin { get; }
        }

        [MenuItem("Emberfall/Setup/Apply M5d 0.8.10 Arena Expansion")]
        public static void Apply()
        {
            M5cRouteEnrichmentProjectSetup.Apply();
            M1ProjectSetup.EnsureInputActions();
            M1ProjectSetup.EnsureCombatTuning();
            M1AnimationSetup.Apply();

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject prior = GameObject.Find(RootName);
            if (prior != null) UnityEngine.Object.DestroyImmediate(prior);
            var root = new GameObject(RootName);

            ArenaSpec[] specs =
            {
                new ArenaSpec("forest-encounter", new Vector3(0f, 0f, 35f), new Vector2(7.72f, 7.35f), 0.6f),
                new ArenaSpec("bridge-encounter", new Vector3(13f, 0f, 44.8f), new Vector2(3.92f, 5.15f), 0.4f),
                new ArenaSpec("courtyard-encounter", new Vector3(29f, 0f, 45f), new Vector2(9.19f, 7.60f), 0.6f),
                new ArenaSpec("pre-sanctum-encounter", new Vector3(39f, 0f, 31f), new Vector2(4.90f, 3.68f), 0.5f)
            };

            CombatEncounterCoordinator[] coordinators = UnityEngine.Object.FindObjectsOfType<CombatEncounterCoordinator>();
            foreach (ArenaSpec spec in specs)
            {
                CombatEncounterCoordinator coordinator = coordinators.Single(item => item.TelemetrySegment == spec.Segment);
                coordinator.ConfigureTelemetryArena(spec.Center, spec.HalfExtents, spec.Margin);
                EditorUtility.SetDirty(coordinator);
                CreateVisibleBoundaries(root.transform, spec);
            }

            // Lift connector decks by 2 cm where they overlap the larger zone floors. The offset is
            // invisible in play but prevents coplanar depth fighting at both seams.
            ResizeSurface("Path_Bridge", new Vector3(13f, -0.23f, 44.8f), new Vector3(8.5f, 0.5f, 11f));
            ResizeSurface("Path_Sanctum", new Vector3(39f, -0.23f, 31f), new Vector3(10.5f, 0.5f, 7.8f));
            MoveActor("Enemy_RunePriest_Bridge_Left", new Vector3(10.7f, 0f, 46.1f));
            MoveActor("Enemy_RunePriest_Bridge_Right", new Vector3(14.5f, 0f, 42.2f));

            M5dArenaRepair.ApplyToLoadedScene();

            NavMeshSurface surface = UnityEngine.Object.FindObjectOfType<NavMeshSurface>();
            if (surface == null) throw new InvalidDataException("Ember Valley NavMesh surface is missing.");
            surface.BuildNavMesh();
            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            PlayerSettings.bundleVersion = M5NetworkingProjectSetup.Version;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("EMBERFALL_M5D_ARENA_EXPANSION_COMPLETE version=0.8.10 areas=1.5x faces=16 sweep=offline");
        }

        private static void CreateVisibleBoundaries(Transform parent, ArenaSpec spec)
        {
            Transform arenaRoot = new GameObject($"Boundary_{spec.Segment}").transform;
            arenaRoot.SetParent(parent);
            CreateFace(arenaRoot, spec, EncounterBoundaryFace.North, WallPath);
            CreateFace(arenaRoot, spec, EncounterBoundaryFace.South, WallPath);
            CreateFace(arenaRoot, spec, EncounterBoundaryFace.East, FencePath);
            CreateFace(arenaRoot, spec, EncounterBoundaryFace.West, FencePath);
        }

        private static void CreateFace(
            Transform parent,
            ArenaSpec spec,
            EncounterBoundaryFace face,
            string modelPath)
        {
            bool horizontal = face == EncounterBoundaryFace.North || face == EncounterBoundaryFace.South;
            float edgeLength = horizontal ? spec.HalfExtents.x * 0.82f : spec.HalfExtents.y * 0.82f;
            float side = face == EncounterBoundaryFace.North || face == EncounterBoundaryFace.East ? 1f : -1f;
            Vector3 position = horizontal
                ? spec.Center + new Vector3(-spec.HalfExtents.x * 0.48f, 0.42f, side * spec.HalfExtents.y)
                : spec.Center + new Vector3(side * spec.HalfExtents.x, 0.42f, spec.HalfExtents.y * 0.48f);

            var root = new GameObject($"{spec.Segment}_{face}_VisibleBoundary");
            root.transform.SetParent(parent);
            root.transform.position = position;
            root.transform.rotation = horizontal ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(edgeLength, 1.8f, 0.5f);
            collider.center = new Vector3(0f, 0.45f, 0f);

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new FileNotFoundException("Boundary model is missing.", modelPath);
            GameObject visual = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (visual == null) throw new InvalidOperationException($"Could not instantiate boundary model: {modelPath}");
            visual.name = "Visual_" + face;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            Renderer renderer = visual.GetComponentInChildren<Renderer>();
            if (renderer == null) throw new InvalidDataException($"Boundary model has no renderer: {modelPath}");
            Vector3 size = renderer.bounds.size;
            float xScale = size.x > 0.001f ? edgeLength / size.x : 1f;
            float yScale = size.y > 0.001f ? 1.8f / size.y : 1f;
            visual.transform.localScale = new Vector3(xScale, yScale, Mathf.Min(xScale, yScale));
            root.AddComponent<EncounterBoundaryVisualMarker>().Configure(spec.Segment, face, renderer);
        }

        private static void ResizeSurface(string name, Vector3 position, Vector3 scale)
        {
            GameObject surface = GameObject.Find(name) ?? throw new InvalidDataException($"Route surface is missing: {name}");
            surface.transform.position = position;
            surface.transform.localScale = scale;
            EditorUtility.SetDirty(surface.transform);
        }

        private static void MoveActor(string name, Vector3 position)
        {
            GameObject actor = GameObject.Find(name) ?? throw new InvalidDataException($"Actor is missing: {name}");
            actor.transform.position = position;
            EditorUtility.SetDirty(actor.transform);
        }
    }
}
