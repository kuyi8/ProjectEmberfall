#if UNITY_EDITOR
using System.Collections;
using System.IO;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Actual production actor/Animator/GUI, controlled hits, not natural player difficulty.</summary>
    public sealed class SummonerInterruptFeedbackTests
    {
        PlayerCombatActor player; SummonerEnemyActor owner; Animator animator;
        [UnitySetUp] public IEnumerator Begin()
        {
            Time.timeScale = 1f;
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null;
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            Assert.That(flow.SavePath, Is.EqualTo(M2RouteFlowController.EditorTestSavePath));
            player = Object.FindObjectOfType<PlayerCombatActor>();
            owner = GameObject.Find("Content_AshApproach_v1").GetComponentInChildren<SummonerEnemyActor>();
            foreach (var companion in owner.transform.parent.GetComponentsInChildren<MeleeEnemyActor>()) companion.SetSimulationAuthority(false);
            animator = owner.GetComponentInChildren<Animator>();
            var controller = player.GetComponent<CharacterController>();
            Vector3 offset = player.transform.position - player.NavigationFootPosition;
            controller.enabled = false; player.transform.position = new Vector3(0, .04f, 10.5f) + offset;
            controller.enabled = true; Physics.SyncTransforms();
        }
        [UnityTearDown] public void End() => Time.timeScale = 1f;
        IEnumerator Chant()
        {
            float end = Time.time + 12f;
            while (!owner.Brain.IsSummoning && Time.time < end) yield return null;
            Assert.That(owner.Brain.IsSummoning, Is.True, "Real AI must enter the authored interruptible chant.");
        }
        DamageResult Interrupt(int sequence = 1) => owner.ReceiveDamage(new DamageRequest(player.CombatantId, sequence, 10f, 0f, AttackTag.Projectile));

        [UnityTest] public IEnumerator DamageInterruptStopsTelegraphShowsRealReactionAndDoesNotMoveGameplayRoot()
        {
            yield return Chant(); yield return new WaitForSeconds(.15f);
            var so = new UnityEditor.SerializedObject(owner);
            var telegraph = (Transform)so.FindProperty("_telegraph").objectReferenceValue;
            var animations = (PlayerAnimationSet)so.FindProperty("_animationSet").objectReferenceValue;
            Assert.That(telegraph.gameObject.activeSelf, Is.True);
            int sequence = owner.Brain.AttackSequence;
            Vector3 root = owner.transform.position; Quaternion facing = owner.transform.rotation;
            Assert.That(Interrupt().AppliedDamage, Is.GreaterThan(0f));
            Assert.That(owner.HasRecentSummonInterrupt, Is.True);
            Assert.That(owner.Brain.State, Is.EqualTo(RangedEnemyState.HitReact));
            Assert.That(telegraph.gameObject.activeSelf, Is.False, "Abandoned chant must vanish in the actual hit callback.");
            Assert.That(owner.Brain.AttackSequence, Is.EqualTo(sequence));
            Assert.That(AnimatorSpeedCoordinator.For(animator).BaseSpeed,
                Is.EqualTo(animations.GetClip(CombatState.HitReact).length / owner.Definition.Combat.HitReactDuration).Within(.0001f));
            yield return new WaitForSeconds(.06f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("HitReact"), Is.True);
            Assert.That(Vector3.Distance(root, owner.transform.position), Is.LessThan(.005f));
            Assert.That(Quaternion.Angle(facing, owner.transform.rotation), Is.LessThan(.1f));
            Assert.That(owner.LivingEntityCount, Is.Zero);
            string folder = Path.GetFullPath("Builds/ArtReview/SummonerFeedback/" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(folder);
            string path = folder + "/real-hitreact-interrupt-hud.png";
            File.WriteAllText(folder + "/scope.txt", "Production main route, real AI chant, controlled damage callback, native Animator reaction and production HUD. No relocation of enemy. Not natural input/difficulty evidence.");
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSeconds(.35f);
            Assert.That(owner.Brain.State, Is.Not.EqualTo(RangedEnemyState.HitReact), "Visual mapping must not lengthen the domain reaction.");
            Assert.That(owner.HasRecentSummonInterrupt, Is.True);
            if (owner.Brain.State == RangedEnemyState.Windup)
                Assert.That(SummonerHudPresentation.Resolve(owner.Brain.State, owner.Brain.CurrentAttack,
                    owner.LivingEntityCount, owner.HasRecentSummonInterrupt, true).TextId, Is.Not.EqualTo("text:summoner.interrupted"));
            float end = Time.realtimeSinceStartup + 4f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(File.Exists(path), Is.True);
        }

        [UnityTest] public IEnumerator PostureCounterReportsOnlyActualChantInterruptWithoutHealthDamage()
        {
            yield return Chant(); float health = owner.Brain.Health.Current;
            Assert.That(owner.ApplyNeutralPostureDamage(999f), Is.GreaterThan(0));
            Assert.That(owner.Brain.Health.Current, Is.EqualTo(health));
            Assert.That(owner.Brain.InterruptCount, Is.EqualTo(1)); Assert.That(owner.HasRecentSummonInterrupt, Is.True);
            Assert.That(owner.IsPostureExecutionWindow, Is.True);
            owner.ApplyNeutralPostureDamage(999f);
            Assert.That(owner.Brain.InterruptCount, Is.EqualTo(1), "Duplicate posture hits must not invent new successes.");
            yield return null;
        }

        [UnityTest] public IEnumerator PostReleaseAndNonAuthorityHitsDoNotInventInterruptSuccess()
        {
            yield return Chant(); owner.SetSimulationAuthority(false);
            Assert.That(Interrupt().Accepted, Is.False); Assert.That(owner.HasRecentSummonInterrupt, Is.False);
            Assert.That(owner.ApplyNeutralPostureDamage(999f), Is.Zero);
            owner.SetSimulationAuthority(true);
            float end = Time.time + 4f;
            while (!owner.Brain.IsReleaseOpen && Time.time < end) yield return null;
            Assert.That(owner.Brain.IsReleaseOpen, Is.True);
            Assert.That(Interrupt(2).AppliedDamage, Is.GreaterThan(0));
            Assert.That(owner.Brain.InterruptCount, Is.Zero); Assert.That(owner.HasRecentSummonInterrupt, Is.False);
        }

        [UnityTest] public IEnumerator DeathResetDisableAndAuthorityLossClearReceiptWithoutReplayingIt()
        {
            yield return Chant(); Interrupt(); owner.SetSimulationAuthority(false);
            Assert.That(owner.HasRecentSummonInterrupt, Is.False);
            owner.SetSimulationAuthority(true); Assert.That(owner.HasRecentSummonInterrupt, Is.False);
            owner.ResetToSpawn(); Assert.That(owner.HasRecentSummonInterrupt, Is.False);
            yield return Chant(); Interrupt(2); Assert.That(owner.HasRecentSummonInterrupt, Is.True);
            owner.gameObject.SetActive(false); Assert.That(owner.HasRecentSummonInterrupt, Is.False);
            owner.gameObject.SetActive(true); Assert.That(owner.HasRecentSummonInterrupt, Is.False);
            owner.ResetToSpawn(); yield return Chant(); Interrupt(3);
            Assert.That(owner.ReceiveDamage(new DamageRequest(player.CombatantId, 4, 9999f, 0f, AttackTag.Heavy)).Killed, Is.True);
            Assert.That(owner.HasRecentSummonInterrupt, Is.False);
            owner.ResetToSpawn(); Assert.That(owner.HasRecentSummonInterrupt, Is.False);
        }

        [UnityTest] public IEnumerator NewReceiptsFitTheExistingChineseStatusPlate()
        {
            yield return new WaitForSeconds(.15f);
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            // Use the production font without making UI's implementation detail public for tests.
            var fontType = typeof(M2RouteHud).Assembly.GetType("Emberfall.UI.RuntimeGuiFont", true);
            var font = (Font)fontType.GetProperty("Chinese", System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            var style = new GUIStyle { font = font, fontSize = 16,
                fontStyle = FontStyle.Bold, wordWrap = true, alignment = TextAnchor.MiddleCenter };
            foreach (string id in new[] { "text:summoner.interrupted", "text:summoner.projectile-released" })
            {
                string text = flow.Resolve(new Emberfall.Core.Identifiers.ContentId(id));
                Assert.That(text, Is.Not.EqualTo(id));
                Assert.That(style.CalcHeight(new GUIContent(text), 304f), Is.LessThanOrEqualTo(25f), text);
            }
            style.fontSize = 14; style.fontStyle = FontStyle.Normal;
            foreach (string id in new[] { "text:message.reinforcement-incoming", "text:message.reinforcement-arrived" })
            {
                string text = flow.Resolve(new Emberfall.Core.Identifiers.ContentId(id));
                float height = style.CalcHeight(new GUIContent(text), 298f);
                foreach (bool checklist in new[] { false, true })
                    Assert.That(PresentationHudLayout.Resolve(1280f, checklist, height).QuestMessage(checklist).height,
                        Is.GreaterThanOrEqualTo(height), text);
            }
        }

        [UnityTest] public IEnumerator RealOwnerShieldAndTwoSpiritsKeepOneReadableLiveCastPlate()
        {
            float end = Time.time + 24f;
            while ((owner.LivingEntityCount < 2 || owner.Brain.State != RangedEnemyState.Windup) && Time.time < end)
            {
                for (int slot = 0; slot < 2; slot++) owner.GetLivingEntity(slot)?.SetSimulationAuthority(false);
                Assert.That(player.IsAvailable, Is.True); yield return null;
            }
            Assert.That(owner.LivingEntityCount, Is.EqualTo(2));
            Assert.That(owner.Brain.State, Is.EqualTo(RangedEnemyState.Windup));
            Assert.That(owner.Brain.CurrentAttack, Is.EqualTo(SummonerAttackKind.Projectile));
            var shield = GameObject.Find("Content_AshGuardPass_v1").GetComponentInChildren<ShieldEnemyActor>();
            shield.SetSimulationAuthority(false);
            var left = owner.GetLivingEntity(0); var right = owner.GetLivingEntity(1);
            left.SetSimulationAuthority(false); right.SetSimulationAuthority(false);
            // Controlled readability layout after REAL spawns. Freeze/warp is explicitly not route or difficulty evidence.
            Time.timeScale = 0f;
            Assert.That(owner.GetComponent<UnityEngine.AI.NavMeshAgent>().Warp(new Vector3(0, 0, 15.2f)), Is.True);
            Assert.That(shield.GetComponent<UnityEngine.AI.NavMeshAgent>().Warp(new Vector3(-1.5f, 0, 15.2f)), Is.True);
            Assert.That(left.GetComponent<UnityEngine.AI.NavMeshAgent>().Warp(new Vector3(1.3f, 0, 15.2f)), Is.True);
            Assert.That(right.GetComponent<UnityEngine.AI.NavMeshAgent>().Warp(new Vector3(2.5f, 0, 15.2f)), Is.True);
            owner.transform.rotation = shield.transform.rotation = left.transform.rotation = right.transform.rotation = Quaternion.Euler(0, 180, 0);
            var controller = player.GetComponent<CharacterController>();
            Vector3 offset = player.transform.position - player.NavigationFootPosition;
            controller.enabled = false; player.transform.position = new Vector3(0, .04f, 7.8f) + offset; controller.enabled = true;
            Physics.SyncTransforms(); yield return new WaitForSecondsRealtime(.6f);
            var hud = Object.FindObjectOfType<M2RouteHud>(); Assert.That(hud.ObservedLivingSummonCount, Is.EqualTo(2));
            var display = SummonerHudPresentation.Resolve(owner.Brain.State, owner.Brain.CurrentAttack, 2, false, false);
            Assert.That(display.TextId, Is.EqualTo("text:summoner.projectile"));
            Assert.That(display.Emphasized, Is.True); Assert.That(owner.IdentityCrown.activeSelf, Is.True);
            string folder = Path.GetFullPath("Builds/ArtReview/SummonerFeedback/" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(folder + "/scope.txt", "REAL two spawned spirits, production caller and shield with production Rig/HUD. Controlled frozen four-unit layout via Warp. Static crown unchanged. This is a readability diagnostic, not natural combat, traversal, or player difficulty acceptance.");
            string path = folder + "/four-unit-live-cast-production-hud.png";
            ScreenCapture.CaptureScreenshot(path);
            float until = Time.realtimeSinceStartup + 4f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(File.Exists(path), Is.True);
        }
    }
}
#endif
