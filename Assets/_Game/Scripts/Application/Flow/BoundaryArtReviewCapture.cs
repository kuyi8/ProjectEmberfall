#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Application.Flow
{
    /// <summary>Explicit diagnostic only: same-pose production-scene before/after, not performance evidence.</summary>
    public sealed class BoundaryArtReviewCapture
    {
        [Serializable] private sealed class Sample
        { public string name; public Vector3 cameraPosition, cameraEuler; public int width=1920,height=1080; }
        [Serializable] private sealed class Report
        { public string measurement="Frozen production scene, offscreen URP StandardRequest; not presented FPS or natural gameplay"; public Sample[] samples; }
        public IEnumerator Run(Camera camera, RenderTexture target)
        {
            bool finished = GameObject.Find("[Art] M6 Boundary Finish") != null;
            string output = "Builds/ArtReview/0.9.2-boundary/" + (finished ? "after-final" : "before");
            Directory.CreateDirectory(output);
            Time.timeScale = 0;
            foreach (var agent in UnityEngine.Object.FindObjectsOfType<UnityEngine.AI.NavMeshAgent>()) agent.enabled=false;
            var samples = new System.Collections.Generic.List<Sample>();
            yield return Capture("forest-route", new Vector3(1,3.4f,25), new Vector3(0,1,35));
            var wall = UnityEngine.Object.FindObjectsOfType<EncounterBoundaryVisualMarker>()
                .Single(x=>x.Segment=="forest-encounter" && x.Face==EncounterBoundaryFace.North);
            Vector3 centre=wall.VisibleRenderer.bounds.center;
            yield return Capture("forest-wall", centre+new Vector3(2,1.9f,-5),centre);
            yield return Capture("bridge-edge",new Vector3(17,4,36),new Vector3(12,-1,45));
            yield return Capture("courtyard-route",new Vector3(20,4,40),new Vector3(29,1,46));
            if (finished)
            {
                // Same build/lights/camera: isolate only the collider-free skirts. This is not a
                // historical-build baseline and cannot establish natural gameplay or performance.
                var skirts=GameObject.Find("[Art] M6 Boundary Finish");
                foreach (bool visible in new[]{false,true})
                {
                    skirts.SetActive(visible);
                    string suffix=visible ? "-wrapped" : "-unwrapped";
                    yield return Capture("cliff-edge"+suffix,new Vector3(24,-1.5f,62),new Vector3(24,-2,55));
                    yield return Capture("far-edge"+suffix,new Vector3(24,-3,108),new Vector3(24,-10,97));
                }
            }
            var gates=UnityEngine.Object.FindObjectsOfType<Transform>(true).Where(x=>x.name.StartsWith("GateBlocker_") && x.GetComponent<Collider>()!=null).ToArray();
            foreach(var gate in gates)
            {
                bool active=gate.gameObject.activeSelf;
                // Diagnostic pose only; authority and normal gameplay are not advanced by this capture.
                gate.gameObject.SetActive(true);
                var face=gate.GetComponent<Renderer>();
                var previousProperties=new MaterialPropertyBlock(); face.GetPropertyBlock(previousProperties);
                var visibleProperties=new MaterialPropertyBlock();
                visibleProperties.SetColor("_BaseColor",face.sharedMaterial.color); face.SetPropertyBlock(visibleProperties);
                Vector3 p=gate.position;
                yield return Capture(gate.name+"-closed",p+new Vector3(-4,2,6),p);
                face.SetPropertyBlock(previousProperties);
                gate.gameObject.SetActive(active);
            }
            yield return SceneManager.LoadSceneAsync("20_Sanctum",LoadSceneMode.Additive);
            yield return Capture("sanctum-wall",new Vector3(1000,-77,994),new Vector3(1000,-78,1011));
            File.WriteAllText(output+"/views.json",JsonUtility.ToJson(new Report{samples=samples.ToArray()},true));
            Time.timeScale=1;
            Debug.Log("[BOUNDARY_REVIEW_COMPLETE] mode="+(finished?"after":"before")+" views="+samples.Count);

            IEnumerator Capture(string name,Vector3 position,Vector3 lookAt)
            {
                camera.transform.position=position; camera.transform.LookAt(lookAt);
                for(int i=0;i<12;i++)yield return null;
                var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
                var previous=RenderTexture.active;
                try
                {
                    RenderTexture.active=target;
                    texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);texture.Apply();
                    File.WriteAllBytes(output+"/"+name+".png",texture.EncodeToPNG());
                }
                finally { RenderTexture.active=previous; UnityEngine.Object.Destroy(texture); }
                samples.Add(new Sample{name=name,cameraPosition=position,cameraEuler=camera.transform.eulerAngles});
            }
        }
    }
}
#endif
