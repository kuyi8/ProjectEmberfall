using System.Collections.Generic;
using System.Reflection;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Targeting;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class LockOnScreenTargetingTests
    {
        readonly List<GameObject> objects = new List<GameObject>();
        Camera camera;
        LockOnTargeting targeting;
        static readonly Vector3 Origin = new Vector3(1000, 1000, 1000);

        [SetUp] public void SetUp()
        {
            camera = Make("TargetingTestCamera").AddComponent<Camera>();
            camera.transform.position = Origin + new Vector3(0, 2, -6);
            camera.aspect = 1f;
            camera.fieldOfView = 60f;
            targeting = Make("TargetingTestPlayer").AddComponent<LockOnTargeting>();
            targeting.Configure(null, camera.transform);
        }

        [TearDown] public void TearDown()
        {
            foreach (var item in objects) Object.DestroyImmediate(item);
            objects.Clear();
        }

        [Test] public void InitialLock_PrefersScreenCentreOverCloserSideTarget()
        {
            var centre = Target(new Vector3(0, 2, 12));
            Target(new Vector3(2, 2, 1));
            Assert.That(Select(0), Is.SameAs(centre));
        }

        [Test] public void InitialLock_UsesVerticalPositionNotJustYaw()
        {
            var centre = Target(new Vector3(0, 2, 12));
            Target(new Vector3(0, 5, 2));
            Assert.That(Select(0), Is.SameAs(centre));
        }

        [Test] public void PitchedCamera_UsesActualProjection()
        {
            camera.transform.rotation = Quaternion.Euler(15, 0, 0);
            var centre = Target(camera.ViewportToWorldPoint(new Vector3(.5f, .5f, 13)) - Origin);
            Target(camera.ViewportToWorldPoint(new Vector3(.65f, .5f, 8)) - Origin);
            Assert.That(Select(0), Is.SameAs(centre));
        }

        [Test] public void InitialLock_RejectsOffscreenAndBehindCamera()
        {
            Target(new Vector3(7, 2, 1));
            Target(new Vector3(0, 2, -8));
            Assert.That(Select(0), Is.Null);
        }

        [Test] public void EqualScreenPosition_UsesWorldDistanceAsTieBreaker()
        {
            Target(new Vector3(0, 2, 12));
            var nearer = Target(new Vector3(0, 2, 3));
            Assert.That(Select(0), Is.SameAs(nearer));
        }

        [Test] public void InactiveTarget_IsNotSelected()
        {
            Target(new Vector3(0, 2, 3)).gameObject.SetActive(false);
            Assert.That(Select(0), Is.Null);
        }

        [Test] public void SwitchingLeft_IsRelativeToCurrentTargetNotScreenCentre()
        {
            var current = Target(camera.ViewportToWorldPoint(new Vector3(.7f, .5f, 12)) - Origin);
            var left = Target(camera.ViewportToWorldPoint(new Vector3(.6f, .5f, 12)) - Origin);
            Target(camera.ViewportToWorldPoint(new Vector3(.8f, .5f, 12)) - Origin);
            typeof(LockOnTargeting).GetProperty("CurrentTarget").SetValue(targeting, current);
            Assert.That(Select(-1), Is.SameAs(left));
        }

        [Test] public void DenseScenery_DoesNotTruncateEnemyCandidates()
        {
            for (int i = 0; i < 48; i++) Make("NonTargetScenery").AddComponent<BoxCollider>();
            var centre = Target(new Vector3(0, 2, 12));
            Target(new Vector3(2, 2, 1));
            Assert.That(Select(0), Is.SameAs(centre));
        }

        CombatTarget Select(int direction)
        {
            Physics.SyncTransforms();
            return (CombatTarget)typeof(LockOnTargeting).GetMethod("FindBestTarget", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(targeting, new object[] { direction });
        }

        TrainingDummy Target(Vector3 position)
        {
            var root = Make("TargetingTestEnemy");
            root.transform.position = Origin + position;
            root.AddComponent<SphereCollider>().radius = .25f;
            var target = root.AddComponent<TrainingDummy>();
            // EditMode does not invoke Awake. No renderer/material is needed in this fixture.
            typeof(TrainingDummy).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
            return target;
        }

        GameObject Make(string name)
        {
            var root = new GameObject(name);
            root.transform.position = Origin;
            objects.Add(root);
            return root;
        }
    }
}
