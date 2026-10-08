#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Reflection;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    public sealed partial class KayKitKnightInputCandidateTests
    {
        const string KnifeStylePath = "Assets/_Game/Art/Review/KayKitHero/knight-knife-trail-20261003-a1/A_Review_Knight_KnifeTrailStyle.asset";
        [Serializable] sealed class StyleAlpha { public float alpha, time; }
        [Serializable] sealed class StyleColor { public Color color; public float time; }
        [Serializable] sealed class StyleTrail
        {
            public int id, materialId; public string name, mode, colorSpace, widthCurve;
            public float time, width, spacing; public bool enabled, receiveShadows;
            public string alignment, textureMode, shadowMode;
            public StyleColor[] colors; public StyleAlpha[] alphas;
        }
        [Serializable] sealed class StylePair
        {
            public int projectileId; public StyleTrail sourceBefore, copyBefore, sourceStyled, copyStyled, sourceFinal, copyFinal;
            public bool sourceParametersUnchanged, copyUntargetedParametersUnchanged, styledParametersMatched;
        }
        sealed class RuntimeStylePair { public PlayerThrowingKnifeProjectile projectile; public TrailRenderer copy, source; public Transform poolParent; public StylePair audit; }
        [Serializable] sealed class StyleScaleAncestor
        {
            public int id, parentId; public string name; public Vector3 localScale;
        }
        [Serializable] sealed class StyleHitboxLocalTransform
        {
            public int id, parentId; public Vector3 localPosition, localScale; public Quaternion localRotation;
        }
        [Serializable] sealed class StyleProjectile
        {
            public int id, transformId, hitboxId, hitboxTransformId; public float spin; public Vector3 center, size, scale, localScale;
            public bool inFlight, hitboxOnProjectileRoot;
            public StyleScaleAncestor[] ancestors, poolAncestors;
            public StyleHitboxLocalTransform[] hitboxLocalChain;
        }
        [Serializable] sealed class StyleLauncher
        {
            public int launchOriginId, launchOriginParentId, prefabId, prewarm;
            public Vector3 launchLocal, launchWorld, launchLocalScale; public Quaternion launchLocalRotation; public float assistRadius; public string prefabPath;
            public StyleScaleAncestor[] launchAncestors;
            public StyleProjectile[] projectiles;
        }
        [Serializable] sealed class StyleRelease
        {
            public int entry, frame, sequence, releaseSequence; public double time;
            public float elapsed, damage, postureDamage, speed, range, expectedDamage, expectedPosture, expectedSpeed, expectedRange;
            public Vector3 launchWorld; public bool matchesTuning;
        }
        [Serializable] sealed class StyleQuery { public int entry; public KnifeQueryTrace.Entry actual; }
        [Serializable] sealed class StyleLife
        {
            public int entry, frame, projectiles, visuals, totalPoints; public double time;
            public string domain, stage; public bool held, flight, sword;
            public float bridgeAge; public Vector3 knifePosition; public int[] copies, sources, copyPoints, sourcePoints;
            public bool[] leases; public bool styledParametersMatched, authorityParametersUnchanged; public StyleLauncher authorConfiguration;
        }
        [Serializable] sealed class StyleReview
        {
            public string scope = "Isolated original Launcher/KnifePresentation and staged Knight input, copied-trail style only. Two controlled diagnostic camera views, NOT the formal ThirdPersonCameraRig, natural contact, palm grip, OS foreground/performance, NET, production Profile or build acceptance. Original three-entry clock/cold phase/signed floor/full Idle/lease gates remain unchanged.";
            public string timing = "Native TrailRenderer.time is seconds; style does not scale it by Animator.speed or freeze it. No authority, .22/.56 timing, .06 visual bridge, emitting/clear/return semantics, shader, material, knife geometry or pose changes.";
            public string transformAudit = "Projectile scale is raw derived lossyScale, retained before/after and every sampled EOF, not serialized author configuration. Exact configuration gates use localScale and complete ancestor identity/localScale chains. Only the original Launcher flight detach to a null parent and return to the same pool are permitted. Hitbox local TRS is frozen below its projectile coordinate frame; a hitbox on the projectile root has the same exact Transform identity and no independent local TRS. Projectile position/rotation remain normal flight motion, not static author fields.";
            public string status = "started", view, stylePath, styleGuid, styleFileBefore, styleFileAfter, styleMetaBefore, styleMetaAfter, styleMemoryBefore, styleMemoryAfter, output;
            public long styleLocalId; public float lifetime, widthScale, alphaScale, cameraFov, lightIntensity;
            public Vector3 cameraPosition, cameraEuler, cameraLookAt, rootPosition, lightEuler; public Color lightColor;
            public int cameraId, lightId, attemptedEntries, executionCompletedEntries, acceptedEntries;
            public bool originalRoutineCompleted, sourceProtected, memoryProtected, observerRestored, sourceTrailParametersUnchanged, copiedParametersMatched, authorityParametersUnchanged;
            public string observerBefore, observerAfter, errorScope = "An uncaught native NUnit assertion or ordinary exception is authoritative; a saved partial report is not a pass.";
            public StylePair[] pairs; public StyleLauncher launcherBefore, launcherAfter; public StyleRelease[] releases; public StyleQuery[] queries;
            public StyleLife[] lifecycle; public EntryRecord[] entries; public string[] images;
        }

        [UnityTest] public IEnumerator ActualF_StagedThrowStyledTrailTpsRecordsThreeEntries() => StyledKnifeThreeEntries(false);
        [UnityTest] public IEnumerator ActualF_StagedThrowStyledTrailSideRecordsThreeEntries() => StyledKnifeThreeEntries(true);

        IEnumerator StyledKnifeThreeEntries(bool side)
        {
            var report = new StyleReview { output = _output, stylePath = KnifeStylePath, view = side ? "controlled-side" : "controlled-TPS-like" };
            var releases = new List<StyleRelease>(); var queries = new List<StyleQuery>(); var life = new List<StyleLife>();
            var stack = new Stack<IEnumerator>();
            Action<KnifeQueryTrace.Entry> previousObserver = KnifeQueryTrace.Observer, installedObserver = null, ownObserver = null;
            Action<RangedAttackRelease> releaseObserver = null;
            RuntimeStylePair[] pairs = null; KnifeTrailStyle style = null;
            string output = _output + "/knife-style-review.json";
            Assert.That(File.Exists(output), Is.False, "Never overwrite a prior view record.");
            try
            {
                Assert.That(_withKnife && _knife != null && _knife.IsConfigured, Is.True);
                Assert.That(HasKnifeTail(), Is.False, "Configure only after initialization and before any real lease.");
                style = Required<KnifeTrailStyle>(KnifeStylePath);
                foreach (string path in AssetDatabase.GetDependencies(KnifeStylePath, true).Concat(new[] { KnifeStylePath }).Distinct())
                {
                    foreach (string file in new[] { path, path + ".meta" }.Where(File.Exists))
                        if (!_disk.ContainsKey(file)) _disk.Add(file, Hash(file));
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path).Where(o => o != null && EditorUtility.IsPersistent(o)))
                        if (!_memory.ContainsKey(asset)) _memory.Add(asset, EditorJsonUtility.ToJson(asset));
                }
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(style, out string guid, out long localId), Is.True);
                report.styleGuid = guid; report.styleLocalId = localId;
                report.styleFileBefore = Hash(KnifeStylePath); report.styleMetaBefore = Hash(KnifeStylePath + ".meta");
                report.styleMemoryBefore = EditorJsonUtility.ToJson(style);
                report.lifetime = style.LifetimeSeconds; report.widthScale = style.WidthMultiplierScale; report.alphaScale = style.AlphaMultiplier;
                Assert.That(report.lifetime, Is.EqualTo(.10f)); Assert.That(report.widthScale, Is.EqualTo(.45f)); Assert.That(report.alphaScale, Is.EqualTo(.5625f));
                pairs = ReadStylePairs(); Assert.That(pairs.Length, Is.EqualTo(4));
                Assert.That(pairs.Select(p => p.copy.GetInstanceID()).Distinct().Count(), Is.EqualTo(4));
                Assert.That(pairs.All(p => pairs.All(other => p.copy != other.source)), Is.True);
                report.pairs = pairs.Select(p => p.audit).ToArray(); report.launcherBefore = ReadStyleLauncher(pairs);
                _knife.ConfigureTrailStyle(style); Assert.That(_knife.TrailStyle, Is.SameAs(style));
                foreach (var pair in pairs)
                {
                    pair.audit.sourceStyled = ReadStyleTrail(pair.source); pair.audit.copyStyled = ReadStyleTrail(pair.copy);
                    Assert.That(JsonUtility.ToJson(pair.audit.sourceStyled), Is.EqualTo(JsonUtility.ToJson(pair.audit.sourceBefore)));
                    Assert.That(CopyUntargetedMatches(pair.audit.copyBefore, pair.audit.copyStyled), Is.True);
                    Assert.That(StyleMatches(pair.copy, pair.source, style), Is.True);
                }
                var camera = _scene.GetRootGameObjects().Single(g => g.name == "Review_Knight_Camera").GetComponent<Camera>();
                Assert.That(camera != null && camera.gameObject.scene == _scene, Is.True);
                Vector3 root = _actor.transform.position;
                camera.transform.position = root + (side ? new Vector3(4, 1, 0) : new Vector3(0, 2.2f, -4));
                Vector3 lookAt = root + (side ? new Vector3(0, -.05f, 0) : new Vector3(0, .1f, 0));
                camera.transform.LookAt(lookAt); camera.fieldOfView = side ? 45 : 60;
                var light = Own("Review_Knight_Style_DirectionalLight").AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.2f; light.color = new Color(1, .96f, .88f);
                light.transform.rotation = Quaternion.Euler(50, -25, 0);
                report.cameraId = camera.GetInstanceID(); report.cameraPosition = camera.transform.position; report.cameraEuler = camera.transform.eulerAngles;
                report.cameraLookAt = lookAt; report.cameraFov = camera.fieldOfView; report.rootPosition = root;
                report.lightId = light.GetInstanceID(); report.lightIntensity = light.intensity; report.lightColor = light.color; report.lightEuler = light.transform.eulerAngles;
                var projectileIds = new HashSet<int>(pairs.Select(p => p.projectile.GetInstanceID()));
                ownObserver = query => { if (projectileIds.Contains(query.projectileId) || (query.kind == "assist" && query.sequence == _actor.Model.AttackSequence)) queries.Add(new StyleQuery { entry = _entryNumber, actual = query }); };
                installedObserver = previousObserver + ownObserver; KnifeQueryTrace.Observer = installedObserver;
                releaseObserver = release => releases.Add(new StyleRelease { entry = _entryNumber, frame = Time.frameCount, time = Time.timeAsDouble,
                    sequence = release.AttackSequence, releaseSequence = _actor.Model.RangedReleaseSequence, elapsed = _actor.Model.StateElapsed,
                    damage = release.Damage, postureDamage = release.PostureDamage, speed = release.ProjectileSpeed, range = release.MaximumDistance,
                    expectedDamage = _expectedTuning.RangedDamage, expectedPosture = _expectedTuning.RangedPostureDamage, expectedSpeed = _expectedTuning.RangedProjectileSpeed, expectedRange = _expectedTuning.RangedMaximumDistance,
                    launchWorld = ReadLaunchOrigin().position,
                    matchesTuning = release.Damage == _expectedTuning.RangedDamage && release.PostureDamage == _expectedTuning.RangedPostureDamage && release.ProjectileSpeed == _expectedTuning.RangedProjectileSpeed && release.MaximumDistance == _expectedTuning.RangedMaximumDistance });
                _actor.RangedAttackReleased += releaseObserver;
                // Drive the original nested routine without catching, replacing or relaxing any of its assertions.
                stack.Push(ActualF_DomainAlignedStagedUpperBodyThrowRecordsThreeEntries());
                while (stack.Count != 0)
                {
                    IEnumerator active = stack.Peek();
                    if (!active.MoveNext()) { stack.Pop(); (active as IDisposable)?.Dispose(); continue; }
                    object value = active.Current;
                    if (value is IEnumerator nested && !(value is CustomYieldInstruction)) stack.Push(nested);
                    else
                    {
                        yield return value;
                        if (value is WaitForEndOfFrame)
                        {
                            StyleLife sample = ReadStyleLifecycle(pairs, style, report.launcherBefore); life.Add(sample);
                            Assert.That(sample.authorityParametersUnchanged, Is.True, "Exact authored scale, parent chain and hitbox configuration must remain unchanged at every sampled EOF.");
                        }
                    }
                }
                report.originalRoutineCompleted = true;
            }
            finally
            {
                while (stack.Count != 0) (stack.Pop() as IDisposable)?.Dispose();
                if (_actor != null && releaseObserver != null) _actor.RangedAttackReleased -= releaseObserver;
                if (installedObserver != null && KnifeQueryTrace.Observer == installedObserver) KnifeQueryTrace.Observer = previousObserver;
                else if (ownObserver != null) KnifeQueryTrace.Observer -= ownObserver; // Remove only our callback if an unrelated observer changed.
                report.observerRestored = ReferenceEquals(KnifeQueryTrace.Observer, previousObserver);
                report.observerBefore = DescribeStyleObserver(previousObserver); report.observerAfter = DescribeStyleObserver(KnifeQueryTrace.Observer);
                if (pairs != null)
                {
                    foreach (var pair in pairs)
                    {
                        pair.audit.sourceFinal = pair.source != null ? ReadStyleTrail(pair.source) : null;
                        pair.audit.copyFinal = pair.copy != null ? ReadStyleTrail(pair.copy) : null;
                        pair.audit.sourceParametersUnchanged = JsonUtility.ToJson(pair.audit.sourceBefore) == JsonUtility.ToJson(pair.audit.sourceFinal);
                        pair.audit.copyUntargetedParametersUnchanged = pair.audit.copyFinal != null && CopyUntargetedMatches(pair.audit.copyBefore, pair.audit.copyFinal);
                        pair.audit.styledParametersMatched = pair.copy != null && pair.source != null && style != null && StyleMatches(pair.copy, pair.source, style);
                    }
                    report.sourceTrailParametersUnchanged = pairs.All(p => p.audit.sourceParametersUnchanged);
                    report.copiedParametersMatched = pairs.All(p => p.audit.copyUntargetedParametersUnchanged && p.audit.styledParametersMatched);
                    report.launcherAfter = _launcher != null ? ReadStyleLauncher(pairs) : null;
                    report.authorityParametersUnchanged = LauncherStaticMatches(report.launcherBefore, report.launcherAfter) && life.Count > 0 && life.All(r => r.authorityParametersUnchanged);
                }
                report.sourceProtected = _disk.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value);
                report.memoryProtected = _memory.All(p => p.Key != null && EditorJsonUtility.ToJson(p.Key) == p.Value);
                report.styleFileAfter = File.Exists(KnifeStylePath) ? Hash(KnifeStylePath) : "MISSING";
                report.styleMetaAfter = File.Exists(KnifeStylePath + ".meta") ? Hash(KnifeStylePath + ".meta") : "MISSING";
                report.styleMemoryAfter = style != null ? EditorJsonUtility.ToJson(style) : "MISSING";
                report.releases = releases.ToArray(); report.queries = queries.ToArray(); report.lifecycle = life.ToArray();
                report.entries = _attemptedEntries.ToArray(); report.images = report.entries.SelectMany(e => e.images ?? Array.Empty<string>()).ToArray();
                report.attemptedEntries = report.entries.Length; report.executionCompletedEntries = report.entries.Count(e => e.executionCompleted); report.acceptedEntries = report.entries.Count(e => e.accepted);
                report.status = !report.sourceProtected || !report.memoryProtected || !report.observerRestored ? "protection-failed" : report.originalRoutineCompleted ? "original-gates-completed-view-awaiting-review" : "incomplete-or-original-gates-failed";
                File.WriteAllText(output, JsonUtility.ToJson(report, true));
            }
            Assert.That(report.sourceProtected && report.memoryProtected && report.observerRestored, Is.True);
            Assert.That(report.sourceTrailParametersUnchanged && report.copiedParametersMatched && report.authorityParametersUnchanged, Is.True);
            Assert.That(report.styleFileAfter, Is.EqualTo(report.styleFileBefore)); Assert.That(report.styleMetaAfter, Is.EqualTo(report.styleMetaBefore)); Assert.That(report.styleMemoryAfter, Is.EqualTo(report.styleMemoryBefore));
            Assert.That(report.entries.Length, Is.EqualTo(3)); Assert.That(report.entries.All(e => e.accepted), Is.True);
            Assert.That(releases.Count, Is.EqualTo(3)); Assert.That(releases.All(r => r.matchesTuning), Is.True);
            for (int entry = 1; entry <= 3; entry++)
            {
                Assert.That(releases.Count(r => r.entry == entry), Is.EqualTo(1));
                Assert.That(life.Any(r => r.entry == entry && r.flight && r.totalPoints > 0), Is.True, "Styled flight must actually generate visible-copy trail points.");
                Assert.That(life.Where(r => r.entry == entry).All(r => r.styledParametersMatched), Is.True);
                Assert.That(queries.Any(q => q.entry == entry && q.actual.kind.StartsWith("release-", StringComparison.Ordinal)), Is.True);
                Assert.That(queries.Any(q => q.entry == entry && q.actual.kind == "sweep"), Is.True);
            }
            Assert.That(report.images.Length, Is.EqualTo(9), "The original three images per real entry are retained, not a new synthetic pose gallery.");
        }

        RuntimeStylePair[] ReadStylePairs()
        {
            var field = typeof(PlayerKnifePresentation).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic);
            var slots = (Array)field.GetValue(_knife);
            return slots.Cast<object>().Select(slot => {
                Type type = slot.GetType();
                var copy = (TrailRenderer)type.GetField("Trail").GetValue(slot); var source = (TrailRenderer)type.GetField("OriginalTrail").GetValue(slot);
                var projectile = (PlayerThrowingKnifeProjectile)type.GetField("Projectile").GetValue(slot);
                return new RuntimeStylePair { copy = copy, source = source, projectile = projectile, poolParent = projectile.transform.parent,
                    audit = new StylePair { projectileId = ((PlayerThrowingKnifeProjectile)type.GetField("Projectile").GetValue(slot)).GetInstanceID(), sourceBefore = ReadStyleTrail(source), copyBefore = ReadStyleTrail(copy) } };
            }).ToArray();
        }
        StyleLife ReadStyleLifecycle(RuntimeStylePair[] pairs, KnifeTrailStyle style, StyleLauncher baseline)
        {
            var slots = (Array)typeof(PlayerKnifePresentation).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_knife);
            StyleLauncher configuration = ReadStyleLauncher(pairs);
            return new StyleLife { entry = _entryNumber, frame = Time.frameCount, time = Time.timeAsDouble, domain = _actor.Model.State.ToString(),
                stage = _knife.HeldVisible ? "held" : _knife.FlightVisible ? (_knife.BridgeAge < PlayerKnifePresentation.BridgeDuration ? "release-or-bridge" : "flight") : HasKnifeTail() ? "tail" : "idle-returned",
                held = _knife.HeldVisible, flight = _knife.FlightVisible, sword = _knife.SwordVisible, bridgeAge = _knife.BridgeAge, knifePosition = _knife.VisualPosition,
                projectiles = _launcher.ActiveProjectileCount, visuals = _knife.ActiveVisualCount, totalPoints = _knife.TrailPointCount,
                copies = pairs.Select(p => p.copy.GetInstanceID()).ToArray(), sources = pairs.Select(p => p.source.GetInstanceID()).ToArray(),
                copyPoints = pairs.Select(p => p.copy.positionCount).ToArray(), sourcePoints = pairs.Select(p => p.source.positionCount).ToArray(),
                leases = slots.Cast<object>().Select(s => (bool)s.GetType().GetField("Leased").GetValue(s)).ToArray(),
                styledParametersMatched = pairs.All(p => StyleMatches(p.copy, p.source, style)), authorConfiguration = configuration,
                authorityParametersUnchanged = LauncherStaticMatches(baseline, configuration, true) };
        }
        Transform ReadLaunchOrigin() => (Transform)new SerializedObject(_launcher).FindProperty("_launchOrigin").objectReferenceValue;
        StyleLauncher ReadStyleLauncher(RuntimeStylePair[] pairs)
        {
            var so = new SerializedObject(_launcher); var origin = (Transform)so.FindProperty("_launchOrigin").objectReferenceValue;
            var prefab = so.FindProperty("_projectilePrefab").objectReferenceValue;
            return new StyleLauncher { launchOriginId = origin.GetInstanceID(), launchOriginParentId = origin.parent.GetInstanceID(),
                launchLocal = origin.localPosition, launchWorld = origin.position, launchLocalScale = origin.localScale, launchLocalRotation = origin.localRotation,
                launchAncestors = ReadStyleScaleAncestors(origin), assistRadius = so.FindProperty("_unlockedAimAssistRadius").floatValue,
                prewarm = so.FindProperty("_prewarmCount").intValue, prefabId = prefab.GetInstanceID(), prefabPath = AssetDatabase.GetAssetPath(prefab),
                projectiles = pairs.Select(p => { var ps = new SerializedObject(p.projectile); var box = (BoxCollider)ps.FindProperty("_hitbox").objectReferenceValue;
                    return new StyleProjectile { id = p.projectile.GetInstanceID(), transformId = p.projectile.transform.GetInstanceID(), hitboxId = box.GetInstanceID(), hitboxTransformId = box.transform.GetInstanceID(),
                        center = box.center, size = box.size, scale = p.projectile.transform.lossyScale, localScale = p.projectile.transform.localScale,
                        inFlight = p.projectile.IsInFlight, hitboxOnProjectileRoot = box.transform == p.projectile.transform,
                        ancestors = ReadStyleScaleAncestors(p.projectile.transform), poolAncestors = ReadStyleScaleAncestors(p.poolParent),
                        hitboxLocalChain = ReadStyleHitboxLocalChain(box.transform, p.projectile.transform), spin = ps.FindProperty("_spinDegreesPerSecond").floatValue }; }).ToArray() };
        }
        static StyleScaleAncestor[] ReadStyleScaleAncestors(Transform current)
        {
            var chain = new List<StyleScaleAncestor>();
            for (; current != null; current = current.parent)
                chain.Add(new StyleScaleAncestor { id = current.GetInstanceID(), parentId = current.parent != null ? current.parent.GetInstanceID() : 0,
                    name = current.name, localScale = current.localScale });
            return chain.ToArray();
        }
        static StyleHitboxLocalTransform[] ReadStyleHitboxLocalChain(Transform current, Transform projectileRoot)
        {
            var chain = new List<StyleHitboxLocalTransform>();
            for (; current != null && current != projectileRoot; current = current.parent)
                chain.Add(new StyleHitboxLocalTransform { id = current.GetInstanceID(), parentId = current.parent != null ? current.parent.GetInstanceID() : 0,
                    localPosition = current.localPosition, localRotation = current.localRotation, localScale = current.localScale });
            Assert.That(current, Is.SameAs(projectileRoot), "Hitbox must retain its exact projectile-local ancestry.");
            return chain.ToArray();
        }
        static bool LauncherStaticMatches(StyleLauncher before, StyleLauncher after, bool allowOriginalFlightDetach = false)
        {
            if (before == null || after == null || before.projectiles == null || after.projectiles == null || before.projectiles.Length != after.projectiles.Length) return false;
            var compare = JsonUtility.FromJson<StyleLauncher>(JsonUtility.ToJson(after));
            // Preserve the actual after-coordinate in evidence. Compare authored local origin/parent, not normal grounded Actor Y.
            compare.launchWorld = before.launchWorld;
            for (int i = 0; i < before.projectiles.Length; i++)
            {
                StyleProjectile original = before.projectiles[i], actual = after.projectiles[i], normalized = compare.projectiles[i];
                // lossyScale includes floating-point matrix/rotation decomposition; never use it as serialized localScale.
                normalized.scale = original.scale;
                if (allowOriginalFlightDetach && actual.inFlight)
                {
                    if (actual.ancestors == null || actual.ancestors.Length != 1 || actual.ancestors[0].id != original.transformId ||
                        actual.ancestors[0].parentId != 0 || !actual.ancestors[0].localScale.Equals(original.localScale)) return false;
                    // Only this known Launcher parent transition is dynamic; own localScale and the saved pool chain still compare exactly.
                    normalized.ancestors = original.ancestors;
                    normalized.inFlight = original.inFlight;
                }
            }
            return JsonUtility.ToJson(before) == JsonUtility.ToJson(compare);
        }
        static StyleTrail ReadStyleTrail(TrailRenderer trail)
        {
            var gradient = trail.colorGradient;
            return new StyleTrail { id = trail.GetInstanceID(), materialId = trail.sharedMaterial != null ? trail.sharedMaterial.GetInstanceID() : 0, name = trail.name,
                time = trail.time, width = trail.widthMultiplier, spacing = trail.minVertexDistance, enabled = trail.enabled, receiveShadows = trail.receiveShadows,
                alignment = trail.alignment.ToString(), textureMode = trail.textureMode.ToString(), shadowMode = trail.shadowCastingMode.ToString(),
                mode = gradient.mode.ToString(), colorSpace = gradient.colorSpace.ToString(),
                widthCurve = trail.widthCurve.preWrapMode + "/" + trail.widthCurve.postWrapMode + ":" + string.Join("|", trail.widthCurve.keys.Select(k => string.Join(",", new[] { k.time, k.value, k.inTangent, k.outTangent, k.inWeight, k.outWeight }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "/" + k.weightedMode)),
                colors = gradient.colorKeys.Select(k => new StyleColor { color = k.color, time = k.time }).ToArray(),
                alphas = gradient.alphaKeys.Select(k => new StyleAlpha { alpha = k.alpha, time = k.time }).ToArray() };
        }
        static bool CopyUntargetedMatches(StyleTrail before, StyleTrail after)
        {
            var compare = JsonUtility.FromJson<StyleTrail>(JsonUtility.ToJson(after));
            compare.time = before.time; compare.width = before.width; compare.alphas = before.alphas;
            return JsonUtility.ToJson(before) == JsonUtility.ToJson(compare);
        }
        static bool StyleMatches(TrailRenderer copy, TrailRenderer source, KnifeTrailStyle style)
        {
            var expected = source.colorGradient; var actual = copy.colorGradient;
            var a = actual.alphaKeys; var b = expected.alphaKeys;
            return copy != source && copy.sharedMaterial == source.sharedMaterial && Mathf.Abs(copy.time - style.LifetimeSeconds) <= .000001f &&
                Mathf.Abs(copy.widthMultiplier - source.widthMultiplier * style.WidthMultiplierScale) <= .000001f &&
                actual.mode == expected.mode && actual.colorSpace == expected.colorSpace && actual.colorKeys.SequenceEqual(expected.colorKeys) && a.Length == b.Length &&
                a.Select((key, i) => key.time == b[i].time && Mathf.Abs(key.alpha - b[i].alpha * style.AlphaMultiplier) <= .000001f).All(v => v) &&
                a[a.Length - 1].alpha == 0f;
        }
        static string DescribeStyleObserver(Delegate observer) => observer == null ? "null" : string.Join("|", observer.GetInvocationList().Select(d => d.Method.DeclaringType?.FullName + "." + d.Method.Name));
    }
}
#endif
