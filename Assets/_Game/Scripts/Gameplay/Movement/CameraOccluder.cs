using UnityEngine;

namespace Emberfall.Gameplay.Movement
{
    [DisallowMultipleComponent]
    public sealed class CameraOccluder : MonoBehaviour
    {
        [SerializeField] private Renderer[] _renderers;
        private CameraQueryProxy _queryProxy;

        public void SetQueryProxy(CameraQueryProxy proxy)
        {
            if (proxy != null && proxy.gameObject != gameObject)
                throw new System.ArgumentException("Query proxy must belong to this occluder.");
            _queryProxy = proxy;
        }

        private void Awake()
        {
            _queryProxy = GetComponent<CameraQueryProxy>();
            if (_renderers == null || _renderers.Length == 0)
            {
                _renderers = GetComponentsInChildren<Renderer>(true);
            }
        }

        public bool TryGetDistance(Ray ray, float maximumDistance, float radius, out float distance)
        {
            if (_queryProxy != null)
                return _queryProxy.TryGetDistance(ray, maximumDistance, radius, out distance);
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
