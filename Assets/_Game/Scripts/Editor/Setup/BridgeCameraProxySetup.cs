using System;
using System.Collections.Generic;
using System.Linq;
using Emberfall.Gameplay.Movement;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    public static class BridgeCameraProxySetup
    {
        // Conservative surface cells, not physics colliders. Only the approved static tree is opted in.
        public static int ApplyToScene(Scene scene)
        {
            if (scene.name != "10_EmberValley") return 0;
            var tree = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<CameraOccluder>(true))
                .Single(x => x.name == "BridgeDeadTree");
            var mesh = tree.GetComponent<MeshFilter>().sharedMesh;
            var scale = tree.transform.lossyScale;
            if (Mathf.Abs(scale.x - scale.y) > .0001f || Mathf.Abs(scale.x - scale.z) > .0001f || scale.x <= 0)
                throw new InvalidOperationException("Approved tree requires positive uniform scale.");
            var cells = BuildCells(mesh, .12f / scale.x);
            var proxy = tree.GetComponent<CameraQueryProxy>() ?? tree.gameObject.AddComponent<CameraQueryProxy>();
            proxy.Configure(tree.GetComponent<Renderer>(), cells);
            tree.SetQueryProxy(proxy);
            EditorUtility.SetDirty(proxy); EditorUtility.SetDirty(tree);
            return cells.Length;
        }

        public static Bounds[] BuildCells(Mesh mesh, float size)
        {
            if (mesh == null || size <= 0) throw new ArgumentException("Mesh and positive cell size required.");
            var occupied = new HashSet<Vector3Int>();
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
                Cover(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]], size, 0, occupied);
            return occupied.OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z)
                .Select(p => new Bounds(((Vector3)p + Vector3.one * .5f) * size, Vector3.one * size)).ToArray();
        }

        static void Cover(Vector3 a, Vector3 b, Vector3 c, float size, int depth, HashSet<Vector3Int> cells)
        {
            float ab = (a-b).sqrMagnitude, bc = (b-c).sqrMagnitude, ca = (c-a).sqrMagnitude;
            if (Mathf.Max(ab, bc, ca) > size * size * 4 && depth < 16)
            {
                if (ab >= bc && ab >= ca) { var m = (a+b)*.5f; Cover(a,m,c,size,depth+1,cells); Cover(m,b,c,size,depth+1,cells); }
                else if (bc >= ca) { var m = (b+c)*.5f; Cover(a,b,m,size,depth+1,cells); Cover(a,m,c,size,depth+1,cells); }
                else { var m = (c+a)*.5f; Cover(a,b,m,size,depth+1,cells); Cover(m,b,c,size,depth+1,cells); }
                return;
            }
            var min = Vector3Int.FloorToInt(Vector3.Min(a, Vector3.Min(b,c)) / size);
            var max = Vector3Int.FloorToInt(Vector3.Max(a, Vector3.Max(b,c)) / size);
            for (int x=min.x;x<=max.x;x++) for (int y=min.y;y<=max.y;y++) for (int z=min.z;z<=max.z;z++)
                cells.Add(new Vector3Int(x,y,z));
        }
    }
}
