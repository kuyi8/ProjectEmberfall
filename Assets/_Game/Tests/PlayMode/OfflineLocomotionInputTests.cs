#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed class OfflineLocomotionInputTests
    {
        [UnityTest] public IEnumerator ForwardW_UsesCompleteJogCycles_AndKeepsCapsuleUpright() => Verify(false);
        [UnityTest] public IEnumerator ForwardWShift_UsesCompleteSprintCycles_AndKeepsCapsuleUpright() => Verify(true);
        [UnityTest] public IEnumerator MovingVisualYaw_DiagonalStopAndLightAttackRestoreWithoutAccumulation() => Verify(false, true);

        IEnumerator Verify(bool sprint, bool transitions=false)
        {
            Assert.That(M2RouteFlowController.EditorTestSavePath, Is.Not.Null.And.Not.Empty);
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            yield return SceneManager.LoadSceneAsync("90_CombatGym", LoadSceneMode.Single);
            yield return null; yield return null;
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var motor = player.GetComponent<ThirdPersonMotor>();
            var input = player.GetComponent<PlayerInputReader>();
            var animator = player.GetComponentInChildren<Animator>();
            var presenter = player.GetComponent<PlayerAnimationPresenter>();
            foreach (var actor in Object.FindObjectsOfType<MonoBehaviour>())
                if (actor is MeleeEnemyActor || actor is ShieldEnemyActor || actor is RangedEnemyActor)
                    actor.enabled = false;
            foreach (var dummy in Object.FindObjectsOfType<TrainingDummy>()) dummy.gameObject.SetActive(false);
            // Explicit isolated straight-floor fixture, not natural route/AI or human acceptance.
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "TEST_ONLY_LocomotionFloor";
            floor.transform.position = new Vector3(100f, -.5f, 100f);
            floor.transform.localScale = new Vector3(12f, 1f, 60f);
            var controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(100f, .1f, 85f), Quaternion.identity);
            motor.ResetAfterTeleport(); controller.enabled = true;
            foreach (var rig in Object.FindObjectsOfType<ThirdPersonCameraRig>()) rig.enabled = false;
            var camera = Camera.main;
            camera.transform.SetPositionAndRotation(player.transform.position + new Vector3(0, 2.3f, -5f), Quaternion.Euler(15, 0, 0));
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var settings = InputSystem.settings;
            var isolated = Object.Instantiate(settings);
            isolated.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            // This fixture measures synthetic held-input motion, not OS foreground delivery.
            // The installed Input System resets devices on focus changes unless BOTH are set.
            bool runInBackground = UnityEngine.Application.runInBackground;
            UnityEngine.Application.runInBackground = true;
            isolated.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = isolated;
            var keyboard = InputSystem.AddDevice<Keyboard>("OfflineLocomotionTestKeyboard");
            var rows = new List<string>();
            var inputEvents = new List<string>();
            Action<InputDevice, InputDeviceChange> observeDevice = (device, change) => {
                if (device == keyboard) inputEvents.Add("device:" + change + " time=" + Time.time + "\n" +
                    new System.Diagnostics.StackTrace(true));
            };
            Action<InputEventPtr, InputDevice> observeEvent = (evt, device) => {
                if (device != keyboard) return;
                float w;
                inputEvents.Add("event:" + evt.type + " time=" + Time.time + " w=" +
                    (keyboard.wKey.ReadValueFromEvent(evt, out w) ? w.ToString("R") : "n/a"));
            };
            InputSystem.onDeviceChange += observeDevice;
            InputSystem.onEvent += observeEvent;
            string folder = "Builds/ArtReview/locomotion-input/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
            Directory.CreateDirectory(folder);
            try
            {
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
                yield return null; yield return null;
                InputSystem.QueueStateEvent(keyboard, sprint ? new KeyboardState(Key.W, Key.LeftShift) : new KeyboardState(Key.W));
                float started = Time.time, firstPhase = -1f, lastPhase = 0f;
                int sampled = 0, captured = 0;
                float meanStart=-1f, yawSum=0f, previousYaw=0f;
                int yawFrames=0;
                Vector3 start = player.transform.position;
                string expected = sprint ? "Rig|Sprint_Loop" : "Rig|Jog_Fwd_Loop";
                // Sprint has an existing authoritative held-input warmup BEFORE acceleration.
                // Do not require a fully weighted Sprint pose during that authored transition.
                var tuning = NaturalPlayerContactTests.Field<CombatTuning>(player.Model, "_tuning");
                float sampleAfter = .6f + (sprint ? tuning.SprintWarmupSeconds : 0f);
                while (Time.time - started < 3.4f)
                {
                    yield return new WaitForEndOfFrame();
                    var state = animator.GetCurrentAnimatorStateInfo(0);
                    var sampledClips = animator.GetCurrentAnimatorClipInfo(0);
                    string clipWeights = "";
                    foreach (var clip in sampledClips) clipWeights += clip.clip.name + ":" + clip.weight.ToString("R") + ";";
                    Vector3 sampledTorso = animator.GetBoneTransform(HumanBodyBones.Chest).position - animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    Vector3 sampledLocal = player.transform.InverseTransformDirection(sampledTorso);
                    // Real world-space shoulder-joint geometry. Animator.bodyRotation outside
                    // OnAnimatorIK emits warnings and is not our LateUpdate observation API.
                    Vector3 shoulderAxis = animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position -
                                           animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position;
                    Vector3 visualForward = player.transform.InverseTransformDirection(Vector3.Cross(shoulderAxis, Vector3.up));
                    float visualHeading = Mathf.Atan2(visualForward.x, visualForward.z) * Mathf.Rad2Deg;
                    rows.Add(JsonUtility.ToJson(new Row { time = Time.time, phase = state.normalizedTime,
                        visualYaw = presenter.AppliedOfflineLocomotionYaw,
                        shoulderYaw = visualHeading, inputX = input.Move.x, inputY = input.Move.y,
                        keyHeld = keyboard.wKey.isPressed, keyboardEnabled = keyboard.enabled,
                        hasFocus = UnityEngine.Application.isFocused,
                        speed = motor.HorizontalSpeed, parameter = animator.GetFloat("Speed"), clips = clipWeights,
                        position = player.transform.position, side = Mathf.Atan2(sampledLocal.x, sampledLocal.y) * Mathf.Rad2Deg,
                        forward = Mathf.Atan2(sampledLocal.z, sampledLocal.y) * Mathf.Rad2Deg,
                        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position,
                        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot).position,
                        leftToes = animator.GetBoneTransform(HumanBodyBones.LeftToes).position,
                        rightToes = animator.GetBoneTransform(HumanBodyBones.RightToes).position }));
                    Assert.That(Mathf.Abs(presenter.AppliedOfflineLocomotionYaw-previousYaw),Is.LessThanOrEqualTo(360f*Time.deltaTime+.01f),"Bound BOTH entering/leaving visual yaw, not Actor rotation.");
                    previousYaw=presenter.AppliedOfflineLocomotionYaw;
                    if (Time.time - started < sampleAfter) continue;
                    Assert.That(input.Move.y, Is.GreaterThan(.9f));
                    Assert.That(input.Move.x, Is.EqualTo(0).Within(.001f));
                    Assert.That(motor.IsSprinting, Is.EqualTo(sprint));
                    Assert.That(animator.runtimeAnimatorController, Is.SameAs(AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(
                        "Assets/_Game/Settings/PlayerAnimationSet_M1.asset").OfflineController));
                    Assert.That(Vector3.Angle(player.transform.up, Vector3.up), Is.LessThan(.01f));
                    Assert.That(animator.applyRootMotion, Is.False);
                    var clips = animator.GetCurrentAnimatorClipInfo(0);
                    bool observed = false;
                    foreach (var clip in clips) if (clip.clip.name == expected && clip.weight > .95f) observed = true;
                    Assert.That(observed, Is.True, "Actual moving Animator must use " + expected + "; actual=" + clipWeights +
                        " motor=" + motor.HorizontalSpeed + " parameter=" + animator.GetFloat("Speed"));
                    if (firstPhase < 0f) firstPhase = state.normalizedTime;
                    lastPhase = state.normalizedTime; sampled++;
                    if(meanStart<0f)meanStart=Mathf.Ceil(state.normalizedTime);
                    if(state.normalizedTime>=meanStart&&state.normalizedTime<meanStart+2f){
                        yawSum+=visualHeading;yawFrames++;
                    }
                    camera.transform.position = player.transform.position + new Vector3(0, 2.3f, -5f);
                    if (captured < 4 && Time.time - started >= .85f + captured * .25f)
                    {
                        ScreenCapture.CaptureScreenshot(folder + "/" + expected.Replace("Rig|", "") + "-" + captured + ".png");
                        captured++;
                    }
                }
                Assert.That(sampled, Is.GreaterThan(30));
                Assert.That(lastPhase - firstPhase, Is.GreaterThan(2f), "Observe more than two real full cycles, not preset poses.");
                Assert.That(player.transform.position.z - start.z, Is.GreaterThan(10f));
                Assert.That(Mathf.Abs(player.transform.position.x - start.x), Is.LessThan(.01f));
                Assert.That(lastPhase,Is.GreaterThanOrEqualTo(meanStart+2f),"Use two complete integer cycles for the yaw mean.");
                Assert.That(yawFrames,Is.GreaterThan(30));
                Assert.That(Mathf.Abs(yawSum/yawFrames),Is.LessThan(5f),"Mean actual shoulder-defined visual heading vs Actor, not collider-forward evidence.");
                if(transitions){
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.D));
                    yield return new WaitForSeconds(.75f);yield return new WaitForEndOfFrame();
                    Assert.That(Mathf.DeltaAngle(0,player.transform.eulerAngles.y),Is.InRange(35f,55f));
                    Assert.That(presenter.AppliedOfflineLocomotionYaw,Is.EqualTo(-27.3f).Within(.3f));
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                    yield return new WaitForSeconds(.35f);yield return new WaitForEndOfFrame();
                    Assert.That(presenter.AppliedOfflineLocomotionYaw,Is.EqualTo(0f).Within(.1f),"Restore while stopped, not a permanent rotated model.");
                    InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W));
                    yield return new WaitForSeconds(.7f);yield return new WaitForEndOfFrame();
                    Assert.That(presenter.AppliedOfflineLocomotionYaw,Is.EqualTo(-27.3f).Within(.3f));
                    var mouse=InputSystem.AddDevice<Mouse>("VisualYawAttackMouse");
                    try{
                        InputSystem.QueueStateEvent(keyboard,new KeyboardState());
                        InputSystem.QueueStateEvent(mouse,new MouseState().WithButton(MouseButton.Left));
                        yield return null;yield return new WaitForEndOfFrame();
                        Assert.That(player.Model.State,Is.EqualTo(CombatState.LightAttack1));
                        yield return new WaitForSeconds(.1f);yield return new WaitForEndOfFrame();
                        Assert.That(presenter.AppliedOfflineLocomotionYaw,Is.EqualTo(0f).Within(.001f),"No locomotion yaw left over at attack contact.");
                    }finally{InputSystem.RemoveDevice(mouse);}
                }
            }
            finally
            {
                inputEvents.Add("fixture-finally-enter time=" + Time.time);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.RemoveDevice(keyboard); InputSystem.settings = settings;
                UnityEngine.Application.runInBackground = runInBackground;
                InputSystem.onDeviceChange -= observeDevice;
                InputSystem.onEvent -= observeEvent;
                Object.DestroyImmediate(isolated); Object.Destroy(floor);
                File.WriteAllLines(folder + "/" + (sprint ? "sprint" : "jog") + ".jsonl", rows);
                File.WriteAllLines(folder + "/input-events.txt", inputEvents);
            }
        }

        // Foot/toe world trajectories are diagnostic, not a new proxy assertion for skin contact.
        [Serializable] sealed class Row { public float time, phase, speed, parameter, side, forward, visualYaw, shoulderYaw, inputX, inputY; public string clips;
            public bool keyHeld, keyboardEnabled, hasFocus;
            public Vector3 position, leftFoot, rightFoot, leftToes, rightToes; }
    }
}
#endif
