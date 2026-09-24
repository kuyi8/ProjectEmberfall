using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    public abstract class CombatTarget : MonoBehaviour
    {
        public abstract int CombatantId { get; }
        public abstract Transform AimPoint { get; }
        public abstract bool IsAvailable { get; }
        public abstract float HealthNormalized { get; }
        public virtual bool HasSecondaryResource => false;
        public virtual float SecondaryResourceNormalized => 0f;
        public virtual bool IsThreatening => false;
        // Authored presentation classification, never inferred from VFX colors or damage authority.
        [SerializeField] private ImpactSurface _impactSurface;
        public ImpactSurface ImpactSurface => _impactSurface;
        public abstract DamageResult ReceiveDamage(DamageRequest request);
        public virtual float ApplyNeutralPostureDamage(float amount) => 0f;
    }
}
