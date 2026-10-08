using System;
using System.IO;
using System.Text;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Editor.Review
{
    // Opt-in, bounded Editor observation only. Never submits input, changes
    // authority/timing, disables actors, or alters a test assertion.
    [InitializeOnLoad]
    public static class GuardCounterInputObserver
    {
        const string Key = "Emberfall.GuardCounterInputObserver.v1";
        static PlayerCombatActor player;
        static float health;
        static int parries, attacks;
        static string state;
        static int rows;

        static GuardCounterInputObserver()
        {
            EditorApplication.update += Observe;
        }

        public static string Arm(string label)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Arm only in idle Edit Mode.");
            if (!string.IsNullOrEmpty(SessionState.GetString(Key, "")))
                throw new InvalidOperationException("Existing observer preserved.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(label, @"\A[a-zA-Z0-9][a-zA-Z0-9._-]*\z"))
                throw new ArgumentException("Safe unique label required.");
            string path = Path.GetFullPath("Builds/TestResults/" + label + "-guard-observer.txt");
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
            SessionState.SetString(Key, path);
            SessionState.SetFloat(Key + ".deadline", (float)EditorApplication.timeSinceStartup + 120f);
            rows = 0;
            return path;
        }

        static void Observe()
        {
            string path = SessionState.GetString(Key, "");
            if (string.IsNullOrEmpty(path)) return;
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + ".deadline", 0f) || rows >= 150)
            { Stop(); return; }
            if (!EditorApplication.isPlaying) return;
            var found = UnityEngine.Object.FindObjectOfType<PlayerCombatActor>();
            if (found != player)
            {
                if (player != null) player.CombatProgressed -= OnDamage;
                player = found;
                if (player != null) { player.CombatProgressed += OnDamage; Record("attached"); }
            }
            if (player == null || player.Model == null) return;
            if (health != player.Model.Health.Current || parries != player.Model.PerfectGuardCount ||
                attacks != player.Model.AttackSequence || state != player.Model.State.ToString())
            {
                Record("transition");
                health = player.Model.Health.Current; parries = player.Model.PerfectGuardCount;
                attacks = player.Model.AttackSequence; state = player.Model.State.ToString();
            }
        }

        static void OnDamage(PlayerCombatActor target, CombatProgressKind kind)
        {
            if (kind != CombatProgressKind.DamageReceived || target != player) return;
            Record("damage callback=" + new System.Diagnostics.StackTrace(false));
        }

        static void Record(string reason)
        {
            if (player == null || player.Model == null) return;
            string path = SessionState.GetString(Key, "");
            if (string.IsNullOrEmpty(path) || rows >= 150) return;
            var text = new StringBuilder();
            text.AppendLine("frame=" + Time.frameCount + " time=" + Time.time + " reason=" + reason);
            text.AppendLine("player " + player.Model.State + " elapsed=" + player.Model.StateElapsed +
                " health=" + player.Model.Health.Current + " parries=" + player.Model.PerfectGuardCount +
                " attacks=" + player.Model.AttackSequence + " counter=" + player.Model.CurrentAttackIsGuardCounter +
                " window=" + player.Model.GuardCounterWindowRemaining + " position=" + player.transform.position +
                " event=" + player.LastCombatEvent);
            foreach (var enemy in UnityEngine.Object.FindObjectsOfType<MeleeEnemyActor>())
                text.AppendLine("melee " + enemy.name + " id=" + enemy.CombatantId + " state=" + enemy.State +
                    " elapsed=" + enemy.Brain.StateElapsed + " move=" + enemy.Brain.CurrentAttack +
                    " seq=" + enemy.Brain.AttackSequence + " window=" + enemy.Brain.DamageWindowIndex +
                    " position=" + enemy.transform.position + " event=" + enemy.LastAiEvent);
            foreach (var enemy in UnityEngine.Object.FindObjectsOfType<RangedEnemyActor>())
                text.AppendLine("ranged " + enemy.name + " id=" + enemy.CombatantId + " state=" + enemy.State +
                    " elapsed=" + enemy.Brain.StateElapsed + " release=" + enemy.ReleasedAttackSequence +
                    " position=" + enemy.transform.position + " event=" + enemy.LastAiEvent);
            foreach (var enemy in UnityEngine.Object.FindObjectsOfType<ShieldEnemyActor>())
                text.AppendLine("shield " + enemy.name + " id=" + enemy.CombatantId + " state=" + enemy.State +
                    " position=" + enemy.transform.position + " event=" + enemy.LastAiEvent);
            File.AppendAllText(path, text.ToString()); rows++;
        }

        public static void Stop()
        {
            if (player != null) player.CombatProgressed -= OnDamage;
            player = null;
            SessionState.EraseString(Key); SessionState.EraseFloat(Key + ".deadline");
        }
    }
}
