using System.Collections;
using System.Linq;
using Emberfall.Application.Flow;
using Emberfall.Infrastructure.Saves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emberfall.Tests.PlayMode
{
    public sealed class BoundaryGateTests
    {
        [UnityTest]
        public IEnumerator AllFourGates_KeepImmediateCollisionAndCompleteVisualRemoval_WhenOpenedAndReclosed()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            yield return null; yield return null;
            var flow=Object.FindObjectOfType<M2RouteFlowController>();
            flow.enabled=false;
            foreach(var existing in Object.FindObjectsOfType<M2StageBarrier>())existing.enabled=false;
            var gates=Object.FindObjectsOfType<Transform>(true).Where(x=>x.name.StartsWith("GateBlocker_") && x.GetComponent<Collider>()!=null).ToArray();
            Assert.That(gates.Length,Is.EqualTo(4));
            foreach(var gate in gates)
            {
                var owner=new GameObject("BoundaryGateProbe");
                gate.gameObject.SetActive(true);
                var barrier=M2StageBarrier.CreateManual(owner,gate.gameObject);
                barrier.SetOpen(false,true);
                var renderer=gate.GetComponent<Renderer>();
                Assert.That(renderer.sharedMaterial.shader.name,Is.EqualTo("Emberfall/BoundaryRuneBarrier"));
                Assert.That(gate.GetComponentsInChildren<Collider>(true).All(x=>x.enabled),Is.True);
                barrier.SetOpen(true);
                Assert.That(gate.GetComponentsInChildren<Collider>(true).All(x=>!x.enabled),Is.True);
                yield return new WaitForSeconds(.25f);
                var properties=new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                Assert.That(properties.GetColor("_BaseColor").a,Is.InRange(.01f,.61f));
                yield return new WaitForSeconds(.35f);
                Assert.That(gate.gameObject.activeInHierarchy,Is.False);
                Assert.That(gate.GetComponentsInChildren<Renderer>(true).All(x=>!x.gameObject.activeInHierarchy),Is.True);
                barrier.SetOpen(false);
                yield return new WaitForSeconds(.6f);
                Assert.That(gate.gameObject.activeInHierarchy,Is.True);
                Assert.That(barrier.VisualOpacity,Is.EqualTo(1));
                Assert.That(gate.GetComponentsInChildren<Collider>(true).All(x=>x.enabled),Is.True);
                Object.Destroy(owner);
            }
            new JsonSaveGameStore(flow.SavePath).DeleteAllRevisions();
        }
    }
}
