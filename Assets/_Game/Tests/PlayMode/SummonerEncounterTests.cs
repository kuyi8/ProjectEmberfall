#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Targeting;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using System.IO;
using UnityEditor;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Real adapter lifecycle in an isolated Gym copy; not player-input/difficulty/route acceptance.</summary>
    public sealed class SummonerEncounterTests : IPrebuildSetup
    {
        private SummonerEnemyActor _owner;
        public void Setup()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Emberfall.Editor")
                .GetType("Emberfall.Editor.Review.SummonerEncounterReview", true);
            type.GetMethod("Prepare").Invoke(null, null);
        }

        [UnitySetUp] public IEnumerator Begin()
        {
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            EditorSceneManager.LoadSceneInPlayMode("Assets/_Game/Scenes/Review/92_AshCallerEncounter.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null; yield return null;
            _owner = Object.FindObjectOfType<SummonerEnemyActor>();
            Assert.That(_owner, Is.Not.Null);
            Assert.That(_owner.Brain, Is.Not.Null);
            Assert.That(_owner.GetComponent<UnityEngine.AI.NavMeshAgent>().isOnNavMesh, Is.True);
        }

        private IEnumerator WaitForMinion()
        {
            float end = Time.time + 12f;
            while (_owner.LivingEntityCount == 0 && Time.time < end) yield return null;
            Assert.That(_owner.LivingEntityCount, Is.EqualTo(1), "Requires a real activated MeleeEnemyActor, not only a domain counter.");
        }
        private void FreezeMinionCombatOnly()
        {
            foreach (var minion in Object.FindObjectsOfType<MeleeEnemyActor>().Where(a => a.name == "Summoned_Fogwalker"))
                minion.SetSimulationAuthority(false);
        }

        [UnityTest] public IEnumerator TwoRealEntities_NeverExceedCapAndMatchCompletedCasts()
        {
            float end = Time.time + 22f;
            while (Time.time < end)
            {
                FreezeMinionCombatOnly();
                Assert.That(_owner.LivingEntityCount, Is.LessThanOrEqualTo(2));
                Assert.That(_owner.Brain.LivingSummonCount, Is.EqualTo(_owner.LivingEntityCount));
                Assert.That(_owner.SpawnedEntityCount, Is.EqualTo(_owner.Brain.SummonCount));
                yield return null;
            }
            Assert.That(_owner.LivingEntityCount, Is.EqualTo(2));
            Assert.That(_owner.SpawnedEntityCount, Is.EqualTo(2));
        }
        [UnityTest] public IEnumerator InterruptedChant_LeavesNoEntityOrReservedSlot()
        {
            float end = Time.time + 10f;
            while (!_owner.Brain.IsSummoning && Time.time < end) yield return null;
            Assert.That(_owner.Brain.IsSummoning, Is.True);
            var result = _owner.ReceiveDamage(new DamageRequest(7, 1, 30f, 14f, AttackTag.Projectile));
            Assert.That(result.Accepted && result.AppliedDamage > 0f, Is.True);
            Assert.That(_owner.Brain.InterruptCount, Is.EqualTo(1));
            for (int i = 0; i < 3; i++) yield return null;
            Assert.That(_owner.LivingEntityCount, Is.Zero);
            Assert.That(_owner.Brain.LivingSummonCount, Is.Zero);
            Assert.That(_owner.SpawnedEntityCount, Is.Zero);
        }
        [UnityTest] public IEnumerator RealMinion_UsesMetreScaleHandWeaponWithoutGameplayColliders()
        {
            yield return WaitForMinion(); FreezeMinionCombatOnly();
            var minion = Object.FindObjectsOfType<MeleeEnemyActor>().Single(a => a.name == "Summoned_Fogwalker");
            var animator = minion.GetComponentInChildren<Animator>();
            var socket = animator.GetBoneTransform(HumanBodyBones.RightHand).Find("SummonedWeaponSocket_RightHand");
            Assert.That(socket, Is.Not.Null);
            Assert.That(Vector3.Distance(socket.lossyScale, Vector3.one), Is.LessThan(.001f));
            Assert.That(socket.GetComponentsInChildren<Renderer>().Length, Is.GreaterThan(0));
            Assert.That(socket.GetComponentsInChildren<Collider>().Length, Is.Zero, "The visual sword must not take ownership of hit detection.");
            yield return null;
        }
        [UnityTest] public IEnumerator LethalHeavy_ClearsRegisteredTargetsImmediatelyAndDestroyCompletesNextFrame()
        {
            yield return WaitForMinion(); FreezeMinionCombatOnly();
            var minion = Object.FindObjectsOfType<MeleeEnemyActor>().Single(a => a.name == "Summoned_Fogwalker");
            Assert.That(_owner.ReceiveDamage(new DamageRequest(7, 1, 999f, 0f, AttackTag.Heavy)).Killed, Is.True);
            Assert.That(minion.gameObject.activeInHierarchy, Is.False);
            Assert.That(_owner.LivingEntityCount, Is.Zero);
            Assert.That(_owner.Brain.LivingSummonCount, Is.Zero);
            var effects = _owner.GetComponentsInChildren<SummonedMinionDissolve>();
            Assert.That(effects.Length, Is.EqualTo(1));
            Assert.That(effects[0].gameObject.scene, Is.EqualTo(_owner.gameObject.scene));
            Assert.That(effects[0].GetComponentsInChildren<Collider>(), Is.Empty);
            Assert.That(effects[0].GetComponentsInChildren<CombatTarget>(), Is.Empty);
            Assert.That(effects[0].GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(), Is.Empty);
            Assert.That(effects[0].GetComponentsInChildren<Renderer>().Length, Is.EqualTo(SummonedMinionDissolve.FragmentCount));
            Assert.That(_owner.ReceiveDamage(new DamageRequest(7, 2, 999f, 0f, AttackTag.Heavy)).Accepted, Is.False);
            yield return null;
            Assert.That(minion == null, Is.True);
            yield return new WaitForSeconds(SummonedMinionDissolve.LifetimeSeconds + .1f);
            Assert.That(Object.FindObjectsOfType<SummonedMinionDissolve>(), Is.Empty);
        }
        [UnityTest] public IEnumerator SpiritIdentity_HasBoundedSilhouetteWithoutExtraPhysicsOrTargets()
        {
            yield return WaitForMinion(); FreezeMinionCombatOnly();
            var minion = Object.FindObjectsOfType<MeleeEnemyActor>().Single(a => a.name == "Summoned_Fogwalker");
            var presentation = minion.GetComponent<SummonedMinionPresentation>();
            Assert.That(presentation, Is.Not.Null); Assert.That(presentation.IdentityFragmentCount, Is.EqualTo(3));
            for (int i = 0; i < SummonedMinionPresentation.FragmentCount; i++)
            {
                var fragment = minion.transform.Find("SpiritSplinter_" + i);
                Assert.That(fragment, Is.Not.Null); Assert.That(fragment.GetComponent<MeshRenderer>(), Is.Not.Null);
                Assert.That(fragment.GetComponentsInChildren<Collider>(), Is.Empty);
                Assert.That(fragment.GetComponentsInChildren<CombatTarget>(), Is.Empty);
            }
            Assert.That(minion.GetComponentsInChildren<Collider>().Length, Is.EqualTo(1), "Only the original authoritative body capsule.");
        }
        [UnityTest] public IEnumerator OwnerDeath_InvalidatesCachedLockAndDamageWithoutFakeMinionKill()
        {
            yield return WaitForMinion(); // Keep real minion authority until the actual cleanup call.
            var minion = Object.FindObjectsOfType<MeleeEnemyActor>().Single(a => a.name == "Summoned_Fogwalker");
            var targeting = Object.FindObjectOfType<PlayerCombatActor>().GetComponent<LockOnTargeting>();
            Assert.That(targeting, Is.Not.Null);
            typeof(LockOnTargeting).GetProperty(nameof(LockOnTargeting.CurrentTarget)).GetSetMethod(true)
                .Invoke(targeting, new object[] { minion }); // Seed only a cached target, not simulated player input.
            int minionKills = 0; minion.Died += _ => minionKills++;
            Assert.That(targeting.IsLocked && minion.IsAvailable && minion.HasSimulationAuthority, Is.True);
            float health = minion.Brain.Health.Current;
            Assert.That(_owner.ReceiveDamage(new DamageRequest(7, 1, 999f, 0f, AttackTag.Heavy)).Killed, Is.True);
            Assert.That(targeting.IsLocked, Is.False, "Must invalidate the cached reference within the lethal call, not next frame.");
            Assert.That(minion.IsAvailable || minion.HasSimulationAuthority || minion.IsExecutionEligible || minion.IsThreatening, Is.False);
            Assert.That(minion.GetComponent<Collider>().enabled, Is.False);
            Assert.That(minion.ReceiveDamage(new DamageRequest(7, 2, 999f, 0f, AttackTag.Heavy)).Accepted, Is.False);
            Assert.That(minion.Brain.Health.Current, Is.EqualTo(health));
            Assert.That(minionKills, Is.Zero, "Owner cleanup is despawn, not a rewarded enemy kill.");
            minion.DespawnRuntimeEntity(); minion.ResetToSpawn(); minion.SetSimulationAuthority(true);
            Assert.That(minion.IsAvailable || minion.HasSimulationAuthority || minion.GetComponent<Collider>().enabled, Is.False);
            yield return null; Assert.That(minion == null, Is.True);
            Assert.That(targeting.CurrentTarget == null, Is.True);
        }
        [UnityTest] public IEnumerator DeathDissolve_ResetClearsTheRenderOnlyReceiptImmediately()
        {
            yield return WaitForMinion(); FreezeMinionCombatOnly();
            _owner.ReceiveDamage(new DamageRequest(7, 1, 999f, 0f, AttackTag.Heavy));
            var effect = Object.FindObjectOfType<SummonedMinionDissolve>(); Assert.That(effect, Is.Not.Null);
            _owner.ResetToSpawn(); _owner.ResetToSpawn();
            Assert.That(effect.gameObject.activeInHierarchy, Is.False);
            yield return null; Assert.That(effect == null, Is.True);
        }
        [UnityTest] public IEnumerator DeathDissolve_DisableClearsTheRenderOnlyReceiptImmediately()
        {
            yield return WaitForMinion(); FreezeMinionCombatOnly();
            _owner.ReceiveDamage(new DamageRequest(7, 1, 999f, 0f, AttackTag.Heavy));
            var effect = Object.FindObjectOfType<SummonedMinionDissolve>(); Assert.That(effect, Is.Not.Null);
            _owner.gameObject.SetActive(false); _owner.gameObject.SetActive(false);
            Assert.That(effect.gameObject.activeInHierarchy, Is.False);
            yield return null; Assert.That(effect == null, Is.True);
        }
        [UnityTest] public IEnumerator DeathDissolve_SceneUnloadLeavesNoDetachedVisuals()
        {
            yield return WaitForMinion(); FreezeMinionCombatOnly();
            _owner.ReceiveDamage(new DamageRequest(7, 1, 999f, 0f, AttackTag.Heavy));
            var effect = Object.FindObjectOfType<SummonedMinionDissolve>(); Assert.That(effect, Is.Not.Null);
            var original = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("SpiritDissolveUnloadFixture"));
            yield return SceneManager.UnloadSceneAsync(original); yield return null;
            Assert.That(effect == null, Is.True); Assert.That(Object.FindObjectsOfType<SummonedMinionDissolve>(), Is.Empty);
        }
        [UnityTest] public IEnumerator SpiritComparison_RealFramesAndOwnerDeathReceipt_RenderReviewOnly()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));
            yield return WaitForMinion(); FreezeMinionCombatOnly();
            var minion = Object.FindObjectsOfType<MeleeEnemyActor>().Single(a => a.name == "Summoned_Fogwalker");
            minion.GetComponent<UnityEngine.AI.NavMeshAgent>().Warp(new Vector3(1.35f, 0f, 0f));
            minion.transform.rotation = Quaternion.identity;
            var normal = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_FogwalkerSkeleton.prefab"));
            var cameraRoot = new GameObject("SpiritComparisonInspectionCamera");
            string folder = Path.GetFullPath("Builds/ArtReview/summoned-spirit/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(folder);
            try
            {
                normal.transform.position = new Vector3(-1.35f, 1.05f, 0f);
                foreach (var renderer in normal.GetComponentsInChildren<Renderer>(true)) renderer.gameObject.layer = 31;
                foreach (var renderer in minion.GetComponentsInChildren<Renderer>(true)) renderer.gameObject.layer = 31;
                // Only this explicitly diagnostic view hides the Gym/caster; normal trial remains untouched.
                var camera = cameraRoot.AddComponent<Camera>(); camera.enabled = false; camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f, .16f, .19f);
                camera.aspect = 1.6f; camera.fieldOfView = 42f; camera.nearClipPlane = .08f; camera.farClipPlane = 30f;
                camera.transform.position = new Vector3(0f, 2.7f, 7f); camera.transform.LookAt(new Vector3(0f, 1.15f, 0f));
                yield return null; yield return null; yield return new WaitForEndOfFrame();
                CaptureSpiritFrame(camera, folder + "/01-spirit-left-ordinary-right.png");
                Assert.That(_owner.ReceiveDamage(new DamageRequest(7, 1, 999f, 0f, AttackTag.Heavy)).Killed, Is.True);
                foreach (var renderer in _owner.GetComponentsInChildren<SummonedMinionDissolve>().SelectMany(e => e.GetComponentsInChildren<Renderer>())) renderer.gameObject.layer = 31;
                yield return null; yield return new WaitForEndOfFrame();
                CaptureSpiritFrame(camera, folder + "/02-owner-death-body-removed.png");
                yield return new WaitForSeconds(.32f); yield return new WaitForEndOfFrame();
                CaptureSpiritFrame(camera, folder + "/03-render-only-dissolve.png");
                yield return new WaitForSeconds(.4f); yield return new WaitForEndOfFrame();
                CaptureSpiritFrame(camera, folder + "/04-dissolve-finished.png");
                Assert.That(Object.FindObjectsOfType<SummonedMinionDissolve>(), Is.Empty);
                File.WriteAllText(folder + "/scope.txt", "Live PlayerLoop GPU frames; controlled inspection camera/layers, minion AI explicitly disabled, public lethal damage. Screen left=real summoned entity; screen right=ordinary pure visual prefab. Not natural input, difficulty, main-route, UI or performance acceptance.");
            }
            finally { Object.Destroy(normal); Object.Destroy(cameraRoot); }
        }
        private static void CaptureSpiritFrame(Camera camera, string path)
        {
            var target = new RenderTexture(960, 600, 24); var texture = new Texture2D(960, 600, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                target.Create(); RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; target.Release(); Object.Destroy(target); Object.Destroy(texture); }
        }
        [UnityTest] public IEnumerator AuthorityLoss_ClearsMinionsAndProjectilesAndCannotGenerateAgain()
        {
            yield return WaitForMinion(); FreezeMinionCombatOnly();
            int total = _owner.SpawnedEntityCount; _owner.SetSimulationAuthority(false); _owner.SetSimulationAuthority(false);
            Assert.That(_owner.LivingEntityCount, Is.Zero);
            yield return new WaitForSeconds(1.5f);
            Assert.That(_owner.SpawnedEntityCount, Is.EqualTo(total));
            Assert.That(Object.FindObjectsOfType<MeleeEnemyActor>().Any(a => a.name == "Summoned_Fogwalker"), Is.False);
            Assert.That(Object.FindObjectsOfType<RangedProjectile>().Any(p => p.name == "AshCallerProjectile"), Is.False);
        }
        [UnityTest] public IEnumerator AttackRecovery_KeepsCommittedFacingWhenTargetMovesSideways()
        {
            float end = Time.time + 6f;
            while (_owner.Brain.State != RangedEnemyState.Windup && Time.time < end) yield return null;
            Assert.That(_owner.Brain.State, Is.EqualTo(RangedEnemyState.Windup));
            // Let bounded entry-facing settle, then observe the actual commitment before moving the target.
            // Recomputing LookRotation from next-frame world positions is not the committed attack snapshot.
            yield return new WaitForSeconds(.55f);
            Assert.That(_owner.Brain.State, Is.EqualTo(RangedEnemyState.Windup));
            var target = Object.FindObjectOfType<Emberfall.Gameplay.Combat.Unity.PlayerCombatActor>();
            Vector3 original = target.transform.position;
            Quaternion committed = _owner.transform.rotation;
            var controller = target.GetComponent<CharacterController>();
            try
            {
                controller.enabled = false; target.transform.position += Vector3.right * 4f; controller.enabled = true;
                end = Time.time + 3f;
                while (_owner.Brain.State != RangedEnemyState.Recovery && Time.time < end) yield return null;
                Assert.That(_owner.Brain.State, Is.EqualTo(RangedEnemyState.Recovery));
                Assert.That(Quaternion.Angle(_owner.transform.rotation, committed), Is.LessThan(1f));
                yield return new WaitForSeconds(.2f);
                Assert.That(Quaternion.Angle(_owner.transform.rotation, committed), Is.LessThan(1f));
            }
            finally { controller.enabled = false; target.transform.position = original; controller.enabled = true; }
        }
        [UnityTest] public IEnumerator DisableAndReset_AreIdempotentAndReturnACleanOwner()
        {
            yield return WaitForMinion(); FreezeMinionCombatOnly();
            _owner.gameObject.SetActive(false); _owner.ResetToSpawn(); _owner.ResetToSpawn();
            Assert.That(_owner.Brain.LivingSummonCount, Is.Zero); Assert.That(_owner.SpawnedEntityCount, Is.Zero);
            yield return null;
            Assert.That(Object.FindObjectsOfType<MeleeEnemyActor>().Any(a => a.name == "Summoned_Fogwalker"), Is.False);
            _owner.gameObject.SetActive(true); yield return null;
            Assert.That(_owner.IsAvailable && _owner.HasSimulationAuthority, Is.True);
        }
        [UnityTest] public IEnumerator SceneUnload_ClearsDetachedSpawnedRootsRatherThanLeakingThem()
        {
            yield return WaitForMinion(); FreezeMinionCombatOnly();
            var minion = Object.FindObjectsOfType<MeleeEnemyActor>().Single(a => a.name == "Summoned_Fogwalker");
            var original = SceneManager.GetActiveScene();
            var empty = SceneManager.CreateScene("SummonerUnloadFixture"); SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(original); yield return null;
            Assert.That(_owner == null && minion == null, Is.True);
        }
    }
}
#endif
