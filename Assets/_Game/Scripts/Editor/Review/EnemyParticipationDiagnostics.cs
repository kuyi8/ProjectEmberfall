using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Passive observer of the existing AI-on input pilot; never moves, damages or directs an actor.</summary>
    [InitializeOnLoad]
    public static class EnemyParticipationDiagnostics
    {
        const string Key = "Emberfall.EnemyParticipation.";
        static readonly List<Row> rows = new List<Row>();
        static readonly List<HitRow> hits = new List<HitRow>();
        static PlayerCombatActor observedPlayer;
        static double next;

        static EnemyParticipationDiagnostics()
        {
            EditorApplication.playModeStateChanged += OnMode;
            EditorApplication.update += Observe;
        }

        public static string Begin(bool historicalGeometry = false)
        {
            if (SessionState.GetBool(Key + "active", false))
                throw new InvalidOperationException("Diagnostic already active.");
            string folder = EnvironmentRouteInputReview.Begin(true, true, false, historicalGeometry);
            SessionState.SetString(Key + "output", folder);
            SessionState.SetBool(Key + "active", true);
            return folder;
        }

        static void OnMode(PlayModeStateChange mode)
        {
            if (!SessionState.GetBool(Key + "active", false)) return;
            if (mode == PlayModeStateChange.EnteredPlayMode) { rows.Clear(); hits.Clear(); next = 0; observedPlayer = null; }
            if (mode == PlayModeStateChange.ExitingPlayMode)
            {
                if (observedPlayer != null) observedPlayer.ImpactPresented -= OnImpact;
                Write();
            }
            if (mode == PlayModeStateChange.EnteredEditMode) SessionState.SetBool(Key + "active", false);
        }

        static void Observe()
        {
            if (!SessionState.GetBool(Key + "active", false) || !EditorApplication.isPlaying ||
                EditorApplication.timeSinceStartup < next) return;
            next = EditorApplication.timeSinceStartup + .1;
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            if (player == null || flow == null || !flow.IsInitialized) return;
            if (observedPlayer == null)
            {
                observedPlayer = player;
                observedPlayer.ImpactPresented += OnImpact;
            }
            string output = SessionState.GetString(Key + "output", "");
            if (Path.GetFullPath(flow.SavePath) != Path.GetFullPath(output + "/isolated-save.json"))
                throw new InvalidOperationException("Diagnostic save isolation failed.");
            foreach (var actor in Object.FindObjectsOfType<CombatTarget>())
            {
                if (!actor.name.Contains("_Bridge_") && !actor.name.Contains("_PreSanctum")) continue;
                var melee = actor as MeleeEnemyActor;
                var ranged = actor as RangedEnemyActor;
                var shield = actor as ShieldEnemyActor;
                if (melee == null && ranged == null && shield == null) continue;
                var agent = actor.GetComponent<NavMeshAgent>();
                var leash = actor.GetComponent<EncounterLeash>();
                var row = new Row { time = Time.time, frame = Time.frameCount, name = actor.name,
                    position = actor.transform.position, player = player.transform.position,
                    health = actor.HealthNormalized, available = actor.IsAvailable,
                    yaw = actor.transform.eulerAngles.y, state = melee != null ? melee.State.ToString() :
                        ranged != null ? ranged.State.ToString() : shield.State.ToString(),
                    sequence = melee != null ? melee.Brain.AttackSequence : ranged != null ? ranged.Brain.AttackSequence : shield.Brain.AttackSequence,
                    authority = melee != null ? melee.HasSimulationAuthority : ranged != null ? ranged.HasSimulationAuthority : shield.HasSimulationAuthority,
                    enabled = ((Behaviour)actor).enabled,
                    fovHalf = (melee != null ? melee.Definition.FieldOfView : ranged != null ? ranged.Definition.FieldOfView : shield.Definition.FieldOfView) * .5f,
                    encounter = leash == null || leash.Encounter == null ? "" : leash.Encounter.TelemetrySegment,
                    encounterActive = leash != null && leash.Encounter != null && leash.Encounter.IsTelemetryActive,
                    leashAllows = leash == null || leash.AllowsTarget(player.transform.position),
                    playerAvailable = player.IsAvailable,
                    angle = Vector3.Angle(actor.transform.forward, Vector3.ProjectOnPlane(player.transform.position - actor.transform.position, Vector3.up)),
                    distance = Vector3.Distance(actor.transform.position, player.transform.position) };
                row.signedApproachBearing = Vector3.SignedAngle(actor.transform.forward,
                    Vector3.ProjectOnPlane(player.transform.position - actor.transform.position, Vector3.up), Vector3.up);
                row.onNavMesh = agent != null && agent.enabled && agent.isOnNavMesh;
                if (NavMesh.SamplePosition(actor.transform.position, out var sample, 2f, NavMesh.AllAreas))
                { row.navSample = true; row.navDistance = sample.distance; }
                if (row.onNavMesh)
                {
                    row.hasPath = agent.hasPath; row.pathStatus = agent.pathStatus.ToString();
                    row.velocity = agent.velocity.magnitude;
                    Vector3 desired = leash == null ? player.NavigationFootPosition : leash.ClampDestination(player.NavigationFootPosition);
                    var path = new NavMeshPath();
                    row.candidatePath = NavMesh.SamplePosition(desired, out var target, .8f, agent.areaMask) &&
                        (leash == null || leash.Contains(target.position, agent.radius)) &&
                        agent.CalculatePath(target.position, path) && path.status == NavMeshPathStatus.PathComplete &&
                        (leash == null || path.corners.All(c => leash.Contains(c)));
                }
                var direction = player.AimPoint.position - actor.AimPoint.position;
                if (Physics.Raycast(actor.AimPoint.position, direction.normalized, out var hit, direction.magnitude,
                    ~0, QueryTriggerInteraction.Ignore)) row.sightHit = Hierarchy(hit.collider.transform);
                var grounds = Physics.RaycastAll(actor.transform.position + Vector3.up * 3f, Vector3.down, 8f,
                    ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
                foreach (var ground in grounds)
                {
                    if (ground.collider.GetComponentInParent<CombatTarget>() != null) continue;
                    row.ground = Hierarchy(ground.collider.transform);
                    row.groundDelta = actor.transform.position.y - ground.point.y; break;
                }
                rows.Add(row);
            }
        }

        static void OnImpact(CombatImpactPresentationEvent impact)
        {
            var target = Object.FindObjectsOfType<CombatTarget>().FirstOrDefault(x => x.CombatantId == impact.TargetId);
            if (target == null || (!target.name.Contains("_Bridge_") && !target.name.Contains("_PreSanctum"))) return;
            // Emitted synchronously after the actual accepted damage/defense result, not a sampled health inference.
            hits.Add(new HitRow { time = Time.time, frame = Time.frameCount, name = target.name,
                player = observedPlayer.transform.position, enemy = target.transform.position,
                incidenceDegrees = Vector3.SignedAngle(target.transform.forward,
                    Vector3.ProjectOnPlane(observedPlayer.transform.position - target.transform.position, Vector3.up), Vector3.up),
                attack = impact.Attack.ToString(), grade = impact.Grade.ToString() });
        }

        static string Hierarchy(Transform t) => t.parent == null ? t.name : Hierarchy(t.parent) + "/" + t.name;

        static void Write()
        {
            string output = SessionState.GetString(Key + "output", "");
            File.WriteAllLines(output + "/enemy-participation.jsonl", rows.Select(r => JsonUtility.ToJson(r)));
            File.WriteAllLines(output + "/actual-hit-incidence.jsonl", hits.Select(r => JsonUtility.ToJson(r)));
            var summary = rows.GroupBy(r => r.name).Select(group => {
                var first = group.First(); var last = group.Last();
                return new Member { name = group.Key, samples = group.Count(), sequence = group.Max(r => r.sequence),
                    minimumHealth = group.Min(r => r.health),
                    maximumDisplacement = group.Max(r => Vector3.Distance(first.position, r.position)),
                    activeSamples = group.Count(r => r.available && r.encounterActive && r.leashAllows),
                    activeOutsideFov = group.Count(r => r.available && r.encounterActive && r.leashAllows && r.angle > r.fovHalf),
                    offNavMeshSamples = group.Count(r => !r.onNavMesh), lastState = last.state };
            }).ToArray();
            File.WriteAllText(output + "/enemy-participation-summary.json", JsonUtility.ToJson(new Summary { members = summary }, true));
        }

        [Serializable] sealed class Row
        {
            public float time, health, yaw, fovHalf, angle, signedApproachBearing, distance, navDistance, velocity, groundDelta;
            public int frame, sequence;
            public string name, state, encounter, pathStatus, sightHit, ground;
            public Vector3 position, player;
            public bool available, authority, enabled, encounterActive, leashAllows, playerAvailable,
                onNavMesh, navSample, hasPath, candidatePath;
        }
        [Serializable] sealed class HitRow
        { public float time, incidenceDegrees; public int frame; public string name, attack, grade; public Vector3 player, enemy; }
        [Serializable] sealed class Member
        {
            public string name, lastState;
            public int samples, sequence, activeSamples, activeOutsideFov, offNavMeshSamples;
            public float minimumHealth, maximumDisplacement;
        }
        [Serializable] sealed class Summary
        {
            public string scope = "Passive Editor observer; existing production-input pilot, AI on, isolated save. candidatePath is read-only feasibility, NOT an observed SetDestination return. Not Player/human acceptance or a historical bisect.";
            public Member[] members;
        }
    }
}
