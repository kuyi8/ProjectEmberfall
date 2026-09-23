using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    public sealed class WeaponArcPresentation : MonoBehaviour
    {
        [SerializeField] private PlayerCombatActor _actor;
        [SerializeField] private Transform _weaponPivot;

        private Quaternion _restRotation;

        public void Configure(PlayerCombatActor actor, Transform weaponPivot)
        {
            _actor = actor;
            _weaponPivot = weaponPivot;
        }

        private void Awake()
        {
            if (_weaponPivot != null)
            {
                _restRotation = _weaponPivot.localRotation;
            }
        }

        private void LateUpdate()
        {
            if (_actor?.Model == null || _weaponPivot == null)
            {
                return;
            }

            CombatStateMachine model = _actor.Model;
            Quaternion target = _restRotation;
            if (model.State >= CombatState.LightAttack1 && model.State <= CombatState.LightAttack3)
            {
                int combo = (int)model.State - (int)CombatState.LightAttack1;
                float direction = combo == 1 ? -1f : 1f;
                float angle = Mathf.Lerp(-85f * direction, 95f * direction, Smooth(model.StateNormalized));
                target = _restRotation * Quaternion.Euler(0f, angle, -25f);
            }
            else if (model.State == CombatState.HeavyCharge)
            {
                target = _restRotation * Quaternion.Euler(-50f, -85f, 15f);
            }
            else if (model.State == CombatState.HeavyAttack)
            {
                float angle = Mathf.Lerp(-110f, 130f, Smooth(model.StateNormalized));
                target = _restRotation * Quaternion.Euler(angle, 0f, -20f);
            }
            else if (model.State == CombatState.Sweep)
            {
                float angle = Mathf.Lerp(-150f, 150f, Smooth(model.StateNormalized));
                target = _restRotation * Quaternion.Euler(0f, angle, -32f);
            }
            else if (model.State == CombatState.Dodge)
            {
                target = _restRotation * Quaternion.Euler(0f, 0f, 160f * model.StateNormalized);
            }

            _weaponPivot.localRotation = Quaternion.Slerp(_weaponPivot.localRotation, target, Time.deltaTime * 24f);
        }

        private static float Smooth(float value) => value * value * (3f - (2f * value));
    }
}
