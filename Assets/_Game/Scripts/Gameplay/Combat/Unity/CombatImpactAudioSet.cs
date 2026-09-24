using System;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    [CreateAssetMenu(menuName = "Emberfall/Presentation/Combat Impact Audio")]
    public sealed class CombatImpactAudioSet : ScriptableObject
    {
        // Read-only compatibility path. The explicit editor upgrade clears these only after validation.
        [SerializeField, HideInInspector] private AudioClip _flesh;
        [SerializeField, HideInInspector] private AudioClip _metal;
        [SerializeField, HideInInspector] private AudioClip _heavyFlesh;
        [SerializeField, HideInInspector] private AudioClip _heavyMetal;
        [SerializeField, HideInInspector] private AudioClip _guardBreak;
        [SerializeField, HideInInspector] private AudioClip _execution;
        [SerializeField] private Slot[] _slots = Array.Empty<Slot>();

        public const int SlotCount = 6;

        public void Preload()
        {
            foreach (var slot in _slots)
                if (slot?.variants != null)
                    foreach (var variant in slot.variants)
                        if (variant.clip != null && variant.clip.loadState == AudioDataLoadState.Unloaded)
                            variant.clip.LoadAudioData();
        }
        [Serializable]
        public sealed class Slot
        {
            public string label;
            public Variant[] variants = Array.Empty<Variant>();
        }

        [Serializable]
        public struct Variant
        {
            public AudioClip clip;
            [Range(0f, 1f)] public float gain;
        }

        public readonly struct Playback
        {
            public readonly AudioClip Clip;
            public readonly float Gain, Pitch;
            public readonly int Index;
            public Playback(AudioClip clip, float gain, float pitch, int index)
            { Clip = clip; Gain = gain; Pitch = pitch; Index = index; }
        }

        public static int SlotIndex(HitFeedbackGrade grade, ImpactSurface surface) => grade switch
        {
            HitFeedbackGrade.Execution => 5,
            HitFeedbackGrade.GuardBreak => 4,
            HitFeedbackGrade.Heavy => surface == ImpactSurface.Metal ? 3 : 2,
            _ => surface == ImpactSurface.Metal ? 1 : 0
        };

        // Randomness and last-selected indices belong to each presenter, never to this shared asset.
        public Playback Select(HitFeedbackGrade grade, ImpactSurface surface, int previousIndex,
            float choice, float pitchChoice)
        {
            int slot = SlotIndex(grade, surface);
            Variant[] variants = slot < _slots.Length ? _slots[slot]?.variants : null;
            float pitch = (grade == HitFeedbackGrade.Execution ? .85f : 1f) * Mathf.Lerp(.95f, 1.05f, pitchChoice);
            if (variants == null || variants.Length == 0) return new Playback(Legacy(grade, surface), 1f, pitch, 0);
            bool skipPrevious = variants.Length > 1 && previousIndex >= 0 && previousIndex < variants.Length;
            int choices = variants.Length - (skipPrevious ? 1 : 0);
            int index = Mathf.Min(choices - 1, (int)(Mathf.Clamp01(choice) * choices));
            if (skipPrevious && index >= previousIndex) index++;
            return new Playback(variants[index].clip, Mathf.Clamp01(variants[index].gain), pitch, index);
        }

        public AudioClip Resolve(HitFeedbackGrade grade, ImpactSurface surface) => Select(grade, surface, -1, 0f, .5f).Clip;

        private AudioClip Legacy(HitFeedbackGrade grade, ImpactSurface surface) => grade switch
        {
            HitFeedbackGrade.Execution => _execution,
            HitFeedbackGrade.GuardBreak => _guardBreak,
            HitFeedbackGrade.Heavy => surface == ImpactSurface.Metal ? _heavyMetal : _heavyFlesh,
            _ => surface == ImpactSurface.Metal ? _metal : _flesh
        };
    }
}
