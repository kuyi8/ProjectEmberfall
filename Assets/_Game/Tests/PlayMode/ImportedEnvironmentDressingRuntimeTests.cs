#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Movement;
using Emberfall.Gameplay.Targeting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Formal runtime camera evidence; fixture warps are not a natural-input route.</summary>
    public sealed class ImportedEnvironmentDressingRuntimeTests
    {
        const string RootName = "ImportedEnvironmentDressing";
        // Exact Valley share of the twenty scene-wrapper exceptions; all six banners remain unregistered.
        static readonly string[] ValleyCameraEntries =
        {
            "CampArchiveWest", "CampArchiveEast", "CampSideWindow", "CampBrokenWest", "CampBrokenEast", "CampCapWest", "CampCapEast",
            "CourtArchiveWest", "CourtArchiveCenter", "CourtArchiveEast", "CourtBrokenEnd",
            "WardenArchiveEastA", "WardenArchiveEastB", "WardenArchiveEastC", "WardenBrokenNorthWest", "WardenBrokenNorthEast"
        };

        [Serializable] sealed class SceneHash { public string path, before, after; }
        [Serializable] sealed class Structure
        {
            public int renderers, colliders, behaviours, rigidbodies, staticObjects;
            public long triangles;
        }
        [Serializable] sealed class Frame
        {
            public string image, areaLabel, scene, lightingScene, camera, avatar, animatorState, combatState;
            public Vector3 requestedActorPosition, actorPosition, cameraPosition, cameraEuler;
            public int frame, settledPlayerLoopFrames, width = 960, height = 600, dressingInFrustum;
            public float requestedYaw, fieldOfView, aspect, nearClip, farClip, combatStateElapsed;
            public bool cameraEnabled, motorEnabled, controllerEnabled, controllerGrounded;
            public Structure dressing;
        }
        [Serializable] sealed class Evidence
        {
            public string scope = "Real Bootstrap/NewGame, production Ranger, live Actor/Motor/AI and formal third-person camera after PlayerLoop. Three labelled fixture warps/turns in offline Valley only; no quest, resource, encounter or AI state forcing. URP 960x600 3D camera renders exclude IMGUI HUD. Not natural traversal, real-input combat/contact, displayed foreground or performance acceptance. Frustum bounds do not prove pixel visibility; inspect PNGs.";
            public string savePath, editorTestSavePath;
            public bool sceneBytesSame;
            public SceneHash[] sceneHashes;
            public Frame[] frames;
        }

        [UnityTest]
        public IEnumerator FormalValleyCamera_ThreeImportedArtViews_PreserveSourceScenes()
        {
            string isolationRoot = Path.GetFullPath("Builds/TestResults/IsolatedSaves") + Path.DirectorySeparatorChar;
            Assert.That(M2RouteFlowController.EditorTestSavePath, Is.Not.Null.And.Not.Empty,
                "The namespace PlayModeSaveIsolation setup must execute before NewGame.");
            string isolatedSave = Path.GetFullPath(M2RouteFlowController.EditorTestSavePath);
            Assert.That(isolatedSave.StartsWith(isolationRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            var hashes = Directory.GetFiles("Assets/_Game/Scenes", "*.unity", SearchOption.AllDirectories)
                .SelectMany(p => new[] { p, p + ".meta" }).OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => p, Hash);
            string directory = Path.GetFullPath("Builds/ArtReview/imported-dressing-runtime/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Assert.That(Directory.Exists(directory), Is.False, "Never overwrite a previous evidence run.");
            Directory.CreateDirectory(directory);
            var frames = new List<Frame>();
            string savePath = null;
            try
            {
                M2LaunchIntent.RequestNewGame();
                yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + 8;
                var flow = Object.FindObjectOfType<M2RouteFlowController>();
                while ((flow == null || !flow.IsInitialized) && Time.realtimeSinceStartup < deadline)
                { yield return null; flow = Object.FindObjectOfType<M2RouteFlowController>(); }
                Assert.That(flow, Is.Not.Null); Assert.That(flow.IsInitialized, Is.True);
                savePath = flow.SavePath;
                Assert.That(Path.GetFullPath(savePath), Is.EqualTo(isolatedSave));
                Assert.That(Path.GetFullPath(savePath).StartsWith(isolationRoot, StringComparison.OrdinalIgnoreCase), Is.True);
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("10_EmberValley"));
                Assert.That(Time.timeScale, Is.EqualTo(1f));

                var actor = Object.FindObjectOfType<PlayerCombatActor>(); Assert.That(actor, Is.Not.Null);
                Assert.That(actor.Model, Is.Not.Null);
                var controller = actor.GetComponent<CharacterController>(); Assert.That(controller, Is.Not.Null); Assert.That(controller.enabled, Is.True);
                var motor = actor.GetComponent<ThirdPersonMotor>(); Assert.That(motor, Is.Not.Null); Assert.That(motor.enabled, Is.True);
                var input = actor.GetComponent<PlayerInputReader>(); Assert.That(input, Is.Not.Null);
                var targeting = actor.GetComponent<LockOnTargeting>(); Assert.That(targeting, Is.Not.Null);
                var camera = Camera.main; Assert.That(camera, Is.Not.Null);
                var rig = camera.GetComponent<ThirdPersonCameraRig>(); Assert.That(rig, Is.Not.Null); Assert.That(rig.enabled, Is.True);
                Assert.That(camera.enabled, Is.True);
                var scene = SceneManager.GetActiveScene();
                var dressing = scene.GetRootGameObjects().Single(r => r.name == RootName);
                AssertPureDressing(dressing);

                // Camera Configure uses the actor's yaw, leaving normal LateUpdate collision/occluder handling active.
                var requested = actor.transform.position;
                actor.transform.rotation = Quaternion.Euler(0, 180, 0);
                rig.Configure(actor.transform, input, targeting);
                int startFrame = Time.frameCount;
                yield return new WaitForSeconds(.45f); yield return new WaitForEndOfFrame();
                frames.Add(Capture(camera, actor, dressing, directory, "camp-back-wall.png", "NewGame camp, controlled turn toward back wall", requested, 180, startFrame));

                requested = OnFloor(scene, controller, "Zone_Courtyard", 29, 43);
                Warp(actor, controller, requested, 0);
                rig.Configure(actor.transform, input, targeting);
                startFrame = Time.frameCount;
                yield return new WaitForSeconds(.45f); yield return new WaitForEndOfFrame();
                frames.Add(Capture(camera, actor, dressing, directory, "courtyard-north-controlled-warp.png", "Courtyard interior, controlled warp toward north", requested, 0, startFrame));

                requested = OnFloor(scene, controller, "Zone_Warden", 46, 19);
                Warp(actor, controller, requested, 90);
                rig.Configure(actor.transform, input, targeting);
                startFrame = Time.frameCount;
                yield return new WaitForSeconds(.45f); yield return new WaitForEndOfFrame();
                frames.Add(Capture(camera, actor, dressing, directory, "warden-east-controlled-warp.png", "Offline Warden interior, controlled warp toward east; AI not disabled", requested, 90, startFrame));
                Assert.That(frames.Count, Is.EqualTo(3));
                Assert.That(hashes.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value), Is.True,
                    "Runtime fixture must not rewrite any source scene or scene meta.");
            }
            finally
            {
                File.WriteAllText(directory + "/actual-camera.json", JsonUtility.ToJson(new Evidence
                {
                    savePath = savePath, editorTestSavePath = M2RouteFlowController.EditorTestSavePath,
                    sceneBytesSame = hashes.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value),
                    sceneHashes = hashes.Select(p => new SceneHash { path = p.Key, before = p.Value, after = File.Exists(p.Key) ? Hash(p.Key) : "MISSING" }).ToArray(),
                    frames = frames.ToArray()
                }, true));
            }
        }

        [UnityTest]
        public IEnumerator NewGameCamp_OriginalEighteenDegreeSeamRay_HitsBothArchivesButNotTheRetainedLowerWalls()
        {
            string isolationRoot = Path.GetFullPath("Builds/TestResults/IsolatedSaves") + Path.DirectorySeparatorChar;
            Assert.That(M2RouteFlowController.EditorTestSavePath, Is.Not.Null.And.Not.Empty);
            string isolatedSave = Path.GetFullPath(M2RouteFlowController.EditorTestSavePath);
            Assert.That(isolatedSave.StartsWith(isolationRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            var hashes = Directory.GetFiles("Assets/_Game/Scenes", "*.unity", SearchOption.AllDirectories)
                .SelectMany(p => new[] { p, p + ".meta" }).ToDictionary(p => p, Hash);
            try
            {
                M2LaunchIntent.RequestNewGame();
                yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + 8;
                var flow = Object.FindObjectOfType<M2RouteFlowController>();
                while ((flow == null || !flow.IsInitialized) && Time.realtimeSinceStartup < deadline)
                { yield return null; flow = Object.FindObjectOfType<M2RouteFlowController>(); }
                Assert.That(flow, Is.Not.Null); Assert.That(flow.IsInitialized, Is.True);
                Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(isolatedSave));
                var actor = Object.FindObjectOfType<PlayerCombatActor>(); Assert.That(actor, Is.Not.Null);
                Assert.That(actor.transform.position.x, Is.EqualTo(0).Within(.05f));
                Assert.That(actor.transform.position.z, Is.EqualTo(-4).Within(.05f));
                var scene = SceneManager.GetActiveScene(); Assert.That(scene.name, Is.EqualTo("10_EmberValley"));
                var dressing = scene.GetRootGameObjects().Single(r => r.name == RootName);
                AssertPureDressing(dressing);
                var rig = Camera.main.GetComponent<ThirdPersonCameraRig>(); Assert.That(rig, Is.Not.Null);
                var cameraSettings = new SerializedObject(rig);
                Assert.That(cameraSettings.FindProperty("_distance").floatValue, Is.EqualTo(5.8f));
                Assert.That(cameraSettings.FindProperty("_collisionRadius").floatValue, Is.EqualTo(.22f));
                Assert.That(cameraSettings.FindProperty("_pivotHeight").floatValue, Is.EqualTo(1.45f));
                // Production birth sightline, not a saved camera's 20-degree authoring pose.
                // AABB query acceptance alone does NOT certify aperture framing or natural route/camera feel.
                var ray = new Ray(new Vector3(0, 2.5f, -4), -(Quaternion.Euler(18, 0, 0) * Vector3.forward));
                foreach (string name in new[] { "CampArchiveWest", "CampArchiveEast" })
                {
                    var occluder = dressing.transform.Find(name).GetComponent<CameraOccluder>();
                    Assert.That(occluder, Is.Not.Null, name);
                    Assert.That(occluder.TryGetDistance(ray, 5.8f, .22f, out float distance), Is.True, name);
                    Assert.That(distance, Is.InRange(3.55f, 4.2f), name + ": newly registered real renderer bounds must catch the seam ray.");
                }
                var transforms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
                foreach (string name in new[] { "Camp_Back_1", "Camp_Back_2" })
                {
                    var lowerWall = transforms.Single(t => t.name == name).GetComponent<CameraOccluder>();
                    Assert.That(lowerWall, Is.Not.Null, name);
                    Assert.That(lowerWall.TryGetDistance(ray, 5.8f, .22f, out _), Is.False,
                        name + ": the existing lower wall must not be misreported as covering this new upper-wall sightline.");
                }
            }
            finally
            {
                Assert.That(hashes.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value), Is.True,
                    "The birth ray fixture must not rewrite any source scene or scene meta.");
            }
        }

        static Vector3 OnFloor(Scene scene, CharacterController controller, string floorName, float x, float z)
        {
            var floor = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>(true)).Single(c => c.name == floorName);
            Assert.That(floor.enabled && floor.gameObject.activeInHierarchy, Is.True);
            Assert.That(x, Is.InRange(floor.bounds.min.x, floor.bounds.max.x));
            Assert.That(z, Is.InRange(floor.bounds.min.z, floor.bounds.max.z));
            return new Vector3(x, floor.bounds.max.y + (controller.height * .5f - controller.center.y) * controller.transform.lossyScale.y + .04f, z);
        }

        static void Warp(PlayerCombatActor actor, CharacterController controller, Vector3 position, float yaw)
        {
            // Fixture relocation only: no domain snapshots, healing, quest progression, enemy toggles or input robot.
            controller.enabled = false;
            try { actor.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0)); }
            finally { controller.enabled = true; }
            Physics.SyncTransforms();
        }

        static Structure AssertPureDressing(GameObject dressing)
        {
            var structure = new Structure
            {
                renderers = dressing.GetComponentsInChildren<Renderer>(true).Length,
                colliders = dressing.GetComponentsInChildren<Collider>(true).Length,
                behaviours = dressing.GetComponentsInChildren<Behaviour>(true).Length,
                rigidbodies = dressing.GetComponentsInChildren<Rigidbody>(true).Length,
                staticObjects = dressing.GetComponentsInChildren<Transform>(true).Count(t => GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0),
                // Index-count metadata remains readable even when an imported mesh disables CPU vertex access.
                triangles = dressing.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).Sum(f =>
                    Enumerable.Range(0, f.sharedMesh.subMeshCount).Where(i => f.sharedMesh.GetTopology(i) == MeshTopology.Triangles)
                        .Sum(i => (long)f.sharedMesh.GetIndexCount(i) / 3))
            };
            Assert.That(structure.renderers, Is.EqualTo(22));
            Assert.That(structure.colliders, Is.Zero); Assert.That(structure.behaviours, Is.EqualTo(16));
            Assert.That(structure.rigidbodies, Is.Zero); Assert.That(structure.staticObjects, Is.Zero);
            Assert.That(structure.triangles, Is.GreaterThan(0));
            var behaviours = dressing.GetComponentsInChildren<Behaviour>(true);
            Assert.That(behaviours.All(b => b != null && b.GetType() == typeof(CameraOccluder)), Is.True,
                "CameraOccluder is the only named scene-wrapper behaviour exception.");
            var occluders = dressing.GetComponentsInChildren<CameraOccluder>(true);
            Assert.That(occluders.Select(c => c.name), Is.EquivalentTo(ValleyCameraEntries));
            foreach (var occluder in occluders)
            {
                Assert.That(occluder.transform.parent, Is.EqualTo(dressing.transform), "Never attach an exception to root/model/banner.");
                Assert.That(occluder.GetComponents<CameraOccluder>().Length, Is.EqualTo(1));
                Assert.That(occluder.enabled, Is.True);
                var expected = occluder.GetComponentsInChildren<Renderer>(true);
                Assert.That(expected, Is.Not.Empty);
                var array = new SerializedObject(occluder).FindProperty("_renderers");
                Assert.That(array, Is.Not.Null); Assert.That(array.arraySize, Is.EqualTo(expected.Length));
                var actual = Enumerable.Range(0, array.arraySize).Select(i => array.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
                Assert.That(actual, Is.EquivalentTo(expected), "Exactly 100% own renderer references, no null/duplicate/foreign entries.");
            }
            foreach (var t in dressing.GetComponentsInChildren<Transform>(true))
                foreach (var component in t.GetComponents<Component>())
                    Assert.That(component is Transform || component is MeshFilter || component is MeshRenderer ||
                        (component is CameraOccluder && t.parent == dressing.transform && ValleyCameraEntries.Contains(t.name)), Is.True,
                        "All other components remain forbidden on runtime dressing: " + t.name);
            return structure;
        }

        static Frame Capture(Camera camera, PlayerCombatActor actor, GameObject dressing, string directory, string filename,
            string areaLabel, Vector3 requestedPosition, float requestedYaw, int startFrame)
        {
            var animator = actor.GetComponentInChildren<Animator>(); Assert.That(animator, Is.Not.Null);
            Assert.That(animator.enabled && animator.avatar != null && animator.avatar.isValid, Is.True);
            var state = animator.GetCurrentAnimatorStateInfo(0);
            var controller = actor.GetComponent<CharacterController>();
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            var row = new Frame
            {
                image = filename, areaLabel = areaLabel, scene = actor.gameObject.scene.name,
                lightingScene = SceneManager.GetActiveScene().name, camera = camera.name, avatar = animator.avatar.name,
                animatorState = "hash:" + state.fullPathHash + " normalized:" + state.normalizedTime,
                combatState = actor.Model.State.ToString(), combatStateElapsed = actor.Model.StateElapsed,
                requestedActorPosition = requestedPosition, requestedYaw = requestedYaw, actorPosition = actor.transform.position,
                cameraPosition = camera.transform.position, cameraEuler = camera.transform.eulerAngles,
                fieldOfView = camera.fieldOfView, aspect = camera.aspect, nearClip = camera.nearClipPlane, farClip = camera.farClipPlane,
                frame = Time.frameCount, settledPlayerLoopFrames = Time.frameCount - startFrame,
                cameraEnabled = camera.enabled, motorEnabled = actor.GetComponent<ThirdPersonMotor>().enabled,
                controllerEnabled = controller.enabled, controllerGrounded = controller.isGrounded,
                dressing = AssertPureDressing(dressing),
                dressingInFrustum = dressing.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled && r.gameObject.activeInHierarchy && GeometryUtility.TestPlanesAABB(planes, r.bounds))
            };
            // Preserve each actual image before checking its coverage; failed staging is evidence, not silently restaged.
            var target = new RenderTexture(row.width, row.height, 24);
            var texture = new Texture2D(row.width, row.height, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                target.Create(); RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, row.width, row.height), 0, 0); texture.Apply();
                File.WriteAllBytes(directory + "/" + filename, texture.EncodeToPNG());
                File.WriteAllText(directory + "/" + filename + ".json", JsonUtility.ToJson(row, true));
            }
            finally { RenderTexture.active = previous; target.Release(); Object.Destroy(target); Object.Destroy(texture); }
            Assert.That(row.settledPlayerLoopFrames, Is.GreaterThanOrEqualTo(2));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(requestedYaw, row.cameraEuler.y)), Is.LessThan(10), "Formal camera must cover the declared direction.");
            Assert.That(new Vector2(row.actorPosition.x - requestedPosition.x, row.actorPosition.z - requestedPosition.z).magnitude,
                Is.LessThan(.2f), "Do not hide a blocked/invalid staging position by choosing another viewpoint.");
            Assert.That(row.dressingInFrustum, Is.GreaterThan(0));
            return row;
        }

        static string Hash(string path)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
#endif
