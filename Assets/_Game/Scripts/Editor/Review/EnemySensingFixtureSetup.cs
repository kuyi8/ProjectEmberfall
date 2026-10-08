using System;
using System.IO;
using System.Linq;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Author isolated initial spawns BEFORE Play. All production AI, gates and coordinators remain intact.</summary>
    [InitializeOnLoad]
    public static class EnemySensingFixtureSetup
    {
        const string Source = "Assets/_Game/Scenes/10_EmberValley.unity";
        public const string SessionKey = "Emberfall.EnemySensingFixtureRoot";
        const string PreparedKey = "Emberfall.EnemySensingFixturePrepared";
        const string CleanupKey = "Emberfall.EnemySensingFixtureCleanupPending";
        static EnemySensingFixtureSetup() { EditorApplication.update += CleanupWhenIdle; }
        public static void RequestCleanup() { SessionState.SetBool(CleanupKey, true); }
        static void CleanupWhenIdle()
        {
            if (!SessionState.GetBool(CleanupKey, false) || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling) return;
            SessionState.SetBool(CleanupKey, false);
            Cleanup();
        }
        static readonly string[] Names = { "Enemy_RunePriest_Bridge_Left", "Enemy_RunePriest_Bridge_Right",
            "Enemy_Fogwalker_PreSanctum_Left", "Enemy_Fogwalker_PreSanctum_Right", "Enemy_RuinGuard_PreSanctum",
            "Enemy_RunePriest_Forest", "Enemy_RunePriest_Courtyard" };
        static readonly Vector3[] Front = { new Vector3(10.7f,0,40.6f), new Vector3(13.3f,0,40f),
            new Vector3(36.7f,0,28.6f), new Vector3(40.7f,0,28.6f), new Vector3(38.5f,0,27.9f),
            new Vector3(6.8f,0,37.3f), new Vector3(32f,0,42.8f) };
        static readonly Vector3[] Back = { new Vector3(10.7f,0,48f), new Vector3(13.3f,0,44.9f),
            new Vector3(38.55f,0,31.95f), new Vector3(38.85f,0,31.95f), new Vector3(38.5f,0,31.8f),
            new Vector3(6.8f,0,42.1f), new Vector3(32f,0,47.9f) };

        public static string Prepare()
        {
            var original = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || original.isDirty ||
                original.path != Source || UnityEngine.SceneManagement.SceneManager.sceneCount != 1 ||
                !string.IsNullOrEmpty(SessionState.GetString(SessionKey, "")))
                throw new InvalidOperationException("Clean, idle Valley required; previous fixtures must be cleaned first.");
            string folder = "Assets/_Game/Scenes/Review/__P0Sensing_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder); AssetDatabase.Refresh();
            SessionState.SetString(SessionKey, folder);
            SessionState.SetBool(PreparedKey, false);
            try
            {
                for (int i = 0; i < Names.Length; i++) for (int rear = 0; rear < 2; rear++)
                {
                    var scene = EditorSceneManager.OpenScene(Source);
                    string path = folder + "/" + Names[i] + (rear == 0 ? "_Front" : "_Back") + ".unity";
                    if (!EditorSceneManager.SaveScene(scene, path, true)) throw new IOException("Fixture copy failed.");
                    scene = EditorSceneManager.OpenScene(path);
                    var player = Object.FindObjectOfType<PlayerCombatActor>();
                    var enemy = Object.FindObjectsOfType<CombatTarget>().Single(t => t.name == Names[i]);
                    var controller = player.GetComponent<CharacterController>();
                    Vector3 requested = rear == 0 ? Front[i] : Back[i];
                    if (!NavMesh.SamplePosition(requested, out var hit, .8f, NavMesh.AllAreas))
                        throw new InvalidOperationException("No fixture floor: " + path);
                    Vector3 spawn = hit.position + Vector3.up * (controller.height * .5f - controller.center.y + .05f);
                    Quaternion facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(enemy.transform.position - spawn, Vector3.up));
                    player.transform.SetPositionAndRotation(spawn, facing);
                    var flow = new SerializedObject(Object.FindObjectOfType<M2RouteFlowController>());
                    var checkpoint = (Transform)flow.FindProperty("_defaultCheckpoint").objectReferenceValue;
                    checkpoint.SetPositionAndRotation(spawn, facing);
                    // Normal initialization restores this authored fixture spawn. No runtime relocation/phase injection.
                    var camera = Camera.main;
                    camera.transform.SetPositionAndRotation(spawn - facing * Vector3.forward * 5.8f + Vector3.up * 2.5f,
                        facing * Quaternion.Euler(18, 0, 0));
                    EditorSceneManager.SaveScene(scene);
                }
                foreach (string name in new[] { Names[0], Names[1], Names[5], Names[6] })
                    PrepareCornerFixture(folder, name);
                PrepareEntryFixture(folder, "BridgeEntry", new Vector3(12.3f, 0f, 39.65f), Vector3.forward);
                PrepareEntryFixture(folder, "PreSanctumEntry", new Vector3(35.2f, 0f, 31f), Vector3.right);
                SessionState.SetBool(PreparedKey, true);
            }
            finally { EditorSceneManager.OpenScene(Source); }
            return folder;
        }

        static void PrepareEntryFixture(string folder, string name, Vector3 requested, Vector3 forward)
        {
            var scene = EditorSceneManager.OpenScene(Source);
            string path = folder + "/" + name + ".unity";
            if (!EditorSceneManager.SaveScene(scene, path, true)) throw new IOException("Entry fixture copy failed.");
            scene = EditorSceneManager.OpenScene(path);
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var controller = player.GetComponent<CharacterController>();
            if (!NavMesh.SamplePosition(requested, out var hit, .8f, NavMesh.AllAreas))
                throw new InvalidOperationException("No entry fixture floor: " + path);
            Vector3 spawn = hit.position + Vector3.up * (controller.height * .5f - controller.center.y + .05f);
            Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);
            player.transform.SetPositionAndRotation(spawn, facing);
            var flow = new SerializedObject(Object.FindObjectOfType<M2RouteFlowController>());
            var checkpoint = (Transform)flow.FindProperty("_defaultCheckpoint").objectReferenceValue;
            checkpoint.SetPositionAndRotation(spawn, facing);
            Camera.main.transform.SetPositionAndRotation(spawn - forward * 5.8f + Vector3.up * 2.5f,
                facing * Quaternion.Euler(18f, 0f, 0f));
            EditorSceneManager.SaveScene(scene);
        }

        static void PrepareCornerFixture(string folder, string name)
        {
            // Synthetic navigation counterexample ONLY in our generated copy. The formal scene,
            // baked navigation, authored arena and gates are untouched; no production layout claim.
            var scene = EditorSceneManager.OpenScene(Source);
            string path = folder + "/" + name + "_Corner.unity";
            if (!EditorSceneManager.SaveScene(scene, path, true)) throw new IOException("Corner copy failed.");
            scene = EditorSceneManager.OpenScene(path);
            var enemy = Object.FindObjectsOfType<CombatTarget>().Single(t => t.name == name);
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var controller = player.GetComponent<CharacterController>();
            if (!NavMesh.SamplePosition(enemy.transform.position, out var enemyFloor, 2f, NavMesh.AllAreas))
                throw new InvalidOperationException("No corner fixture enemy floor: " + path);
            Vector3 requested = enemy.transform.position + enemy.transform.forward * 2.2f;
            // The generic 2.2m front point lands in an existing Forest navigation hole.
            // Reuse its already validated front fixture, never repair/bake production navigation for a test.
            if (name == Names[5]) requested = Front[5];
            requested.y = enemyFloor.position.y;
            if (!NavMesh.SamplePosition(requested, out var floor, .8f, NavMesh.AllAreas))
                throw new InvalidOperationException("No corner fixture player floor: " + path);
            Vector3 spawn = floor.position + Vector3.up * (controller.height * .5f - controller.center.y + .05f);
            Quaternion facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(enemy.transform.position - spawn, Vector3.up));
            player.transform.SetPositionAndRotation(spawn, facing);
            var flow = new SerializedObject(Object.FindObjectOfType<M2RouteFlowController>());
            var checkpoint = (Transform)flow.FindProperty("_defaultCheckpoint").objectReferenceValue;
            checkpoint.SetPositionAndRotation(spawn, facing);
            Camera.main.transform.SetPositionAndRotation(spawn - facing * Vector3.forward * 5.8f + Vector3.up * 2.5f,
                facing * Quaternion.Euler(18, 0, 0));
            floor = enemyFloor;
            for (int i = 0; i < 4; i++)
            {
                bool alongX = i < 2;
                float side = i % 2 == 0 ? -1f : 1f;
                var blocker = new GameObject("TEST_ONLY_RetreatPocket_" + i);
                blocker.transform.position = floor.position + new Vector3(alongX ? side * 1.5f : 0, .2f,
                    alongX ? 0 : side * 1.5f);
                var obstacle = blocker.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = alongX ? new Vector3(.2f, .4f, 3.2f) : new Vector3(3.2f, .4f, .2f);
                obstacle.carving = true; obstacle.carveOnlyStationary = false;
                // No collider/renderer/LOS cheat: real navigation carving below the natural sight ray.
            }
            EditorSceneManager.SaveScene(scene);
        }

        public static void Cleanup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle Edit Mode required.");
            string folder = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(folder)) return;
            if (!folder.StartsWith("Assets/_Game/Scenes/Review/__P0Sensing_", StringComparison.Ordinal) ||
                Path.GetDirectoryName(folder).Replace('\\','/') != "Assets/_Game/Scenes/Review")
                throw new InvalidOperationException("Unexpected fixture target; nothing removed.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path.StartsWith(folder + "/", StringComparison.Ordinal))
            {
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                    throw new InvalidOperationException("Generated fixture is dirty; preserve it for manual review.");
                EditorSceneManager.OpenScene(Source);
            }
            // Preserve a deletion audit outside Assets before removing exactly our generated fixture folder.
            const string audit = "Builds/TestResults/EnemyParticipation/fixture-cleanup.jsonl";
            Directory.CreateDirectory(Path.GetDirectoryName(audit));
            File.AppendAllText(audit, JsonUtility.ToJson(new CleanupRecord {
                utc = DateTime.UtcNow.ToString("O"), folder = folder, removed = false }) + "\n");
            if (!AssetDatabase.DeleteAsset(folder)) throw new IOException("Cannot remove generated fixture folder.");
            File.AppendAllText(audit, JsonUtility.ToJson(new CleanupRecord {
                utc = DateTime.UtcNow.ToString("O"), folder = folder, removed = true }) + "\n");
            SessionState.EraseString(SessionKey);
            SessionState.EraseBool(PreparedKey);
        }
        [Serializable] sealed class CleanupRecord { public string utc, folder; public bool removed; }
    }
}
