#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Movement;
using Emberfall.Gameplay.Targeting;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Input-system events and actual PlayerLoop on a disposable rig. Not a human/contact/production gate.</summary>
    public sealed class TinyPoseInputCandidateTests
    {
        const string Candidate="Assets/_Game/Art/Review/TinyHero/PoseCandidates/20261002-024935-197/";
        const string Native="Assets/RPG Tiny Hero Duo/Animation/SwordAndShield/";
        readonly List<Object> _owned=new List<Object>();
        readonly List<Row> _rows=new List<Row>();
        InputSettings _originalSettings;
        Keyboard _keyboard;
        Mouse _mouse;
        string _profileEvidence;
        PlayerCombatActor _actor;
        Animator _animator;
        AnimatorSpeedCoordinator _speed;
        PlayerThrowingKnifeLauncher _launcher;
        Camera _camera;
        Vector3 _animatorAnchor;
        Quaternion _animatorRotation;
        string _output;
        int _rate,_vsync;
        bool _previousDodgePolicy;

        [Serializable] sealed class Row
        {
            public int frame;
            public string state,grade;
            public float time,delta,normalized,speed,minY,headMinY;
            public Vector3 root,hips,hand;
            public bool held,flight;
        }
        [Serializable] sealed class Trace
        {
            public string candidate=Candidate;
            public string scope="Disposable metre-scale physics fixture: InputSystem -> unchanged Actor/Motor/Presenter -> actual Animator/knife. CPU skin vertices plus conservative Renderer bounds for unreadable rigid meshes; original import flags unchanged. Scripted incoming damage, NOT natural enemy contact, human feel, formal Profile or network acceptance.";
            public Row[] rows;
        }

        [UnitySetUp]
        public IEnumerator Setup()
        {
            Assert.That(M2RouteFlowController.EditorTestSavePath,Does.Contain("IsolatedSaves"));
            foreach(var flow in Object.FindObjectsOfType<M2RouteFlowController>())
                Assert.That(flow.SavePath,Does.Contain("IsolatedSaves"));
            Assert.That(SystemInfo.graphicsDeviceType,Is.Not.EqualTo(GraphicsDeviceType.Null));
            _previousDodgePolicy=PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving;
            _profileEvidence=Candidate;
            PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving=true;
            _output=Path.GetFullPath("Builds/ArtReview/tiny-input/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(_output);
            _originalSettings=InputSystem.settings;
            var settings=Object.Instantiate(_originalSettings);_owned.Add(settings);
            settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings=settings;
            _rate=UnityEngine.Application.targetFrameRate;_vsync=QualitySettings.vSyncCount;
            UnityEngine.Application.targetFrameRate=120;QualitySettings.vSyncCount=0;
            _keyboard=InputSystem.AddDevice<Keyboard>("TinyCandidateKeyboard");

            var root=new GameObject("Review_TinyInputActor");_owned.Add(root);root.SetActive(false);
            root.transform.position=new Vector3(500,.94f,500);root.layer=30;
            var controller=root.AddComponent<CharacterController>();controller.height=1.8f;controller.radius=.32f;controller.center=Vector3.zero;
            var input=root.AddComponent<PlayerInputReader>();
            input.Configure(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Game/Settings/PlayerControls.asset"));
            var visual=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Review/TinyHero/P_Review_TinyHero_Polyart.prefab"),root.transform);
            visual.transform.localPosition=new Vector3(0,-.9f,0);visual.transform.localRotation=Quaternion.identity;
            foreach(var t in visual.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            _animator=visual.GetComponentInChildren<Animator>();
            _animatorAnchor=_animator.transform.localPosition;_animatorRotation=_animator.transform.localRotation;
            _speed=_animator.gameObject.AddComponent<AnimatorSpeedCoordinator>();
            foreach(var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>())skin.updateWhenOffscreen=true;
            var sword=visual.GetComponentsInChildren<MeshRenderer>(true).Single(r=>r.name=="OHS03Polyart");
            sword.name="Sword_M6_Player_Equipped"; // Instance-only explicit launcher render lease.
            var origin=new GameObject("ReviewAttackOrigin").transform;origin.SetParent(root.transform,false);origin.localPosition=new Vector3(0,0,.7f);
            var launch=new GameObject("ReviewLaunchOrigin").transform;launch.SetParent(root.transform,false);launch.localPosition=new Vector3(.35f,.25f,.55f);
            _actor=root.AddComponent<PlayerCombatActor>();
            _actor.Configure(input,AssetDatabase.LoadAssetAtPath<CombatTuningAsset>("Assets/_Game/Settings/CombatTuning_M1.asset"),origin,root.transform,controller,null);
            var cameraObject=new GameObject("ReviewGameplayDistanceCamera");_owned.Add(cameraObject);
            _camera=cameraObject.AddComponent<Camera>();_camera.enabled=false;_camera.cullingMask=1<<30;
            if(Object.FindObjectsOfType<AudioListener>().Length==0)cameraObject.AddComponent<AudioListener>();
            _camera.clearFlags=CameraClearFlags.SolidColor;_camera.backgroundColor=new Color(.2f,.33f,.38f);
            _camera.fieldOfView=55;_camera.nearClipPlane=.03f;
            _camera.transform.position=root.transform.position+new Vector3(2.3f,2.2f,-4.7f);
            _camera.transform.LookAt(root.transform.position+Vector3.up*.1f);
            var targeting=root.AddComponent<LockOnTargeting>();targeting.Configure(input,_camera.transform);
            var motor=root.AddComponent<ThirdPersonMotor>();motor.Configure(input,_actor,targeting,_camera.transform,controller);
            // A fixed forward input frame for reproducible wall alignment, not camera control changes.
            var inputFrame=new GameObject("ReviewInputFrame");_owned.Add(inputFrame);motor.Configure(input,_actor,targeting,inputFrame.transform,controller);
            var set=Object.Instantiate(AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>("Assets/_Game/Settings/PlayerAnimationSet_M1.asset"));_owned.Add(set);
            var overrides=new AnimatorOverrideController(set.Controller);_owned.Add(overrides);
            var pairs=new List<KeyValuePair<AnimationClip,AnimationClip>>();overrides.GetOverrides(pairs);
            var dodge=AssetDatabase.LoadAssetAtPath<AnimationClip>(Candidate+"A_Review_Tiny_EmberEvade.anim");
            var knife=AssetDatabase.LoadAssetAtPath<AnimationClip>(Candidate+"A_Review_Tiny_Knife.anim");
            Assert.That(dodge,Is.Not.Null);Assert.That(knife,Is.Not.Null);
            for(int i=0;i<pairs.Count;i++)
            {
                var original=pairs[i].Key;AnimationClip replacement=original;
                if(original==set.GetOfflineClip(CombatState.Dodge))replacement=dodge;
                else if(original==set.GetOfflineClip(CombatState.RangedAttack)||original==set.GetClip(CombatState.RangedAttack))replacement=knife;
                else if(original.name=="Rig|Idle_Loop")replacement=Clip("Idle_Battle_SwordAndShiled.fbx");
                else if(original.name=="Rig|Walk_Loop")replacement=Clip("InPlace/MoveFWD_Battle_InPlace_SwordAndShield.fbx");
                else if(original.name=="Rig|Sprint_Loop")replacement=Clip("InPlace/SprintFWD_Battle_InPlace_SwordAndShield.fbx");
                pairs[i]=new KeyValuePair<AnimationClip,AnimationClip>(original,replacement);
            }
            overrides.ApplyOverrides(pairs);
            var serialized=new SerializedObject(set);serialized.FindProperty("_controller").objectReferenceValue=overrides;
            serialized.FindProperty("_dodge").objectReferenceValue=dodge;serialized.FindProperty("_offlineRangedAttack").objectReferenceValue=knife;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<PlayerAnimationPresenter>().Configure(_animator,_actor,motor,set);
            root.AddComponent<PerfectDefenseFeedbackPresenter>().Configure(_actor,_animator);
            _launcher=root.AddComponent<PlayerThrowingKnifeLauncher>();
            _launcher.Configure(_actor,targeting,launch,AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Player_ThrowingKnife.prefab"));
            _launcher.ConfigurePresentation(AssetDatabase.LoadAssetAtPath<KnifeGripPose>(Candidate+"Grip_Review_Tiny.asset"));
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);_owned.Add(floor);floor.name="ReviewVisibleGround";floor.layer=30;
            floor.transform.position=new Vector3(500,-.1f,500);floor.transform.localScale=new Vector3(30,.2f,30);
            var sun=new GameObject("ReviewLight");_owned.Add(sun);var light=sun.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.2f;light.cullingMask=1<<30;
            sun.transform.rotation=Quaternion.Euler(35,-25,0);
            root.SetActive(true);_animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            for(int i=0;i<20;i++)yield return null;
            Assert.That(root.GetComponent<PlayerAnimationPresenter>().IsConfigured,Is.True);
            Assert.That(root.GetComponent<ThirdPersonMotor>().enabled,Is.True);
            Assert.That(_launcher.GetComponent<PlayerKnifePresentation>().IsConfigured,Is.True);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if(_output!=null)File.WriteAllText(_output+"/actual-input.json",JsonUtility.ToJson(new Trace{candidate=_profileEvidence,rows=_rows.ToArray()},true));
            if(_keyboard!=null&&_keyboard.added)InputSystem.RemoveDevice(_keyboard);
            if(_mouse!=null&&_mouse.added)InputSystem.RemoveDevice(_mouse);
            if(_originalSettings!=null)InputSystem.settings=_originalSettings;
            UnityEngine.Application.targetFrameRate=_rate;QualitySettings.vSyncCount=_vsync;
            PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving=_previousDodgePolicy;
            foreach(var item in _owned)if(item!=null)Object.Destroy(item);
            _owned.Clear();_rows.Clear();yield return null;
        }

        [UnityTest]
        public IEnumerator SpaceInput_StopsAtVisibleWall_AndLeavesNoExtraRootTravel()
        {
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);_owned.Add(wall);wall.name="ReviewVisibleWall";wall.layer=30;
            wall.transform.position=new Vector3(500,1,502);wall.transform.localScale=new Vector3(3,2,.3f);Physics.SyncTransforms();
            Vector3 initial=_actor.transform.position;
            yield return Press(Key.Space);
            Assert.That(_actor.Model.State,Is.EqualTo(CombatState.Dodge));
            Assert.That(_speed.BaseSpeed,Is.EqualTo(1).Within(.001f));
            while(_actor.Model.State==CombatState.Dodge)
            {
                yield return new WaitForEndOfFrame();Observe();
                Assert.That(_actor.transform.position.z,Is.LessThan(501.85f));
                // The prefab's visual wrapper is -.9; its nested Animator is locally0.
                // Validate the actual captured anchor, not an assumed hierarchy shape.
                Assert.That(_animator.transform.localPosition,Is.EqualTo(_animatorAnchor));
                Assert.That(_animator.transform.localRotation,Is.EqualTo(_animatorRotation));
                Assert.That(_animator.applyRootMotion,Is.False);
                if(_rows.Count==15)Capture("dodge-wall.png");
                yield return null;
            }
            float stopped=_actor.transform.position.z;
            yield return new WaitForSeconds(.2f);Observe();Capture("dodge-recovered.png");
            Assert.That(stopped-initial.z,Is.GreaterThan(.5f));
            Assert.That(_actor.transform.position.z,Is.EqualTo(stopped).Within(.01f));
        }

        [UnityTest]
        public IEnumerator LegacyPerfectDodgeFeedback_FreezesNewAnimator_ButRecoveryStillTakesDamage()
        {
            PlayerFreezePresentationPolicy.KeepDodgeAnimationMoving=false;
            yield return Press(Key.Space);
            Assert.That(_actor.Model.State,Is.EqualTo(CombatState.Dodge));
            Assert.That(_actor.Model.StateElapsed,Is.LessThan(.15f));
            float health=_actor.Model.Health.Current;
            var result=_actor.ReceiveDamage(new DamageRequest(991,1,20,5,AttackTag.Light));
            Assert.That(result.PerfectDodge,Is.True);Assert.That(_speed.ActiveGrade,Is.EqualTo(HitFeedbackGrade.PerfectDefense));
            Assert.That(_animator.speed,Is.Zero);Vector3 freezeStart=_actor.transform.position;
            int frozen=0;
            while(_speed.ActiveGrade!=HitFeedbackGrade.None)
            {
                yield return new WaitForEndOfFrame();Observe();frozen++;yield return null;
            }
            Assert.That(frozen,Is.GreaterThan(0));
            Assert.That(_speed.LastEffectiveMilliseconds,Is.GreaterThanOrEqualTo(HitFeedbackRules.Duration(HitFeedbackGrade.PerfectDefense)*1000-.01));
            Debug.Log("[TINY_INPUT] perfect-defense-freeze rootTravel="+Vector3.Distance(freezeStart,_actor.transform.position));
            while(_actor.Model.State==CombatState.Dodge&&_actor.Model.StateElapsed<.44f)
            {yield return new WaitForEndOfFrame();Observe();yield return null;}
            Assert.That(_actor.Model.State,Is.EqualTo(CombatState.Dodge));
            Assert.That(_actor.Model.Health.Current,Is.EqualTo(health));
            result=_actor.ReceiveDamage(new DamageRequest(991,2,20,5,AttackTag.Hazard));
            Assert.That(result.Accepted,Is.True);Assert.That(_actor.Model.State,Is.EqualTo(CombatState.HitReact));
            yield return new WaitForEndOfFrame();Observe();Capture("recovery-hit.png");
        }

        [UnityTest]
        public IEnumerator PerfectDodge_KeepsOwnerMotion_WhileKnifeHitStillFreezesTargetAndPlaysSound()
        {
            yield return Press(Key.Space);
            Assert.That(_actor.Model.State,Is.EqualTo(CombatState.Dodge));
            float health=_actor.Model.Health.Current;
            var result=_actor.ReceiveDamage(new DamageRequest(991,1,20,5,AttackTag.Light));
            Assert.That(result.PerfectDodge,Is.True);
            Assert.That(_speed.ActiveGrade,Is.EqualTo(HitFeedbackGrade.None));
            Assert.That(_animator.speed,Is.GreaterThan(0));
            Assert.That(_actor.GetComponent<AudioSource>().isPlaying,Is.True,"Perfect-dodge confirmation remains audible.");

            var target=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Review/TinyHero/P_Review_TinyHero_Polyart.prefab"));
            _owned.Add(target);target.transform.position=new Vector3(520,0,520);
            var targetAnimator=target.GetComponentInChildren<Animator>();
            targetAnimator.runtimeAnimatorController=_animator.runtimeAnimatorController;
            var targetSpeed=AnimatorSpeedCoordinator.For(targetAnimator);
            var feedback=_actor.gameObject.AddComponent<CombatHitFeedbackPresenter>();
            feedback.Configure(_actor,_animator,AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>("Assets/_Game/Settings/CombatImpactAudio_M6.asset"),null);
            var sequence=typeof(AnimatorSpeedCoordinator).GetField("_sequence",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
            ulong before=(ulong)sequence.GetValue(_speed);
            feedback.Enqueue(new CombatImpactPresentationEvent(_actor.transform.position,CombatImpactStyle.Steel,
                77,1,5,HitFeedbackGrade.Light,ImpactSurface.Flesh,targetAnimator,AttackTag.Projectile));
            // Space starts an80ms crossfade: layer0's current state can still be
            // Locomotion. Compare the same Dodge destination, not two different states.
            var dodgeState=_animator.IsInTransition(0)?_animator.GetNextAnimatorStateInfo(0):_animator.GetCurrentAnimatorStateInfo(0);
            float normalized=dodgeState.normalizedTime;
            Vector3 start=_actor.transform.position;
            yield return null;yield return new WaitForEndOfFrame();Observe();
            Assert.That(_speed.ActiveGrade,Is.EqualTo(HitFeedbackGrade.None));
            Assert.That((ulong)sequence.GetValue(_speed),Is.EqualTo(before),"Suppressed owner request is never sent to the coordinator.");
            Assert.That(targetSpeed.ActiveGrade,Is.EqualTo(HitFeedbackGrade.Light));
            Assert.That(targetAnimator.speed,Is.Zero);
            Assert.That(feedback.LastAudioFrame,Is.EqualTo(feedback.LastFeedbackFrame));
            Assert.That(feedback.LastAudioFrame,Is.GreaterThanOrEqualTo(0));
            yield return new WaitForSeconds(.07f);yield return new WaitForEndOfFrame();Observe();
            var advanced=_animator.IsInTransition(0)?_animator.GetNextAnimatorStateInfo(0):_animator.GetCurrentAnimatorStateInfo(0);
            Assert.That(advanced.fullPathHash,Is.EqualTo(dodgeState.fullPathHash));
            Assert.That(advanced.normalizedTime,Is.GreaterThan(normalized));
            Assert.That(Vector3.Distance(start,_actor.transform.position),Is.GreaterThan(.1f));
            while(_actor.Model.State==CombatState.Dodge&&_actor.Model.StateElapsed<.44f)
            {yield return new WaitForEndOfFrame();Observe();yield return null;}
            Assert.That(_actor.Model.State,Is.EqualTo(CombatState.Dodge));
            Assert.That(_actor.Model.Health.Current,Is.EqualTo(health));
            result=_actor.ReceiveDamage(new DamageRequest(991,2,20,5,AttackTag.Hazard));
            Assert.That(result.Accepted,Is.True);Assert.That(_actor.Model.State,Is.EqualTo(CombatState.HitReact));
            yield return new WaitForEndOfFrame();Observe();Capture("evasion-motion-feedback.png");
        }

        [UnityTest]
        public IEnumerator FInput_UsesSevenMappedBones_AndRealReleaseReturnsVisualsToPool()
        {
            var visual=_actor.GetComponent<PlayerKnifePresentation>();
            var pose=_launcher.PresentationGrip;
            Assert.That(pose.joints.Length,Is.EqualTo(7));
            foreach(var joint in pose.joints)Assert.That(_animator.GetBoneTransform(joint.bone),Is.Not.Null);
            int releases=0;_actor.RangedAttackReleased+=r=>releases++;
            yield return Press(Key.F);
            Assert.That(_actor.Model.State,Is.EqualTo(CombatState.RangedAttack));
            bool held=false,flight=false;float firstRelease=-1;
            while(_actor.Model.State==CombatState.RangedAttack||_launcher.ActiveProjectileCount>0)
            {
                yield return new WaitForEndOfFrame();Observe();
                held|=visual.HeldVisible;flight|=visual.FlightVisible;
                if(releases>0&&firstRelease<0){firstRelease=_actor.Model.StateElapsed;Capture("knife-release.png");}
                if(visual.HeldVisible&&_actor.Model.StateElapsed>.1f&&_actor.Model.StateElapsed<.14f)Capture("knife-held.png");
                Assert.That(_animator.applyRootMotion,Is.False);yield return null;
            }
            Assert.That(held,Is.True);Assert.That(flight,Is.True);Assert.That(releases,Is.EqualTo(1));
            Assert.That(firstRelease,Is.InRange(.22f,.253334f));
            // The projectile can return after the render lease's LateUpdate in its last
            // frame. Observe the next actual lifecycle, never call LateUpdate manually.
            yield return null;yield return new WaitForEndOfFrame();Observe();
            Assert.That(visual.HeldVisible||visual.FlightVisible,Is.False);Assert.That(visual.SwordVisible,Is.True);
            Assert.That(visual.TrailPointCount,Is.Zero);
        }

        [UnityTest]
        public IEnumerator EnterDodge_CancelsOutstandingOwnerFreeze_WithoutChangingMotorOrRecovery()
        {
            Assert.That(_speed.Request(HitFeedbackGrade.Heavy,91),Is.True);
            Assert.That(_animator.speed,Is.Zero);
            Vector3 before=_actor.transform.position;
            yield return Press(Key.Space);
            Assert.That(_actor.Model.State,Is.EqualTo(CombatState.Dodge));
            Assert.That(_speed.ActiveGrade,Is.EqualTo(HitFeedbackGrade.None));
            Assert.That(_animator.speed,Is.GreaterThan(0));
            yield return new WaitForSeconds(.06f);yield return new WaitForEndOfFrame();Observe();
            Assert.That(Vector3.Distance(before,_actor.transform.position),Is.GreaterThan(.1f));
            Assert.That(_animator.applyRootMotion,Is.False);
        }

        [UnityTest]
        public IEnumerator ActualGuardInput_StillFreezesOwnerOnPerfectGuard()
        {
            InputSystem.QueueStateEvent(_keyboard,new KeyboardState(Key.Q));yield return null;yield return null;
            Assert.That(_actor.Model.State,Is.EqualTo(CombatState.Guard));
            var result=_actor.ReceiveDamage(new DamageRequest(991,1,20,5,AttackTag.Light));
            Assert.That(result.PerfectGuard,Is.True);
            Assert.That(_speed.ActiveGrade,Is.EqualTo(HitFeedbackGrade.PerfectDefense));
            Assert.That(_animator.speed,Is.Zero);
            Assert.That(_actor.GetComponent<AudioSource>().isPlaying,Is.True);
            InputSystem.QueueStateEvent(_keyboard,new KeyboardState());yield return null;
        }

        IEnumerator Press(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard,new KeyboardState(key));yield return null;
            InputSystem.QueueStateEvent(_keyboard,new KeyboardState());yield return null;
        }

        [UnityTest]
        public IEnumerator NativeCombatCandidate_ActualThreeClickCombo_KeepsHipsAligned()
            => CheckNativeCombo(false);

        [UnityTest]
        public IEnumerator NativeCombatCandidate_AfterIdleSettles_ActualThreeClickCombo_KeepsHipsAligned()
            => CheckNativeCombo(true);

        IEnumerator CheckNativeCombo(bool settleIdle)
        {
            const string profile="Assets/_Game/Art/Review/TinyHero/CombatCandidates/20261002-053304-723/PlayerAnimationSet_Review_Tiny.asset";
            var set=AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(profile);Assert.That(set,Is.Not.Null);
            _profileEvidence=profile;
            _actor.GetComponent<PlayerAnimationPresenter>().Configure(_animator,_actor,_actor.GetComponent<ThirdPersonMotor>(),set);
            _animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            Assert.That(_animator.GetComponent<AnimatorSpeedCoordinator>(),Is.SameAs(_speed));
            _mouse=InputSystem.AddDevice<Mouse>("TinyNativeComboMouse");
            yield return null;
            if(settleIdle)yield return new WaitForSeconds(.5f);
            Vector2 baseline=PlanarHips();Vector3 root=_actor.transform.position;
            float peak=0;bool queuedSecond=false,queuedThird=false,reachedThird=false;
            InputSystem.QueueStateEvent(_mouse,new MouseState{buttons=1});yield return null;
            InputSystem.QueueStateEvent(_mouse,new MouseState());yield return null;
            Assert.That(_actor.Model.State,Is.EqualTo(CombatState.LightAttack1));
            for(int frame=0;frame<5000;frame++)
            {
                yield return new WaitForEndOfFrame();Observe();
                var state=_actor.Model.State;
                peak=Mathf.Max(peak,Vector2.Distance(baseline,PlanarHips()));
                Assert.That(Vector2.Distance(new Vector2(root.x,root.z),new Vector2(_actor.transform.position.x,_actor.transform.position.z)),Is.LessThan(.01f));
                Assert.That(_animator.applyRootMotion,Is.False);
                if(state==CombatState.LightAttack1&&!queuedSecond&&_actor.Model.StateElapsed>=_actor.Model.LightRecoveryStart+.01f)
                {
                    queuedSecond=true;InputSystem.QueueStateEvent(_mouse,new MouseState{buttons=1});
                    yield return null;InputSystem.QueueStateEvent(_mouse,new MouseState());
                }
                else if(state==CombatState.LightAttack2&&!queuedThird&&_actor.Model.StateElapsed>=_actor.Model.LightRecoveryStart+.01f)
                {
                    queuedThird=true;InputSystem.QueueStateEvent(_mouse,new MouseState{buttons=1});
                    yield return null;InputSystem.QueueStateEvent(_mouse,new MouseState());
                }
                if(state==CombatState.LightAttack3&&!reachedThird){Capture("native-combo-third.png");reachedThird=true;}
                if(reachedThird&&state==CombatState.Locomotion)break;
                yield return null;
            }
            yield return null;yield return new WaitForEndOfFrame();Observe();Capture("native-combo-recovered.png");
            float final=Vector2.Distance(baseline,PlanarHips());
            File.WriteAllText(_output+"/native-combo-metrics.txt","profile="+profile+";settled="+settleIdle+";peak="+peak.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+
                ";final="+final.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+";root="+_actor.transform.position);
            Assert.That(queuedSecond&&queuedThird&&reachedThird,Is.True,"All three clicks must drive the unchanged domain combo.");
            // Exactly the old production thresholds; its two Ranger tests remain untouched.
            Assert.That(peak,Is.LessThan(.4f));Assert.That(final,Is.LessThan(.1f));
        }

        Vector2 PlanarHips()
        {
            Vector3 p=_actor.transform.InverseTransformPoint(_animator.GetBoneTransform(HumanBodyBones.Hips).position);
            return new Vector2(p.x,p.z);
        }

        void Observe()
        {
            var visual=_actor.GetComponent<PlayerKnifePresentation>();
            _rows.Add(new Row{frame=Time.frameCount,state=_actor.Model.State.ToString(),time=_actor.Model.StateElapsed,delta=Time.deltaTime,
                normalized=_animator.GetCurrentAnimatorStateInfo(0).normalizedTime,speed=_animator.speed,grade=_speed.ActiveGrade.ToString(),
                root=_actor.transform.position,hips=_animator.GetBoneTransform(HumanBodyBones.Hips).position,
                hand=_animator.GetBoneTransform(HumanBodyBones.RightHand).position,held=visual.HeldVisible,flight=visual.FlightVisible,
                minY=VertexMinimum(_animator.gameObject),headMinY=VertexMinimum(_animator.GetBoneTransform(HumanBodyBones.Head).gameObject)});
        }

        static float VertexMinimum(GameObject root)
        {
            float min=float.PositiveInfinity;
            foreach(var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=new Mesh();skin.BakeMesh(mesh,true);
                foreach(var vertex in mesh.vertices)min=Mathf.Min(min,skin.transform.TransformPoint(vertex).y);
                Object.Destroy(mesh);
            }
            foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var renderer=filter.GetComponent<Renderer>();
                if(!renderer.enabled||renderer.forceRenderingOff)continue;
                // Runtime strips vertex access on some imported rigid shields. Do not
                // mutate originals/importers or suppress the error to make a test green.
                if(!filter.sharedMesh.isReadable)min=Mathf.Min(min,renderer.bounds.min.y);
                else foreach(var vertex in filter.sharedMesh.vertices)min=Mathf.Min(min,filter.transform.TransformPoint(vertex).y);
            }
            return min;
        }

        void Capture(string filename)
        {
            var rt=new RenderTexture(960,600,24);rt.Create();var old=RenderTexture.active;
            var image=new Texture2D(960,600,TextureFormat.RGB24,false);
            try
            {
                RenderPipeline.SubmitRenderRequest(_camera,new RenderPipeline.StandardRequest{destination=rt});
                RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,960,600),0,0);image.Apply();File.WriteAllBytes(_output+"/"+filename,image.EncodeToPNG());
            }
            finally{RenderTexture.active=old;rt.Release();Object.Destroy(rt);Object.Destroy(image);}
        }

        static AnimationClip Clip(string name)=>AssetDatabase.LoadAllAssetsAtPath(Native+name).OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview"));
    }
}
#endif
