using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Explicit Editor-only Game View evidence. Diagnostic stations, not a natural route run.</summary>
    [InitializeOnLoad]
    public static class EnvironmentGameViewReview
    {
        const string Source = "Assets/_Game/Scenes/10_EmberValley.unity";
        const string Key = "Emberfall.R3GameView.";
        static int station = -1, nextFrame;
        static double deadline;
        static bool requested;
        static readonly string[] Names = { "camp", "forest", "watchtower", "bridge", "right" };
        static readonly Vector3[] Positions = {
            new Vector3(0, 1.05f, -4), new Vector3(0, 1.05f, 31), new Vector3(-11, 1.05f, 36),
            new Vector3(12, 1.05f, 44), new Vector3(33, 1.05f, 43)
        };
        static readonly float[] Yaws = { 0, -25, -35, 90, 145 };

        static EnvironmentGameViewReview()
        {
            EditorApplication.playModeStateChanged += OnMode;
            EditorApplication.update += Tick;
        }

        public static void Begin(string label, string baselineSnapshot = null)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || scene.path != Source || scene.isDirty || SceneManager.sceneCount != 1 ||
                SessionState.GetBool(Key + "active", false))
                throw new InvalidOperationException("Saved Valley alone in idle Edit Mode required.");
            if (label != "before" && label != "after") throw new ArgumentException("Use before or after.");
            foreach (var point in Positions)
            {
                if (!UnityEngine.AI.NavMesh.SamplePosition(point - Vector3.up, out var hit, .5f, UnityEngine.AI.NavMesh.AllAreas) ||
                    Vector3.Distance(hit.position, point - Vector3.up) > .3f)
                    throw new InvalidOperationException("Station is not on the existing walkable route: " + point);
                var blockers = Physics.OverlapCapsule(point + Vector3.down * .5f, point + Vector3.up * .5f,
                    .3f, ~0, QueryTriggerInteraction.Ignore).Where(c => c.GetComponent<PlayerCombatActor>() == null);
                if (blockers.Any()) throw new InvalidOperationException("Station intersects solid geometry: " + point);
            }
            string id = Guid.NewGuid().ToString("N");
            string output = "Builds/ArtReview/0.9.6-r3-game-view/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + label;
            Directory.CreateDirectory(output);
            string temporary = "Assets/_Game/Scenes/__R3CameraReview_" + id + ".unity";
            if (baselineSnapshot == null)
            {
                if (!EditorSceneManager.SaveScene(scene, temporary, true)) throw new IOException("Cannot create review copy.");
            }
            else
            {
                string baseline = Path.GetFullPath(baselineSnapshot);
                string reviewRoot = Path.GetFullPath("Builds/ArtReview") + Path.DirectorySeparatorChar;
                if (label != "before" || !baseline.StartsWith(reviewRoot, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(baseline) != ".unity" || !File.Exists(baseline))
                    throw new ArgumentException("Baseline must be an existing ArtReview scene snapshot.");
                File.Copy(baseline, temporary, false);
                AssetDatabase.ImportAsset(temporary, ImportAssetOptions.ForceSynchronousImport);
                File.WriteAllText(output + "/baseline-source.txt", baseline);
            }
            SessionState.SetString(Key + "temporary", temporary);
            SessionState.SetString(Key + "output", output);
            SessionState.SetBool(Key + "active", true);
            var copy = EditorSceneManager.OpenScene(temporary);
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            var serialized = new SerializedObject(flow);
            // A rooted filename overrides Path.Combine's first argument, before Awake loads/creates a save.
            serialized.FindProperty("_saveFileName").stringValue = Path.GetFullPath(output + "/isolated-save.json");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(copy);
            File.WriteAllText(output + "/scope.txt", "Actual Game View and production ThirdPersonCameraRig; fixed diagnostic stations; AI/input/motor suspended in disposable scene copy. Not natural traversal, combat or performance evidence. No production scene/save changes.");
            EditorApplication.isPlaying = true;
        }

        static void OnMode(PlayModeStateChange mode)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            if (mode == PlayModeStateChange.EnteredPlayMode)
            {
                station = -1; requested = false; nextFrame = Time.frameCount + 30;
                deadline = EditorApplication.timeSinceStartup + 90;
                var gameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                EditorWindow.GetWindow(gameView).Focus();
            }
            if (mode == PlayModeStateChange.EnteredEditMode)
            {
                string temp = SessionState.GetString(Key + "temporary", "");
                // Only remove the exact disposable scene created by Begin; never a user's scene.
                if (!temp.StartsWith("Assets/_Game/Scenes/__R3CameraReview_", StringComparison.Ordinal)) return;
                if (SceneManager.GetActiveScene().path != temp) return;
                EditorSceneManager.OpenScene(Source);
                AssetDatabase.DeleteAsset(temp);
                SessionState.SetBool(Key + "active", false);
            }
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key + "active", false) || !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            string output = SessionState.GetString(Key + "output", "");
            try
            {
                if (deadline > 0 && EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Game View capture timed out.");
                if (Time.frameCount < nextFrame) return;
                var player = Object.FindObjectOfType<PlayerCombatActor>();
                var rig = Object.FindObjectOfType<ThirdPersonCameraRig>();
                if (player == null || rig == null) throw new InvalidOperationException("Production player/camera missing.");
                var flow = Object.FindObjectOfType<M2RouteFlowController>();
                if (flow == null || Path.GetFullPath(flow.SavePath) != Path.GetFullPath(output + "/isolated-save.json"))
                    throw new InvalidOperationException("Save isolation failed.");
                foreach (var b in Object.FindObjectsOfType<MonoBehaviour>())
                    if (b.GetType().Namespace == "Emberfall.AI.Unity" || b is ThirdPersonMotor || b is PlayerInputReader ||
                        b is M2RouteFlowController || b is CombatEncounterCoordinator || b is ForestSealTemplateCoordinator)
                        b.enabled = false;
                foreach (var agent in Object.FindObjectsOfType<UnityEngine.AI.NavMeshAgent>()) agent.enabled = false;
                rig.SetLookInputBlocked(true);
                if (station >= 0 && !requested)
                {
                    Camera camera = Camera.main;
                    var record = new ViewRecord { station = Names[station], player = player.transform.position,
                        camera = camera.transform.position, euler = camera.transform.eulerAngles, fov = camera.fieldOfView,
                        width = Screen.width, height = Screen.height };
                    File.WriteAllText(output + "/" + Names[station] + ".json", JsonUtility.ToJson(record, true));
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(output + "/" + Names[station] + ".png"));
                    requested = true; nextFrame = Time.frameCount + 8; return;
                }
                if (station >= 0 && !File.Exists(output + "/" + Names[station] + ".png")) return;
                if (++station == Names.Length)
                {
                    File.WriteAllText(output + "/complete.txt", "5 Game View screenshots captured; diagnostic stations only.");
                    File.WriteAllText("Builds/ArtReview/0.9.6-r3-game-view/latest.txt", output);
                    EditorApplication.isPlaying = false; return;
                }
                var controller = player.GetComponent<CharacterController>();
                controller.enabled = false;
                player.transform.SetPositionAndRotation(Positions[station], Quaternion.Euler(0, Yaws[station], 0));
                controller.enabled = true;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(ThirdPersonCameraRig).GetField("_yaw", flags).SetValue(rig, Yaws[station]);
                typeof(ThirdPersonCameraRig).GetField("_pitch", flags).SetValue(rig, 18f);
                // Let the live rig resolve its normal collision and smoothing for 45 frames.
                requested = false; nextFrame = Time.frameCount + 45;
            }
            catch (Exception exception)
            {
                File.WriteAllText(output + "/failed.txt", exception.ToString());
                Debug.LogException(exception); EditorApplication.isPlaying = false;
            }
        }

        [Serializable] sealed class ViewRecord
        { public string station; public Vector3 player, camera, euler; public float fov; public int width, height; }
    }
}
