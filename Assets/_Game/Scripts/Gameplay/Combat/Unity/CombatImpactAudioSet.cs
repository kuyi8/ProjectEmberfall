using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    [CreateAssetMenu(menuName = "Emberfall/Presentation/Combat Impact Audio")]
    public sealed class CombatImpactAudioSet : ScriptableObject
    {
        [SerializeField] private AudioClip _flesh;
        [SerializeField] private AudioClip _metal;
        [SerializeField] private AudioClip _heavyFlesh;
        [SerializeField] private AudioClip _heavyMetal;
        [SerializeField] private AudioClip _guardBreak;
        [SerializeField] private AudioClip _execution;
        public AudioClip Resolve(HitFeedbackGrade grade, ImpactSurface surface) => grade switch
        {
            HitFeedbackGrade.Execution => _execution,
            HitFeedbackGrade.GuardBreak => _guardBreak,
            HitFeedbackGrade.Heavy => surface == ImpactSurface.Metal ? _heavyMetal : _heavyFlesh,
            _ => surface == ImpactSurface.Metal ? _metal : _flesh
        };
    }
}
