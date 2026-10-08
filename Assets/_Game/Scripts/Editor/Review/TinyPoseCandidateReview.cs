using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Disposable, target-rig pose baking. Never wires a production Profile or changes authority.</summary>
    public static class TinyPoseCandidateReview
    {
        public const string AvatarPath = "Assets/_Game/Art/Review/TinyHero/P_Review_TinyHero_Polyart.prefab";
        const string Native = "Assets/RPG Tiny Hero Duo/Animation/SwordAndShield/";
        const string Throw = "Assets/_Game/Art/Animations/Player/Derived/A_Player_RangedAttack_Dedicated.anim";
        const int SampleRate = 120;

        [Serializable] sealed class Row
        {
            public string action;
            public bool footIK;
            public float time, minY, headMinY, hipX, hipZ, hipY, hipYaw, handStep, jointAngleStep;
            public Vector3 hand, leftFoot, rightFoot;
        }
        [Serializable] sealed class Preflight
        {
            public string clip;
            public int footTargetBindings;
            public float ikOffMinY, ikOnMinY, maxFootIkBoneDelta, maxPoseRoundTripBoneDelta;
        }
        [Serializable] sealed class Report
        {
            public string scope = "Target-rig controlled pose bake/IK comparison and CPU-skin images ONLY. Not actual-input, wall collision, HitStop, natural contact or production acceptance.";
            public string assets, avatarHash;
            public int frozenFiles;
            public bool frozenFilesUnchanged;
            public float dodgeDuration, invulnerability, knifeDuration, releaseTime;
            public Vector3 referenceBodyPosition;
            public Quaternion referenceBodyRotation;
            public float referenceHipHeight,foldSourceTime;
            public string knifeMethod = "Target-rig sampled anticipation/release/follow-through poses; smooth fixed-domain intervals. Not a rescaled continuous retarget of the original wrist flip.";
            public string bodyOrigin = "Target native Idle hips height; body COM recalculated for changed limbs. No floor-driven root lift or Actor transform change.";
            public Preflight[] preflight;
            public Row[] samples;
        }

        // Smooth, fixed envelopes in DOMAIN seconds, not another runtime clock.
        public static float Envelope(float time, float riseEnd, float fallStart, float fallEnd)
        {
            if (time <= 0 || time >= fallEnd) return 0;
            if (time < riseEnd) return Mathf.SmoothStep(0, 1, time / riseEnd);
            if (time <= fallStart) return 1;
            return 1 - Mathf.SmoothStep(0, 1, (time - fallStart) / (fallEnd - fallStart));
        }

        public static string MuscleProperty(string muscle)
        {
            foreach (string side in new[] { "Left", "Right" })
                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    string prefix = side + " " + finger + " ";
                    if (muscle.StartsWith(prefix, StringComparison.Ordinal))
                        return side + "Hand." + finger + "." + muscle.Substring(prefix.Length);
                }
            return muscle;
        }

        public static string BakeAndCapture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Idle graphics Editor required.");
            for (int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve unsaved scenes.");
            string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            string dir = Path.GetFullPath("Builds/ArtReview/tiny-pose/" + id);
            string assets = "Assets/_Game/Art/Review/TinyHero/PoseCandidates/" + id;
            Directory.CreateDirectory(dir);
            var frozen = new[] {"Assets/RPG Tiny Hero Duo","Assets/_Game/Art/Animations/Player","Assets/_Game/Settings",
                "Assets/_Game/Scenes","Assets/_Game/Prefabs/Characters","Assets/_Game/Resources/Networking"}
                .SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).Distinct().ToDictionary(p=>p,Hash);
            File.WriteAllText(dir+"/frozen-before.txt",string.Join("\n",frozen.Select(p=>p.Value+" "+p.Key)));
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            var rows = new List<Row>();
            var tuning = AssetDatabase.LoadAssetAtPath<CombatTuningAsset>("Assets/_Game/Settings/CombatTuning_M1.asset").CreateRuntimeCopy();
            var idle = Clip(Native+"Idle_Battle_SwordAndShiled.fbx");
            var defend = Clip(Native+"Defend_SwordAndShield.fbx");
            var jump = Clip(Native+"InPlace/JumpFull_Normal_InPlace_SwordAndShield.fbx");
            var throwing = Clip(Throw);
            var report = new Report {assets=assets, frozenFiles=frozen.Count, avatarHash=AssetDatabase.GetAssetDependencyHash(AvatarPath).ToString(),
                dodgeDuration=tuning.DodgeDuration, invulnerability=tuning.DodgeInvulnerabilitySeconds,
                knifeDuration=tuning.RangedDuration, releaseTime=tuning.RangedReleaseTime};
            try
            {
                var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPath));
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.GetComponentInChildren<Animator>().gameObject.AddComponent<Emberfall.Gameplay.Animation.AnimatorSpeedCoordinator>();
                using (var rig = new TinyHeroBakeoff.Rig(go))
                using (var handler = new HumanPoseHandler(rig.animator.avatar, rig.animator.transform))
                {
                    report.preflight = new[] { idle, defend, jump, throwing }.Select(c => CheckIkAndRoundTrip(rig, handler, c)).ToArray();
                    File.WriteAllText(dir+"/preflight.json",JsonUtility.ToJson(report,true));
                    // Sample on THIS avatar at identity. Do not copy another avatar's hip height,
                    // or assume world/local conversion from API prose without a round-trip test.
                    rig.Pose(idle, idle.length*.375f);
                    HumanPose baseline = Pose(handler);
                    float baselineHipHeight=rig.animator.GetBoneTransform(HumanBodyBones.Hips).position.y;
                    var hips=rig.animator.GetBoneTransform(HumanBodyBones.Hips);
                    Vector3 baselineHipLocalPosition=hips.localPosition;
                    Quaternion baselineHipLocalRotation=hips.localRotation;
                    report.referenceHipHeight=baselineHipHeight;
                    report.referenceBodyPosition=baseline.bodyPosition;
                    report.referenceBodyRotation=baseline.bodyRotation;
                    rig.Pose(defend, defend.length*.45f); HumanPose guard=Pose(handler);
                    // A clip's 50% is not necessarily its tucked pose. Select actual
                    // bilateral leg flexion, independent of the source's root jump.
                    HumanPose fold=baseline;float bestFlexion=float.NegativeInfinity;
                    for(int i=0;i<=120;i++)
                    {
                        float time=Mathf.Min(jump.length-.0001f,i*jump.length/120);
                        rig.Pose(jump,time);
                        float flexion=Mathf.Min(rig.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,
                            rig.animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y)-rig.animator.GetBoneTransform(HumanBodyBones.Hips).position.y;
                        if(flexion<=bestFlexion)continue;
                        bestFlexion=flexion;fold=Pose(handler);report.foldSourceTime=time;
                    }
                    Func<HumanPose,HumanPose> preserveNativeHipHeight=pose=>
                    {
                        handler.SetHumanPose(ref pose);
                        // Recompute COM from the actual target skeleton with its native
                        // pelvis transform. Fixing only COM.y is insufficient: Humanoid
                        // can rotate the pelvis when the arms redistribute body mass.
                        // No Actor root write, mesh-minimum-driven offset or body scaling.
                        hips.SetLocalPositionAndRotation(baselineHipLocalPosition,baselineHipLocalRotation);
                        pose=Pose(handler);
                        pose.bodyPosition.x=0;pose.bodyPosition.z=0;
                        return pose;
                    };
                    var dodge = Bake("A_Review_Tiny_EmberEvade", tuning.DodgeDuration, t =>
                    {
                        float weight=Envelope(t,.085f,.31f,tuning.DodgeInvulnerabilitySeconds);
                        return preserveNativeHipHeight(Mix(baseline, guard, fold, weight, weight));
                    });
                    // Continuous cross-rig source playback has a measured 33-degree / 8ms
                    // shoulder/wrist jump on this Avatar. Preserve that failed candidate;
                    // author a separate fixed-domain key-pose gesture rather than hide it
                    // with runtime smoothing or change projectile release authority.
                    Func<float,HumanPose> sampleThrow=normalized=>
                    {
                        rig.Pose(throwing,Mathf.Min(throwing.length-.0001f,normalized*throwing.length));
                        var upper=Pose(handler);
                        for(int i=0;i<20;i++)upper.muscles[i]=baseline.muscles[i];
                        return Mix(baseline,upper,baseline,1,0);
                    };
                    var anticipation=sampleThrow(.30f);
                    var release=sampleThrow(.50f);
                    var followThrough=sampleThrow(.68f);
                    var knife = Bake("A_Review_Tiny_Knife", tuning.RangedDuration, t =>
                    {
                        HumanPose upper;
                        if(t<=.12f)upper=Blend(baseline,anticipation,Mathf.SmoothStep(0,1,t/.12f));
                        else if(t<=tuning.RangedReleaseTime)upper=Blend(anticipation,release,
                            Mathf.SmoothStep(0,1,(t-.12f)/(tuning.RangedReleaseTime-.12f)));
                        else if(t<=.36f)upper=Blend(release,followThrough,
                            Mathf.SmoothStep(0,1,(t-tuning.RangedReleaseTime)/(.36f-tuning.RangedReleaseTime)));
                        else upper=Blend(followThrough,baseline,Mathf.SmoothStep(0,1,(t-.36f)/(tuning.RangedDuration-.36f)));
                        return preserveNativeHipHeight(upper);
                    });
                    EnsureFolder(assets);
                    AssetDatabase.CreateAsset(dodge,assets+"/"+dodge.name+".anim");
                    AssetDatabase.CreateAsset(knife,assets+"/"+knife.name+".anim");
                    // Save ONLY owned new assets, never SaveAssets all dirty project objects.
                    AssetDatabase.SaveAssetIfDirty(dodge); AssetDatabase.SaveAssetIfDirty(knife);
                    TinyHeroBakeoff.SetupLight();
                    TinyHeroBakeoff.Cube("PoseGround",new Vector3(0,-.07f,0),new Vector3(8,.1f,8),new Color(.24f,.33f,.34f));
                    var camera=new GameObject("TinyPoseCamera").AddComponent<Camera>();
                    camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.3f,.43f,.46f);
                    camera.orthographic=true;camera.orthographicSize=1.12f;
                    TinyHeroBakeoff.View(camera,new Vector3(2.7f,1.5f,-3.5f),new Vector3(0,.78f,0));
                    foreach(var root in scene.GetRootGameObjects()) foreach(var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=31;
                    camera.cullingMask=1<<31;
                    Capture(rig, camera, dodge, dir, rows);
                    Capture(rig, camera, knife, dir, rows);
                    // The only mapped fingers are thumb/index; bake a real seven-bone grip,
                    // not a null-filled Ranger pose or an empty array that passes a count check.
                    rig.Pose(knife, tuning.RangedReleaseTime);
                    var grip = ScriptableObject.CreateInstance<KnifeGripPose>();
                    var bones = new[] {HumanBodyBones.RightHand,HumanBodyBones.RightThumbProximal,
                        HumanBodyBones.RightThumbIntermediate,HumanBodyBones.RightThumbDistal,
                        HumanBodyBones.RightIndexProximal,HumanBodyBones.RightIndexIntermediate,HumanBodyBones.RightIndexDistal};
                    grip.joints=bones.Select(b=>new KnifeGripPose.Joint{bone=b,rotation=rig.animator.GetBoneTransform(b).localRotation}).ToArray();
                    var hand=rig.animator.GetBoneTransform(HumanBodyBones.RightHand);
                    // Thumb-to-index separation points ACROSS the hand, not down the
                    // blade: the previous preview visibly embedded the knife in the arm.
                    // Derive the hilt and blade direction from this rig's actual authored
                    // sword geometry. No inherited bone-scale change or Ranger socket copy.
                    var sword=rig.go.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="OHS03Polyart");
                    var swordBounds=sword.GetComponent<MeshFilter>().sharedMesh.bounds;
                    int longAxis=swordBounds.size.x>swordBounds.size.y?0:1;
                    if(swordBounds.size.z>swordBounds.size[longAxis])longAxis=2;
                    Vector3 localAxis=Vector3.zero;localAxis[longAxis]=Mathf.Sign(swordBounds.center[longAxis]);
                    if(localAxis[longAxis]==0)localAxis[longAxis]=1;
                    Vector3 center=sword.transform.TransformPoint(swordBounds.center-localAxis*swordBounds.size[longAxis]*.40f);
                    grip.gripLocalPosition=hand.InverseTransformPoint(center);
                    // A sword slash's release blade points toward this large head, so it
                    // is not a safe knife orientation. Author forward blade orientation
                    // AT the knife release pose, then keep that fixed hand-local socket.
                    // This is visual aiming, never an authority/launch-origin change.
                    grip.gripLocalRotation=Quaternion.Inverse(hand.rotation)*
                        Quaternion.LookRotation(rig.animator.transform.forward,rig.animator.transform.up);
                    grip.avatarPath=AvatarPath;grip.sourcePath=AssetDatabase.GetAssetPath(knife);
                    grip.sourceHash=AssetDatabase.GetAssetDependencyHash(grip.sourcePath).ToString();grip.sourceTime=tuning.RangedReleaseTime;
                    AssetDatabase.CreateAsset(grip,assets+"/Grip_Review_Tiny.asset");AssetDatabase.SaveAssetIfDirty(grip);
                    CaptureGrip(rig,camera,knife,grip,dir);
                }
                report.samples=rows.ToArray();
                report.frozenFilesUnchanged=frozen.All(p=>Hash(p.Key)==p.Value);
                File.WriteAllText(dir+"/pose-candidate.json",JsonUtility.ToJson(report,true));
                if(!report.frozenFilesUnchanged) throw new InvalidOperationException("Protected production/source bytes changed; retained candidate is not accepted.");
                return dir;
            }
            finally { EditorSceneManager.CloseScene(scene,true);EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        static Preflight CheckIkAndRoundTrip(TinyHeroBakeoff.Rig rig, HumanPoseHandler handler, AnimationClip clip)
        {
            var result=new Preflight{clip=AssetDatabase.GetAssetPath(clip),ikOffMinY=float.PositiveInfinity,ikOnMinY=float.PositiveInfinity,
                footTargetBindings=AnimationUtility.GetCurveBindings(clip).Count(b=>b.propertyName.StartsWith("LeftFootT.")||b.propertyName.StartsWith("RightFootT."))};
            int steps=Mathf.Max(60,Mathf.CeilToInt(clip.length*SampleRate));
            for(int i=0;i<=steps;i++)
            {
                float time=Mathf.Min(clip.length-.0001f,i*clip.length/steps);
                rig.Pose(clip,time,false);
                result.ikOffMinY=Mathf.Min(result.ikOffMinY,TinyHeroBakeoff.VertexBounds(rig.go).min.y);
                Vector3 left=rig.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                Vector3 right=rig.animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
                var transforms=Enumerable.Range(0,(int)HumanBodyBones.LastBone).Select(b=>rig.animator.GetBoneTransform((HumanBodyBones)b)).Where(b=>b!=null).ToArray();
                var positions=transforms.Select(t=>t.position).ToArray();
                HumanPose pose=Pose(handler);handler.SetHumanPose(ref pose);
                result.maxPoseRoundTripBoneDelta=Mathf.Max(result.maxPoseRoundTripBoneDelta,transforms.Select((t,b)=>Vector3.Distance(positions[b],t.position)).Max());
                rig.Pose(clip,time,true);
                result.ikOnMinY=Mathf.Min(result.ikOnMinY,TinyHeroBakeoff.VertexBounds(rig.go).min.y);
                result.maxFootIkBoneDelta=Mathf.Max(result.maxFootIkBoneDelta,
                    Vector3.Distance(left,rig.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position),
                    Vector3.Distance(right,rig.animator.GetBoneTransform(HumanBodyBones.RightFoot).position));
            }
            return result;
        }

        static HumanPose Pose(HumanPoseHandler handler)
        { var pose=new HumanPose();handler.GetHumanPose(ref pose);pose.muscles=(float[])pose.muscles.Clone();return pose; }

        static HumanPose Mix(HumanPose baseline, HumanPose upper, HumanPose legs, float upperWeight, float legWeight)
        {
            var result=new HumanPose{bodyPosition=new Vector3(0,baseline.bodyPosition.y,0),bodyRotation=baseline.bodyRotation,muscles=new float[HumanTrait.MuscleCount]};
            for(int i=0;i<result.muscles.Length;i++)
                result.muscles[i]=Mathf.Lerp(baseline.muscles[i], i>=20&&i<=35 ? legs.muscles[i] : upper.muscles[i], i>=20&&i<=35 ? legWeight : upperWeight);
            return result;
        }

        static HumanPose Blend(HumanPose from,HumanPose to,float weight)
        {
            var result=new HumanPose{bodyPosition=Vector3.Lerp(from.bodyPosition,to.bodyPosition,weight),
                bodyRotation=Quaternion.Slerp(from.bodyRotation,to.bodyRotation,weight),muscles=new float[HumanTrait.MuscleCount]};
            for(int i=0;i<result.muscles.Length;i++)result.muscles[i]=Mathf.Lerp(from.muscles[i],to.muscles[i],weight);
            return result;
        }

        static void CaptureGrip(TinyHeroBakeoff.Rig rig,Camera camera,AnimationClip knife,KnifeGripPose grip,string dir)
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Player_ThrowingKnife.prefab").transform.Find("Model");
            var held=new GameObject("ReviewHeldKnife_Metres");held.layer=31;
            var model=Object.Instantiate(source.gameObject,held.transform);
            foreach(var t in model.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
            if(model.GetComponentInChildren<Collider>(true)!=null||model.GetComponentInChildren<MonoBehaviour>(true)!=null)
                throw new InvalidOperationException("Preview knife must be render-only.");
            var swords=rig.go.GetComponentsInChildren<Renderer>(true).Where(r=>r.name=="OHS03Polyart").ToArray();
            if(swords.Length!=1)throw new InvalidOperationException("Expected one Tiny sword; no broad renderer hiding.");
            var hand=rig.animator.GetBoneTransform(HumanBodyBones.RightHand);
            var savedPosition=camera.transform.position;var savedRotation=camera.transform.rotation;float savedSize=camera.orthographicSize;
            try
            {
                swords[0].enabled=false;
                foreach(float time in new[]{.06f,.12f,.20f,grip.sourceTime})
                {
                    rig.Pose(knife,time);
                    float weight=Mathf.Clamp01(time/.06f);
                    foreach(var joint in grip.joints)
                    {
                        var bone=rig.animator.GetBoneTransform(joint.bone);
                        bone.localRotation=Quaternion.Slerp(bone.localRotation,joint.rotation,weight);
                    }
                    var rotation=hand.rotation*grip.gripLocalRotation;
                    held.transform.SetPositionAndRotation(hand.TransformPoint(grip.gripLocalPosition)+rotation*Vector3.forward*PlayerKnifePresentation.GripToCenter,rotation);
                    camera.orthographicSize=.28f;
                    TinyHeroBakeoff.View(camera,hand.position+new Vector3(.65f,.24f,.5f),hand.position);
                    var image=TinyNativeMotionReview.RenderEvaluatedPose(camera,rig.go);
                    File.WriteAllBytes(dir+"/grip-"+Mathf.RoundToInt(time*1000).ToString("000")+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
                    camera.orthographicSize=savedSize;camera.transform.SetPositionAndRotation(savedPosition,savedRotation);
                    image=TinyNativeMotionReview.RenderEvaluatedPose(camera,rig.go);
                    File.WriteAllBytes(dir+"/held-knife-"+Mathf.RoundToInt(time*1000).ToString("000")+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
                }
            }
            finally
            {
                swords[0].enabled=true;camera.orthographicSize=savedSize;camera.transform.SetPositionAndRotation(savedPosition,savedRotation);
                Object.DestroyImmediate(held);
            }
        }

        static AnimationClip Bake(string name,float duration,Func<float,HumanPose> evaluate)
        {
            int steps=Mathf.Max(60,Mathf.CeilToInt(duration*SampleRate));
            var keys=new List<Keyframe>[HumanTrait.MuscleCount+7];
            for(int j=0;j<keys.Length;j++) keys[j]=new List<Keyframe>();
            for(int i=0;i<=steps;i++)
            {
                float t=i*duration/steps; HumanPose pose=evaluate(t);
                var values=pose.muscles.Concat(new[]{pose.bodyPosition.x,pose.bodyPosition.y,pose.bodyPosition.z,
                    pose.bodyRotation.x,pose.bodyRotation.y,pose.bodyRotation.z,pose.bodyRotation.w}).ToArray();
                for(int j=0;j<keys.Length;j++) keys[j].Add(new Keyframe(t,values[j]));
            }
            var clip=new AnimationClip{name=name,frameRate=SampleRate};
            for(int j=0;j<keys.Length;j++)
            {
                string property=j<HumanTrait.MuscleCount ? MuscleProperty(HumanTrait.MuscleName[j]) : new[]{"RootT.x","RootT.y","RootT.z","RootQ.x","RootQ.y","RootQ.z","RootQ.w"}[j-HumanTrait.MuscleCount];
                var curve=new AnimationCurve(keys[j].ToArray());
                for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Animator),property),curve);
            }
            var settings=AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime=false;settings.loopBlendPositionY=true;settings.loopBlendPositionXZ=true;settings.loopBlendOrientation=true;
            settings.keepOriginalPositionY=true;settings.keepOriginalPositionXZ=true;settings.keepOriginalOrientation=true;
            AnimationUtility.SetAnimationClipSettings(clip,settings);
            return clip;
        }

        static void Capture(TinyHeroBakeoff.Rig rig,Camera camera,AnimationClip clip,string dir,List<Row> rows)
        {
            int steps=Mathf.Max(60,Mathf.CeilToInt(clip.length*SampleRate));
            Vector3 previousHand=Vector3.zero;Quaternion[] previous=null;
            var joints=new[]{HumanBodyBones.RightShoulder,HumanBodyBones.RightUpperArm,HumanBodyBones.RightLowerArm,HumanBodyBones.RightHand};
            for(int i=0;i<=steps;i++)
            {
                float t=Mathf.Min(clip.length-.0001f,i*clip.length/steps);rig.Pose(clip,t);
                var rotations=joints.Select(b=>rig.animator.GetBoneTransform(b).rotation).ToArray();
                var hand=rig.animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                var hip=rig.animator.GetBoneTransform(HumanBodyBones.Hips).position;
                rows.Add(new Row{action=clip.name,time=t,minY=TinyHeroBakeoff.VertexBounds(rig.go).min.y,
                    headMinY=TinyHeroBakeoff.VertexBounds(rig.animator.GetBoneTransform(HumanBodyBones.Head).gameObject).min.y,
                    hipX=hip.x,hipZ=hip.z,hipY=hip.y,hipYaw=rig.animator.GetBoneTransform(HumanBodyBones.Hips).eulerAngles.y,hand=hand,
                    handStep=i==0?0:Vector3.Distance(hand,previousHand),jointAngleStep=i==0?0:rotations.Select((q,b)=>Quaternion.Angle(q,previous[b])).Max(),
                    leftFoot=rig.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position,rightFoot=rig.animator.GetBoneTransform(HumanBodyBones.RightFoot).position});
                previousHand=hand;previous=rotations;
                if(i==steps||i%Mathf.Max(1,steps/6)==0)
                {var image=TinyNativeMotionReview.RenderEvaluatedPose(camera,rig.go);File.WriteAllBytes(dir+"/"+clip.name+"-"+i.ToString("000")+".png",image.EncodeToPNG());Object.DestroyImmediate(image);}
            }
        }
        static AnimationClip Clip(string path)=>AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview"));
        static string Hash(string path)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
        static void EnsureFolder(string path)
        {if(AssetDatabase.IsValidFolder(path))return;EnsureFolder(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
    }
}
