using System.Collections;
using System.Collections.Generic;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class CombatBurstVfxPoolTests
    {
        private readonly List<Scene> _scenes = new List<Scene>();
        private float _timeScale;
        private GameObject _prefab;

        [SetUp]
        public void Setup()
        {
            _timeScale = Time.timeScale;
#if UNITY_EDITOR
            _prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/_Game/Prefabs/VFX/M6Art/P_M6_Impact_Steel.prefab");
#endif
            Assert.That(_prefab, Is.Not.Null);
        }

        private Scene NewScene()
        {
            var scene = SceneManager.CreateScene("VfxBudgetTest_" + System.Guid.NewGuid());
            _scenes.Add(scene);
            return scene;
        }

        private GameObject NewObject(Scene scene, string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private bool Spawn(CombatBurstVfxPool pool, CombatBurstKind kind = CombatBurstKind.Steel,
            float lifetime = 10f, GameObject prefab = null) =>
            pool.TrySpawn(kind, prefab != null ? prefab : _prefab, Vector3.zero, Quaternion.identity, lifetime);

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Time.timeScale = _timeScale;
            foreach (var scene in _scenes)
                if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            _scenes.Clear();
        }

        [UnityTest]
        public IEnumerator Cap_IsSharedAcrossScenesAndPrefabs_ButKindsAreIndependent()
        {
            var first = CombatBurstVfxPool.ForScene(NewScene());
            var second = CombatBurstVfxPool.ForScene(NewScene());
            first.Prewarm(CombatBurstKind.Steel, _prefab);
            Assert.That(first.CreatedCount, Is.EqualTo(3));
            Assert.That(Spawn(first), Is.True);
            Assert.That(Spawn(second), Is.True);
            Assert.That(Spawn(first), Is.True);
            var alternate = NewObject(second.gameObject.scene, "AlternatePrefab");
            alternate.AddComponent<ParticleSystem>(); alternate.SetActive(false);
            Assert.That(Spawn(second, prefab: alternate), Is.False, "A different prefab must not bypass the semantic budget.");
            Assert.That(Spawn(second, CombatBurstKind.Guard), Is.True);
            Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.Steel), Is.EqualTo(3));
            Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.Guard), Is.EqualTo(1));
            Assert.That(second.DroppedCount, Is.EqualTo(1));
            first.enabled = false;
            Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.Steel), Is.EqualTo(1));
            Assert.That(Spawn(first), Is.False);
            first.enabled = true;
            Assert.That(Spawn(first), Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WarmedPressure_ReusesThreeInstances_AndExpiryHonorsPause()
        {
            var pool = CombatBurstVfxPool.ForScene(NewScene());
            pool.Prewarm(CombatBurstKind.Steel, _prefab);
            for (int wave = 0; wave < 5; wave++)
            {
                for (int i = 0; i < 30; i++)
                    Assert.That(Spawn(pool, lifetime: .08f), Is.EqualTo(i < 3));
                Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.Steel), Is.EqualTo(3));
                if (wave == 0)
                {
                    Time.timeScale = 0;
                    yield return new WaitForSecondsRealtime(.15f);
                    Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.Steel), Is.EqualTo(3));
                    Time.timeScale = 1;
                }
                yield return new WaitForSeconds(.12f);
                Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.Steel), Is.Zero);
            }
            Assert.That(pool.CreatedCount, Is.EqualTo(3));
            Assert.That(pool.DroppedCount, Is.EqualTo(135));
            Debug.Log("[M6_VFX] event=pool-pressure requests=150 spawned=15 dropped=135 created=3 peak=3");
        }

        [UnityTest]
        public IEnumerator Unload_ReleasesGlobalBudget_AndReentryCreatesFreshSceneCache()
        {
            var scene = NewScene();
            var pool = CombatBurstVfxPool.ForScene(scene);
            for (int i = 0; i < 3; i++) Assert.That(Spawn(pool), Is.True);
            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.That(pool == null, Is.True);
            Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.Steel), Is.Zero);
            var next = CombatBurstVfxPool.ForScene(NewScene());
            Assert.That(next.CreatedCount, Is.Zero);
            Assert.That(Spawn(next), Is.True);
        }

        [UnityTest]
        public IEnumerator Presenters_DeduplicateConfirmedSequences_AndShareOneBudget()
        {
            var scene = NewScene();
            var first = NewObject(scene, "AttackerA").AddComponent<CombatImpactVfxPresenter>();
            var second = NewObject(scene, "AttackerB").AddComponent<CombatImpactVfxPresenter>();
            first.Configure(null, _prefab, null, null);
            second.Configure(null, _prefab, null, null);
            for (ulong i = 1; i <= 2; i++)
            {
                var impact = new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Steel,
                    i, (int)i, 8, HitFeedbackGrade.Light, ImpactSurface.Flesh);
                first.Present(impact); first.Present(impact); second.Present(impact);
            }
            Assert.That(first.PresentedCount + second.PresentedCount, Is.EqualTo(3));
            Assert.That(first.DroppedCount + second.DroppedCount, Is.EqualTo(1));
            Assert.That(CombatBurstVfxPool.ForScene(scene).CreatedCount, Is.EqualTo(3));
            first.enabled = false;
            first.Present(new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Steel,
                3, 3, 8, HitFeedbackGrade.Light, ImpactSurface.Flesh));
            Assert.That(first.DroppedCount, Is.Zero);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GradeEffects_UseIndependentSharedCaps_AndReplayDoesNotRespawn()
        {
            var scene = NewScene();
            var first = NewObject(scene, "GradeA").AddComponent<CombatImpactVfxPresenter>();
            var second = NewObject(scene, "GradeB").AddComponent<CombatImpactVfxPresenter>();
            first.ConfigureGradeEffects(_prefab, _prefab, _prefab);
            second.ConfigureGradeEffects(_prefab, _prefab, _prefab);
            ulong sequence = 0;
            foreach (var grade in new[] { HitFeedbackGrade.GuardBreak, HitFeedbackGrade.Execution, HitFeedbackGrade.Sweep })
            {
                for (int i = 0; i < 2; i++)
                {
                    var impact = new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Steel,
                        ++sequence, (int)sequence, 8, grade, ImpactSurface.Flesh);
                    first.Present(impact); first.Present(impact); second.Present(impact);
                }
            }
            Assert.That(first.PresentedCount + second.PresentedCount, Is.EqualTo(9));
            Assert.That(first.DroppedCount + second.DroppedCount, Is.EqualTo(3));
            foreach (var kind in new[] { CombatBurstKind.GuardBreak, CombatBurstKind.Execution, CombatBurstKind.Sweep })
                Assert.That(CombatBurstVfxPool.ActiveCount(kind), Is.EqualTo(3));
            Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.Steel), Is.Zero);
            yield return new WaitForSeconds(.8f);
            foreach (var kind in new[] { CombatBurstKind.GuardBreak, CombatBurstKind.Execution, CombatBurstKind.Sweep })
                Assert.That(CombatBurstVfxPool.ActiveCount(kind), Is.Zero);
        }

        [UnityTest]
        public IEnumerator Sweep_MultiTargetConfirmationEmitsOneArc_BreakStillHasOwnAccent()
        {
            var presenter = NewObject(NewScene(), "SweepAttacker").AddComponent<CombatImpactVfxPresenter>();
            presenter.ConfigureGradeEffects(_prefab, _prefab, _prefab);
            for (ulong i = 1; i <= 3; i++)
                presenter.Present(new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Steel,
                    i, 7, (int)i, HitFeedbackGrade.Sweep, ImpactSurface.Flesh));
            presenter.Present(new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Guard,
                4, 7, 4, HitFeedbackGrade.GuardBreak, ImpactSurface.Metal));
            Assert.That(presenter.PresentedCount, Is.EqualTo(2));
            Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.Sweep), Is.EqualTo(1));
            Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.GuardBreak), Is.EqualTo(1));
            presenter.Present(new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Steel,
                5, 8, 1, HitFeedbackGrade.Sweep, ImpactSurface.Flesh));
            Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.Sweep), Is.EqualTo(2));
            yield return null;
        }

        [UnityTest]
        public IEnumerator MissingGradeVariant_FallsBackToStyle_AndUnconfirmedDoesNotSpawn()
        {
            var presenter = NewObject(NewScene(), "LegacyAttacker").AddComponent<CombatImpactVfxPresenter>();
            presenter.Configure(null, _prefab, null, null);
            presenter.Present(new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Steel));
            Assert.That(presenter.PresentedCount, Is.Zero);
            presenter.Present(new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Steel,
                1, 1, 8, HitFeedbackGrade.Execution, ImpactSurface.Flesh));
            Assert.That(presenter.PresentedCount, Is.EqualTo(1));
            Assert.That(CombatBurstVfxPool.ActiveCount(CombatBurstKind.Steel), Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SaturatedGroundBlast_DoesNotSuppressWarningOrFuseCallback()
        {
            var scene = NewScene();
            var pool = CombatBurstVfxPool.ForScene(scene);
            for (int i = 0; i < 3; i++) Assert.That(Spawn(pool, CombatBurstKind.GroundBlast), Is.True);
            int resolved = 0;
            var rune = NewObject(scene, "CappedRune").AddComponent<RangedGroundRune>();
            rune.Configure(1, 1, .05f, 2f, 10f, 10f, null, false, _ => resolved++, _prefab);
            Assert.That(rune.WarningSegmentCount, Is.EqualTo(10));
            yield return new WaitForSeconds(.15f);
            Assert.That(resolved, Is.EqualTo(1));
            Assert.That(pool.DroppedCount, Is.EqualTo(1));
            yield return new WaitForSeconds(.25f);
            Assert.That(rune == null, Is.True);
            Assert.That(resolved, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SaturatedGroundBlast_DealsExactlyTheSameAuthoritativeDamage()
        {
            var scene = NewScene();
            var pool = CombatBurstVfxPool.ForScene(scene);
            var tuning = ScriptableObject.CreateInstance<CombatTuningAsset>();
            float baseline = 0;
            try
            {
                for (int capped = 0; capped < 2; capped++)
                {
                    pool.enabled = false; pool.enabled = true;
                    if (capped == 1)
                        for (int i = 0; i < 3; i++) Assert.That(Spawn(pool, CombatBurstKind.GroundBlast), Is.True);
                    var playerRoot = NewObject(scene, "RuneDamageTarget");
                    playerRoot.SetActive(false);
                    playerRoot.transform.position = new Vector3(10000 + capped * 100, 0, 0);
                    var input = playerRoot.AddComponent<PlayerInputReader>(); input.enabled = false;
                    var player = playerRoot.AddComponent<PlayerCombatActor>();
                    player.Configure(input, tuning, playerRoot.transform, playerRoot.transform, null, null);
                    playerRoot.AddComponent<CapsuleCollider>();
                    playerRoot.SetActive(true);
                    player.enabled = false; // Isolate the real damage adapter from unrelated input/ticks.
                    var rune = NewObject(scene, "AuthoritativeRune").AddComponent<RangedGroundRune>();
                    rune.transform.position = playerRoot.transform.position;
                    float damage = 0; int callbacks = 0;
                    rune.Configure(123, capped + 1, .05f, 2f, 20f, 10f, null, true,
                        result => { damage += result.AppliedDamage; callbacks++; }, _prefab);
                    Physics.SyncTransforms();
                    yield return new WaitForSeconds(.15f);
                    Assert.That(callbacks, Is.EqualTo(1));
                    Assert.That(damage, Is.GreaterThan(0));
                    if (capped == 0) baseline = damage;
                    else Assert.That(damage, Is.EqualTo(baseline));
                }
                Assert.That(pool.DroppedCount, Is.EqualTo(1));
                Debug.Log($"[M6_VFX] event=cap-independent-damage baseline={baseline} capped={baseline} callbacks=1");
            }
            finally { Object.Destroy(tuning); }
        }
    }
}
