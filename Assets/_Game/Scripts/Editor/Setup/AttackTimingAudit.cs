using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Emberfall.AI.Data;
using Emberfall.AI.Domain;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    public static class AttackTimingAudit
    {
        public const string AnnotationPath = "Assets/_Game/Settings/AttackTiming_M6.asset";
        public const string EnemyPath = "Assets/_Game/Data/M2/enemies.v1.json";
        public const string TuningPath = "Assets/_Game/Settings/CombatTuning_M1.asset";
        public const float Tolerance = 1f / 30f;

        public sealed class Mapping
        {
            public string id, source, note;
            public AnimationClip clip;
            public float duration, playbackDuration, clipStart, clipEnd, minSpeed = .25f, maxSpeed = 3f;
            public float windowStart, windowEnd, stateStart, stateEnd;
            public bool pointEvent;
        }

        [Serializable] public sealed class Row
        {
            public string actionId, clipPath, clipName, dataSource, note;
            public float clipLength, clipStart, clipEnd, duration, playbackDuration;
            public float contactSeconds = -1, unclampedContactTime = -1, actualContactTime = -1;
            public float windowStart, windowEnd, requiredSpeed, clampedSpeed, deviationMs = -1;
            public bool pointEvent, speedInRange, finishesInTime, confirmed, accepted;
            public string[] failures;
        }

        [Serializable] public sealed class Report
        {
            public string scope = "Offline authored baseline, no freeze/crossfade/network latency simulation. Not runtime contact proof.";
            public string createdUtc, tuningHash, enemyHash, animationSetHash;
            public bool accepted;
            public string[] coverageErrors;
            public Row[] rows;
        }

        // Independent required checklist: extra ordinary/elite attacks are also generated below.
        public static readonly string[] RequiredIds = {
            "player.light1", "player.light2", "player.light3", "player.heavy", "player.sweep",
            "player.knife", "player.execution", "fogwalker.quick", "fogwalker.combo1", "fogwalker.combo2",
            "priest.projectile", "priest.rune", "guard.bash", "warden.combo1", "warden.combo2",
            "warden.rune", "warden.charge", "guard.sword", "scorched.sword", "scorched.bash",
            "scorched.burst", "warden.bash", "warden.blast"
        };

        public static List<Mapping> ReadMappings()
        {
            var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(M1AnimationSetup.AnimationSetPath);
            var tuning = AssetDatabase.LoadAssetAtPath<CombatTuningAsset>(TuningPath).CreateRuntimeCopy();
            string json = File.ReadAllText(EnemyPath);
            var melee = MeleeEnemyDefinitionJsonLoader.Load(json).GetRequired(new ContentId("enemy:fogwalker"));
            var ranged = RangedEnemyDefinitionJsonLoader.Load(json).GetRequired(new ContentId("enemy:rune-priest"));
            var guards = ShieldEnemyDefinitionJsonLoader.Load(json);
            var boss = WardenDefinitionJsonLoader.Load(json).GetRequired(new ContentId("boss:ember-warden"));
            var rows = new List<Mapping>();
            Mapping Add(string id, AnimationClip clip, float duration, float open, float close, string source,
                float stateStart = 0)
            {
                if (clip == null) throw new InvalidDataException("Missing mapped clip: " + id);
                var row = new Mapping { id = id, clip = clip, duration = duration, playbackDuration = duration,
                    clipEnd = clip.length, windowStart = open, windowEnd = close, stateStart = stateStart,
                    stateEnd = duration, source = source, pointEvent = open == close, note = "" };
                rows.Add(row);
                return row;
            }
            for (int i = 0; i < 3; i++)
            {
                var row = Add("player.light" + (i + 1), set.GetClip(CombatState.LightAttack1 + i),
                    tuning.GetLightDuration(i), tuning.GetLightDamageOpen(i), tuning.GetLightDamageClose(i), TuningPath);
                row.minSpeed = .35f;
                if (i < 2) {
                    row.playbackDuration = tuning.GetLightComboOpen(i);
                    row.note = "Offline strike ends at LightRecoveryStart; separate recovery clip follows. Network currently differs.";
                }
            }
            Add("player.heavy", set.GetClip(CombatState.HeavyAttack), tuning.HeavyDuration,
                tuning.HeavyDamageOpen, tuning.HeavyDamageClose, TuningPath).minSpeed = .35f;
            Add("player.sweep", set.GetClip(CombatState.Sweep), tuning.SweepDuration,
                tuning.SweepDamageOpen, tuning.SweepDamageClose, TuningPath).minSpeed = .35f;
            Add("player.knife", set.GetClip(CombatState.RangedAttack), tuning.RangedDuration,
                tuning.RangedReleaseTime, tuning.RangedReleaseTime, TuningPath).minSpeed = .35f;
            Add("player.execution", set.GetClip(CombatState.Execution), tuning.ExecutionDuration,
                tuning.ExecutionResolveTime, tuning.ExecutionResolveTime, TuningPath).minSpeed = .35f;
            Add("fogwalker.quick", set.GetClip(CombatState.LightAttack1), melee.WindupDuration + melee.AttackDuration,
                melee.WindupDuration + melee.DamageWindowStart, melee.WindupDuration + melee.DamageWindowEnd,
                EnemyPath, melee.WindupDuration);
            Add("fogwalker.combo1", set.GetEnemyClip(EnemyAnimationAction.MeleeCombo), melee.ComboWindupDuration + melee.ComboAttackDuration,
                melee.ComboWindupDuration + melee.ComboDamageWindow1Start, melee.ComboWindupDuration + melee.ComboDamageWindow1End,
                EnemyPath, melee.ComboWindupDuration);
            Add("fogwalker.combo2", set.GetEnemyClip(EnemyAnimationAction.MeleeCombo), melee.ComboWindupDuration + melee.ComboAttackDuration,
                melee.ComboWindupDuration + melee.ComboDamageWindow2Start, melee.ComboWindupDuration + melee.ComboDamageWindow2End,
                EnemyPath, melee.ComboWindupDuration);
            // Release is consumed on entering Release, not at the middle of the release animation.
            Add("priest.projectile", set.GetEnemyClip(EnemyAnimationAction.PriestProjectileRelease), ranged.ReleaseDuration,
                0, 0, EnemyPath).minSpeed = .35f;
            Add("priest.rune", set.GetEnemyClip(EnemyAnimationAction.RuneCast), ranged.GroundRuneWindupDuration + ranged.GroundRuneReleaseDuration,
                ranged.GroundRuneWindupDuration, ranged.GroundRuneWindupDuration, EnemyPath,
                ranged.GroundRuneWindupDuration).minSpeed = .35f;
            foreach (string kind in new[] { "guard", "scorched" })
            {
                var g = guards.GetRequired(new ContentId(kind == "guard" ? "enemy:ruin-guard" : "enemy:ruin-guard-scorched"));
                Add(kind + ".sword", set.GetClip(CombatState.HeavyAttack), g.WindupDuration + g.AttackDuration,
                    g.WindupDuration + g.DamageWindowStart, g.WindupDuration + g.DamageWindowEnd, EnemyPath, g.WindupDuration);
                Add(kind + ".bash", set.GetEnemyClip(EnemyAnimationAction.ShieldBash), g.BashWindupDuration + g.BashAttackDuration,
                    g.BashWindupDuration + g.BashDamageWindowStart, g.BashWindupDuration + g.BashDamageWindowEnd, EnemyPath, g.BashWindupDuration);
                if (g.ScorchedBurstEnabled)
                    Add(kind + ".burst", set.GetEnemyClip(EnemyAnimationAction.RuneCast), g.ScorchedBurstWindupDuration + g.ScorchedBurstAttackDuration,
                        g.ScorchedBurstWindupDuration, g.ScorchedBurstWindupDuration, EnemyPath, g.ScorchedBurstWindupDuration)
                        .note = "Marks cast release only; delayed area damage follows fuse, not this clip.";
            }
            void Boss(string id, WardenAttackDefinition attack, AnimationClip clip, bool second = false)
            {
                Add(id, clip, attack.WindupDuration + attack.AttackDuration,
                    attack.WindupDuration + (second ? attack.SecondWindowStart : attack.FirstWindowStart),
                    attack.WindupDuration + (second ? attack.SecondWindowEnd : attack.FirstWindowEnd), EnemyPath, attack.WindupDuration);
            }
            Boss("warden.combo1", boss.SwordCombo, set.GetEnemyClip(EnemyAnimationAction.MeleeCombo));
            Boss("warden.combo2", boss.SwordCombo, set.GetEnemyClip(EnemyAnimationAction.MeleeCombo), true);
            Boss("warden.bash", boss.ShieldBash, set.GetEnemyClip(EnemyAnimationAction.ShieldBash));
            Boss("warden.rune", boss.RuneCleave, set.GetEnemyClip(EnemyAnimationAction.WardenRuneCleave));
            Boss("warden.blast", boss.DelayedBlast, set.GetEnemyClip(EnemyAnimationAction.RuneCast));
            var charge = Add("warden.charge", set.GetEnemyClip(EnemyAnimationAction.WardenCharge), boss.Charge.AttackDuration,
                boss.Charge.FirstWindowStart, boss.Charge.FirstWindowEnd, EnemyPath);
            // Travel owns 24-78% of the clip; convert both boundaries to clip seconds.
            // Unity 2022.3 measured entry = fixedTimeOffset * Animator.speed / clip.length.
            // Current literal offset is preserved pending Harness's ruling on speed compensation.
            float chargeSpeed = Mathf.Clamp(charge.clip.length * (.78f - .24f) / charge.duration, .25f, 3f);
            charge.clipStart = .24f * chargeSpeed;
            charge.clipEnd = charge.clipStart + charge.clip.length * (.78f - .24f);
            charge.note = "Intended domain travel phase 24-78%, but actual fixed-time entry is speed-scaled; engine probe pending Harness. Contact remains unconfirmed and spatially dependent.";
            return rows;
        }

        public static string[] CoverageErrors(IEnumerable<string> ids)
        {
            var values = ids.ToArray();
            return RequiredIds.Where(id => !values.Contains(id)).Select(id => "missing:" + id)
                .Concat(values.GroupBy(id => id).Where(g => g.Count() != 1).Select(g => "duplicate:" + g.Key)).ToArray();
        }

        public static Row Evaluate(Mapping map, AttackTimingAnnotations.Contact contact)
        {
            var errors = new List<string>();
            float length = map.clip != null ? map.clip.length : 0;
            float effective = map.clipEnd - map.clipStart;
            bool valid = Finite(length) && length > 0 && Finite(effective) && effective > 0 &&
                Finite(map.playbackDuration) && map.playbackDuration > 0 && Finite(map.duration) && map.duration > 0 &&
                Finite(map.clipStart) && map.clipStart >= 0 && map.clipEnd <= length + .0001f &&
                Finite(map.minSpeed) && map.minSpeed > 0 && Finite(map.maxSpeed) && map.maxSpeed >= map.minSpeed;
            var row = new Row { actionId = map.id, clipName = map.clip != null ? map.clip.name : "MISSING",
                clipPath = map.clip != null ? AssetDatabase.GetAssetPath(map.clip) : "", clipLength = length,
                clipStart = map.clipStart, clipEnd = map.clipEnd, duration = map.duration, playbackDuration = map.playbackDuration,
                windowStart = map.windowStart, windowEnd = map.windowEnd, pointEvent = map.pointEvent, dataSource = map.source, note = map.note };
            if (!valid) errors.Add("invalid-playback-mapping");
            else {
                row.requiredSpeed = effective / map.playbackDuration;
                row.clampedSpeed = Mathf.Clamp(row.requiredSpeed, map.minSpeed, map.maxSpeed);
                row.speedInRange = row.requiredSpeed >= map.minSpeed - .0001f && row.requiredSpeed <= map.maxSpeed + .0001f;
                row.finishesInTime = effective / row.clampedSpeed <= map.playbackDuration + .0001f;
                if (!row.speedInRange) errors.Add("speed-clamped");
            }
            if (!Finite(map.windowStart) || !Finite(map.windowEnd) || !Finite(map.stateStart) || !Finite(map.stateEnd) ||
                map.stateStart < 0 || map.stateEnd > map.duration || map.windowStart < map.stateStart || map.windowEnd > map.stateEnd ||
                (map.pointEvent ? map.windowEnd != map.windowStart : map.windowEnd <= map.windowStart))
                errors.Add("invalid-domain-window");
            row.confirmed = contact != null && contact.confirmed;
            if (!row.confirmed) errors.Add("contact-unconfirmed");
            else if (contact.actionId != map.id || contact.clip != map.clip || !Finite(contact.contactSeconds) || contact.contactSeconds < map.clipStart ||
                     contact.contactSeconds > map.clipEnd || string.IsNullOrWhiteSpace(contact.observedBy) ||
                     string.IsNullOrWhiteSpace(contact.observation) || string.IsNullOrWhiteSpace(contact.evidencePath) ||
                     !File.Exists(contact.evidencePath) || contact.observedClipHash != ClipHash(map.clip))
                errors.Add("invalid-or-stale-observation");
            else if (valid) {
                row.contactSeconds = contact.contactSeconds;
                row.unclampedContactTime = (contact.contactSeconds - map.clipStart) / row.requiredSpeed;
                row.actualContactTime = (contact.contactSeconds - map.clipStart) / row.clampedSpeed;
                row.deviationMs = 1000 * Mathf.Max(map.windowStart - row.actualContactTime, row.actualContactTime - map.windowEnd, 0);
                if (row.deviationMs > Tolerance * 1000 + .001f) errors.Add("contact-outside-window");
            }
            row.failures = errors.ToArray();
            row.accepted = errors.Count == 0;
            return row;
        }

        public static string ClipHash(AnimationClip clip) => clip == null ? "" :
            AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(clip)).ToString();
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        [MenuItem("Emberfall/Review/Attack Timing/Create Missing Annotations")]
        public static void CreateMissingAnnotations()
        {
            var asset = AssetDatabase.LoadAssetAtPath<AttackTimingAnnotations>(AnnotationPath);
            if (asset == null) { asset = ScriptableObject.CreateInstance<AttackTimingAnnotations>(); AssetDatabase.CreateAsset(asset, AnnotationPath); }
            foreach (var map in ReadMappings())
                if (!asset.contacts.Any(c => c.actionId == map.id))
                    asset.contacts.Add(new AttackTimingAnnotations.Contact { actionId = map.id, clip = map.clip });
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Emberfall/Review/Attack Timing/Export Baseline")]
        public static void Export()
        {
            var maps = ReadMappings();
            var asset = AssetDatabase.LoadAssetAtPath<AttackTimingAnnotations>(AnnotationPath);
            var contacts = asset != null ? asset.contacts : new List<AttackTimingAnnotations.Contact>();
            var coverage = CoverageErrors(maps.Select(m => m.id)).Concat(CoverageErrors(contacts.Select(c => c.actionId))).ToArray();
            var report = new Report { createdUtc = DateTime.UtcNow.ToString("O"),
                tuningHash = AssetDatabase.GetAssetDependencyHash(TuningPath).ToString(),
                enemyHash = AssetDatabase.GetAssetDependencyHash(EnemyPath).ToString(),
                animationSetHash = AssetDatabase.GetAssetDependencyHash(M1AnimationSetup.AnimationSetPath).ToString(),
                coverageErrors = coverage,
                rows = maps.Select(m => Evaluate(m, contacts.FirstOrDefault(c => c.actionId == m.id))).ToArray() };
            report.accepted = coverage.Length == 0 && report.rows.All(r => r.accepted);
            string output = "Builds/TestResults/0.9.3-timing-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(output);
            File.WriteAllText(output + "/alignment.json", JsonUtility.ToJson(report, true), Encoding.UTF8);
            var md = new StringBuilder("# 动作对齐：编辑器基线\n\n仅单机静态映射；未确认接触不通过，不代表真实运行时/联机已对齐。点事件保持真实语义，不伪造伤害窗口。\n\n");
            md.AppendLine("| 动作 | clip / 长度 | 有效段 | 播放时长 / 全序列 | 接触秒 | 未限幅 / 实际领域秒 | 当前窗口 | 偏差ms | 所需 / 实际倍速 | 播得完 | 结论 |");
            md.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            foreach (var r in report.rows)
                md.AppendLine(FormattableString.Invariant($"| {r.actionId} | {r.clipName} / {r.clipLength:F4} | {r.clipStart:F4}–{r.clipEnd:F4} | {r.playbackDuration:F4} / {r.duration:F4} | {r.contactSeconds:F4} | {r.unclampedContactTime:F4} / {r.actualContactTime:F4} | {r.windowStart:F4}–{r.windowEnd:F4}{(r.pointEvent ? " (点事件)" : "")} | {r.deviationMs:F2} | {r.requiredSpeed:F4} / {r.clampedSpeed:F4} | {r.finishesInTime} | {string.Join(",", r.failures)} |"));
            md.AppendLine("\n-1 表示尚无有效观察，不是接触发生在负时间。\n");
            foreach (var r in report.rows.Where(r => !string.IsNullOrEmpty(r.note))) md.AppendLine("- " + r.actionId + ": " + r.note);
            File.WriteAllText(output + "/alignment.md", md.ToString(), Encoding.UTF8);
            Debug.Log($"[ATTACK_TIMING_AUDIT] rows={report.rows.Length} accepted={report.accepted} output={output}");
        }

        public static void CreateAndExport() { CreateMissingAnnotations(); Export(); }

        [MenuItem("Emberfall/Review/Attack Timing/Validate For Delivery")]
        public static void ValidateForDelivery()
        {
            var asset = AssetDatabase.LoadAssetAtPath<AttackTimingAnnotations>(AnnotationPath);
            if (asset == null) throw new InvalidDataException("Contact annotations missing.");
            var maps = ReadMappings();
            string[] coverage = CoverageErrors(maps.Select(m => m.id))
                .Concat(CoverageErrors(asset.contacts.Select(c => c.actionId))).ToArray();
            if (coverage.Length > 0) throw new InvalidDataException(string.Join(";", coverage));
            foreach (var map in maps) {
                var row = Evaluate(map, asset.contacts.FirstOrDefault(c => c.actionId == map.id));
                if (!row.accepted) throw new InvalidDataException(map.id + ": " + string.Join(";", row.failures));
            }
        }
    }
}
