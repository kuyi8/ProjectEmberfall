using System.Collections;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed class PlayerSwordIntegrationTests
    {
        const string SwordName = "Sword_M6_Player_Equipped";
        const string Prefab = "Assets/_Game/Prefabs/Weapons/Player/P_Player_KayKitSword.prefab";

        static void RequireSaveIsolation()
        {
#if UNITY_EDITOR
            Assert.That(M2RouteFlowController.EditorTestSavePath, Is.Not.Null.And.Not.Empty);
            Assert.That(System.IO.Path.GetFullPath(M2RouteFlowController.EditorTestSavePath).Replace('\\', '/'),
                Does.Contain("/Builds/TestResults/IsolatedSaves/"));
#endif
        }
        static Transform Sword(PlayerCombatActor actor) => actor.GetComponentsInChildren<Transform>(true).Single(t => t.name == SwordName);
        static void AssertBinding(PlayerCombatActor actor)
        {
            Transform sword = Sword(actor); var animator = actor.GetComponentInChildren<Animator>();
            Assert.That(animator.GetBoneTransform(HumanBodyBones.Hips).name, Is.EqualTo("pelvis"));
            Assert.That(sword.parent.name, Is.EqualTo("WeaponSocket_RightHand"));
            Assert.That(sword.parent.parent, Is.SameAs(animator.GetBoneTransform(HumanBodyBones.RightHand)));
            Assert.That((sword.lossyScale - Vector3.one).magnitude, Is.LessThan(.001f));
            Assert.That(sword.GetComponentsInChildren<MeshRenderer>(true), Has.Length.EqualTo(1));
            Assert.That(sword.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(sword.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
#if UNITY_EDITOR
            // Scene runtime objects no longer retain Editor prefab correspondence.
            // Check the actual rendered asset references and authored local placement,
            // not an Editor-only connection API on the running clone.
            var expected = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            Assert.That(expected, Is.Not.Null);
            Assert.That(sword.GetComponentInChildren<MeshFilter>().sharedMesh, Is.SameAs(expected.GetComponentInChildren<MeshFilter>().sharedMesh));
            Assert.That(sword.GetComponentInChildren<MeshRenderer>().sharedMaterial, Is.SameAs(expected.GetComponentInChildren<MeshRenderer>().sharedMaterial));
            Transform model = sword.Find("Model"), expectedModel = expected.transform.Find("Model");
            Assert.That(Vector3.Distance(model.localPosition, expectedModel.localPosition), Is.LessThan(.00001f));
            Assert.That(Quaternion.Angle(model.localRotation, expectedModel.localRotation), Is.LessThan(.01f));
            Assert.That(Vector3.Distance(model.localScale, expectedModel.localScale), Is.LessThan(.00001f));
#endif
        }

        [UnityTest] public IEnumerator ValleyAndGymLoadExactlyOneOwnedSwordWithOriginalHandSocket()
        {
            foreach (string scene in new[] { "10_EmberValley", "90_CombatGym" })
            {
                RequireSaveIsolation(); yield return SceneManager.LoadSceneAsync(scene, LoadSceneMode.Single);
                yield return null; yield return null;
                AssertBinding(Object.FindObjectOfType<PlayerCombatActor>());
                var flow = Object.FindObjectOfType<M2RouteFlowController>();
#if UNITY_EDITOR
                if (flow != null) Assert.That(flow.SavePath, Is.EqualTo(M2RouteFlowController.EditorTestSavePath));
#endif
            }
        }

        [UnityTest] public IEnumerator RealComboKeepsTrailTipOnActualVisibleBladeMesh()
        {
            RequireSaveIsolation(); yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null; yield return null;
            foreach (var enemy in Object.FindObjectsOfType<MonoBehaviour>())
                if (enemy is MeleeEnemyActor || enemy is RangedEnemyActor || enemy is ShieldEnemyActor) enemy.enabled = false;
            var actor = Object.FindObjectOfType<PlayerCombatActor>(); AssertBinding(actor);
            Transform sword = Sword(actor); var filter = sword.GetComponentInChildren<MeshFilter>();
            Vector3[] sourceVertices;
#if UNITY_EDITOR
            // Keep the immutable importer's Read/Write disabled. Editor's public
            // read-only MeshData API inspects real geometry without enabling it.
            using (var data = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(filter.sharedMesh))
            using (var buffer = new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount, Unity.Collections.Allocator.Temp))
            {
                data[0].GetVertices(buffer); sourceVertices = buffer.ToArray();
            }
#else
            sourceVertices = filter.sharedMesh.vertices;
#endif
            Vector3[] vertices = sourceVertices.Select(v => sword.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray();
            float top = vertices.Max(v => v.y);
            Vector3 tipLocal = vertices.Where(v => Mathf.Abs(v.y - top) < .0001f).OrderBy(v => v.x * v.x + v.z * v.z).First();
            var trail = actor.GetComponent<SwordTrailPresenter>();
            Transform tip = NaturalPlayerContactTests.Field<Transform>(trail, "_bladeTip");
            Transform root = NaturalPlayerContactTests.Field<Transform>(trail, "_bladeRoot");
            bool second = false, third = false, reachedThird = false; int observed = 0;
            Assert.That(actor.Model.Submit(CombatCommand.LightAttack), Is.True);
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null; observed++;
                Assert.That(Vector3.Distance(tip.position, sword.TransformPoint(tipLocal)), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(root.position, sword.position), Is.LessThan(.001f));
                AssertBinding(actor);
                if (!second && actor.Model.State == CombatState.LightAttack1 && actor.Model.StateElapsed > actor.Model.LightRecoveryStart + .01f)
                    second = actor.Model.Submit(CombatCommand.LightAttack);
                if (!third && actor.Model.State == CombatState.LightAttack2 && actor.Model.StateElapsed > actor.Model.LightRecoveryStart + .01f)
                    third = actor.Model.Submit(CombatCommand.LightAttack);
                reachedThird |= actor.Model.State == CombatState.LightAttack3;
                if (reachedThird && actor.Model.State == CombatState.Locomotion) break;
            }
            Assert.That(observed, Is.GreaterThan(3)); Assert.That(second && third && reachedThird, Is.True);
            Assert.That(actor.Model.State, Is.EqualTo(CombatState.Locomotion));
        }

        [UnityTest] public IEnumerator KnifeInitializationCachesNewSwordRatherThanRemovedRenderers()
        {
            RequireSaveIsolation(); yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null; yield return null;
            var actor = Object.FindObjectOfType<PlayerCombatActor>(); AssertBinding(actor);
            var visual = actor.GetComponent<PlayerKnifePresentation>(); Assert.That(visual.IsConfigured, Is.True);
            Renderer[] cached = NaturalPlayerContactTests.Field<Renderer[]>(visual, "_sword");
            Assert.That(cached, Is.EquivalentTo(Sword(actor).GetComponentsInChildren<Renderer>(true)));
            Assert.That(cached.All(r => r != null && r.enabled && !r.forceRenderingOff), Is.True);
        }
    }
}
