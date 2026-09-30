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
        static CombatTarget[] combatTargets;
        static float lastEnemyHealth;
        static double nextCombatButton;
        static double orbitStarted, nextOrbitShot;

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
                Advance(9, now, "bridge-B-activated"); orbitStarted = nextOrbitShot = now;
            }
            if (phase == 9)
            {
                // Diagnostic camera orbit through the actual Look binding, after normal B interaction.
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = new Vector2(.5f, 0) });
                if (now >= nextOrbitShot) { Capture("bridge-B-orbit-" + (int)(now - orbitStarted)); nextOrbitShot = now + 1; }
                if (now - orbitStarted >= 4.4) Advance(10, now, "bridge-orbit-complete");
                return;
            }
            if (phase == 10 && logs.Any(x => x.Contains("segment=bridge-seal event=activated")))
            {
                Finish(true, "Forest and bridge A/combat/B plus bridge seal activated via input; camera acceptance remains separate.");
                return;
            }
            var state = new GamepadState();
            if (phase == 2 || phase == 7) state = Fight(now);
            else
            {
                if (target == null)
                {
                    if (phase == 3) target = forest.SigilPickup;
                    if (phase == 4) target = Object.FindObjectsOfType<M2RouteInteractable>().Single(x => x.StableId.Value == "seal:forest");
                    if (phase == 5) target = forest.GuardRune;
                    if (phase == 10) target = Object.FindObjectsOfType<M2RouteInteractable>().Single(x => x.StableId.Value == "seal:bridge");
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
                string suffix = phase == 2 ? "_Forest" : "_Bridge_";
                combatTargets = Object.FindObjectsOfType<CombatTarget>().Where(x => x != player && x.name.Contains(suffix)).ToArray();
                if (combatTargets.Length != 2) throw new InvalidOperationException("Expected two authored enemies, found " + combatTargets.Length);
            }
            float remaining = combatTargets.Where(x => x != null).Sum(x => x.HealthNormalized);
            if (remaining != lastEnemyHealth)
            {
                lastEnemyHealth = remaining; lastProgress = now;
                logs.Add((now - started).ToString("F3") + " combat " + string.Join(";", combatTargets.Where(x => x != null)
                    .Select(x => x.name + "=" + x.HealthNormalized.ToString("F3") + "/available=" + x.IsAvailable)));
            }
            var living = combatTargets.Where(x => x != null && x.IsAvailable).ToArray();
            if (living.Length == 0) return new GamepadState(); // Coordinator must advance, never force it.
            var locking = player.GetComponent<LockOnTargeting>();
            var enemy = locking.IsLocked && living.Contains(locking.CurrentTarget) ? locking.CurrentTarget :
                living.OrderBy(x => Vector3.Distance(player.transform.position, x.transform.position)).First();
            var state = new GamepadState();
            Vector3 toEnemy = Vector3.ProjectOnPlane(enemy.transform.position - player.transform.position, Vector3.up);
            float distance = toEnemy.magnitude;
            if (distance > 1.65f) Steer(ref state, enemy.transform.position);
            else
            {
                Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                state.rightStick = new Vector2(Mathf.Clamp(Vector3.SignedAngle(forward, toEnemy, Vector3.up) / 70f, -1, 1), 0);
            }
            if (now < nextCombatButton) return state;
            nextCombatButton = now + .3;
            if (player.HealthNormalized < .58f && player.Model.HealingFlasks.CurrentCharges > 0 && player.Model.State == CombatState.Locomotion)
            { state.leftStick = Vector2.zero; return state.WithButton(GamepadButton.DpadUp); }
            if (!locking.IsLocked && distance < 12 && Vector3.Angle(Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up), toEnemy) < 40)
                state = state.WithButton(GamepadButton.RightStick); // A failed lock acquisition must not starve attack input.
            if (enemy.IsThreatening && distance < 3 && player.Model.State == CombatState.Locomotion)
            { state.leftStick = new Vector2(1, 0); return state.WithButton(GamepadButton.East); }
            if (distance < 2.25f) return state.WithButton(GamepadButton.West);
            if (distance < 12 && locking.IsLocked) return state.WithButton(GamepadButton.RightShoulder);
            return state;
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
