using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Production actors and natural Update; scripted player, not a full quest/visual acceptance.</summary>
    public sealed class EnemyNavigationFollowupTests
    {
        private readonly List<string> _rows = new List<string>();
        private PlayerCombatActor _player;

        [UnityTearDown]
        public IEnumerator SaveEvidence()
        {
            string dir = "Builds/ArtReview/0.9.6-navigation-followup";
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, TestContext.CurrentContext.Test.Name + "-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".txt"), _rows);
            _rows.Clear();
            yield return null;
        }

        private IEnumerator Load(string activeEnemy)
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley");
            yield return null;
            yield return null;
            _player = Object.FindObjectOfType<PlayerCombatActor>();
            // Isolate navigation from unrelated encounter damage, preserving this actor's authored leash.
            foreach (var coordinator in Object.FindObjectsOfType<CombatEncounterCoordinator>()) coordinator.enabled = false;
            foreach (var actor in Object.FindObjectsOfType<MonoBehaviour>())
                if (actor is MeleeEnemyActor || actor is ShieldEnemyActor || actor is RangedEnemyActor || actor is WardenActor)
                    if (actor.name != activeEnemy)
                    {
                        actor.enabled = false;
                        var agent = actor.GetComponent<NavMeshAgent>();
                        if (agent != null && agent.isOnNavMesh) { agent.isStopped = true; agent.ResetPath(); }
                    }
            _rows.Add("Formal Valley; unrelated AI/coordinators isolated. No enemy warp, manual Tick, injected damage or phase mutation.");
        }

        private void PlacePlayer(Vector3 feet)
        {
            Assert.That(NavMesh.SamplePosition(feet, out var hit, .8f, NavMesh.AllAreas), Is.True);
            var controller = _player.GetComponent<CharacterController>();
            controller.enabled = false;
            _player.transform.position = hit.position + Vector3.up * (controller.height * .5f - controller.center.y + .05f);
            controller.enabled = true;
            _player.GetComponent<ThirdPersonMotor>().ResetAfterTeleport();
            Physics.SyncTransforms();
        }

        [UnityTest]
        public IEnumerator Ranged_OutsidePreferredRange_ApproachesAndCasts()
        {
            yield return Load("Enemy_RunePriest_Courtyard");
            var enemy = Object.FindObjectsOfType<RangedEnemyActor>().Single(x => x.name == "Enemy_RunePriest_Courtyard");
            PlacePlayer(new Vector3(22, 0, 43));
            Vector3 start = enemy.transform.position;
            float initial = Vector3.Distance(start, _player.transform.position), until = Time.time + 12, next = 0;
            Assert.That(initial, Is.GreaterThan(enemy.Definition.PreferredMaximumRange));
            Assert.That(enemy.GetComponent<EncounterLeash>().AllowsTarget(_player.transform.position), Is.True);
            bool approach = false, released = false;
            while (Time.time < until && !released)
            {
                approach |= enemy.State == RangedEnemyState.Approach;
                released |= enemy.State == RangedEnemyState.Release;
                if (Time.time >= next) { LogRanged(enemy); next = Time.time + .2f; }
                yield return null;
            }
            float moved = Vector3.Distance(start, enemy.transform.position);
            _rows.Add($"initial={initial:F3} moved={moved:F3} approach={approach} released={released}");
            Assert.That(approach && released, Is.True, "Must approach and naturally cast within 12s.");
            Assert.That(moved, Is.GreaterThan(1));
            Assert.That(Vector3.Distance(enemy.transform.position, _player.transform.position),
                Is.InRange(enemy.Definition.PreferredMinimumRange - .2f, enemy.Definition.PreferredMaximumRange + .2f));
        }

        [UnityTest]
        public IEnumerator Ranged_InsidePreferredRange_RetreatsAndCasts()
        {
            yield return Load("Enemy_RunePriest_Courtyard");
            var enemy = Object.FindObjectsOfType<RangedEnemyActor>().Single(x => x.name == "Enemy_RunePriest_Courtyard");
            PlacePlayer(enemy.transform.position + Vector3.back * 2.5f);
            Vector3 start = enemy.transform.position;
            float initial = Vector3.Distance(start, _player.transform.position), until = Time.time + 12, next = 0;
            Assert.That(initial, Is.LessThan(enemy.Definition.PreferredMinimumRange));
            bool retreat = false, released = false;
            while (Time.time < until && !released)
            {
                retreat |= enemy.State == RangedEnemyState.Retreat;
                released |= enemy.State == RangedEnemyState.Release;
                if (Time.time >= next) { LogRanged(enemy); next = Time.time + .2f; }
                yield return null;
            }
            float distance = Vector3.Distance(enemy.transform.position, _player.transform.position);
            _rows.Add($"initial={initial:F3} final={distance:F3} moved={Vector3.Distance(start, enemy.transform.position):F3} retreat={retreat} released={released}");
            Assert.That(retreat && released, Is.True, "Must retreat and naturally cast within 12s.");
            Assert.That(distance, Is.GreaterThan(initial + 1));
            Assert.That(distance, Is.InRange(enemy.Definition.PreferredMinimumRange - .2f, enemy.Definition.PreferredMaximumRange + .2f));
        }

        private void LogRanged(RangedEnemyActor enemy)
        {
            var agent = enemy.GetComponent<NavMeshAgent>();
            bool raw = NavMesh.SamplePosition(_player.transform.position, out _, .8f, NavMesh.AllAreas);
            bool foot = NavMesh.SamplePosition(_player.NavigationFootPosition, out _, .8f, NavMesh.AllAreas);
            _rows.Add($"t={Time.time:F3} state={enemy.State} enemy={enemy.transform.position:F3} player={_player.transform.position:F3} rawSample={raw} footSample={foot} stopped={agent.isStopped} path={agent.pathStatus}");
        }

        [UnityTest]
        public IEnumerator Warden_ApproachesAndFightsThroughNaturalPhaseGate()
        {
            // Actual Warden name is obtained without changing its production spawn or configuration.
            yield return Load(null);
            var enemy = Object.FindObjectOfType<WardenActor>();
            enemy.enabled = true;
            PlacePlayer(enemy.transform.position + Vector3.back * 9.7f);
            enemy.SetEncounterEnabled(true); // Encounter fixture, not a claim of quest progression.
            Vector3 start = enemy.transform.position;
            var observed = new HashSet<WardenAttackKind>();
            bool transition = false, blastBorn = false;
            float until = Time.time + 90, next = 0, chaseTravel = 0;
            Vector3 previous = start;
            var controller = _player.GetComponent<CharacterController>();
            while (Time.time < until && !(blastBorn && observed.Contains(WardenAttackKind.RuneCleave)))
            {
                Assert.That(_player.IsAvailable, Is.True, "Scripted player died; do not bypass damage or phase gate.");
                if (enemy.State == WardenState.Chase) chaseTravel += Vector3.Distance(previous, enemy.transform.position);
                previous = enemy.transform.position;
                if (enemy.State == WardenState.Attack) observed.Add(enemy.Brain.CurrentAttack);
                transition |= enemy.State == WardenState.PhaseTransition;
                blastBorn |= Object.FindObjectsOfType<RangedGroundRune>().Any(r => r.name.StartsWith("WardenDelayedBlast_"));
                Vector3 toEnemy = Vector3.ProjectOnPlane(enemy.transform.position - _player.transform.position, Vector3.up);
                if (toEnemy.sqrMagnitude > .01f) _player.transform.rotation = Quaternion.LookRotation(toEnemy);
                // Real combat commands; actual player Update performs timing, queries and damage.
                bool defend = enemy.State == WardenState.Windup || enemy.State == WardenState.Attack;
                _player.Model.Submit(defend ? CombatCommand.GuardPressed : CombatCommand.GuardReleased);
                if (_player.Model.Health.Normalized < .55f && !defend) _player.Model.Submit(CombatCommand.Heal);
                bool phaseOne = enemy.Phase == WardenPhase.PhaseOne;
                if (phaseOne && enemy.Brain.IsRecoveryOpening && toEnemy.magnitude < 2.1f)
                    _player.Model.Submit(CombatCommand.LightAttack);
                float desiredDistance = !observed.Contains(WardenAttackKind.Charge) ? 9.7f :
                    phaseOne || !observed.Contains(WardenAttackKind.RuneCleave) ? 1.95f : 5.5f;
                // Spatial fixture, not hardware-input/motor acceptance. Move only in free locomotion;
                // do not accidentally grant full walking speed during the production .35-speed guard.
                if (observed.Contains(WardenAttackKind.Charge) && _player.Model.State == CombatState.Locomotion)
                {
                    float error = toEnemy.magnitude - desiredDistance;
                    if (Mathf.Abs(error) > .12f)
                        controller.Move(toEnemy.normalized * Mathf.Clamp(error, -5.4f * Time.deltaTime, 5.4f * Time.deltaTime));
                }
                if (Time.time >= next)
                {
                    _rows.Add($"t={Time.time:F3} state={enemy.State} attack={enemy.Brain.CurrentAttack} phase={enemy.Phase} bossHP={enemy.Brain.Health.Current:F3} playerHP={_player.Model.Health.Current:F3} playerState={_player.Model.State} distance={toEnemy.magnitude:F3} enemy={enemy.transform.position:F3} player={_player.transform.position:F3} chaseTravel={chaseTravel:F3}");
                    next = Time.time + .2f;
                }
                yield return null;
            }
            _rows.Add($"observed={string.Join(",", observed)} transition={transition} blastBorn={blastBorn} chaseTravel={chaseTravel:F3}");
            Assert.That(chaseTravel, Is.GreaterThan(.5f), "Must walk into attack range, not only charge.");
            Assert.That(observed, Does.Contain(WardenAttackKind.Charge));
            Assert.That(observed, Does.Contain(WardenAttackKind.SwordCombo));
            Assert.That(observed, Does.Contain(WardenAttackKind.RuneCleave));
            Assert.That(observed, Does.Contain(WardenAttackKind.DelayedBlast));
            Assert.That(transition && blastBorn, Is.True, "Real phase gate and spawned blast required, not injected state.");
        }
    }
}
