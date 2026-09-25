using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.AI.Unity;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    /// <summary>Only renderer assets change. Collision and navigation are audited, never regenerated.</summary>
    public static class M6BoundaryArtSetup
    {
        public const string ArtRoot = "[Art] M6 Boundary Finish";
        public const string AssetRoot = "Assets/_Game/Art/Meshes/M6Boundary";
        public const string EvidenceRoot = "Builds/ArtReview/0.9.2-boundary";
        public static readonly string[] Scenes = { "10_EmberValley", "20_Sanctum" };
        private static readonly string[] Floors = { "Zone_Camp", "Path_Forest", "Zone_Forest", "Path_Bridge",
            "Zone_Courtyard", "Path_Sanctum", "Zone_Warden", "Path_Return", "ForestShoulder_West",
            "ForestShoulder_East", "CampTreeBerm", "BridgeCliffShell", "CourtyardCliffShell",
            "SanctumCliffShell", "WardenCliffShell", "ReturnCliffShell", "ValleyFloor_VisualOnly", "Sanctum_ArenaFloor" };

        [Serializable] private sealed class PhysicsRow
        {
            public string path, type, serialized;
            public Matrix4x4 localToWorld;
            public Vector3 boundsCenter, boundsSize;
            public bool active;
        }
        [Serializable] private sealed class PhysicsReport
        {
            public string scene;
            public PhysicsRow[] components;
            public string[] navMeshAssets;
        }

        public static string CapturePhysics(Scene scene)
        {
            Physics.SyncTransforms();
            var components = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Component>(true))
                .Where(x => x is Collider || x is NavMeshObstacle).OrderBy(x => PathOf(x.transform) + x.GetType().Name);
            return JsonUtility.ToJson(new PhysicsReport
            {
                scene = scene.name,
                components = components.Select(x => new PhysicsRow
                {
                    path = PathOf(x.transform), type = x.GetType().Name, serialized = EditorJsonUtility.ToJson(x),
                    localToWorld = x.transform.localToWorldMatrix, active = x.gameObject.activeInHierarchy,
                    boundsCenter = x is Collider c ? c.bounds.center : Vector3.zero,
                    boundsSize = x is Collider d ? d.bounds.size : Vector3.zero
                }).ToArray(),
                navMeshAssets = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<NavMeshSurface>(true))
                    .Select(x => AssetDatabase.GetAssetPath(x.navMeshData)).OrderBy(x => x).Select(x =>
                    {
                        using (var hash = SHA256.Create())
                            return x + ":" + (File.Exists(x) ? BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(x))) : "none");
                    }).ToArray()
            }, true);
        }

        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        [MenuItem("Emberfall/Setup/Apply 0.9.2 Boundary Finish")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            if (!UnityEngine.Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(EvidenceRoot);
            foreach (string name in Scenes)
            {
                var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/" + name + ".unity");
                string before = CapturePhysics(scene);
                ApplyToScene(scene);
                string after = CapturePhysics(scene);
                if (before != after) throw new InvalidOperationException("Boundary art changed physics/navigation: " + name);
                // Keep the first, pre-art baseline, even when the setup is invoked again by a build.
                string baseline = EvidenceRoot + "/" + name + "-colliders-before.json";
                if (!File.Exists(baseline)) File.WriteAllText(baseline, before);
                string firstAfter = EvidenceRoot + "/" + name + "-colliders-after.json";
                if (!File.Exists(firstAfter)) File.WriteAllText(firstAfter, after);
                File.WriteAllText(EvidenceRoot + "/" + name + "-colliders-current.json", after);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[BOUNDARY_ART_COMPLETE] physicsUnchanged=true navMeshRebaked=false");
        }

        public static void ApplyToScene(Scene scene)
        {
            EnsureFolder(AssetRoot);
            var all = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
            ApplyFloorFaces(all);
            if (all.Any(x => x.name == ArtRoot)) return;
            var root = new GameObject(ArtRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            var stone = new[] { Material("M_BoundaryStoneA", new Color(.33f,.36f,.28f)),
                Material("M_BoundaryStoneB", new Color(.41f,.43f,.34f)), Material("M_BoundaryStoneC", new Color(.28f,.32f,.27f)),
                Material("M_BoundaryMortar", new Color(.12f,.16f,.14f)) };
            var cliff = CliffMaterials();
            foreach (var t in all)
            {
                var marker = t.GetComponent<EncounterBoundaryVisualMarker>();
                bool wall = t.name == "Camp_BackWall" || t.name == "Courtyard_NorthWall" ||
                    t.name == "Warden_EastWall" || t.name == "Warden_SouthWall" || t.name.StartsWith("Sanctum_Wall");
                Renderer renderer = marker != null ? marker.VisibleRenderer : wall ? t.GetComponent<Renderer>() : null;
                if (renderer != null && renderer.TryGetComponent(out MeshFilter filter))
                {
                    Vector3 size = renderer.bounds.size;
                    // Work in the renderer's original cube coordinates, preserving its exact authored bounds.
                    var mesh = BuildMasonry(renderer.transform.lossyScale);
                    filter.sharedMesh = SaveMesh(mesh, scene.name + "_" + t.name);
                    renderer.sharedMaterials = stone;
                    renderer.enabled = true;
                    if (size.sqrMagnitude == 0) throw new InvalidOperationException("Invalid wall size: " + t.name);
                }
                if (((t.name.StartsWith("GateBlocker_") && t.GetComponent<Collider>() != null) || t.name == "Sanctum_EntranceSeal") &&
                    t.TryGetComponent(out MeshRenderer gate))
                    gate.sharedMaterial = GateMaterial();
            }
            // Vertical skirts only: no new walkable top faces, no collider/obstacle, no NavMesh bake.
            var floors = all.Where(x => Floors.Contains(x.name) && x.GetComponent<Renderer>() != null)
                .Select(x => x.GetComponent<Renderer>()).ToArray();
            foreach (var floor in floors)
            {
                var mesh = BuildSkirt(floor.bounds, floors.Select(x => x.bounds).ToArray());
                if (mesh.vertexCount == 0) { UnityEngine.Object.DestroyImmediate(mesh); continue; }
                var skirt = new GameObject("RockSkirt_" + floor.name, typeof(MeshFilter), typeof(MeshRenderer));
                skirt.transform.SetParent(root.transform, false);
                skirt.GetComponent<MeshFilter>().sharedMesh = SaveMesh(mesh, scene.name + "_" + skirt.name);
                skirt.GetComponent<MeshRenderer>().sharedMaterials = cliff;
            }
        }

        private static Material[] CliffMaterials() => new[] {
            Material("M_BoundaryCliff", new Color(.26f,.29f,.22f)),
            Material("M_BoundaryCliffDark", new Color(.21f,.25f,.18f)),
            Material("M_BoundaryCliffLight", new Color(.32f,.35f,.25f)) };

        private static void ApplyFloorFaces(Transform[] all)
        {
            foreach (var floor in all.Where(x => Floors.Contains(x.name)))
            {
                if (!floor.TryGetComponent(out MeshFilter filter) || !floor.TryGetComponent(out MeshRenderer renderer)) continue;
                if (filter.sharedMesh.name == "BoundaryFloorFaces") continue;
                // Only the authored Unity cubes are supported. Keep vertices, normals, UVs and
                // original top material; split the existing side faces into a rock material slot.
                if (filter.sharedMesh.vertexCount != 24) throw new InvalidOperationException("Expected cube floor: " + floor.name);
                filter.sharedMesh = SaveMesh(BuildFloorFaces(filter.sharedMesh), "BoundaryFloorFaces");
                renderer.sharedMaterials = new[] {renderer.sharedMaterial, CliffMaterials()[0]};
            }
        }

        public static Mesh BuildFloorFaces(Mesh source)
        {
            var mesh = UnityEngine.Object.Instantiate(source);
            var top = new List<int>(); var sides = new List<int>();
            var indices = source.triangles; var normals = source.normals;
            for (int i=0; i<indices.Length; i+=3)
            {
                var destination = normals[indices[i]].y > .5f ? top : sides;
                destination.AddRange(new[]{indices[i],indices[i+1],indices[i+2]});
            }
            mesh.subMeshCount=2; mesh.SetTriangles(top,0); mesh.SetTriangles(sides,1);
            return mesh;
        }

        [MenuItem("Emberfall/Setup/Rebuild 0.9.2 Rock Facing")]
        public static void RebuildRockFacing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            if (!UnityEngine.Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (string name in Scenes)
            {
                var scene=EditorSceneManager.OpenScene("Assets/_Game/Scenes/"+name+".unity");
                string before=CapturePhysics(scene);
                var all=scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Transform>(true)).ToArray();
                ApplyFloorFaces(all);
                var floors=all.Where(x=>Floors.Contains(x.name) && x.GetComponent<Renderer>()!=null)
                    .ToDictionary(x=>x.name,x=>x.GetComponent<Renderer>().bounds);
                foreach(var t in all.Where(x=>x.name.StartsWith("RockSkirt_")))
                {
                    var filter=t.GetComponent<MeshFilter>();
                    var rebuilt=BuildSkirt(floors[t.name.Substring("RockSkirt_".Length)],floors.Values.ToArray());
                    rebuilt.name=filter.sharedMesh.name;
                    EditorUtility.CopySerialized(rebuilt,filter.sharedMesh);
                    UnityEngine.Object.DestroyImmediate(rebuilt);
                    EditorUtility.SetDirty(filter.sharedMesh);
                    t.GetComponent<Renderer>().sharedMaterials=CliffMaterials();
                }
                if(before!=CapturePhysics(scene))throw new InvalidOperationException("Rock facing changed physics: "+name);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[BOUNDARY_ROCK_REFINED] physicsUnchanged=true");
        }

        public static void RepairCandidateGateFilter()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity");
            string before=CapturePhysics(scene);
            var legacy=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Game/Art/Materials/M6Art/M_ArenaGateBarrier.mat");
            foreach (var renderer in scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Renderer>(true))
                .Where(x=>x.name.StartsWith("GateBlocker_")))
            {
                // The first candidate's prefix also matched the existing *_ArtFacade. Restore only our
                // accidental shader substitution, to the material authored by M5dArenaRepair.
                if(renderer.GetComponent<Collider>()==null && renderer.sharedMaterial.name=="M_BoundaryRuneBarrier")
                    renderer.sharedMaterial=legacy;
                Debug.Log("[BOUNDARY_GATE_RENDERER] " + PathOf(renderer.transform) + " material=" + renderer.sharedMaterial.name +
                    " collider=" + (renderer.GetComponent<Collider>() != null));
            }
            if(before!=CapturePhysics(scene))throw new InvalidOperationException("Gate filter repair changed physics.");
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }

        private static Material GateMaterial()
        {
            const string path = "Assets/_Game/Art/Materials/M6Art/M_BoundaryRuneBarrier.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Emberfall/BoundaryRuneBarrier"));
            material.SetColor("_BaseColor", new Color(.08f,.65f,.8f,.62f));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Material Material(string name, Color color)
        {
            string path = "Assets/_Game/Art/Materials/M6Art/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .12f);
            AssetDatabase.CreateAsset(material, path); return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

        private static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = AssetRoot + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) { UnityEngine.Object.DestroyImmediate(mesh); return existing; }
            mesh.name = name; AssetDatabase.CreateAsset(mesh, path); return mesh;
        }

        public static Mesh BuildMasonry(Vector3 scale)
        {
            var builder = new MeshBuilder(4);
            // Full backing keeps every collision face physically legible between the recessed stone seams.
            bool alongX = Mathf.Abs(scale.x) >= Mathf.Abs(scale.z);
            builder.Box(Vector3.zero, Vector3.one, 3, alongX);
            float length = Mathf.Abs(alongX ? scale.x : scale.z), height = Mathf.Abs(scale.y);
            int rows = Mathf.Max(2, Mathf.CeilToInt(height / .38f));
            float rowHeight = 1f / rows;
            int columns = Mathf.Max(2, Mathf.CeilToInt(length / 1.1f));
            for (int row = 0; row < rows; row++)
            for (int col = -1; col < columns; col++)
            {
                float start = Mathf.Max(-.5f, -.5f + (col + (row % 2) * .5f) / columns);
                float end = Mathf.Min(.5f, -.5f + (col + 1 + (row % 2) * .5f) / columns);
                if (end <= start) continue;
                float seam = .012f / Mathf.Max(.1f, length);
                float ySeam = .012f / Mathf.Max(.1f, height);
                // Extruded bevel is inside the existing wall thickness; backing is inset behind its face.
                Vector3 centre = new Vector3((start + end) * .5f, -.5f + (row + .5f) * rowHeight, 0);
                Vector3 size = new Vector3(end - start - seam, rowHeight - ySeam, 1f);
                builder.BevelBlock(centre, size, (row * 7 + col + columns) % 3, alongX);
            }
            return builder.Finish("Masonry");
        }

        public static Mesh BuildSkirt(Bounds bounds, Bounds[] all)
        {
            var builder = new MeshBuilder(3);
            for (int side = 0; side < 4; side++)
            {
                bool x = side < 2;
                float fixedAxis = side % 2 == 0 ? (x ? bounds.min.z : bounds.min.x) : (x ? bounds.max.z : bounds.max.x);
                float min = x ? bounds.min.x : bounds.min.z, max = x ? bounds.max.x : bounds.max.z;
                int segments = Mathf.CeilToInt((max - min) / 1.5f);
                for (int i = 0; i < segments; i++)
                {
                    float a = Mathf.Lerp(min, max, i / (float)segments), b = Mathf.Lerp(min, max, (i + 1f) / segments);
                    Vector3 left = x ? new Vector3(a, bounds.max.y - .035f, fixedAxis) : new Vector3(fixedAxis, bounds.max.y - .035f, a);
                    Vector3 right = x ? new Vector3(b, left.y, fixedAxis) : new Vector3(fixedAxis, left.y, b);
                    Vector3 outside = (left + right) * .5f + (x ? Vector3.forward : Vector3.right) * (side % 2 == 0 ? -.03f : .03f);
                    if (all.Any(other => other != bounds && outside.x > other.min.x && outside.x < other.max.x &&
                        outside.z > other.min.z && outside.z < other.max.z && other.max.y >= bounds.max.y - .08f)) continue;
                    float depth = bounds.size.x > 100 ? 16 : 4.5f;
                    Vector3 inward = (x ? Vector3.forward : Vector3.right) * (side % 2 == 0 ? 1 : -1);
                    Vector3 lowerLeft = left + Vector3.down * (depth + .2f * (i % 3)) + inward * .35f;
                    Vector3 lowerRight = right + Vector3.down * (depth + .2f * ((i + 1) % 3)) + inward * .35f;
                    Vector3 middle = (left + right) * .5f + Vector3.down * depth * .4f + inward * (.08f + .06f * (i % 3));
                    // Double-sided triangles are explicit geometry; standard Lit/shadows remain usable.
                    builder.Triangle(left, right, middle, (i+side)%3, true);
                    builder.Triangle(right, lowerRight, middle, (i+side+1)%3, true);
                    builder.Triangle(lowerRight, lowerLeft, middle, (i+side)%3, true);
                    builder.Triangle(lowerLeft, left, middle, (i+side+2)%3, true);
                }
            }
            return builder.Finish("RockSkirt");
        }

        private sealed class MeshBuilder
        {
            private readonly List<Vector3> _vertices = new List<Vector3>();
            private readonly List<int>[] _indices;
            public MeshBuilder(int materials) { _indices = Enumerable.Range(0, materials).Select(_ => new List<int>()).ToArray(); }
            public void Triangle(Vector3 a, Vector3 b, Vector3 c, int material, bool twoSided = false)
            {
                int index = _vertices.Count; _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
                _indices[material].AddRange(new[] { index, index + 1, index + 2 });
                if (twoSided) Triangle(c, b, a, material);
            }
            private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int m)
            { Triangle(a,b,c,m); Triangle(a,c,d,m); }
            public void Box(Vector3 centre, Vector3 size, int m, bool alongX)
            {
                // Inset only the broad faces so stones do not depth-fight with their mortar backing.
                var h = size * .5f;
                if (alongX) h.z *= .82f; else h.x *= .82f;
                Quad(centre+new Vector3(-h.x,-h.y,-h.z),centre+new Vector3(-h.x,h.y,-h.z),centre+new Vector3(h.x,h.y,-h.z),centre+new Vector3(h.x,-h.y,-h.z),m);
                Quad(centre+new Vector3(h.x,-h.y,h.z),centre+new Vector3(h.x,h.y,h.z),centre+new Vector3(-h.x,h.y,h.z),centre+new Vector3(-h.x,-h.y,h.z),m);
                Quad(centre+new Vector3(-h.x,-h.y,h.z),centre+new Vector3(-h.x,h.y,h.z),centre+new Vector3(-h.x,h.y,-h.z),centre+new Vector3(-h.x,-h.y,-h.z),m);
                Quad(centre+new Vector3(h.x,-h.y,-h.z),centre+new Vector3(h.x,h.y,-h.z),centre+new Vector3(h.x,h.y,h.z),centre+new Vector3(h.x,-h.y,h.z),m);
                Quad(centre+new Vector3(-h.x,h.y,-h.z),centre+new Vector3(-h.x,h.y,h.z),centre+new Vector3(h.x,h.y,h.z),centre+new Vector3(h.x,h.y,-h.z),m);
                Quad(centre+new Vector3(-h.x,-h.y,h.z),centre+new Vector3(-h.x,-h.y,-h.z),centre+new Vector3(h.x,-h.y,-h.z),centre+new Vector3(h.x,-h.y,h.z),m);
            }
            public void BevelBlock(Vector3 centre, Vector3 size, int m, bool alongX)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 Point(float x, float y, float z)
                    { var p = centre + new Vector3(x * size.x, y * size.y, z); return alongX ? p : new Vector3(p.z,p.y,p.x); }
                    var rim = new[] {Point(-.5f,-.5f,sign*.41f),Point(-.5f,.5f,sign*.41f),Point(.5f,.5f,sign*.41f),Point(.5f,-.5f,sign*.41f)};
                    var face = new[] {Point(-.46f,-.42f,sign*.5f),Point(-.46f,.42f,sign*.5f),Point(.46f,.42f,sign*.5f),Point(.46f,-.42f,sign*.5f)};
                    // Both orientations keep normals pointing out, including Z-aligned walls.
                    if ((sign > 0) == alongX) { Array.Reverse(rim); Array.Reverse(face); }
                    Quad(face[0],face[1],face[2],face[3],m);
                    for (int i=0;i<4;i++) Quad(rim[i],rim[(i+1)%4],face[(i+1)%4],face[i],m);
                }
            }
            public Mesh Finish(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = _indices.Length };
                mesh.SetVertices(_vertices);
                for (int i=0;i<_indices.Length;i++) mesh.SetTriangles(_indices[i],i);
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
        }
    }
}
