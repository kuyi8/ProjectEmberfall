#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Emberfall.Application.Flow;
using Emberfall.Core.Content;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using Emberfall.Networking;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>One actual Game View of controlled OFFLINE additive Sanctum; image still needs human review.</summary>
    public sealed class SanctumDressingCameraRuntimeTests
    {
        const string Sanctum = "20_Sanctum", RigSource = "Assets/_Game/Scripts/Gameplay/Movement/ThirdPersonCameraRig.cs";
        static readonly string[] Names = { "SanctumArchiveWest", "SanctumArchiveEast", "SanctumCapWest", "SanctumCapEast" };
        static readonly string[] ParameterNames = { "_target", "_input", "_targeting", "_distance", "_pivotHeight", "_mouseSensitivity",
            "_gamepadDegreesPerSecond", "_minimumPitch", "_maximumPitch", "_collisionRadius", "_collisionMask" };
        [Serializable] sealed class Source { public string path, before, after; }
        [Serializable] sealed class Wrapper
        {
            public string name, scene; public int instanceId, cacheMultiplicity, renderers;
            public bool frustumBoundsIntersect; public Bounds worldBounds;
        }
        [Serializable] sealed class Evidence
        {
            public string status = "failed", scope = "Actual offline NewGame/Bootstrap and Game View ScreenCapture including existing IMGUI. Production Ranger is temporarily warped onto the real additive 20_Sanctum floor; original ThirdPersonCameraRig and Configure references/parameters retained. AI/Flow/Motor remain active, no input injection, quest/resource forcing, Peer or replacement camera. Active lighting scene stays Valley; its offline HUD is not a network Sanctum objective. Four loaded wrappers and actual cache are checked; frustum AABB is NOT pixel visibility or subjective composition. One controlled view, NOT natural arrival, NGO/second Peer, whole route, foreground/display, stale-query-frame measurement, performance or Player acceptance. PNG requires human review.";
            public string savePath, saveOverride, image, actorScene, additiveScene, additiveScenePath, activeLightingScene, camera, rigSourceSha, state;
            public int additiveHandle, loadedFrame, requestedFrame, fileReadyFrame, settledFrames, width, height, guiRepaints, cacheCount;
            public float health, fieldOfView; public Vector3 requestedPosition, actorPosition, cameraPosition, cameraEuler;
            public bool pngDecoded, controllerGrounded, sourceBytesSame, saveOverrideSame, ownSceneUnloaded;
            public Wrapper[] wrappers; public Source[] sources;
        }
        PlayerCombatActor _actor; ThirdPersonCameraRig _rig; CharacterController _controller;
        HudInformationGuiProbe _probe; Scene _ownScene; bool _ownRequested, _moved;
        Vector3 _originalPosition; Quaternion _originalRotation; CursorLockMode _cursorLock; bool _cursorVisible;
        string _saveOverride, _directory; Dictionary<string, string> _sources; Dictionary<string, object> _parameters;
        Evidence _evidence;

        [SetUp] public void RequireIsolatedOfflineScope()
        {
            _actor = null; _rig = null; _probe = null; _controller = null; _ownScene = default; _ownRequested = _moved = false;
            _directory = null; _evidence = null; _sources = null; _parameters = null;
            _cursorLock = Cursor.lockState; _cursorVisible = Cursor.visible;
            _saveOverride = M2RouteFlowController.EditorTestSavePath;
            Assert.That(_saveOverride, Is.Not.Null.And.Not.Empty);
            string isolated = Path.GetFullPath("Builds/TestResults/IsolatedSaves") + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(_saveOverride).StartsWith(isolated, StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(SessionRuntime.Current.Snapshot.Mode, Is.EqualTo(SessionMode.Offline));
            Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True, "Never stop an existing Peer.");
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            _sources = Directory.GetFiles("Assets/_Game/Scenes", "*.unity", SearchOption.AllDirectories)
                .SelectMany(p => new[] { p, p + ".meta" }).Concat(new[] { RigSource, RigSource + ".meta" }).ToDictionary(p => p, Hash);
        }

        [UnityTearDown] public IEnumerator RestoreOnlyOwnedRuntimeStaging()
        {
            SceneManager.sceneLoaded -= RememberOwnScene;
            try
            {
                if (_probe != null) Object.DestroyImmediate(_probe.gameObject);
                if (_moved && _actor != null && _controller != null) Warp(_originalPosition, _originalRotation);
                if (_ownRequested && _ownScene.IsValid())
                {
                    Scene loaded = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                        .FirstOrDefault(s => s.handle == _ownScene.handle);
                    if (loaded.IsValid() && loaded.isLoaded && loaded.name == Sanctum && loaded.path == "Assets/_Game/Scenes/20_Sanctum.unity")
                    { yield return SceneManager.UnloadSceneAsync(loaded); if (_evidence != null) _evidence.ownSceneUnloaded = true; }
                }
            }
            finally
            {
                Cursor.lockState = _cursorLock; Cursor.visible = _cursorVisible;
                if (_evidence != null)
                {
                    _evidence.sourceBytesSame = _sources.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value);
                    _evidence.saveOverrideSame = M2RouteFlowController.EditorTestSavePath == _saveOverride;
                    _evidence.sources = _sources.Select(p => new Source { path = p.Key, before = p.Value, after = File.Exists(p.Key) ? Hash(p.Key) : "MISSING" }).ToArray();
                    if (!_evidence.sourceBytesSame || !_evidence.saveOverrideSame) _evidence.status = "failed";
                    File.WriteAllText(Path.Combine(_directory, "report.json"), JsonUtility.ToJson(_evidence, true));
                    Assert.That(_evidence.sourceBytesSame && _evidence.saveOverrideSame, Is.True);
                }
            }
        }

        [UnityTest] public IEnumerator OfflineNewGame_AdditiveSanctumFourWrappers_ActualFormalGameView()
        {
            _directory = Path.GetFullPath("Builds/ArtReview/sanctum-dressing-camera/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
            Assert.That(Directory.Exists(_directory), Is.False); Directory.CreateDirectory(_directory);
            _evidence = new Evidence { saveOverride = _saveOverride, image = "sanctum-additive-controlled-gameview.png", rigSourceSha = _sources[RigSource] };
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 8f;
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            while ((flow == null || !flow.IsInitialized) && Time.realtimeSinceStartup < deadline)
            { yield return null; flow = Object.FindObjectOfType<M2RouteFlowController>(); }
            Assert.That(flow, Is.Not.Null); Assert.That(flow.IsInitialized && flow.enabled, Is.True);
            _evidence.savePath = flow.SavePath;
            Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(Path.GetFullPath(_saveOverride)));
            _actor = Object.FindObjectOfType<PlayerCombatActor>(); Assert.That(_actor, Is.Not.Null);
            _controller = _actor.GetComponent<CharacterController>(); Assert.That(_controller, Is.Not.Null); Assert.That(_controller.enabled, Is.True);
            Assert.That(_actor.enabled && _actor.Model != null && !_actor.Model.Health.IsDead, Is.True);
            var motor = _actor.GetComponent<ThirdPersonMotor>(); Assert.That(motor, Is.Not.Null); Assert.That(motor.enabled, Is.True);
            var camera = Camera.main; Assert.That(camera, Is.Not.Null); Assert.That(camera.enabled, Is.True);
            _rig = camera.GetComponent<ThirdPersonCameraRig>(); Assert.That(_rig, Is.Not.Null); Assert.That(_rig.enabled, Is.True);
            yield return null; yield return null;
            _originalPosition = _actor.transform.position; _originalRotation = _actor.transform.rotation;
            _parameters = ParameterNames.ToDictionary(n => n, n => Field(n).GetValue(_rig));
            Assert.That(Field("_target").GetValue(_rig), Is.EqualTo(_actor.transform));
            var before = SceneManager.GetSceneByName(Sanctum); Assert.That(!before.IsValid() || !before.isLoaded, Is.True);
            _ownRequested = true; SceneManager.sceneLoaded += RememberOwnScene;
            yield return SceneManager.LoadSceneAsync(Sanctum, LoadSceneMode.Additive);
            Assert.That(_ownScene.IsValid() && _ownScene.isLoaded, Is.True);
            Assert.That(_ownScene.path, Is.EqualTo("Assets/_Game/Scenes/20_Sanctum.unity"));
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("10_EmberValley"));
            var root = _ownScene.GetRootGameObjects().Single(r => r.name == "ImportedEnvironmentDressing");
            var additions = root.GetComponentsInChildren<CameraOccluder>(true);
            Assert.That(additions.Select(c => c.name), Is.EquivalentTo(Names));
            var floor = _ownScene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>(true)).Single(c => c.name == "Sanctum_ArenaFloor");
            Assert.That(floor.enabled && floor.gameObject.activeInHierarchy && !floor.isTrigger, Is.True);
            var destination = new Vector3(floor.bounds.center.x, floor.bounds.max.y + (_controller.height * .5f - _controller.center.y) * _actor.transform.lossyScale.y + .04f, floor.bounds.center.z - 2.5f);
            Assert.That(destination.z, Is.InRange(floor.bounds.min.z, floor.bounds.max.z));
            _moved = true; Warp(destination, Quaternion.identity); // Controlled pose only; no Configure/input/domain changes.
            int stagedFrame = Time.frameCount;
            yield return new WaitForSeconds(.65f); yield return new WaitForEndOfFrame();
            Assert.That(_actor.enabled && motor.enabled && _rig.enabled && flow.enabled && !_actor.Model.Health.IsDead, Is.True);
            Assert.That(Vector2.Distance(new Vector2(_actor.transform.position.x, _actor.transform.position.z), new Vector2(destination.x, destination.z)), Is.LessThan(.2f));
            var cache = (CameraOccluder[])Field("_artOccluders").GetValue(_rig);
            Assert.That(cache, Is.Not.Null); Assert.That(cache.All(c => c != null), Is.True);
            Assert.That(cache.Select(c => c.GetInstanceID()).Distinct().Count(), Is.EqualTo(cache.Length));
            Assert.That(cache, Is.EqualTo(Object.FindObjectsOfType<CameraOccluder>(true)));
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            _evidence.wrappers = additions.Select(c =>
            {
                Assert.That(c.enabled && c.gameObject.scene == _ownScene && c.transform.parent == root.transform, Is.True);
                Assert.That(cache.Count(item => item == c), Is.EqualTo(1), c.name);
                var renderers = c.GetComponentsInChildren<Renderer>(true); Assert.That(renderers, Is.Not.Empty);
                var bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                return new Wrapper { name = c.name, scene = c.gameObject.scene.name, instanceId = c.GetInstanceID(), cacheMultiplicity = cache.Count(item => item == c), renderers = renderers.Length, worldBounds = bounds, frustumBoundsIntersect = GeometryUtility.TestPlanesAABB(planes, bounds) };
            }).ToArray();
            Assert.That(_evidence.wrappers.Any(w => w.frustumBoundsIntersect), Is.True, "The declared controlled view must face some actual new geometry; this is still not pixel/composition approval.");
            foreach (var p in _parameters) Assert.That(Field(p.Key).GetValue(_rig), Is.EqualTo(p.Value), p.Key);
            _evidence.requestedPosition = destination; _evidence.actorPosition = _actor.transform.position;
            _evidence.cameraPosition = camera.transform.position; _evidence.cameraEuler = camera.transform.eulerAngles;
            _evidence.actorScene = _actor.gameObject.scene.name; _evidence.additiveScene = _ownScene.name;
            _evidence.additiveScenePath = _ownScene.path; _evidence.additiveHandle = _ownScene.handle;
            _evidence.activeLightingScene = SceneManager.GetActiveScene().name; _evidence.camera = camera.name;
            _evidence.state = _actor.Model.State.ToString(); _evidence.health = _actor.Model.Health.Current;
            _evidence.controllerGrounded = _controller.isGrounded; _evidence.cacheCount = cache.Length;
            _evidence.fieldOfView = camera.fieldOfView; _evidence.settledFrames = Time.frameCount - stagedFrame;
            Assert.That(_evidence.settledFrames, Is.GreaterThanOrEqualTo(2));
            var gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); gameView.Show(); gameView.Focus();
            _probe = new GameObject("Sanctum Game View fixture probe").AddComponent<HudInformationGuiProbe>();
            deadline = Time.realtimeSinceStartup + 8f;
            while (_probe.repaintCount == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(_probe.repaintCount, Is.GreaterThan(0));
            _evidence.guiRepaints = _probe.repaintCount; _evidence.requestedFrame = Time.frameCount;
            _evidence.actorPosition = _actor.transform.position; _evidence.cameraPosition = camera.transform.position; _evidence.cameraEuler = camera.transform.eulerAngles;
            _evidence.width = Screen.width; _evidence.height = Screen.height;
            string image = Path.Combine(_directory, _evidence.image); ScreenCapture.CaptureScreenshot(image);
            byte[] bytes = null; deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                try { if (File.Exists(image)) bytes = File.ReadAllBytes(image); } catch (IOException) { bytes = null; }
                if (bytes != null && bytes.Length > 32 && bytes[bytes.Length - 8] == 73 && bytes[bytes.Length - 7] == 69 && bytes[bytes.Length - 6] == 78 && bytes[bytes.Length - 5] == 68) break;
                bytes = null; yield return null;
            }
            _evidence.fileReadyFrame = Time.frameCount; Assert.That(bytes, Is.Not.Null, "Keep partial/failed PNG evidence; never synthesize a screenshot.");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try { _evidence.pngDecoded = ImageConversion.LoadImage(texture, bytes); Assert.That(_evidence.pngDecoded, Is.True); Assert.That(texture.width, Is.EqualTo(_evidence.width)); Assert.That(texture.height, Is.EqualTo(_evidence.height)); }
            finally { Object.DestroyImmediate(texture); }
            Assert.That(NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening, Is.True);
            _evidence.status = "complete"; // Engineering/capture complete, NOT visual approval.
        }

        void RememberOwnScene(Scene scene, LoadSceneMode mode)
        { if (_ownRequested && scene.name == Sanctum && mode == LoadSceneMode.Additive) { _ownScene = scene; if (_evidence != null) _evidence.loadedFrame = Time.frameCount; } }
        void Warp(Vector3 position, Quaternion rotation)
        { _controller.enabled = false; try { _actor.transform.SetPositionAndRotation(position, rotation); } finally { _controller.enabled = true; } Physics.SyncTransforms(); }
        static FieldInfo Field(string name)
        { var f = typeof(ThirdPersonCameraRig).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); Assert.That(f, Is.Not.Null, name); return f; }
        static string Hash(string path)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
#endif
