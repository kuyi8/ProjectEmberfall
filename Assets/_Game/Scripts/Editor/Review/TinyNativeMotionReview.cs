using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Emberfall.Editor.Review
{
    /// <summary>Same-rig controlled preview; no production rig, collider, movement or clips modified.</summary>
    public static class TinyNativeMotionReview
    {
        const string Source="Assets/RPG Tiny Hero Duo/Animation/SwordAndShield/";
        [Serializable] sealed class Pose
        {
            public string action,clip;
            public float normalized,minY,headY,headMinY,hipX,hipZ;
            public Vector3 leftFoot,rightFoot,virtualDodgeRoot;
        }
        [Serializable] sealed class Report
        {
            public string scope="Original-size Polyart controlled poses. CPU-baked skins rendered synchronously; virtual dodge displacement is diagnostic ONLY, NOT physical contact/natural gameplay/locomotion proof.";
            public Pose[] poses;
        }
        public static string Capture()
            => Capture(new[]{"Idle_Battle_SwordAndShiled.fbx","InPlace/MoveFWD_Battle_InPlace_SwordAndShield.fbx",
                "Attack01_SwordAndShiled.fbx","Attack02_SwordAndShiled.fbx","Attack03_SwordAndShiled.fbx",
                "InPlace/JumpFull_Spin_InPlace_SwordAndShield.fbx"});

        public static string CaptureEvasionAndKnife()
            => Capture(new[]{"Idle_Battle_SwordAndShiled.fbx","InPlace/SprintFWD_Battle_InPlace_SwordAndShield.fbx",
                "Defend_SwordAndShield.fbx","InPlace/JumpFull_Normal_InPlace_SwordAndShield.fbx",
                "Assets/_Game/Art/Animations/Player/Derived/A_Player_RangedAttack_Dedicated.anim"});

        static string Capture(string[] paths)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle graphics Editor required.");
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
                if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve unsaved scenes.");
            var setup=EditorSceneManager.GetSceneManagerSetup();
            string dir=Path.GetFullPath("Builds/ArtReview/tiny-native/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(dir);
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            var rows=new List<Pose>();
            var tuning=AssetDatabase.LoadAssetAtPath<CombatTuningAsset>("Assets/_Game/Settings/CombatTuning_M1.asset").CreateRuntimeCopy();
            try
            {
                TinyHeroBakeoff.SetupLight();
                TinyHeroBakeoff.Cube("PreviewGround",new Vector3(0,-.07f,0),new Vector3(10,.1f,10),new Color(.26f,.36f,.37f));
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Review/TinyHero/P_Review_TinyHero_Polyart.prefab");
                if(prefab==null) throw new InvalidOperationException("Previously reviewed project-owned original-size Polyart candidate missing.");
                var go=UnityEngine.Object.Instantiate(prefab); go.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                var camera=new GameObject("NativeMotionCamera").AddComponent<Camera>();
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.28f,.43f,.46f);
                camera.orthographic=true; camera.orthographicSize=1.1f; camera.nearClipPlane=.03f;
                foreach(var root in scene.GetRootGameObjects())
                    foreach(var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer=31;
                camera.cullingMask=1<<31;
                TinyHeroBakeoff.View(camera,new Vector3(2.7f,1.5f,-3.5f),new Vector3(0,.85f,0));
                using(var rig=new TinyHeroBakeoff.Rig(go))
                {
                    foreach(string path in paths)
                    {
                        var clip=AssetDatabase.LoadAllAssetsAtPath(path.StartsWith("Assets/",StringComparison.Ordinal) ? path : Source+path)
                            .OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview"));
                        int steps=Mathf.Max(60,Mathf.CeilToInt(clip.length*60));
                        for(int i=0;i<=steps;i++)
                        {
                            float normalized=i/(float)steps; rig.Pose(clip,Mathf.Min(clip.length-.0001f,normalized*clip.length));
                            Bounds bounds=TinyHeroBakeoff.VertexBounds(go);
                            var hip=rig.animator.GetBoneTransform(HumanBodyBones.Hips).position-go.transform.position;
                            rows.Add(new Pose { action=Path.GetFileNameWithoutExtension(path),clip=AssetDatabase.GetAssetPath(clip),normalized=normalized,
                                minY=bounds.min.y-go.transform.position.y,headY=rig.animator.GetBoneTransform(HumanBodyBones.Head).position.y-go.transform.position.y,
                                headMinY=TinyHeroBakeoff.VertexBounds(rig.animator.GetBoneTransform(HumanBodyBones.Head).gameObject).min.y-go.transform.position.y,
                                hipX=hip.x,hipZ=hip.z,
                                leftFoot=rig.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position-go.transform.position,
                                rightFoot=rig.animator.GetBoneTransform(HumanBodyBones.RightFoot).position-go.transform.position,
                                // A mathematical reference ONLY: does not move or simulate the production Motor.
                                virtualDodgeRoot=Vector3.forward*tuning.DodgeDistance*DodgeTravelProfile.Evaluate(normalized) });
                            if(i==steps || i%Mathf.Max(1,steps/5)==0)
                            {
                                var image=RenderEvaluatedPose(camera,go);
                                File.WriteAllBytes(Path.Combine(dir,Path.GetFileNameWithoutExtension(path)+"-"+i.ToString("00")+".png"),image.EncodeToPNG());
                                UnityEngine.Object.DestroyImmediate(image);
                            }
                        }
                    }
                }
                File.WriteAllText(dir+"/native-poses.json",JsonUtility.ToJson(new Report{poses=rows.ToArray()},true));
                return dir;
            }
            finally
            {
                EditorSceneManager.CloseScene(scene,true);
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
        }

        internal static Texture2D RenderEvaluatedPose(Camera camera,GameObject go)
        {
            // Execute-code loops do not advance Unity's GPU skinning PlayerLoop. Bake the SAME
            // evaluated bones for the snapshot rather than render a stale skinned-body buffer.
            var skins=go.GetComponentsInChildren<SkinnedMeshRenderer>().Where(s=>s.enabled).ToArray();
            var snapshots=new List<GameObject>();
            var meshes=new List<Mesh>();
            try
            {
                foreach(var skin in skins)
                {
                    var mesh=new Mesh(); skin.BakeMesh(mesh,true); meshes.Add(mesh);
                    var snapshot=new GameObject("__EvaluatedPoseSkin",typeof(MeshFilter),typeof(MeshRenderer));
                    snapshots.Add(snapshot); snapshot.layer=skin.gameObject.layer;
                    snapshot.transform.SetParent(skin.transform,false);
                    snapshot.GetComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=snapshot.GetComponent<MeshRenderer>(); renderer.sharedMaterials=skin.sharedMaterials;
                    renderer.shadowCastingMode=skin.shadowCastingMode; renderer.receiveShadows=skin.receiveShadows;
                    skin.enabled=false;
                }
                return TinyHeroBakeoff.Render(camera,800,600);
            }
            finally
            {
                foreach(var snapshot in snapshots) UnityEngine.Object.DestroyImmediate(snapshot);
                foreach(var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
                foreach(var skin in skins) skin.enabled=true;
            }
        }
    }
}
