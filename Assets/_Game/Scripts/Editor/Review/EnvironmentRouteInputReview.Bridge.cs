using System;
using System.Linq;
using Emberfall.Core.Content;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Interaction;
using Emberfall.Gameplay.Targeting;
using Emberfall.Application.Flow;
using Emberfall.Quests.Domain;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    public static partial class EnvironmentRouteInputReview
    {
        static bool bridgeRun;
        static bool endRun;
        static bool cameraReview;
        static double heavyUntil;
        static CombatTarget[] combatTargets;
        static float lastEnemyHealth;
        static double nextCombatButton;
        static double orbitStarted;
        static readonly float[] OrbitYaws = { 27,108,189,264,342 };
        static readonly Vector3 OrbitStation = new Vector3(11.762124f,1.1f,47.786297f);
        static int orbitIndex;
        static double orbitSettled;
        static bool orbitCaptured;

        static void Advance(int next, double now, string label)
        {
            Capture(label); logs.Add((now - started).ToString("F3") + " phase=" + next + " " + label);
            phase = next; target = null; combatTargets = null; lastEnemyHealth = -1;
            lastProgress = now; progressPosition = player.transform.position;
        }

        static void TickBridge(double now)
        {
            if (!ContentPackageRuntime.IsInitialized) throw new InvalidOperationException("Extended route requires real content initialization.");
            var forest = flow.ForestTemplate;
            if (phase == 2 && forest.Phase != ForestSealPhase.Encounter) Advance(3, now, "forest-cleared");
            if (phase == 3 && forest.Phase == ForestSealPhase.SigilClaimed) Advance(4, now, "sigil-claimed");
            if (phase == 4 && forest.Phase == ForestSealPhase.RuneChoice) Advance(5, now, "forest-seal-activated");
            if (phase == 5 && forest.Phase == ForestSealPhase.Completed) Advance(6, now, "guard-rune-chosen");
            if (phase == 6 && flow.BridgeMechanismAActivated) Advance(7, now, "bridge-A-activated");
            if (phase == 7 && flow.BridgeEncounterCleared) Advance(8, now, "bridge-cleared");
            if (phase == 8 && flow.BridgeMechanismBActivated)
            {
                Advance(endRun || !cameraReview ? 10 : 11, now, "bridge-B-activated"); orbitStarted = now;
                orbitIndex=0; orbitSettled=0; orbitCaptured=false;
            }
            if(phase==11)
            {
                // Walk back to the recorded problem station; no relocation/disabled collision.
                var stationInput=new GamepadState();
                if(Vector3.ProjectOnPlane(player.transform.position-OrbitStation,Vector3.up).magnitude>.08f)
                    Steer(ref stationInput,OrbitStation);
                else { Advance(9,now,"bridge-camera-station"); orbitStarted=now; }
                InputSystem.QueueStateEvent(pad,stationInput);
                if(now>=nextSample)
                {
                    nextSample=now+.1;
                    samples.Add(new Sample { seconds=now-started,position=player.transform.position,move=dynamicMove,
                        requestedMove=stationInput.leftStick,health=player.HealthNormalized,phase=phase,candidate="camera-station" });
                }
                if(now-orbitStarted>20) Finish(false,"Cannot physically reach recorded camera station in20s.");
                return;
            }
            if (phase == 9)
            {
                // Real Look binding, then a stationary hold so asynchronous screenshots match stable poses.
                float error = Mathf.DeltaAngle(Camera.main.transform.eulerAngles.y, OrbitYaws[orbitIndex]);
                var orbitInput = new GamepadState();
                if (Mathf.Abs(error) > .6f && !orbitCaptured)
                {
                    orbitSettled=0;
                    // Stay above the existing stick dead zone; do not change the player's input settings.
                    orbitInput.rightStick=new Vector2(Mathf.Sign(error)*Mathf.Clamp(Mathf.Abs(error)/40f,.2f,.55f),0);
                }
                else
                {
                    if(orbitSettled==0) orbitSettled=now;
                    if(!orbitCaptured && now-orbitSettled>.45)
                    { Capture("bridge-B-orbit-"+orbitIndex); orbitCaptured=true; }
                    if(orbitCaptured && now-orbitSettled>.9)
                    {
                        orbitIndex++; orbitCaptured=false; orbitSettled=0;
                        if(orbitIndex==OrbitYaws.Length) Advance(10,now,"bridge-orbit-complete");
                    }
                }
                InputSystem.QueueStateEvent(pad,orbitInput);
                if(now-orbitStarted>40) Finish(false,"Camera orbit input did not settle within40s.");
                return;
            }
            if (phase == 10 && logs.Any(x => x.Contains("segment=bridge-seal event=activated")))
            {
                if (!endRun)
                {
                    Finish(true, "Forest and bridge A/combat/B plus bridge seal activated via input; camera acceptance remains separate.");
                    return;
                }
                Advance(12, now, "bridge-seal-complete");
            }
            if (phase == 12 && GroupDefeated("_Courtyard", 3)) Advance(13, now, "courtyard-cleared");
            if (phase == 13 && flow.Stage == MainQuestStage.EnterSanctum) Advance(14, now, "courtyard-seal-activated");
            if (phase == 14 && GroupDefeated("_PreSanctum", 3)) Advance(15, now, "presanctum-cleared");
            if (phase == 15 && logs.Any(x => x.Contains("segment=checkpoint:courtyard event=activated")))
                Advance(16, now, "checkpoint-activated");
            if (phase == 16 && flow.Stage == MainQuestStage.DefeatWarden) Advance(17, now, "sanctum-open");
            if (phase == 17 && flow.Stage == MainQuestStage.ReturnToScout) Advance(18, now, "warden-defeated");
            if (phase == 18 && flow.IsComplete) { Finish(true, "Main route returned to scout and completed through real input."); return; }
            var state = new GamepadState();
            if (phase == 2 || phase == 7 || phase == 12 || phase == 14 || phase == 17) state = Fight(now);
            else
            {
                if (target == null)
                {
                    if (phase == 3) target = forest.SigilPickup;
                    if (phase == 4) target = Object.FindObjectsOfType<M2RouteInteractable>().Single(x => x.StableId.Value == "seal:forest");
                    if (phase == 5) target = forest.GuardRune;
                    if (phase == 10) target = Object.FindObjectsOfType<M2RouteInteractable>().Single(x => x.StableId.Value == "seal:bridge");
                    if (phase == 13) target = Object.FindObjectsOfType<M2RouteInteractable>().Single(x => x.StableId.Value == "seal:courtyard");
                    if (phase == 15) target = Object.FindObjectsOfType<M2RouteInteractable>().Single(x => x.StableId.Value == "checkpoint:courtyard");
                    if (phase == 16) target = Object.FindObjectsOfType<M2RouteInteractable>().Single(x => x.Role == M2RouteRole.SanctumGate);
                    if (phase == 18) target = Object.FindObjectsOfType<M2RouteInteractable>().Single(x => x.Role == M2RouteRole.Scout);
                    if (phase == 6 || phase == 8) target = Object.FindObjectsOfType<BridgeMechanismInteractable>()
                        .Single(x => x.StableId == (phase == 6 ? "bridge-mechanism:A" : "bridge-mechanism:B"));
                }
                var locking = player.GetComponent<LockOnTargeting>();
                if (locking.IsLocked && now >= nextCombatButton)
                { state = state.WithButton(GamepadButton.RightStick); nextCombatButton = now + .35; }
                var candidate = player.GetComponent<PlayerInteractor>().CurrentCandidate;
                if (ReferenceEquals(candidate, target))
                {
                    if ((int)((now - started) * 4) % 2 == 0) state = state.WithButton(GamepadButton.South);
                }
                else Steer(ref state, target.transform.position);
            }
            InputSystem.QueueStateEvent(pad, state);
            if (Vector3.Distance(progressPosition, player.transform.position) > .4f)
            { progressPosition = player.transform.position; lastProgress = now; }
            if (now >= nextSample)
            {
                nextSample = now + .1;
                var candidate = player.GetComponent<PlayerInteractor>().CurrentCandidate;
                samples.Add(new Sample { seconds = now - started, position = player.transform.position, move = dynamicMove,
                    requestedMove = state.leftStick, health = player.HealthNormalized, phase = phase,
                    candidate = candidate == null ? "" : candidate.InteractionTransform.name });
            }
            if (now >= nextShot) { nextShot = now + 4; Capture("route-" + samples.Count); }
            if (now - lastProgress > 12) Finish(false, "No movement/damage/interaction progress for 12s; phase=" + phase + " at " + player.transform.position);
        }

        static GamepadState Fight(double now)
        {
            if (combatTargets == null)
            {
                string suffix = phase == 2 ? "_Forest" : phase == 7 ? "_Bridge_" : phase == 12 ? "_Courtyard" : phase == 14 ? "_PreSanctum" : "Enemy_EmberWarden";
                combatTargets = Object.FindObjectsOfType<CombatTarget>().Where(x => x != player && x.name.Contains(suffix)).ToArray();
                int expected = phase == 17 ? 1 : phase >= 12 ? 3 : 2;
                if (combatTargets.Length != expected) throw new InvalidOperationException("Expected " + expected + " authored enemies, found " + combatTargets.Length);
            }
            float remaining = combatTargets.Where(x => x != null).Sum(x => x.HealthNormalized + .01f * x.SecondaryResourceNormalized);
            if (remaining != lastEnemyHealth)
            {
                lastEnemyHealth = remaining; lastProgress = now;
                logs.Add((now - started).ToString("F3") + " combat " + string.Join(";", combatTargets.Where(x => x != null)
                    .Select(x => x.name + "=" + x.HealthNormalized.ToString("F3") + "/secondary=" + x.SecondaryResourceNormalized.ToString("F3") + "/available=" + x.IsAvailable)));
            }
            var living = combatTargets.Where(x => x != null && x.IsAvailable).ToArray();
            if (living.Length == 0) return new GamepadState(); // Coordinator must advance, never force it.
            var locking = player.GetComponent<LockOnTargeting>();
            var enemy = locking.IsLocked && living.Contains(locking.CurrentTarget) ? locking.CurrentTarget :
                living.OrderBy(x => Vector3.Distance(player.transform.position, x.transform.position)).First();
            var state = new GamepadState();
            Vector3 toEnemy = Vector3.ProjectOnPlane(enemy.transform.position - player.transform.position, Vector3.up);
            float distance = toEnemy.magnitude;
            // Stop queueing attacks before attempting to heal; otherwise continuous combos starve Locomotion.
            if (player.HealthNormalized < .70f && player.Model.HealingFlasks.CurrentCharges > 0)
            {
                heavyUntil = 0;
                if (player.Model.State == CombatState.Heal) return state;
                float nearest = living.Min(x => Vector3.ProjectOnPlane(x.transform.position-player.transform.position, Vector3.up).magnitude);
                if (nearest < 4f)
                {
                    Vector3 retreat = player.NavigationFootPosition;
                    float best = float.NegativeInfinity;
                    for (int i=0;i<8;i++)
                    {
                        Vector3 offset = Quaternion.Euler(0,i*45,0)*Vector3.forward*3.5f;
                        NavMeshHit hit;
                        if (!NavMesh.SamplePosition(player.NavigationFootPosition+offset,out hit,.8f,NavMesh.AllAreas)) continue;
                        var path = new NavMeshPath();
                        if (!NavMesh.CalculatePath(player.NavigationFootPosition,hit.position,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete) continue;
                        float score = living.Min(x=>Vector3.ProjectOnPlane(hit.position-x.transform.position,Vector3.up).sqrMagnitude);
                        if(score>best) { best=score; retreat=hit.position; }
                    }
                    Steer(ref state, retreat);
                    if (nearest < 2.5f && player.Model.State == CombatState.Locomotion && now >= nextCombatButton)
                    { nextCombatButton=now+.7; state=state.WithButton(GamepadButton.East); }
                    return state;
                }
                if (player.Model.State == CombatState.Locomotion && now >= nextCombatButton)
                { nextCombatButton=now+.4; return state.WithButton(GamepadButton.DpadUp); }
                return state;
            }
            if (distance > 1.65f) Steer(ref state, enemy.transform.position);
            else
            {
                Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                state.rightStick = new Vector2(Mathf.Clamp(Vector3.SignedAngle(forward, toEnemy, Vector3.up) / 70f, -1, 1), 0);
            }
            if (now < heavyUntil) return state.WithButton(GamepadButton.North);
            if (heavyUntil > 0) { heavyUntil = 0; nextCombatButton = now + .3; return state; }
            if (now < nextCombatButton) return state;
            nextCombatButton = now + .3;
            if (player.ExecutionPromptTarget != null && player.Model.State == CombatState.Locomotion)
                return state.WithButton(GamepadButton.South);
            if (!locking.IsLocked && distance < 12 && Vector3.Angle(Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up), toEnemy) < 40)
                state = state.WithButton(GamepadButton.RightStick); // A failed lock acquisition must not starve attack input.
            if (enemy.IsThreatening && distance < 3 && player.Model.State == CombatState.Locomotion)
            { state.leftStick = new Vector2(1, 0); return state.WithButton(GamepadButton.East); }
            if (living.Count(x=>Vector3.Distance(x.transform.position,player.transform.position)<3f)>=2 &&
                player.Model.State == CombatState.Locomotion && player.Model.SweepCooldownRemaining <= 0)
                return state.WithButton(GamepadButton.DpadLeft);
            if (enemy is Emberfall.AI.Unity.ShieldEnemyActor && distance < 2.25f && player.Model.State == CombatState.Locomotion)
            { heavyUntil = now + .85; return state.WithButton(GamepadButton.North); }
            if (distance < 2.25f) return state.WithButton(GamepadButton.West);
            if (distance < 12 && locking.IsLocked) return state.WithButton(GamepadButton.RightShoulder);
            return state;
        }

        static bool GroupDefeated(string suffix, int expected)
        {
            var group = Object.FindObjectsOfType<CombatTarget>().Where(x => x != player && x.name.Contains(suffix)).ToArray();
            if (group.Length != expected) throw new InvalidOperationException("Authored group mismatch: " + suffix);
            return group.All(x => !x.IsAvailable);
        }

        static void Steer(ref GamepadState state, Vector3 destination)
        {
            NavMeshHit startHit, endHit;
            if (!NavMesh.SamplePosition(player.NavigationFootPosition, out startHit, .8f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(destination, out endHit, 2.2f, NavMesh.AllAreas))
                throw new InvalidOperationException("Navigation endpoint unavailable: " + destination);
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException("Incomplete path at phase " + phase + " from " + startHit.position + " to " + endHit.position);
            Vector3 corner = path.corners.Last();
            foreach (var point in path.corners.Skip(1))
                if (Vector3.ProjectOnPlane(point - player.NavigationFootPosition, Vector3.up).magnitude > .35f) { corner = point; break; }
            Vector3 direction = Vector3.ProjectOnPlane(corner - player.NavigationFootPosition, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            state.leftStick = new Vector2(Vector3.Dot(direction, Vector3.Cross(Vector3.up, forward)), Vector3.Dot(direction, forward));
            state.rightStick = new Vector2(Mathf.Clamp(Vector3.SignedAngle(forward, direction, Vector3.up) / 70f, -1, 1), 0);
        }
    }
}
