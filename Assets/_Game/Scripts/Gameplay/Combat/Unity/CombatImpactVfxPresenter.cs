using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>
    /// Converts read-only combat presentation events into short-lived VFX instances.
    /// Damage, posture, hit registration and timing remain owned by PlayerCombatActor.
    /// </summary>
    public sealed class CombatImpactVfxPresenter : MonoBehaviour
    {
        [SerializeField] private PlayerCombatActor _actor;
        [SerializeField] private GameObject _steelImpactPrefab;
        [SerializeField] private GameObject _guardImpactPrefab;
        [SerializeField] private GameObject _emberImpactPrefab;
        [SerializeField, Min(0.1f)] private float _lifetime = 1.8f;

        private bool _subscribed;

        public void Configure(
            PlayerCombatActor actor,
            GameObject steelImpactPrefab,
            GameObject guardImpactPrefab,
            GameObject emberImpactPrefab)
        {
            Unsubscribe();
            _actor = actor;
            _steelImpactPrefab = steelImpactPrefab;
            _guardImpactPrefab = guardImpactPrefab;
            _emberImpactPrefab = emberImpactPrefab;
            Subscribe();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed || _actor == null) return;
            _actor.ImpactPresented += Present;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_actor != null) _actor.ImpactPresented -= Present;
            _subscribed = false;
        }

        private void Present(CombatImpactPresentationEvent impact)
        {
            GameObject prefab = impact.Style switch
            {
                CombatImpactStyle.Guard => _guardImpactPrefab,
                CombatImpactStyle.Ember => _emberImpactPrefab,
                _ => _steelImpactPrefab
            };
            if (prefab == null) return;

            GameObject instance = Instantiate(prefab, impact.Position, Quaternion.identity);
            instance.name = $"CombatImpact_{impact.Style}";
            Destroy(instance, _lifetime);
        }
    }
}
