using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Actual drawn-vertex attribution before local pose authoring, not bone-name diagnosis.</summary>
    public static class TinyDeathPoseRefinement
    {
        const string Profile="Assets/_Game/Art/Review/TinyHero/CombatCandidates/20261002-053304-723/";
        [Serializable] public sealed class MeshPoint
        {
            public string renderer,kind,influence;
            public int vertex;
            public Vector3 world;
            public float groundY,clearance;
        }
        [Serializable] sealed class Row
        {
            public float time;
            public MeshPoint[] minima;
        }
        [Serializable] sealed class Report
        {
            public string scope="120Hz CPU-evaluated target-rig death, actual drawn triangle vertices vs a real flat collider top -0.02. No whole-body lift, rescale, production clip/profile, Actor/Motor or save changes. Attribution includes rigid hair and skin weights; this is not natural runtime, sloped/stair or continuous-time acceptance.";
            public string source;
            public bool frozenSame;
            public Row[] rows;
        }

        [Serializable] sealed class RefinedReport
        {
            public string scope="Bounded target-rig head/neck and two generic cloak-bone pose derivative. Native per-frame pelvis local position/rotation preserved, COM recomputed and bounded RootT-only replay calibration uses native pelvis error, NEVER ground clearance. Position gates are world metres; raw local errors are retained. No Actor/root lift, rescale, production profile or authority changes.120Hz flat-collider samples and CPU images are not continuous-time, slope/stair or runtime acceptance.";
            public string source,candidate;
            public float neckOffset,headOffset,cloakDegrees,hipsPositionError,hipsAngleError;
            public float hipsLocalPositionError,rawHipsPositionError,rawHipsLocalPositionError,maxComCorrection;
            public int cloakAxis,samples;
            public bool frozenSame,unrelatedGenericCurvesSame,allDrawnVerticesAboveFlatGround,calibrationNonRootTCurvesSame,calibrationValid;
            public string pelvisCalibration;
            public CalibrationRound[] calibrationRounds;
            public Row[] rows;
        }

        [Serializable] sealed class CalibrationSample
        {
            public float time,errorBeforeMetres,normalizedDeterminant;
            public Vector3 correctionRootT;
        }
        [Serializable] sealed class CalibrationRound
        {
            public int round;
            public float maxErrorBeforeMetres,maxErrorBeforeLocal;
            public string outcome;
            public CalibrationSample[] samples;
        }

        public static float RefineWeight(float time,float duration)
        {
            if(duration<=0)throw new ArgumentOutOfRangeException(nameof(duration));
            return Mathf.SmoothStep(0,1,Mathf.InverseLerp(duration*.25f,duration*.8f,time));
        }

        public static string RefineAndCapture()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle graphics Editor required.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Preserve dirty scenes.");
            string id=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"),dir="Builds/ArtReview/tiny-death/"+id;
            string assets="Assets/_Game/Art/Review/TinyHero/DeathCandidates/"+id;Directory.CreateDirectory(dir);EnsureFolder(assets);
            var source=AssetDatabase.LoadAssetAtPath<AnimationClip>(Profile+"A_Review_Tiny_Dead.anim");
            if(source==null)throw new InvalidOperationException("Known native death candidate absent.");
            var frozen=new[]{"Assets/RPG Tiny Hero Duo","Assets/_Game/Settings","Assets/_Game/Scenes","Assets/_Game/Prefabs/Characters","Assets/_Game/Resources/Networking","Assets/_Game/Art/Animations/Player",Profile.TrimEnd('/')}
                .SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).ToDictionary(p=>p,Hash);
            File.WriteAllLines(dir+"/frozen-before.txt",frozen.Select(p=>p.Value+" "+p.Key));
            var setup=EditorSceneManager.GetSceneManagerSetup();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            Material floorMaterial=null;
            // Rig caches its Playables. Keep unique probe clips alive until that
            // graph is disposed, and never read a modified clip through a stale Playable.
            var replayProbes=new List<AnimationClip>();
            try
            {
                var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TinyPoseCandidateReview.AvatarPath));
                go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);TinyHeroBakeoff.SetupLight();
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="DeathRefinement_ActualFlatGround";
                floor.transform.position=new Vector3(0,-.07f,0);floor.transform.localScale=new Vector3(8,.1f,8);
                floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));floorMaterial.color=new Color(.3f,.4f,.4f);
                floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;var ground=floor.GetComponent<Collider>();Physics.SyncTransforms();
                var camera=new GameObject("DeathRefinementCamera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.3f,.43f,.46f);camera.orthographic=true;camera.orthographicSize=1.12f;
                TinyHeroBakeoff.View(camera,new Vector3(2.7f,1.5f,-3.5f),new Vector3(0,.65f,0));
                foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                camera.cullingMask=1<<31;
                var report=new RefinedReport{source=AssetDatabase.GetAssetPath(source)};
                using(var rig=new TinyHeroBakeoff.Rig(go))
                using(var handler=new HumanPoseHandler(rig.animator.avatar,rig.animator.transform))
                {
                    var hips=rig.animator.GetBoneTransform(HumanBodyBones.Hips);
                    int neck=Array.IndexOf(HumanTrait.MuscleName,"Neck Nod Down-Up"),head=Array.IndexOf(HumanTrait.MuscleName,"Head Nod Down-Up");
                    if(neck<0||head<0)throw new InvalidOperationException("Target humanoid nod channels absent.");
                    var cloak=new[]{"CloakBone02","CloakBone03"}.Select(n=>rig.go.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n)).ToArray();
                    var cloakRest=cloak.Select(t=>t.localRotation).ToArray();
                    Action<float> nativePose=time=>
                    {
                        // Unanimated generic bones must not retain the last manual
                        // probe's delta when switching back to the native clip.
                        for(int c=0;c<cloak.Length;c++)cloak[c].localRotation=cloakRest[c];
                        rig.Pose(source,time);
                    };
                    Action<float,float,float,int,float> evaluate=(time,neckOffset,headOffset,axis,degrees)=>
                    {
                        nativePose(time);
                        Vector3 p=hips.localPosition;Quaternion q=hips.localRotation;
                        var pose=HumanPose(handler);float w=RefineWeight(time,source.length);
                        pose.muscles[neck]=Mathf.Lerp(pose.muscles[neck],Mathf.Clamp(pose.muscles[neck]+neckOffset,-1,1),w);
                        pose.muscles[head]=Mathf.Lerp(pose.muscles[head],Mathf.Clamp(pose.muscles[head]+headOffset,-1,1),w);
                        handler.SetHumanPose(ref pose);
                        // Preserve this native FALL pelvis, not an Idle pelvis or a floor-derived offset.
                        hips.SetLocalPositionAndRotation(p,q);
                        Vector3 direction=axis==0?Vector3.right:axis==1?Vector3.up:Vector3.forward;
                        foreach(var bone in cloak)bone.localRotation=bone.localRotation*Quaternion.AngleAxis(degrees*w,direction);
                    };
                    float[] probe={source.length*.72f,source.length*.82f,source.length};
                    float bestHead=float.NegativeInfinity,bestCost=float.PositiveInfinity;
                    foreach(float n in new[]{-.45f,-.3f,-.15f,0,.15f,.3f,.45f})
                    foreach(float h in new[]{-.45f,-.3f,-.15f,0,.15f,.3f,.45f})
                    {
                        float minimum=float.PositiveInfinity;
                        foreach(float time in probe)
                        {
                            evaluate(time,n,h,0,0);
                            minimum=Mathf.Min(minimum,TinyHeroBakeoff.VertexBounds(rig.animator.GetBoneTransform(HumanBodyBones.Head).gameObject).min.y-ground.bounds.max.y);
                        }
                        float cost=Mathf.Abs(n)+Mathf.Abs(h);
                        // Prefer smallest adjustment once the measured head meshes have margin.
                        float score=Mathf.Min(minimum,.008f);
                        if(score<bestHead-.00001f||(Mathf.Abs(score-bestHead)<.00001f&&cost>=bestCost))continue;
                        bestHead=score;bestCost=cost;report.neckOffset=n;report.headOffset=h;
                    }
                    float bestCloak=float.NegativeInfinity;
                    for(int axis=0;axis<3;axis++)foreach(float degrees in new[]{-45f,-30f,-15f,0,15f,30f,45f})
                    {
                        float minimum=float.PositiveInfinity;
                        foreach(float time in probe)
                        {
                            evaluate(time,report.neckOffset,report.headOffset,axis,degrees);
                            minimum=Mathf.Min(minimum,MeshMinima(rig.go,ground,"Cloak02").Single().clearance);
                        }
                        if(minimum<=bestCloak)continue;bestCloak=minimum;report.cloakAxis=axis;report.cloakDegrees=degrees;
                    }
                    int steps=Mathf.CeilToInt(source.length*120);report.samples=steps+1;
                    var values=new List<Keyframe>[HumanTrait.MuscleCount+7+cloak.Length*4];
                    for(int j=0;j<values.Length;j++)values[j]=new List<Keyframe>();
                    Quaternion previousBody=Quaternion.identity;var previousCloak=new Quaternion[cloak.Length];
                    var nativeHips=new Vector3[steps+1];var nativeRotation=new Quaternion[steps+1];
                    for(int i=0;i<=steps;i++)
                    {
                        float time=source.length*i/steps;nativePose(time);nativeHips[i]=hips.localPosition;nativeRotation[i]=hips.localRotation;
                        evaluate(time,report.neckOffset,report.headOffset,report.cloakAxis,report.cloakDegrees);
                        var pose=HumanPose(handler);
                        if(i>0&&Quaternion.Dot(previousBody,pose.bodyRotation)<0)pose.bodyRotation=Negate(pose.bodyRotation);
                        previousBody=pose.bodyRotation;
                        var sample=pose.muscles.Concat(new[]{pose.bodyPosition.x,pose.bodyPosition.y,pose.bodyPosition.z,
                            pose.bodyRotation.x,pose.bodyRotation.y,pose.bodyRotation.z,pose.bodyRotation.w}).ToList();
                        for(int c=0;c<cloak.Length;c++)
                        {
                            Quaternion q=cloak[c].localRotation;if(i>0&&Quaternion.Dot(previousCloak[c],q)<0)q=Negate(q);previousCloak[c]=q;
                            sample.AddRange(new[]{q.x,q.y,q.z,q.w});
                        }
                        for(int j=0;j<sample.Count;j++)values[j].Add(new Keyframe(time,sample[j]));
                    }
                    var candidate=Object.Instantiate(source);candidate.name="A_Review_Tiny_DeadLocalized";candidate.frameRate=120;
                    string[] cloakPaths=cloak.Select(t=>AnimationUtility.CalculateTransformPath(t,rig.animator.transform)).ToArray();
                    foreach(var binding in AnimationUtility.GetCurveBindings(candidate).Where(b=>b.type==typeof(Transform)&&cloakPaths.Contains(b.path)&&b.propertyName.Contains("Euler")))
                        AnimationUtility.SetEditorCurve(candidate,binding,null);
                    for(int j=0;j<values.Length;j++)
                    {
                        int generic=j-HumanTrait.MuscleCount-7;
                        string property=j<HumanTrait.MuscleCount?TinyPoseCandidateReview.MuscleProperty(HumanTrait.MuscleName[j]):
                            generic<0?new[]{"RootT.x","RootT.y","RootT.z","RootQ.x","RootQ.y","RootQ.z","RootQ.w"}[j-HumanTrait.MuscleCount]:
                            "m_LocalRotation."+new[]{"x","y","z","w"}[generic%4];
                        var binding=generic<0?EditorCurveBinding.FloatCurve("",typeof(Animator),property):EditorCurveBinding.FloatCurve(cloakPaths[generic/4],typeof(Transform),property);
                        var curve=new AnimationCurve(values[j].ToArray());
                        for(int k=0;k<curve.length;k++){AnimationUtility.SetKeyLeftTangentMode(curve,k,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,k,AnimationUtility.TangentMode.Linear);}
                        AnimationUtility.SetEditorCurve(candidate,binding,curve);
                    }
                    AnimationUtility.SetAnimationEvents(candidate,Array.Empty<AnimationEvent>());
                    CalibratePelvisReplay(rig,hips,candidate,nativeHips,steps,report,replayProbes);
                    report.unrelatedGenericCurvesSame=AnimationUtility.GetCurveBindings(source).Where(b=>b.type!=typeof(Animator)&&!cloakPaths.Contains(b.path))
                        .All(b=>CurveSignature(AnimationUtility.GetEditorCurve(source,b))==CurveSignature(AnimationUtility.GetEditorCurve(candidate,b)));
                    string path=assets+"/"+candidate.name+".anim";AssetDatabase.CreateAsset(candidate,path);AssetDatabase.SaveAssetIfDirty(candidate);report.candidate=path;
                    var rows=new List<Row>();
                    for(int i=0;i<=steps;i++)
                    {
                        float time=source.length*i/steps;rig.Pose(candidate,time);
                        Vector3 hipsError=hips.localPosition-nativeHips[i];
                        report.hipsLocalPositionError=Mathf.Max(report.hipsLocalPositionError,hipsError.magnitude);
                        report.hipsPositionError=Mathf.Max(report.hipsPositionError,hips.parent.TransformVector(hipsError).magnitude);
                        report.hipsAngleError=Mathf.Max(report.hipsAngleError,Quaternion.Angle(hips.localRotation,nativeRotation[i]));
                        rows.Add(new Row{time=time,minima=MeshMinima(rig.go,ground)});
                        if(i==0||i==steps*3/4||i==steps)
                        {
                            var image=TinyNativeMotionReview.RenderEvaluatedPose(camera,rig.go);File.WriteAllBytes(dir+"/refined-"+i+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
                            nativePose(time);image=TinyNativeMotionReview.RenderEvaluatedPose(camera,rig.go);File.WriteAllBytes(dir+"/native-"+i+".png",image.EncodeToPNG());Object.DestroyImmediate(image);
                        }
                    }
                    report.rows=rows.ToArray();report.allDrawnVerticesAboveFlatGround=rows.All(r=>r.minima.All(m=>m.clearance>=0));
                }
                report.frozenSame=frozen.All(p=>Hash(p.Key)==p.Value);
                File.WriteAllText(dir+"/refinement.json",JsonUtility.ToJson(report,true));
                if(!report.frozenSame||!report.unrelatedGenericCurvesSame||!report.calibrationNonRootTCurvesSame||!report.calibrationValid||
                    !Finite(report.hipsPositionError)||!Finite(report.hipsAngleError)||report.hipsPositionError>.001f||report.hipsAngleError>.1f)
                    throw new InvalidOperationException("Candidate pelvis/source-preservation gate failed; retain this isolated version: "+dir);
                return dir;
            }
            finally
            {
                foreach(var probe in replayProbes)if(probe!=null)Object.DestroyImmediate(probe);
                if(floorMaterial!=null)Object.DestroyImmediate(floorMaterial);
                EditorSceneManager.CloseScene(scene,true);EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        // Unity's HumanPose COM-to-clip conversion is not an exact inverse on
        // every avatar. Correct ONLY the baked COM translation against actual
        // target-clip playback, not a floor/Idle pose or an assumed humanScale.
        static void CalibratePelvisReplay(TinyHeroBakeoff.Rig rig,Transform hips,AnimationClip candidate,
            Vector3[] nativeHips,int steps,RefinedReport report,List<AnimationClip> probes)
        {
            const float epsilon=.005f,maxStep=.02f,maxTotal=.04f;
            var bindings=new[]{"RootT.x","RootT.y","RootT.z"}
                .Select(p=>EditorCurveBinding.FloatCurve("",typeof(Animator),p)).ToArray();
            var rootCurves=bindings.Select(b=>AnimationUtility.GetEditorCurve(candidate,b)).ToArray();
            if(rootCurves.Any(c=>c==null||c.length!=steps+1))
                throw new InvalidOperationException("Pelvis calibration requires all native-time RootT keys.");
            var originalKeys=rootCurves.Select(c=>c.keys).ToArray();
            var untouched=NonRootTCurveSignatures(candidate);
            var rounds=new List<CalibrationRound>();
            report.calibrationValid=true;
            report.pelvisCalibration="Three-round bounded RootT-only native-pelvis compensation; not yet evaluated.";

            for(int round=0;round<3;round++)
            {
                var baseline=ReplaySnapshot(candidate,probes);
                var positions=new Vector3[steps+1];var parentMatrices=new Matrix4x4[steps+1];
                var errors=new Vector3[steps+1];
                var record=new CalibrationRound{round=round+1};
                for(int i=0;i<=steps;i++)
                {
                    rig.Pose(baseline,candidate.length*i/steps);
                    positions[i]=hips.localPosition;parentMatrices[i]=hips.parent.localToWorldMatrix;
                    Vector3 localError=nativeHips[i]-positions[i];
                    errors[i]=parentMatrices[i].MultiplyVector(localError);
                    if(!Finite(localError.x)||!Finite(localError.y)||!Finite(localError.z)||
                        !Finite(errors[i].x)||!Finite(errors[i].y)||!Finite(errors[i].z))
                        throw new InvalidOperationException("Non-finite native pelvis replay error at sample "+i);
                    record.maxErrorBeforeLocal=Mathf.Max(record.maxErrorBeforeLocal,localError.magnitude);
                    record.maxErrorBeforeMetres=Mathf.Max(record.maxErrorBeforeMetres,errors[i].magnitude);
                }
                if(round==0)
                {
                    report.rawHipsPositionError=record.maxErrorBeforeMetres;
                    report.rawHipsLocalPositionError=record.maxErrorBeforeLocal;
                }
                rounds.Add(record);
                if(record.maxErrorBeforeMetres<=.0001f)
                {
                    record.outcome="Native pelvis reproduced within the stricter 0.1mm calibration target; no further correction.";
                    report.pelvisCalibration=record.outcome;break;
                }

                // Central difference in three normalized RootT coordinates, at
                // every sampled time. Each probe starts from the same baseline.
                var columns=new Vector3[3][];
                for(int axis=0;axis<3;axis++)
                {
                    var plus=ReplaySnapshot(candidate,probes);var minus=ReplaySnapshot(candidate,probes);
                    OffsetRootCurve(plus,bindings[axis],epsilon);
                    OffsetRootCurve(minus,bindings[axis],-epsilon);
                    columns[axis]=new Vector3[steps+1];
                    for(int i=0;i<=steps;i++)
                    {
                        float time=candidate.length*i/steps;
                        rig.Pose(plus,time);Vector3 a=hips.localPosition;
                        rig.Pose(minus,time);Vector3 b=hips.localPosition;
                        columns[axis][i]=parentMatrices[i].MultiplyVector(a-b)/(2*epsilon);
                    }
                }
                var corrections=new Vector3[steps+1];var samples=new CalibrationSample[steps+1];
                string failure=null;
                for(int i=0;i<=steps;i++)
                {
                    Vector3 x=columns[0][i],y=columns[1][i],z=columns[2][i];
                    float determinant=Vector3.Dot(x,Vector3.Cross(y,z));
                    float scale=x.magnitude*y.magnitude*z.magnitude;
                    float normalized=scale>0?Mathf.Abs(determinant)/scale:0;
                    var sample=new CalibrationSample{time=candidate.length*i/steps,errorBeforeMetres=errors[i].magnitude,normalizedDeterminant=normalized};
                    samples[i]=sample;
                    if(!Finite(normalized)||normalized<.02f||Mathf.Min(x.magnitude,Mathf.Min(y.magnitude,z.magnitude))<.01f)
                    {failure="Measured RootT Jacobian is non-finite or rank-deficient at sample "+i;break;}
                    Vector3 e=errors[i];
                    var correction=new Vector3(Vector3.Dot(e,Vector3.Cross(y,z)),
                        Vector3.Dot(x,Vector3.Cross(e,z)),Vector3.Dot(x,Vector3.Cross(y,e)))/determinant;
                    Vector3 accumulated=new Vector3(
                        rootCurves[0].keys[i].value-originalKeys[0][i].value,
                        rootCurves[1].keys[i].value-originalKeys[1][i].value,
                        rootCurves[2].keys[i].value-originalKeys[2][i].value)+correction;
                    if(!Finite(correction.x)||!Finite(correction.y)||!Finite(correction.z)||correction.magnitude>maxStep||accumulated.magnitude>maxTotal)
                    {failure="Required native-pelvis COM correction exceeded the declared normalized bounds at sample "+i;break;}
                    sample.correctionRootT=correction;corrections[i]=correction;
                }
                record.samples=samples;
                if(failure!=null)
                {
                    // Apply none of a rejected round; retain prior raw evidence
                    // and leave the final unchanged pelvis gate to reject it.
                    record.outcome=failure+"; this round was not applied.";
                    report.calibrationValid=false;
                    report.pelvisCalibration=record.outcome;break;
                }
                for(int axis=0;axis<3;axis++)
                {
                    var keys=rootCurves[axis].keys;
                    for(int i=0;i<keys.Length;i++)keys[i].value+=corrections[i][axis];
                    rootCurves[axis]=LinearCurve(keys);
                    AnimationUtility.SetEditorCurve(candidate,bindings[axis],rootCurves[axis]);
                }
                for(int i=0;i<=steps;i++)
                {
                    var total=new Vector3(rootCurves[0].keys[i].value-originalKeys[0][i].value,
                        rootCurves[1].keys[i].value-originalKeys[1][i].value,
                        rootCurves[2].keys[i].value-originalKeys[2][i].value);
                    report.maxComCorrection=Mathf.Max(report.maxComCorrection,total.magnitude);
                }
                record.outcome="Applied measured native-pelvis-only COM compensation; final actual asset replay gate remains authoritative.";
                report.pelvisCalibration=record.outcome;
            }
            report.calibrationRounds=rounds.ToArray();
            var after=NonRootTCurveSignatures(candidate);
            report.calibrationNonRootTCurvesSame=untouched.Count==after.Count&&untouched.All(p=>after.ContainsKey(p.Key)&&after[p.Key]==p.Value);
        }

        static AnimationClip ReplaySnapshot(AnimationClip clip,List<AnimationClip> probes)
        {var copy=Object.Instantiate(clip);copy.name=clip.name+"_TransientReplay";copy.hideFlags=HideFlags.HideAndDontSave;probes.Add(copy);return copy;}
        static void OffsetRootCurve(AnimationClip clip,EditorCurveBinding binding,float offset)
        {
            var curve=AnimationUtility.GetEditorCurve(clip,binding);var keys=curve.keys;
            for(int i=0;i<keys.Length;i++)keys[i].value+=offset;
            AnimationUtility.SetEditorCurve(clip,binding,LinearCurve(keys));
        }
        static AnimationCurve LinearCurve(Keyframe[] keys)
        {
            var curve=new AnimationCurve(keys);
            for(int i=0;i<curve.length;i++)
            {AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
            return curve;
        }
        static bool Finite(float f)=>!float.IsNaN(f)&&!float.IsInfinity(f);
        static Dictionary<string,string> NonRootTCurveSignatures(AnimationClip clip)
        {
            return AnimationUtility.GetCurveBindings(clip)
                .Where(b=>!(b.type==typeof(Animator)&&b.path==""&&(b.propertyName=="RootT.x"||b.propertyName=="RootT.y"||b.propertyName=="RootT.z")))
                .ToDictionary(b=>b.path+"|"+b.type.FullName+"|"+b.propertyName,b=>CurveSignature(AnimationUtility.GetEditorCurve(clip,b)));
        }

        static HumanPose HumanPose(HumanPoseHandler handler)
        {var pose=new HumanPose();handler.GetHumanPose(ref pose);pose.muscles=(float[])pose.muscles.Clone();return pose;}
        static Quaternion Negate(Quaternion q)=>new Quaternion(-q.x,-q.y,-q.z,-q.w);
        static string CurveSignature(AnimationCurve curve)
        {return curve==null?"missing":curve.preWrapMode+"|"+curve.postWrapMode+"|"+string.Join(";",curve.keys.Select(k=>k.time.ToString("R")+":"+k.value.ToString("R")+":"+k.inTangent.ToString("R")+":"+k.outTangent.ToString("R")+":"+k.inWeight.ToString("R")+":"+k.outWeight.ToString("R")+":"+k.weightedMode));}
        static void EnsureFolder(string path)
        {if(AssetDatabase.IsValidFolder(path))return;string parent=path.Substring(0,path.LastIndexOf('/'));EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}

        public static string DiagnoseAndCapture()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle graphics Editor required.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Preserve dirty scenes.");
            string dir="Builds/ArtReview/tiny-death/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");Directory.CreateDirectory(dir);
            var frozen=new[]{"Assets/RPG Tiny Hero Duo","Assets/_Game/Settings","Assets/_Game/Scenes","Assets/_Game/Prefabs/Characters","Assets/_Game/Resources/Networking","Assets/_Game/Art/Animations/Player"}
                .SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).ToDictionary(p=>p,Hash);
            var setup=EditorSceneManager.GetSceneManagerSetup();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            var rows=new List<Row>();
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Profile+"A_Review_Tiny_Dead.anim");
            if(clip==null)throw new InvalidOperationException("Known native candidate absent.");
            try
            {
                var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TinyPoseCandidateReview.AvatarPath));
                go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                TinyHeroBakeoff.SetupLight();
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="DeathReview_ActualFlatGround";
                floor.transform.position=new Vector3(0,-.07f,0);floor.transform.localScale=new Vector3(8,.1f,8);
                var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.color=new Color(.3f,.4f,.4f);floor.GetComponent<Renderer>().sharedMaterial=mat;
                var ground=floor.GetComponent<Collider>();Physics.SyncTransforms();
                var camera=new GameObject("DeathAttributionCamera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.3f,.43f,.46f);camera.orthographic=true;camera.orthographicSize=1.12f;
                TinyHeroBakeoff.View(camera,new Vector3(2.7f,1.5f,-3.5f),new Vector3(0,.65f,0));
                foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                camera.cullingMask=1<<31;
                using(var rig=new TinyHeroBakeoff.Rig(go))
                {
                    int steps=Mathf.CeilToInt(clip.length*120);
                    for(int i=0;i<=steps;i++)
                    {
                        float time=clip.length*i/steps;rig.Pose(clip,time);
                        rows.Add(new Row{time=time,minima=MeshMinima(rig.go,ground)});
                        if(i==steps||i==steps*3/4)
                        {var image=TinyNativeMotionReview.RenderEvaluatedPose(camera,rig.go);File.WriteAllBytes(dir+"/native-"+i+".png",image.EncodeToPNG());Object.DestroyImmediate(image);}
                    }
                }
                bool same=frozen.All(p=>Hash(p.Key)==p.Value);
                File.WriteAllText(dir+"/attribution.json",JsonUtility.ToJson(new Report{source=AssetDatabase.GetAssetPath(clip),frozenSame=same,rows=rows.ToArray()},true));
                Object.DestroyImmediate(mat);if(!same)throw new InvalidOperationException("Protected bytes changed; report retained.");return dir;
            }
            finally{EditorSceneManager.CloseScene(scene,true);EditorSceneManager.RestoreSceneManagerSetup(setup);}
        }

        internal static MeshPoint[] MeshMinima(GameObject root,Collider ground,string onlyRenderer=null)
        {
            var result=new List<MeshPoint>();
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(false).Where(r=>r.enabled&&!r.forceRenderingOff&&(r is MeshRenderer||r is SkinnedMeshRenderer)))
            {
                if(IsEquipment(renderer.transform,root.transform))continue;
                if(onlyRenderer!=null&&renderer.name!=onlyRenderer)continue;
                var skin=renderer as SkinnedMeshRenderer;
                Mesh mesh=skin!=null?new Mesh():renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if(mesh==null)continue;
                try
                {
                    if(skin!=null)skin.BakeMesh(mesh,true);
                    // Editor snapshots can read the actual source even when runtime vertex
                    // access is stripped; never change the importer to make this readable.
                    var vertices=mesh.vertices;var indices=mesh.triangles.Distinct();
                    var lowest=new MeshPoint{renderer=PathOf(renderer.transform,root.transform),kind=skin!=null?"CPU-skinned drawn vertex":"rigid drawn vertex",clearance=float.PositiveInfinity};
                    foreach(int index in indices)
                    {
                        Vector3 p=renderer.transform.TransformPoint(vertices[index]);RaycastHit hit;
                        if(!ground.Raycast(new Ray(new Vector3(p.x,4,p.z),Vector3.down),out hit,8))throw new InvalidOperationException("Ground coverage missing for "+renderer.name);
                        float clearance=p.y-hit.point.y;
                        if(clearance>=lowest.clearance)continue;
                        lowest.vertex=index;lowest.world=p;lowest.groundY=hit.point.y;lowest.clearance=clearance;
                    }
                    if(skin==null)lowest.influence="rigid parent "+PathOf(renderer.transform.parent,root.transform);
                    else
                    {
                        var weights=skin.sharedMesh.boneWeights;
                        if(lowest.vertex<weights.Length)
                        {
                            var w=weights[lowest.vertex];int[] bone={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] amount={w.weight0,w.weight1,w.weight2,w.weight3};
                            lowest.influence=string.Join(";",Enumerable.Range(0,4).Where(j=>amount[j]>0).Select(j=>skin.bones[bone[j]].name+":"+amount[j]));
                        }
                    }
                    result.Add(lowest);
                }
                finally{if(skin!=null)Object.DestroyImmediate(mesh);}
            }
            return result.OrderBy(p=>p.clearance).ToArray();
        }
        static bool IsEquipment(Transform t,Transform root)
        {while(t!=null&&t!=root){string n=t.name.ToLowerInvariant();if(new[]{"sword","shield","ohs","staff","weapon","knife","trail","telegraph"}.Any(n.Contains))return true;t=t.parent;}return false;}
        static string PathOf(Transform t,Transform root)
        {var names=new List<string>();while(t!=null&&t!=root){names.Add(t.name);t=t.parent;}names.Reverse();return string.Join("/",names);}
        static string Hash(string path)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    }
}
