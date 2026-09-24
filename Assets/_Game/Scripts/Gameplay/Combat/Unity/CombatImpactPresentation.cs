using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    public enum CombatImpactStyle
    {
        Steel,
        Guard,
        Ember
    }

    public readonly struct CombatImpactPresentationEvent
    {
        public CombatImpactPresentationEvent(Vector3 position, CombatImpactStyle style)
        {
            Position = position;
            Style = style;
            Sequence = 0;
            AttackSequence = 0;
            TargetId = 0;
            Grade = HitFeedbackGrade.None;
            Surface = ImpactSurface.Flesh;
            TargetAnimator = null;
            Attack = AttackTag.Light;
        }

        public Vector3 Position { get; }
        public CombatImpactStyle Style { get; }
        public ulong Sequence { get; }
        public int AttackSequence { get; }
        public int TargetId { get; }
        public HitFeedbackGrade Grade { get; }
        public ImpactSurface Surface { get; }
        public Animator TargetAnimator { get; }
        public AttackTag Attack { get; }

        public CombatImpactPresentationEvent(Vector3 position, CombatImpactStyle style,
            ulong sequence, int attackSequence, int targetId, HitFeedbackGrade grade,
            ImpactSurface surface, Animator targetAnimator = null, AttackTag attack = AttackTag.Light) : this(position, style)
        {
            Sequence = sequence;
            AttackSequence = attackSequence;
            TargetId = targetId;
            Grade = grade;
            Surface = surface;
            TargetAnimator = targetAnimator;
            Attack = attack;
        }
    }

}
