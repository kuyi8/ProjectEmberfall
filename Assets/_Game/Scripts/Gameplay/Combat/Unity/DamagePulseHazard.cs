using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    public sealed class DamagePulseHazard : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float _damage = 34f;
        [SerializeField, Min(0.1f)] private float _interval = 1.1f;

        private float _nextDamageTime;
        private int _sequence;

        private void OnTriggerStay(Collider other)
        {
            if (Time.time < _nextDamageTime)
            {
                return;
            }

            PlayerCombatActor player = other.GetComponentInParent<PlayerCombatActor>();
            if (player == null)
            {
                return;
            }

            _nextDamageTime = Time.time + _interval;
            player.ReceiveDamage(new DamageRequest(
                GetInstanceID(), ++_sequence, _damage, 20f, AttackTag.Hazard, false, false));
        }
    }
}
