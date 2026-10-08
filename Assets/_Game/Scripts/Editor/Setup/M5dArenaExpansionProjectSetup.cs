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
                // M6 owns the approved kit walls. Do not create proxy faces that it immediately destroys.
            }

            // Lift connector decks by 2 cm where they overlap the larger zone floors. The offset is
            // invisible in play but prevents coplanar depth fighting at both seams.
            ResizeSurface("Path_Bridge", new Vector3(13f, -0.23f, 44.8f), new Vector3(8.5f, 0.5f, 11f));
            ResizeSurface("Path_Sanctum", new Vector3(39f, -0.23f, 31f), new Vector3(10.5f, 0.5f, 7.8f));
            MoveActor("Enemy_RunePriest_Bridge_Left", new Vector3(10.7f, 0f, 46.1f));
            MoveActor("Enemy_RunePriest_Bridge_Right", new Vector3(14.5f, 0f, 42.2f));

            M5dArenaRepair.ApplyToLoadedScene();
            M6EnvironmentSetup.ApplyToScene(scene);

            NavMeshSurface surface = UnityEngine.Object.FindObjectOfType<NavMeshSurface>();
            if (surface == null) throw new InvalidDataException("Ember Valley NavMesh surface is missing.");
            M6EnvironmentSetup.BakeNavigation(scene);
            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            PlayerSettings.bundleVersion = M5NetworkingProjectSetup.Version;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("EMBERFALL_M5D_ARENA_EXPANSION_COMPLETE version=0.8.10 areas=1.5x boundaries=M6-kit sweep=offline");
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
