using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class VoidExecutionVolume : MonoBehaviour
    {
        [SerializeField] private BoxCollider _volume;

        public Bounds WorldBounds => _volume != null
            ? _volume.bounds
            : new Bounds(transform.position, Vector3.zero);

        public void Configure(BoxCollider volume)
        {
            _volume = volume;
            ApplySettings();
        }

        private void Reset()
        {
            _volume = GetComponent<BoxCollider>();
            ApplySettings();
        }

        private void Awake()
        {
            _volume ??= GetComponent<BoxCollider>();
            ApplySettings();
        }

        private void OnTriggerEnter(Collider other) => TryExecute(other);

        private void OnTriggerStay(Collider other) => TryExecute(other);

        private static void TryExecute(Collider other)
        {
            PlayerCombatActor player = other.GetComponentInParent<PlayerCombatActor>();
            if (player != null)
            {
                player.ExecuteVoidFall();
            }
        }

        private void ApplySettings()
        {
            if (_volume != null)
            {
                _volume.isTrigger = true;
            }
        }

        private void OnDrawGizmosSelected()
        {
            BoxCollider volume = _volume != null ? _volume : GetComponent<BoxCollider>();
            if (volume == null)
            {
                return;
            }

            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.9f, 0.05f, 0.05f, 0.35f);
            Gizmos.DrawCube(volume.center, volume.size);
            Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(volume.center, volume.size);
            Gizmos.matrix = previous;
        }
    }
}
