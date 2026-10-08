#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Networking;
using Emberfall.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Actual saved clear + passive rendered response; controlled hits, not natural difficulty.</summary>
    public sealed class AshEncounterBannerTests
    {
        bool ownsHost;
        [UnitySetUp] public IEnumerator Begin()
        {
            Time.timeScale = 1;
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            M2LaunchIntent.RequestNewGame(); yield return SceneManager.LoadSceneAsync("10_EmberValley"); yield return null; yield return null;
            Assert.That(Object.FindObjectOfType<M2RouteFlowController>().SavePath, Is.EqualTo(M2RouteFlowController.EditorTestSavePath));
        }
        [UnityTearDown] public IEnumerator End()
        {
            if (ownsHost) { SessionRuntime.Current.Shutdown(); ownsHost = false; yield return null; }
            Time.timeScale = 1;
        }
        static M2EncounterBannerPresenter Banner(AshEncounterBanner kind)
            => Object.FindObjectsOfType<M2EncounterBannerPresenter>(true).Single(p => p.Encounter == kind);
        static void Visible(AshEncounterBanner kind, bool visible)
        {
            var p = Banner(kind); Assert.That(p.IsBannerVisible, Is.EqualTo(visible));
            foreach (var r in p.GetComponentsInChildren<Renderer>(true)) Assert.That(r.enabled, Is.EqualTo(visible));
        }

        [UnityTest] public IEnumerator AuthoredFlagsAreOpaquePureWallDecorWithNoCameraOrGameplayComponents()
        {
            var flags = Object.FindObjectsOfType<M2EncounterBannerPresenter>(true);
            Assert.That(flags.Length, Is.EqualTo(3));
            foreach (var flag in flags)
            {
                Assert.That(flag.transform.parent.name, Is.EqualTo("[Art] Ash Clear Banners"));
                Assert.That(flag.GetComponentsInChildren<Component>(true).All(c => c is Transform || c is MeshFilter || c is MeshRenderer || c is M2EncounterBannerPresenter), Is.True);
                Visible(flag.Encounter, false);
                var r = flag.GetComponentInChildren<Renderer>();
                Assert.That(r.bounds.size.y, Is.LessThan(1.41f));
                Assert.That(r.sharedMaterial.renderQueue, Is.EqualTo(2000));
                Assert.That(r.sharedMaterial.GetFloat("_Surface"), Is.Zero); Assert.That(r.sharedMaterial.GetFloat("_ZWrite"), Is.EqualTo(1));
                Assert.That(r.sharedMaterial.GetFloat("_Cull"), Is.Zero);
                var binding = new SerializedObject(flag).FindProperty("_flow").objectReferenceValue;
                Assert.That(binding, Is.SameAs(Object.FindObjectOfType<M2RouteFlowController>()));
            }
            yield return null;
        }

        [UnityTest] public IEnumerator RealClearShowsBannerWithoutReloadThenContinueKeepsItAndNewGameHidesIt()
        {
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var controller = player.GetComponent<CharacterController>();
            Vector3 offset = player.transform.position - player.NavigationFootPosition;
            controller.enabled = false; player.transform.position = new Vector3(0, .04f, 10.2f) + offset; controller.enabled = true;
            Physics.SyncTransforms(); yield return null;
            var root = GameObject.Find("Content_AshApproach_v1");
            var owner = root.GetComponentInChildren<SummonerEnemyActor>();
            var melee = root.GetComponentsInChildren<MeleeEnemyActor>();
            owner.SetSimulationAuthority(false); foreach (var e in melee) e.SetSimulationAuthority(false);
            yield return new WaitForSeconds(.6f);
            Visible(AshEncounterBanner.Approach, false); Visible(AshEncounterBanner.GuardPass, false); Visible(AshEncounterBanner.Return, false);
            string folder = Path.GetFullPath("Builds/ArtReview/ash-clear-banners/" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(folder + "/scope.txt", "Actual production Game View/rig, isolated save, controlled placement and authority-disabled A before/after real damage clear. No natural input/difficulty/foreground/performance or independent Player restart acceptance.");
            yield return Capture(folder + "/a-before.png");
            owner.SetSimulationAuthority(true);
            Assert.That(owner.ReceiveDamage(new DamageRequest(992, 1, 9999, 100, AttackTag.Heavy)).Killed, Is.True);
            foreach (var e in melee) { e.SetSimulationAuthority(true); Assert.That(e.ReceiveDamage(new DamageRequest(992, 2, 9999, 100, AttackTag.Heavy)).Killed, Is.True); }
            yield return null;
            Assert.That(flow.AshApproachCleared, Is.True);
            int clearFrame = Time.frameCount;
            yield return new WaitForEndOfFrame();
            Assert.That(Time.frameCount, Is.EqualTo(clearFrame), "Observe after LateUpdate in the SAME clear frame, not a delayed retry.");
            Visible(AshEncounterBanner.Approach, true); Visible(AshEncounterBanner.GuardPass, false); Visible(AshEncounterBanner.Return, false);
            yield return Capture(folder + "/a-after.png");
            M2LaunchIntent.RequestContinue(); yield return SceneManager.LoadSceneAsync("10_EmberValley"); yield return null; yield return null;
            Assert.That(Object.FindObjectOfType<M2RouteFlowController>().AshApproachCleared, Is.True);
            Visible(AshEncounterBanner.Approach, true); Visible(AshEncounterBanner.GuardPass, false); Visible(AshEncounterBanner.Return, false);
            M2LaunchIntent.RequestNewGame(); yield return SceneManager.LoadSceneAsync("10_EmberValley"); yield return null; yield return null;
            Visible(AshEncounterBanner.Approach, false); Visible(AshEncounterBanner.GuardPass, false); Visible(AshEncounterBanner.Return, false);
        }

        [UnityTest] public IEnumerator ActualListeningHostDisablesTheWholeNewRootBeforeOfflineInitialization()
        {
            SessionRuntime.Current.Shutdown(); yield return null;
            Assert.That(SessionRuntime.Current.StartHost(47842), Is.True, SessionRuntime.Current.Snapshot.Message);
            ownsHost = true; yield return null;
            yield return SceneManager.LoadSceneAsync("10_EmberValley"); yield return null;
            var root = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "[Art] Ash Clear Banners");
            Assert.That(root.activeSelf, Is.False);
            Assert.That(root.GetComponentsInChildren<Behaviour>(true).All(b => !b.enabled), Is.True);
            Assert.That(root.GetComponentsInChildren<Renderer>(true).All(r => !r.enabled), Is.True);
            var flow = Object.FindObjectOfType<M2RouteFlowController>(true);
            Assert.That(flow.enabled, Is.False); Assert.That(flow.IsInitialized, Is.False);
            Assert.That(Object.FindObjectOfType<NetworkGymSceneController>(true).AuthoredEncounterCount, Is.EqualTo(4));
            var data = new SerializedObject(Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true));
            var array = data.FindProperty("_offlineBehaviours");
            Assert.That(array.arraySize, Is.EqualTo(115));
            Assert.That(Enumerable.Range(0, array.arraySize).All(i => array.GetArrayElementAtIndex(i).objectReferenceValue != null), Is.True);
        }

        static IEnumerator Capture(string path)
        {
            yield return null; ScreenCapture.CaptureScreenshot(path);
            float end = Time.realtimeSinceStartup + 5;
            while (!File.Exists(path) && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(File.Exists(path), Is.True);
        }
    }
}
#endif
