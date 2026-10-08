#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Core.Content;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Networking;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using UnityEngine.Rendering;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Formal route integration, controlled lifecycle and real movement input; not human difficulty acceptance.</summary>
    public sealed class AshApproachEncounterTests
    {
        PlayerCombatActor player;
        M2RouteFlowController flow;
        SummonerEnemyActor caller;
        CombatEncounterCoordinator group;
        MeleeEnemyActor[] companions;
        Gamepad pad;
        bool ownHost;

        [UnitySetUp] public IEnumerator Begin()
        {
            Assert.That(M2RouteFlowController.EditorTestSavePath, Does.Contain("IsolatedSaves"));
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single);
            yield return null; yield return null;
            player = Object.FindObjectOfType<PlayerCombatActor>(); flow = Object.FindObjectOfType<M2RouteFlowController>();
            Assert.That(Path.GetFullPath(flow.SavePath), Is.EqualTo(Path.GetFullPath(M2RouteFlowController.EditorTestSavePath)));
            group = Object.FindObjectsOfType<CombatEncounterCoordinator>().Single(c => c.TelemetrySegment == "ash-approach-encounter");
            caller = group.GetComponentInChildren<SummonerEnemyActor>();
            companions = group.GetComponentsInChildren<MeleeEnemyActor>();
            Assert.That(caller.Brain, Is.Not.Null); Assert.That(companions.Length, Is.EqualTo(2));
        }
        [UnityTearDown] public IEnumerator End()
        {
            if (pad != null && pad.added) { InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.RemoveDevice(pad); }
            pad = null; yield return null;
            if (ownHost) { SessionRuntime.Current.Shutdown(); ownHost = false; yield return null; }
        }
        void Place(Vector3 position)
        {
            // Positions describe walkable feet, not the centre-root. Otherwise the fixture
            // buries the capsule in the floor and invokes real void recovery before a cast.
            Vector3 rootFromFeet = player.transform.position - player.NavigationFootPosition;
            var cc = player.GetComponent<CharacterController>(); cc.enabled = false;
            player.transform.position = position + rootFromFeet + Vector3.up * .04f; cc.enabled = true;
            Physics.SyncTransforms();
            Assert.That(Mathf.Abs(player.NavigationFootPosition.y - position.y), Is.LessThan(.05f));
        }
        static void Kill(CombatTarget target, int sequence) => Assert.That(target.ReceiveDamage(new DamageRequest(987, sequence, 9999f, 0, AttackTag.Heavy)).Killed, Is.True);

        [UnityTest] public IEnumerator FormalRoster_HasRealNavAndWholeNetExclusion()
        {
            Assert.That(group.SummonerMemberCount, Is.EqualTo(1)); Assert.That(group.MeleeMemberCount, Is.EqualTo(2));
            Assert.That(group.EndsAttemptOnExit, Is.True);
            foreach (var a in group.GetComponentsInChildren<NavMeshAgent>()) Assert.That(a.isOnNavMesh, Is.True, a.name);
            var adapter = Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true);
            var serialized = new SerializedObject(adapter);
            var roots = serialized.FindProperty("_offlineActorRoots");
            Assert.That(Enumerable.Range(0, roots.arraySize).Select(i => roots.GetArrayElementAtIndex(i).objectReferenceValue), Does.Contain(group.gameObject));
            Assert.That(adapter.ContainsOfflineBehaviour<SummonerEnemyActor>(), Is.True);
            Assert.That(Object.FindObjectOfType<NetworkGymSceneController>(true).AuthoredEncounterCount, Is.EqualTo(4), "No implicit NET summoner or new spawn definition.");
            Assert.That(caller.EncounterId, Is.EqualTo("encounter:ash-approach"));
            Assert.That(caller.Definition.Id.Value, Is.EqualTo("enemy:ash-caller"));
            yield return null;
        }

        [UnityTest] public IEnumerator VisibleWalls_MatchCollidersAndKeepThreePointTwoMetreDoorways()
        {
            yield return new WaitForSeconds(.7f);
            var walls = group.GetComponentsInChildren<Transform>().Where(t => t.name == "AshApproach_VisibleEndWall").ToArray();
            Assert.That(walls.Length, Is.EqualTo(4));
            foreach (Transform wall in walls)
            {
                var collider = wall.GetComponentsInChildren<Collider>().Single();
                var renderer = wall.GetComponentsInChildren<Renderer>().Single();
                Assert.That(Vector3.Distance(collider.bounds.size, new Vector3(2.6f, .8f, .5f)), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(collider.bounds.center, renderer.bounds.center), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(collider.bounds.size, renderer.bounds.size), Is.LessThan(.001f));
            }
            foreach (float z in new[] { 9.6f, 17f })
            {
                var pair = walls.Select(w => w.GetComponentInChildren<Collider>()).Where(c => Mathf.Abs(c.bounds.center.z - z) < .001f).OrderBy(c => c.bounds.center.x).ToArray();
                Assert.That(pair.Length, Is.EqualTo(2));
                Assert.That(pair[1].bounds.min.x - pair[0].bounds.max.x, Is.EqualTo(3.2f).Within(.001f));
            }
            foreach (Vector3 point in new[] { new Vector3(0, 0, 8.5f), new Vector3(0, 0, 13.3f), new Vector3(0, 0, 18.5f), new Vector3(0, 0, 28f) })
                Assert.That(NavMesh.SamplePosition(point, out NavMeshHit hit, .2f, NavMesh.AllAreas), Is.True);
        }

        [UnityTest] public IEnumerator PartialKill_ExitReentryDoesNotResetOrFalselyClear()
        {
            Place(new Vector3(0, 0, 11)); yield return null;
            Assert.That(group.IsTelemetryActive, Is.True);
            Kill(companions[0], 1); yield return null;
            Place(new Vector3(0, 0, 19)); yield return null;
            Assert.That(group.IsTelemetryActive, Is.False); Assert.That(companions[0].Brain.Health.IsDead, Is.True);
            Place(new Vector3(0, 0, 11)); yield return null;
            Assert.That(group.IsTelemetryActive, Is.True); Assert.That(companions[0].Brain.Health.IsDead, Is.True);
            Assert.That(flow.AshApproachCleared, Is.False);
            Assert.That(caller.IsAvailable, Is.True); Assert.That(companions[1].IsAvailable, Is.True);
        }

        [UnityTest] public IEnumerator OwnerAndBothCompanionsRequired_ClearSavesAndContinueDoesNotRespawn()
        {
            Place(new Vector3(0, 0, 11)); yield return null;
            Kill(caller, 10); yield return null;
            Assert.That(flow.AshApproachCleared, Is.False);
            Kill(companions[0], 11); yield return null;
            Assert.That(flow.AshApproachCleared, Is.False);
            Kill(companions[1], 12); yield return null;
            Assert.That(group.IsTelemetryActive, Is.False); Assert.That(flow.AshApproachCleared, Is.True);
            Assert.That(File.Exists(flow.SavePath), Is.True);
            M2LaunchIntent.RequestContinue(); yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null;
            Assert.That(Object.FindObjectOfType<M2RouteFlowController>().AshApproachCleared, Is.True);
            var restored = Object.FindObjectsOfType<CombatEncounterCoordinator>().Single(c => c.TelemetrySegment == "ash-approach-encounter");
            Assert.That(restored.GetComponentsInChildren<SummonerEnemyActor>(), Is.Empty);
            Assert.That(Object.FindObjectsOfType<CombatEncounterCoordinator>().Single(c => c.TelemetrySegment == "ash-guard-pass-encounter")
                .GetComponentInChildren<SummonerEnemyActor>().IsAvailable, Is.True, "Clearing A must not silently clear the independent B encounter.");
            Assert.That(restored.GetComponentsInChildren<MeleeEnemyActor>(), Is.Empty);
            Assert.That(restored.IsTelemetryActive, Is.False);
        }

        [UnityTest] public IEnumerator DisablingLivingMembers_DoesNotCountAsDefeat()
        {
            Place(new Vector3(0, 0, 11)); yield return null;
            caller.SetSimulationAuthority(false);
            foreach (var c in companions) c.gameObject.SetActive(false);
            caller.gameObject.SetActive(false); yield return null;
            Assert.That(flow.AshApproachCleared, Is.False);
        }

        [UnityTest] public IEnumerator RealMovementInput_CanEnterLeaveAndReachForestWithoutOverlappingActiveEncounters()
        {
            pad = InputSystem.AddDevice<Gamepad>();
            yield return Walk(new Vector3(.8f, 0, 12));
            Assert.That(group.IsTelemetryActive, Is.True);
            yield return Walk(new Vector3(.8f, 0, 20));
            Assert.That(group.IsTelemetryActive, Is.False);
            yield return Walk(new Vector3(0, 0, 28.5f));
            Assert.That(Object.FindObjectsOfType<CombatEncounterCoordinator>().Count(c => c.IsTelemetryActive), Is.EqualTo(1));
            Assert.That(Object.FindObjectsOfType<CombatEncounterCoordinator>().Single(c => c.IsTelemetryActive).TelemetrySegment, Is.EqualTo("forest-encounter"));
            Assert.That(player.IsAvailable, Is.True, "Traversal with actual AI must not borrow invulnerability or path warps.");
        }
        [UnityTest] public IEnumerator FormalCaller_ActuallySummonsAndEmitsOnlyOneMeaningfulBeatAcrossReset()
        {
            Place(new Vector3(0, 0, 10.5f));
            foreach (var companion in companions) companion.SetSimulationAuthority(false);
            int beats = 0;
            UnityEngine.Application.LogCallback observer = (message, stack, type) =>
            {
                // The established log sanitizer maps ':' to '_'; do not change the production schema for a fixture.
                if (message.StartsWith(PacingTelemetryRecorder.LogPrefix) && message.Contains("segment=encounter_ash-approach event=first-summon")) beats++;
            };
            UnityEngine.Application.logMessageReceived += observer;
            try
            {
                float until = Time.time + 12f;
                while (caller.LivingEntityCount == 0 && Time.time < until) { Assert.That(player.IsAvailable, Is.True); yield return null; }
                Assert.That(caller.LivingEntityCount, Is.EqualTo(1), $"state={caller.Brain.State} owner={caller.transform.position} playerRoot={player.transform.position} feet={player.NavigationFootPosition} active={group.IsTelemetryActive} casts={caller.Brain.SummonCount} preferred={caller.RetreatHasPreferredDestination} reachable={caller.RetreatBestReachableDistance} candidates={caller.RetreatCandidatesExamined} noNav={caller.RetreatNoNavCandidates} outside={caller.RetreatOutsideCandidates} incomplete={caller.RetreatIncompleteCandidates}");
                Assert.That(beats, Is.EqualTo(1), "The actual sanitized first-summon pacing event must be present.");
                var hud = Object.FindObjectOfType<Emberfall.UI.M2RouteHud>();
                var minion = caller.GetLivingEntity(0);
                Assert.That(minion, Is.Not.Null);
                yield return null;
                Assert.That(hud.ObservedLivingSummonCount, Is.EqualTo(1));
                minion.ApplyNeutralPostureDamage(999f);
                yield return null;
                Assert.That(hud.SummonExecutionReadyCount, Is.EqualTo(1), "Spawned entities must join the same marker/readiness loop as authored mobs.");
                yield return null; yield return null;
                string folder = "Builds/ArtReview/AshApproach/" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
                Directory.CreateDirectory(folder);
                var rt = new RenderTexture(1280, 720, 24); var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                var previous = RenderTexture.active;
                try
                {
                    rt.Create(); RenderPipeline.SubmitRenderRequest(Camera.main, new RenderPipeline.StandardRequest { destination = rt });
                    RenderTexture.active = rt; texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                    File.WriteAllBytes(folder + "/01-formal-camera-world.png", texture.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; rt.Release(); Object.Destroy(rt); Object.Destroy(texture); }
                // Actual game output including OnGUI, separate from the world-only camera render.
                ScreenCapture.CaptureScreenshot(Path.GetFullPath(folder + "/02-formal-game-hud.png"));
                float captureEnd = Time.realtimeSinceStartup + 4f;
                while (!File.Exists(folder + "/02-formal-game-hud.png") && Time.realtimeSinceStartup < captureEnd) yield return null;
                Assert.That(File.Exists(folder + "/02-formal-game-hud.png"), Is.True, "Preserve a missing HUD capture, do not borrow the world-only image.");
                caller.ResetToSpawn();
                until = Time.time + 12f;
                while (caller.LivingEntityCount == 0 && Time.time < until) { Assert.That(player.IsAvailable, Is.True); yield return null; }
                Assert.That(caller.LivingEntityCount, Is.EqualTo(1)); Assert.That(beats, Is.EqualTo(1), "Retries are diagnostic casts, not extra content beats.");
                Kill(caller, 99);
                Assert.That(caller.GetLivingEntity(0), Is.Null);
                yield return null;
                Assert.That(hud.ObservedLivingSummonCount, Is.Zero);
                Assert.That(hud.SummonExecutionReadyCount, Is.Zero);
                Assert.That(Object.FindObjectsOfType<MeleeEnemyActor>().Where(e => e.name == "Summoned_Fogwalker").All(e => !e.IsAvailable), Is.True);
            }
            finally { UnityEngine.Application.logMessageReceived -= observer; }
        }

        [UnityTest] public IEnumerator RealKnifeInput_LockedThrowInterruptsFormalSummonWithoutSpawning()
        {
            Place(new Vector3(0, 0, 10.5f));
            foreach (var companion in companions) companion.gameObject.SetActive(false);
            pad = InputSystem.AddDevice<Gamepad>();
            float until = Time.time + 12f;
            while (!caller.Brain.IsSummoning && Time.time < until)
            {
                // New neighbouring encounters are legitimate screen-centre candidates.
                // Aim through normal look input rather than hiding them or overriding lock authority.
                Vector3 viewport = Camera.main.WorldToViewportPoint(caller.AimPoint.position);
                Vector2 look = Vector2.ClampMagnitude(new Vector2(viewport.x - .5f, viewport.y - .5f) * 5f, 1f);
                InputSystem.QueueStateEvent(pad, new GamepadState { rightStick = look });
                Assert.That(player.IsAvailable, Is.True); yield return null;
            }
            Assert.That(caller.Brain.IsSummoning, Is.True);
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.RightStick));
            yield return null; InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
            Assert.That(player.GetComponent<Emberfall.Gameplay.Targeting.LockOnTargeting>().CurrentTarget, Is.SameAs(caller));
            int releaseBefore = player.Model.RangedReleaseSequence;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.RightShoulder));
            yield return null; InputSystem.QueueStateEvent(pad, new GamepadState());
            until = Time.time + 1f;
            while (caller.Brain.InterruptCount == 0 && Time.time < until) yield return null;
            Assert.That(player.Model.RangedReleaseSequence, Is.EqualTo(releaseBefore + 1), "Requires a real input-driven projectile, not direct ReceiveDamage.");
            Assert.That(caller.Brain.InterruptCount, Is.EqualTo(1));
            Assert.That(caller.Brain.SummonCount, Is.Zero); Assert.That(caller.LivingEntityCount, Is.Zero);
            Assert.That(caller.Brain.RetreatBlockedElapsed, Is.Zero);
            yield return null; // Let the ordinary HUD Update observe the real damage receipt before capture.
            string path = Path.GetFullPath("Builds/ArtReview/AshApproach/knife-interrupt-" + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            ScreenCapture.CaptureScreenshot(path);
            until = Time.realtimeSinceStartup + 4f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < until) yield return null;
            Assert.That(File.Exists(path), Is.True);
        }

        [UnityTest] public IEnumerator ListeningHost_WholeOfflineEncounterIsDisabledBeforeItsAiInitializes()
        {
            var service = SessionRuntime.Current; service.Shutdown(); yield return null;
            Assert.That(service.StartHost(47790), Is.True, service.Snapshot.Message); ownHost = true; yield return null;
            yield return SceneManager.LoadSceneAsync("10_EmberValley", LoadSceneMode.Single); yield return null;
            var adapter = Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true);
            Assert.That(adapter.NetworkModeActive, Is.True);
            var root = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Content_AshApproach_v1");
            Assert.That(root.activeInHierarchy, Is.False);
            Assert.That(root.GetComponentsInChildren<SummonerEnemyActor>(true).Single().Brain, Is.Null, "Offline AI must never Awaken in the NET authority route.");
            Assert.That(Object.FindObjectsOfType<SummonerEnemyActor>(), Is.Empty);
            Assert.That(Object.FindObjectsOfType<SummonedMinionPresentation>(), Is.Empty);
        }

        [UnityTest] public IEnumerator RetreatPlanner_LShapedLegalPathIsNotMistakenForACorner()
        { yield return CheckRetreatGeometry(true); }

        [UnityTest] public IEnumerator RetreatPlanner_DeadEndReportsReachableDistanceBelowPreferredBand()
        { yield return CheckRetreatGeometry(false); }

        IEnumerator CheckRetreatGeometry(bool lShape)
        {
            // Synthetic navigation regression only: no natural contact or difficulty claim.
            player.GetComponent<Emberfall.Gameplay.Movement.ThirdPersonMotor>().enabled = false;
            player.GetComponent<CharacterController>().enabled = false;
            group.enabled = false;
            Vector3 center = new Vector3(103, 0, lShape ? 7 : 3);
            group.ConfigureTelemetryArena(center, new Vector2(3, lShape ? 7 : 3), .15f);
            var sources = new System.Collections.Generic.List<NavMeshBuildSource>();
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.TRS(new Vector3(103, -.1f, lShape ? 2 : 3), Quaternion.identity, Vector3.one),
                size = new Vector3(6, .2f, lShape ? 4 : 6), area = 0 });
            if (lShape) sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.TRS(new Vector3(104, -.1f, 7), Quaternion.identity, Vector3.one),
                size = new Vector3(4, .2f, 14), area = 0 });
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(center, new Vector3(10, 4, 18)), Vector3.zero, Quaternion.identity);
            Assert.That(data, Is.Not.Null);
            var nav = NavMesh.AddNavMeshData(data);
            var holder = new GameObject("SyntheticSummonerNavigation"); holder.SetActive(false);
            try
            {
                Vector3 rootFromFeet = player.transform.position - player.NavigationFootPosition;
                Assert.That(NavMesh.SamplePosition(new Vector3(100.9f, 0, 2), out NavMeshHit foot, .8f, NavMesh.AllAreas), Is.True);
                player.transform.position = foot.position + rootFromFeet;
                var clone = Object.Instantiate(caller.gameObject, holder.transform);
                clone.GetComponent<EncounterLeash>().Configure(group);
                Assert.That(NavMesh.SamplePosition(new Vector3(lShape ? 101.8f : 104, 0, lShape ? 2 : 4), out NavMeshHit spawn, .8f, NavMesh.AllAreas), Is.True);
                clone.transform.SetPositionAndRotation(spawn.position, Quaternion.LookRotation(player.transform.position - spawn.position));
                caller.SetSimulationAuthority(false); holder.SetActive(true);
                var subject = clone.GetComponent<SummonerEnemyActor>();
                float until = Time.time + 1f;
                while (subject.RetreatCandidatesExamined == 0 && Time.time < until) yield return null;
                Assert.That(subject.RetreatCandidatesExamined, Is.InRange(1, 9));
                Assert.That(subject.RetreatHasPreferredDestination, Is.EqualTo(lShape));
                Assert.That(subject.RetreatUnavailable, Is.EqualTo(!lShape));
                if (lShape)
                {
                    Assert.That(subject.RetreatBestReachableDistance, Is.GreaterThanOrEqualTo(8f));
                    var path = new NavMeshPath();
                    Assert.That(subject.GetComponent<NavMeshAgent>().CalculatePath(subject.RetreatDestination, path), Is.True);
                    Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
                    Assert.That(path.corners.Length, Is.GreaterThanOrEqualTo(3), "The L regression must actually require a bend.");
                    Assert.That(path.corners.All(p => subject.GetComponent<EncounterLeash>().Contains(p)), Is.True);
                }
                else
                {
                    Assert.That(subject.RetreatBestReachableDistance, Is.LessThan(8f));
                    Assert.That(subject.Brain.IsSummoning, Is.False, "Entry/low distance cannot instantly authorize a chant.");
                }
                string folder = "Builds/ArtReview/AshApproach/Navigation";
                Directory.CreateDirectory(folder);
                File.WriteAllText(folder + "/" + (lShape ? "l-path-" : "dead-end-") + System.DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".txt",
                    $"preferred={subject.RetreatHasPreferredDestination}; unavailable={subject.RetreatUnavailable}; selected={subject.RetreatDestination}; reachable={subject.RetreatBestReachableDistance}; examined={subject.RetreatCandidatesExamined}; noNav={subject.RetreatNoNavCandidates}; outside={subject.RetreatOutsideCandidates}; incomplete={subject.RetreatIncompleteCandidates}\nSynthetic geometry, not human difficulty evidence.");
            }
            finally { holder.SetActive(false); Object.Destroy(holder); nav.Remove(); Object.Destroy(data); }
        }

        // Real input traversal never substitutes a test-only transform path for normal motor movement.
        IEnumerator Walk(Vector3 target)
        {
            float deadline = Time.time + 12f;
            while (Vector3.ProjectOnPlane(player.transform.position - target, Vector3.up).magnitude > .45f && Time.time < deadline)
            {
                Vector3 direction = Vector3.ProjectOnPlane(target - player.transform.position, Vector3.up).normalized;
                Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)) });
                Assert.That(player.IsAvailable, Is.True); yield return null;
            }
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null;
            Assert.That(Vector3.ProjectOnPlane(player.transform.position - target, Vector3.up).magnitude, Is.LessThan(.6f), "Normal motor/collider traversal failed; do not warp or loosen the doorway.");
        }
    }
}
#endif
