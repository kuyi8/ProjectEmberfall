using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>One static owner-only hollow crest. No pulse, collider, target, timer or animation authority.</summary>
    public static class SummonerIdentitySetup
    {
        public const string CrownName = "Summoner_StaticIdentityCrown";
        const string Folder = "Assets/_Game/Art/SummonerIdentity";
        public static string Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || scene.isDirty ||
                scene.path != AshApproachEncounterSetup.ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Use the clean idle main route; no regeneration.");
            var actors = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SummonerEnemyActor>(true)).ToArray();
            if (actors.Length != 3) throw new InvalidOperationException("Expected the three formal owners only.");
            if (actors.All(a => a.IdentityCrown != null)) return "Existing three owner crests preserved; no writes.";
            string backup = Path.GetFullPath("Builds/SceneBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "/pre-summoner-identity.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(scene.path, backup, false); File.Copy(scene.path + ".meta", backup + ".meta", false);
            foreach (var actor in actors) EnsureCrown(actor);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Identity save failed; preserve " + backup);
            return "Three static owner-only crests authored outside Animator; backup=" + backup;
        }
        public static void EnsureCrown(SummonerEnemyActor actor)
        {
            if (actor.IdentityCrown != null) return;
            if (actor.transform.Find(CrownName) != null) throw new InvalidOperationException("Unbound existing crown preserved for inspection.");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Game/Art", "SummonerIdentity");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/M_SummonerHollowCrown.asset");
            if (mesh == null) { mesh = BuildMesh(); AssetDatabase.CreateAsset(mesh, Folder + "/M_SummonerHollowCrown.asset"); }
            var material = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/M_SummonerIvoryGold.mat");
            if (material == null)
            {
                var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/SummonedSpirit/M_SpiritSplinter.mat");
                material = new Material(source) { name = "M_SummonerIvoryGold" };
                material.SetColor("_BaseColor", new Color(.95f, .82f, .36f));
                material.SetColor("_Color", new Color(.95f, .82f, .36f));
                material.SetColor("_EmissionColor", new Color(.15f, .1f, .025f));
                AssetDatabase.CreateAsset(material, Folder + "/M_SummonerIvoryGold.mat");
            }
            var root = new GameObject(CrownName, typeof(MeshFilter), typeof(MeshRenderer));
            root.transform.SetParent(actor.transform, false); root.transform.localPosition = Vector3.up * 2.24f;
            root.GetComponent<MeshFilter>().sharedMesh = mesh; root.GetComponent<MeshRenderer>().sharedMaterial = material;
            actor.ConfigureIdentityCrown(root);
        }
        private static Mesh BuildMesh()
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            foreach (var center in new[] { new Vector2(-.32f, 0), new Vector2(0, .18f), new Vector2(.32f, 0) })
            {
                int start = vertices.Count;
                var points = new[] { new Vector2(0, .23f), new Vector2(.17f, 0), new Vector2(0, -.23f), new Vector2(-.17f, 0) };
                for (int side = 0; side < 2; side++)
                    for (int rim = 0; rim < 2; rim++)
                        foreach (var point in points) { var p = center + point * (rim == 0 ? 1 : .63f); vertices.Add(new Vector3(p.x, p.y, side == 0 ? -.045f : .045f)); }
                for (int i = 0; i < 4; i++)
                {
                    int next = (i + 1) % 4;
                    Quad(triangles, start + i, start + next, start + 4 + next, start + 4 + i);
                    Quad(triangles, start + 8 + next, start + 8 + i, start + 12 + i, start + 12 + next);
                    Quad(triangles, start + next, start + i, start + 8 + i, start + 8 + next);
                    Quad(triangles, start + 4 + i, start + 4 + next, start + 12 + next, start + 12 + i);
                }
            }
            var mesh = new Mesh { name = "M_SummonerHollowCrown" }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static void Quad(List<int> t, int a, int b, int c, int d)
        { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }
    }
}
