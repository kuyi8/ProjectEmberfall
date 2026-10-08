using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.AI.Unity
{
    /// <summary>Fixed-capacity horizontal projection of the existing spherical query radius.</summary>
    public sealed class GroundRuneBoundaryMesh : IDisposable
    {
        public const int Segments = 64;
        private readonly Vector3[] _vertices = new Vector3[(Segments + 1) * 2];
        public Mesh Mesh { get; private set; }

        public GroundRuneBoundaryMesh()
        {
            var uv = new Vector2[_vertices.Length];
            var normals = new Vector3[_vertices.Length];
            var indices = new int[Segments * 6];
            for (int i = 0; i <= Segments; i++)
            {
                uv[i * 2] = new Vector2(i / (float)Segments, 0);
                uv[i * 2 + 1] = new Vector2(i / (float)Segments, 1);
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
                if (i == Segments) continue;
                int v = i * 2, t = i * 6;
                // XZ cosine/sine winding faces the normal gameplay camera above the floor.
                indices[t] = v; indices[t + 1] = v + 2; indices[t + 2] = v + 1;
                indices[t + 3] = v + 1; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
            }
            Mesh = new Mesh { name = "HostileRuneBoundary_Runtime" };
            Mesh.MarkDynamic();
            Mesh.vertices = _vertices; Mesh.uv = uv; Mesh.normals = normals; Mesh.triangles = indices;
        }

        public void SetRadius(float radius)
        {
            if (radius <= 0 || float.IsNaN(radius) || float.IsInfinity(radius))
                throw new ArgumentOutOfRangeException(nameof(radius));
            if (Mesh == null) throw new ObjectDisposedException(nameof(GroundRuneBoundaryMesh));
            float inner = radius - Mathf.Min(.1f, radius * .08f);
            for (int i = 0; i <= Segments; i++)
            {
                float radians = i * (Mathf.PI * 2 / Segments);
                var direction = new Vector3(Mathf.Cos(radians), 0, Mathf.Sin(radians));
                _vertices[i * 2] = direction * inner;
                _vertices[i * 2 + 1] = direction * radius;
            }
            Mesh.vertices = _vertices;
            Mesh.RecalculateBounds();
        }

        public void Dispose()
        {
            if (Mesh == null) return;
            if (UnityEngine.Application.isPlaying) Object.Destroy(Mesh); else Object.DestroyImmediate(Mesh);
            Mesh = null;
        }
    }
}
