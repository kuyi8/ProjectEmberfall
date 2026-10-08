using UnityEngine;

namespace Emberfall.AI.Unity
{
    /// <summary>Render-only receipt AFTER immediate combat removal. Owns its clock, not the dead actor.</summary>
    public sealed class SummonedMinionDissolve : MonoBehaviour
    {
        public const float LifetimeSeconds = .65f;
        public const int FragmentCount = 6;
        private readonly Transform[] _fragments = new Transform[FragmentCount];
        private readonly Vector3[] _births = new Vector3[FragmentCount];
        private float _elapsed;
        private bool _configured;
        public float ElapsedSeconds => _elapsed;

        public static SummonedMinionDissolve Create(SummonedMinionStyle style, Transform owner, Vector3 position)
        {
            if (style == null || !style.IsValid || owner == null || !owner.gameObject.scene.isLoaded) return null;
            var root = new GameObject("SummonOwnerDeath_Dissolve") { layer = 2 };
            // Owned hierarchy guarantees disable/unload cleanup, but the effect advances its own bounded clock.
            root.transform.SetParent(owner, false); root.transform.position = position;
            root.transform.rotation = Quaternion.identity;
            var effect = root.AddComponent<SummonedMinionDissolve>();
            for (int i = 0; i < FragmentCount; i++)
            {
                effect._fragments[i] = SummonedMinionPresentation.CreateFragment("DissolveSplinter_" + i, root.transform, style);
                float angle = i * Mathf.PI * 2f / FragmentCount;
                effect._births[i] = new Vector3(Mathf.Cos(angle) * .3f, .65f + (i % 3) * .5f, Mathf.Sin(angle) * .3f);
                effect._fragments[i].localPosition = effect._births[i];
                effect._fragments[i].localScale = new Vector3(.2f, .4f, .16f);
            }
            effect._configured = true;
            return effect;
        }

        private void Update()
        {
            if (!_configured) return;
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / LifetimeSeconds);
            for (int i = 0; i < FragmentCount; i++)
            {
                Vector3 radial = Vector3.ProjectOnPlane(_births[i], Vector3.up).normalized;
                _fragments[i].localPosition = _births[i] + radial * (.55f * t) + Vector3.up * (.75f * t);
                _fragments[i].localRotation = Quaternion.Euler(t * 80f, i * 60f + t * 120f, t * 30f);
                _fragments[i].localScale = new Vector3(.2f, .4f, .16f) * (1f - t);
            }
            if (t >= 1f) { gameObject.SetActive(false); Destroy(gameObject); }
        }
    }
}
