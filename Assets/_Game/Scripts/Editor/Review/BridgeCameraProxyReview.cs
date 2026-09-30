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
    public static class BridgeCameraProxyReview
    {
        public static string ApplyProtected()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                scene.path != "Assets/_Game/Scenes/10_EmberValley.unity" || scene.isDirty || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Saved Valley alone in idle Edit Mode required.");
            string dir = "Builds/ArtReview/0.9.6-camera-proxy/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(dir);
            File.Copy(scene.path, dir + "/before.unity");
            string physics = M6BoundaryArtSetup.CapturePhysics(scene), protection = CourtyardKitBakeoff.ProtectedState(scene);
            string gameplay = ExistingState(scene);
            var frozen = Directory.GetFiles("Assets/_Game/Settings", "*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets/StreamingAssets", "*", SearchOption.AllDirectories))
                .Where(p => !p.EndsWith(".meta", StringComparison.Ordinal)).ToDictionary(p=>p, Hash);
            File.WriteAllText(dir+"/physics-before.json",physics); File.WriteAllText(dir+"/protected-before.txt",protection);
            File.WriteAllText(dir+"/gameplay-before.txt",gameplay);
            int cells = BridgeCameraProxySetup.ApplyToScene(scene);
            bool physicsSame = physics == M6BoundaryArtSetup.CapturePhysics(scene);
            bool protectedSame = protection == CourtyardKitBakeoff.ProtectedState(scene);
            bool gameplaySame = gameplay == ExistingState(scene);
            if (!physicsSame || !protectedSame || !gameplaySame) throw new InvalidOperationException("Frozen scene state changed; NOT saved: " + dir);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            byte[] once = File.ReadAllBytes(scene.path);
            BridgeCameraProxySetup.ApplyToScene(scene);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            var result = new Result { cells=cells,physicsUnchanged=physicsSame,protectedUnchanged=protectedSame,
                existingBehavioursUnchanged=gameplaySame,settingsAndContentUnchanged=frozen.All(p=>Hash(p.Key)==p.Value),
                idempotent=once.SequenceEqual(File.ReadAllBytes(scene.path)) };
            File.WriteAllText(dir+"/result.json",JsonUtility.ToJson(result,true));
            File.WriteAllText(dir+"/physics-after.json",M6BoundaryArtSetup.CapturePhysics(scene));
            File.WriteAllText(dir+"/protected-after.txt",CourtyardKitBakeoff.ProtectedState(scene));
            File.WriteAllText(dir+"/gameplay-after.txt",ExistingState(scene));
            File.WriteAllLines(dir+"/settings-content-hashes.txt",frozen.Select(p=>p.Value+" "+p.Key));
            if(!result.settingsAndContentUnchanged || !result.idempotent) throw new InvalidOperationException("Post-save invariant failed: "+dir);
            return dir;
        }
        static string ExistingState(Scene scene) => string.Join("\n",scene.GetRootGameObjects()
            .SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true)).Where(b=>b!=null &&
                !(b is CameraQueryProxy) && !(b is CameraOccluder && b.name=="BridgeDeadTree"))
            .Select(b=>b.GetInstanceID()+"|"+EditorJsonUtility.ToJson(b)).OrderBy(x=>x));
        static string Hash(string path) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
        [Serializable] sealed class Result
        { public int cells; public bool physicsUnchanged,protectedUnchanged,existingBehavioursUnchanged,settingsAndContentUnchanged,idempotent; }
    }
}
