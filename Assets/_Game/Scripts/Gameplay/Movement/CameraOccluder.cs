using UnityEngine;

namespace Emberfall.Gameplay.Movement
{
    [DisallowMultipleComponent]
    public sealed class CameraOccluder : MonoBehaviour
    {
        [SerializeField] private Renderer[] _renderers;

        private void Awake()
        {
            if (_renderers == null || _renderers.Length == 0)
            {
                _renderers = GetComponentsInChildren<Renderer>(true);
            }
        }

        public bool TryGetDistance(Ray ray, float maximumDistance, float radius, out float distance)
        {
            distance = maximumDistance;
            if (_renderers == null || _renderers.Length == 0)
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                bounds.Expand(radius * 2f);
                if (bounds.IntersectRay(ray, out float hitDistance) && hitDistance <= maximumDistance)
                {
                    distance = Mathf.Min(distance, Mathf.Max(0f, hitDistance));
                    found = true;
                }
            }

            return found;
        }
    }
}
