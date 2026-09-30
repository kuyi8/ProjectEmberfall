using System;
using UnityEngine;

namespace Emberfall.Gameplay.Movement
{
    /// <summary>Conservative mesh-derived cells for camera queries only. Never enters the physics world.</summary>
    [DisallowMultipleComponent]
    public sealed class CameraQueryProxy : MonoBehaviour
    {
        [SerializeField] Renderer _source;
        [SerializeField] Bounds[] _localCells = Array.Empty<Bounds>();
        [SerializeField] Bounds _localBounds;
        public int CellCount => _localCells.Length;

        public void Configure(Renderer source, Bounds[] cells)
        {
            if (source == null || source.transform != transform || cells == null || cells.Length == 0)
                throw new ArgumentException("A same-object renderer and nonempty cells are required.");
            _source = source;
            _localCells = (Bounds[])cells.Clone();
            _localBounds = cells[0];
            foreach (var cell in cells) _localBounds.Encapsulate(cell);
        }

        public bool TryGetDistance(Ray worldRay, float maximumDistance, float radius, out float distance)
        {
            distance = maximumDistance;
            if (!isActiveAndEnabled || _source == null || !_source.enabled || !_source.gameObject.activeInHierarchy)
                return false;
            Vector3 direction = transform.InverseTransformVector(worldRay.direction.normalized);
            float units = direction.magnitude;
            Vector3 scale = transform.lossyScale;
            float minimumScale = Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (units < .00001f || minimumScale < .00001f) return false;
            var ray = new Ray(transform.InverseTransformPoint(worldRay.origin), direction / units);
            // Scene tree has uniform scale; min scale stays conservative for axis-scaled authoring.
            float expansion = Mathf.Max(0, radius) * 2 / minimumScale;
            var broad = _localBounds;
            broad.Expand(expansion);
            if (!broad.IntersectRay(ray, out var entry) || entry > maximumDistance * units) return false;
            bool found = false;
            foreach (var original in _localCells)
            {
                var cell = original;
                cell.Expand(expansion);
                if (!cell.IntersectRay(ray, out var hit) || hit > distance * units) continue;
                distance = Mathf.Min(distance, Mathf.Max(0, hit) / units);
                found = true;
                if (distance == 0) break;
            }
            return found;
        }
    }
}
