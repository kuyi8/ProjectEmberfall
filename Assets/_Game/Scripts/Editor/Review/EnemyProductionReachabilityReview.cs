using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.AI.Data;
using Emberfall.Core.Identifiers;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Editor-only collision traversal and actual-input probe; never changes AI or navigation.</summary>
    [InitializeOnLoad]
    public static class EnemyProductionReachabilityReview
    {
        const string Source = "Assets/_Game/Scenes/10_EmberValley.unity";
        const string Key = "Emberfall.P0Access.";
        static Gamepad pad;
        static readonly List<WalkRow> walk = new List<WalkRow>();
        static float startTime, next;
        static Vector2 actualMove;
        static bool finished;
        static readonly Vector3 Hole = new Vector3(6.8f, 0, 38.3f);
        static EnemyProductionReachabilityReview()
        {
            EditorApplication.playModeStateChanged += OnMode;
            EditorApplication.update += Tick;
        }

        static void RequireIdle()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                scene.path != Source || scene.isDirty || UnityEngine.SceneManagement.SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Clean idle formal Valley required.");
        }

        public static string CaptureSurvey()
        {
            RequireIdle();
            string output = "Builds/TestResults/EnemyParticipation/reachability-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(output);
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var original = player.GetComponent<CharacterController>();
            var probe = new GameObject("EDITOR_ONLY_ControllerTraversalProbe");
            var controller = probe.AddComponent<CharacterController>();
            controller.height = original.height; controller.radius = original.radius;
            controller.center = original.center; controller.skinWidth = original.skinWidth;
            controller.stepOffset = original.stepOffset; controller.slopeLimit = original.slopeLimit;
            var rows = new List<AccessRow>();
            try
            {
                foreach (var encounter in Object.FindObjectsOfType<CombatEncounterCoordinator>())
                {
                    var nodes = new Dictionary<Vector2Int, Vector3>();
                    Vector3 center = encounter.ArenaCenter; Vector2 half = encounter.ArenaHalfExtents;
                    const float spacing = .75f;
                    for (int ix = 0; ix * spacing <= half.x * 2; ix++)
                    for (int iz = 0; iz * spacing <= half.y * 2; iz++)
                    {
                        Vector3 desired = center + new Vector3(-half.x + ix * spacing, 0, -half.y + iz * spacing);
                        if (!Floor(desired, out var floor)) continue;
                        nodes[new Vector2Int(ix, iz)] = floor;
                    }
                    // Start at the arena's south/entry edge, not at arbitrary isolated islands.
                    Vector3 entry = encounter.TelemetrySegment == "bridge-encounter" ? new Vector3(10.7f, 0, 40.6f) :
                        encounter.TelemetrySegment == "courtyard-encounter" ? new Vector3(20.3f, 0, 44f) :
                        encounter.TelemetrySegment == "pre-sanctum-encounter" ? new Vector3(39f, 0, 28.6f) : new Vector3(0, 0, 28.5f);
                    var seed = nodes.OrderBy(n => Vector3.ProjectOnPlane(n.Value - entry, Vector3.up).sqrMagnitude).First();
                    var reached = new HashSet<Vector2Int> { seed.Key };
                    var queue = new Queue<Vector2Int>(); queue.Enqueue(seed.Key);
                    while (queue.Count > 0)
                    {
                        var node = queue.Dequeue();
                        foreach (var delta in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
                        {
                            var neighbour = node + delta;
                            if (reached.Contains(neighbour) || !nodes.TryGetValue(neighbour, out var goal)) continue;
                            if (!Traverse(controller, nodes[node], goal)) continue;
                            reached.Add(neighbour); queue.Enqueue(neighbour);
                        }
                    }
                    probe.SetActive(false); // It must not occlude any attack ray.
                    var members = Object.FindObjectsOfType<EncounterLeash>().Where(l => l.Encounter == encounter).ToArray();
                    foreach (var node in reached)
                    foreach (var member in members) rows.Add(Check(member, nodes[node], encounter.TelemetrySegment));
                    probe.SetActive(true);
                }
                probe.SetActive(false);
                if (Floor(Hole, out var holeFloor))
                {
                    var member = Object.FindObjectsOfType<EncounterLeash>().Single(l => l.name == "Enemy_RunePriest_Forest");
                    var row = Check(member, holeFloor, "forest-hole");
                    row.controllerTraversal = false; // Independently measured by BeginForestHole, not grid-inferred.
                    rows.Add(row);
                }
                File.WriteAllLines(output + "/survey.jsonl", rows.Select(JsonUtility.ToJson));
                File.WriteAllText(output + "/scope.json", JsonUtility.ToJson(new SurveyScope {
                    rows = rows.Count, radius = controller.radius, height = controller.height,
                    scope = "0.75m grid; actual copied CharacterController.Move over existing collision, entry-edge flood. Editor local arena feasibility, NOT natural route or exhaustive continuous-space proof. Legal attacks use original range, actual LOS and complete paths from authored spawns. Static actors/gates retain authored states; agent avoidance is not simulated. Forest hole has separate real-input probe."
                }, true));
            }
            finally { Object.DestroyImmediate(probe); }
            return output;
        }

        static bool Floor(Vector3 desired, out Vector3 floor)
        {
            foreach (var hit in Physics.RaycastAll(desired + Vector3.up * 2, Vector3.down, 4, ~0,
                QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider.GetComponentInParent<CombatTarget>() != null ||
                    hit.collider.name == "EDITOR_ONLY_ControllerTraversalProbe" || hit.normal.y < .6f ||
                    Mathf.Abs(hit.point.y - desired.y) > .4f) continue;
                floor = hit.point; return true;
            }
            floor = default; return false;
        }

        static bool Traverse(CharacterController controller, Vector3 from, Vector3 to)
        {
            float lift = controller.height * .5f - controller.center.y + .03f;
            controller.transform.position = from + Vector3.up * lift;
            Physics.SyncTransforms();
            Vector3 step = (to - from) / 4;
            for (int i = 0; i < 4; i++) controller.Move(step + Vector3.down * .05f);
            return Vector3.ProjectOnPlane(controller.transform.position - to, Vector3.up).magnitude < .12f &&
                Mathf.Abs(controller.transform.position.y - lift - to.y) < .2f;
        }

        static AccessRow Check(EncounterLeash leash, Vector3 foot, string segment)
        {
            var actor = leash.GetComponent<CombatTarget>(); var agent = leash.GetComponent<NavMeshAgent>();
            var ranged = actor as RangedEnemyActor; var melee = actor as MeleeEnemyActor; var shield = actor as ShieldEnemyActor;
            var authored = new SerializedObject(actor);
            string json = ((TextAsset)authored.FindProperty("_definitionJson").objectReferenceValue).text;
            string id = authored.FindProperty("_enemyId").stringValue;
            ContentId.TryCreate(id, out var contentId);
            float range = ranged != null ? RangedEnemyDefinitionJsonLoader.Load(json).GetRequired(contentId).PreferredMaximumRange :
                melee != null ? MeleeEnemyDefinitionJsonLoader.Load(json).GetRequired(contentId).AttackRange :
                ShieldEnemyDefinitionJsonLoader.Load(json).GetRequired(contentId).AttackRange;
            var row = new AccessRow { segment = segment, name = actor.name, playerFoot = foot,
                enemySpawn = actor.transform.position, originalRange = range, controllerTraversal = true };
            var path = new NavMeshPath();
            if (!NavMesh.SamplePosition(actor.transform.position, out var start, 2, agent.areaMask)) return row;
            row.centreCompletePath = Complete(start.position, foot, leash, agent, path, out _);
            for (int candidate = -1; candidate < 24; candidate++)
            {
                float radius = ranged != null ? 6f : range * .65f;
                Vector3 desired = candidate < 0 ? start.position : foot + Quaternion.Euler(0, candidate * 15, 0) * Vector3.forward * radius;
                if (!Complete(start.position, desired, leash, agent, path, out var position)) continue;
                Vector3 playerRoot = foot + Vector3.up * 1f;
                if (Vector3.Distance(position, playerRoot) > range) continue;
                Vector3 aim = foot + Vector3.up * 1.4f;
                Vector3 origin = position + (actor.AimPoint.position - actor.transform.position);
                Vector3 ray = aim - origin;
                var obstruction = Physics.RaycastAll(origin, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore)
                    .OrderBy(h => h.distance).FirstOrDefault(h => h.collider.GetComponentInParent<CombatTarget>() != actor);
                if (obstruction.collider != null) continue;
                row.legalAttackPosition = true; row.attackPosition = position; break;
            }
            return row;
        }

        static bool Complete(Vector3 start, Vector3 desired, EncounterLeash leash, NavMeshAgent agent, NavMeshPath path, out Vector3 goal)
        {
            goal = default;
            if (!NavMesh.SamplePosition(desired, out var hit, .25f, agent.areaMask) || !leash.Contains(hit.position, agent.radius) ||
                !NavMesh.CalculatePath(start, hit.position, agent.areaMask, path) || path.status != NavMeshPathStatus.PathComplete ||
                path.corners.Any(c => !leash.Contains(c))) return false;
            goal = hit.position; return true;
        }

        public static string BeginForestHole()
        {
            RequireIdle();
            string output = "Builds/TestResults/EnemyParticipation/forest-hole-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(output);
            string temp = "Assets/_Game/Scenes/__P0Access_" + Guid.NewGuid().ToString("N") + ".unity";
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), temp, true);
            var scene = EditorSceneManager.OpenScene(temp);
            var player = Object.FindObjectOfType<PlayerCombatActor>(); var controller = player.GetComponent<CharacterController>();
            if (!Floor(new Vector3(6.8f, 0, 37.3f), out var floor)) throw new InvalidOperationException("Missing authored entry floor.");
            var spawn = floor + Vector3.up * (controller.height * .5f - controller.center.y + .05f);
            player.transform.SetPositionAndRotation(spawn, Quaternion.identity);
            var flow = new SerializedObject(Object.FindObjectOfType<M2RouteFlowController>());
            ((Transform)flow.FindProperty("_defaultCheckpoint").objectReferenceValue).SetPositionAndRotation(spawn, Quaternion.identity);
            flow.FindProperty("_saveFileName").stringValue = Path.GetFullPath(output + "/isolated-save.json");
            flow.ApplyModifiedPropertiesWithoutUndo(); EditorSceneManager.SaveScene(scene);
            SessionState.SetString(Key + "temp", temp); SessionState.SetString(Key + "output", output);
            SessionState.SetBool(Key + "active", true); EditorApplication.isPlaying = true;
            return output;
        }

        static void OnMode(PlayModeStateChange mode)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            if (mode == PlayModeStateChange.EnteredPlayMode)
            {
                walk.Clear(); startTime = Time.time; next = 0; finished = false; actualMove = Vector2.zero;
                pad = InputSystem.AddDevice<Gamepad>("P0AccessProbe"); InputSystem.onAfterUpdate += OnInput;
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            }
            if (mode == PlayModeStateChange.ExitingPlayMode)
            {
                InputSystem.onAfterUpdate -= OnInput;
                if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
                File.WriteAllLines(SessionState.GetString(Key + "output", "") + "/actual-input.jsonl", walk.Select(JsonUtility.ToJson));
            }
            if (mode == PlayModeStateChange.EnteredEditMode)
            {
                string temp = SessionState.GetString(Key + "temp", "");
                if (!temp.StartsWith("Assets/_Game/Scenes/__P0Access_", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected probe path.");
                EditorSceneManager.OpenScene(Source); AssetDatabase.DeleteAsset(temp);
                SessionState.SetBool(Key + "active", false);
            }
        }

        static void OnInput()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
            var reader = Object.FindObjectOfType<PlayerInputReader>(); if (reader != null) actualMove = reader.Move;
        }
        static void Tick()
        {
            if (!SessionState.GetBool(Key + "active", false) || !EditorApplication.isPlaying || pad == null || finished) return;
            var player = Object.FindObjectOfType<PlayerCombatActor>(); var flow = Object.FindObjectOfType<M2RouteFlowController>();
            if (player == null || flow == null || !flow.IsInitialized) return;
            if (Path.GetFullPath(flow.SavePath) != Path.GetFullPath(SessionState.GetString(Key + "output", "") + "/isolated-save.json"))
                throw new InvalidOperationException("Probe save isolation failed.");
            float elapsed = Time.time - startTime;
            Vector3 delta = Vector3.ProjectOnPlane(Hole - player.NavigationFootPosition, Vector3.up);
            var state = new GamepadState();
            if (elapsed < 5 && delta.magnitude > .08f)
            {
                Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                state.leftStick = new Vector2(Vector3.Dot(delta.normalized, Vector3.Cross(Vector3.up, forward)), Vector3.Dot(delta.normalized, forward)) * .4f;
            }
            InputSystem.QueueStateEvent(pad, state);
            if (Time.time >= next)
            {
                next = Time.time + .1f;
                walk.Add(new WalkRow { time = elapsed, foot = player.NavigationFootPosition, actualMove = actualMove,
                    requestedMove = state.leftStick, distance = delta.magnitude, health = player.HealthNormalized,
                    controllerEnabled = player.GetComponent<CharacterController>().enabled, motorEnabled = player.GetComponent<ThirdPersonMotor>().enabled,
                    nearNavMesh = NavMesh.SamplePosition(player.NavigationFootPosition, out _, .8f, NavMesh.AllAreas) });
            }
            if (elapsed > 8 || player.HealthNormalized <= 0 || flow.DeathCount > 0)
            { finished = true; InputSystem.QueueStateEvent(pad, new GamepadState()); EditorApplication.isPlaying = false; }
        }

        [Serializable] sealed class WalkRow
        { public float time, distance, health; public Vector3 foot; public Vector2 actualMove, requestedMove; public bool controllerEnabled, motorEnabled, nearNavMesh; }
        [Serializable] sealed class AccessRow
        { public string segment, name; public Vector3 playerFoot, enemySpawn, attackPosition; public float originalRange; public bool controllerTraversal, centreCompletePath, legalAttackPosition; }
        [Serializable] sealed class SurveyScope
        { public string scope; public int rows; public float radius, height; }
    }
}
