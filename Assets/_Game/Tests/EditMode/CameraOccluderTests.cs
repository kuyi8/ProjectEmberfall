using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class CameraOccluderTests
    {
        [Test, Explicit("Known independent inactive legacy-AABB defect; pending separate approval.")]
        public void LegacyInactiveOccluder_ShouldBeIgnored_PendingSeparateApproval()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var legacy = cube.AddComponent<CameraOccluder>();
                InitializeLegacy(legacy);
                cube.SetActive(false);
                Assert.That(legacy.TryGetDistance(new Ray(new Vector3(0, 0, -3), Vector3.forward), 5, .2f, out _), Is.False);
            }
            finally { Object.DestroyImmediate(cube); }
        }

        [Test] public void LegacyOccluder_StillBlocksOutsideAndInside()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var legacy = cube.AddComponent<CameraOccluder>(); InitializeLegacy(legacy);
                Assert.That(legacy.TryGetDistance(new Ray(new Vector3(0,0,-3),Vector3.forward),5,.2f,out var outside),Is.True);
                Assert.That(outside,Is.EqualTo(2.3f).Within(.0001f));
                Assert.That(legacy.TryGetDistance(new Ray(Vector3.zero,Vector3.forward),5,.2f,out var inside),Is.True);
                Assert.That(inside,Is.Zero);
            }
            finally { Object.DestroyImmediate(cube); }
        }

        static void InitializeLegacy(CameraOccluder occluder)
        {
            // EditMode does not run Awake; initialize only the existing serialized renderer cache.
            var serialized = new UnityEditor.SerializedObject(occluder);
            var renderers = serialized.FindProperty("_renderers");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = occluder.GetComponent<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
