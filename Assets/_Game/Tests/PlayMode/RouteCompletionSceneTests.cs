#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Infrastructure.Saves;
using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed class RouteCompletionSceneTests
    {
        [UnitySetUp] public IEnumerator Begin()
        {
            Time.timeScale = 1f;
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley"); yield return null; yield return null;
            Assert.That(Object.FindObjectOfType<M2RouteFlowController>().SavePath, Is.EqualTo(M2RouteFlowController.EditorTestSavePath));
        }
        [UnityTearDown] public void End() => Time.timeScale = 1f;

        [UnityTest] public IEnumerator PartialClearReadbackMatchesIndependentPersistedFlagsAfterFreshSceneInitialization()
        {
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            Assert.That(RouteCompletionPresentation.Read(flow, player).Encounters,
                Is.EqualTo(RouteCompletionPresentation.DescribeEncounters(false, false, false)));
            var body = player.GetComponent<CharacterController>();
            Vector3 offset = player.transform.position - player.NavigationFootPosition;
            body.enabled = false; player.transform.position = new Vector3(0, .04f, 11) + offset; body.enabled = true;
            Physics.SyncTransforms(); yield return null;
            var group = GameObject.Find("Content_AshApproach_v1");
            var owner = group.GetComponentInChildren<SummonerEnemyActor>();
            var companions = group.GetComponentsInChildren<MeleeEnemyActor>();
            Assert.That(companions.Length, Is.EqualTo(2));
            Assert.That(owner.ReceiveDamage(new DamageRequest(991, 1, 9999, 100, AttackTag.Heavy)).Killed, Is.True);
            Assert.That(companions[0].ReceiveDamage(new DamageRequest(991, 2, 9999, 100, AttackTag.Heavy)).Killed, Is.True);
            yield return null;
            Assert.That(flow.AshApproachCleared, Is.False, "Partial death is not a clear, nor evidence of no participation.");
            flow.SaveSessionProgress();
            M2LaunchIntent.RequestContinue(); yield return SceneManager.LoadSceneAsync("10_EmberValley"); yield return null; yield return null;
            flow = Object.FindObjectOfType<M2RouteFlowController>(); player = Object.FindObjectOfType<PlayerCombatActor>();
            Assert.That(flow.AshApproachCleared, Is.False);
            Assert.That(GameObject.Find("Content_AshApproach_v1").GetComponentsInChildren<MeleeEnemyActor>().Length, Is.EqualTo(2));
            Assert.That(RouteCompletionPresentation.Read(flow, player).Encounters, Does.Contain("尚未清场（可选）"));

            body = player.GetComponent<CharacterController>(); offset = player.transform.position - player.NavigationFootPosition;
            body.enabled = false; player.transform.position = new Vector3(0, .04f, 11) + offset; body.enabled = true;
            Physics.SyncTransforms(); yield return null;
            group = GameObject.Find("Content_AshApproach_v1"); owner = group.GetComponentInChildren<SummonerEnemyActor>();
            companions = group.GetComponentsInChildren<MeleeEnemyActor>();
            owner.ReceiveDamage(new DamageRequest(991, 3, 9999, 100, AttackTag.Heavy));
            foreach (var member in companions) member.ReceiveDamage(new DamageRequest(991, 4, 9999, 100, AttackTag.Heavy));
            yield return null;
            Assert.That(flow.AshApproachCleared, Is.True);
            string expected = RouteCompletionPresentation.DescribeEncounters(true, false, false);
            Assert.That(RouteCompletionPresentation.Read(flow, player).Encounters, Is.EqualTo(expected));
            flow.SaveSessionProgress();
            // Construct a new disk store and a new scene/flow, not just reuse the in-memory model.
            // This is restart-equivalent save reconstruction, NOT a separate Player-process restart.
            var disk = new JsonSaveGameStore(flow.SavePath).LoadOrCreate(() => null);
            Assert.That(disk.Save.schemaVersion, Is.EqualTo(SaveGameV1.CurrentSchemaVersion));
            Assert.That(disk.Save.routeEnrichment.ashApproachCleared, Is.True);
            Assert.That(disk.Save.routeEnrichment.ashGuardPassCleared, Is.False);
            Assert.That(disk.Save.routeEnrichment.ashReturnCleared, Is.False);
            M2LaunchIntent.RequestContinue(); yield return SceneManager.LoadSceneAsync("10_EmberValley"); yield return null; yield return null;
            flow = Object.FindObjectOfType<M2RouteFlowController>(); player = Object.FindObjectOfType<PlayerCombatActor>();
            Assert.That(RouteCompletionPresentation.Read(flow, player).Encounters, Is.EqualTo(expected));
            Assert.That(RouteCompletionPresentation.Read(flow, player).Journey,
                Is.EqualTo(RouteCompletionPresentation.DescribeJourney(flow.SessionElapsedSeconds, flow.DeathCount, player.ActiveCheckpointId.Value)));
            Assert.That(GameObject.Find("Content_AshApproach_v1").GetComponentsInChildren<SummonerEnemyActor>(), Is.Empty);
        }

        [UnityTest] public IEnumerator ActualChineseFontFitsLongestSupportedValuesWithoutShrinkingOrButtonOverlap()
        {
            yield return new WaitForSeconds(.15f);
            var type = typeof(M2RouteHud).Assembly.GetType("Emberfall.UI.RuntimeGuiFont", true);
            var font = (Font)type.GetProperty("Chinese", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
            Assert.That(font, Is.Not.Null);
            var style = new GUIStyle { font = font, fontSize = 16, wordWrap = true,
                alignment = TextAnchor.UpperLeft, padding = new RectOffset(0, 0, 0, 0) };
            var l = RouteCompletionLayout.Resolve(1280, 800);
            string[] bodies = {
                "路线抉择：补给路线\n\n增援抉择：守住窄口 · 两人先出、后补一人",
                "路线抉择：险径路线\n\n增援抉择：引到空地 · 三人同时在场",
                "废弃补给车：已取得（重击架势 +15%）\n险径奖励：已取得（药剂上限 +1）\n旧瞭望塔：已发现",
                RouteCompletionPresentation.DescribeEncounters(false, false, false),
                RouteCompletionPresentation.DescribeEncounters(true, true, true),
                RouteCompletionPresentation.DescribeJourney(359999, 9999, "checkpoint:courtyard") };
            Rect box = RouteCompletionLayout.Body(l.Choices);
            foreach (string text in bodies)
                Assert.That(style.CalcHeight(new GUIContent(text), box.width), Is.LessThanOrEqualTo(box.height), text);
            style.fontSize = 14;
            Assert.That(style.CalcHeight(new GUIContent(RouteCompletionPresentation.ScopeNote), l.Scope.width), Is.LessThanOrEqualTo(l.Scope.height));
            Assert.That(l.Scope.Overlaps(l.Return), Is.False);
        }
    }
}
#endif
