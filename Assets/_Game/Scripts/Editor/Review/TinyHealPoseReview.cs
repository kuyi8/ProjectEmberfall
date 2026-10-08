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
    /// <summary>Independent target-rig drinking art. Never installs a production profile or resolves healing.</summary>
    public static class TinyHealPoseReview
    {
        public const string Root = "Assets/_Game/Art/Review/TinyHero/HealCandidates";
        public const int SampleRate = 120;
        public const float HipsPositionLimit = .005f, HipsRotationLimit = .5f;
        public const float JointStepLimit = 8f, MouthDistanceLimit = .045f;
        public const float BottleSpoutHeight = .11f;
        const int DrinkSolveIterations = 12;
        const float MuscleStepLimit = .12f;
        const string NativeIdle = "Assets/RPG Tiny Hero Duo/Animation/SwordAndShield/Idle_Battle_SwordAndShiled.fbx";

        [Serializable] public sealed class Timing
        {
            public float pickupEnd, mouthArrival, resolve, end;
        }
        [Serializable] sealed class Row
        {
            public float time, hipNativePositionDeltaMetres, hipLocalRotationDelta, jointAngleStep;
            public float mouthDistance, minGroundClearance, footY;
            public string phase;
            public Vector3 hand, mouth, bottleSpout, bodyPosition;
            public Quaternion bodyRotation;
        }
        [Serializable] sealed class Report
        {
            public string scope = "Independent Editor-only target-rig HumanPose bake and CPU-skin images, not PlayMode input, gameplay healing, interrupted consumption, contact, network or production acceptance.";
            public string candidateAssetPath, avatarPath, idleSource, bottleSource, buildException;
            public string bodyPolicy = "Keep THIS native Idle pelvis local position and rotation; recalculate COM after changed upper limbs. No Actor/Animator-root lift, scale change, all-state flattening or floor-driven adjustment.";
            public string bottlePolicy = "OWN procedural 18cm low-poly visual candidate. No imported art chosen from filenames, no Collider/behaviour. Sword/shield hidden ONLY on disposable review instance and restored in finally. Bottle world size cancels inherited hand scale.";
            public string presentationResolve = "Sip hold reaches existing domain HealResolveTime; no AnimationEvent or alternate healing clock. No resource/health writes.";
            public Timing timing;
            public bool protectedBytesSame, gatesPass, actorTransformUnchanged, equipmentRestored;
            public int protectedFiles, samples, mappedRightGripBones;
            public int drinkSolveIterations;
            public float beforeSolveMouthDistance, afterSolveMouthDistance, afterSolveAxisAngle;
            public Vector3 nativeUpperArm, nativeLowerArm, nativeHand, nativeThumbIntermediate, nativeIndexIntermediate;
            public float maxHipNativePositionDeltaMetres, maxHipLocalRotationDelta, maxJointAngleStep;
            public float minGroundClearance, resolveMouthDistance, roundTripBoneDelta;
            public Vector3 referenceHipLocalPosition, gripLocalPosition;
            public Quaternion referenceHipLocalRotation, gripLocalRotation;
            public string[] gateFailures;
            public Row[] poses;
        }

        // Fractions are art phase boundaries in the existing domain interval, not new gameplay timings.
        public static Timing GetTiming(float duration, float resolve)
        {
            if (!Finite(duration) || !Finite(resolve) || duration <= 0 || resolve <= 0 || resolve >= duration)
                throw new ArgumentOutOfRangeException("resolve", "Healing requires a finite pre-end resolve point.");
            return new Timing { pickupEnd = resolve * .23f, mouthArrival = resolve * .76f, resolve = resolve, end = duration };
        }

        // Used for pose interpolation ONLY. At resolve, art is still visibly taking the sip.
        public static float SipWeight(float time, float duration, float resolve)
        {
            var timing = GetTiming(duration, resolve);
            if (time <= timing.pickupEnd || time >= timing.end) return 0;
            if (time < timing.mouthArrival) return Smooth((time - timing.pickupEnd) / (timing.mouthArrival - timing.pickupEnd));
            if (time <= timing.resolve) return 1;
            return 1 - Smooth((time - timing.resolve) / (timing.end - timing.resolve));
        }

        public static string Phase(float time, float duration, float resolve)
        {
            var timing = GetTiming(duration, resolve);
            return time < timing.pickupEnd ? "ReachFlask" : time < timing.mouthArrival ? "RaiseFlask" :
                time <= timing.resolve ? "SipBeforeDomainResolve" : time < duration ? "ReturnFlask" : "NativeIdle";
        }

        public static string BuildAndCapture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Idle graphics Editor required.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Preserve unsaved scenes.");

            string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"), assets = Root + "/" + id;
            string dir = Path.GetFullPath("Builds/ArtReview/tiny-heal/" + id);
            Directory.CreateDirectory(dir);
            var frozen = new[] { "Assets/RPG Tiny Hero Duo", "Assets/_Game/Settings", "Assets/_Game/Scenes",
                "Assets/_Game/Prefabs/Characters", "Assets/_Game/Resources/Networking", "Assets/_Game/Art/Animations/Player" }
                .SelectMany(p => Directory.GetFiles(p, "*", SearchOption.AllDirectories))
                .Concat(new[] { TinyPoseCandidateReview.AvatarPath, TinyPoseCandidateReview.AvatarPath + ".meta" })
                .Distinct().ToDictionary(p => p, Hash);
            File.WriteAllLines(dir + "/frozen-before.txt", frozen.Select(p => p.Value + " " + p.Key));
            var previous = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            var materials = new List<Material>(); var meshes = new List<Mesh>();
            var report = new Report { avatarPath = TinyPoseCandidateReview.AvatarPath, idleSource = NativeIdle,
                bottleSource = "Codex-authored procedural low-poly candidate; preview only", protectedFiles = frozen.Count };
            Renderer[] equipment = null; bool[] equipmentEnabled = null;
            try
            {
                var tuning = AssetDatabase.LoadAssetAtPath<CombatTuningAsset>("Assets/_Game/Settings/CombatTuning_M1.asset").CreateRuntimeCopy();
                report.timing = GetTiming(tuning.HealDuration, tuning.HealResolveTime);
                var idle = AssetDatabase.LoadAllAssetsAtPath(NativeIdle).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview"));
                var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TinyPoseCandidateReview.AvatarPath));
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (instance.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                    throw new InvalidOperationException("Candidate rig must not bring gameplay behaviours into the authoring preview.");
                equipment = instance.GetComponentsInChildren<Renderer>(true).Where(r => r.name == "OHS03Polyart" || r.name == "Shield08Polyart").ToArray();
                if (equipment.Length != 2 || equipment.Select(r => r.name).Distinct().Count() != 2)
                    throw new InvalidOperationException("Expected explicit Tiny sword and shield renderers.");
                equipmentEnabled = equipment.Select(r => r.enabled).ToArray();
                foreach (var item in equipment) item.enabled = false;
                TinyHeroBakeoff.SetupLight();
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "ReviewHealGround_ColliderTopMinus02";
                ground.transform.SetPositionAndRotation(new Vector3(0, -.07f, 0), Quaternion.identity);
                ground.transform.localScale = new Vector3(8, .1f, 8);
                var groundMaterial = Material(new Color(.24f, .33f, .34f)); materials.Add(groundMaterial);
                ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                var groundCollider = ground.GetComponent<Collider>();
                var camera = new GameObject("TinyHealReviewCamera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.3f, .43f, .46f);
                camera.orthographic = true; camera.orthographicSize = 1.12f; camera.nearClipPlane = .03f;
                TinyHeroBakeoff.View(camera, new Vector3(2.7f, 1.5f, 3.5f), new Vector3(0, .78f, 0));
                var bottle = CreateBottle(materials, meshes);
                using (var rig = new TinyHeroBakeoff.Rig(instance))
                using (var handler = new HumanPoseHandler(rig.animator.avatar, rig.animator.transform))
                {
                    rig.Pose(idle, idle.length * .375f);
                    var hips = Bone(rig, HumanBodyBones.Hips); var hand = Bone(rig, HumanBodyBones.RightHand);
                    var referencePosition = hips.localPosition; var referenceRotation = hips.localRotation;
                    report.referenceHipLocalPosition = referencePosition; report.referenceHipLocalRotation = referenceRotation;
                    HumanPose baseline = Pose(handler);
                    var humanoidBones = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                        .Select(b => rig.animator.GetBoneTransform((HumanBodyBones)b)).Where(b => b != null).ToArray();
                    var before = humanoidBones.Select(b => b.position).ToArray(); handler.SetHumanPose(ref baseline);
                    report.roundTripBoneDelta = humanoidBones.Select((b, i) => Vector3.Distance(b.position, before[i])).Max();
                    if (report.roundTripBoneDelta > .001f) throw new InvalidOperationException("Target identity HumanPose round-trip failed; no guessed coordinate conversion.");
                    rig.Pose(idle, idle.length * .375f);
                    var mouth = rig.go.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.enabled && r.name == "Mouth01");
                    Vector3 mouthPoint = DrawnBounds(mouth).center;
                    var fingers = new[] { HumanBodyBones.RightHand, HumanBodyBones.RightThumbProximal,
                        HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal, HumanBodyBones.RightIndexProximal,
                        HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal };
                    if (fingers.Any(b => rig.animator.GetBoneTransform(b) == null)) throw new InvalidOperationException("Verified seven-bone grip mapping changed.");
                    report.mappedRightGripBones = fingers.Length;
                    // Hold between actual finger middle segments, not buried at the palm's proximal joints.
                    // This socket is fixed throughout the gesture; it is not translated to pass the mouth gate.
                    Vector3 gripPoint = (Bone(rig, HumanBodyBones.RightThumbIntermediate).position + Bone(rig, HumanBodyBones.RightIndexIntermediate).position) * .5f;
                    report.nativeUpperArm = Bone(rig, HumanBodyBones.RightUpperArm).position;
                    report.nativeLowerArm = Bone(rig, HumanBodyBones.RightLowerArm).position;
                    report.nativeHand = hand.position;
                    report.nativeThumbIntermediate = Bone(rig, HumanBodyBones.RightThumbIntermediate).position;
                    report.nativeIndexIntermediate = Bone(rig, HumanBodyBones.RightIndexIntermediate).position;
                    report.gripLocalPosition = hand.InverseTransformPoint(gripPoint);
                    report.gripLocalRotation = Quaternion.Inverse(hand.rotation);
                    Vector3 gripOffsetMetres = Quaternion.Inverse(hand.rotation) * (gripPoint - hand.position);
                    Quaternion nativeHandRotation = hand.rotation;
                    Vector3 nativeHandPosition = hand.position;
                    HumanPose pickup = KeyPose(rig, handler, idle, referencePosition, referenceRotation,
                        nativeHandPosition + Vector3.down * .025f - Vector3.forward * .03f, nativeHandRotation);
                    rig.Pose(idle, idle.length * .375f);
                    Vector3 bottleAxis = (-Vector3.forward * .09f + Vector3.up * .045f).normalized;
                    Quaternion drinkHandRotation = Quaternion.FromToRotation(Vector3.up, bottleAxis) * nativeHandRotation;
                    Vector3 drinkHandTarget = mouthPoint - bottleAxis * BottleSpoutHeight - drinkHandRotation * gripOffsetMetres;
                    HumanPose drinking = KeyPose(rig, handler, idle, referencePosition, referenceRotation, drinkHandTarget, drinkHandRotation);
                    // The first retained run proved that direct bone IK is altered by Humanoid muscle
                    // projection: spout missed the mouth by 7.2cm. Solve on the ACTUAL Set/Get target
                    // rig, changing only nine whitelisted upper-limb muscles, never the bottle socket.
                    drinking = RefineDrinkPose(rig, handler, drinking, referencePosition, referenceRotation,
                        mouth, report.gripLocalPosition, report.gripLocalRotation, bottleAxis, report);
                    Func<float, HumanPose> evaluate = t =>
                    {
                        HumanPose pose;
                        if (t < report.timing.pickupEnd) pose = Blend(baseline, pickup, Smooth(t / report.timing.pickupEnd));
                        else if (t < report.timing.mouthArrival) pose = Blend(pickup, drinking,
                            Smooth((t - report.timing.pickupEnd) / (report.timing.mouthArrival - report.timing.pickupEnd)));
                        else if (t <= report.timing.resolve) pose = Clone(drinking);
                        else pose = Blend(drinking, baseline, Smooth((t - report.timing.resolve) / (report.timing.end - report.timing.resolve)));
                        handler.SetHumanPose(ref pose);
                        hips.SetLocalPositionAndRotation(referencePosition, referenceRotation);
                        return Pose(handler); // Recalculate COM; do not zero RootT after this or it moves the native pelvis again.
                    };
                    var clip = Bake(idle, tuning.HealDuration, evaluate);
                    EnsureFolder(assets); report.candidateAssetPath = assets + "/" + clip.name + ".anim";
                    AssetDatabase.CreateAsset(clip, report.candidateAssetPath); AssetDatabase.SaveAssetIfDirty(clip);
                    foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
                    camera.cullingMask = 1 << 31; Physics.SyncTransforms();
                    var rows = new List<Row>(); Quaternion[] lastRotations = null;
                    var joints = new[] { HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand };
                    var captureTimes = new[] { 0, report.timing.pickupEnd, report.timing.mouthArrival, report.timing.resolve, tuning.HealDuration };
                    int steps = Mathf.CeilToInt(tuning.HealDuration * SampleRate);
                    var sampleTimes = Enumerable.Range(0, steps + 1).Select(i => tuning.HealDuration * i / steps).Concat(captureTimes).Distinct().OrderBy(t => t).ToArray();
                    foreach (float time in sampleTimes)
                    {
                        rig.Pose(clip, time);
                        bottle.transform.SetPositionAndRotation(hand.TransformPoint(report.gripLocalPosition), hand.rotation * report.gripLocalRotation);
                        Vector3 actualMouth = DrawnBounds(mouth).center, spout = bottle.transform.TransformPoint(Vector3.up * BottleSpoutHeight);
                        var rotations = joints.Select(b => Bone(rig, b).rotation).ToArray();
                        var currentPose = Pose(handler);
                        var row = new Row { time = time, phase = Phase(time, tuning.HealDuration, tuning.HealResolveTime),
                            // Imported bones have inherited scale; local units are not metres.
                            hipNativePositionDeltaMetres = hips.parent.TransformVector(hips.localPosition - referencePosition).magnitude,
                            hipLocalRotationDelta = Quaternion.Angle(hips.localRotation, referenceRotation),
                            jointAngleStep = lastRotations == null ? 0 : rotations.Select((q, i) => Quaternion.Angle(q, lastRotations[i])).Max(),
                            hand = hand.position, mouth = actualMouth, bottleSpout = spout,
                            mouthDistance = Vector3.Distance(actualMouth, spout), minGroundClearance = GroundClearance(rig.go, groundCollider),
                            footY = Mathf.Min(Bone(rig, HumanBodyBones.LeftFoot).position.y, Bone(rig, HumanBodyBones.RightFoot).position.y),
                            bodyPosition = currentPose.bodyPosition, bodyRotation = currentPose.bodyRotation };
                        rows.Add(row); lastRotations = rotations;
                        if (captureTimes.Any(t => Mathf.Abs(t - time) < .00001f))
                        {
                            var image = TinyNativeMotionReview.RenderEvaluatedPose(camera, rig.go);
                            File.WriteAllBytes(dir + "/heal-" + Mathf.RoundToInt(time * 1000).ToString("0000") + ".png", image.EncodeToPNG()); Object.DestroyImmediate(image);
                            if (Mathf.Abs(time - tuning.HealResolveTime) < .00001f)
                            {
                                Vector3 savedPosition = camera.transform.position; Quaternion savedRotation = camera.transform.rotation;
                                float savedSize = camera.orthographicSize;
                                try
                                {
                                    camera.orthographicSize = .35f;
                                    TinyHeroBakeoff.View(camera, actualMouth + new Vector3(.65f, .1f, .9f), actualMouth - Vector3.up * .10f);
                                    image = TinyNativeMotionReview.RenderEvaluatedPose(camera, rig.go);
                                    File.WriteAllBytes(dir + "/heal-resolve-grip-detail.png", image.EncodeToPNG()); Object.DestroyImmediate(image);
                                }
                                finally { camera.transform.SetPositionAndRotation(savedPosition, savedRotation); camera.orthographicSize = savedSize; }
                            }
                        }
                    }
                    report.poses = rows.ToArray(); report.samples = rows.Count;
                    report.maxHipNativePositionDeltaMetres = rows.Max(r => r.hipNativePositionDeltaMetres);
                    report.maxHipLocalRotationDelta = rows.Max(r => r.hipLocalRotationDelta);
                    report.maxJointAngleStep = rows.Max(r => r.jointAngleStep);
                    report.minGroundClearance = rows.Min(r => r.minGroundClearance);
                    report.resolveMouthDistance = rows.Single(r => Mathf.Abs(r.time - tuning.HealResolveTime) < .00001f).mouthDistance;
                    report.actorTransformUnchanged = rig.go.transform.position == Vector3.zero && rig.go.transform.rotation == Quaternion.identity && rig.go.transform.localScale == Vector3.one;
                }
                for (int i = 0; i < equipment.Length; i++) equipment[i].enabled = equipmentEnabled[i];
                report.equipmentRestored = equipment.Select((r, i) => r.enabled == equipmentEnabled[i]).All(b => b);
                report.protectedBytesSame = frozen.All(p => Hash(p.Key) == p.Value);
                var failures = new List<string>();
                if (!report.protectedBytesSame) failures.Add("Protected source/production bytes changed");
                if (!report.actorTransformUnchanged || !report.equipmentRestored) failures.Add("Preview isolation/restoration failed");
                if (report.maxHipNativePositionDeltaMetres > HipsPositionLimit || report.maxHipLocalRotationDelta > HipsRotationLimit) failures.Add("Native pelvis preservation failed after actual clip replay");
                if (report.maxJointAngleStep > JointStepLimit) failures.Add("120Hz arm-chain continuity exceeded predeclared threshold");
                if (report.minGroundClearance < 0) failures.Add("Drawn character vertices penetrate actual diagnostic floor");
                if (report.resolveMouthDistance > MouthDistanceLimit) failures.Add("Bottle spout does not reach actual drawn mouth at domain resolve");
                report.gateFailures = failures.ToArray(); report.gatesPass = failures.Count == 0;
                File.WriteAllText(dir + "/heal-candidate.json", JsonUtility.ToJson(report, true));
                if (!report.gatesPass) throw new InvalidOperationException("Independent healing candidate failed; retain " + dir + ": " + string.Join("; ", failures));
                return dir;
            }
            catch (Exception exception)
            {
                report.buildException = exception.GetType().Name + ": " + exception.Message;
                report.protectedBytesSame = frozen.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value);
                File.WriteAllText(dir + "/heal-candidate.json", JsonUtility.ToJson(report, true));
                throw;
            }
            finally
            {
                if (equipment != null && equipmentEnabled != null) for (int i = 0; i < equipment.Length; i++) if (equipment[i] != null) equipment[i].enabled = equipmentEnabled[i];
                EditorSceneManager.CloseScene(scene, true); EditorSceneManager.RestoreSceneManagerSetup(previous);
                foreach (var material in materials) if (material != null) Object.DestroyImmediate(material);
                foreach (var mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        static HumanPose KeyPose(TinyHeroBakeoff.Rig rig, HumanPoseHandler handler, AnimationClip idle,
            Vector3 hipPosition, Quaternion hipRotation, Vector3 handTarget, Quaternion handRotation)
        {
            rig.Pose(idle, idle.length * .375f);
            SolveArm(Bone(rig, HumanBodyBones.RightUpperArm), Bone(rig, HumanBodyBones.RightLowerArm), Bone(rig, HumanBodyBones.RightHand), handTarget);
            Bone(rig, HumanBodyBones.RightHand).rotation = handRotation;
            var pose = Pose(handler); handler.SetHumanPose(ref pose);
            Bone(rig, HumanBodyBones.Hips).SetLocalPositionAndRotation(hipPosition, hipRotation);
            return Pose(handler);
        }

        static void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target)
        {
            Vector3 a = upper.position, b = lower.position, c = hand.position, direction = target - a;
            float first = Vector3.Distance(a, b), second = Vector3.Distance(b, c);
            float distance = Mathf.Clamp(direction.magnitude, Mathf.Abs(first - second) + .001f, first + second - .001f);
            direction.Normalize();
            // Explicit outward/front elbow pole; no runtime IK writer is introduced.
            Vector3 pole = new Vector3(1, -.15f, .35f), bend = (pole - Vector3.Dot(pole, direction) * direction).normalized;
            float along = (first * first + distance * distance - second * second) / (2 * distance);
            Vector3 elbow = a + direction * along + bend * Mathf.Sqrt(Mathf.Max(0, first * first - along * along));
            upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, target - lower.position) * lower.rotation;
        }

        static HumanPose RefineDrinkPose(TinyHeroBakeoff.Rig rig, HumanPoseHandler handler, HumanPose start,
            Vector3 hipPosition, Quaternion hipRotation, MeshRenderer mouth, Vector3 gripPosition,
            Quaternion gripRotation, Vector3 desiredAxis, Report report)
        {
            var allowed = new[] { "Right Shoulder Down-Up", "Right Shoulder Front-Back", "Right Arm Down-Up", "Right Arm Front-Back",
                "Right Arm Twist In-Out", "Right Forearm Stretch", "Right Forearm Twist In-Out", "Right Hand Down-Up", "Right Hand In-Out" };
            var indices = allowed.Select(name => Array.IndexOf(HumanTrait.MuscleName, name)).ToArray();
            if (indices.Any(i => i < 0)) throw new InvalidOperationException("Actual upper-limb muscle mapping changed.");
            HumanPose current = Clone(start);
            Func<HumanPose, float[]> features = pose =>
            {
                handler.SetHumanPose(ref pose);
                Bone(rig, HumanBodyBones.Hips).SetLocalPositionAndRotation(hipPosition, hipRotation);
                var actualHand = Bone(rig, HumanBodyBones.RightHand);
                Quaternion bottleRotation = actualHand.rotation * gripRotation;
                Vector3 axis = bottleRotation * Vector3.up;
                Vector3 spout = actualHand.TransformPoint(gripPosition) + axis * BottleSpoutHeight;
                Vector3 error = spout - DrawnBounds(mouth).center;
                // Keep a recognizable pouring orientation, rather than satisfy tip distance with
                // an upside-down/inside-head flask. Position dominates; 3cm weights axis residual.
                Vector3 orientation = (axis - desiredAxis) * .03f;
                return new[] { error.x, error.y, error.z, orientation.x, orientation.y, orientation.z };
            };
            float[] residual = features(current); current = Pose(handler);
            report.beforeSolveMouthDistance = new Vector3(residual[0], residual[1], residual[2]).magnitude;
            for (int iteration = 0; iteration < DrinkSolveIterations; iteration++)
            {
                report.drinkSolveIterations = iteration + 1;
                residual = features(current); current = Pose(handler);
                var jacobian = new float[6, indices.Length];
                for (int channel = 0; channel < indices.Length; channel++)
                {
                    int index = indices[channel]; HumanPose probe = Clone(current);
                    float value = Mathf.Clamp(probe.muscles[index] + .02f, -1, 1);
                    if (Mathf.Abs(value - probe.muscles[index]) < .001f) value = Mathf.Clamp(probe.muscles[index] - .02f, -1, 1);
                    float delta = value - probe.muscles[index]; if (Mathf.Abs(delta) < .0001f) continue;
                    probe.muscles[index] = value; float[] measured = features(probe);
                    for (int row = 0; row < 6; row++) jacobian[row, channel] = (measured[row] - residual[row]) / delta;
                }
                var normal = new float[6, 6];
                for (int row = 0; row < 6; row++) for (int column = 0; column < 6; column++)
                {
                    float value = row == column ? .00001f : 0;
                    for (int channel = 0; channel < indices.Length; channel++) value += jacobian[row, channel] * jacobian[column, channel];
                    normal[row, column] = value;
                }
                float[] solution = SolveLinear(normal, residual); var changes = new float[indices.Length];
                for (int channel = 0; channel < indices.Length; channel++)
                {
                    float value = 0; for (int row = 0; row < 6; row++) value -= jacobian[row, channel] * solution[row];
                    changes[channel] = Mathf.Clamp(value, -MuscleStepLimit, MuscleStepLimit);
                }
                float best = residual.Sum(value => value * value); HumanPose chosen = Clone(current);
                foreach (float fraction in new[] { 1f, .5f, .25f, .125f })
                {
                    HumanPose trial = Clone(current);
                    for (int channel = 0; channel < indices.Length; channel++)
                        trial.muscles[indices[channel]] = Mathf.Clamp(trial.muscles[indices[channel]] + changes[channel] * fraction, -1, 1);
                    float[] measured = features(trial); float cost = measured.Sum(value => value * value);
                    if (cost >= best) continue;
                    best = cost; chosen = Pose(handler);
                }
                current = chosen;
            }
            residual = features(current); current = Pose(handler);
            report.afterSolveMouthDistance = new Vector3(residual[0], residual[1], residual[2]).magnitude;
            report.afterSolveAxisAngle = Vector3.Angle(Bone(rig, HumanBodyBones.RightHand).rotation * gripRotation * Vector3.up, desiredAxis);
            return current;
        }

        static float[] SolveLinear(float[,] matrix, float[] vector)
        {
            int size = vector.Length; var augmented = new float[size, size + 1];
            for (int row = 0; row < size; row++)
            { for (int column = 0; column < size; column++) augmented[row, column] = matrix[row, column]; augmented[row, size] = vector[row]; }
            for (int pivot = 0; pivot < size; pivot++)
            {
                int best = pivot; for (int row = pivot + 1; row < size; row++) if (Mathf.Abs(augmented[row, pivot]) > Mathf.Abs(augmented[best, pivot])) best = row;
                if (Mathf.Abs(augmented[best, pivot]) < .000000001f) throw new InvalidOperationException("Singular bounded pose solve.");
                for (int column = pivot; column <= size; column++) { float swap = augmented[pivot, column]; augmented[pivot, column] = augmented[best, column]; augmented[best, column] = swap; }
                float divisor = augmented[pivot, pivot]; for (int column = pivot; column <= size; column++) augmented[pivot, column] /= divisor;
                for (int row = 0; row < size; row++) if (row != pivot)
                { float factor = augmented[row, pivot]; for (int column = pivot; column <= size; column++) augmented[row, column] -= factor * augmented[pivot, column]; }
            }
            return Enumerable.Range(0, size).Select(row => augmented[row, size]).ToArray();
        }

        static AnimationClip Bake(AnimationClip idle, float duration, Func<float, HumanPose> evaluate)
        {
            int steps = Mathf.CeilToInt(duration * SampleRate); var curves = new List<Keyframe>[HumanTrait.MuscleCount + 7];
            for (int i = 0; i < curves.Length; i++) curves[i] = new List<Keyframe>();
            Quaternion previous = Quaternion.identity;
            for (int i = 0; i <= steps; i++)
            {
                float time = duration * i / steps; HumanPose pose = evaluate(time);
                if (i > 0 && Quaternion.Dot(previous, pose.bodyRotation) < 0) pose.bodyRotation = new Quaternion(-pose.bodyRotation.x, -pose.bodyRotation.y, -pose.bodyRotation.z, -pose.bodyRotation.w);
                previous = pose.bodyRotation;
                var values = pose.muscles.Concat(new[] { pose.bodyPosition.x, pose.bodyPosition.y, pose.bodyPosition.z,
                    pose.bodyRotation.x, pose.bodyRotation.y, pose.bodyRotation.z, pose.bodyRotation.w }).ToArray();
                for (int j = 0; j < values.Length; j++)
                {
                    if (!Finite(values[j])) throw new InvalidOperationException("Nonfinite target pose, not accepted.");
                    curves[j].Add(new Keyframe(time, values[j]));
                }
            }
            var clip = new AnimationClip { name = "A_Review_Tiny_HealDrink", frameRate = SampleRate };
            for (int i = 0; i < curves.Length; i++)
            {
                string property = i < HumanTrait.MuscleCount ? TinyPoseCandidateReview.MuscleProperty(HumanTrait.MuscleName[i]) :
                    new[] { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" }[i - HumanTrait.MuscleCount];
                var curve = new AnimationCurve(curves[i].ToArray());
                for (int j = 0; j < curve.length; j++) { AnimationUtility.SetKeyLeftTangentMode(curve, j, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, j, AnimationUtility.TangentMode.Linear); }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), property), curve);
            }
            // Keep all unmapped cape/backpack/pad channels at the SAME reference Idle pose.
            // A muscles-only clip would silently discard those target-rig parts.
            foreach (var binding in AnimationUtility.GetCurveBindings(idle).Where(b => b.type != typeof(Animator)))
            {
                float value = AnimationUtility.GetEditorCurve(idle, binding).Evaluate(idle.length * .375f);
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, duration, value));
            }
            AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = false;
            settings.loopBlendPositionY = true; settings.loopBlendPositionXZ = true; settings.loopBlendOrientation = true;
            settings.keepOriginalPositionY = true; settings.keepOriginalPositionXZ = true; settings.keepOriginalOrientation = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings); return clip;
        }

        static GameObject CreateBottle(List<Material> materials, List<Mesh> meshes)
        {
            var go = new GameObject("OWN_ReviewFlask_18cm_NoAuthority", typeof(MeshFilter), typeof(MeshRenderer));
            var mesh = CreateBottleMeshForReview(); meshes.Add(mesh);
            var material = Material(new Color(.18f, .52f, .43f)); materials.Add(material);
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (go.GetComponentsInChildren<Collider>(true).Length != 0 || go.GetComponentsInChildren<MonoBehaviour>(true).Length != 0) throw new InvalidOperationException("Bottle is pure art.");
            return go;
        }

        // Caller owns the temporary mesh; exposed to exact triangle winding checks, not production use.
        public static Mesh CreateBottleMeshForReview()
        {
            var verts = new List<Vector3>(); var triangles = new List<int>(); const int sides = 8;
            // Grip reference is y=0; measured spout is +.11m. Total height is .18m.
            // Broad body below the finger grip improves silhouette; no scale/root compensation.
            var rings = new[] { new Vector2(-.07f, .029f), new Vector2(-.05f, .052f), new Vector2(.012f, .052f),
                new Vector2(.035f, .019f), new Vector2(BottleSpoutHeight, .019f) };
            foreach (var ring in rings) for (int i = 0; i < sides; i++)
            { float angle = i * Mathf.PI * 2 / sides; verts.Add(new Vector3(Mathf.Cos(angle) * ring.y, ring.x, Mathf.Sin(angle) * ring.y)); }
            for (int ring = 0; ring < rings.Length - 1; ring++) for (int i = 0; i < sides; i++)
            { int a = ring * sides + i, b = ring * sides + (i + 1) % sides, c = b + sides, d = a + sides; triangles.AddRange(new[] { a, d, c, a, c, b }); }
            for (int i = 1; i < sides - 1; i++) { triangles.AddRange(new[] { 0, i, i + 1 }); int top = (rings.Length - 1) * sides; triangles.AddRange(new[] { top, top + i + 1, top + i }); }
            var mesh = new Mesh { name = "OWN_ReviewFlaskMesh_18cm" }; mesh.SetVertices(verts); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        static float GroundClearance(GameObject character, Collider ground)
        {
            Bounds body = TinyHeroBakeoff.VertexBounds(character); RaycastHit hit;
            if (!ground.Raycast(new Ray(new Vector3(body.center.x, 4, body.center.z), Vector3.down), out hit, 8))
                throw new InvalidOperationException("Actual diagnostic floor missing beneath drawn body; no guessed ground.");
            return body.min.y - hit.point.y;
        }
        static Bounds DrawnBounds(MeshRenderer renderer)
        {
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh; var vertices = mesh.vertices; Bounds bounds = new Bounds(); bool first = true;
            foreach (int index in mesh.triangles.Distinct()) { Vector3 point = renderer.transform.TransformPoint(vertices[index]); if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point); }
            if (first) throw new InvalidOperationException("No drawn mouth geometry."); return bounds;
        }
        static Transform Bone(TinyHeroBakeoff.Rig rig, HumanBodyBones bone)
        { var value = rig.animator.GetBoneTransform(bone); if (value == null) throw new InvalidOperationException("Required target bone missing: " + bone); return value; }
        static HumanPose Pose(HumanPoseHandler handler) { var pose = new HumanPose(); handler.GetHumanPose(ref pose); return Clone(pose); }
        static HumanPose Clone(HumanPose pose) { pose.muscles = (float[])pose.muscles.Clone(); return pose; }
        static HumanPose Blend(HumanPose from, HumanPose to, float weight)
        {
            var result = new HumanPose { bodyPosition = Vector3.Lerp(from.bodyPosition, to.bodyPosition, weight),
                bodyRotation = Quaternion.Slerp(from.bodyRotation, to.bodyRotation, weight), muscles = new float[HumanTrait.MuscleCount] };
            for (int i = 0; i < result.muscles.Length; i++) result.muscles[i] = Mathf.Lerp(from.muscles[i], to.muscles[i], weight); return result;
        }
        static float Smooth(float weight) { return Mathf.SmoothStep(0, 1, Mathf.Clamp01(weight)); }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        static Material Material(Color color) { var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = color; material.SetFloat("_Smoothness", .15f); return material; }
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        static void EnsureFolder(string path) { if (AssetDatabase.IsValidFolder(path)) return; string parent = path.Substring(0, path.LastIndexOf('/')); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }
    }
}
