using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Editor.Setup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    public static class EnvironmentFinishReview
    {
        const string ScenePath = "Assets/_Game/Scenes/10_EmberValley.unity";
        const string OutputRoot = "Builds/ArtReview/0.9.6-environment-finish";
        [Serializable] sealed class Result
        {
            public bool physicsUnchanged, protectedUnchanged, gameplayComponentsUnchanged,
                navigationUnchanged, sceneIdempotent, assetsIdempotent, frozenAssetsUnchanged;
            public int renderersBefore, renderersAfter, addedColliders, addedBehaviours;
            public string scope = "Fixed-camera Editor images only; not a natural walk, player-camera visibility or performance acceptance.";
        }
        public static void Capture()
        {
            if (!UnityEngine.Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Edit Mode batch required.");
            CaptureCore(false);
        }

        // MCP entry: never discard an unsaved user scene or leave the Editor in an empty scene.
        public static void CaptureInteractive()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Idle Edit Mode required.");
            if (UnityEngine.SceneManagement.SceneManager.sceneCount != 1 ||
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath ||
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Open the saved Valley scene alone before R3 capture.");
            CaptureCore(true);
        }

        static void CaptureCore(bool interactive)
        {
            string dir = OutputRoot + "/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(dir);
            var frozen = Directory.GetFiles("Assets/_Game/Settings", "*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets/_Game/Art/Animations", "*", SearchOption.AllDirectories))
                .Concat(new[] { "Assets/_Game/Scenes/20_Sanctum.unity", "Assets/_Game/Scenes/90_CombatGym.unity",
                    "Assets/_Game/Resources/Networking/P_M5_NetworkGymPlayer.prefab", "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab" })
                .ToDictionary(p => p, Hash);
            File.WriteAllLines(dir + "/frozen-before.txt", frozen.Select(p => p.Value + " " + p.Key));
            var scene = interactive ? UnityEngine.SceneManagement.SceneManager.GetActiveScene() : EditorSceneManager.OpenScene(ScenePath);
            File.Copy(ScenePath, dir + "/before.unity");
            string physics = M6BoundaryArtSetup.CapturePhysics(scene), protection = CourtyardKitBakeoff.ProtectedState(scene);
            string gameplay = GameplayState();
            File.WriteAllText(dir + "/physics-before.json", physics);
            File.WriteAllText(dir + "/protected-before.txt", protection);
            File.WriteAllText(dir + "/gameplay-before.txt", gameplay);
            var result = new Result { renderersBefore = Object.FindObjectsOfType<Renderer>(true).Length };
            var before = Shots(dir, "before");
            M6EnvironmentFinishSetup.ApplyToScene(scene);
            result.physicsUnchanged = physics == M6BoundaryArtSetup.CapturePhysics(scene);
            result.protectedUnchanged = protection == CourtyardKitBakeoff.ProtectedState(scene);
            result.gameplayComponentsUnchanged = gameplay == GameplayState();
            File.WriteAllText(dir + "/physics-after.json", M6BoundaryArtSetup.CapturePhysics(scene));
            File.WriteAllText(dir + "/protected-after.txt", CourtyardKitBakeoff.ProtectedState(scene));
            File.WriteAllText(dir + "/gameplay-after.txt", GameplayState());
            if (!result.physicsUnchanged || !result.protectedUnchanged || !result.gameplayComponentsUnchanged)
                throw new InvalidOperationException("R3 mutated protected scene state; source scene not saved: " + dir);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            var once = File.ReadAllBytes(ScenePath);
            var owned = Directory.GetFiles(M6EnvironmentFinishSetup.AssetRoot, "*", SearchOption.AllDirectories).ToDictionary(p => p, Hash);
            M6EnvironmentFinishSetup.ApplyToScene(scene);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            result.sceneIdempotent = once.SequenceEqual(File.ReadAllBytes(ScenePath));
            result.assetsIdempotent = owned.All(p => Hash(p.Key) == p.Value);
            result.frozenAssetsUnchanged = frozen.All(p => Hash(p.Key) == p.Value);
            result.navigationUnchanged = frozen.Where(p => p.Key.Contains("Navigation")).All(p => Hash(p.Key) == p.Value);
            var art = scene.GetRootGameObjects().Single(r => r.name == M6EnvironmentFinishSetup.RootName);
            result.addedColliders = art.GetComponentsInChildren<Collider>(true).Length;
            result.addedBehaviours = art.GetComponentsInChildren<MonoBehaviour>(true).Length;
            result.renderersAfter = Object.FindObjectsOfType<Renderer>(true).Length;
            var after = Shots(dir, "after");
            var sheet = new Texture2D(1920, 540 * before.Length, TextureFormat.RGB24, false);
            for (int i = 0; i < before.Length; i++)
            { int y = (before.Length - i - 1) * 540; sheet.SetPixels(0, y, 960, 540, before[i].GetPixels()); sheet.SetPixels(960, y, 960, 540, after[i].GetPixels()); }
            sheet.Apply(); File.WriteAllBytes(dir + "/comparison.png", sheet.EncodeToPNG());
            foreach (var t in before.Concat(after)) Object.DestroyImmediate(t); Object.DestroyImmediate(sheet);
            // Preserve the actual pre-R3 reference, not an intermediate relabelled as old appearance.
            const string original = OutputRoot + "/20260929-140701-783";
            if (Directory.Exists(original) && original != dir)
            {
                var originalSheet = new Texture2D(1920, 2700, TextureFormat.RGB24, false);
                string[] views = { "overview", "entry", "left", "right", "watchtower" };
                for (int i = 0; i < views.Length; i++)
                {
                    var a = new Texture2D(2, 2); var b = new Texture2D(2, 2);
                    a.LoadImage(File.ReadAllBytes(original + "/before-" + views[i] + ".png"));
                    b.LoadImage(File.ReadAllBytes(dir + "/after-" + views[i] + ".png"));
                    originalSheet.SetPixels(0, (4 - i) * 540, 960, 540, a.GetPixels());
                    originalSheet.SetPixels(960, (4 - i) * 540, 960, 540, b.GetPixels());
                    Object.DestroyImmediate(a); Object.DestroyImmediate(b);
                }
                originalSheet.Apply(); File.WriteAllBytes(dir + "/original-to-final.png", originalSheet.EncodeToPNG());
                File.WriteAllText(dir + "/comparison-provenance.txt", "original-to-final left: " + original + "/before-*.png; right: this run after-*.png. comparison.png is incremental current-before vs current-after.");
                Object.DestroyImmediate(originalSheet);
            }
            File.WriteAllText(dir + "/result.json", JsonUtility.ToJson(result, true));
            Debug.Log("[R3_FINISH] " + dir + " " + JsonUtility.ToJson(result));
            if (!result.sceneIdempotent || !result.assetsIdempotent || !result.frozenAssetsUnchanged ||
                result.addedColliders != 0 || result.addedBehaviours != 0) throw new InvalidOperationException("R3 invariant failed: " + dir);
            File.WriteAllText(OutputRoot + "/latest.txt", dir);
            if (!interactive) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
        static string GameplayState() => string.Join("\n", Object.FindObjectsOfType<MonoBehaviour>(true)
            .OrderBy(c => EnvironmentKitReview.PathOf(c.transform) + c.GetType().Name)
            .Select(c => EnvironmentKitReview.PathOf(c.transform) + "|" + c.GetType().Name + "|" + EditorJsonUtility.ToJson(c)));
        static Texture2D[] Shots(string dir, string label) => new[] {
            EnvironmentKitReview.Shot(dir+"/"+label+"-overview.png",new Vector3(52,65,-8),new Vector3(18,0,34),55),
            EnvironmentKitReview.Shot(dir+"/"+label+"-entry.png",new Vector3(0,3,23),new Vector3(0,1,37),60),
            EnvironmentKitReview.Shot(dir+"/"+label+"-left.png",new Vector3(22,5,54),new Vector3(12,1,44),60),
            EnvironmentKitReview.Shot(dir+"/"+label+"-right.png",new Vector3(27,4,43),new Vector3(39,1,31),60),
            EnvironmentKitReview.Shot(dir+"/"+label+"-watchtower.png",new Vector3(-5,3,36),new Vector3(-14,1,39),60)
        };
        static string Hash(string path)
        { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
