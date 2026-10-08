#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.Application.Flow;
using Emberfall.Networking;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Saved six-marker structure and controlled grazing-angle render; not natural discovery or performance.</summary>
    public sealed class ChoiceMarkerArtSceneTests
    {
        const string Child="[Art] Choice Stone Plaque";
        static readonly string[] Names={"RuneChoice_Ember","RuneChoice_Guard","RouteChoice_Supply","RouteChoice_Risk","AshReinforcement_StagedReinforcement","AshReinforcement_TogetherReinforcement"};
        bool ownsHost;
        [Serializable] sealed class Row { public string anchor,image,withoutPlaqueImage;public Vector3 cameraPosition;public float bottom,top,highestFloor,lowestFloor;public Vector3[] floorPoints; }
        [Serializable] sealed class Report {public string scope="Actual saved Valley plaques at six existing anchors, nine actual solid-floor probes per plaque and controlled 960x540 near-grazing world-only URP camera renders. No original camera/input/scene/saves authored, no crowd-state fabrication, no natural traversal/discovery, human/foreground/performance or package acceptance.";public Row[] rows;}

        [UnitySetUp] public IEnumerator Begin()
        {
            Assert.That(M2RouteFlowController.EditorTestSavePath,Does.Contain("IsolatedSaves"));
            M2LaunchIntent.RequestNewGame();yield return SceneManager.LoadSceneAsync("10_EmberValley");yield return null;yield return null;
            Assert.That(Object.FindObjectOfType<M2RouteFlowController>().SavePath,Is.EqualTo(M2RouteFlowController.EditorTestSavePath));
        }
        [UnityTearDown] public IEnumerator End(){if(ownsHost){SessionRuntime.Current.Shutdown();ownsHost=false;yield return null;}}

        [UnityTest] public IEnumerator AllSixPlaquesHaveOneVisibleBaseAndGeometricFloorSeparationAtGrazingAngles()
        {
            var all=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            Assert.That(all.Count(t=>t.name==Child),Is.EqualTo(6));
            var rows=new List<Row>();string folder=Path.GetFullPath("Builds/ArtReview/choice-plaque-grazing/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(folder);
            var cameraObject=new GameObject("OwnedPlaqueGrazingCamera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
            try
            {
                foreach(string name in Names)
                {
                    var anchor=all.Single(t=>t.name==name);var art=anchor.Find(Child);Assert.That(art,Is.Not.Null);
                    var legacy=name.StartsWith("RuneChoice_")?anchor.GetComponent<Renderer>():anchor.Find("InteractionMarker").GetComponent<Renderer>();
                    Assert.That(legacy.enabled,Is.False,name);Assert.That(legacy.GetComponent<MeshFilter>().sharedMesh,Is.Not.Null,"Legacy geometry remains recoverable.");
                    Assert.That(anchor.GetComponent<Collider>().isTrigger,Is.True);Assert.That(art.GetComponentsInChildren<Component>(true).All(c=>c is Transform||c is MeshFilter||c is MeshRenderer),Is.True);
                    var renderers=art.GetComponentsInChildren<Renderer>(true);Assert.That(renderers,Is.Not.Empty);Assert.That(renderers.All(r=>r.enabled),Is.True);
                    var b=renderers[0].bounds;foreach(var r in renderers.Skip(1))b.Encapsulate(r.bounds);
                    Assert.That(b.size.x,Is.EqualTo(.86f).Within(.001));Assert.That(b.size.z,Is.EqualTo(.86f).Within(.001));Assert.That(b.size.y,Is.EqualTo(.02f).Within(.001));
                    var points=new List<Vector3>();foreach(float x in new[]{-.43f,0,.43f})foreach(float z in new[]{-.43f,0,.43f})
                    {
                        var hits=Physics.RaycastAll(anchor.position+new Vector3(x,3,z),Vector3.down,5,~0,QueryTriggerInteraction.Ignore)
                            .Where(h=>h.collider.name=="Zone_Forest"||h.collider.name=="Zone_Courtyard"||h.collider.name=="Path_Bridge"||h.collider.name=="Path_Forest"||
                                (h.transform.root.name=="[Art] M6 Environment"&&h.transform.parent!=null&&h.transform.parent.name.Contains("_Paving_")))
                            .OrderBy(h=>h.distance).ToArray();
                        Assert.That(hits,Is.Not.Empty,"Existing floor, not an enemy body, determines plaque placement.");points.Add(hits[0].point);
                    }
                    float highest=points.Max(p=>p.y);Assert.That(b.min.y-highest,Is.EqualTo(.005f).Within(.001),"Actual bottom above sampled floor, not only the ornament's highest vertex.");
                    // Observe from the approachable south side. The first north-side
                    // review was blocked by existing seal boulders, not a useful
                    // plaque-surface inspection; keep that evidence separately.
                    camera.transform.position=b.center+new Vector3(1.2f,.42f,-3.2f);camera.transform.LookAt(b.center);camera.fieldOfView=35;camera.nearClipPlane=.03f;camera.farClipPlane=120;
                    yield return new WaitForEndOfFrame();string image=name+"-grazing.png";Capture(camera,folder+"/"+image);
                    string without=name+"-without-owned-plaque.png";
                    try{foreach(var r in renderers)r.enabled=false;Capture(camera,folder+"/"+without);}finally{foreach(var r in renderers)r.enabled=true;}
                    rows.Add(new Row{anchor=name,image=image,withoutPlaqueImage=without,cameraPosition=camera.transform.position,bottom=b.min.y,top=b.max.y,highestFloor=highest,lowestFloor=points.Min(p=>p.y),floorPoints=points.ToArray()});
                }
            }
            finally{Object.Destroy(cameraObject);File.WriteAllText(folder+"/report.json",JsonUtility.ToJson(new Report{rows=rows.ToArray()},true));}
        }

        [UnityTest] public IEnumerator ActualHostDisablesPlaquesThroughTheExistingChoiceRootsWithoutAnotherNetEntry()
        {
            SessionRuntime.Current.Shutdown();yield return null;Assert.That(SessionRuntime.Current.StartHost(47843),Is.True);ownsHost=true;yield return null;
            yield return SceneManager.LoadSceneAsync("10_EmberValley");yield return null;
            var data=new SerializedObject(Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true));
            Assert.That(data.FindProperty("_offlineActorRoots").arraySize,Is.EqualTo(43));Assert.That(data.FindProperty("_offlineBehaviours").arraySize,Is.EqualTo(115));
            var list=data.FindProperty("_offlineActorRoots");var roots=Enumerable.Range(0,list.arraySize).Select(i=>list.GetArrayElementAtIndex(i).objectReferenceValue as GameObject).ToArray();
            Assert.That(roots.All(r=>r!=null),Is.True);Assert.That(roots.Distinct().Count(),Is.EqualTo(43));
            var all=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            foreach(string name in Names)
            {
                var anchor=all.Single(t=>t.name==name);
                Assert.That(roots.Any(r=>anchor==r.transform||anchor.IsChildOf(r.transform)),Is.True,name+": exact six-choice table must be covered by real isolation roots.");
                Assert.That(anchor.gameObject.activeInHierarchy,Is.False);Assert.That(anchor.Find(Child).gameObject.activeInHierarchy,Is.False);
            }
            Assert.That(Object.FindObjectOfType<NetworkGymSceneController>(true).AuthoredEncounterCount,Is.EqualTo(4));
            Assert.That(Object.FindObjectOfType<M2RouteFlowController>(true).IsInitialized,Is.False);
        }
        static void Capture(Camera camera,string path)
        {
            var rt=RenderTexture.GetTemporary(960,540,24);var previous=RenderTexture.active;var texture=new Texture2D(960,540,TextureFormat.RGB24,false);
            try{RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,960,540),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());}
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);Object.Destroy(texture);}
        }
    }
}
#endif
