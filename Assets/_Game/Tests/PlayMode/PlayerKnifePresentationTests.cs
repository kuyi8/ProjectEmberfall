using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed class PlayerKnifePresentationTests
    {
        private PlayerCombatActor _actor;
        private PlayerKnifePresentation _visual;
        private readonly MethodInfo _present = typeof(PlayerKnifePresentation).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        [UnitySetUp]
        public IEnumerator Setup()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null; yield return null;
            foreach (var item in Object.FindObjectsOfType<MonoBehaviour>())
                if (item is MeleeEnemyActor || item is RangedEnemyActor || item is ShieldEnemyActor) item.enabled = false;
            _actor = Object.FindObjectOfType<PlayerCombatActor>();
            _visual = _actor.GetComponent<PlayerThrowingKnifeLauncher>().PreparePresentationCandidate();
#if UNITY_EDITOR
            var pose = UnityEditor.AssetDatabase.LoadAssetAtPath<KnifeGripPose>(
                "Assets/_Game/Settings/KnifeGripPose_Ranger.asset");
            Assert.That(pose, Is.Not.Null);
            _visual.ConfigureGrip(pose);
#endif
            Assert.That(_visual != null && _visual.IsConfigured, Is.True);
        }

        private void ObserveWithoutAuthorityWrites()
        {
            var launcher = _actor.GetComponent<PlayerThrowingKnifeLauncher>();
            var origin = NaturalPlayerContactTests.Field<Transform>(launcher, "_launchOrigin");
            var animator = _actor.GetComponentInChildren<Animator>();
            var forearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Vector3 actorPosition = _actor.transform.position, launchPosition = origin.position;
            Quaternion actorRotation = _actor.transform.rotation, forearmRotation = forearm.rotation;
            var projectiles = Object.FindObjectsOfType<PlayerThrowingKnifeProjectile>();
            var positions = new Vector3[projectiles.Length];
            var rotations = new Quaternion[projectiles.Length];
            for (int i = 0; i < projectiles.Length; i++)
            { positions[i] = projectiles[i].transform.position; rotations[i] = projectiles[i].transform.rotation; }
            _present.Invoke(_visual, null); // Lifecycle assertion only, NOT natural timing evidence.
            Assert.That(_actor.transform.position, Is.EqualTo(actorPosition));
            Assert.That(_actor.transform.rotation, Is.EqualTo(actorRotation));
            Assert.That(origin.position, Is.EqualTo(launchPosition));
            Assert.That(forearm.rotation, Is.EqualTo(forearmRotation));
            for (int i = 0; i < projectiles.Length; i++)
            {
                Assert.That(projectiles[i].transform.position, Is.EqualTo(positions[i]));
                Assert.That(projectiles[i].transform.rotation, Is.EqualTo(rotations[i]));
            }
        }

        [UnityTest]
        public IEnumerator ReadOnlyTrace_RecordsRealAssistReleaseAndSweep()
        {
            var entries = new List<KnifeQueryTrace.Entry>();
            System.Action<KnifeQueryTrace.Entry> observe = entry => entries.Add(entry);
            KnifeQueryTrace.Observer += observe;
            try
            {
                Assert.That(_actor.Model.Submit(CombatCommand.RangedAttack), Is.True);
                int sequence = _actor.Model.AttackSequence;
                yield return new WaitForSeconds(1.5f);
                Assert.That(entries.Count, Is.GreaterThan(2));
                Assert.That(entries[0].kind, Is.EqualTo("assist"));
                Assert.That(entries[1].kind, Is.EqualTo("release-unlocked"));
                Assert.That(entries[1].direction.magnitude, Is.EqualTo(1).Within(.0001f));
                Assert.That(entries[1].sequence, Is.EqualTo(sequence));
                int sweepCount = 0;
                foreach (var entry in entries)
                {
                    Assert.That(entry.sequence, Is.EqualTo(sequence));
                    if (entry.kind != "sweep") continue;
                    sweepCount++;
                    Assert.That(entry.projectileId, Is.EqualTo(entries[1].projectileId));
                    Assert.That(entry.halfExtents.x, Is.GreaterThan(0));
                    Assert.That(entry.distance, Is.GreaterThan(0));
                    if (entry.hit) Assert.That(entry.colliderId, Is.Not.Zero);
                }
                Assert.That(sweepCount, Is.GreaterThan(0));
                Assert.That(_actor.Model.RangedReleaseSequence, Is.EqualTo(1));
                Assert.That(_actor.GetComponent<PlayerThrowingKnifeLauncher>().ActiveProjectileCount, Is.Zero);
                Assert.That(_visual.TrailPointCount, Is.Zero);
            }
            finally { KnifeQueryTrace.Observer -= observe; }
        }

        [UnityTest]
        public IEnumerator FiveThrows_ReuseBoundedVisuals_ClearTrails_AndRestoreSword()
        {
            int capacity = _visual.VisualCapacity;
            int children = _actor.transform.childCount;
            var seen = new HashSet<int>();
            bool sawTrail = false;
            for (int shot = 0; shot < 5; shot++)
            {
                float timeout = Time.realtimeSinceStartup + 5;
                while (_actor.Model.RangedCooldownRemaining > 0 && Time.realtimeSinceStartup < timeout) yield return null;
                Assert.That(_actor.Model.Submit(CombatCommand.RangedAttack), Is.True);
                bool flew = false;
                timeout = Time.realtimeSinceStartup + 2;
                while (Time.realtimeSinceStartup < timeout)
                {
                    yield return null;
                    ObserveWithoutAuthorityWrites();
                    if (_actor.Model.State == CombatState.RangedAttack)
                    { Assert.That(_visual.SwordVisible, Is.False); Assert.That(_visual.SwordTrailVisible, Is.False); }
                    var active = Object.FindObjectsOfType<PlayerThrowingKnifeProjectile>();
                    int flying = 0;
                    foreach (var p in active) if (p.IsInFlight) { flying++; seen.Add(p.GetInstanceID()); }
                    Assert.That(_visual.ActiveVisualCount, Is.EqualTo(flying));
                    if (_visual.FlightVisible) flew = true;
                    sawTrail |= _visual.TrailPointCount > 1;
                    if (flew && flying == 0 && _actor.Model.State == CombatState.Locomotion) break;
                }
                Assert.That(flew, Is.True);
                Assert.That(_visual.HeldVisible || _visual.FlightVisible, Is.False);
                Assert.That(_visual.TrailPointCount, Is.Zero);
                Assert.That(_visual.SwordVisible, Is.True);
                Assert.That(_actor.transform.childCount, Is.EqualTo(children));
                Assert.That(_visual.VisualCapacity, Is.EqualTo(capacity));
            }
            Assert.That(seen.Count, Is.LessThanOrEqualTo(capacity));
            Assert.That(seen.Count, Is.LessThan(5), "At least one real projectile was reused.");
            // Null graphics may not update native TrailRenderer sampling; D3D capture covers visible trail.
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.That(sawTrail, Is.True);
        }

        [UnityTest]
        public IEnumerator DisableDuringFlight_ClearsLease_WithoutStoppingAuthorityOrReplaying()
        {
            Assert.That(_actor.Model.Submit(CombatCommand.RangedAttack), Is.True);
            float timeout = Time.realtimeSinceStartup + 1;
            while (!_visual.FlightVisible && Time.realtimeSinceStartup < timeout) yield return null;
            Assert.That(_visual.FlightVisible, Is.True);
            var projectile = Object.FindObjectOfType<PlayerThrowingKnifeProjectile>();
            Vector3 position = projectile.transform.position;
            _visual.enabled = false;
            Assert.That(_visual.HeldVisible || _visual.FlightVisible, Is.False);
            Assert.That(_visual.TrailPointCount, Is.Zero);
            Assert.That(_visual.SwordVisible, Is.True);
            Assert.That(projectile.IsInFlight, Is.True);
            Assert.That(projectile.transform.position, Is.EqualTo(position));
            foreach (var r in projectile.GetComponentsInChildren<Renderer>()) Assert.That(r.forceRenderingOff, Is.False);
            _visual.enabled = true;
            ObserveWithoutAuthorityWrites();
            Assert.That(_visual.HeldVisible || _visual.FlightVisible, Is.False, "Do not replay old release after re-enable.");
            yield return new WaitForSeconds(1);
            Assert.That(_visual.TrailPointCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator InterruptedWindup_AndActorDisable_RestoreSwordWithoutRelease()
        {
            Assert.That(_actor.Model.Submit(CombatCommand.RangedAttack), Is.True);
            yield return null;
            ObserveWithoutAuthorityWrites();
            Assert.That(_visual.HeldVisible, Is.True);
            var joints = NaturalPlayerContactTests.Field<Transform[]>(_visual, "_handJoints");
            var animated = (Quaternion[])NaturalPlayerContactTests.Field<Quaternion[]>(_visual, "_animatedRotations").Clone();
            int released = _actor.Model.RangedReleaseSequence;
            _actor.enabled = false;
            ObserveWithoutAuthorityWrites();
            Assert.That(_visual.HeldVisible || _visual.FlightVisible, Is.False);
            Assert.That(_visual.SwordVisible, Is.True);
            for (int i = 0; i < joints.Length; i++)
                Assert.That(joints[i].localRotation, Is.EqualTo(animated[i]), "Interrupted grip must restore Animator pose.");
            _actor.enabled = true;
            _actor.Model.ForceDeath();
            ObserveWithoutAuthorityWrites();
            Assert.That(_actor.Model.RangedReleaseSequence, Is.EqualTo(released));
            Assert.That(_visual.TrailPointCount, Is.Zero);
            Assert.That(_visual.SwordVisible, Is.True);
        }
    }
}
