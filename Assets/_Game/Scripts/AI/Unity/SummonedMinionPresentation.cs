using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberfall.AI.Unity
{
    /// <summary>Three floating splinters distinguish a temporary spirit without danger/interaction rings.</summary>
    public sealed class SummonedMinionPresentation : MonoBehaviour
    {
        public const int FragmentCount = 3;
        private readonly Transform[] _fragments = new Transform[FragmentCount];
        private readonly Vector3[] _anchors = { new Vector3(-.54f, 1.5f, -.1f), new Vector3(.54f, 1.5f, -.1f), new Vector3(0f, 1.95f, -.25f) };
        private SummonedMinionStyle _style;
        private float _elapsed;
        public SummonedMinionStyle Style => _style;
        public int IdentityFragmentCount => _style != null ? FragmentCount : 0;

        public void Configure(SummonedMinionStyle style)
        {
            if (_style != null) throw new InvalidOperationException("Spirit identity is configured once.");
            if (style == null || !style.IsValid) throw new ArgumentException("Spirit identity needs authored visual, mesh and material.", nameof(style));
            _style = style;
            for (int i = 0; i < FragmentCount; i++)
            {
                _fragments[i] = CreateFragment("SpiritSplinter_" + i, transform, style);
                _fragments[i].localPosition = _anchors[i];
                _fragments[i].localRotation = Quaternion.Euler(0f, i * 70f, i == 0 ? -18f : i == 1 ? 18f : 0f);
                _fragments[i].localScale = new Vector3(.16f, .32f, .12f);
            }
        }

        private void Update()
        {
            if (_style == null) return;
            _elapsed += Time.deltaTime;
            for (int i = 0; i < FragmentCount; i++)
                _fragments[i].localPosition = _anchors[i] + Vector3.up * (.055f * Mathf.Sin(_elapsed * 2.4f + i * 2f));
        }

        internal static Transform CreateFragment(string name, Transform parent, SummonedMinionStyle style)
        {
            // Unlike CreatePrimitive this never creates even a temporary collider.
            var root = new GameObject(name) { layer = 2 };
            root.transform.SetParent(parent, false);
            root.AddComponent<MeshFilter>().sharedMesh = style.FragmentMesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = style.FragmentMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return root.transform;
        }
    }
}
