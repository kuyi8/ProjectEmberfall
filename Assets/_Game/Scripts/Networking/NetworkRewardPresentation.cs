using UnityEngine;

namespace Emberfall.Networking
{
    /// <summary>Collider-free, presentation-only motion for the shared Ember reward.</summary>
    [DisallowMultipleComponent]
    public sealed class NetworkRewardPresentation : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _bobHeight = 0.14f;
        [SerializeField, Min(0f)] private float _bobSpeed = 1.8f;
        [SerializeField] private float _rotationSpeed = 42f;

        private Vector3 _anchor;

        public bool IsProjectPresentation => GetComponent<MeshFilter>() != null &&
                                             GetComponent<MeshRenderer>() != null &&
                                             GetComponent<Collider>() == null;

        private void OnEnable()
        {
            _anchor = transform.localPosition;
        }

        private void Update()
        {
            float bob = Mathf.Sin(Time.unscaledTime * _bobSpeed) * _bobHeight;
            transform.localPosition = _anchor + (Vector3.up * bob);
            transform.Rotate(Vector3.up, _rotationSpeed * Time.unscaledDeltaTime, Space.World);
        }
    }
}
