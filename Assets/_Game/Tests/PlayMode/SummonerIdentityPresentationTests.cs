#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed class SummonerIdentityPresentationTests
    {
        PlayerCombatActor player; SummonerEnemyActor owner; M2RouteFlowController flow;
        [UnitySetUp] public IEnumerator Begin()
        {
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            M2LaunchIntent.RequestNewGame(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null;
            flow = Object.FindObjectOfType<M2RouteFlowController>(); Assert.That(flow.SavePath, Is.EqualTo(M2RouteFlowController.EditorTestSavePath));
            player = Object.FindObjectOfType<PlayerCombatActor>();
            owner = GameObject.Find("Content_AshApproach_v1").GetComponentInChildren<SummonerEnemyActor>();
        }
        [UnityTearDown] public void End() => Time.timeScale = 1;

        [UnityTest] public IEnumerator AllOwnersHaveStaticNonAuthorityCrestsOutsideAnimatorAndDeathResetIsImmediate()
        {
            var actors = Object.FindObjectsOfType<SummonerEnemyActor>(true); Assert.That(actors.Length, Is.EqualTo(3));
            foreach (var actor in actors)
            {
                var crown = actor.IdentityCrown; Assert.That(crown, Is.Not.Null);
                Assert.That(crown.transform.parent, Is.SameAs(actor.transform));
                Assert.That(crown.transform.IsChildOf(actor.GetComponentInChildren<Animator>(true).transform), Is.False);
                Assert.That(crown.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(crown.GetComponentsInChildren<CombatTarget>(true), Is.Empty);
                Assert.That(crown.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
                Assert.That(crown.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(1));
            }
            Quaternion rotation = owner.IdentityCrown.transform.localRotation; Vector3 position = owner.IdentityCrown.transform.localPosition;
            yield return new WaitForSeconds(.4f);
            Assert.That(owner.IdentityCrown.transform.localPosition, Is.EqualTo(position)); Assert.That(owner.IdentityCrown.transform.localRotation, Is.EqualTo(rotation));
            Assert.That(owner.ReceiveDamage(new DamageRequest(967, 1, 9999, 100, AttackTag.Heavy)).Killed, Is.True);
            Assert.That(owner.IdentityCrown.activeSelf, Is.False, "Hide in the same actual death callback, not after animation or Update.");
            owner.ResetToSpawn(); Assert.That(owner.IdentityCrown.activeSelf, Is.True); Assert.That(owner.IsAvailable, Is.True);
        }

        [UnityTest] public IEnumerator RealSummonDoesNotInheritCrestAndThreeIdentitiesRenderTogetherAtGameDistance()
        {
            foreach (var companion in GameObject.Find("Content_AshApproach_v1").GetComponentsInChildren<MeleeEnemyActor>()) companion.SetSimulationAuthority(false);
            Place(new Vector3(0, 0, 10.5f));
            float deadline = Time.time + 12;
            while (owner.LivingEntityCount == 0 && Time.time < deadline) { Assert.That(player.IsAvailable, Is.True); yield return null; }
            Assert.That(owner.LivingEntityCount, Is.EqualTo(1)); var spirit = owner.GetLivingEntity(0);
            Assert.That(spirit.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Summoner_StaticIdentityCrown"), Is.False);
            Assert.That(spirit.GetComponent<SummonedMinionPresentation>().IdentityFragmentCount, Is.EqualTo(3));
            var priest = Object.FindObjectsOfType<RangedEnemyActor>().Single(p => p.name == "Enemy_RunePriest_Forest");
            priest.SetSimulationAuthority(false); spirit.SetSimulationAuthority(false);
            Place(new Vector3(0, 0, 7.8f)); yield return new WaitForSeconds(.6f);
            // Controlled equal-distance comparison with production Rig and HUD, not natural input/difficulty.
            Time.timeScale = 0;
            Assert.That(owner.GetComponent<NavMeshAgent>().Warp(new Vector3(0, 0, 15.2f)), Is.True);
            Assert.That(priest.GetComponent<NavMeshAgent>().Warp(new Vector3(-1.6f, 0, 15.2f)), Is.True);
            Assert.That(spirit.GetComponent<NavMeshAgent>().Warp(new Vector3(1.6f, 0, 15.2f)), Is.True);
            owner.transform.rotation = priest.transform.rotation = spirit.transform.rotation = Quaternion.Euler(0, 180, 0);
            Physics.SyncTransforms(); yield return new WaitForSecondsRealtime(.6f); yield return null;
            string folder = Path.GetFullPath("Builds/ArtReview/SummonerIdentity/" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")); Directory.CreateDirectory(folder);
            File.WriteAllText(folder + "/scope.txt", "Actual spawned spirit + ordinary Priest + crowned owner in one frame, equal Z, production Rig/HUD. Time frozen only for controlled render; setup Warp is not traversal or natural difficulty evidence. Identity objects remain outside Animator and authority.");
            string path = folder + "/three-identities-production-rig.png"; ScreenCapture.CaptureScreenshot(path);
            float until = Time.realtimeSinceStartup + 4;
            while (!File.Exists(path) && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(File.Exists(path), Is.True); Assert.That(owner.IdentityCrown.activeSelf, Is.True);
            Assert.That(priest.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Summoner_StaticIdentityCrown"), Is.False);
        }
        void Place(Vector3 feet)
        {
            Vector3 offset = player.transform.position - player.NavigationFootPosition;
            var controller = player.GetComponent<CharacterController>(); controller.enabled = false;
            player.transform.position = feet + offset + Vector3.up * .04f; controller.enabled = true; Physics.SyncTransforms();
        }
    }
}
#endif
