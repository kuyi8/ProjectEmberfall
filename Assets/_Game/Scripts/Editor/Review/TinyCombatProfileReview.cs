using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Offline-only candidate assets. Never redirects a production scene, prefab or network profile.</summary>
    public static class TinyCombatProfileReview
    {
        public const string Root="Assets/_Game/Art/Review/TinyHero/CombatCandidates";
        const string Native="Assets/RPG Tiny Hero Duo/Animation/SwordAndShield/";
        const string Pose="Assets/_Game/Art/Review/TinyHero/PoseCandidates/20261002-024935-197/";
        // Declared before authoring; sampled evidence is not continuous-time or natural-contact proof.
        public const float HipsLimit=.4f,SeamPositionLimit=.00001f,SeamAngleLimit=.01f;
        const int Rate=120;
        [Serializable] public sealed class StateRow
        {
            public string state,source;
            public int samples;
            public float duration,domainDuration,playbackSpeed,minY,headMinY,hipsPeak,hipsFinal,rootPlanarMax,rootExtractionAngleMax;
            public bool hipsPass;
        }
        [Serializable] sealed class SeamRow { public string state;public float positionMax,angleMax;public bool pass; }
        [Serializable] sealed class Report
        {
            public string assets;
            public string scope="120Hz target-rig CPU poses and derivatives ONLY. Not actual-input, natural contact, grounded locomotion, human, production or network acceptance. Heal is a labelled native guard placeholder, not accepted drinking art. Knife uses the separately-reviewed candidate and retains its independent gate.";
            public bool protectedBytesSame;
            public StateRow[] states;
            public SeamRow[] seams;
        }

        public static string BuildAndCapture()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle graphics Editor required.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Preserve unsaved scenes.");
            string id=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"),assets=Root+"/"+id;
            string dir="Builds/ArtReview/tiny-combat/"+id;Directory.CreateDirectory(dir);EnsureFolder(assets);
            var frozen=new[]{"Assets/RPG Tiny Hero Duo","Assets/_Game/Settings","Assets/_Game/Scenes","Assets/_Game/Prefabs/Characters",
                "Assets/_Game/Resources/Networking","Assets/_Game/Art/Animations/Player"}
                .SelectMany(p=>Directory.GetFiles(p,"*",SearchOption.AllDirectories)).ToDictionary(p=>p,Hash);
            File.WriteAllLines(dir+"/frozen-before.txt",frozen.Select(p=>p.Value+" "+p.Key));
            var previous=EditorSceneManager.GetSceneManagerSetup();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            var rows=new List<StateRow>();var seams=new List<SeamRow>();
            var tuning=AssetDatabase.LoadAssetAtPath<CombatTuningAsset>("Assets/_Game/Settings/CombatTuning_M1.asset").CreateRuntimeCopy();
            try
            {
                var idle=Derive(Clip("Idle_Battle_SwordAndShiled.fbx"),"Idle",assets,0,1,true);
                var walk=Derive(Clip("InPlace/MoveFWD_Battle_InPlace_SwordAndShield.fbx"),"Walk",assets,0,1,true);
                var sprint=Derive(Clip("InPlace/SprintFWD_Battle_InPlace_SwordAndShield.fbx"),"Sprint",assets,0,1,true);
                var light=new AnimationClip[3];var recovery=new AnimationClip[2];
                for(int i=0;i<3;i++)
                {
                    var source=Clip("Attack0"+(i+1)+"_SwordAndShiled.fbx");
                    light[i]=Derive(source,"LightAttack"+(i+1),assets,0,i<2?.65f:1,false);
                    if(i<2)recovery[i]=Derive(source,"LightAttack"+(i+1)+"Recovery",assets,.65f,1,false);
                }
                var heavy=Derive(Clip("Attack03_SwordAndShiled.fbx"),"HeavyAttack",assets,0,1,false);
                var sweep=Derive(Clip("Attack04_SwordAndShiled.fbx"),"Sweep",assets,0,1,false);
                var guard=Derive(Clip("Defend_SwordAndShield.fbx"),"Guard",assets,0,1,true);
                var broken=Derive(Clip("Dizzy_SwordAndShield.fbx"),"GuardBreak",assets,0,1,false);
                var hurt=Derive(Clip("GetHit01_SwordAndShield.fbx"),"HitReact",assets,0,1,false);
                var dead=Derive(Clip("Die01_SwordAndShield.fbx"),"Dead",assets,0,1,false);
                var dodge=AssetDatabase.LoadAssetAtPath<AnimationClip>(Pose+"A_Review_Tiny_EmberEvade.anim");
                var knife=AssetDatabase.LoadAssetAtPath<AnimationClip>(Pose+"A_Review_Tiny_Knife.anim");
                if(dodge==null||knife==null)throw new InvalidOperationException("Separate evasion/knife candidates missing.");
                var controller=AnimatorController.CreateAnimatorControllerAtPath(assets+"/AC_Review_TinyCombat.controller");
                controller.AddParameter("Speed",AnimatorControllerParameterType.Float);
                var machine=controller.layers[0].stateMachine;
                var tree=new BlendTree{name="TinyNativeLocomotion",blendType=BlendTreeType.Simple1D,blendParameter="Speed",useAutomaticThresholds=false};
                AssetDatabase.AddObjectToAsset(tree,controller);tree.AddChild(idle,0);tree.AddChild(walk,2.2f);tree.AddChild(sprint,5.4f);
                machine.defaultState=Add(machine,"Locomotion",tree);
                for(int i=0;i<3;i++)Add(machine,"LightAttack"+(i+1),light[i]);
                for(int i=0;i<2;i++)Add(machine,"LightAttack"+(i+1)+"Recovery",recovery[i]);
                var motions=new Dictionary<string,AnimationClip>{{"HeavyCharge",guard},{"HeavyAttack",heavy},{"Sweep",sweep},{"RangedAttack",knife},
                    {"Dodge",dodge},{"Guard",guard},{"GuardBreak",broken},{"HitReact",hurt},{"Heal",guard},{"Execution",heavy},{"Dead",dead}};
                foreach(var pair in motions)Add(machine,pair.Key,pair.Value);
                var set=ScriptableObject.CreateInstance<PlayerAnimationSet>();
                set.Configure(controller,light[0],recovery[0],light[1],recovery[1],light[2],guard,heavy,sweep,knife,dodge,guard,broken,hurt,guard,dead,
                    null,null,null,null,null,null,null);
                AssetDatabase.CreateAsset(set,assets+"/PlayerAnimationSet_Review_Tiny.asset");
                EditorUtility.SetDirty(controller);AssetDatabase.SaveAssetIfDirty(controller);AssetDatabase.SaveAssetIfDirty(set);
                var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TinyPoseCandidateReview.AvatarPath));
                go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                var animator=go.GetComponentInChildren<Animator>();AnimatorSpeedCoordinator.For(animator);
                TinyHeroBakeoff.SetupLight();TinyHeroBakeoff.Cube("ReviewGround",new Vector3(0,-.07f,0),new Vector3(8,.1f,8),new Color(.24f,.33f,.34f));
                var camera=new GameObject("ReviewCamera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.3f,.43f,.46f);camera.orthographic=true;camera.orthographicSize=1.12f;
                TinyHeroBakeoff.View(camera,new Vector3(2.7f,1.5f,-3.5f),new Vector3(0,.78f,0));
                foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))t.gameObject.layer=31;
                camera.cullingMask=1<<31;
                using(var rig=new TinyHeroBakeoff.Rig(go))
                {
                    rig.Pose(idle,0);var baseHip=rig.animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    for(int i=0;i<3;i++)
                    {
                        rows.Add(Measure(rig,camera,light[i],tuning.GetLightComboOpen(i),baseHip,dir));
                        if(i<2)
                        {
                            rows.Add(Measure(rig,camera,recovery[i],tuning.GetLightDuration(i)-tuning.GetLightComboOpen(i),baseHip,dir));
                            seams.Add(Seam(rig,light[i],recovery[i]));
                        }
                    }
                    // Light3 does not have a separate recovery state: actual presenter uses full state duration.
                    rows.Single(r=>r.state==light[2].name).domainDuration=tuning.GetLightDuration(2);
                    rows.Single(r=>r.state==light[2].name).playbackSpeed=light[2].length/tuning.GetLightDuration(2);
                    foreach(var pair in motions)
                    {
                        float duration=pair.Key=="HeavyAttack"?tuning.HeavyDuration:pair.Key=="Sweep"?tuning.SweepDuration:
                            pair.Key=="Dodge"?tuning.DodgeDuration:pair.Key=="RangedAttack"?tuning.RangedDuration:
                            pair.Key=="GuardBreak"?tuning.GuardBreakDuration:pair.Key=="HitReact"?tuning.HitReactDuration:
                            pair.Key=="Heal"?tuning.HealDuration:pair.Key=="Execution"?tuning.ExecutionDuration:0;
                        var row=Measure(rig,camera,pair.Value,duration,baseHip,dir);row.state=pair.Key;rows.Add(row);
                    }
                }
                bool unchanged=frozen.All(p=>Hash(p.Key)==p.Value);
                File.WriteAllText(dir+"/candidate.json",JsonUtility.ToJson(new Report{assets=assets,protectedBytesSame=unchanged,states=rows.ToArray(),seams=seams.ToArray()},true));
                if(!unchanged)throw new InvalidOperationException("Protected source changed; candidate retained, not accepted.");
                if(seams.Any(s=>!s.pass))throw new InvalidOperationException("Predeclared light recovery seam failed; evidence retained.");
                return dir;
            }
            finally{EditorSceneManager.CloseScene(scene,true);EditorSceneManager.RestoreSceneManagerSetup(previous);}
        }

        static AnimationClip Derive(AnimationClip source,string name,string assets,float from,float to,bool loop)
        {
            float start=source.length*from,end=source.length*to,duration=end-start;
            var clip=new AnimationClip{name="A_Review_Tiny_"+name,frameRate=Rate};
            foreach(var binding in AnimationUtility.GetCurveBindings(source))
            {
                var original=AnimationUtility.GetEditorCurve(source,binding);if(original==null)continue;
                int count=Mathf.CeilToInt(duration*Rate);var keys=new Keyframe[count+1];
                for(int i=0;i<=count;i++)
                {
                    float t=duration*i/count;
                    float value=binding.propertyName=="RootT.x"||binding.propertyName=="RootT.z"?0:original.Evaluate(start+t);
                    // Humanoid RootQ also carries body lean/turn, not gameplay-root authority.
                    // Preserve native body orientation, especially the fall in Dead; the
                    // existing presenter anchors the actual Animator transform separately.
                    keys[i]=new Keyframe(t,value);
                }
                var curve=new AnimationCurve(keys);
                for(int i=0;i<=count;i++){AnimationUtility.SetKeyLeftTangentMode(curve,i,AnimationUtility.TangentMode.Linear);AnimationUtility.SetKeyRightTangentMode(curve,i,AnimationUtility.TangentMode.Linear);}
                AnimationUtility.SetEditorCurve(clip,binding,curve);
            }
            AnimationUtility.SetAnimationEvents(clip,Array.Empty<AnimationEvent>());
            var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlendPositionY=true;settings.loopBlendOrientation=true;settings.keepOriginalPositionY=true;
            settings.keepOriginalPositionXZ=true;settings.keepOriginalOrientation=true;AnimationUtility.SetAnimationClipSettings(clip,settings);
            AssetDatabase.CreateAsset(clip,assets+"/"+clip.name+".anim");AssetDatabase.SaveAssetIfDirty(clip);return clip;
        }

        static StateRow Measure(TinyHeroBakeoff.Rig rig,Camera camera,AnimationClip clip,float domain,Vector3 baseline,string dir)
        {
            var row=new StateRow{state=clip.name,source=AssetDatabase.GetAssetPath(clip),duration=clip.length,domainDuration=domain,
                playbackSpeed=domain>0?clip.length/domain:1,minY=float.PositiveInfinity,headMinY=float.PositiveInfinity};
            int steps=Mathf.CeilToInt(clip.length*Rate);row.samples=steps+1;
            for(int i=0;i<=steps;i++)
            {
                float t=clip.length*i/steps;rig.Pose(clip,t);
                Vector3 hip=rig.animator.GetBoneTransform(HumanBodyBones.Hips).position;
                row.hipsFinal=Vector2.Distance(new Vector2(hip.x,hip.z),new Vector2(baseline.x,baseline.z));row.hipsPeak=Mathf.Max(row.hipsPeak,row.hipsFinal);
                row.minY=Mathf.Min(row.minY,TinyHeroBakeoff.VertexBounds(rig.go).min.y);
                row.headMinY=Mathf.Min(row.headMinY,TinyHeroBakeoff.VertexBounds(rig.animator.GetBoneTransform(HumanBodyBones.Head).gameObject).min.y);
                if(i==steps||i%Mathf.Max(1,steps/4)==0)
                {var image=TinyNativeMotionReview.RenderEvaluatedPose(camera,rig.go);File.WriteAllBytes(dir+"/"+clip.name+"-"+i.ToString("000")+".png",image.EncodeToPNG());Object.DestroyImmediate(image);}
            }
            row.hipsPass=row.hipsPeak<HipsLimit;
            foreach(var b in AnimationUtility.GetCurveBindings(clip))
            {
                var curve=AnimationUtility.GetEditorCurve(clip,b);
                if(b.propertyName=="RootT.x"||b.propertyName=="RootT.z")row.rootPlanarMax=Mathf.Max(row.rootPlanarMax,curve.keys.Max(k=>Mathf.Abs(k.value)));
            }
            var q=AnimationUtility.GetCurveBindings(clip).Where(b=>b.propertyName.StartsWith("RootQ.",StringComparison.Ordinal)).OrderBy(b=>b.propertyName).ToArray();
            if(q.Length==4)
            {
                Func<float,Quaternion> rotation=t=>new Quaternion(AnimationUtility.GetEditorCurve(clip,q[1]).Evaluate(t),AnimationUtility.GetEditorCurve(clip,q[2]).Evaluate(t),
                    AnimationUtility.GetEditorCurve(clip,q[3]).Evaluate(t),AnimationUtility.GetEditorCurve(clip,q[0]).Evaluate(t));
                // Alphabetical RootQ.w,x,y,z. Store total extraction angle, not only yaw.
                for(int i=0;i<=steps;i++)row.rootExtractionAngleMax=Mathf.Max(row.rootExtractionAngleMax,Quaternion.Angle(rotation(0),rotation(clip.length*i/steps)));
            }
            return row;
        }

        static SeamRow Seam(TinyHeroBakeoff.Rig rig,AnimationClip first,AnimationClip second)
        {
            var bones=Enumerable.Range(0,(int)HumanBodyBones.LastBone).Select(b=>rig.animator.GetBoneTransform((HumanBodyBones)b)).Where(b=>b!=null).ToArray();
            rig.Pose(first,first.length);var positions=bones.Select(b=>b.position).ToArray();var rotations=bones.Select(b=>b.rotation).ToArray();rig.Pose(second,0);
            var result=new SeamRow{state=first.name,positionMax=bones.Select((b,i)=>Vector3.Distance(b.position,positions[i])).Max(),angleMax=bones.Select((b,i)=>Quaternion.Angle(b.rotation,rotations[i])).Max()};
            result.pass=result.positionMax<SeamPositionLimit&&result.angleMax<SeamAngleLimit;return result;
        }
        static AnimatorState Add(AnimatorStateMachine machine,string name,Motion motion){var s=machine.AddState(name);s.motion=motion;s.writeDefaultValues=false;return s;}
        static AnimationClip Clip(string path)=>AssetDatabase.LoadAllAssetsAtPath(Native+path).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview"));
        static void EnsureFolder(string path){if(AssetDatabase.IsValidFolder(path))return;string parent=path.Substring(0,path.LastIndexOf('/'));EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
        static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","");}
    }
}
