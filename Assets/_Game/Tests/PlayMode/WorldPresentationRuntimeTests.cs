#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Actual formal camera after actual PlayerLoop; controlled runtime warp is NOT natural traversal.</summary>
    public sealed class WorldPresentationRuntimeTests
    {
        [Serializable] sealed class Frame
        {
            public string image,scene,areaLabel,lightingScene,camera,rig,state,stateCategory,animatorState;
            public Vector3 actorPosition,cameraPosition,cameraEuler;
            public int frame,width,height,worldIdentityRenderers;
            public float stateElapsed,fieldOfView;
        }
        [Serializable] sealed class Evidence
        {
            public string scope="Real runtime Bootstrap/offline Actor/Motor/AI and existing third-person camera after PlayerLoop. 960x600 URP camera render includes 3D only, not IMGUI HUD, external foreground, a natural route or performance acceptance. Camp is NewGame; Sanctum uses a labelled temporary runtime warp, no quest or encounter facts forced. Original production Ranger, not Tiny.";
            public string savePath;
            public bool sceneBytesSame;
            public Frame[] frames;
        }

        [UnityTest] public IEnumerator FormalCamera_ValleyAndAdditiveSanctum_RenderActualRuntimeWorld()
        {
            Assert.That(M2RouteFlowController.EditorTestSavePath,Does.Contain("IsolatedSaves"));
            string[] paths={"Assets/_Game/Scenes/10_EmberValley.unity","Assets/_Game/Scenes/20_Sanctum.unity"};
            var hashes=paths.ToDictionary(p=>p,Hash);
            string dir=Path.GetFullPath("Builds/ArtReview/world-runtime/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(dir);
            var frames=new List<Frame>();string savePath=null;
            try
            {
                M2LaunchIntent.RequestNewGame();
                yield return SceneManager.LoadSceneAsync("10_EmberValley",LoadSceneMode.Single);
                float deadline=Time.realtimeSinceStartup+8;
                var flow=Object.FindObjectOfType<M2RouteFlowController>();
                while((flow==null||!flow.IsInitialized)&&Time.realtimeSinceStartup<deadline)
                {yield return null;flow=Object.FindObjectOfType<M2RouteFlowController>();}
                Assert.That(flow,Is.Not.Null);Assert.That(flow.IsInitialized,Is.True);
                savePath=flow.SavePath;Assert.That(savePath,Does.Contain("IsolatedSaves"));
                var actor=Object.FindObjectOfType<PlayerCombatActor>();Assert.That(actor,Is.Not.Null);
                var camera=Camera.main;Assert.That(camera,Is.Not.Null);
                Assert.That(camera.GetComponent<ThirdPersonCameraRig>(),Is.Not.Null,"Use the production camera, not a review camera.");
                yield return new WaitForSeconds(.45f);yield return new WaitForEndOfFrame();
                frames.Add(Capture(camera,actor,"valley-camp.png",dir,"NewGame camp"));
                yield return SceneManager.LoadSceneAsync("20_Sanctum",LoadSceneMode.Additive);
                Assert.That(SceneManager.GetActiveScene().name,Is.EqualTo("10_EmberValley"),"Retain Valley's actual lighting environment.");
                var sanctum=SceneManager.GetSceneByName("20_Sanctum");
                var floor=sanctum.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Collider>(true))
                    .Single(c=>c.name=="Sanctum_ArenaFloor");
                var cc=actor.GetComponent<CharacterController>();Assert.That(cc,Is.Not.Null);
                // Fixture relocation only: no RestoreSnapshot/TryEnterSanctum/quest/resource writes.
                Vector3 destination=new Vector3(1000,floor.bounds.max.y+(cc.height*.5f-cc.center.y)*actor.transform.lossyScale.y+.04f,996);
                cc.enabled=false;actor.transform.SetPositionAndRotation(destination,Quaternion.identity);cc.enabled=true;Physics.SyncTransforms();
                yield return new WaitForSeconds(.65f);yield return new WaitForEndOfFrame();
                frames.Add(Capture(camera,actor,"sanctum-entrance-controlled-warp.png",dir,"Sanctum entrance, controlled warp"));
                Assert.That(frames[1].worldIdentityRenderers,Is.EqualTo(10),"Mosaic, two motifs, four pillar visual meshes, two high crowns and one outside-wall arch.");
                var art=sanctum.GetRootGameObjects().Single(r=>r.name=="[Art] Emberfall Sanctum Identity");
                Assert.That(art.GetComponentsInChildren<Collider>(true),Is.Empty);
                Assert.That(art.GetComponentsInChildren<MonoBehaviour>(true),Is.Empty,"No CameraOccluder may be registered on the decorative skyline.");
                Assert.That(frames[1].actorPosition.z,Is.GreaterThan(990));
                // A second actual camera angle inside the room, still labelled fixture
                // staging rather than a natural-input arrival or active boss fight.
                destination.x+=2.5f;destination.z+=6;
                cc.enabled=false;actor.transform.SetPositionAndRotation(destination,Quaternion.Euler(0,-25,0));cc.enabled=true;Physics.SyncTransforms();
                var input=actor.GetComponent<Emberfall.Gameplay.Input.PlayerInputReader>();
                var targeting=actor.GetComponent<Emberfall.Gameplay.Targeting.LockOnTargeting>();
                camera.GetComponent<ThirdPersonCameraRig>().Configure(actor.transform,input,targeting);
                yield return new WaitForSeconds(.65f);yield return new WaitForEndOfFrame();
                frames.Add(Capture(camera,actor,"sanctum-interior-controlled-warp.png",dir,"Sanctum interior, controlled warp and yaw"));
                Assert.That(Mathf.DeltaAngle(frames[1].cameraEuler.y,frames[2].cameraEuler.y),Is.LessThan(-15));
                Assert.That(hashes.All(p=>Hash(p.Key)==p.Value),Is.True);
            }
            finally
            {
                File.WriteAllText(dir+"/actual-camera.json",JsonUtility.ToJson(new Evidence{savePath=savePath,
                    sceneBytesSame=hashes.All(p=>Hash(p.Key)==p.Value),frames=frames.ToArray()},true));
            }
        }

        static Frame Capture(Camera camera,PlayerCombatActor actor,string filename,string dir,string areaLabel)
        {
            var animator=actor.GetComponentInChildren<Animator>();
            var info=animator.GetCurrentAnimatorStateInfo(0);
            var state=actor.Model.State;
            string category=state.ToString().StartsWith("LightAttack",StringComparison.Ordinal)||state.ToString()=="HeavyAttack"||state.ToString()=="Sweep"||state.ToString()=="Execution"?"attack":"non-attack";
            var row=new Frame{image=filename,scene=actor.gameObject.scene.name,camera=camera.name,rig=animator.avatar.name,
                areaLabel=areaLabel,lightingScene=SceneManager.GetActiveScene().name,fieldOfView=camera.fieldOfView,
                state=state.ToString(),stateCategory=category,animatorState="hash:"+info.fullPathHash+" normalized:"+info.normalizedTime,
                actorPosition=actor.transform.position,cameraPosition=camera.transform.position,cameraEuler=camera.transform.eulerAngles,
                frame=Time.frameCount,width=960,height=600,stateElapsed=actor.Model.StateElapsed};
            var sanctum=SceneManager.GetSceneByName("20_Sanctum");
            if(sanctum.IsValid()&&sanctum.isLoaded)
                row.worldIdentityRenderers=sanctum.GetRootGameObjects().Where(r=>r.name=="[Art] Emberfall Sanctum Identity")
                    .SelectMany(r=>r.GetComponentsInChildren<Renderer>(true)).Count();
            var rt=new RenderTexture(row.width,row.height,24);var tex=new Texture2D(row.width,row.height,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                rt.Create();RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=rt});
                RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,row.width,row.height),0,0);tex.Apply();
                File.WriteAllBytes(dir+"/"+filename,tex.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;rt.Release();Object.Destroy(rt);Object.Destroy(tex);}
            return row;
        }
        static string Hash(string path)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    }
}
#endif
