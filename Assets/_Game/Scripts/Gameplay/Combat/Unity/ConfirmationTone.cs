using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    // Project-owned presentation only; no combat or quest authority.
    public static class ConfirmationTone
    {
        public static AudioClip Create(string name, float frequency, float duration = 0.22f)
        {
            const int rate = 22050;
            int count = Mathf.CeilToInt(rate * duration);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Sin(Mathf.PI * i / count);
                samples[i] = (Mathf.Sin(2f * Mathf.PI * frequency * t) +
                    0.35f * Mathf.Sin(2f * Mathf.PI * frequency * 1.5f * t)) * envelope * 0.2f;
            }
            AudioClip clip = AudioClip.Create(name, count, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
