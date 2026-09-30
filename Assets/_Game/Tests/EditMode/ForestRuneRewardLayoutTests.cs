using System.Linq;
using Emberfall.Editor.Setup;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

namespace Emberfall.Tests.EditMode
{
    public sealed class ForestRuneRewardLayoutTests
    {
        [Test] public void Rewards_FollowSeal_KeepTriggersAndAreIdempotent()
        {
            // Preview scenes isolate fixtures without saving/replacing the Test Runner's untitled host.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var seal = new GameObject("Seal_Forest");
                var ember = new GameObject("RuneChoice_Ember");
                var guard = new GameObject("RuneChoice_Guard");
                foreach (var go in new[]{seal,ember,guard}) SceneManager.MoveGameObjectToScene(go,scene);
                seal.transform.position = new Vector3(5.4f,1,42.3f);
                ember.AddComponent<CapsuleCollider>().isTrigger = true;
                guard.AddComponent<CapsuleCollider>().isTrigger = true;
                var before = new[]{ember,guard}.Select(g=>UnityEditor.EditorJsonUtility.ToJson(g.GetComponent<Collider>())).ToArray();
                ForestRuneRewardLayout.ApplyToScene(scene);
                var first = new[]{ember.transform.position,guard.transform.position};
                foreach(var position in first) Assert.That(Vector3.Distance(position,seal.transform.position),Is.LessThan(3f));
                Assert.That(Vector3.Distance(first[0],first[1]),Is.GreaterThan(2f),"Choices must remain individually approachable.");
                Assert.That(new[]{ember,guard}.Select(g=>UnityEditor.EditorJsonUtility.ToJson(g.GetComponent<Collider>())).ToArray(),Is.EqualTo(before));
                ForestRuneRewardLayout.ApplyToScene(scene);
                Assert.That(new[]{ember.transform.position,guard.transform.position},Is.EqualTo(first));
                seal.transform.position += Vector3.forward * 2;
                ForestRuneRewardLayout.ApplyToScene(scene);
                Assert.That(ember.transform.position,Is.EqualTo(first[0]+Vector3.forward*2));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
