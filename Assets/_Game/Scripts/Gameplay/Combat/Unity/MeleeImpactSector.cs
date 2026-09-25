using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>Read-only snapshot of an actual melee query; absent for ranged/network legacy events.</summary>
    public readonly struct MeleeImpactSector
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Forward;
        public readonly float Radius;
        public readonly float FullAngle;
        public bool IsValid => Finite(Origin.x) && Finite(Origin.y) && Finite(Origin.z) &&
            Radius > 0 && !float.IsInfinity(Radius) &&
            FullAngle > 0 && FullAngle <= 360 && Forward.sqrMagnitude > .99f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public MeleeImpactSector(Vector3 origin, Vector3 forward, float radius, float fullAngle)
        {
            Origin = origin;
            Forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
            Radius = radius;
            FullAngle = fullAngle;
        }
    }

    /// <summary>Fixed-capacity, presentation-only mesh. Width grows inward; the outer edge is the query radius.</summary>
    public sealed class MeleeImpactArcMesh
    {
        public const int Segments = 64;
        public Mesh Mesh { get; }
        private readonly Vector3[] _vertices = new Vector3[(Segments + 1) * 2];

        public MeleeImpactArcMesh()
        {
            Mesh = new Mesh { name = "ConfirmedMeleeArc_Runtime" };
            Mesh.MarkDynamic();
            var uv = new Vector2[_vertices.Length];
            var triangles = new int[Segments * 6];
            for (int i = 0; i <= Segments; i++)
            {
                uv[i * 2] = new Vector2(i / (float)Segments, 0);
                uv[i * 2 + 1] = new Vector2(i / (float)Segments, 1);
                if (i == Segments) continue;
                int v = i * 2, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            Mesh.vertices = _vertices;
            Mesh.uv = uv;
            Mesh.triangles = triangles;
        }

        public void SetSector(float radius, float fullAngle)
        {
            float inner = Mathf.Max(0, radius - Mathf.Min(.24f, radius * .12f));
            for (int i = 0; i <= Segments; i++)
            {
                float angle = (-fullAngle * .5f + fullAngle * i / Segments) * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                _vertices[i * 2] = direction * inner;
                _vertices[i * 2 + 1] = direction * radius;
            }
            Mesh.vertices = _vertices;
            Mesh.RecalculateBounds();
        }
    }
}
