using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Isolated preview feasibility; Generic probe may author ONE owned copy, never an original/profile/production scene.</summary>
    public static partial class KayKitNativeCharacterReview
    {
        const string Project = "E:/unityproject/3DRPGdemo";
        const string Pack = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/03_Character_Kit/KayKit_Adventurers";
        const string Source = Pack + "/addons/kaykit_character_pack_adventures/Characters/fbx/";
        const string Valley = "Assets/_Game/Scenes/10_EmberValley.unity";
        const string Archive = "Builds/Delivery/ProjectEmberfall_0.9.6-presentation-20261003-r4.zip";
        const string ArchiveSha = "9EF0DB03551996772A066BE77C9DC1EE1BB8C8F743D196D36FE48C5D285E0860";
        const string JobPrefix = "Emberfall.KayKitNativeCharacterReview.";
        static readonly string[] Actors = { "Knight", "Barbarian" };
        static readonly string[] Motions = { "Idle", "Walking_A", "Running_A", "Hit_A", "Death_A", "Death_B",
            "Death_A_Pose", "Death_B_Pose", "Dodge_Forward", "1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Diagonal",
            "1H_Melee_Attack_Slice_Horizontal", "1H_Melee_Attack_Stab", "2H_Melee_Attack_Spin", "Throw", "Use_Item", "Block" };
        // Actual FBX Model subtype=LimbNode inventory; Rig and mesh-only nodes are deliberately NOT limbs.
        static readonly string[] Limbs = { "root", "hips", "spine", "chest", "upperarm.l", "lowerarm.l", "wrist.l", "hand.l", "handslot.l",
            "upperarm.r", "lowerarm.r", "wrist.r", "hand.r", "handslot.r", "head", "upperleg.l", "lowerleg.l", "foot.l", "toes.l",
            "upperleg.r", "lowerleg.r", "foot.r", "toes.r", "kneeIK.l", "control-toe-roll.l", "control-heel-roll.l", "control-foot-roll.l",
            "heelIK.l", "IK-foot.l", "IK-toe.l", "kneeIK.r", "control-toe-roll.r", "control-heel-roll.r", "control-foot-roll.r", "heelIK.r",
            "IK-foot.r", "IK-toe.r", "elbowIK.l", "handIK.l", "elbowIK.r", "handIK.r" };
        static bool pending;
        static bool assembledPending;
        static bool motionPending;
        static bool throwPending;
        static bool genericPending;
        const string GenericPrefab = "Assets/_Game/Art/Review/KayKitHero/knight-assembly-20261003-a1/Review_Knight_Hero.prefab";
        [Serializable] sealed class PropertyRow { public string path, type, value; }
        [Serializable] sealed class GenericGear
        {
            public string path, socket; public int canonicalAxis; public float canonicalMeshLength, worldLength;
            public V socketWorldScale, anchorWorld;
        }
        [Serializable] sealed class GenericSide
        {
            public string mode, clip, clipAsset, clipGuid, avatarGuid; public long clipLocalId, avatarLocalId;
            public float length, frameRate, idleBodyMinY, idleCapeMinY, idleGearMinY, idleLeftLegMinY, idleRightLegMinY,
                idleBodyHeight, idleAnchorWorldPositionDrift, idleAnchorWorldRotationDrift, minimumBodyY, minimumCapeY, minimumGearY, minimumLeftLegDelta, maximumLeftLegDelta,
                minimumRightLegDelta, maximumRightLegDelta, maximumAnchorWorldPositionDrift, maximumAnchorWorldRotationDrift;
            public bool baselineMeasured, denseCompleted, bodyNonpenetrating, capeNonpenetrating, gearNonpenetrating;
            public string[] missingBindings; public int objectBindings; public ThrowFrame[] frames; public GenericGear[] gear;
        }
        [Serializable] sealed class GenericReport
        {
            public string label, directory, status, error, startedUtc, finishedUtc, module, firstFatal, ownedFbx,
                originalImporterJson, copiedImporterBeforeJson, copiedImporterAfterJson, sourceImporterAfterJson, image,
                sourceFbxSha, copiedFbxShaBeforeImport, copiedFbxShaAfterImport, retainedCopyReport, retainedCopyReportSha;
            public string scope = "ONE byte-identical owned Knight FBX copy; only its animationType becomes Generic. Two instances of the SAME .75 pure visual candidate retain original meshes/material slots/gear. Same Manual Playables, rate 1, null Controller/AOC, FootIK/PlayableIK/root motion false; exactly whole native Idle and Throw at 120Hz including endpoints. Explicit Rig/... Transform paths, no Generic HumanBodyBones API. No Blender/export/remapping/crop/profile/runtime/input/NET/production/save-all changes.";
            public string floorScope = "Fixed world origin y=0, no lift or anchor reset. Body includes helmet, excludes cape/equipment. Exact Knight_LegLeft/Right drawn triangle minima are whole combined leg/boot geometry, NOT anatomical soles. Each mode uses its own fixed Idle t=0 baseline. Relative feasibility is separate from STRICT body/cape/gear worldY>=0; neither is whole ability, proportions/grip/knife/projectile/contact acceptance.";
            public string importerIdentityScope = "Both comparisons retain all authored take/import fields. ONLY actual importer-owned ImportedRoots[0] and ImportedTakeInfos[n].clip references use SELF/type/localID (clips also exact name); external references retain GUID/localID. ObjectReference children are Unity temporary implementation IDs, not persistent binding IDs; parent identity remains strict. AFTER type change the SAME owned GUID/localIDs/names must remain stable. Allowed post-type field differences ONLY m_AnimationType, m_RigImportErrors, m_AnimationImportWarnings; sourceAvatar remains null. Raw full importer JSON retained; no Avatar/CopyAvatar/root-node exception.";
            public bool sourceCopyHashSame, copiedImporterBeforeMatchesSource, importerOnlyAllowedChange, sourceImporterUnchanged,
                restContractCompared, restContractPassed, genericAvatarIdentityValid, bindingsPassed, dimensionsMeasured, dimensionRatiosPassed,
                denseCompleted, cpuSheetCompleted, anchorGatePassed, throwImprovementGatePassed, genericLegGatePassed,
                feasibilityPassed, absoluteNonpenetrationScreenPassed, fullAbilityAccepted = false, candidateInEnabledClosure,
                sourcesUnchanged, sourceMaterialMemoryUnchanged, loadedScenesUnchanged, renderSettingsUnchanged, enabledSceneClosureUnchanged, retainedCopyVerified,
                readOnlyStoppedImport, geometryFeasibilityPassed, ownedImporterMemoryUnchanged;
            public float visualScale = .75f, groundY = 0f, restPositionTolerance = 1e-6f, restScaleTolerance = 1e-6f,
                restAngleToleranceDegrees = .001f, maxAnchorWorldPositionAllowed = 1e-5f, maxAnchorWorldRotationAllowed = .001f,
                minimumThrowBodyImprovement = .04f, legDeltaMinimumAllowed = -.005f, legDeltaMaximumAllowed = .02f,
                dimensionRatioMinimum = .98f, dimensionRatioMaximum = 1.02f, bodyHeightRatio, throwBodyImprovement;
            public int copyCalls, batchCopyCalls, ownedReimports, transformCount, rendererCount; public string[] contractDifferences, importerDifferences,
                enabledSceneClosureBefore, enabledSceneClosureAfter; public float[] gearLengthRatios;
            public PropertyRow[] originalImporterProperties, copiedImporterBeforeProperties, copiedImporterAfterProperties;
            public GenericSide[] measurements; public FileRow[] protectedBefore, protectedAfter, expectedOwnedFiles; public MaterialChange[] materialChanges;
        }
        /// <summary>One owned copy, one Generic import. Contract failure stops before dense sampling; never retries settings.</summary>
        public static string RequestGenericFeasibilityReview(string uniqueLabel)
            => QueueGenericFeasibilityReview(uniqueLabel, null);

        /// <summary>Bounded comparator repair: reuse the unchanged Human copy from the exact pre-import failure, never copy again.</summary>
        public static string RequestGenericFeasibilityReviewFromRetainedCopy(string uniqueLabel, string previousReportPath)
            => QueueGenericFeasibilityReview(uniqueLabel, previousReportPath);

        /// <summary>Separate zero-import geometry assessment of a retained Generic output. Never grants the failed importer contract.</summary>
        public static string RequestRetainedGenericGeometryReview(string uniqueLabel, string stoppedImportReportPath)
            => QueueGenericFeasibilityReview(uniqueLabel, stoppedImportReportPath, true);

        static string QueueGenericFeasibilityReview(string uniqueLabel, string previousReportPath, bool readOnlyStoppedImport = false)
        {
            if (string.IsNullOrWhiteSpace(uniqueLabel) || !Regex.IsMatch(uniqueLabel, "^[A-Za-z0-9_-]{8,72}$")) throw new ArgumentException("Fresh ASCII label required.");
            string prefix = JobPrefix + "Generic.", key = prefix + uniqueLabel, prior = SessionState.GetString(key, "");
            if (prior.Length != 0) { if (!File.Exists(prior)) throw new IOException("Retained Generic probe missing."); return prior; }
            RequireIdle(); if (pending || assembledPending || motionPending || throwPending || genericPending) throw new InvalidOperationException("One preview job at a time.");
            foreach (string activeKey in new[] { JobPrefix + "active", JobPrefix + "Assembled.active", JobPrefix + "Motion.active", JobPrefix + "Throw.active", prefix + "active" })
            {
                string active = SessionState.GetString(activeKey, ""); if (!File.Exists(active)) continue;
                var old = JsonUtility.FromJson<GenericReport>(File.ReadAllText(active)); if (old.status == "queued" || old.status == "running") throw new InvalidOperationException("Inspect pending/interrupted preview: " + active);
            }
            GenericReport retained = previousReportPath == null ? null : ValidateRetainedHumanCopy(previousReportPath, readOnlyStoppedImport);
            string dir = Path.GetFullPath("Builds/ArtReview/kaykit-generic/" + uniqueLabel); if (Directory.Exists(dir)) throw new IOException("Unique evidence required."); Directory.CreateDirectory(dir);
            var report = new GenericReport { label = uniqueLabel, directory = dir, status = "queued", startedUtc = DateTime.UtcNow.ToString("O"), module = typeof(KayKitNativeCharacterReview).Assembly.ManifestModule.ModuleVersionId.ToString(), ownedFbx = "Assets/_Game/Art/Review/KayKitGeneric/" + uniqueLabel + "/Knight_Generic.fbx" };
            if (retained != null) { report.ownedFbx = retained.ownedFbx; report.retainedCopyReport = Path.GetFullPath(previousReportPath); report.retainedCopyReportSha = Hash(report.retainedCopyReport); }
            report.readOnlyStoppedImport = readOnlyStoppedImport;
            if (readOnlyStoppedImport) report.scope = "SEPARATE READ-ONLY geometry/motion assessment of exact retained stopped Generic output; zero CopyAsset, zero SaveAndReimport, all source AND owned FBX/meta bytes frozen. Old strict importer input contract remains FAILED/NOT GRANTED. Same .75 assembled clones/original meshes/materials/gear, native whole Idle+Throw, Manual graphs/rate1/null Controller/IK-rootmotion false. Strict rest/mesh/bindpose/bone-order first; no production/runtime/input/NET changes.";
            string record = dir + "/report.json"; File.WriteAllText(record, JsonUtility.ToJson(report, true), new UTF8Encoding(false)); SessionState.SetString(key, record); SessionState.SetString(prefix + "active", record); genericPending = true;
            double deadline = EditorApplication.timeSinceStartup + 180; EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < deadline) return; EditorApplication.update -= callback;
                try { RequireIdle(); report.status = "running"; File.WriteAllText(record, JsonUtility.ToJson(report, true)); CaptureGeneric(report); report.status = "completed"; }
                catch (Exception ex) { report.status = "failed"; report.error = ex.ToString(); if (report.firstFatal == null) report.firstFatal = ex.Message; Debug.LogException(ex); }
                finally { report.finishedUtc = DateTime.UtcNow.ToString("O"); File.WriteAllText(record, JsonUtility.ToJson(report, true), new UTF8Encoding(false)); genericPending = false; SessionState.EraseString(prefix + "active"); }
            };
            EditorApplication.update += callback; return record;
        }
        static GenericReport ValidateRetainedHumanCopy(string reportPath, bool convertedGeneric = false)
        {
            string full = Path.GetFullPath(reportPath); string root = Path.GetFullPath("Builds/ArtReview/kaykit-generic") + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) throw new IOException("Exact retained Generic report required.");
            var previous = JsonUtility.FromJson<GenericReport>(File.ReadAllText(full));
            if (previous == null || !Regex.IsMatch(previous.label ?? "", "^[A-Za-z0-9_-]{8,72}$") || full != Path.GetFullPath("Builds/ArtReview/kaykit-generic/" + previous.label + "/report.json") || !Regex.IsMatch(previous.ownedFbx ?? "", "^Assets/_Game/Art/Review/KayKitGeneric/[A-Za-z0-9_-]{8,72}/Knight_Generic\\.fbx$")) throw new IOException("Retained report/path/owned asset identity differs.");
            bool expectedStage = convertedGeneric
                ? previous.firstFatal == "Owned import changed fields beyond animationType and two derived diagnostic properties." && previous.copyCalls == 0 && previous.batchCopyCalls == 1 && previous.ownedReimports == 1 && previous.retainedCopyVerified && previous.copiedImporterBeforeMatchesSource && !string.IsNullOrEmpty(previous.retainedCopyReport) && File.Exists(previous.retainedCopyReport) && Hash(previous.retainedCopyReport) == previous.retainedCopyReportSha
                : previous.firstFatal == "Copy changed source importer properties; exact differences retained." && previous.copyCalls == 1 && previous.ownedReimports == 0 && previous.ownedFbx == "Assets/_Game/Art/Review/KayKitGeneric/" + previous.label + "/Knight_Generic.fbx";
            if (previous.status != "failed" || !expectedStage || previous.restContractCompared || previous.denseCompleted || previous.feasibilityPassed || !previous.sourceCopyHashSame || !previous.sourceImporterUnchanged || !previous.sourcesUnchanged || !previous.sourceMaterialMemoryUnchanged || !previous.loadedScenesUnchanged || !previous.renderSettingsUnchanged || !previous.enabledSceneClosureUnchanged || previous.candidateInEnabledClosure) throw new InvalidOperationException("Only exact preserved stopped import stage may be assessed; no prior gate acceptance.");
            if (Hash(Source + "Knight.fbx") != previous.sourceFbxSha || Hash(previous.ownedFbx) != previous.copiedFbxShaAfterImport) throw new IOException("Retained source/copy FBX bytes changed.");
            foreach (string path in new[] { Source + "Knight.fbx", Source + "Knight.fbx.meta" }) { var row = previous.protectedAfter.Single(r => r.path == path); if (!File.Exists(path) || Hash(path) != row.sha256 || new FileInfo(path).Length != row.bytes) throw new IOException("Retained original source/meta changed: " + path); }
            foreach (string path in new[] { previous.ownedFbx, previous.ownedFbx + ".meta" }) { var row = previous.expectedOwnedFiles.Single(r => r.path == path); if (!File.Exists(path) || Hash(path) != row.sha256 || new FileInfo(path).Length != row.bytes) throw new IOException("Retained owned FBX/meta changed: " + path); }
            var importer = (ModelImporter)AssetImporter.GetAtPath(previous.ownedFbx); if (importer == null || importer.animationType != (convertedGeneric ? ModelImporterAnimationType.Generic : ModelImporterAnimationType.Human) || importer.sourceAvatar != null) throw new InvalidOperationException("Exact preserved owned importer type required, no settings retry.");
            return previous;
        }
        static PropertyRow[] GenericImporterRows(ModelImporter importer)
        {
            var serialized = new SerializedObject(importer); var iterator = serialized.GetIterator(); var rows = new List<PropertyRow>();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = true;
                string value;
                if (iterator.propertyType == SerializedPropertyType.Generic) value = "struct|array=" + iterator.isArray;
                else if (iterator.propertyType == SerializedPropertyType.ObjectReference)
                {
                    // Parent GUID/localID is the persistent identity. Do not recurse into Unity's transient m_FileID implementation.
                    enterChildren = false;
                    var obj = iterator.objectReferenceValue; if (obj == null) value = "null";
                    else if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long id))
                    {
                        bool importedSelf = iterator.propertyPath == "m_ImportedRoots.Array.data[0]" || Regex.IsMatch(iterator.propertyPath, "^m_ImportedTakeInfos\\.Array\\.data\\[[0-9]+\\]\\.clip$");
                        value = importedSelf && AssetDatabase.GetAssetPath(obj) == importer.assetPath ? "SELF:" + obj.GetType().FullName + ":" + id + (obj is AnimationClip ? ":" + obj.name : "") : guid + ":" + id;
                    }
                    else throw new InvalidOperationException("Nonpersistent importer reference: " + iterator.propertyPath);
                }
                else if (iterator.propertyType == SerializedPropertyType.AnimationCurve) value = JsonUtility.ToJson(new GenericCurve { curve = iterator.animationCurveValue });
                else if (iterator.propertyType == SerializedPropertyType.ManagedReference) throw new InvalidOperationException("Unreviewed importer managed reference: " + iterator.propertyPath);
                else
                {
                    // Compare the complete string once; its internal character array is not a second authored field.
                    if (iterator.propertyType == SerializedPropertyType.String) enterChildren = false;
                    value = GenericPropertyValue(iterator);
                }
                rows.Add(new PropertyRow { path = iterator.propertyPath, type = iterator.propertyType.ToString(), value = value });
            }
            return rows.OrderBy(r => r.path, StringComparer.Ordinal).ToArray();
        }
        [Serializable] sealed class GenericCurve { public AnimationCurve curve; }
        static string GenericPropertyValue(SerializedProperty p)
        {
            // boxedValue preserves full enum/int/vector structs on this exact Unity 2022.3 API; binary value encoding avoids locale/float rounding.
            object value = p.propertyType == SerializedPropertyType.Enum || p.propertyType == SerializedPropertyType.ArraySize || p.propertyType == SerializedPropertyType.Character || p.propertyType == SerializedPropertyType.LayerMask || p.propertyType == SerializedPropertyType.FixedBufferSize ? (object)p.intValue : p.boxedValue;
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            {
                if (value is bool b) w.Write(b); else if (value is int i) w.Write(i); else if (value is long l) w.Write(l); else if (value is uint ui) w.Write(ui); else if (value is ulong ul) w.Write(ul); else if (value is char ch) w.Write(ch); else if (value is byte by) w.Write(by); else if (value is short sh) w.Write(sh); else if (value is ushort us) w.Write(us); else if (value is sbyte sb) w.Write(sb);
                else if (value is float f) w.Write(f); else if (value is double d) w.Write(d); else if (value is string s) w.Write(s);
                else if (value is Vector2 v2) { w.Write(v2.x); w.Write(v2.y); } else if (value is Vector3 v3) WriteVector(w, v3);
                else if (value is Vector4 v4) { w.Write(v4.x); w.Write(v4.y); w.Write(v4.z); w.Write(v4.w); }
                else if (value is Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
                else if (value is Color c) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
                else if (value is Rect rect) { w.Write(rect.x); w.Write(rect.y); w.Write(rect.width); w.Write(rect.height); }
                else if (value is Bounds bounds) { WriteVector(w, bounds.center); WriteVector(w, bounds.size); }
                else if (value is Vector2Int vi2) { w.Write(vi2.x); w.Write(vi2.y); } else if (value is Vector3Int vi3) { w.Write(vi3.x); w.Write(vi3.y); w.Write(vi3.z); }
                else if (value is RectInt ri) { w.Write(ri.x); w.Write(ri.y); w.Write(ri.width); w.Write(ri.height); }
                else if (value is BoundsInt bi) { w.Write(bi.position.x); w.Write(bi.position.y); w.Write(bi.position.z); w.Write(bi.size.x); w.Write(bi.size.y); w.Write(bi.size.z); }
                else if (value is Hash128 hash) w.Write(hash.ToString());
                else throw new InvalidOperationException("Unreviewed importer leaf type " + p.propertyType + " at " + p.propertyPath + ": " + (value == null ? "null" : value.GetType().FullName));
                w.Flush(); return Convert.ToBase64String(stream.ToArray());
            }
        }
        static string[] GenericImporterDiff(PropertyRow[] a, PropertyRow[] b, bool ownedChange)
        {
            var left = a.ToDictionary(r => r.path, StringComparer.Ordinal); var right = b.ToDictionary(r => r.path, StringComparer.Ordinal); var differences = new List<string>();
            foreach (string path in left.Keys.Concat(right.Keys).Distinct().OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!left.TryGetValue(path, out var x) || !right.TryGetValue(path, out var y)) { differences.Add(path + ": property missing"); continue; }
                if (x.type == y.type && x.value == y.value) continue;
                if (ownedChange && x.type == y.type && (path == "m_AnimationType" || path == "m_RigImportErrors" || path == "m_AnimationImportWarnings")) continue;
                differences.Add(path + ": " + x.type + "/" + x.value + " -> " + y.type + "/" + y.value);
            }
            return differences.ToArray();
        }
        static void GenericFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return; if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/_Game/Art/Review/KayKitGeneric", StringComparison.Ordinal)) throw new IOException("Missing fixed existing review parent; no broad folder creation."); string parent = Path.GetDirectoryName(path).Replace('\\', '/'); GenericFolder(parent);
            if (AssetDatabase.CreateFolder(parent, Path.GetFileName(path)).Length == 0) throw new IOException("Cannot create owned candidate folder: " + path);
        }
        static void WriteVector(BinaryWriter writer, Vector3 v) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
        static string GenericMeshHash(Mesh mesh)
        {
            if (mesh == null) throw new InvalidOperationException("Missing contract mesh.");
            using (var stream = new MemoryStream()) using (var w = new BinaryWriter(stream))
            {
                w.Write(mesh.vertexCount); w.Write((int)mesh.indexFormat); w.Write(mesh.subMeshCount);
                foreach (var a in mesh.GetVertexAttributes()) { w.Write((int)a.attribute); w.Write((int)a.format); w.Write(a.dimension); w.Write(a.stream); }
                foreach (var v in mesh.vertices) WriteVector(w, v); w.Write(mesh.normals.Length); foreach (var v in mesh.normals) WriteVector(w, v);
                w.Write(mesh.tangents.Length); foreach (var v in mesh.tangents) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); }
                w.Write(mesh.colors.Length); foreach (var v in mesh.colors) { w.Write(v.r); w.Write(v.g); w.Write(v.b); w.Write(v.a); }
                for (int channel = 0; channel < 8; channel++) { var uv = new List<Vector4>(); mesh.GetUVs(channel, uv); w.Write(channel); w.Write(uv.Count); foreach (var v in uv) { w.Write(v.x); w.Write(v.y); w.Write(v.z); w.Write(v.w); } }
                for (int sub = 0; sub < mesh.subMeshCount; sub++) { w.Write((int)mesh.GetTopology(sub)); w.Write(mesh.GetBaseVertex(sub)); var indices = mesh.GetIndices(sub, false); w.Write(indices.Length); foreach (int i in indices) w.Write(i); }
                // Mesh returns non-owning NativeArray views; never Dispose() its source-owned bone buffers.
                var counts = mesh.GetBonesPerVertex(); w.Write(counts.Length); foreach (byte count in counts) w.Write(count);
                var weights = mesh.GetAllBoneWeights(); w.Write(weights.Length); foreach (var weight in weights) { w.Write(weight.boneIndex); w.Write(weight.weight); }
                w.Write(mesh.bindposes.Length); foreach (var matrix in mesh.bindposes) for (int i = 0; i < 16; i++) w.Write(matrix[i]);
                w.Write(mesh.blendShapeCount); for (int shape = 0; shape < mesh.blendShapeCount; shape++) { w.Write(mesh.GetBlendShapeName(shape)); int frames = mesh.GetBlendShapeFrameCount(shape); w.Write(frames); for (int frame = 0; frame < frames; frame++) { w.Write(mesh.GetBlendShapeFrameWeight(shape, frame)); var v = new Vector3[mesh.vertexCount]; var n = new Vector3[mesh.vertexCount]; var t = new Vector3[mesh.vertexCount]; mesh.GetBlendShapeFrameVertices(shape, frame, v, n, t); foreach (var x in v) WriteVector(w, x); foreach (var x in n) WriteVector(w, x); foreach (var x in t) WriteVector(w, x); } }
                w.Flush(); using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "");
            }
        }
        static string GenericBonePath(Transform t, Transform root) => t == null ? "<NULL>" : (t.IsChildOf(root) || t == root ? PathOf(t, root) : throw new InvalidOperationException("Contract bone outside model."));
        static string[] GenericRestDiff(GameObject a, GameObject b, GenericReport report)
        {
            var differences = new List<string>(); var left = a.GetComponentsInChildren<Transform>(true).ToDictionary(t => PathOf(t, a.transform), StringComparer.Ordinal); var right = b.GetComponentsInChildren<Transform>(true).ToDictionary(t => PathOf(t, b.transform), StringComparer.Ordinal); report.transformCount = left.Count;
            foreach (string path in left.Keys.Concat(right.Keys).Distinct().OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!left.TryGetValue(path, out var x) || !right.TryGetValue(path, out var y)) { differences.Add("transform missing: " + path); continue; }
                string px = x == a.transform ? "<ROOT>" : PathOf(x.parent, a.transform), py = y == b.transform ? "<ROOT>" : PathOf(y.parent, b.transform);
                float p = Vector3.Distance(x.localPosition, y.localPosition), s = Vector3.Distance(x.localScale, y.localScale), q = Quaternion.Angle(x.localRotation, y.localRotation);
                if (!GenericFiniteTransform(x) || !GenericFiniteTransform(y) || !Finite(p) || !Finite(s) || !Finite(q) || p > report.restPositionTolerance || s > report.restScaleTolerance || q > report.restAngleToleranceDegrees || px != py || x.GetSiblingIndex() != y.GetSiblingIndex() || x.gameObject.activeSelf != y.gameObject.activeSelf) differences.Add("rest TRS/parent/sibling/active: " + path + " pos=" + p + " scale=" + s + " angle=" + q);
            }
            var ra = a.GetComponentsInChildren<Renderer>(true).ToDictionary(r => PathOf(r.transform, a.transform), StringComparer.Ordinal); var rb = b.GetComponentsInChildren<Renderer>(true).ToDictionary(r => PathOf(r.transform, b.transform), StringComparer.Ordinal); report.rendererCount = ra.Count;
            foreach (string path in ra.Keys.Concat(rb.Keys).Distinct().OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!ra.TryGetValue(path, out var x) || !rb.TryGetValue(path, out var y)) { differences.Add("renderer missing: " + path); continue; }
                if (x.GetType() != y.GetType() || x.enabled != y.enabled || x.sharedMaterials.Length != y.sharedMaterials.Length) differences.Add("renderer type/enabled/slots: " + path);
                var mx = x is SkinnedMeshRenderer sx ? sx.sharedMesh : x.GetComponent<MeshFilter>()?.sharedMesh; var my = y is SkinnedMeshRenderer sy ? sy.sharedMesh : y.GetComponent<MeshFilter>()?.sharedMesh;
                if (mx == null || my == null || GenericMeshHash(mx) != GenericMeshHash(my)) differences.Add("mesh geometry/UV/indices/weights/bindposes: " + path);
                if (x is SkinnedMeshRenderer ax && y is SkinnedMeshRenderer by && (!ax.bones.Select(t => GenericBonePath(t, a.transform)).SequenceEqual(by.bones.Select(t => GenericBonePath(t, b.transform))) || GenericBonePath(ax.rootBone, a.transform) != GenericBonePath(by.rootBone, b.transform))) differences.Add("ordered skin bones/rootBone: " + path);
            }
            return differences.ToArray();
        }
        static void GenericFatal(GenericReport report, string message) { if (report.firstFatal == null) report.firstFatal = message; throw new InvalidOperationException(message); }
        static void RequireNoRetainedGeneric()
        {
            string active = SessionState.GetString(JobPrefix + "Generic.active", ""); if (!File.Exists(active)) return;
            var report = JsonUtility.FromJson<GenericReport>(File.ReadAllText(active)); if (report.status == "queued" || report.status == "running") throw new InvalidOperationException("Inspect retained Generic job before another preview: " + active);
        }
        static bool GenericFiniteTransform(Transform t) => new[] { t.localPosition.x, t.localPosition.y, t.localPosition.z, t.localScale.x, t.localScale.y, t.localScale.z, t.localRotation.x, t.localRotation.y, t.localRotation.z, t.localRotation.w }.All(Finite);
        static void CaptureGeneric(GenericReport report)
        {
            Scene[] scenes = OriginalScenes(); string sceneMemory = SceneMemory(scenes), settings = RenderState(); report.enabledSceneClosureBefore = ThrowEntryClosure();
            var extra = AssetDatabase.GetDependencies(GenericPrefab, true).Concat(new[] { GenericPrefab, "Packages/manifest.json", "Packages/packages-lock.json" }).Concat(report.enabledSceneClosureBefore).Concat(report.readOnlyStoppedImport ? new[] { report.ownedFbx } : Array.Empty<string>()).Distinct().SelectMany(p => new[] { p, p + ".meta" }).Where(File.Exists);
            var frozen = Protected().Concat(extra.Select(p => new FileRow { path = p, sha256 = Hash(p), bytes = new FileInfo(p).Length })).GroupBy(f => f.path, StringComparer.Ordinal).Select(g => g.First()).OrderBy(f => f.path, StringComparer.Ordinal).ToArray(); report.protectedBefore = frozen;
            var watches = MaterialMemory(frozen).Select(p => { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(p.Key, out string guid, out long id); return new MaterialWatch { original = p.Key, row = new MaterialChange { name = p.Key.name, asset = AssetDatabase.GetAssetPath(p.Key), guid = guid, localId = id, originalInstanceId = p.Key.GetInstanceID(), beforeJson = p.Value } }; }).ToArray();
            var sides = new List<GenericSide>(); ModelImporter originalImporter = (ModelImporter)AssetImporter.GetAtPath(Source + "Knight.fbx"); report.originalImporterJson = EditorJsonUtility.ToJson(originalImporter); report.originalImporterProperties = GenericImporterRows(originalImporter);
            try
            {
                if (originalImporter.animationType != ModelImporterAnimationType.Human || originalImporter.sourceAvatar != null) GenericFatal(report, "Original Human importer with null sourceAvatar required.");
                if (string.IsNullOrEmpty(report.retainedCopyReport))
                {
                    string folder = Path.GetDirectoryName(report.ownedFbx).Replace('\\', '/'); if (Directory.Exists(folder) || File.Exists(report.ownedFbx) || File.Exists(report.ownedFbx + ".meta")) GenericFatal(report, "Fresh owned folder required; never overwrite existing candidate."); GenericFolder(folder);
                    report.copyCalls++; report.batchCopyCalls = report.copyCalls; if (!AssetDatabase.CopyAsset(Source + "Knight.fbx", report.ownedFbx)) GenericFatal(report, "Single owned CopyAsset failed.");
                }
                else
                {
                    if (Hash(report.retainedCopyReport) != report.retainedCopyReportSha) GenericFatal(report, "Retained failed report bytes changed after queue.");
                    var previous = ValidateRetainedHumanCopy(report.retainedCopyReport, report.readOnlyStoppedImport); if (previous.ownedFbx != report.ownedFbx) GenericFatal(report, "Retained owned identity changed."); report.retainedCopyVerified = true; report.batchCopyCalls = report.readOnlyStoppedImport ? previous.batchCopyCalls : previous.copyCalls;
                }
                report.sourceFbxSha = Hash(Source + "Knight.fbx"); report.copiedFbxShaBeforeImport = Hash(report.ownedFbx); report.sourceCopyHashSame = report.sourceFbxSha == report.copiedFbxShaBeforeImport; if (!report.sourceCopyHashSame) GenericFatal(report, "Copied FBX bytes differ.");
                var copied = (ModelImporter)AssetImporter.GetAtPath(report.ownedFbx); report.copiedImporterBeforeJson = EditorJsonUtility.ToJson(copied); report.copiedImporterBeforeProperties = GenericImporterRows(copied);
                if (!report.readOnlyStoppedImport)
                {
                report.importerDifferences = GenericImporterDiff(report.originalImporterProperties, report.copiedImporterBeforeProperties, false); report.copiedImporterBeforeMatchesSource = report.importerDifferences.Length == 0; if (!report.copiedImporterBeforeMatchesSource) GenericFatal(report, "Copy changed source importer properties; exact differences retained.");
                copied.animationType = ModelImporterAnimationType.Generic; report.ownedReimports++; copied.SaveAndReimport(); copied = (ModelImporter)AssetImporter.GetAtPath(report.ownedFbx);
                report.copiedImporterAfterJson = EditorJsonUtility.ToJson(copied); report.copiedImporterAfterProperties = GenericImporterRows(copied); report.importerDifferences = GenericImporterDiff(report.copiedImporterBeforeProperties, report.copiedImporterAfterProperties, true);
                report.copiedFbxShaAfterImport = Hash(report.ownedFbx); report.sourceCopyHashSame &= report.sourceFbxSha == report.copiedFbxShaAfterImport; if (!report.sourceCopyHashSame) GenericFatal(report, "Owned reimport changed copied FBX bytes.");
                report.importerOnlyAllowedChange = report.importerDifferences.Length == 0 && copied.animationType == ModelImporterAnimationType.Generic && copied.sourceAvatar == null && copied.avatarSetup == originalImporter.avatarSetup && copied.animationCompression == originalImporter.animationCompression && copied.globalScale == originalImporter.globalScale && copied.motionNodeName == originalImporter.motionNodeName;
                if (!report.importerOnlyAllowedChange) GenericFatal(report, "Owned import changed fields beyond animationType and two derived diagnostic properties.");
                }
                using (var stage = new Stage())
                try
                {
                    var originalModel = stage.preview.InstantiatePrefabInScene(Required<GameObject>(Source + "Knight.fbx")); var genericModel = stage.preview.InstantiatePrefabInScene(Required<GameObject>(report.ownedFbx));
                    originalModel.transform.SetParent(stage.New("OriginalRest_Identity").transform, false); genericModel.transform.SetParent(stage.New("GenericRest_Identity").transform, false);
                    report.contractDifferences = GenericRestDiff(originalModel, genericModel, report); report.restContractCompared = true; report.restContractPassed = report.contractDifferences.Length == 0;
                    if (!report.restContractPassed) GenericFatal(report, "Generic imported rest/mesh/skin contract differs; dense gate NOT reached.");
                    var originalAvatar = originalModel.GetComponentInChildren<Animator>(true)?.avatar; var genericAvatar = genericModel.GetComponentInChildren<Animator>(true)?.avatar;
                    report.genericAvatarIdentityValid = originalAvatar != null && originalAvatar.isValid && originalAvatar.isHuman && genericAvatar != null && genericAvatar.isValid && !genericAvatar.isHuman && genericAvatar != originalAvatar && AssetDatabase.GetAssetPath(genericAvatar) == report.ownedFbx;
                    if (!report.genericAvatarIdentityValid) GenericFatal(report, "New valid nonhuman Generic model Avatar identity required.");
                    originalModel.SetActive(false); genericModel.SetActive(false);
                    var frameA = stage.New("Human_IdentityActorFrame"); var frameB = stage.New("Generic_IdentityActorFrame"); var a = stage.preview.InstantiatePrefabInScene(Required<GameObject>(GenericPrefab)); var b = stage.preview.InstantiatePrefabInScene(Required<GameObject>(GenericPrefab)); a.transform.SetParent(frameA.transform, false); b.transform.SetParent(frameB.transform, false); stage.preview.camera.scene = a.scene;
                    foreach (var candidate in new[] { a, b }) if (candidate.transform.localScale != Vector3.one * .75f || candidate.transform.localPosition != Vector3.zero || candidate.transform.localRotation != Quaternion.identity || candidate.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 || candidate.GetComponentsInChildren<Collider>(true).Length != 0) GenericFatal(report, "Exact .75 origin pure visual candidate required.");
                    var aa = a.GetComponentInChildren<Animator>(true); var ab = b.GetComponentInChildren<Animator>(true); if (aa == null || ab == null || aa.avatar != originalAvatar || ab.avatar != originalAvatar) GenericFatal(report, "Same original candidate and Avatar wiring required before replacement.");
                    GenericSameCandidate(a, b); ab.avatar = genericAvatar;
                    var originalClips = AssetDatabase.LoadAllAssetsAtPath(Source + "Knight.fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview", StringComparison.Ordinal)).ToDictionary(c => c.name, StringComparer.Ordinal); var genericClips = AssetDatabase.LoadAllAssetsAtPath(report.ownedFbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview", StringComparison.Ordinal)).ToDictionary(c => c.name, StringComparer.Ordinal);
                    foreach (string clipName in new[] { "Idle", "Throw" })
                    {
                        var ac = originalClips[clipName]; var bc = genericClips[clipName];
                        if (!ac.isHumanMotion || bc.isHumanMotion || ac.legacy || bc.legacy || !Finite(ac.length) || ac.length <= 0 || ac.length > 10 || ac.length != bc.length || !Finite(ac.frameRate) || ac.frameRate <= 0 || ac.frameRate != bc.frameRate) GenericFatal(report, "Whole finite matching native clip timeline required: " + clipName);
                        foreach (var pair in new[] { new { animator = aa, clip = ac, mode = "Human" }, new { animator = ab, clip = bc, mode = "Generic" } })
                        {
                            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(pair.clip, out string guid, out long id); AssetDatabase.TryGetGUIDAndLocalFileIdentifier(pair.animator.avatar, out string avatarGuid, out long avatarId);
                            var side = new GenericSide { mode = pair.mode, clip = clipName, clipAsset = AssetDatabase.GetAssetPath(pair.clip), clipGuid = guid, clipLocalId = id, avatarGuid = avatarGuid, avatarLocalId = avatarId, length = pair.clip.length, frameRate = pair.clip.frameRate, missingBindings = MissingBindings(pair.clip, pair.animator.gameObject), objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(pair.clip).Length }; sides.Add(side);
                            if (side.missingBindings.Length != 0 || side.objectBindings != 0) GenericFatal(report, "Nonzero actual transform/object binding gate: " + side.mode + "/" + clipName);
                        }
                    }
                    report.bindingsPassed = sides.Count == 4; stage.Materials(a); stage.Materials(b); stage.Floor(Vector3.zero, new Vector3(8, .02f, 6));
                    using (var ra = new Rig(aa)) using (var rb = new Rig(ab)) using (var da = new Drawn(a)) using (var db = new Drawn(b))
                    {
                        foreach (var item in new[] { new { candidate = a, actorFrame = frameA.transform, animator = aa, rig = ra, drawn = da, clips = originalClips, mode = "Human" }, new { candidate = b, actorFrame = frameB.transform, animator = ab, rig = rb, drawn = db, clips = genericClips, mode = "Generic" } })
                        {
                            item.rig.Start(item.clips["Idle"]); item.rig.Pose(0); var idleFloors = item.drawn.PerRendererFloors(0); ThrowMinima(idleFloors, out float body, out float cape, out float gear, out float left, out float right); float height = item.drawn.BodyBounds().size.y; var equipment = GenericGearDimensions(item.candidate, item.animator);
                            if (!Finite(height) || height <= 0) GenericFatal(report, "Finite positive actual Idle body height required.");
                            foreach (var side in sides.Where(s => s.mode == item.mode)) { side.idleBodyMinY = body; side.idleCapeMinY = cape; side.idleGearMinY = gear; side.idleLeftLegMinY = left; side.idleRightLegMinY = right; side.idleBodyHeight = height; side.idleAnchorWorldPositionDrift = item.rig.WorldPositionDrift; side.idleAnchorWorldRotationDrift = item.rig.WorldRotationDrift; side.gear = equipment; side.baselineMeasured = true; }
                        }
                        var human = sides.Single(s => s.mode == "Human" && s.clip == "Idle"); var generic = sides.Single(s => s.mode == "Generic" && s.clip == "Idle"); report.dimensionsMeasured = true; report.bodyHeightRatio = generic.idleBodyHeight / human.idleBodyHeight;
                        report.gearLengthRatios = human.gear.Select(g => generic.gear.Single(x => x.path == g.path).worldLength / g.worldLength).ToArray(); report.dimensionRatiosPassed = new[] { report.bodyHeightRatio }.Concat(report.gearLengthRatios).All(v => Finite(v) && v >= report.dimensionRatioMinimum && v <= report.dimensionRatioMaximum);
                        if (!report.dimensionRatiosPassed) GenericFatal(report, "Idle body/each canonical world gear dimension ratio gate failed; dense NOT reached.");
                        foreach (var side in sides) GenericSample(side, side.mode == "Human" ? aa : ab, side.mode == "Human" ? frameA.transform : frameB.transform, side.mode == "Human" ? ra : rb, side.mode == "Human" ? da : db, side.mode == "Human" ? originalClips[side.clip] : genericClips[side.clip]);
                        report.denseCompleted = sides.All(s => s.denseCompleted); var ht = sides.Single(s => s.mode == "Human" && s.clip == "Throw"); var gt = sides.Single(s => s.mode == "Generic" && s.clip == "Throw"); report.throwBodyImprovement = gt.minimumBodyY - ht.minimumBodyY; report.throwImprovementGatePassed = report.denseCompleted && report.throwBodyImprovement >= report.minimumThrowBodyImprovement;
                        report.genericLegGatePassed = report.denseCompleted && sides.Where(s => s.mode == "Generic").All(s => s.minimumLeftLegDelta >= report.legDeltaMinimumAllowed && s.maximumLeftLegDelta <= report.legDeltaMaximumAllowed && s.minimumRightLegDelta >= report.legDeltaMinimumAllowed && s.maximumRightLegDelta <= report.legDeltaMaximumAllowed);
                        report.anchorGatePassed = report.denseCompleted && sides.All(s => Finite(s.idleAnchorWorldPositionDrift) && Finite(s.idleAnchorWorldRotationDrift) && s.idleAnchorWorldPositionDrift <= report.maxAnchorWorldPositionAllowed && s.idleAnchorWorldRotationDrift <= report.maxAnchorWorldRotationAllowed && s.maximumAnchorWorldPositionDrift <= report.maxAnchorWorldPositionAllowed && s.maximumAnchorWorldRotationDrift <= report.maxAnchorWorldRotationAllowed);
                        report.absoluteNonpenetrationScreenPassed = report.denseCompleted && sides.Where(s => s.mode == "Generic").All(s => s.bodyNonpenetrating && s.capeNonpenetrating && s.gearNonpenetrating);
                        var sheet = new Texture2D(2000, 1200, TextureFormat.RGB24, false); try { stage.Camera(new Vector3(3.5f, 2.2f, 6.2f), new Vector3(0, .9f, 0), 38f); for (int row = 0; row < 2; row++) { a.SetActive(row == 0); b.SetActive(row == 1); var rig = row == 0 ? ra : rb; var clip = row == 0 ? originalClips["Throw"] : genericClips["Throw"]; rig.Start(clip); for (int column = 0; column < 5; column++) { rig.Pose(clip.length * column / 4f); var image = stage.Image(400, 600); try { sheet.SetPixels(column * 400, (1 - row) * 600, 400, 600, image.GetPixels()); } finally { Object.DestroyImmediate(image); } } } sheet.Apply(); report.image = "01-human-top-generic-bottom-native-throw.png"; File.WriteAllBytes(report.directory + "/" + report.image, sheet.EncodeToPNG()); report.cpuSheetCompleted = true; } finally { Object.DestroyImmediate(sheet); }
                    }
                }
                finally { foreach (var watch in watches) { watch.row.beforeCleanupReached = true; watch.row.aliveBeforeCleanup = watch.original != null; watch.row.beforeCleanupJson = watch.original == null ? null : EditorJsonUtility.ToJson(watch.original); } }
            }
            finally
            {
                report.measurements = sides.ToArray(); report.sourceImporterAfterJson = EditorJsonUtility.ToJson(originalImporter); report.sourceImporterUnchanged = report.originalImporterJson == report.sourceImporterAfterJson;
                if (report.readOnlyStoppedImport && report.retainedCopyVerified)
                {
                    var currentOwned = (ModelImporter)AssetImporter.GetAtPath(report.ownedFbx); report.copiedImporterAfterJson = EditorJsonUtility.ToJson(currentOwned); report.copiedImporterAfterProperties = GenericImporterRows(currentOwned);
                    report.importerDifferences = GenericImporterDiff(report.copiedImporterBeforeProperties, report.copiedImporterAfterProperties, false);
                    report.ownedImporterMemoryUnchanged = report.copiedImporterBeforeJson == report.copiedImporterAfterJson && report.importerDifferences.Length == 0;
                }
                if (report.copyCalls != 0 || report.retainedCopyVerified) { report.copiedFbxShaAfterImport = File.Exists(report.ownedFbx) ? Hash(report.ownedFbx) : "MISSING"; report.sourceCopyHashSame &= report.sourceFbxSha == report.copiedFbxShaAfterImport; }
                report.protectedAfter = frozen.Select(f => new FileRow { path = f.path, sha256 = File.Exists(f.path) ? Hash(f.path) : "MISSING", bytes = File.Exists(f.path) ? new FileInfo(f.path).Length : -1 }).ToArray(); report.sourcesUnchanged = frozen.Zip(report.protectedAfter, (a, b) => a.path == b.path && a.sha256 == b.sha256 && a.bytes == b.bytes).All(b => b);
                foreach (var watch in watches) { watch.row.aliveAfterCleanup = watch.original != null; watch.row.afterCleanupJson = watch.original == null ? null : EditorJsonUtility.ToJson(watch.original); } report.materialChanges = watches.Where(w => !w.row.aliveAfterCleanup || w.row.beforeJson != w.row.afterCleanupJson || (w.row.beforeCleanupReached && (!w.row.aliveBeforeCleanup || w.row.beforeJson != w.row.beforeCleanupJson))).Select(w => w.row).ToArray(); report.sourceMaterialMemoryUnchanged = report.materialChanges.Length == 0;
                report.loadedScenesUnchanged = sceneMemory == SceneMemory(scenes) && SameScenes(scenes); report.renderSettingsUnchanged = settings == RenderState(); report.enabledSceneClosureAfter = ThrowEntryClosure(); report.enabledSceneClosureUnchanged = report.enabledSceneClosureBefore.SequenceEqual(report.enabledSceneClosureAfter); report.candidateInEnabledClosure = report.enabledSceneClosureAfter.Any(p => p.StartsWith("Assets/_Game/Art/Review/KayKitGeneric/", StringComparison.Ordinal) || p == GenericPrefab);
                string ownedFolder = Path.GetDirectoryName(report.ownedFbx).Replace('\\', '/'); report.expectedOwnedFiles = Directory.Exists(ownedFolder) ? Directory.GetFiles(ownedFolder, "*", SearchOption.AllDirectories).Concat(new[] { ownedFolder + ".meta" }).Where(File.Exists).OrderBy(p => p, StringComparer.Ordinal).Select(p => new FileRow { path = p.Replace('\\', '/'), sha256 = Hash(p), bytes = new FileInfo(p).Length }).ToArray() : Array.Empty<FileRow>();
                bool guards = report.sourceImporterUnchanged && report.sourcesUnchanged && report.sourceMaterialMemoryUnchanged && report.loadedScenesUnchanged && report.renderSettingsUnchanged && report.enabledSceneClosureUnchanged && !report.candidateInEnabledClosure && (!report.readOnlyStoppedImport || report.ownedImporterMemoryUnchanged);
                bool geometryGates = guards && report.sourceCopyHashSame && report.restContractPassed && report.genericAvatarIdentityValid && report.bindingsPassed && report.dimensionRatiosPassed && report.denseCompleted && report.cpuSheetCompleted && report.anchorGatePassed && report.throwImprovementGatePassed && report.genericLegGatePassed;
                report.feasibilityPassed = !report.readOnlyStoppedImport && geometryGates && report.batchCopyCalls == 1 && (report.copyCalls == 1 || (report.copyCalls == 0 && report.retainedCopyVerified)) && report.ownedReimports == 1 && report.copiedImporterBeforeMatchesSource && report.importerOnlyAllowedChange;
                report.geometryFeasibilityPassed = report.readOnlyStoppedImport && geometryGates && report.batchCopyCalls == 1 && report.copyCalls == 0 && report.ownedReimports == 0 && report.retainedCopyVerified;
                if (!guards) GenericFatal(report, "Generic probe source/material/scene/settings/entry closure protection failed; no unknown restoration.");
            }
        }
        static void GenericSameCandidate(GameObject a, GameObject b)
        {
            var ra = a.GetComponentsInChildren<Renderer>(true); var rb = b.GetComponentsInChildren<Renderer>(true); if (ra.Length != rb.Length) throw new InvalidOperationException("Same original candidate renderer inventory required.");
            for (int i = 0; i < ra.Length; i++) { var x = ra[i]; var y = rb[i]; Mesh mx = x is SkinnedMeshRenderer sx ? sx.sharedMesh : x.GetComponent<MeshFilter>()?.sharedMesh, my = y is SkinnedMeshRenderer sy ? sy.sharedMesh : y.GetComponent<MeshFilter>()?.sharedMesh; if (PathOf(x.transform, a.transform) != PathOf(y.transform, b.transform) || x.GetType() != y.GetType() || x.enabled != y.enabled || x.gameObject.activeSelf != y.gameObject.activeSelf || mx != my || !x.sharedMaterials.SequenceEqual(y.sharedMaterials)) throw new InvalidOperationException("Same ORIGINAL candidate geometry/materials/gear required."); }
        }
        static GenericGear[] GenericGearDimensions(GameObject candidate, Animator animator)
        {
            string right = "Rig/root/hips/spine/chest/upperarm.r/lowerarm.r/wrist.r/hand.r/handslot.r", left = "Rig/root/hips/spine/chest/upperarm.l/lowerarm.l/wrist.l/hand.l/handslot.l";
            return new[] { right + "/Review_SwordSocket/Review_1H_Sword", left + "/Review_ShieldSocket/Review_Badge_Shield" }.Select(path => { var t = animator.transform.Find(path); var mesh = t == null ? null : t.GetComponent<MeshFilter>()?.sharedMesh; if (mesh == null || !t.GetComponent<Renderer>().enabled || !t.gameObject.activeInHierarchy) throw new InvalidOperationException("Actual canonical candidate gear missing: " + path); var size = mesh.bounds.size; int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2; var endpoint = Vector3.zero; endpoint[axis] = size[axis]; float length = Vector3.Distance(t.TransformPoint(mesh.bounds.center), t.TransformPoint(mesh.bounds.center + endpoint)); var scale = t.parent.lossyScale; var anchor = t.parent.position; if (!Finite(length) || length <= 0 || new[] { scale.x, scale.y, scale.z, anchor.x, anchor.y, anchor.z }.Any(v => !Finite(v))) throw new InvalidOperationException("Invalid canonical world gear dimension."); return new GenericGear { path = PathOf(t, candidate.transform), socket = PathOf(t.parent, candidate.transform), canonicalAxis = axis, canonicalMeshLength = size[axis], worldLength = length, socketWorldScale = new V(scale), anchorWorld = new V(anchor) }; }).ToArray();
        }
        static void GenericSample(GenericSide side, Animator animator, Transform actorFrame, Rig rig, Drawn drawn, AnimationClip clip)
        {
            string chest = "Rig/root/hips/spine/chest"; var hips = animator.transform.Find("Rig/root/hips"); var rightHand = animator.transform.Find(chest + "/upperarm.r/lowerarm.r/wrist.r/hand.r"); var leftHand = animator.transform.Find(chest + "/upperarm.l/lowerarm.l/wrist.l/hand.l"); var upperArm = animator.transform.Find(chest + "/upperarm.r"); var leftFoot = animator.transform.Find("Rig/root/hips/upperleg.l/lowerleg.l/foot.l"); var rightFoot = animator.transform.Find("Rig/root/hips/upperleg.r/lowerleg.r/foot.r"); var bones = new[] { hips, rightHand, leftHand, upperArm, leftFoot, rightFoot }; if (bones.Any(t => t == null)) throw new InvalidOperationException("Exact source Transform paths missing; no Generic HumanBodyBones fallback.");
            rig.Start(clip); var frames = new List<ThrowFrame>(); Vector3 startHand = default, startHip = default; int steps = Mathf.CeilToInt(clip.length * 120f);
            try
            {
                for (int f = 0; f <= steps; f++)
                {
                    float time = Mathf.Min(f / 120f, clip.length); rig.Pose(time); var floors = drawn.PerRendererFloors(time); ThrowMinima(floors, out float body, out float cape, out float gear, out float leftLeg, out float rightLeg); Vector3 hand = actorFrame.InverseTransformPoint(rightHand.position), hip = actorFrame.InverseTransformPoint(hips.position); if (f == 0) { startHand = hand; startHip = hip; }
                    float dt = f == 0 ? 0 : time - frames[frames.Count - 1].sourceTime, velocity = dt > 0 ? (hand.z - Vec(frames[frames.Count - 1].rightHandActor).z) / dt : 0; float anchorP = rig.WorldPositionDrift, anchorQ = rig.WorldRotationDrift;
                    foreach (var bone in bones.Concat(new[] { animator.transform })) { Vector3 p = bone.position; Quaternion q = bone.rotation; if (new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, bone.lossyScale.x, bone.lossyScale.y, bone.lossyScale.z }.Any(v => !Finite(v))) throw new InvalidOperationException("Nonfinite Generic/Human pose at " + side.mode + "/" + time); } if (!Finite(dt) || !Finite(velocity) || !Finite(anchorP) || !Finite(anchorQ)) throw new InvalidOperationException("Nonfinite sampled time/velocity/anchor.");
                    frames.Add(new ThrowFrame { sourceTime = time, sourceDt = dt, bodyMinY = body, capeMinY = cape, gearMinY = gear, leftLegBootMinY = leftLeg, rightLegBootMinY = rightLeg, leftLegDeltaFromIdle = leftLeg - side.idleLeftLegMinY, rightLegDeltaFromIdle = rightLeg - side.idleRightLegMinY, bodyNonpenetrating = body >= 0, capeNonpenetrating = cape >= 0, gearNonpenetrating = gear >= 0, hipsFromIdleXZ = Planar(hip - startHip), rawAnchorPositionDrift = anchorP, rawAnchorRotationDrift = anchorQ, rightHandForwardFromStart = hand.z - startHand.z, rightHandForwardVelocity = velocity, velocityMeasured = dt > 0, hipsActor = new V(hip), rightHandActor = new V(hand), leftHandActor = new V(actorFrame.InverseTransformPoint(leftHand.position)), rightUpperArmActor = new V(actorFrame.InverseTransformPoint(upperArm.position)), leftFootBoneActor = new V(actorFrame.InverseTransformPoint(leftFoot.position)), rightFootBoneActor = new V(actorFrame.InverseTransformPoint(rightFoot.position)), rightHandActorRotation = new Q(Quaternion.Inverse(actorFrame.rotation) * rightHand.rotation), leftHandActorRotation = new Q(Quaternion.Inverse(actorFrame.rotation) * leftHand.rotation), rightUpperArmActorRotation = new Q(Quaternion.Inverse(actorFrame.rotation) * upperArm.rotation), rawAnimatorLocalPosition = new V(animator.transform.localPosition), rawAnimatorLocalRotation = new Q(animator.transform.localRotation), rendererFloors = floors });
                }
                side.denseCompleted = frames.Count == steps + 1 && frames[0].sourceTime == 0 && frames[frames.Count - 1].sourceTime == clip.length;
            }
            finally
            {
                side.frames = frames.ToArray(); if (frames.Count != 0) { side.minimumBodyY = frames.Min(f => f.bodyMinY); side.minimumCapeY = frames.Min(f => f.capeMinY); side.minimumGearY = frames.Min(f => f.gearMinY); side.minimumLeftLegDelta = frames.Min(f => f.leftLegDeltaFromIdle); side.maximumLeftLegDelta = frames.Max(f => f.leftLegDeltaFromIdle); side.minimumRightLegDelta = frames.Min(f => f.rightLegDeltaFromIdle); side.maximumRightLegDelta = frames.Max(f => f.rightLegDeltaFromIdle); side.maximumAnchorWorldPositionDrift = frames.Max(f => f.rawAnchorPositionDrift); side.maximumAnchorWorldRotationDrift = frames.Max(f => f.rawAnchorRotationDrift); side.bodyNonpenetrating = frames.All(f => f.bodyNonpenetrating); side.capeNonpenetrating = frames.All(f => f.capeNonpenetrating); side.gearNonpenetrating = frames.All(f => f.gearNonpenetrating); }
            }
        }
        [Serializable] sealed class ThrowFrame
        {
            public float sourceTime, sourceDt, bodyMinY, capeMinY, gearMinY, leftLegBootMinY, rightLegBootMinY,
                leftLegDeltaFromIdle, rightLegDeltaFromIdle, hipsFromIdleXZ, rawAnchorPositionDrift, rawAnchorRotationDrift,
                rightHandForwardFromStart, rightHandForwardVelocity, hypotheticalWholeClipDomainTime;
            public bool velocityMeasured, bodyNonpenetrating, capeNonpenetrating, gearNonpenetrating;
            public V hipsActor, rightHandActor, leftHandActor, rightUpperArmActor, leftFootBoneActor, rightFootBoneActor, rawAnimatorLocalPosition;
            public Q rightHandActorRotation, leftHandActorRotation, rightUpperArmActorRotation, rawAnimatorLocalRotation;
            public Floor[] rendererFloors;
        }
        [Serializable] sealed class ThrowReport
        {
            public string label, directory, status, error, startedUtc, finishedUtc, module, prefab, clipAsset, clipGuid, avatarAsset, avatarGuid;
            public long clipLocalId, avatarLocalId;
            public string scope = "ONE original Knight Unarmed_Melee_Attack_Punch_A, fixed owned .75 prefab at world origin y=0, 120Hz Manual Playables/CPU BakeMesh(true), original Avatar, no root reset/lift. Not actual Controller/input/throw/grip/visible knife/projectile/contact/Heal/network/Build or whole-kit acceptance. No clips/importers/Generic/profile/production writes. Preview URP materials are temporary clones, not exact production lighting.";
            public string footScope = "Knight_LegLeft/Knight_LegRight drawn triangle minima are COMBINED leg/boot geometry, not isolated anatomical soles. Fixed Idle t=0 minima and signed changes are diagnostics; foot bone points are NOT soles. No borrowed +50mm runtime capsule offset or after-fit contact gate.";
            public string phaseScope = "Hand +Z displacement/adjacent source-time velocity is a gesture screen only. Whole clip mapped to .56/.22 is hypothetical; not actual release or a speed-peak acceptance gate. RightUpperArm is proximal-joint reference, not a mapped shoulder.";
            public bool baselineMeasured, denseCompleted, cpuSheetCompleted, isolatedFootSoleMeasured = false, fullAbilityAccepted = false,
                bodyNonpenetrating, capeNonpenetrating, gearNonpenetrating, nonpenetrationScreenPassed, handGestureScreenPassed,
                sourcesUnchanged, sourceMaterialMemoryUnchanged, loadedScenesUnchanged, renderSettingsUnchanged, enabledSceneClosureUnchanged;
            public float groundY = 0f, visualScale = .75f, minimumFloorAllowed = 0f, minimumHandForward = .10f,
                sourceLength, sourceFrameRate, idleBodyMinY, idleCapeMinY, idleGearMinY, idleLeftLegBootMinY, idleRightLegBootMinY,
                minimumBodyY, minimumCapeY, minimumGearY, maximumAbsLeftLegDeltaFromIdle, maximumAbsRightLegDeltaFromIdle,
                maximumHipsFromIdleXZ, maximumRawAnchorPositionDrift, maximumRawAnchorRotationDrift, maximumHandForwardFromStart,
                maximumHandForwardVelocity, velocityPeakSourceTime, hypotheticalDuration = .56f, hypotheticalReleaseTime = .22f,
                hypotheticalReleaseSourceTime, hypotheticalReleaseFractionalFrame;
            public int sampledFrames; public string[] images, enabledSceneClosureBefore, enabledSceneClosureAfter;
            public float[] imageFractions; public ThrowFrame[] frames; public Floor[] floors; public FileRow[] protectedBefore, protectedAfter; public MaterialChange[] materialChanges;
        }

        [Serializable] sealed class ThrowClipMemory
        {
            public string name, asset, guid, beforeJson, beforeCleanupJson, afterCleanupJson;
            public long localId; public int originalInstanceId;
            public bool beforeCleanupReached, aliveBeforeCleanup, aliveAfterCleanup;
        }
        sealed class ThrowClipWatch { public AnimationClip original; public ThrowClipMemory row; }
        [Serializable] sealed class UpperBodyThrowReport
        {
            public string label, directory, status, error, startedUtc, finishedUtc, module,
                ownedClipPath, ownedClipName, ownedClipGuid, ownedClipShaBefore, ownedClipShaAfter;
            public long ownedClipLocalId;
            public string scope = "ONE already-authored owned Human upper-body Throw clip ONLY; original Knight Avatar/full rig, fixed .75 pure visual prefab at world origin Y0. Manual Playables at 1x, 120Hz including full endpoints, CPU BakeMesh(true). Zero clip/importer/prefab/profile/controller/runtime/NET/production writes. Evidence files only; completion is capture completion, NOT ability acceptance.";
            public string timingScope = "Authored .56s at playback 1x. Original domain release .22 is a labelled pose time only: no actual Input/Actor/Presenter/event/entry or recovery fade. Authoring source-time warp native Throw length/.56 is distinct from runtime playback rate.";
            public string floorScope = "Body/cape/gear signed world Y>=0 at fixed Y0, independently reported, with native Idle t0 raw baseline. Any negative value FAILS this screen; no +50mm capsule offset, fit/lift, leg scaling, tolerance or transfer from actual Actor floor evidence. Leg/boot minima are not isolated anatomical soles.";
            public string handScope = "Existing Punch .10m hand-forward threshold is a DIAGNOSTIC only, never a new upper-body-Throw acceptance gate. Adjacent sampled hand velocities and .22 pose do not prove same-event-frame forward motion or visible release.";
            public string props = "NOT MEASURED: no held/flight knife, palm/finger grip, projectile, collider/contact, bottle or mouth geometry.";
            public string abilityScope = "NOT MEASURED: actual F/full fade/visible knife release, complete named states, melee-and-knife, contact/HitStop, death/respawn, NET and Build. Retained native-F/Punch/Generic failures unchanged; Ranger/r4 remain production.";
            public bool fullAbilityAccepted = false, sourceClipMemoryUnchanged, fullRigPathsPreserved, candidateInEnabledSceneClosure,
                y0FloorEvaluated, y0BodyNonpenetrating, y0CapeNonpenetrating, y0GearNonpenetrating,
                y0NonpenetrationScreenPassed, idleY0FloorEvaluated, idleY0Nonpenetrating,
                handForwardDiagnosticMeasured, handForwardDiagnosticPassed;
            public float authoredDuration = .56f, labelledReleaseTime = .22f, playbackRate = 1f,
                authoringSourceTimeWarp, handForwardDiagnosticThreshold = .10f;
            public int fullRigLimbCount, ownedFloatBindings;
            public ThrowClipMemory[] sourceClipMemory;
            public ThrowReport preview;
        }

        /// <summary>Read-only CPU review of one owned .56s Human candidate; duplicate labels never repeat work.</summary>
        public static string RequestUpperBodyThrowReview(string uniqueLabel, string ownedClipPath)
        {
            if (string.IsNullOrWhiteSpace(uniqueLabel) || !Regex.IsMatch(uniqueLabel, "^[A-Za-z0-9_-]{8,72}$")) throw new ArgumentException("Fresh ASCII label required.");
            const string ownedRoot = "Assets/_Game/Art/Review/KayKitHeroMotion/";
            if (string.IsNullOrWhiteSpace(ownedClipPath) || ownedClipPath.IndexOf('\\') >= 0 || !ownedClipPath.StartsWith(ownedRoot, StringComparison.Ordinal)
                || !ownedClipPath.EndsWith(".anim", StringComparison.Ordinal) || ownedClipPath.Split('/').Any(p => p == "." || p == ".." || p.Length == 0)
                || !Path.GetFullPath(ownedClipPath).StartsWith(Path.GetFullPath(ownedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(ownedClipPath) || !File.Exists(ownedClipPath + ".meta")) throw new ArgumentException("Existing project-owned KayKitHeroMotion .anim and meta required; no original/importer paths or traversal.");
            string prefix = JobPrefix + "UpperBodyThrow.", key = prefix + uniqueLabel, prior = SessionState.GetString(key, "");
            if (prior.Length != 0)
            {
                if (!File.Exists(prior)) throw new IOException("Retained upper-body Throw preview missing.");
                var old = JsonUtility.FromJson<UpperBodyThrowReport>(File.ReadAllText(prior));
                if (old == null || old.ownedClipPath != ownedClipPath || old.ownedClipShaBefore != Hash(ownedClipPath)) throw new IOException("Label already belongs to another clip identity/bytes; use a fresh label.");
                return prior;
            }
            RequireIdle(); if (pending || assembledPending || motionPending || throwPending || genericPending) throw new InvalidOperationException("One preview job at a time.");
            foreach (string activeKey in new[] { JobPrefix + "active", JobPrefix + "Assembled.active", JobPrefix + "Motion.active", JobPrefix + "Throw.active", JobPrefix + "Generic.active", prefix + "active" })
            {
                string active = SessionState.GetString(activeKey, ""); if (!File.Exists(active)) continue;
                var old = JsonUtility.FromJson<ThrowReport>(File.ReadAllText(active)); if (old.status == "queued" || old.status == "running") throw new InvalidOperationException("Inspect pending/interrupted preview: " + active);
            }
            var clip = Required<AnimationClip>(ownedClipPath);
            if (!EditorUtility.IsPersistent(clip) || AssetDatabase.GetAssetPath(clip) != ownedClipPath || !clip.isHumanMotion || clip.legacy || clip.isLooping
                || !Finite(clip.length) || Mathf.Abs(clip.length - .56f) > .000001f || !Finite(clip.frameRate) || clip.frameRate != 120f
                || AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 0 || AnimationUtility.GetAnimationEvents(clip).Length != 0) throw new InvalidOperationException("Existing nonlooping .56s/120Hz Human clip, no object curves/events, required; never author or fit it here.");
            string dir = Path.GetFullPath("Builds/ArtReview/kaykit-upperbody-throw/" + uniqueLabel); if (Directory.Exists(dir)) throw new IOException("Unique evidence required."); Directory.CreateDirectory(dir);
            string record = dir + "/report.json"; var report = new UpperBodyThrowReport
            {
                label = uniqueLabel, directory = dir, status = "queued", startedUtc = DateTime.UtcNow.ToString("O"), module = typeof(KayKitNativeCharacterReview).Assembly.ManifestModule.ModuleVersionId.ToString(),
                ownedClipPath = ownedClipPath, ownedClipName = clip.name, ownedClipShaBefore = Hash(ownedClipPath), ownedFloatBindings = AnimationUtility.GetCurveBindings(clip).Length,
                preview = new ThrowReport { label = uniqueLabel, directory = dir, status = "queued", prefab = GenericPrefab }
            };
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out report.ownedClipGuid, out report.ownedClipLocalId) || string.IsNullOrEmpty(report.ownedClipGuid) || report.ownedClipLocalId == 0) throw new InvalidOperationException("Owned clip must have a real persistent GUID/localID.");
            report.preview.startedUtc = report.startedUtc; report.preview.module = report.module;
            report.preview.scope = report.scope; report.preview.phaseScope = report.timingScope + " " + report.handScope; report.preview.footScope = report.floorScope;
            File.WriteAllText(record, JsonUtility.ToJson(report, true), new UTF8Encoding(false)); SessionState.SetString(key, record); SessionState.SetString(prefix + "active", record);
            // Share the existing Throw active slot so every older entry point also sees this queued job after reload.
            SessionState.SetString(JobPrefix + "Throw.active", record); throwPending = true;
            double deadline = EditorApplication.timeSinceStartup + 180; EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < deadline) return; EditorApplication.update -= callback;
                try
                {
                    RequireIdle(); if (Hash(ownedClipPath) != report.ownedClipShaBefore) throw new InvalidOperationException("Queued owned clip bytes changed; capture not started.");
                    report.status = report.preview.status = "running"; File.WriteAllText(record, JsonUtility.ToJson(report, true)); CaptureThrowAlternative(report.preview, report); report.status = report.preview.status = "completed";
                }
                catch (Exception ex) { report.status = report.preview.status = "failed"; report.error = report.preview.error = ex.ToString(); Debug.LogException(ex); }
                finally
                {
                    report.finishedUtc = report.preview.finishedUtc = DateTime.UtcNow.ToString("O"); File.WriteAllText(record, JsonUtility.ToJson(report, true), new UTF8Encoding(false)); throwPending = false;
                    SessionState.EraseString(prefix + "active"); if (SessionState.GetString(JobPrefix + "Throw.active", "") == record) SessionState.EraseString(JobPrefix + "Throw.active");
                }
            };
            EditorApplication.update += callback; return record;
        }

        /// <summary>One bounded native Punch_A diagnostic. Duplicate labels return evidence, never rerun or author assets.</summary>
        public static string RequestThrowAlternativeReview(string uniqueLabel)
        {
            if (string.IsNullOrWhiteSpace(uniqueLabel) || !Regex.IsMatch(uniqueLabel, "^[A-Za-z0-9_-]{8,72}$")) throw new ArgumentException("Fresh ASCII label required.");
            string prefix = JobPrefix + "Throw.", key = prefix + uniqueLabel, prior = SessionState.GetString(key, "");
            if (prior.Length != 0) { if (!File.Exists(prior)) throw new IOException("Retained throw probe missing."); return prior; }
            RequireIdle(); if (pending || assembledPending || motionPending || throwPending || genericPending) throw new InvalidOperationException("One preview job at a time.");
            foreach (string activeKey in new[] { JobPrefix + "active", JobPrefix + "Assembled.active", JobPrefix + "Motion.active", JobPrefix + "Generic.active", prefix + "active" })
            {
                string active = SessionState.GetString(activeKey, ""); if (!File.Exists(active)) continue;
                var old = JsonUtility.FromJson<ThrowReport>(File.ReadAllText(active)); if (old.status == "queued" || old.status == "running") throw new InvalidOperationException("Inspect pending/interrupted preview: " + active);
            }
            string dir = Path.GetFullPath("Builds/ArtReview/kaykit-throw/" + uniqueLabel); if (Directory.Exists(dir)) throw new IOException("Unique evidence required."); Directory.CreateDirectory(dir);
            string record = dir + "/report.json"; var report = new ThrowReport { label = uniqueLabel, directory = dir, status = "queued", startedUtc = DateTime.UtcNow.ToString("O"), module = typeof(KayKitNativeCharacterReview).Assembly.ManifestModule.ModuleVersionId.ToString(), prefab = "Assets/_Game/Art/Review/KayKitHero/knight-assembly-20261003-a1/Review_Knight_Hero.prefab" };
            File.WriteAllText(record, JsonUtility.ToJson(report, true), new UTF8Encoding(false)); SessionState.SetString(key, record); SessionState.SetString(prefix + "active", record); throwPending = true;
            double deadline = EditorApplication.timeSinceStartup + 180; EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < deadline) return; EditorApplication.update -= callback;
                try { RequireIdle(); report.status = "running"; File.WriteAllText(record, JsonUtility.ToJson(report, true)); CaptureThrowAlternative(report); report.status = "completed"; }
                catch (Exception ex) { report.status = "failed"; report.error = ex.ToString(); Debug.LogException(ex); }
                finally { report.finishedUtc = DateTime.UtcNow.ToString("O"); File.WriteAllText(record, JsonUtility.ToJson(report, true), new UTF8Encoding(false)); throwPending = false; SessionState.EraseString(prefix + "active"); }
            };
            EditorApplication.update += callback; return record;
        }

        static string[] ThrowEntryClosure() => EditorBuildSettings.scenes.Where(s => s.enabled).SelectMany(s => AssetDatabase.GetDependencies(s.path, true).Concat(new[] { s.path })).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
        static void CaptureThrowAlternative(ThrowReport report, UpperBodyThrowReport upperBody = null)
        {
            Scene[] scenes = OriginalScenes(); string sceneMemory = SceneMemory(scenes), settings = RenderState(); report.enabledSceneClosureBefore = ThrowEntryClosure();
            if (upperBody != null)
            {
                upperBody.candidateInEnabledSceneClosure = report.enabledSceneClosureBefore.Contains(upperBody.ownedClipPath);
                if (upperBody.candidateInEnabledSceneClosure) throw new InvalidOperationException("Owned review clip is already in enabled scene closure; this isolated preview will not accept production migration.");
            }
            var extra = AssetDatabase.GetDependencies(report.prefab, true).Concat(new[] { report.prefab, "Packages/manifest.json", "Packages/packages-lock.json" }).Concat(report.enabledSceneClosureBefore)
                .Concat(upperBody == null ? Enumerable.Empty<string>() : AssetDatabase.GetDependencies(upperBody.ownedClipPath, true).Concat(new[] { upperBody.ownedClipPath }))
                .Distinct().SelectMany(p => new[] { p, p + ".meta" }).Where(File.Exists);
            var frozen = Protected().Concat(extra.Select(p => new FileRow { path = p, sha256 = Hash(p), bytes = new FileInfo(p).Length })).GroupBy(f => f.path, StringComparer.Ordinal).Select(g => g.First()).OrderBy(f => f.path, StringComparer.Ordinal).ToArray(); report.protectedBefore = frozen;
            var watches = MaterialMemory(frozen).Select(p => { AssetDatabase.TryGetGUIDAndLocalFileIdentifier(p.Key, out string guid, out long id); return new MaterialWatch { original = p.Key, row = new MaterialChange { name = p.Key.name, asset = AssetDatabase.GetAssetPath(p.Key), guid = guid, localId = id, originalInstanceId = p.Key.GetInstanceID(), beforeJson = p.Value } }; }).ToArray();
            var clipWatches = upperBody == null ? new ThrowClipWatch[0] : AssetDatabase.LoadAllAssetsAtPath(Source + "Knight.fbx").OfType<AnimationClip>().Where(c => c.name == "Idle" || c.name == "Throw")
                .Concat(new[] { Required<AnimationClip>(upperBody.ownedClipPath) }).Distinct().Select(c =>
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c, out string guid, out long id);
                    return new ThrowClipWatch { original = c, row = new ThrowClipMemory { name = c.name, asset = AssetDatabase.GetAssetPath(c), guid = guid, localId = id, originalInstanceId = c.GetInstanceID(), beforeJson = EditorJsonUtility.ToJson(c) } };
                }).ToArray();
            if (upperBody != null) { upperBody.sourceClipMemory = clipWatches.Select(w => w.row).ToArray(); if (clipWatches.Length != 3) throw new InvalidOperationException("Exactly native Idle/Throw plus owned clip full memory snapshots required."); }
            var frames = new List<ThrowFrame>(); var images = new List<string>();
            try
            {
                using (var stage = new Stage())
                try
                {
                    var actorFrame = stage.New("ThrowProbe_IdentityActorCoordinateFrame"); var candidate = stage.preview.InstantiatePrefabInScene(Required<GameObject>(report.prefab)); candidate.transform.SetParent(actorFrame.transform, false); stage.preview.camera.scene = candidate.scene;
                    if (candidate.transform.localScale != Vector3.one * .75f || candidate.transform.localPosition != Vector3.zero || candidate.transform.localRotation != Quaternion.identity || candidate.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 || candidate.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("Exact origin/.75 pure visual prefab required; never fit/lift it.");
                    var animator = candidate.GetComponentInChildren<Animator>(true); var sourceAvatar = Required<GameObject>(Source + "Knight.fbx").GetComponentInChildren<Animator>(true)?.avatar;
                    if (animator == null || sourceAvatar == null || animator.avatar != sourceAvatar || !animator.avatar.isHuman || !animator.avatar.isValid) throw new InvalidOperationException("Original immutable Knight Avatar required.");
                    report.avatarAsset = AssetDatabase.GetAssetPath(animator.avatar); AssetDatabase.TryGetGUIDAndLocalFileIdentifier(animator.avatar, out report.avatarGuid, out report.avatarLocalId);
                    var clips = AssetDatabase.LoadAllAssetsAtPath(Source + "Knight.fbx").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview", StringComparison.Ordinal)).ToDictionary(c => c.name, StringComparer.Ordinal); var clip = upperBody == null ? clips["Unarmed_Melee_Attack_Punch_A"] : Required<AnimationClip>(upperBody.ownedClipPath);
                    if (!clip.isHumanMotion || clip.legacy || !Finite(clip.length) || clip.length <= 0f || clip.length > 10f || !Finite(clip.frameRate) || clip.frameRate <= 0f || MissingBindings(clip, animator.gameObject).Length != 0 || AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 0) throw new InvalidOperationException(upperBody == null ? "One finite native human Punch_A with valid bindings required." : "One finite owned Human upper-body Throw with valid bindings required.");
                    if (upperBody != null)
                    {
                        if (Mathf.Abs(clip.length - upperBody.authoredDuration) > .000001f || clip.frameRate != 120f || clip.isLooping || AnimationUtility.GetAnimationEvents(clip).Length != 0) throw new InvalidOperationException("Owned authored .56/120Hz nonlooping event-free clip contract changed.");
                        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string candidateGuid, out long candidateId) || candidateGuid != upperBody.ownedClipGuid || candidateId != upperBody.ownedClipLocalId || clip.name != upperBody.ownedClipName) throw new InvalidOperationException("Queued owned clip identity changed.");
                        var originalRoot = Required<GameObject>(Source + "Knight.fbx").GetComponentInChildren<Animator>(true).transform;
                        foreach (string limb in Limbs)
                        {
                            var original = originalRoot.GetComponentsInChildren<Transform>(true).Single(t => t.name == limb); var actual = animator.GetComponentsInChildren<Transform>(true).Single(t => t.name == limb);
                            if (PathOf(actual, animator.transform) != PathOf(original, originalRoot) || actual.GetSiblingIndex() != original.GetSiblingIndex() || actual.gameObject.activeSelf != original.gameObject.activeSelf
                                || Vector3.Distance(actual.localPosition, original.localPosition) > 1e-6f || Vector3.Distance(actual.localScale, original.localScale) > 1e-6f || Quaternion.Angle(actual.localRotation, original.localRotation) > .001f) throw new InvalidOperationException("Original full-rig path/rest/order differs before evaluation: " + limb);
                        }
                        upperBody.fullRigLimbCount = Limbs.Length; upperBody.fullRigPathsPreserved = true; upperBody.authoringSourceTimeWarp = clips["Throw"].length / upperBody.authoredDuration;
                    }
                    report.clipAsset = AssetDatabase.GetAssetPath(clip); AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out report.clipGuid, out report.clipLocalId); report.sourceLength = clip.length; report.sourceFrameRate = clip.frameRate;
                    report.hypotheticalReleaseSourceTime = clip.length * report.hypotheticalReleaseTime / report.hypotheticalDuration; report.hypotheticalReleaseFractionalFrame = report.hypotheticalReleaseSourceTime * clip.frameRate;
                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips); var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand); var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand); var upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm); var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot); var rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                    if (new[] { hips, rightHand, leftHand, upperArm, leftFoot, rightFoot }.Any(t => t == null)) throw new InvalidOperationException("Actual mapped hand/hips/foot/upper-arm references required; no invented zeros.");
                    stage.Materials(candidate); stage.Floor(Vector3.zero, new Vector3(8, .02f, 6));
                    using (var rig = new Rig(animator)) using (var drawn = new Drawn(candidate))
                    {
                        rig.Start(clips["Idle"]); rig.Pose(0); var idle = drawn.PerRendererFloors(0); ThrowMinima(idle, out report.idleBodyMinY, out report.idleCapeMinY, out report.idleGearMinY, out report.idleLeftLegBootMinY, out report.idleRightLegBootMinY); report.baselineMeasured = true;
                        if (upperBody != null) { upperBody.idleY0FloorEvaluated = true; upperBody.idleY0Nonpenetrating = report.idleBodyMinY >= 0f && report.idleCapeMinY >= 0f && report.idleGearMinY >= 0f; }
                        Vector3 idleHip = actorFrame.transform.InverseTransformPoint(hips.position), startHand = default; rig.Start(clip); int steps = Mathf.CeilToInt(clip.length * 120f);
                        if (upperBody != null) { upperBody.playbackRate = rig.PlaybackRate; if (upperBody.playbackRate != 1f) throw new InvalidOperationException("Owned .56 timeline must be sampled at actual playable rate 1."); }
                        for (int f = 0; f <= steps; f++)
                        {
                            float time = Mathf.Min(f / 120f, clip.length); rig.Pose(time); var floors = drawn.PerRendererFloors(time); ThrowMinima(floors, out float body, out float cape, out float gear, out float leftLeg, out float rightLeg);
                            Vector3 hand = actorFrame.transform.InverseTransformPoint(rightHand.position), hip = actorFrame.transform.InverseTransformPoint(hips.position); if (f == 0) startHand = hand;
                            float dt = frames.Count == 0 ? 0f : time - frames[frames.Count - 1].sourceTime; float velocity = dt > 0 ? (hand.z - Vec(frames[frames.Count - 1].rightHandActor).z) / dt : 0f;
                            if (new[] { hand.x, hand.y, hand.z, hip.x, hip.y, hip.z, dt, velocity, rig.PositionDrift, rig.RotationDrift }.Any(v => !Finite(v))) throw new InvalidOperationException("Non-finite native pose at " + time);
                            foreach (var bone in new[] { hips, rightHand, leftHand, upperArm, leftFoot, rightFoot, animator.transform }) { Vector3 p = bone.position; Quaternion q = bone.rotation; if (new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w }.Any(v => !Finite(v))) throw new InvalidOperationException("Non-finite native bone TRS: " + bone.name + " at " + time); }
                            frames.Add(new ThrowFrame { sourceTime = time, sourceDt = dt, bodyMinY = body, capeMinY = cape, gearMinY = gear, leftLegBootMinY = leftLeg, rightLegBootMinY = rightLeg, leftLegDeltaFromIdle = leftLeg - report.idleLeftLegBootMinY, rightLegDeltaFromIdle = rightLeg - report.idleRightLegBootMinY,
                                bodyNonpenetrating = body >= 0f, capeNonpenetrating = cape >= 0f, gearNonpenetrating = gear >= 0f, hipsFromIdleXZ = Planar(hip - idleHip), rawAnchorPositionDrift = rig.PositionDrift, rawAnchorRotationDrift = rig.RotationDrift,
                                rightHandForwardFromStart = hand.z - startHand.z, rightHandForwardVelocity = velocity, velocityMeasured = dt > 0, hypotheticalWholeClipDomainTime = time / clip.length * report.hypotheticalDuration,
                                hipsActor = new V(hip), rightHandActor = new V(hand), leftHandActor = new V(actorFrame.transform.InverseTransformPoint(leftHand.position)), rightUpperArmActor = new V(actorFrame.transform.InverseTransformPoint(upperArm.position)), leftFootBoneActor = new V(actorFrame.transform.InverseTransformPoint(leftFoot.position)), rightFootBoneActor = new V(actorFrame.transform.InverseTransformPoint(rightFoot.position)),
                                rightHandActorRotation = new Q(Quaternion.Inverse(actorFrame.transform.rotation) * rightHand.rotation), leftHandActorRotation = new Q(Quaternion.Inverse(actorFrame.transform.rotation) * leftHand.rotation), rightUpperArmActorRotation = new Q(Quaternion.Inverse(actorFrame.transform.rotation) * upperArm.rotation), rawAnimatorLocalPosition = new V(animator.transform.localPosition), rawAnimatorLocalRotation = new Q(animator.transform.localRotation), rendererFloors = floors });
                        }
                        report.denseCompleted = frames.Count == steps + 1 && frames[0].sourceTime == 0f && frames[frames.Count - 1].sourceTime == clip.length;
                        var sheet = new Texture2D(2000, 600, TextureFormat.RGB24, false); report.imageFractions = upperBody == null ? new[] { 0f, .25f, .5f, .75f, 1f } : new[] { 0f, .25f, upperBody.labelledReleaseTime / clip.length, .75f, 1f };
                        try
                        {
                            stage.Camera(new Vector3(3.5f, 2.2f, 6.2f), new Vector3(0f, .9f, 0f), 38f);
                            for (int i = 0; i < report.imageFractions.Length; i++) { rig.Start(clip); rig.Pose(clip.length * report.imageFractions[i]); var image = stage.Image(400, 600); try { sheet.SetPixels(i * 400, 0, 400, 600, image.GetPixels()); } finally { Object.DestroyImmediate(image); } }
                            sheet.Apply(); string name = upperBody == null ? "01-punch-a-native-five-fractions.png" : "01-upper-body-throw-five-poses.png"; File.WriteAllBytes(report.directory + "/" + name, sheet.EncodeToPNG()); images.Add(name); report.cpuSheetCompleted = true;
                        }
                        finally { Object.DestroyImmediate(sheet); }
                    }
                }
                finally
                {
                    foreach (var watch in watches) { watch.row.beforeCleanupReached = true; watch.row.aliveBeforeCleanup = watch.original != null; watch.row.beforeCleanupJson = watch.original == null ? null : EditorJsonUtility.ToJson(watch.original); }
                    foreach (var watch in clipWatches) { watch.row.beforeCleanupReached = true; watch.row.aliveBeforeCleanup = watch.original != null; watch.row.beforeCleanupJson = watch.original == null ? null : EditorJsonUtility.ToJson(watch.original); }
                }
            }
            finally
            {
                report.frames = frames.ToArray(); report.sampledFrames = frames.Count; report.images = images.ToArray();
                if (frames.Count != 0)
                {
                    report.minimumBodyY = frames.Min(f => f.bodyMinY); report.minimumCapeY = frames.Min(f => f.capeMinY); report.minimumGearY = frames.Min(f => f.gearMinY);
                    report.bodyNonpenetrating = frames.All(f => f.bodyNonpenetrating); report.capeNonpenetrating = frames.All(f => f.capeNonpenetrating); report.gearNonpenetrating = frames.All(f => f.gearNonpenetrating); report.nonpenetrationScreenPassed = report.denseCompleted && report.bodyNonpenetrating && report.capeNonpenetrating && report.gearNonpenetrating;
                    report.maximumAbsLeftLegDeltaFromIdle = frames.Max(f => Mathf.Abs(f.leftLegDeltaFromIdle)); report.maximumAbsRightLegDeltaFromIdle = frames.Max(f => Mathf.Abs(f.rightLegDeltaFromIdle)); report.maximumHipsFromIdleXZ = frames.Max(f => f.hipsFromIdleXZ); report.maximumRawAnchorPositionDrift = frames.Max(f => f.rawAnchorPositionDrift); report.maximumRawAnchorRotationDrift = frames.Max(f => f.rawAnchorRotationDrift);
                    report.maximumHandForwardFromStart = frames.Max(f => f.rightHandForwardFromStart);
                    if (upperBody == null) report.handGestureScreenPassed = report.denseCompleted && report.maximumHandForwardFromStart >= report.minimumHandForward;
                    else
                    {
                        upperBody.y0FloorEvaluated = report.denseCompleted; upperBody.y0BodyNonpenetrating = report.bodyNonpenetrating; upperBody.y0CapeNonpenetrating = report.capeNonpenetrating; upperBody.y0GearNonpenetrating = report.gearNonpenetrating; upperBody.y0NonpenetrationScreenPassed = report.nonpenetrationScreenPassed;
                        upperBody.handForwardDiagnosticMeasured = report.denseCompleted; upperBody.handForwardDiagnosticPassed = report.denseCompleted && report.maximumHandForwardFromStart >= upperBody.handForwardDiagnosticThreshold;
                        report.handGestureScreenPassed = false; // The old Punch screen never grants this new candidate a phase/ability gate.
                    }
                    var peak = frames.Where(f => f.velocityMeasured).OrderByDescending(f => f.rightHandForwardVelocity).FirstOrDefault(); if (peak != null) { report.maximumHandForwardVelocity = peak.rightHandForwardVelocity; report.velocityPeakSourceTime = peak.sourceTime; }
                    report.floors = frames.SelectMany(f => f.rendererFloors).GroupBy(f => f.renderer, StringComparer.Ordinal).Select(g => { var min = g.OrderBy(f => f.minY).First(); return new Floor { renderer = g.Key, category = min.category, minY = min.minY, time = min.time, activeSamples = g.Count() }; }).OrderBy(f => f.renderer, StringComparer.Ordinal).ToArray();
                }
                report.protectedAfter = frozen.Select(f => new FileRow { path = f.path, sha256 = File.Exists(f.path) ? Hash(f.path) : "MISSING", bytes = File.Exists(f.path) ? new FileInfo(f.path).Length : -1 }).ToArray(); report.sourcesUnchanged = frozen.Zip(report.protectedAfter, (a, b) => a.path == b.path && a.sha256 == b.sha256 && a.bytes == b.bytes).All(b => b);
                foreach (var watch in watches) { watch.row.aliveAfterCleanup = watch.original != null; watch.row.afterCleanupJson = watch.original == null ? null : EditorJsonUtility.ToJson(watch.original); }
                report.materialChanges = watches.Where(w => !w.row.aliveAfterCleanup || w.row.beforeJson != w.row.afterCleanupJson || (w.row.beforeCleanupReached && (!w.row.aliveBeforeCleanup || w.row.beforeJson != w.row.beforeCleanupJson))).Select(w => w.row).ToArray(); report.sourceMaterialMemoryUnchanged = report.materialChanges.Length == 0;
                if (upperBody != null)
                {
                    foreach (var watch in clipWatches) { watch.row.aliveAfterCleanup = watch.original != null; watch.row.afterCleanupJson = watch.original == null ? null : EditorJsonUtility.ToJson(watch.original); }
                    upperBody.sourceClipMemoryUnchanged = clipWatches.All(w => w.row.aliveAfterCleanup && w.row.beforeJson == w.row.afterCleanupJson && (!w.row.beforeCleanupReached || w.row.aliveBeforeCleanup && w.row.beforeJson == w.row.beforeCleanupJson));
                    upperBody.ownedClipShaAfter = File.Exists(upperBody.ownedClipPath) ? Hash(upperBody.ownedClipPath) : "MISSING";
                }
                report.loadedScenesUnchanged = sceneMemory == SceneMemory(scenes) && SameScenes(scenes); report.renderSettingsUnchanged = settings == RenderState(); report.enabledSceneClosureAfter = ThrowEntryClosure(); report.enabledSceneClosureUnchanged = report.enabledSceneClosureBefore.SequenceEqual(report.enabledSceneClosureAfter);
                if (!report.sourcesUnchanged || !report.sourceMaterialMemoryUnchanged || !report.loadedScenesUnchanged || !report.renderSettingsUnchanged || !report.enabledSceneClosureUnchanged || (upperBody != null && (!upperBody.sourceClipMemoryUnchanged || upperBody.ownedClipShaBefore != upperBody.ownedClipShaAfter))) throw new InvalidOperationException(upperBody == null ? "Throw probe source/material/scene/settings/entry-closure protection failed; partial evidence retained, no unknown restoration." : "Upper-body Throw source/material/clip-memory/scene/settings/entry-closure protection failed; partial evidence retained, no unknown restoration.");
            }
        }
        static void ThrowMinima(Floor[] floors, out float body, out float cape, out float gear, out float leftLeg, out float rightLeg)
        {
            body = floors.Where(f => !f.category.StartsWith("named equipment", StringComparison.Ordinal) && !f.category.StartsWith("separate named cape", StringComparison.Ordinal)).Select(f => f.minY).DefaultIfEmpty(float.PositiveInfinity).Min();
            cape = floors.Where(f => f.category.StartsWith("separate named cape", StringComparison.Ordinal)).Select(f => f.minY).DefaultIfEmpty(float.PositiveInfinity).Min(); gear = floors.Where(f => f.category.StartsWith("named equipment", StringComparison.Ordinal)).Select(f => f.minY).DefaultIfEmpty(float.PositiveInfinity).Min();
            leftLeg = floors.Where(f => f.renderer == "Knight_LegLeft" || f.renderer.EndsWith("/Knight_LegLeft", StringComparison.Ordinal)).Select(f => f.minY).Single(); rightLeg = floors.Where(f => f.renderer == "Knight_LegRight" || f.renderer.EndsWith("/Knight_LegRight", StringComparison.Ordinal)).Select(f => f.minY).Single();
            if (new[] { body, cape, gear, leftLeg, rightLeg }.Any(v => !Finite(v))) throw new InvalidOperationException("Missing/non-finite actual body/cape/gear/left/right leg triangles; no fake zero.");
        }
        [Serializable] sealed class VariantFrame
        {
            public float time, sourceTime, sourceBodyMinY, variantBodyMinY, sourceCapeMinY, variantCapeMinY, sourceGearMinY, variantGearMinY,
                maxBonePositionDifference, maxBoneRotationDifference, sourceHipFromIdleXZ, variantHipFromIdleXZ, sourceAnchorPosition, variantAnchorPosition, sourceAnchorRotation, variantAnchorRotation;
        }
        [Serializable] sealed class VariantCase
        {
            public string name, source, variant; public float sourceStart, sourceStop, variantLength, sourceFrameRate, fractionalSourceCutFrame,
                maxBonePositionDifference, maxBoneRotationDifference, maxSourceHipFromIdleXZ, maxVariantHipFromIdleXZ, finalVariantHipFromIdleXZ,
                holdSourceBodyMin, holdSourceBodyMax, holdVariantBodyMin, holdVariantBodyMax;
            public int sampledFrames; public bool controlledHold; public VariantFrame[] frames; public Floor[] sourceFloors, variantFloors;
        }
        [Serializable] sealed class VariantReport
        {
            public string label, directory, status, error, startedUtc, finishedUtc, module;
            public string scope = "Same owned .75 Knight prefab/Avatar, dense 120Hz manual CPU poses. Crops compared against their source intervals, single Death Y setting compared with original, death time after clip end explicitly clamped. No AnimatorController blends, actual input/contact/HitStop/grip/heal/network/Build or production replacement. Old Ranger hips tests unchanged, not rerun/borrowed.";
            public bool sourcesUnchanged, sourceMaterialMemoryUnchanged, loadedScenesUnchanged, renderSettingsUnchanged;
            public string[] variantPaths; public VariantCase[] cases; public FileRow[] protectedBefore, protectedAfter; public MaterialChange[] materialChanges;
        }

        public static string RequestMotionVariantsReview(string uniqueLabel, string[] variantPaths)
        {
            if(string.IsNullOrWhiteSpace(uniqueLabel) || !Regex.IsMatch(uniqueLabel,"^[A-Za-z0-9_-]{8,72}$") || variantPaths==null || variantPaths.Length!=5 || variantPaths.Any(p=>string.IsNullOrEmpty(p) || !p.StartsWith("Assets/_Game/Art/Review/KayKitHeroMotion/",StringComparison.Ordinal) || !p.EndsWith(".anim",StringComparison.Ordinal))) throw new ArgumentException("Fresh label and five owned ordered clip paths required.");
            string prefix=JobPrefix+"Motion.",key=prefix+uniqueLabel,prior=SessionState.GetString(key,"");if(prior.Length!=0){if(!File.Exists(prior))throw new IOException("Retained job missing.");return prior;}
            RequireIdle(); RequireNoRetainedGeneric();if(pending || assembledPending || motionPending || throwPending || genericPending)throw new InvalidOperationException("One preview job at a time.");
            string active=SessionState.GetString(prefix+"active","");if(File.Exists(active)){var old=JsonUtility.FromJson<VariantReport>(File.ReadAllText(active));if(old.status=="queued"||old.status=="running")throw new InvalidOperationException("Inspect pending job: "+active);}
            string dir=Path.GetFullPath("Builds/ArtReview/kaykit-motion/"+uniqueLabel);if(Directory.Exists(dir))throw new IOException("Unique evidence required.");Directory.CreateDirectory(dir);string record=dir+"/report.json";
            var report=new VariantReport{label=uniqueLabel,directory=dir,status="queued",startedUtc=DateTime.UtcNow.ToString("O"),module=typeof(KayKitNativeCharacterReview).Assembly.ManifestModule.ModuleVersionId.ToString(),variantPaths=variantPaths.ToArray()};
            File.WriteAllText(record,JsonUtility.ToJson(report,true),new UTF8Encoding(false));SessionState.SetString(key,record);SessionState.SetString(prefix+"active",record);motionPending=true;
            double deadline=EditorApplication.timeSinceStartup+180;EditorApplication.CallbackFunction callback=null;
            callback=()=>{if((EditorApplication.isCompiling||EditorApplication.isUpdating)&&EditorApplication.timeSinceStartup<deadline)return;EditorApplication.update-=callback;
                try{RequireIdle();report.status="running";File.WriteAllText(record,JsonUtility.ToJson(report,true));CaptureVariants(report);report.status="completed";}catch(Exception ex){report.status="failed";report.error=ex.ToString();Debug.LogException(ex);}finally{report.finishedUtc=DateTime.UtcNow.ToString("O");File.WriteAllText(record,JsonUtility.ToJson(report,true),new UTF8Encoding(false));motionPending=false;SessionState.EraseString(prefix+"active");}};
            EditorApplication.update+=callback;return record;
        }

        static void CaptureVariants(VariantReport report)
        {
            const string prefab="Assets/_Game/Art/Review/KayKitHero/knight-assembly-20261003-a1/Review_Knight_Hero.prefab";
            var scenes=OriginalScenes();string sceneMemory=SceneMemory(scenes),settings=RenderState();
            var extra=report.variantPaths.Concat(new[]{prefab}).SelectMany(p=>AssetDatabase.GetDependencies(p,true).Concat(new[]{p})).Distinct().SelectMany(p=>File.Exists(p+".meta")?new[]{p,p+".meta"}:new[]{p}).Where(File.Exists);
            var frozen=Protected().Concat(extra.Select(p=>new FileRow{path=p,sha256=Hash(p),bytes=new FileInfo(p).Length})).GroupBy(f=>f.path,StringComparer.Ordinal).Select(g=>g.First()).OrderBy(f=>f.path,StringComparer.Ordinal).ToArray();report.protectedBefore=frozen;
            var watches=MaterialMemory(frozen).Select(p=>{AssetDatabase.TryGetGUIDAndLocalFileIdentifier(p.Key,out string guid,out long id);return new MaterialWatch{original=p.Key,row=new MaterialChange{name=p.Key.name,asset=AssetDatabase.GetAssetPath(p.Key),guid=guid,localId=id,originalInstanceId=p.Key.GetInstanceID(),beforeJson=p.Value}};}).ToArray();var cases=new List<VariantCase>();
            try
            {
                using(var stage=new Stage())
                {
                    var reference=stage.preview.InstantiatePrefabInScene(Required<GameObject>(prefab));var variant=stage.preview.InstantiatePrefabInScene(Required<GameObject>(prefab));stage.preview.camera.scene=reference.scene;
                    var aa=reference.GetComponentInChildren<Animator>(true);var ab=variant.GetComponentInChildren<Animator>(true);
                    if(aa.avatar!=ab.avatar || AssetDatabase.GetAssetPath(aa.avatar)!=Source+"Knight.fbx" || reference.transform.localScale!=variant.transform.localScale || reference.transform.localScale!=Vector3.one*KayKitHeroCandidateAuthoring.HeroVisualScale)throw new InvalidOperationException("One fixed source Avatar/owned scale required.");
                    var bonesA=Limbs.Select(n=>reference.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n)).ToArray();var bonesB=Limbs.Select(n=>variant.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n)).ToArray();
                    var clips=AssetDatabase.LoadAllAssetsAtPath(Source+"Knight.fbx").OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview",StringComparison.Ordinal)).ToDictionary(c=>c.name,StringComparer.Ordinal);
                    using(var ra=new Rig(aa))using(var rb=new Rig(ab))using(var da=new Drawn(reference))using(var db=new Drawn(variant))
                    {
                        ra.Start(clips["Idle"]);rb.Start(clips["Idle"]);ra.Pose(0);rb.Pose(0);Vector3 idleA=aa.GetBoneTransform(HumanBodyBones.Hips).position,idleB=ab.GetBoneTransform(HumanBodyBones.Hips).position;
                        for(int i=0;i<5;i++)
                        {
                            string src=i<2?"1H_Melee_Attack_Chop":i<4?"1H_Melee_Attack_Slice_Diagonal":"Death_A";var original=clips[src];var candidate=Required<AnimationClip>(report.variantPaths[i]);bool hold=i==4;float cut=i<2?original.length*.28f/.58f:original.length*.30f/.62f;float start=hold||i%2==0?0:cut,stop=hold||i%2!=0?original.length:cut;
                            var row=new VariantCase{name=i==0?"Light1Attack":i==1?"Light1Recovery":i==2?"Light2Attack":i==3?"Light2Recovery":"DeathYBake",source=Source+"Knight.fbx::"+src,variant=report.variantPaths[i],sourceStart=start,sourceStop=stop,sourceFrameRate=original.frameRate,fractionalSourceCutFrame=hold?0:cut*original.frameRate,variantLength=candidate.length,controlledHold=hold};cases.Add(row);
                            if(!candidate.humanMotion || candidate.legacy || AnimationUtility.GetObjectReferenceCurveBindings(candidate).Length!=0)throw new InvalidOperationException("Owned native human clip contract failed: "+candidate.name);
                            if(!Finite(candidate.length) || Mathf.Abs(candidate.length-(stop-start))>1e-6f)throw new InvalidOperationException("Variant interval length differs from source: "+candidate.name);
                            ra.Start(original);rb.Start(candidate);float end=hold?2.2f:candidate.length;int steps=Mathf.CeilToInt(end*120f);var frames=new List<VariantFrame>();var fa=new Dictionary<string,Floor>();var fb=new Dictionary<string,Floor>();
                            bool holdReached=false;
                            try
                            {
                            for(int f=0;f<=steps;f++)
                            {
                                float t=Mathf.Min(f/120f,end),sourceTime=hold?Mathf.Min(t,original.length):Mathf.Min(start+t,stop);ra.Pose(sourceTime);rb.Pose(Mathf.Min(t,candidate.length));
                                da.Minima(fa,t,out float bodyA,out float capeA,out float gearA);db.Minima(fb,t,out float bodyB,out float capeB,out float gearB);float pos=0,angle=0;
                                for(int b=0;b<bonesA.Length;b++){pos=Mathf.Max(pos,Vector3.Distance(bonesA[b].position,bonesB[b].position));angle=Mathf.Max(angle,Quaternion.Angle(bonesA[b].rotation,bonesB[b].rotation));}
                                float hipsA=Planar(aa.GetBoneTransform(HumanBodyBones.Hips).position-idleA),hipsB=Planar(ab.GetBoneTransform(HumanBodyBones.Hips).position-idleB);
                                if(new[]{bodyA,capeA,gearA,bodyB,capeB,gearB,pos,angle,hipsA,hipsB,ra.PositionDrift,rb.PositionDrift,ra.RotationDrift,rb.RotationDrift}.Any(v=>!Finite(v)))throw new InvalidOperationException("Non-finite/missing drawn geometry or pose at "+row.name+" t="+t);
                                var frame=new VariantFrame{time=t,sourceTime=sourceTime,sourceBodyMinY=bodyA,variantBodyMinY=bodyB,sourceCapeMinY=capeA,variantCapeMinY=capeB,sourceGearMinY=gearA,variantGearMinY=gearB,maxBonePositionDifference=pos,maxBoneRotationDifference=angle,sourceHipFromIdleXZ=hipsA,variantHipFromIdleXZ=hipsB,sourceAnchorPosition=ra.PositionDrift,variantAnchorPosition=rb.PositionDrift,sourceAnchorRotation=ra.RotationDrift,variantAnchorRotation=rb.RotationDrift};frames.Add(frame);
                                row.maxBonePositionDifference=Mathf.Max(row.maxBonePositionDifference,pos);row.maxBoneRotationDifference=Mathf.Max(row.maxBoneRotationDifference,angle);row.maxSourceHipFromIdleXZ=Mathf.Max(row.maxSourceHipFromIdleXZ,hipsA);row.maxVariantHipFromIdleXZ=Mathf.Max(row.maxVariantHipFromIdleXZ,hipsB);row.finalVariantHipFromIdleXZ=hipsB;
                                if(hold && t>=.8f){if(!holdReached){row.holdSourceBodyMin=row.holdSourceBodyMax=bodyA;row.holdVariantBodyMin=row.holdVariantBodyMax=bodyB;holdReached=true;}else{row.holdSourceBodyMin=Mathf.Min(row.holdSourceBodyMin,bodyA);row.holdSourceBodyMax=Mathf.Max(row.holdSourceBodyMax,bodyA);row.holdVariantBodyMin=Mathf.Min(row.holdVariantBodyMin,bodyB);row.holdVariantBodyMax=Mathf.Max(row.holdVariantBodyMax,bodyB);}}
                            }
                            }
                            finally{row.frames=frames.ToArray();row.sampledFrames=frames.Count;row.sourceFloors=fa.Values.OrderBy(x=>x.renderer,StringComparer.Ordinal).ToArray();row.variantFloors=fb.Values.OrderBy(x=>x.renderer,StringComparer.Ordinal).ToArray();}
                            if(hold && !holdReached)throw new InvalidOperationException("Controlled death hold not reached.");
                        }
                    }
                    foreach(var w in watches){w.row.beforeCleanupReached=true;w.row.aliveBeforeCleanup=w.original!=null;w.row.beforeCleanupJson=w.original==null?null:EditorJsonUtility.ToJson(w.original);}
                }
            }
            finally
            {
                report.cases=cases.ToArray();report.protectedAfter=frozen.Select(f=>new FileRow{path=f.path,sha256=Hash(f.path),bytes=new FileInfo(f.path).Length}).ToArray();report.sourcesUnchanged=frozen.Zip(report.protectedAfter,(a,b)=>a.path==b.path && a.sha256==b.sha256 && a.bytes==b.bytes).All(b=>b);
                foreach(var w in watches){w.row.aliveAfterCleanup=w.original!=null;w.row.afterCleanupJson=w.original==null?null:EditorJsonUtility.ToJson(w.original);}report.materialChanges=watches.Where(w=>!w.row.aliveAfterCleanup || w.row.beforeJson!=w.row.afterCleanupJson || (w.row.beforeCleanupReached && (!w.row.aliveBeforeCleanup || w.row.beforeJson!=w.row.beforeCleanupJson))).Select(w=>w.row).ToArray();report.sourceMaterialMemoryUnchanged=report.materialChanges.Length==0;
                report.loadedScenesUnchanged=sceneMemory==SceneMemory(scenes) && SameScenes(scenes);report.renderSettingsUnchanged=settings==RenderState();if(!report.sourcesUnchanged || !report.sourceMaterialMemoryUnchanged || !report.loadedScenesUnchanged || !report.renderSettingsUnchanged)throw new InvalidOperationException("Motion probe protection failed; raw differences retained.");
            }
        }
        [Serializable] sealed class AssembledPose { public string clip; public float fraction, sourceTime, drawnMinY, rawAnchorPosition, rawAnchorRotation; }
        [Serializable] sealed class AssembledReport
        {
            public string label, prefab, directory, status, error, startedUtc, finishedUtc, module;
            public string scope = "Owned Knight prefab, original native clips and CPU BakeMesh(true) snapshots at saved scale; controlled Editor poses/static cloned courtyard, not real input, contact, knife release, heal, natural camera, network, performance or delivery acceptance.";
            public bool environmentOnly; public string environmentCameraPolicy = "Independent diagnostic view shortened to 2.8m inside courtyard, not the settled production camera or camera-occlusion acceptance.";
            public bool sourcesUnchanged, sourceMaterialMemoryUnchanged, loadedScenesUnchanged, renderSettingsUnchanged;
            public string[] images; public AssembledPose[] poses; public FileRow[] protectedBefore, protectedAfter; public MaterialChange[] materialChanges;
            public V savedScale, idleDrawnMin, idleDrawnMax, cameraPosition, cameraTarget;
            public int excludedActorRenderers, copiedEnvironmentRenderers; public float cameraFieldOfView;
        }

        /// <summary>Inspect an owned persistent candidate, without reauthoring it or changing any production references.</summary>
        public static string RequestAssembledPreview(string uniqueLabel, string prefabPath, bool environmentOnly = false)
        {
            if (string.IsNullOrWhiteSpace(uniqueLabel) || !Regex.IsMatch(uniqueLabel,"^[A-Za-z0-9_-]{8,72}$")) throw new ArgumentException("Fresh ASCII label required.");
            if (!prefabPath.StartsWith(KayKitHeroCandidateAuthoring.CandidateRoot+"/",StringComparison.Ordinal) || !prefabPath.EndsWith(".prefab",StringComparison.Ordinal)) throw new ArgumentException("Owned candidate prefab only.");
            string prefix=JobPrefix+"Assembled.", key=prefix+uniqueLabel, prior=SessionState.GetString(key,"");
            if(prior.Length!=0) { if(!File.Exists(prior)) throw new IOException("Retained record missing."); return prior; }
            RequireIdle(); RequireNoRetainedGeneric(); if(assembledPending || pending || throwPending || genericPending) throw new InvalidOperationException("One preview job at a time.");
            string active=SessionState.GetString(prefix+"active",""); if(File.Exists(active)) { var old=JsonUtility.FromJson<AssembledReport>(File.ReadAllText(active)); if(old.status=="queued" || old.status=="running") throw new InvalidOperationException("Inspect pending job: "+active); }
            string dir=Path.GetFullPath("Builds/ArtReview/kaykit-assembled-preview/"+uniqueLabel); if(Directory.Exists(dir)) throw new IOException("Unique evidence required.");
            Directory.CreateDirectory(dir); string record=dir+"/report.json";
            var report=new AssembledReport { label=uniqueLabel, prefab=prefabPath, environmentOnly=environmentOnly, directory=dir, status="queued", startedUtc=DateTime.UtcNow.ToString("O"), module=typeof(KayKitNativeCharacterReview).Assembly.ManifestModule.ModuleVersionId.ToString() };
            File.WriteAllText(record,JsonUtility.ToJson(report,true),new UTF8Encoding(false)); SessionState.SetString(key,record);SessionState.SetString(prefix+"active",record);assembledPending=true;
            double deadline=EditorApplication.timeSinceStartup+180; EditorApplication.CallbackFunction callback=null;
            callback=()=> { if((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup<deadline) return; EditorApplication.update-=callback;
                try { RequireIdle();report.status="running";File.WriteAllText(record,JsonUtility.ToJson(report,true)); CaptureAssembled(report); report.status="completed"; }
                catch(Exception ex) { report.status="failed";report.error=ex.ToString();Debug.LogException(ex); }
                finally { report.finishedUtc=DateTime.UtcNow.ToString("O"); File.WriteAllText(record,JsonUtility.ToJson(report,true),new UTF8Encoding(false));assembledPending=false;SessionState.EraseString(prefix+"active"); } };
            EditorApplication.update+=callback; return record;
        }

        static void CaptureAssembled(AssembledReport report)
        {
            var scenes=OriginalScenes();string sceneMemory=SceneMemory(scenes),settings=RenderState(); var scene=SceneManager.GetSceneByPath(Valley);
            var extra=AssetDatabase.GetDependencies(report.prefab,true).Concat(new[]{report.prefab}).SelectMany(p=>File.Exists(p+".meta")?new[]{p,p+".meta"}:new[]{p}).Where(File.Exists);
            var frozen=Protected().Concat(extra.Select(p=>new FileRow {path=p,sha256=Hash(p),bytes=new FileInfo(p).Length})).GroupBy(f=>f.path,StringComparer.Ordinal).Select(g=>g.First()).OrderBy(f=>f.path,StringComparer.Ordinal).ToArray();report.protectedBefore=frozen;
            var watches=MaterialMemory(frozen).Select(p=> {AssetDatabase.TryGetGUIDAndLocalFileIdentifier(p.Key,out string guid,out long id);return new MaterialWatch {original=p.Key,row=new MaterialChange {name=p.Key.name,asset=AssetDatabase.GetAssetPath(p.Key),guid=guid,localId=id,originalInstanceId=p.Key.GetInstanceID(),beforeJson=p.Value}};}).ToArray();
            var images=new List<string>(); var poses=new List<AssembledPose>();
            try
            {
                using(var stage=new Stage())
                {
                    var candidate=stage.preview.InstantiatePrefabInScene(Required<GameObject>(report.prefab));stage.preview.camera.scene=candidate.scene;report.savedScale=new V(candidate.transform.localScale);stage.Materials(candidate);
                    var animator=candidate.GetComponentInChildren<Animator>(true);if(animator==null || animator.avatar==null || AssetDatabase.GetAssetPath(animator.avatar)!=Source+"Knight.fbx") throw new InvalidOperationException("Original native Knight Avatar required.");
                    if(candidate.GetComponentsInChildren<MonoBehaviour>(true).Length!=0 || candidate.GetComponentsInChildren<Collider>(true).Length!=0) throw new InvalidOperationException("Pure visual candidate required.");
                    var clips=AssetDatabase.LoadAllAssetsAtPath(Source+"Knight.fbx").OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview",StringComparison.Ordinal)).ToDictionary(c=>c.name,StringComparer.Ordinal);
                    var enemy=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Animator>(true)).Single(a=>a.GetComponentInParent<MeleeEnemyActor>()!=null && a.GetComponentInParent<MeleeEnemyActor>().name=="Enemy_Fogwalker_Courtyard_Support");
                    stage.Floor(Vector3.zero,new Vector3(8,.02f,6));
                    using(var rig=new Rig(animator))
                    {
                        rig.Start(clips["Idle"]);rig.Pose(0);var bounds=DrawnWorldBounds(candidate);report.idleDrawnMin=new V(bounds.min);report.idleDrawnMax=new V(bounds.max);
                        if(!report.environmentOnly)
                        {
                        // Do not align a production Editor T-pose by capsule centre: that clipped the old comparison's legs.
                        // This is the candidate at its saved origin, no floor-driven lift or borrowed Ranger acceptance.
                        var lineup=new Texture2D(1280,800,TextureFormat.RGB24,false);
                        try {for(int i=0;i<2;i++){stage.Camera(i==0?new Vector3(2.6f,1.7f,4.5f):new Vector3(-2.6f,1.7f,-4.5f),new Vector3(0,.85f,0),34);var viewImage=stage.Image(640,800);try{lineup.SetPixels(i*640,0,640,800,viewImage.GetPixels());}finally{Object.DestroyImmediate(viewImage);}}lineup.Apply();File.WriteAllBytes(report.directory+"/01-assembled-front-back.png",lineup.EncodeToPNG());images.Add("01-assembled-front-back.png");}
                        finally{Object.DestroyImmediate(lineup);}
                        var sheet=new Texture2D(1200,800,TextureFormat.RGB24,false);
                        try
                        {
                            string[] names={"Idle","1H_Melee_Attack_Chop","1H_Melee_Attack_Slice_Diagonal","Throw","Use_Item","Death_A"};float[] fractions={0,.5f,.5f,.392857f,.742857f,.999f};stage.Camera(new Vector3(2.6f,1.7f,4.5f),new Vector3(0,.85f,0),34);
                            for(int i=0;i<names.Length;i++) {var clip=clips[names[i]];float time=clip.length*fractions[i];rig.Start(clip);rig.Pose(time);poses.Add(new AssembledPose {clip=names[i],fraction=fractions[i],sourceTime=time,drawnMinY=DrawnWorldBounds(candidate).min.y,rawAnchorPosition=rig.PositionDrift,rawAnchorRotation=rig.RotationDrift});var image=stage.Image(400,400);try{sheet.SetPixels(i%3*400,(1-i/3)*400,400,400,image.GetPixels());}finally{Object.DestroyImmediate(image);}}
                            sheet.Apply();File.WriteAllBytes(report.directory+"/02-assembled-native-poses.png",sheet.EncodeToPNG());images.Add("02-assembled-native-poses.png");
                        }
                        finally{Object.DestroyImmediate(sheet);}
                        }
                        // First placement intersected the real posture shrine's authored Editor markers; retain that raw image.
                        // Move only this preview clone to clear courtyard paving; all shrine geometry stays cloned and unchanged.
                        stage.HideFloor(); var enemyActor=enemy.GetComponentInParent<MeleeEnemyActor>().transform;Vector3 position=new Vector3(enemyActor.position.x+1f,0,enemyActor.position.z-1f);
                        foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Where(r=>r.enabled && r.gameObject.activeInHierarchy && r.GetComponent<MeshFilter>()?.sharedMesh!=null && Vector3.Distance(r.bounds.ClosestPoint(position),position)<12))
                        {
                            if(r.GetComponentInParent<CombatTarget>()!=null || r.GetComponentInParent<Animator>()!=null){report.excludedActorRenderers++;continue;}
                            stage.CopyVisual(r.gameObject,Vector3.zero,Quaternion.identity,r);report.copiedEnvironmentRenderers++;
                        }
                        stage.CopyVisual(enemy.gameObject,Vector3.zero,Quaternion.identity);candidate.transform.position=position;rig.Start(clips["Idle"]);rig.Pose(0);
                        var formal=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ThirdPersonCameraRig>(true)).First();var serialized=new SerializedObject(formal);
                        float distance=serialized.FindProperty("_distance").floatValue,pivot=serialized.FindProperty("_pivotHeight").floatValue,pitch=(float)typeof(ThirdPersonCameraRig).GetField("_pitch",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(formal);
                        report.cameraFieldOfView=formal.GetComponent<Camera>().fieldOfView;Vector3 target=position+Vector3.up*pivot,view=target-Quaternion.Euler(pitch,-15,0)*Vector3.forward*Mathf.Min(distance,2.8f);
                        report.cameraTarget=new V(target);report.cameraPosition=new V(view);stage.Camera(view,target,report.cameraFieldOfView);stage.Save(report.directory+"/03-assembled-controlled-courtyard.png",1280,800);images.Add("03-assembled-controlled-courtyard.png");
                    }
                    foreach(var w in watches){w.row.beforeCleanupReached=true;w.row.aliveBeforeCleanup=w.original!=null;w.row.beforeCleanupJson=w.original==null?null:EditorJsonUtility.ToJson(w.original);}
                }
            }
            finally
            {
                report.images=images.ToArray();report.poses=poses.ToArray();report.protectedAfter=frozen.Select(f=>new FileRow{path=f.path,sha256=Hash(f.path),bytes=new FileInfo(f.path).Length}).ToArray();report.sourcesUnchanged=frozen.Zip(report.protectedAfter,(a,b)=>a.path==b.path && a.sha256==b.sha256 && a.bytes==b.bytes).All(b=>b);
                foreach(var w in watches){w.row.aliveAfterCleanup=w.original!=null;w.row.afterCleanupJson=w.original==null?null:EditorJsonUtility.ToJson(w.original);}
                report.materialChanges=watches.Where(w=>!w.row.aliveAfterCleanup || w.row.beforeJson!=w.row.afterCleanupJson || (w.row.beforeCleanupReached && (!w.row.aliveBeforeCleanup || w.row.beforeJson!=w.row.beforeCleanupJson))).Select(w=>w.row).ToArray();report.sourceMaterialMemoryUnchanged=report.materialChanges.Length==0;
                report.loadedScenesUnchanged=sceneMemory==SceneMemory(scenes) && SameScenes(scenes);report.renderSettingsUnchanged=settings==RenderState();
                if(!report.sourcesUnchanged || !report.sourceMaterialMemoryUnchanged || !report.loadedScenesUnchanged || !report.renderSettingsUnchanged)throw new InvalidOperationException("Candidate preview protection failed; full changed material JSON retained, no masking/restoration.");
            }
        }
        [Serializable] sealed class V { public float x, y, z; public V(Vector3 v) { x = v.x; y = v.y; z = v.z; } }
        [Serializable] sealed class Q { public float x, y, z, w; public Q(Quaternion q) { x=q.x; y=q.y; z=q.z; w=q.w; } }
        [Serializable] sealed class FileRow { public string path, sha256; public long bytes; }
        [Serializable] sealed class Bone { public string name, path, parent; public V position, scale; public Q rotation; }
        [Serializable] sealed class Geometry { public string path, mesh, category; public bool enabled, active; public int vertices, submeshes, slots; public long indices; public V lossyScale, sourceBoundsSize, drawnWorldMin, drawnWorldMax; }
        [Serializable] sealed class Frame { public float time, minY, rawAnimatorPositionDrift, rawAnimatorRotationDrift, hipsFromStartXZ, hipsFromIdleXZ; public V hips, leftHand, rightHand; }
        [Serializable] sealed class Floor { public string renderer, category; public float minY, time; public int activeSamples; }
        [Serializable] sealed class Motion
        {
            public string name, asset, guid; public long localId;
            public bool human, legacy, looping, hasRootCurves, loopTime, loopBlend, lockRootRotation, lockRootHeightY, lockRootPositionXZ,
                keepOriginalOrientation, keepOriginalPositionY, keepOriginalPositionXZ, heightFromFeet,
                loopBlendOrientation, loopBlendPositionY, loopBlendPositionXZ;
            public float length, sourceFrameRate, importedStart, importedStop, orientationOffsetY, level, maxRawAnchorPosition,
                maxRawAnchorRotation, maxHipsFromStartXZ, finalHipsFromStartXZ, maxHipsFromIdleXZ;
            public int floatBindings, objectBindings, sampledFrames;
            public string[] missingBindings; public Frame[] frames; public Floor[] floors;
        }
        [Serializable] sealed class Model
        {
            public string name, source, sourceGuid, avatarPath, avatarGuid, importerType, avatarSetup, compression, rigErrors, importWarnings;
            public long avatarLocalId; public bool avatarValid, avatarHuman, technicalReady;
            public int nativeClips, mappedHumanBones; public float humanScale; public V sourceRootPosition, sourceRootScale, restMin, restMax;
            public Q sourceRootRotation; public string[] allTransformPaths, nativeClipNames, fatal;
            public Bone[] limbs; public Geometry[] geometry; public Motion[] motions;
        }
        [Serializable] sealed class Production { public string role, sourceSceneObject, avatarPath, poseScope; public V actorPosition, animatorLossyScale, drawnWorldMin, drawnWorldMax; }
        [Serializable] sealed class MaterialChange
        {
            public string name, asset, guid, beforeJson, beforeCleanupJson, afterCleanupJson;
            public long localId; public int originalInstanceId;
            public bool beforeCleanupReached, aliveBeforeCleanup, aliveAfterCleanup;
        }
        sealed class MaterialWatch { public Material original; public MaterialChange row; }
        [Serializable] sealed class Report
        {
            public string label, directory, status, error, startedUtc, finishedUtc, loadedModule, scope =
                "Same-source native FBX controlled Edit-mode/120Hz Manual Playables; original scale, root-motion disabled, no per-pose root reset/lift. CPU drawn-index bounds and CPU snapshots, not natural movement/contact/HitStop/input/knife grip/heal resolution/difficulty/network/performance acceptance.";
            public bool sourcesUnchanged, sourceMaterialMemoryUnchanged, loadedScenesUnchanged, renderSettingsUnchanged, limbPathsSame, limbRestCompared;
            public float maxRestPositionDifference, maxRestRotationDifference, maxRestScaleDifference, groundReferenceY;
            public string[] restDifferences, images, sheetCells, gaps; public int totalNativeClips, totalSampledFrames;
            public Model[] models; public Production[] production; public FileRow[] protectedBefore, protectedAfter;
            public MaterialChange[] materialChanges;
            public V environmentCandidatePosition, environmentCameraPosition, environmentCameraTarget;
            public float cameraDistance, cameraPivotHeight, cameraPitch, cameraFieldOfView;
        }

        /// <summary>Returns report.json immediately; duplicate labels never repeat the work.</summary>
        public static string Request(string uniqueLabel)
        {
            if (string.IsNullOrWhiteSpace(uniqueLabel) || !Regex.IsMatch(uniqueLabel, "^[A-Za-z0-9_-]{8,72}$")) throw new ArgumentException("Unique ASCII label required.");
            string key=JobPrefix+uniqueLabel, prior=SessionState.GetString(key, "");
            if (prior.Length != 0) { if(!File.Exists(prior)) throw new IOException("Retained job record missing: "+prior); return prior; }
            if (pending || throwPending || genericPending) throw new InvalidOperationException("One native-character review at a time.");
            RequireIdle(); RequireNoRetainedGeneric();
            string active=SessionState.GetString(JobPrefix+"active", "");
            if (File.Exists(active)) { var old=JsonUtility.FromJson<Report>(File.ReadAllText(active)); if(old.status=="queued" || old.status=="running") throw new InvalidOperationException("Inspect retained interrupted/pending job first: "+active); }
            string dir=Path.GetFullPath("Builds/ArtReview/kaykit-native/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+uniqueLabel);
            if(Directory.Exists(dir)) throw new IOException("Never overwrite evidence."); Directory.CreateDirectory(dir);
            string record=dir+"/report.json"; var report=new Report { label=uniqueLabel, directory=dir, status="queued", startedUtc=DateTime.UtcNow.ToString("O"), loadedModule=typeof(KayKitNativeCharacterReview).Assembly.ManifestModule.ModuleVersionId.ToString() };
            Write(record,report); SessionState.SetString(key,record); SessionState.SetString(JobPrefix+"active",record); pending=true;
            double deadline=EditorApplication.timeSinceStartup+180; EditorApplication.CallbackFunction callback=null;
            callback=()=>
            {
                if((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup<deadline) return;
                EditorApplication.update-=callback;
                try { RequireIdle(); report.status="running"; Write(record,report); Capture(report); report.status="completed"; }
                catch(Exception ex) { report.status="failed"; report.error=ex.ToString(); Debug.LogException(ex); }
                finally { report.finishedUtc=DateTime.UtcNow.ToString("O"); Write(record,report); pending=false; SessionState.EraseString(JobPrefix+"active"); }
            };
            EditorApplication.update+=callback; return record;
        }

        static void Capture(Report report)
        {
            var scene=SceneManager.GetSceneByPath(Valley); var scenes=OriginalScenes(); string sceneMemory=SceneMemory(scenes), settings=RenderState();
            var frozen=Protected(); report.protectedBefore=frozen; var materialMemory=MaterialMemory(frozen);
            var materialWatches=materialMemory.Select(p=>
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(p.Key,out string guid,out long id);
                return new MaterialWatch { original=p.Key,row=new MaterialChange { name=p.Key.name,asset=AssetDatabase.GetAssetPath(p.Key),guid=guid,localId=id,originalInstanceId=p.Key.GetInstanceID(),beforeJson=p.Value } };
            }).ToArray();
            var models=new List<Model>(); var production=new List<Production>(); var images=new List<string>();
            try
            {
                using(var stage=new Stage())
                {
                    var instances=new List<GameObject>(); var idleBounds=new List<Bounds>();
                    foreach(string name in Actors)
                    {
                        var asset=Required<GameObject>(Source+name+".fbx"); var instance=stage.preview.InstantiatePrefabInScene(asset); stage.preview.camera.scene=instance.scene;
                        var model=Inspect(instance,name); models.Add(model); report.models=models.ToArray();
                        stage.Materials(instance); instances.Add(instance);
                        if(!model.technicalReady) throw new InvalidOperationException("Native technical preflight failed: "+name+" "+string.Join(";",model.fatal));
                    }
                    CompareRest(models[0],models[1],report);
                    for(int i=0;i<instances.Count;i++)
                    {
                        var root=instances[i]; var animator=root.GetComponentInChildren<Animator>(true);
                        var clips=AssetDatabase.LoadAllAssetsAtPath(models[i].source).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview",StringComparison.Ordinal)).ToDictionary(c=>c.name,StringComparer.Ordinal);
                        var results=new List<Motion>();
                        using(var rig=new Rig(animator)) using(var geometry=new Drawn(root))
                        {
                            rig.Start(clips["Idle"]); rig.Pose(0); Vector3 idleHip=Hip(animator,root); idleBounds.Add(geometry.Bounds());
                            foreach(string name in Motions)
                            {
                                var clip=clips[name]; Motion motion=Describe(clip,root); results.Add(motion); models[i].motions=results.ToArray();
                                rig.Start(clip); int steps=Mathf.CeilToInt(clip.length*120f); var frames=new List<Frame>(steps+1); var floors=new Dictionary<string,Floor>(StringComparer.Ordinal);
                                Vector3 startHip=Vector3.zero;
                                for(int frame=0;frame<=steps;frame++)
                                {
                                    float time=Mathf.Min(frame/120f,clip.length); rig.Pose(time); Vector3 hips=Hip(animator,root); if(frame==0) startHip=hips;
                                    float min=geometry.Minima(floors,time); float fromStart=Planar(hips-startHip), fromIdle=Planar(hips-idleHip);
                                    frames.Add(new Frame { time=time,minY=min,rawAnimatorPositionDrift=rig.PositionDrift,rawAnimatorRotationDrift=rig.RotationDrift,
                                        hipsFromStartXZ=fromStart,hipsFromIdleXZ=fromIdle,hips=new V(hips),leftHand=new V(Hand(animator,HumanBodyBones.LeftHand,root)),rightHand=new V(Hand(animator,HumanBodyBones.RightHand,root)) });
                                    motion.maxRawAnchorPosition=Mathf.Max(motion.maxRawAnchorPosition,rig.PositionDrift); motion.maxRawAnchorRotation=Mathf.Max(motion.maxRawAnchorRotation,rig.RotationDrift);
                                    motion.maxHipsFromStartXZ=Mathf.Max(motion.maxHipsFromStartXZ,fromStart); motion.maxHipsFromIdleXZ=Mathf.Max(motion.maxHipsFromIdleXZ,fromIdle); motion.finalHipsFromStartXZ=fromStart;
                                }
                                motion.frames=frames.ToArray(); motion.floors=floors.Values.OrderBy(f=>f.renderer,StringComparer.Ordinal).ToArray(); motion.sampledFrames=frames.Count; report.totalSampledFrames+=frames.Count;
                            }
                            rig.Start(clips["Idle"]); rig.Pose(0);
                        }
                        report.totalNativeClips+=models[i].nativeClips;
                    }
                    var references=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Animator>(true)).Where(a=>a.GetComponentInParent<PlayerCombatActor>()!=null || a.GetComponentInParent<MeleeEnemyActor>()!=null || a.GetComponentInParent<WardenActor>()!=null).ToArray();
                    var chosen=new[] { references.First(a=>a.GetComponentInParent<PlayerCombatActor>()!=null), references.Single(a=>a.GetComponentInParent<MeleeEnemyActor>()!=null && a.GetComponentInParent<MeleeEnemyActor>().name=="Enemy_Fogwalker_Courtyard_Support") };
                    var neutral=stage.New("NeutralLineup");
                    for(int i=0;i<instances.Count;i++) { instances[i].transform.SetParent(neutral.transform,true); instances[i].transform.position+=Vector3.right*(i==0?-2.3f:-.8f); }
                    for(int i=0;i<chosen.Length;i++)
                    {
                        var actor=chosen[i].GetComponentInParent<PlayerCombatActor>()!=null ? chosen[i].GetComponentInParent<PlayerCombatActor>().transform : chosen[i].GetComponentInParent<MeleeEnemyActor>().transform;
                        Bounds b=DrawnWorldBounds(chosen[i].gameObject); production.Add(new Production { role=i==0?"Ranger":"Fogwalker",sourceSceneObject=scene.path+"::"+actor.name+"/"+PathOf(chosen[i].transform,actor),avatarPath=AssetDatabase.GetAssetPath(chosen[i].avatar),actorPosition=new V(actor.position),animatorLossyScale=new V(chosen[i].transform.lossyScale),drawnWorldMin=new V(b.min),drawnWorldMax=new V(b.max),poseScope="Currently loaded original Editor pose, not evaluated/mutated; actual world scale, not gameplay idle acceptance." });
                        var copy=stage.CopyVisual(chosen[i].gameObject,new Vector3((i==0?1f:2.7f)-actor.position.x,0,-actor.position.z),Quaternion.identity); copy.transform.SetParent(neutral.transform,true);
                    }
                    report.production=production.ToArray(); report.groundReferenceY=0; stage.Floor(Vector3.zero,new Vector3(12,.02f,8));
                    stage.Camera(new Vector3(0,2.1f,9.5f),new Vector3(0,1,0),42); stage.Save(report.directory+"/01-native-lineup.png",1400,800); images.Add("01-native-lineup.png");
                    neutral.SetActive(false); foreach(var instance in instances) { instance.transform.SetParent(null,true); instance.transform.position=Vector3.zero; instance.SetActive(false); }
                    // Twelve CPU snapshots, one sheet. Not a 12-frame animation/contact or technical acceptance gate.
                    var sheet=new Texture2D(1200,900,TextureFormat.RGB24,false);
                    try
                    {
                        string[] picks={ "Idle","Walking_A","1H_Melee_Attack_Chop","Death_A","Death_B","Dodge_Forward","Throw","Use_Item","2H_Melee_Attack_Spin","Block","Hit_A","1H_Melee_Attack_Stab" };
                        var root=instances[0]; root.SetActive(true); stage.Camera(new Vector3(3.1f,1.8f,4.8f),new Vector3(0,.9f,0),36);
                        using(var rig=new Rig(root.GetComponentInChildren<Animator>(true)))
                        {
                            var clips=AssetDatabase.LoadAllAssetsAtPath(models[0].source).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview",StringComparison.Ordinal)).ToDictionary(c=>c.name,StringComparer.Ordinal);
                            var cells=new List<string>();
                            for(int i=0;i<picks.Length;i++) { float fraction=picks[i]=="Throw"?.392857f:picks[i]=="Use_Item"?.742857f:picks[i].StartsWith("Death",StringComparison.Ordinal)?.95f:.5f; rig.Start(clips[picks[i]]); rig.Pose(clips[picks[i]].length*fraction); cells.Add("row="+(i/4)+", col="+(i%4)+", clip="+picks[i]+", fraction="+fraction+"; Throw/Use_Item are hypothetical full-length domain fits, NOT actual input/entry blend/resolve acceptance"); var image=stage.Image(300,300); try { sheet.SetPixels(i%4*300,(2-i/4)*300,300,300,image.GetPixels()); } finally { Object.DestroyImmediate(image); } }
                            report.sheetCells=cells.ToArray();
                        }
                        sheet.Apply(); File.WriteAllBytes(report.directory+"/02-native-12-poses.png",sheet.EncodeToPNG()); images.Add("02-native-12-poses.png");
                    }
                    finally { Object.DestroyImmediate(sheet); foreach(var instance in instances) instance.SetActive(false); }
                    stage.HideFloor(); EnvironmentShot(stage,scene,instances[0],chosen[1],report); images.Add("03-controlled-current-environment.png");
                    report.images=images.ToArray();
                    report.gaps=new[] { "No candidate Profile/controller/derived clips or actual gameplay input. No production replacement; Ranger and r4 remain unchanged.",
                        "Throw: hand trajectories only. No held-knife socket/hand-to-authority-origin/0.22s release/grip acceptance.",
                        "Use_Item: no bottle/mouth anchor or resolve-frame distance/real R-input/interruption acceptance.",
                        "Native hips maximum offset and travelled distance are different. Existing .4m/.1m Ranger tests are NOT automatically a universal native-clip gate.",
                        "Dodge_Forward only dense-sampled; no diagonal input, domain displacement/iframe/recovery retiming acceptance. Other three native directions remain unmeasured.",
                        "Heavy/Sweep/Execution mappings and attack/contact/recovery transitions unselected. Nonlooping Idle/walk/run may need owned loop derivation.",
                        "Production images use actual original Editor pose and cloned static world; no AI/lighting/camera PlayerLoop or natural arrival/network/second Peer acceptance.",
                        "Humanoid importer warnings and source compression retained. Bone-name agreement alone is not full path/rest/pose equivalence; see measured rest rows.",
                        "No finger bones: source hand slots exist, but prior Ranger/Tiny grip/contact evidence does not transfer.",
                        "Whole-Chop/.28=3.81 and Diagonal/.30=3.33 exceed presenter initial max3: direct full-clip hooking is not ready; owned derivation/transition design needed. Coordinator itself does not clamp.",
                        "Death_B length2.633s exceeds existing2.2s offline respawn at speed1; Death_A preferred ONLY if its actual drawn-floor and pose evidence is suitable.",
                        "Three PNGs use temporary URP Lit materials with original atlas/color/UV transforms; do not certify original shader or exact production lighting." };
                    foreach(var watch in materialWatches)
                    {
                        watch.row.beforeCleanupReached=true; watch.row.aliveBeforeCleanup=watch.original!=null;
                        watch.row.beforeCleanupJson=watch.original==null?null:EditorJsonUtility.ToJson(watch.original);
                    }
                }
            }
            finally
            {
                report.models=models.ToArray(); report.production=production.ToArray(); report.images=images.ToArray(); report.protectedAfter=frozen.Select(f=>new FileRow { path=f.path,sha256=Hash(f.path),bytes=new FileInfo(f.path).Length }).ToArray();
                report.sourcesUnchanged=frozen.Zip(report.protectedAfter,(a,b)=>a.path==b.path && a.sha256==b.sha256 && a.bytes==b.bytes).All(x=>x);
                report.sourceMaterialMemoryUnchanged=materialMemory.All(p=>p.Key!=null && p.Value==EditorJsonUtility.ToJson(p.Key));
                foreach(var watch in materialWatches) { watch.row.aliveAfterCleanup=watch.original!=null; watch.row.afterCleanupJson=watch.original==null?null:EditorJsonUtility.ToJson(watch.original); }
                report.materialChanges=materialWatches.Where(w=>!w.row.aliveAfterCleanup || w.row.beforeJson!=w.row.afterCleanupJson || (w.row.beforeCleanupReached && (!w.row.aliveBeforeCleanup || w.row.beforeJson!=w.row.beforeCleanupJson))).Select(w=>w.row).ToArray();
                report.loadedScenesUnchanged=sceneMemory==SceneMemory(scenes) && SameScenes(scenes); report.renderSettingsUnchanged=settings==RenderState();
                if(!report.sourcesUnchanged || !report.sourceMaterialMemoryUnchanged || !report.loadedScenesUnchanged || !report.renderSettingsUnchanged) throw new InvalidOperationException("Protected source/material/scene/render state changed; retained evidence, no restoration of unknown user state.");
            }
        }

        static Model Inspect(GameObject root,string name)
        {
            string path=Source+name+".fbx"; var importer=(ModelImporter)AssetImporter.GetAtPath(path); var animator=root.GetComponentInChildren<Animator>(true); var fatal=new List<string>();
            var model=new Model { name=name,source=path,sourceGuid=AssetDatabase.AssetPathToGUID(path),importerType=importer.animationType.ToString(),avatarSetup=importer.avatarSetup.ToString(),compression=importer.animationCompression.ToString(),rigErrors=importer.GetSerializedString("rigImportErrors"),importWarnings=importer.GetSerializedString("animationImportWarnings"),sourceRootPosition=new V(root.transform.localPosition),sourceRootScale=new V(root.transform.localScale),sourceRootRotation=new Q(root.transform.localRotation),mappedHumanBones=importer.humanDescription.human.Length };
            if(animator==null || animator.avatar==null) fatal.Add("Missing Animator/Avatar");
            else { model.avatarValid=animator.avatar.isValid; model.avatarHuman=animator.avatar.isHuman; model.avatarPath=AssetDatabase.GetAssetPath(animator.avatar); AssetDatabase.TryGetGUIDAndLocalFileIdentifier(animator.avatar,out string guid,out long id); model.avatarGuid=guid; model.avatarLocalId=id; if(!model.avatarValid || !model.avatarHuman) fatal.Add("Invalid/non-Human Avatar"); else model.humanScale=animator.humanScale; if(model.avatarPath!=path) fatal.Add("Avatar is not embedded in this source FBX"); }
            var transforms=root.GetComponentsInChildren<Transform>(true); model.allTransformPaths=transforms.Select(t=>PathOf(t,root.transform)).OrderBy(p=>p,StringComparer.Ordinal).ToArray(); var bones=new List<Bone>();
            foreach(string limb in Limbs)
            {
                var found=transforms.Where(t=>t.name==limb).ToArray(); if(found.Length!=1) { fatal.Add("Limb absent/ambiguous: "+limb); continue; } var t=found[0];
                bones.Add(new Bone { name=limb,path=PathOf(t,root.transform),parent=t.parent==null?"":PathOf(t.parent,root.transform),position=new V(t.localPosition),rotation=new Q(t.localRotation),scale=new V(t.localScale) });
            }
            model.limbs=bones.ToArray(); var clips=AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview",StringComparison.Ordinal)).ToArray(); model.nativeClips=clips.Length; model.nativeClipNames=clips.Select(c=>c.name).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
            foreach(string selected in Motions) { var found=clips.Where(c=>c.name==selected).ToArray(); if(found.Length!=1) fatal.Add("Native clip absent/ambiguous: "+selected); else { var missing=MissingBindings(found[0],root); if(missing.Length!=0) fatal.Add(selected+": "+string.Join(";",missing)); } }
            using(var drawn=new Drawn(root)) { var b=drawn.Bounds(); model.restMin=new V(b.min); model.restMax=new V(b.max); model.geometry=drawn.Describe(); if(b.size.y<.2f || b.size.y>5f) fatal.Add("Implausible original world-scale drawn height "+b.size.y); }
            model.fatal=fatal.ToArray(); model.technicalReady=fatal.Count==0; return model;
        }
        static string[] MissingBindings(AnimationClip clip,GameObject root)
        {
            var missing=new List<string>(); foreach(var binding in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
            { var t=binding.path.Length==0?root.transform:root.transform.Find(binding.path); if(t==null || (binding.type!=typeof(Transform) && t.GetComponent(binding.type)==null)) missing.Add(binding.type.FullName+"|"+binding.path+"|"+binding.propertyName); }
            return missing.Distinct().ToArray();
        }
        static Motion Describe(AnimationClip clip,GameObject root)
        {
            var s=AnimationUtility.GetAnimationClipSettings(clip); AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip,out string guid,out long id);
            var importer=(ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)); var entries=importer.clipAnimations.Length==0?importer.defaultClipAnimations:importer.clipAnimations; var authored=entries.Single(e=>e.name==clip.name);
            return new Motion { name=clip.name,asset=AssetDatabase.GetAssetPath(clip),guid=guid,localId=id,human=clip.isHumanMotion,legacy=clip.legacy,length=clip.length,sourceFrameRate=clip.frameRate,looping=clip.isLooping,hasRootCurves=clip.hasRootCurves,importedStart=s.startTime,importedStop=s.stopTime,loopTime=s.loopTime,loopBlend=s.loopBlend,lockRootRotation=authored.lockRootRotation,lockRootHeightY=authored.lockRootHeightY,lockRootPositionXZ=authored.lockRootPositionXZ,loopBlendOrientation=s.loopBlendOrientation,loopBlendPositionY=s.loopBlendPositionY,loopBlendPositionXZ=s.loopBlendPositionXZ,keepOriginalOrientation=s.keepOriginalOrientation,keepOriginalPositionY=s.keepOriginalPositionY,keepOriginalPositionXZ=s.keepOriginalPositionXZ,heightFromFeet=s.heightFromFeet,orientationOffsetY=s.orientationOffsetY,level=s.level,floatBindings=AnimationUtility.GetCurveBindings(clip).Length,objectBindings=AnimationUtility.GetObjectReferenceCurveBindings(clip).Length,missingBindings=MissingBindings(clip,root) };
        }
        static void CompareRest(Model a,Model b,Report report)
        {
            var differences=new List<string>(); report.limbPathsSame=a.limbs.Length==Limbs.Length && b.limbs.Length==Limbs.Length; report.limbRestCompared=true;
            foreach(var x in a.limbs) { var y=b.limbs.SingleOrDefault(t=>t.name==x.name); if(y==null) { report.limbPathsSame=false; differences.Add(x.name+":missing"); continue; }
                bool path=x.path==y.path && x.parent==y.parent; report.limbPathsSame&=path; float p=Vector3.Distance(Vec(x.position),Vec(y.position)), q=Quaternion.Angle(Quat(x.rotation),Quat(y.rotation)), s=Vector3.Distance(Vec(x.scale),Vec(y.scale));
                report.maxRestPositionDifference=Mathf.Max(report.maxRestPositionDifference,p); report.maxRestRotationDifference=Mathf.Max(report.maxRestRotationDifference,q); report.maxRestScaleDifference=Mathf.Max(report.maxRestScaleDifference,s); if(!path || p>1e-6f || q>.001f || s>1e-6f) differences.Add(x.name+": paths="+path+", position="+p+", deg="+q+", scale="+s); }
            report.restDifferences=differences.ToArray();
        }
        sealed class Rig : IDisposable
        {
            readonly Animator animator; readonly Vector3 position, worldPosition; readonly Quaternion rotation, worldRotation; PlayableGraph graph; AnimationClipPlayable playable;
            public float PositionDrift => Vector3.Distance(animator.transform.localPosition,position);
            public float RotationDrift => Quaternion.Angle(animator.transform.localRotation,rotation);
            public float WorldPositionDrift => Vector3.Distance(animator.transform.position, worldPosition);
            public float WorldRotationDrift => Quaternion.Angle(animator.transform.rotation, worldRotation);
            public float PlaybackRate => (float)playable.GetSpeed();
            public Rig(Animator animator) { this.animator=animator; animator.runtimeAnimatorController=null; animator.applyRootMotion=false; animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; position=animator.transform.localPosition; rotation=animator.transform.localRotation; worldPosition=animator.transform.position; worldRotation=animator.transform.rotation; }
            public void Start(AnimationClip clip)
            {
                if(graph.IsValid()) graph.Destroy(); animator.Rebind(); animator.Update(0); graph=PlayableGraph.Create("KayKit native controlled"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable=AnimationClipPlayable.Create(graph,clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false); AnimationPlayableOutput.Create(graph,"Native",animator).SetSourcePlayable(playable); graph.Play();
            }
            // Deliberately DOES NOT reset/lift the animator anchor after evaluation; drift is measured before any correction.
            public void Pose(float time) { playable.SetTime(time); graph.Evaluate(0); }
            public void Dispose() { if(graph.IsValid()) graph.Destroy(); }
        }
        sealed partial class Drawn : IDisposable
        {
            sealed class Part { public Renderer renderer; public Mesh source,scratch; public int[] indices; public List<Vector3> vertices; public string path,category; }
            readonly List<Part> parts=new List<Part>();
            public Drawn(GameObject root)
            {
                foreach(var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh m=r is SkinnedMeshRenderer skin?skin.sharedMesh:r.GetComponent<MeshFilter>()?.sharedMesh; if(m==null) continue;
                    var indices=new List<int>(); for(int s=0;s<m.subMeshCount;s++) if(m.GetTopology(s)==MeshTopology.Triangles) indices.AddRange(m.GetIndices(s));
                    if(indices.Count==0) continue; string path=PathOf(r.transform,root.transform);
                    parts.Add(new Part { renderer=r,source=m,scratch=r is SkinnedMeshRenderer?new Mesh { hideFlags=HideFlags.HideAndDontSave }:null,indices=indices.Distinct().ToArray(),vertices=new List<Vector3>(m.vertexCount),path=path,category=Category(path) });
                }
            }
            Bounds PartBounds(Part p)
            {
                Mesh m=p.source; if(p.scratch!=null) { ((SkinnedMeshRenderer)p.renderer).BakeMesh(p.scratch,true); m=p.scratch; }
                m.GetVertices(p.vertices); bool first=true; Bounds b=default; var matrix=p.renderer.localToWorldMatrix;
                foreach(int index in p.indices) { var point=matrix.MultiplyPoint3x4(p.vertices[index]); if(first) { b=new Bounds(point,Vector3.zero); first=false; } else b.Encapsulate(point); } return b;
            }
            public Bounds Bounds() { bool first=true; Bounds b=default; foreach(var p in parts.Where(p=>p.renderer.enabled && p.renderer.gameObject.activeInHierarchy)) { var x=PartBounds(p); if(first) { b=x; first=false; } else b.Encapsulate(x); } if(first) throw new InvalidOperationException("No active drawn triangles."); return b; }
            public Bounds BodyBounds() { bool first = true; Bounds b = default; foreach (var p in parts.Where(p => p.renderer.enabled && !p.renderer.forceRenderingOff && p.renderer.gameObject.activeInHierarchy && !p.category.StartsWith("named equipment", StringComparison.Ordinal) && !p.category.StartsWith("separate named cape", StringComparison.Ordinal))) { var x = PartBounds(p); if (first) { b = x; first = false; } else b.Encapsulate(x); } if (first) throw new InvalidOperationException("No actual body/helmet triangles."); return b; }
            public float Minima(Dictionary<string,Floor> floors,float time)
            {
                return Minima(floors,time,out _,out _,out _);
            }
            public float Minima(Dictionary<string,Floor> floors,float time,out float bodyMin,out float capeMin,out float gearMin)
            {
                bodyMin=capeMin=gearMin=float.PositiveInfinity;
                float min=float.PositiveInfinity; foreach(var p in parts.Where(p=>p.renderer.enabled && p.renderer.gameObject.activeInHierarchy))
                { float y=PartBounds(p).min.y; min=Mathf.Min(min,y);if(p.category.StartsWith("named equipment",StringComparison.Ordinal))gearMin=Mathf.Min(gearMin,y);else if(p.category.StartsWith("separate named cape",StringComparison.Ordinal))capeMin=Mathf.Min(capeMin,y);else bodyMin=Mathf.Min(bodyMin,y); if(!floors.TryGetValue(p.path,out var floor)) { floor=new Floor { renderer=p.path,category=p.category,minY=y,time=time }; floors.Add(p.path,floor); } if(y<floor.minY) { floor.minY=y; floor.time=time; } floor.activeSamples++; }
                if(float.IsInfinity(min)) throw new InvalidOperationException("No active pose geometry."); return min;
            }
            public Floor[] PerRendererFloors(float time) => parts.Where(p => p.renderer.enabled && !p.renderer.forceRenderingOff && p.renderer.gameObject.activeInHierarchy).Select(p => new Floor { renderer = p.path, category = p.category, minY = PartBounds(p).min.y, time = time, activeSamples = 1 }).ToArray();
            public Geometry[] Describe() => parts.Select(p=> { var b=PartBounds(p); long indices=0; for(int i=0;i<p.source.subMeshCount;i++) indices+=(long)p.source.GetIndexCount(i); return new Geometry { path=p.path,mesh=AssetDatabase.GetAssetPath(p.source)+"::"+p.source.name,category=p.category,enabled=p.renderer.enabled,active=p.renderer.gameObject.activeInHierarchy,vertices=p.source.vertexCount,submeshes=p.source.subMeshCount,slots=p.renderer.sharedMaterials.Length,indices=indices,lossyScale=new V(p.renderer.transform.lossyScale),sourceBoundsSize=new V(p.source.bounds.size),drawnWorldMin=new V(b.min),drawnWorldMax=new V(b.max) }; }).ToArray();
            public void Dispose() { foreach(var p in parts) if(p.scratch!=null) Object.DestroyImmediate(p.scratch); }
        }
        static Bounds DrawnWorldBounds(GameObject root) { using(var d=new Drawn(root)) return d.Bounds(); }
        static string Category(string path) { if(Regex.IsMatch(path,"Sword|Shield|Axe|Dagger|Staff|Wand|Crossbow|Arrow",RegexOptions.IgnoreCase)) return "named equipment branch (semantic review pending)"; if(path.IndexOf("Cape",StringComparison.OrdinalIgnoreCase)>=0) return "separate named cape renderer"; return "combined body/cloth/hair unless separately identified in rendered review"; }

        sealed partial class Stage : IDisposable
        {
            public readonly PreviewRenderUtility preview=new PreviewRenderUtility(); readonly List<Object> owned=new List<Object>(); readonly Dictionary<Material,Material> materials=new Dictionary<Material,Material>(); readonly List<GameObject> floors=new List<GameObject>();
            public Stage() { preview.ambientColor=new Color(.42f,.42f,.42f); preview.lights[0].intensity=1.2f; preview.lights[0].transform.rotation=Quaternion.Euler(40,30,0); preview.lights[1].intensity=.6f; preview.lights[1].transform.rotation=Quaternion.Euler(20,210,0); preview.camera.clearFlags=CameraClearFlags.SolidColor; preview.camera.backgroundColor=new Color(.29f,.34f,.38f); preview.camera.allowHDR=false; preview.camera.allowMSAA=false; preview.camera.GetUniversalAdditionalCameraData().renderPostProcessing=false; }
            public GameObject New(string name) { var go=EditorUtility.CreateGameObjectWithHideFlags(name,HideFlags.HideAndDontSave); preview.AddSingleGO(go); return go; }
            public void Materials(GameObject root) { foreach(var r in root.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials=r.sharedMaterials.Select(Clone).ToArray(); }
            Material Clone(Material source)
            {
                if(source==null) throw new InvalidOperationException("Missing native material."); if(materials.TryGetValue(source,out var result)) return result;
                result=new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags=HideFlags.HideAndDontSave,name="PreviewOnly_"+source.name }; owned.Add(result);
                Color color=source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.GetColor("_Color"):Color.white;
                Texture texture=source.HasProperty("_BaseMap")?source.GetTexture("_BaseMap"):source.HasProperty("_MainTex")?source.GetTexture("_MainTex"):null;
                result.SetColor("_BaseColor",color); result.SetTexture("_BaseMap",texture); result.SetFloat("_Smoothness",source.HasProperty("_Smoothness")?source.GetFloat("_Smoothness"):.15f); result.SetFloat("_Metallic",source.HasProperty("_Metallic")?source.GetFloat("_Metallic"):0);
                string property=source.HasProperty("_BaseMap")?"_BaseMap":"_MainTex"; if(source.HasProperty(property)) { result.SetTextureScale("_BaseMap",source.GetTextureScale(property)); result.SetTextureOffset("_BaseMap",source.GetTextureOffset(property)); }
                materials.Add(source,result); return result;
            }
            public GameObject CopyVisual(GameObject source,Vector3 translation,Quaternion rotation,Renderer onlyRenderer=null)
            {
                var root=New("StaticVisual_"+source.name);
                foreach(var r in (onlyRenderer!=null?new[] {onlyRenderer}:source.GetComponentsInChildren<Renderer>(true)).Where(r=>r.enabled && r.gameObject.activeInHierarchy))
                {
                    Mesh mesh=r is SkinnedMeshRenderer skin?skin.sharedMesh:r.GetComponent<MeshFilter>()?.sharedMesh; if(mesh==null) continue;
                    var vertices=new List<Vector3>(); Mesh baked=null;
                    try { if(r is SkinnedMeshRenderer sk) { baked=new Mesh(); sk.BakeMesh(baked,true); mesh=baked; } mesh.GetVertices(vertices);
                        var copy=Object.Instantiate(mesh); copy.hideFlags=HideFlags.HideAndDontSave; var transform=Matrix4x4.Rotate(rotation)*r.localToWorldMatrix;
                        for(int i=0;i<vertices.Count;i++) vertices[i]=transform.MultiplyPoint3x4(vertices[i])+translation; copy.SetVertices(vertices); copy.RecalculateBounds();
                        var normals=mesh.normals; var normalTransform=transform.inverse.transpose; for(int i=0;i<normals.Length;i++) normals[i]=normalTransform.MultiplyVector(normals[i]).normalized; copy.normals=normals;
                        var tangents=mesh.tangents; for(int i=0;i<tangents.Length;i++) { var t=transform.MultiplyVector(new Vector3(tangents[i].x,tangents[i].y,tangents[i].z)).normalized; tangents[i]=new Vector4(t.x,t.y,t.z,tangents[i].w*Mathf.Sign(transform.determinant)); } copy.tangents=tangents; owned.Add(copy);
                        var go=New("Visual_"+r.name); go.transform.SetParent(root.transform,false); go.AddComponent<MeshFilter>().sharedMesh=copy; var draw=go.AddComponent<MeshRenderer>(); draw.sharedMaterials=r.sharedMaterials.Select(Clone).ToArray(); draw.shadowCastingMode=r.shadowCastingMode; draw.receiveShadows=r.receiveShadows;
                    } finally { if(baked!=null) Object.DestroyImmediate(baked); }
                }
                return root;
            }
            public void Floor(Vector3 position,Vector3 size)
            {
                var mesh=new Mesh { hideFlags=HideFlags.HideAndDontSave }; mesh.vertices=new[] { new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(.5f,0,.5f),new Vector3(-.5f,0,.5f) }; mesh.triangles=new[] {0,2,1,0,3,2}; mesh.RecalculateNormals(); owned.Add(mesh);
                var material=new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags=HideFlags.HideAndDontSave }; material.SetColor("_BaseColor",new Color(.27f,.3f,.32f)); owned.Add(material);
                var go=New("ReferencePlaneY"+position.y); go.transform.position=position; go.transform.localScale=size; go.AddComponent<MeshFilter>().sharedMesh=mesh; go.AddComponent<MeshRenderer>().sharedMaterial=material; floors.Add(go);
            }
            public void HideFloor() { foreach(var floor in floors) floor.SetActive(false); }
            public void Camera(Vector3 position,Vector3 target,float fov) { var c=preview.camera; c.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position,Vector3.up)); c.fieldOfView=fov; c.nearClipPlane=.02f; c.farClipPlane=80; }
            public Texture2D Image(int width,int height)
            {
                var skins=preview.camera.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<SkinnedMeshRenderer>(true)).Where(s=>s.enabled && s.gameObject.activeInHierarchy).ToArray(); var snapshots=new List<GameObject>(); var meshes=new List<Mesh>(); bool open=false; var prior=RenderTexture.active; RenderTexture rt=null;
                try
                {
                    foreach(var skin in skins) { var mesh=new Mesh { hideFlags=HideFlags.HideAndDontSave }; skin.BakeMesh(mesh,true); meshes.Add(mesh); var go=New("CPU_"+skin.name); snapshots.Add(go); go.transform.SetParent(skin.transform,false); go.AddComponent<MeshFilter>().sharedMesh=mesh; var r=go.AddComponent<MeshRenderer>(); r.sharedMaterials=skin.sharedMaterials; r.shadowCastingMode=skin.shadowCastingMode; r.receiveShadows=skin.receiveShadows; skin.enabled=false; }
                    preview.BeginPreview(new Rect(0,0,width,height),GUIStyle.none); open=true; preview.Render(true,false); var texture=preview.EndPreview(); open=false;
                    rt=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB); Graphics.Blit(texture,rt); RenderTexture.active=rt; var image=new Texture2D(width,height,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply(); return image;
                }
                finally { if(open) preview.EndPreview(); RenderTexture.active=prior; if(rt!=null) RenderTexture.ReleaseTemporary(rt); foreach(var go in snapshots) Object.DestroyImmediate(go); foreach(var mesh in meshes) Object.DestroyImmediate(mesh); foreach(var skin in skins) skin.enabled=true; }
            }
            public void Save(string path,int width,int height) { var image=Image(width,height); try { File.WriteAllBytes(path,image.EncodeToPNG()); } finally { Object.DestroyImmediate(image); } }
            public void Dispose() { preview.Cleanup(); foreach(var item in owned) if(item!=null) Object.DestroyImmediate(item); }
        }
        static void EnvironmentShot(Stage stage,Scene scene,GameObject candidate,Animator enemy,Report report)
        {
            var actor=enemy.GetComponentInParent<MeleeEnemyActor>().transform; Vector3 position=new Vector3(actor.position.x-1.5f,0,actor.position.z-1f);
            foreach(var r in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshRenderer>(true)).Where(r=>r.enabled && r.gameObject.activeInHierarchy && r.GetComponentInParent<Animator>()==null && r.GetComponent<MeshFilter>()?.sharedMesh!=null && Vector3.Distance(r.bounds.ClosestPoint(position),position)<12f)) stage.CopyVisual(r.gameObject,Vector3.zero,Quaternion.identity,r);
            stage.CopyVisual(enemy.gameObject,Vector3.zero,Quaternion.identity); candidate.SetActive(true); candidate.transform.position=position;
            var rigSource=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ThirdPersonCameraRig>(true)).First(); var serialized=new SerializedObject(rigSource);
            report.cameraDistance=serialized.FindProperty("_distance").floatValue; report.cameraPivotHeight=serialized.FindProperty("_pivotHeight").floatValue;
            var pitch=typeof(ThirdPersonCameraRig).GetField("_pitch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic); if(pitch==null) throw new MissingFieldException("Current Editor camera pitch field missing."); report.cameraPitch=(float)pitch.GetValue(rigSource);
            var camera=rigSource.GetComponent<Camera>(); report.cameraFieldOfView=camera!=null?camera.fieldOfView:60;
            Vector3 target=position+Vector3.up*report.cameraPivotHeight; Vector3 view=target-Quaternion.Euler(report.cameraPitch,-15,0)*Vector3.forward*report.cameraDistance;
            report.environmentCandidatePosition=new V(position); report.environmentCameraPosition=new V(view); report.environmentCameraTarget=new V(target);
            using(var rig=new Rig(candidate.GetComponentInChildren<Animator>(true))) { var idle=AssetDatabase.LoadAllAssetsAtPath(Source+"Knight.fbx").OfType<AnimationClip>().Single(c=>c.name=="Idle"); rig.Start(idle); rig.Pose(0); stage.Camera(view,target,report.cameraFieldOfView); stage.Save(report.directory+"/03-controlled-current-environment.png",1280,800); }
        }

        static void RequireIdle()
        {
            if(Path.GetFullPath(UnityEngine.Application.dataPath).Replace('\\','/')!=Project+"/Assets") throw new InvalidOperationException("Wrong project.");
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Idle Editor required.");
            var scene=SceneManager.GetSceneByPath(Valley); if(!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Existing clean Valley must already be loaded; this tool will not open it.");
            foreach(var current in OriginalScenes()) if(current.isDirty) throw new InvalidOperationException("Preserve unsaved scene: "+current.path);
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset) || Shader.Find("Universal Render Pipeline/Lit")==null) throw new InvalidOperationException("Real URP graphics/Lit required.");
        }
        static FileRow[] Protected()
        {
            var files=new HashSet<string>(StringComparer.Ordinal); Action<string> add=p=> { if(File.Exists(p)) { files.Add(p.Replace('\\','/')); if(File.Exists(p+".meta")) files.Add((p+".meta").Replace('\\','/')); } };
            foreach(string p in Actors.Select(n=>Source+n+".fbx").Concat(new[] { Pack+"/LICENSE.txt",Pack+"/README.md",Pack+"/addons/kaykit_character_pack_adventures/LICENSE.txt","Assets/_Game/Scripts/Editor/Review/KayKitNativeCharacterReview.cs" })) { add(p); foreach(var dep in AssetDatabase.GetDependencies(p,true)) add(dep); }
            foreach(string dir in new[] { "ProjectSettings","Assets/_Game/Settings","Assets/_Game/Scenes","Assets/_Game/Prefabs/Characters","Assets/_Game/Resources/Networking" })
                foreach(string p in Directory.GetFiles(dir,"*",SearchOption.AllDirectories)) { add(p); if(p.EndsWith(".prefab",StringComparison.Ordinal) || p.EndsWith(".unity",StringComparison.Ordinal)) foreach(var dep in AssetDatabase.GetDependencies(p.Replace('\\','/'),true)) add(dep); }
            add(Archive); var result=files.OrderBy(p=>p,StringComparer.Ordinal).Select(p=>new FileRow { path=p,sha256=Hash(p),bytes=new FileInfo(p).Length }).ToArray();
            if(!result.Any(r=>r.path==Archive && r.sha256==ArchiveSha)) throw new InvalidOperationException("Protected delivered r4 archive missing/different."); return result;
        }
        static Dictionary<Material,string> MaterialMemory(FileRow[] files) => files.Where(f=>f.path.StartsWith("Assets/",StringComparison.Ordinal) && (f.path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase) || f.path.EndsWith(".fbx",StringComparison.OrdinalIgnoreCase))).SelectMany(f=>AssetDatabase.LoadAllAssetsAtPath(f.path).OfType<Material>()).Distinct().ToDictionary(m=>m,m=>EditorJsonUtility.ToJson(m));
        static Scene[] OriginalScenes() { var scenes=new List<Scene>(); for(int i=0;i<SceneManager.sceneCount;i++) { var scene=SceneManager.GetSceneAt(i); if(!EditorSceneManager.IsPreviewScene(scene)) scenes.Add(scene); } return scenes.ToArray(); }
        static bool SameScenes(Scene[] scenes) { var current=OriginalScenes(); return current.Length==scenes.Length && current.Select(s=>s.handle).SequenceEqual(scenes.Select(s=>s.handle)) && current.All(s=>!s.isDirty); }
        static string SceneMemory(Scene[] scenes) { var rows=new List<string>(); rows.Add("active="+SceneManager.GetActiveScene().handle); foreach(var scene in scenes) { if(!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Original scene lost."); foreach(var root in scene.GetRootGameObjects()) foreach(var t in root.GetComponentsInChildren<Transform>(true)) { rows.Add(t.GetInstanceID()+"|"+EditorJsonUtility.ToJson(t.gameObject)); foreach(var c in t.GetComponents<Component>()) rows.Add(c==null?"missing":c.GetInstanceID()+"|"+EditorJsonUtility.ToJson(c)); } } rows.Sort(StringComparer.Ordinal); return string.Join("\n",rows); }
        static string RenderState() => RenderSettings.ambientMode+"|"+RenderSettings.ambientLight+"|"+RenderSettings.ambientSkyColor+"|"+RenderSettings.ambientEquatorColor+"|"+RenderSettings.ambientGroundColor+"|"+RenderSettings.ambientIntensity+"|"+RenderSettings.fog+"|"+RenderSettings.fogColor+"|"+RenderSettings.fogDensity+"|"+(RenderSettings.sun==null?0:RenderSettings.sun.GetInstanceID());
        static T Required<T>(string path) where T:Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException(path);
        static string GetSerializedString(this ModelImporter importer,string name) { var property=new SerializedObject(importer).FindProperty("m_AnimationImportWarnings"); if(name=="rigImportErrors") property=new SerializedObject(importer).FindProperty("m_RigImportErrors"); return property?.stringValue ?? "not exposed by serialized importer; inspect original meta"; }
        static string PathOf(Transform t,Transform root) => AnimationUtility.CalculateTransformPath(t,root);
        static Vector3 Hip(Animator animator,GameObject root) => animator.GetBoneTransform(HumanBodyBones.Hips).position-root.transform.position;
        static Vector3 Hand(Animator animator,HumanBodyBones bone,GameObject root) => animator.GetBoneTransform(bone).position-root.transform.position;
        static float Planar(Vector3 v) => new Vector2(v.x,v.z).magnitude;
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static Vector3 Vec(V v) => new Vector3(v.x,v.y,v.z); static Quaternion Quat(Q q) => new Quaternion(q.x,q.y,q.z,q.w);
        static string Hash(string path) { using(var stream=File.OpenRead(path)) using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-",""); }
        static void Write(string path,Report report) => File.WriteAllText(path,JsonUtility.ToJson(report,true),new UTF8Encoding(false));
    }
}
