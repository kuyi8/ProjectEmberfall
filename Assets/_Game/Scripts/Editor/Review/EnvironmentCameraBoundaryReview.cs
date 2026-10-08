using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.AI.Unity;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Review
{
    public static class EnvironmentCameraBoundaryReview
    {
        public static string Apply()
        {
            var active = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || active.isDirty ||
                active.path != "Assets/_Game/Scenes/10_EmberValley.unity" || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Clean idle formal Valley required.");
            string output = "Builds/ArtReview/batch-A-camera/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(output);
            foreach (string name in new[] { "10_EmberValley", "90_CombatGym" })
            {
                string path = "Assets/_Game/Scenes/" + name + ".unity";
                var scene = EditorSceneManager.OpenScene(path);
                string before = CaptureFrozen(scene);
                File.WriteAllText(output + "/" + name + "-before.json", before);
                File.Copy(path, output + "/" + name + "-before.unity");
                int count = M6EnvironmentSetup.RegisterCameraOccluders(scene);
                string after = CaptureFrozen(scene);
                File.WriteAllText(output + "/" + name + "-after.json", after);
                if (before != after) throw new InvalidOperationException("Frozen object delta; preserve unsaved scene and stop: " + name);
                EditorSceneManager.SaveScene(scene);
                string firstHash = Hash(path);
                int repeated = M6EnvironmentSetup.RegisterCameraOccluders(scene);
                EditorSceneManager.SaveScene(scene);
                if (repeated != 0 || Hash(path) != firstHash) throw new InvalidOperationException("Camera authoring not byte-idempotent: " + name);
                var root = scene.GetRootGameObjects().Single(r => r.name == M6EnvironmentSetup.RootName);
                var rays = root.GetComponentsInChildren<CameraOccluder>(true).Select(o => {
                    var renderer = o.GetComponentInChildren<Renderer>(); var b = renderer.bounds;
                    Vector3 origin = b.center - o.transform.forward * (b.size.magnitude + 1);
                    float maximum = b.size.magnitude * 2 + 2;
                    bool query = o.TryGetDistance(new Ray(origin, o.transform.forward), maximum, .22f, out var distance);
                    return new RayRow { name = o.name, origin = origin, direction = o.transform.forward,
                        hit = query, distance = distance, maximum = maximum };
                }).ToArray();
                if (rays.Any(r => !r.hit || r.distance < 0 || r.distance > r.maximum)) throw new InvalidOperationException("World-space occluder ray failed.");
                File.WriteAllText(output + "/" + name + "-result.json", JsonUtility.ToJson(new Result {
                    addedCameraOccluders = count, repeatedAdditions = repeated, frozenEqual = before == after,
                    byteIdempotent = true, sceneHash = firstHash, rays = rays
                }, true));
            }
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity");
            return output;
        }

        public static string CaptureFrozen(Scene scene)
        {
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Component>(true)).Where(c => c != null).ToArray();
            return JsonUtility.ToJson(new Snapshot {
                physics = M6BoundaryArtSetup.CapturePhysics(scene),
                transforms = all.OfType<Transform>().OrderBy(PathOf).Select(t => PathOf(t) + ":" + JsonUtility.ToJson(t.localToWorldMatrix)).ToArray(),
                behaviours = all.OfType<Behaviour>().Where(b => !(b is CameraOccluder)).OrderBy(b => PathOf(b.transform) + b.GetType().FullName)
                    .Select(b => PathOf(b.transform) + ":" + b.GetType().FullName + ":" + EditorJsonUtility.ToJson(b)).ToArray(),
                rigidbodies = all.OfType<Rigidbody>().OrderBy(r => PathOf(r.transform)).Select(r => PathOf(r.transform) + ":" + EditorJsonUtility.ToJson(r)).ToArray(),
                members = all.OfType<CombatTarget>().OrderBy(a => a.name).Select(a => a.name + ":" + a.transform.position.ToString("R") + ":" + a.transform.eulerAngles.ToString("R")).ToArray(),
                arenas = all.OfType<CombatEncounterCoordinator>().OrderBy(a => a.TelemetrySegment).Select(a => EditorJsonUtility.ToJson(a)).ToArray(),
                leashes = all.OfType<EncounterLeash>().OrderBy(a => a.name).Select(a => a.name + ":" + EditorJsonUtility.ToJson(a)).ToArray()
            }, true);
        }
        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        static string Hash(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        [Serializable] sealed class Snapshot { public string physics; public string[] transforms, behaviours, rigidbodies, members, arenas, leashes; }
        [Serializable] sealed class RayRow { public string name; public Vector3 origin, direction; public bool hit; public float distance, maximum; }
        [Serializable] sealed class Result
        {
            public string scope = "Actual world-space camera-query rays on loaded kit wall bounds; NOT user feel or natural-camera-route acceptance. Frozen snapshot excludes only CameraOccluder; physical/gameplay/nav/actor data must match exactly.";
            public int addedCameraOccluders, repeatedAdditions; public bool frozenEqual, byteIdempotent; public string sceneHash; public RayRow[] rays;
        }
    }
}
