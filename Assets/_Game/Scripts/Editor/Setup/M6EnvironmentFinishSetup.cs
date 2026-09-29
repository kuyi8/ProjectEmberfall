using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>R3 visual dressing only. No gameplay components, physics, navigation or gate edits.</summary>
    public static class M6EnvironmentFinishSetup
    {
        public const string RootName = "[Art] M6 Environment Finish";
        public const string AssetRoot = "Assets/_Game/Art/M6Environment/Finish";
        static readonly string[] FloorNames = {
            "Zone_Camp", "Path_Forest", "Zone_Forest", "Path_Bridge", "Zone_Courtyard",
            "Path_Sanctum", "Zone_Warden", "Path_Return", "ForestShoulder_West", "ForestShoulder_East",
            "CampTreeBerm", "BridgeCliffShell", "CourtyardCliffShell", "SanctumCliffShell",
            "WardenCliffShell", "ReturnCliffShell", "OldWatchtower_Platform"
        };

        public static void ApplyToScene(Scene scene)
        {
            if (scene.name != "10_EmberValley") return;
            if (!AssetDatabase.IsValidFolder(AssetRoot)) AssetDatabase.CreateFolder(M6EnvironmentSetup.AssetRoot, "Finish");
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var root = scene.GetRootGameObjects().FirstOrDefault(r => r.name == RootName) ?? new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var expected = new HashSet<string>();
            var cliff = FlatMaterial("M_Cliff", new Color(.32f, .37f, .31f));
            var distantRock = FlatMaterial("M_DistantRock", new Color(.36f, .43f, .42f));
            var earth = FlatMaterial("M_WeatheredEarth", new Color(.34f, .39f, .27f));
            var towerStone = new[] {
                FlatMaterial("M_TowerStoneA", new Color(.48f, .52f, .48f)),
                FlatMaterial("M_TowerStoneB", new Color(.44f, .48f, .45f)),
                FlatMaterial("M_TowerStoneC", new Color(.51f, .54f, .50f)),
                FlatMaterial("M_TowerMortar", new Color(.28f, .32f, .29f))
            };
            // Dress the three existing greybox solids inside their exact unit-cube bounds.
            // Discovery trigger, reward, pillar positions and all BoxColliders remain untouched.
            foreach (string name in new[] { "OldWatchtower_Base", "OldWatchtower_Pillar_A", "OldWatchtower_Pillar_B" })
            {
                var t = all.Single(x => x.name == name);
                var renderer = t.GetComponent<MeshRenderer>();
                Bounds before = renderer.bounds;
                t.GetComponent<MeshFilter>().sharedMesh = SaveMesh(M6BoundaryArtSetup.BuildMasonry(t.lossyScale), name);
                renderer.sharedMaterials = towerStone;
                if (Vector3.Distance(before.center, renderer.bounds.center) > .001f ||
                    Vector3.Distance(before.size, renderer.bounds.size) > .001f)
                    throw new InvalidOperationException("Watchtower dressing changed visible footprint: " + name);
            }
            var floors = all.Where(t => FloorNames.Contains(t.name) && t.GetComponent<Renderer>() != null)
                .Select(t => t.GetComponent<Renderer>()).ToArray();

            foreach (var floor in floors)
            {
                string name = "Cliff_" + floor.name;
                Mesh generated = BuildCliff(floor.bounds, floors.Select(f => f.bounds).ToArray());
                if (generated.vertexCount == 0) { Object.DestroyImmediate(generated); continue; }
                var t = Child(root.transform, name, expected);
                var filter = t.GetComponent<MeshFilter>();
                if (filter == null) filter = t.gameObject.AddComponent<MeshFilter>();
                var renderer = t.GetComponent<MeshRenderer>();
                if (renderer == null) renderer = t.gameObject.AddComponent<MeshRenderer>();
                filter.sharedMesh = SaveMesh(generated, name);
                renderer.sharedMaterial = cliff;
                // Replace only the old visual skirt, not its physical support or top surface.
                foreach (var old in all.Where(x => x.name == "RockSkirt_" + floor.name))
                    if (old.TryGetComponent<Renderer>(out var oldRenderer)) oldRenderer.enabled = false;
            }

            var kit = scene.GetRootGameObjects().Single(r => r.name == M6EnvironmentSetup.RootName);
            foreach (var t in kit.GetComponentsInChildren<Transform>(true).Where(t => t.parent == kit.transform && t.name.Contains("_Dirt_")))
                foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true)) r.sharedMaterial = earth;

            // Original gameplay/collision footprint ends well inside these background groups.
            // Trees stand on the existing distant floor (-4.7m), not on phantom walkable ledges.
            var tree = all.Single(t => t.name == "CampTree");
            var rock = all.Single(t => t.name == "WardenRock_A");
            var groups = new[] {
                new Vector3(-28, -4.65f, 26), new Vector3(-29, -4.65f, 48),
                new Vector3(2, -4.65f, 68), new Vector3(27, -4.65f, 74),
                new Vector3(67, -4.65f, 43), new Vector3(70, -4.65f, 12)
            };
            for (int i = 0; i < groups.Length; i++)
            {
                FitClone(root.transform, expected, "BackdropRock_" + i, rock, groups[i] + new Vector3(1, -.1f, 4),
                    new Vector3(15 + i % 2 * 3, 8 + i % 3 * 2, 10), i * 43, distantRock);
                for (int j = 0; j < 3; j++)
                    FitClone(root.transform, expected, "BackdropTree_" + i + "_" + j, tree,
                        groups[i] + new Vector3((j - 1) * 4.4f, 0, j % 2 * 4),
                        Vector3.one * (j == 1 ? 14 : 10 + i % 3), i * 31 + j * 67, null);
            }
            // Right-rear sightline deliberately stays open between north and east groups.
            foreach (Transform t in root.transform.Cast<Transform>().ToArray())
                if (!expected.Contains(t.name)) Object.DestroyImmediate(t.gameObject);
        }

        static Transform Child(Transform root, string name, HashSet<string> expected)
        {
            expected.Add(name);
            var t = root.Find(name);
            if (t == null) { t = new GameObject(name).transform; t.SetParent(root, false); }
            return t;
        }

        static void FitClone(Transform root, HashSet<string> expected, string name, Transform source,
            Vector3 foot, Vector3 size, float yaw, Material material)
        {
            var holder = Child(root, name, expected);
            // Preserve IDs on repeated application. Reset the holder before world-bounds fitting.
            holder.SetPositionAndRotation(Vector3.zero, Quaternion.identity); holder.localScale = Vector3.one;
            if (holder.childCount == 0)
            {
                var model = Object.Instantiate(source.gameObject, holder, false); model.name = "Model";
                foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var c in model.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(c);
                foreach (var c in model.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(c);
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
            }
            var renderers = holder.GetComponentsInChildren<Renderer>(true);
            Bounds b = BoundsOf(renderers);
            // Trees keep proportions; rock groups can stretch to form a geological silhouette.
            Vector3 scale = material == null ? Vector3.one * (size.y / b.size.y)
                : new Vector3(size.x / b.size.x, size.y / b.size.y, size.z / b.size.z);
            holder.localScale = scale; holder.rotation = Quaternion.Euler(0, yaw, 0);
            b = BoundsOf(renderers);
            holder.position += foot - new Vector3(b.center.x, b.min.y, b.center.z);
            foreach (var r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (material != null) r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
            }
        }

        static Bounds BoundsOf(Renderer[] renderers)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        static Material FlatMaterial(string name, Color color)
        {
            string path = AssetRoot + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", .05f); EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh SaveMesh(Mesh generated, string name)
        {
            string path = AssetRoot + "/" + name + ".asset";
            generated.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(generated, path); return generated; }
            EditorUtility.CopySerialized(generated, existing); Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing); return existing;
        }

        // Sheer, top-less rock facing: no apparent new traversable top beyond the physical platform.
        public static Mesh BuildCliff(Bounds bounds, Bounds[] all)
        {
            var vertices = new List<Vector3>(); var indices = new List<int>();
            void Triangle(Vector3 a, Vector3 b, Vector3 c)
            {
                int n = vertices.Count;
                vertices.AddRange(new[] { a, b, c, c, b, a });
                indices.AddRange(new[] { n, n + 1, n + 2, n + 3, n + 4, n + 5 });
            }
            for (int side = 0; side < 4; side++)
            {
                bool x = side < 2; float sign = side % 2 == 0 ? -1 : 1;
                float fixedAxis = sign < 0 ? (x ? bounds.min.z : bounds.min.x) : (x ? bounds.max.z : bounds.max.x);
                float min = x ? bounds.min.x : bounds.min.z, max = x ? bounds.max.x : bounds.max.z;
                var normal = (x ? Vector3.forward : Vector3.right) * sign;
                int count = Mathf.CeilToInt((max - min) / 3f);
                // Split at every occluder endpoint before testing visibility. A midpoint
                // alone cannot represent a partially covered three-metre segment.
                float probe = fixedAxis + sign * .04f;
                var neighbours = all.Where(o => o != bounds && o.max.y >= bounds.max.y - .08f &&
                    probe > (x ? o.min.z : o.min.x) && probe < (x ? o.max.z : o.max.x)).ToArray();
                var cuts = new SortedSet<float>();
                for (int i = 0; i <= count; i++) cuts.Add(Mathf.Lerp(min, max, i / (float)count));
                foreach (var o in neighbours)
                {
                    cuts.Add(Mathf.Clamp(x ? o.min.x : o.min.z, min, max));
                    cuts.Add(Mathf.Clamp(x ? o.max.x : o.max.z, min, max));
                }
                var points = cuts.ToArray();
                for (int i = 0; i < points.Length - 1; i++)
                {
                    float a = points[i], b = points[i + 1];
                    if (b - a < .00001f) continue;
                    float midpoint = (a + b) * .5f;
                    if (neighbours.Any(o => midpoint > (x ? o.min.x : o.min.z) &&
                        midpoint < (x ? o.max.x : o.max.z))) continue;
                    Vector3 left = x ? new Vector3(a, bounds.max.y - .04f, fixedAxis) : new Vector3(fixedAxis, bounds.max.y - .04f, a);
                    Vector3 right = x ? new Vector3(b, left.y, fixedAxis) : new Vector3(fixedAxis, left.y, b);
                    left += normal * .025f; right += normal * .025f;
                    var bottomLeft = left + Vector3.down * 5.2f;
                    var bottomRight = right + Vector3.down * 5.2f;
                    var middle = (left + right) * .5f + Vector3.down * (2.1f + i % 3 * .25f) + normal * .20f;
                    Triangle(left, right, middle); Triangle(right, bottomRight, middle);
                    Triangle(bottomRight, bottomLeft, middle); Triangle(bottomLeft, left, middle);
                }
            }
            var mesh = new Mesh { name = "EnvironmentCliff" };
            mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
