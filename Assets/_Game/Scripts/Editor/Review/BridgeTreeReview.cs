using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Review
{
    public static class BridgeTreeReview
    {
        public static string ApplyProtected(bool removeLegacyProxy = false)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                scene.path != "Assets/_Game/Scenes/10_EmberValley.unity" || scene.isDirty || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Saved Valley alone in idle Edit Mode required.");
            string dir = "Builds/ArtReview/0.9.6-camera-cleanup/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(dir);
            File.Copy(scene.path, dir + "/before.unity");
            string physics = M6BoundaryArtSetup.CapturePhysics(scene), protection = CourtyardKitBakeoff.ProtectedState(scene);
            string gameplay = ExistingState(scene);
            var tree = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CameraOccluder>(true)).Single(x => x.name == "BridgeDeadTree");
            Vector3 treeBefore = tree.transform.position;
            string transforms = OtherTransforms(scene, tree.transform);
            int collidersBefore = scene.GetRootGameObjects().Sum(r=>r.GetComponentsInChildren<Collider>(true).Length);
            int behavioursBefore = scene.GetRootGameObjects().Sum(r=>r.GetComponentsInChildren<MonoBehaviour>(true).Length);
            var frozen = Directory.GetFiles("Assets/_Game/Settings", "*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets/StreamingAssets", "*", SearchOption.AllDirectories))
                .Where(p => !p.EndsWith(".meta", StringComparison.Ordinal)).ToDictionary(p=>p, Hash);
            File.WriteAllText(dir+"/physics-before.json",physics); File.WriteAllText(dir+"/protected-before.txt",protection);
            File.WriteAllText(dir+"/gameplay-before.txt",gameplay);
            // Exact approved migration only. Never remove an arbitrary missing or third-party component.
            var proxies = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(b=>b!=null && b.GetType().FullName=="Emberfall.Gameplay.Movement.CameraQueryProxy").ToArray();
            if (proxies.Length > 0)
            {
                if (!removeLegacyProxy || proxies.Length != 1 || proxies[0].gameObject != tree.gameObject)
                    throw new InvalidOperationException("Unexpected legacy proxy scope; no component removed.");
                UnityEngine.Object.DestroyImmediate(proxies[0]);
            }
            BridgeTreePresentationSetup.ApplyToScene(scene);
            bool physicsSame = physics == M6BoundaryArtSetup.CapturePhysics(scene);
            bool protectedSame = protection == CourtyardKitBakeoff.ProtectedState(scene);
            bool gameplaySame = gameplay == ExistingState(scene);
            bool transformsSame = transforms == OtherTransforms(scene, tree.transform);
            if (!physicsSame || !protectedSame || !gameplaySame || !transformsSame) throw new InvalidOperationException("Frozen scene state changed; NOT saved: " + dir);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            byte[] once = File.ReadAllBytes(scene.path);
            BridgeTreePresentationSetup.ApplyToScene(scene);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            var result = new Result { removedProxies=proxies.Length,physicsUnchanged=physicsSame,protectedUnchanged=protectedSame,
                treeBefore=treeBefore,treeAfter=tree.transform.position,otherTransformsUnchanged=transformsSame,
                colliderDelta=scene.GetRootGameObjects().Sum(r=>r.GetComponentsInChildren<Collider>(true).Length)-collidersBefore,
                behaviourDelta=scene.GetRootGameObjects().Sum(r=>r.GetComponentsInChildren<MonoBehaviour>(true).Length)-behavioursBefore,
                existingBehavioursUnchanged=gameplaySame,settingsAndContentUnchanged=frozen.All(p=>Hash(p.Key)==p.Value),
                idempotent=once.SequenceEqual(File.ReadAllBytes(scene.path)) };
            File.WriteAllText(dir+"/result.json",JsonUtility.ToJson(result,true));
            File.WriteAllText(dir+"/physics-after.json",M6BoundaryArtSetup.CapturePhysics(scene));
            File.WriteAllText(dir+"/protected-after.txt",CourtyardKitBakeoff.ProtectedState(scene));
            File.WriteAllText(dir+"/gameplay-after.txt",ExistingState(scene));
            File.WriteAllLines(dir+"/settings-content-hashes.txt",frozen.Select(p=>p.Value+" "+p.Key));
            if(result.colliderDelta != 0 || result.behaviourDelta != -result.removedProxies || !result.settingsAndContentUnchanged || !result.idempotent) throw new InvalidOperationException("Post-save invariant failed: "+dir);
            return dir;
        }
        static string ExistingState(Scene scene) => string.Join("\n",scene.GetRootGameObjects()
            .SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true)).Where(b=>b!=null && b.GetType().FullName!="Emberfall.Gameplay.Movement.CameraQueryProxy")
            .Select(b=>b.GetInstanceID()+"|"+EditorJsonUtility.ToJson(b)).OrderBy(x=>x));
        static string OtherTransforms(Scene scene, Transform excluded) => string.Join("\n",scene.GetRootGameObjects()
            .SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Where(t=>t!=excluded)
            .Select(t=>t.GetInstanceID()+"|"+EditorJsonUtility.ToJson(t)).OrderBy(x=>x));
        static string Hash(string path) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
        [Serializable] sealed class Result
        { public int removedProxies,colliderDelta,behaviourDelta; public Vector3 treeBefore,treeAfter;
          public bool otherTransformsUnchanged,physicsUnchanged,protectedUnchanged,existingBehavioursUnchanged,settingsAndContentUnchanged,idempotent; }
    }
}
