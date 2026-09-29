using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Bounded editor-only feasibility experiment. Saves evidence, never production assets.</summary>
    public static class TinyHeroAdaptationReview
    {
        const string Candidate = "Assets/_Game/Art/Review/TinyHero/P_Review_TinyHero_Polyart.prefab";
        const string Knife = "Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Player_ThrowingKnife.prefab";
        const string PosePath = "Assets/_Game/Settings/KnifeGripPose_Ranger.asset";
        const float MinHeadScale = .65f, MaxLift = .12f;
        [Serializable] sealed class Sample
        {
            public string variant, action;
            public float fraction, bodyMinY, headMinY, footBoneMinY, headScale, lift, weaponMinY;
        }
        [Serializable] sealed class Evidence
        {
            public string scope = "Discrete fixed-root editor poses, not natural play/contact or a production adaptation.";
            public float minimumHeadScale = MinHeadScale, maximumLift = MaxLift;
            public bool frozenUnchanged;
            public Vector3 swordLocalPosition, swordLocalEuler, swordSourceBounds;
            public Vector3 knifeLocalGrip, knifeLocalEuler;
            public string[] mappedGripJoints, missingGripJoints;
            public Sample[] samples;
        }
        sealed class Snapshot
        {
            readonly Transform[] bones;
            readonly Vector3[] positions, scales;
            readonly Quaternion[] rotations;
            public Snapshot(GameObject go)
            {
                bones = go.GetComponentsInChildren<Transform>(true);
                positions = bones.Select(t=>t.localPosition).ToArray();
                scales = bones.Select(t=>t.localScale).ToArray(); rotations = bones.Select(t=>t.localRotation).ToArray();
            }
            public void Restore()
            {
                for(int i=0;i<bones.Length;i++)
                { bones[i].SetLocalPositionAndRotation(positions[i],rotations[i]); bones[i].localScale=scales[i]; }
            }
        }
        public static void Capture()
        {
            if (!UnityEngine.Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Graphics EditMode batch required.");
            string dir="Builds/ArtReview/0.9.6-tinyhero-adaptation/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(dir);
            var protectedFiles=Directory.GetFiles("Assets/RPG Tiny Hero Duo","*",SearchOption.AllDirectories)
                .Concat(new[]{"Assets/_Game/Scenes/10_EmberValley.unity","Assets/_Game/Scenes/90_CombatGym.unity",
                    "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab",
                    "Assets/_Game/Resources/Networking/P_M5_NetworkGymPlayer.prefab",Knife,PosePath,Candidate})
                .Concat(Directory.GetFiles("Assets/_Game/Settings","*",SearchOption.AllDirectories))
                .Concat(Directory.GetFiles("Assets/_Game/Art/Animations","*",SearchOption.AllDirectories))
                .Distinct().ToDictionary(p=>p,Hash);
            File.WriteAllLines(dir+"/frozen-before.txt",protectedFiles.Select(p=>p.Value+" "+p.Key));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            TinyHeroBakeoff.SetupLight();
            TinyHeroBakeoff.Cube("Ground",new Vector3(0,-.055f,0),new Vector3(20,.1f,20),new Color(.38f,.43f,.46f));
            var camera=new GameObject("Review camera").AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.35f,.48f,.61f);
            camera.nearClipPlane=.01f; camera.orthographic=true; camera.orthographicSize=1.2f;
            TinyHeroBakeoff.View(camera,new Vector3(3,1.8f,5.5f),new Vector3(0,.75f,0));
            var data=new Evidence(); var samples=new List<Sample>();
            using (var rig=new TinyHeroBakeoff.Rig(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Candidate))))
            {
                var animator=rig.animator;
                var originalSword=rig.go.GetComponentsInChildren<MeshRenderer>().Single(r=>r.name=="OHS03Polyart");
                foreach(var r in rig.go.GetComponentsInChildren<Renderer>().Where(r=>r.name.Contains("Shield"))) r.gameObject.SetActive(false);
                var snapshot=new Snapshot(rig.go);
                var set=AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(M1AnimationSetup.AnimationSetPath);
                var dodge=set.GetOfflineClip(CombatState.Dodge);
                var head=animator.GetBoneTransform(HumanBodyBones.Head);
                string[] variants={"baseline","head-scale-0.65","root-lift-0.12","combined"};
                // Explicitly no foot-height compensation: a failed uplift must remain visible.
                for(int mode=0;mode<variants.Length;mode++)
                {
                    for(int i=0;i<=40;i++)
                    {
                        float f=i/40f; snapshot.Restore(); rig.Pose(dodge,dodge.length*f);
                        float weight=Envelope(f);
                        float scale=(mode==1||mode==3)?Mathf.Lerp(1,MinHeadScale,weight):1;
                        float lift=(mode==2||mode==3)?MaxLift*weight:0;
                        head.localScale*=scale; rig.go.transform.position+=Vector3.up*lift;
                        var row=Measure(rig,variants[mode],"Dodge",f,scale,lift,originalSword);
                        samples.Add(row);
                        if(i%5==0) SaveFrame(camera,dir+$"/roll-{mode}-{i:D2}.png",400,400);
                    }
                }
                MakeSheet(dir,"roll-before-after.png",4,9,(row,col)=>$"roll-{row}-{col*5:D2}.png",400,400);

                snapshot.Restore();
                var idle=AssetDatabase.LoadAllAssetsAtPath(M1AnimationSetup.Library1Path).OfType<AnimationClip>().Single(c=>c.name=="Rig|Idle_Loop");
                rig.Pose(idle,.25f);
                Transform hand=animator.GetBoneTransform(HumanBodyBones.RightHand);
                // This Avatar has thumb/index chains, not all fifteen mapped finger joints.
                // Use its authored weapon socket instead of assuming a MiddleProximal bone exists.
                Transform weaponSocket=hand.GetComponentsInChildren<Transform>().Single(t=>t.name=="weapon_r");
                Vector3 palm=weaponSocket.position;
                Vector3 gripLocal=hand.InverseTransformPoint(palm);
                Vector3 bladeDirection=new Vector3(.15f,.25f,1).normalized;
                var swordMesh=originalSword.GetComponent<MeshFilter>().sharedMesh;
                Bounds swordBounds=swordMesh.bounds; data.swordSourceBounds=swordBounds.size;
                int axis=swordBounds.size.x>swordBounds.size.y?0:1;
                if(swordBounds.size.z>swordBounds.size[axis]) axis=2;
                Vector3 sourceAxis=Vector3.zero; sourceAxis[axis]=Mathf.Sign(swordBounds.center[axis]);
                if(sourceAxis[axis]==0) sourceAxis[axis]=1;
                Vector3 sourceGrip=swordBounds.center-sourceAxis*swordBounds.size[axis]*.40f;
                // One constant hand-local socket, no per-frame sword aiming or mesh scale change.
                Quaternion swordLocalRotation=Quaternion.Inverse(hand.rotation)*Quaternion.FromToRotation(sourceAxis,bladeDirection);
                Vector3 swordScale=originalSword.transform.lossyScale;
                var fittedSword=Object.Instantiate(originalSword.gameObject);
                fittedSword.name="Preview Sword"; fittedSword.transform.SetParent(hand,false);
                fittedSword.transform.localScale=new Vector3(swordScale.x/hand.lossyScale.x,swordScale.y/hand.lossyScale.y,swordScale.z/hand.lossyScale.z);
                fittedSword.transform.localRotation=swordLocalRotation;
                fittedSword.transform.localPosition=gripLocal-swordLocalRotation*Vector3.Scale(sourceGrip,fittedSword.transform.localScale);
                data.swordLocalPosition=fittedSword.transform.localPosition; data.swordLocalEuler=swordLocalRotation.eulerAngles;
                fittedSword.SetActive(false);
                var walk=AssetDatabase.LoadAllAssetsAtPath(M1AnimationSetup.Library1Path).OfType<AnimationClip>().Single(c=>c.name=="Rig|Walk_Loop");
                var actions=new Dictionary<string,AnimationClip>{{"Idle",idle},{"Walk",walk},{"Light1",set.GetOfflineClip(CombatState.LightAttack1)}};
                foreach(var action in actions)
                {
                    for(int row=0;row<2;row++)
                    for(int i=0;i<=4;i++)
                    {
                        snapshot.Restore(); rig.Pose(action.Value,action.Value.length*i*.24f);
                        originalSword.gameObject.SetActive(row==0); fittedSword.SetActive(row==1);
                        samples.Add(Measure(rig,row==0?"original-sword":"fitted-sword",action.Key,i*.24f,1,0,
                            row==0?originalSword:fittedSword.GetComponent<MeshRenderer>()));
                        SaveFrame(camera,dir+$"/sword-{action.Key}-{row}-{i}.png",400,400);
                    }
                    MakeSheet(dir,$"sword-{action.Key}.png",2,5,(r,c)=>$"sword-{action.Key}-{r}-{c}.png",400,400);
                }
                originalSword.gameObject.SetActive(false); fittedSword.SetActive(false);
                var knife=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Knife));
                foreach(var script in knife.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
                foreach(var c in knife.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach(var r in knife.GetComponentsInChildren<TrailRenderer>(true)) Object.DestroyImmediate(r);
                var oldGrip=AssetDatabase.LoadAssetAtPath<KnifeGripPose>(PosePath);
                data.mappedGripJoints=oldGrip.joints.Where(j=>animator.GetBoneTransform(j.bone)!=null).Select(j=>j.bone.ToString()).ToArray();
                data.missingGripJoints=oldGrip.joints.Where(j=>animator.GetBoneTransform(j.bone)==null).Select(j=>j.bone.ToString()).ToArray();
                var knifeClip=set.GetOfflineClip(CombatState.RangedAttack);
                data.knifeLocalGrip=gripLocal;
                for(int row=0;row<2;row++)
                for(int i=0;i<4;i++)
                {
                    snapshot.Restore(); rig.Pose(knifeClip,knifeClip.length*i*.13f);
                    if(row==1) foreach(var joint in oldGrip.joints)
                    { var bone=animator.GetBoneTransform(joint.bone); if(bone!=null) bone.localRotation=joint.rotation; }
                    // Trial uses unchanged Ranger rotations but a new palm-positioned visual anchor only.
                    Quaternion rotation=hand.rotation*oldGrip.gripLocalRotation;
                    Vector3 anchor=hand.TransformPoint(gripLocal);
                    knife.transform.SetPositionAndRotation(anchor+rotation*Vector3.forward*.13f,rotation);
                    data.knifeLocalEuler=oldGrip.gripLocalRotation.eulerAngles;
                    var point=hand.position;
                    camera.orthographicSize=.34f;
                    TinyHeroBakeoff.View(camera,point+new Vector3(.55f,.35f,.7f),point+Vector3.forward*.06f);
                    SaveFrame(camera,dir+$"/knife-hand-{row}-{i}.png",500,500);
                }
                MakeSheet(dir,"knife-hand-trial.png",2,4,(r,c)=>$"knife-hand-{r}-{c}.png",500,500);
                File.WriteAllText(dir+"/method.txt","Roll rows: baseline / Head uniform scale>=0.65 / visual-root lift<=0.12m / combined. " +
                    "Columns: 0..1 in 0.125 steps; JSON dense 0.025 steps. Smooth envelope on .05-.18, off .60-.86. " +
                    "Shield hidden in all rows, sword original in roll. Sword rows original/fitted; .00,.24,.48,.72,.96. " +
                    "Knife rows source hand / available mapped subset of Ranger16-joint rotations; missing joints listed in JSON. Columns .00,.13,.26,.39. " +
                    "Knife photos follow hand, no release or collision proof. No asset SaveAssets/SaveScene calls.");
            }
            data.samples=samples.ToArray(); data.frozenUnchanged=protectedFiles.All(p=>Hash(p.Key)==p.Value);
            File.WriteAllText(dir+"/measurements.json",JsonUtility.ToJson(data,true));
            File.WriteAllText("Builds/ArtReview/0.9.6-tinyhero-adaptation/latest.txt",dir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            if(!data.frozenUnchanged) throw new InvalidOperationException("Protected assets changed.");
            Debug.Log("[TINY_HERO_ADAPTATION] "+dir);
        }
        static float Envelope(float f)=>Mathf.SmoothStep(0,1,Mathf.InverseLerp(.05f,.18f,f))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.60f,.86f,f)));
        static Sample Measure(TinyHeroBakeoff.Rig rig,string variant,string action,float f,float scale,float lift,Renderer weapon)
        {
            var head=rig.animator.GetBoneTransform(HumanBodyBones.Head);
            var b=TinyHeroBakeoff.VertexBounds(rig.go);
            var headBounds=TinyHeroBakeoff.VertexBounds(head.gameObject);
            return new Sample { variant=variant,action=action,fraction=f,bodyMinY=b.min.y,headMinY=headBounds.min.y,
                footBoneMinY=Mathf.Min(rig.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,
                    rig.animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y),headScale=scale,lift=lift,weaponMinY=weapon.bounds.min.y };
        }
        static void SaveFrame(Camera camera,string path,int w,int h)
        { var texture=TinyHeroBakeoff.Render(camera,w,h); File.WriteAllBytes(path,texture.EncodeToPNG()); Object.DestroyImmediate(texture); }
        static void MakeSheet(string dir,string name,int rows,int columns,Func<int,int,string> frame,int w,int h)
        {
            var sheet=new Texture2D(w*columns,h*rows,TextureFormat.RGB24,false);
            for(int row=0;row<rows;row++) for(int col=0;col<columns;col++)
            { var image=new Texture2D(2,2); image.LoadImage(File.ReadAllBytes(dir+"/"+frame(row,col))); sheet.SetPixels(col*w,(rows-row-1)*h,w,h,image.GetPixels()); Object.DestroyImmediate(image); }
            sheet.Apply(); File.WriteAllBytes(dir+"/"+name,sheet.EncodeToPNG()); Object.DestroyImmediate(sheet);
        }
        static string Hash(string path) { using var sha=SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-",""); }
    }
}
