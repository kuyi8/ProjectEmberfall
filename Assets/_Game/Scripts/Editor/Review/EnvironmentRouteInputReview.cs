using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Interaction;
using Emberfall.Gameplay.Movement;
using Emberfall.Quests.Domain;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Editor-only input-chain pilot, not a full Player/human route acceptance.</summary>
    [InitializeOnLoad]
    public static class EnvironmentRouteInputReview
    {
        const string Source = "Assets/_Game/Scenes/10_EmberValley.unity";
        const string Key = "Emberfall.R4Input.";
        static Gamepad pad;
        static PlayerCombatActor player;
        static M2RouteFlowController flow;
        static InteractableBehaviour target;
        static readonly List<Sample> samples = new List<Sample>();
        static readonly List<string> logs = new List<string>();
        static double started, nextSample, nextShot, lastProgress;
        static Vector3 progressPosition;
        static int phase, lastFrame, finishFrame;
        static string output;
        static bool finishing;
        static Vector2 dynamicMove;
        static int dynamicInputSamples;

        static EnvironmentRouteInputReview()
        {
            EditorApplication.playModeStateChanged += OnMode;
            EditorApplication.update += Tick;
        }

        public static string Begin()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || scene.path != Source || scene.isDirty ||
                UnityEngine.SceneManagement.SceneManager.sceneCount != 1 || SessionState.GetBool(Key + "active", false))
                throw new InvalidOperationException("Saved Valley alone in idle Edit Mode required.");
            string folder = "Builds/RouteReview/0.9.6-r4-input/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(folder);
            string temp = "Assets/_Game/Scenes/__R4InputReview_" + Guid.NewGuid().ToString("N") + ".unity";
            if (!EditorSceneManager.SaveScene(scene, temp, true)) throw new IOException("Cannot create isolated scene.");
            SessionState.SetString(Key + "temp", temp);
            SessionState.SetString(Key + "output", folder);
            SessionState.SetBool(Key + "active", true);
            var copy = EditorSceneManager.OpenScene(temp);
            var serialized = new SerializedObject(Object.FindObjectOfType<M2RouteFlowController>());
            serialized.FindProperty("_saveFileName").stringValue = Path.GetFullPath(folder + "/isolated-save.json");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(copy);
            EditorApplication.isPlaying = true;
            return folder;
        }

        static void OnMode(PlayModeStateChange mode)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            if (mode == PlayModeStateChange.EnteredPlayMode)
            {
                output = SessionState.GetString(Key + "output", "");
                samples.Clear(); logs.Clear(); phase = 0; finishing = false;
                dynamicMove = Vector2.zero; dynamicInputSamples = 0;
                player = null; flow = null; target = null;
                started = nextSample = nextShot = lastProgress = EditorApplication.timeSinceStartup;
                lastFrame = -1;
                pad = InputSystem.AddDevice<Gamepad>("R4ReviewVirtualGamepad");
                UnityEngine.Application.logMessageReceived += OnLog;
                InputSystem.onAfterUpdate += ObserveDynamicInput;
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            }
            if (mode == PlayModeStateChange.ExitingPlayMode)
            {
                UnityEngine.Application.logMessageReceived -= OnLog;
                InputSystem.onAfterUpdate -= ObserveDynamicInput;
                if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
                pad = null;
                if (!finishing && output != null) WriteResult(false, "Interrupted before completion.");
            }
            if (mode == PlayModeStateChange.EnteredEditMode)
            {
                string temp = SessionState.GetString(Key + "temp", "");
                if (temp.StartsWith("Assets/_Game/Scenes/__R4InputReview_", StringComparison.Ordinal) &&
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == temp)
                {
                    EditorSceneManager.OpenScene(Source);
                    AssetDatabase.DeleteAsset(temp);
                }
                SessionState.SetBool(Key + "active", false);
            }
        }

        static void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || message.Contains("[M5C_PACING]"))
                logs.Add((EditorApplication.timeSinceStartup - started).ToString("F3") + " " + type + " " + message);
        }

        static void ObserveDynamicInput()
        {
            // EditorApplication.update may expose Editor-device state (zero), not the game's dynamic state.
            if (InputState.currentUpdateType != InputUpdateType.Dynamic || player == null) return;
            dynamicMove = player.GetComponent<PlayerInputReader>().Move;
            if (dynamicMove.sqrMagnitude > .01f) dynamicInputSamples++;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key + "active", false) || !EditorApplication.isPlaying || pad == null) return;
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            if (finishing)
            {
                if (Time.frameCount >= finishFrame) EditorApplication.isPlaying = false;
                return;
            }
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - started > 100) { Finish(false, "100s pilot deadline."); return; }
                if (player == null) player = Object.FindObjectOfType<PlayerCombatActor>();
                if (flow == null) flow = Object.FindObjectOfType<M2RouteFlowController>();
                if (player == null || flow == null || !flow.IsInitialized) return;
                if (Path.GetFullPath(flow.SavePath) != Path.GetFullPath(output + "/isolated-save.json"))
                    throw new InvalidOperationException("Save isolation failed.");
                if (!player.GetComponent<ThirdPersonMotor>().enabled || !player.GetComponent<CharacterController>().enabled)
                    throw new InvalidOperationException("Production movement chain disabled.");
                if (flow.DeathCount > 0 || player.HealthNormalized <= 0) { Finish(false, "Player died; no reset bypass."); return; }
                if (phase == 0 && flow.Stage != MainQuestStage.MeetScout)
                {
                    phase = 1; target = null; lastProgress = now;
                    logs.Add("Scout advanced via input; now walking to Watchtower.");
                    Capture("scout-interacted");
                }
                if (phase == 1 && flow.WatchtowerDiscovered) { Finish(true, "Scout and watchtower completed via input."); return; }
                if (target == null)
                {
                    target = phase == 0
                        ? (InteractableBehaviour)Object.FindObjectsOfType<M2RouteInteractable>().Single(x => x.Role == M2RouteRole.Scout)
                        : Object.FindObjectsOfType<RouteEnrichmentInteractable>().Single(x => x.Kind == RouteEnrichmentInteractionKind.Watchtower);
                    progressPosition = player.transform.position;
                    lastProgress = now; // Scene initialization is not a stalled movement attempt.
                }
                var state = new GamepadState();
                var candidate = player.GetComponent<PlayerInteractor>().CurrentCandidate;
                if (ReferenceEquals(candidate, target))
                {
                    // Pulse the real interact binding, never call quest/interaction methods directly.
                    if ((int)((now - started) * 4) % 2 == 0) state = state.WithButton(GamepadButton.South);
                }
                else
                {
                    NavMeshHit startHit, endHit;
                    if (!NavMesh.SamplePosition(player.NavigationFootPosition, out startHit, .8f, NavMesh.AllAreas) ||
                        !NavMesh.SamplePosition(target.transform.position, out endHit, 2.2f, NavMesh.AllAreas))
                        throw new InvalidOperationException("Route endpoint has no navigation sample.");
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                        throw new InvalidOperationException("No complete navigation path to " + target.name);
                    Vector3 corner = path.corners.Last();
                    foreach (var point in path.corners.Skip(1))
                        if (Vector3.ProjectOnPlane(point - player.NavigationFootPosition, Vector3.up).magnitude > .35f) { corner = point; break; }
                    Vector3 direction = Vector3.ProjectOnPlane(corner - player.NavigationFootPosition, Vector3.up).normalized;
                    Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                    state.leftStick = new Vector2(Vector3.Dot(direction, Vector3.Cross(Vector3.up, forward)), Vector3.Dot(direction, forward));
                    // Turn the production camera through its actual Look binding, no transform writes.
                    state.rightStick = new Vector2(Mathf.Clamp(Vector3.SignedAngle(forward, direction, Vector3.up) / 70f, -1f, 1f), 0);
                }
                InputSystem.QueueStateEvent(pad, state);
                if (Vector3.Distance(progressPosition, player.transform.position) > .4f)
                { progressPosition = player.transform.position; lastProgress = now; }
                if (now >= nextSample)
                {
                    nextSample = now + .1;
                    samples.Add(new Sample { seconds = now - started, position = player.transform.position,
                        move = dynamicMove, requestedMove = state.leftStick,
                        health = player.HealthNormalized, phase = phase, candidate = candidate == null ? "" : candidate.InteractionTransform.name });
                }
                if (now >= nextShot) { nextShot = now + 4; Capture("route-" + samples.Count); }
                if (now - lastProgress > 8) Finish(false, "No spatial/interaction progress for 8 seconds at " + player.transform.position);
            }
            catch (Exception ex) { Finish(false, ex.ToString()); }
        }

        static void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.GetFullPath(output + "/" + name + ".png"));
        static void Finish(bool success, string reason)
        {
            InputSystem.QueueStateEvent(pad, new GamepadState());
            Capture(success ? "pilot-complete" : "pilot-blocked");
            WriteResult(success, reason);
            finishing = true; finishFrame = Time.frameCount + 8;
        }
        static void WriteResult(bool success, string reason)
        {
            File.WriteAllText(output + "/result.json", JsonUtility.ToJson(new Result { pilotPassed = success, reason = reason,
                samples = samples.ToArray(), watchtower = flow != null && flow.WatchtowerDiscovered,
                dynamicMoveSamples = dynamicInputSamples, flaskCapacity = player == null ? -1 : player.Model.HealingFlasks.MaximumCharges,
                deaths = flow == null ? -1 : flow.DeathCount }, true));
            File.WriteAllLines(output + "/events.log", logs);
        }
        [Serializable] sealed class Sample
        { public double seconds; public Vector3 position; public Vector2 move, requestedMove; public float health; public int phase; public string candidate; }
        [Serializable] sealed class Result
        {
            public string scope = "Editor input-chain pilot: authored spawn to scout/watchtower; AI on; no relocation/damage injection. Not full R4/Player/performance/human acceptance.";
            public bool pilotPassed, watchtower; public int deaths, dynamicMoveSamples, flaskCapacity; public string reason; public Sample[] samples;
        }
    }
}
