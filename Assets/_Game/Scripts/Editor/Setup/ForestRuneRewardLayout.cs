using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    // User-requested relocation: reward roots (including their existing trigger) follow the seal.
    public static class ForestRuneRewardLayout
    {
        public static void ApplyToScene(Scene scene)
        {
            var seal = Find(scene, "Seal_Forest");
            Place(Find(scene, "RuneChoice_Ember"), seal.position + new Vector3(-1.8f, -.47f, -1.9f));
            Place(Find(scene, "RuneChoice_Guard"), seal.position + new Vector3(.6f, -.47f, -1.9f));
        }

        static void Place(Transform rune, Vector3 position)
        {
            // Position only: do not change interaction size, role, reward, active state or marker shape.
            var colliders = rune.GetComponentsInChildren<Collider>(true);
            if (colliders.Length != 1 || !colliders[0].isTrigger)
                throw new InvalidOperationException("Reward relocation expects one trigger only: " + rune.name);
            rune.position = position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rune);
            EditorUtility.SetDirty(rune);
        }

        public static string ApplyProtected()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                scene.path != "Assets/_Game/Scenes/10_EmberValley.unity" || scene.isDirty || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Saved Valley alone in idle Edit Mode required.");
            var ember = Find(scene, "RuneChoice_Ember");
            var guard = Find(scene, "RuneChoice_Guard");
            var beforeEmber = ember.position; var beforeGuard = guard.position;
            string state = Capture(scene, ember, guard);
            string folder = "Builds/ArtReview/0.9.6-rune-rewards/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(folder);
            File.Copy(scene.path, folder + "/before.unity");
            var frozen = Directory.GetFiles("Assets/_Game/Settings", "*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets/StreamingAssets", "*", SearchOption.AllDirectories))
                .ToDictionary(p => p, Hash);
            ApplyToScene(scene);
            if (state != Capture(scene, ember, guard)) throw new InvalidOperationException("Unexpected component changes; not saved.");
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            byte[] once = File.ReadAllBytes(scene.path);
            ApplyToScene(scene);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            var result = new Result { emberBefore=beforeEmber,guardBefore=beforeGuard,emberAfter=ember.position,guardAfter=guard.position,
                otherSerializedComponentsUnchanged=state==Capture(scene,ember,guard),
                settingsContentUnchanged=frozen.All(p=>p.Value==Hash(p.Key)),idempotent=once.SequenceEqual(File.ReadAllBytes(scene.path)) };
            File.WriteAllText(folder+"/result.json",JsonUtility.ToJson(result,true));
            if (!result.otherSerializedComponentsUnchanged || !result.settingsContentUnchanged || !result.idempotent)
                throw new InvalidOperationException("Reward layout validation failed: " + folder);
            return folder;
        }

        static Transform Find(Scene scene, string name) => scene.GetRootGameObjects()
            .SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>t.name==name);
        static string Capture(Scene scene, Transform ember, Transform guard) => string.Join("\n",scene.GetRootGameObjects()
            .SelectMany(r=>r.GetComponentsInChildren<Component>(true)).Where(c=>c!=null && c!=ember && c!=guard)
            .Select(c=>c.GetInstanceID()+"|"+EditorJsonUtility.ToJson(c)).OrderBy(x=>x));
        static string Hash(string path) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))); }
        [Serializable] sealed class Result
        {
            public Vector3 emberBefore,guardBefore,emberAfter,guardAfter;
            public bool otherSerializedComponentsUnchanged,settingsContentUnchanged,idempotent;
        }
    }
}
