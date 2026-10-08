using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using Emberfall.Infrastructure.Saves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace Emberfall.Tests.PlayMode
{
    /// <summary>Targeted rebuilt-navigation checks, not a replacement for natural route/R4 playtesting.</summary>
    public sealed class EnvironmentNavigationTests
    {
        private readonly List<string> _rows=new List<string>();
        private AsyncOperation LoadValley()
        {
#if UNITY_EDITOR
            if (System.Environment.GetCommandLineArgs().Contains("-emberfallOldGeometry"))
            {
                const string path="Assets/_Game/Scenes/Review/NavigationBaseline/10_EmberValley.unity";
                _rows.Add("Geometry=historical exact snapshot; unchanged placement/time/assertions; no rebake");
                return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path,new LoadSceneParameters(LoadSceneMode.Single));
            }
#endif
            _rows.Add("Geometry=current production");
            return SceneManager.LoadSceneAsync("10_EmberValley");
        }
        [UnityTearDown] public IEnumerator SaveEvidence()
        {
            string dir="Builds/ArtReview/0.9.6-environment-production/runtime";
            Directory.CreateDirectory(dir);
            File.WriteAllLines(dir+"/"+TestContext.CurrentContext.Test.Name+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+".txt",_rows);
            _rows.Clear();
            yield return null;
        }
        private static Vector3 Point(Vector3 p)
        {
            Assert.That(NavMesh.SamplePosition(p,out var hit,.8f,NavMesh.AllAreas),Is.True,"No navigation at "+p);
            return hit.position;
        }
        private void Path(Vector3 start,Vector3 goal,string label)
        {
            var path=new NavMeshPath();
            bool found=NavMesh.CalculatePath(Point(start),Point(goal),NavMesh.AllAreas,path);
            _rows.Add(label+" from="+start+" goal="+goal+" status="+path.status+" corners="+string.Join(";",path.corners.Select(c=>c.ToString("F3"))));
            Assert.That(found && path.status==NavMeshPathStatus.PathComplete,Is.True,label);
        }
        [UnityTest] public IEnumerator Valley_SpawnPathsAndOpenPortals_AreTraversable()
        {
            M2LaunchIntent.RequestNewGame();
            yield return SceneManager.LoadSceneAsync("10_EmberValley"); yield return null; yield return null;
            Assert.That(GameObject.Find("[Art] M6 Environment"),Is.Not.Null);
            var destinations=new Dictionary<string,Vector3> {
                {"forest-encounter",new Vector3(0,0,32)}, {"bridge-encounter",new Vector3(13,0,44)},
                {"courtyard-encounter",new Vector3(24,0,43)}, {"pre-sanctum-encounter",new Vector3(39,0,30)},
                {"ash-approach-encounter",new Vector3(0,0,13.3f)},
                {"ash-guard-pass-encounter",new Vector3(0,0,21.75f)},
                {"ash-return-encounter",new Vector3(23,0,10.2f)} };
            var observedSegments=new HashSet<string>();
            foreach(var leash in Object.FindObjectsOfType<EncounterLeash>(true))
                if(leash.Encounter!=null)
                {
                    string segment=leash.Encounter.TelemetrySegment;
                    Assert.That(destinations.ContainsKey(segment),Is.True,"Unregistered authored navigation segment: "+segment);
                    observedSegments.Add(segment);
                    Path(leash.transform.position,destinations[segment],leash.name);
                }
            // Include the dormant return members: inactivity is not permission to
            // omit their authored spawn paths. Unknown OR missing groups stay red.
            Assert.That(observedSegments,Is.EquivalentTo(destinations.Keys),"Every authored encounter must receive a real path check.");
            Path(new Vector3(0,0,32),new Vector3(5.4f,0,42.3f),"Forest exit seal");
            Path(new Vector3(0,0,36),new Vector3(-12.5f,0,37.5f),"Existing watchtower branch");
            foreach(var actor in Object.FindObjectsOfType<MonoBehaviour>())
                if(actor is MeleeEnemyActor || actor is RangedEnemyActor || actor is ShieldEnemyActor || actor is CombatEncounterCoordinator)actor.enabled=false;
            foreach(var agent in Object.FindObjectsOfType<NavMeshAgent>())agent.enabled=false;
            foreach(var pair in new[]{(new Vector3(16,0,43),new Vector3(22,0,43)),(new Vector3(34,0,38.5f),new Vector3(36.5f,0,34.5f))})
            {
                var go=new GameObject("Runtime navigation traversal probe"); go.transform.position=Point(pair.Item1);
                var agent=go.AddComponent<NavMeshAgent>(); agent.radius=.4f; agent.height=1.8f; agent.speed=3.5f; agent.acceleration=20; agent.stoppingDistance=.1f;
                try
                {
                    var target=Point(pair.Item2); Assert.That(agent.SetDestination(target),Is.True);
                    float start=Time.time; Vector3 previous=go.transform.position; float traveled=0;
                    while(Time.time-start<10 && Vector3.Distance(go.transform.position,target)>.3f)
                    { yield return null; traveled+=Vector3.Distance(previous,go.transform.position); previous=go.transform.position; }
                    _rows.Add("Natural NavMeshAgent portal traversal start="+pair.Item1+" goal="+target+" final="+go.transform.position+" traveled="+traveled+" elapsed="+(Time.time-start));
                    Assert.That(Vector3.Distance(go.transform.position,target),Is.LessThan(.3f));
                    Assert.That(traveled,Is.GreaterThan(3),"Must actually travel, not warp to goal.");
                }
                finally { Object.Destroy(go); }
                yield return null;
            }
        }
        [UnityTest] public IEnumerator Courtyard_RealActorsEngagePlayer_AfterRebake()
        {
            M2LaunchIntent.RequestNewGame();
            yield return LoadValley(); yield return null; yield return null;
            var player=Object.FindObjectOfType<PlayerCombatActor>();
            var controller=player.GetComponent<CharacterController>(); controller.enabled=false;
            // Player root is at capsule centre, unlike enemy/NavMesh feet. Never embed the fixture in paving.
            player.transform.position=Point(new Vector3(28,0,39))+Vector3.up*(controller.height*.5f-controller.center.y+.05f); controller.enabled=true;
            player.GetComponent<ThirdPersonMotor>().ResetAfterTeleport();
            var melee=Object.FindObjectsOfType<MeleeEnemyActor>().Single(x=>x.name=="Enemy_Fogwalker_Courtyard_Support");
            var ranged=Object.FindObjectsOfType<RangedEnemyActor>().Single(x=>x.name=="Enemy_RunePriest_Courtyard");
            var elite=Object.FindObjectsOfType<ShieldEnemyActor>().Single(x=>x.name=="Enemy_RuinGuard_Courtyard");
            var start=melee.transform.position; bool sawMelee=false,sawRanged=false,sawElite=false;
            float until=Time.time+12, next=Time.time;
            while(Time.time<until && !(sawMelee && sawRanged && sawElite))
            {
                sawMelee|=melee.IsThreatening; sawRanged|=ranged.IsThreatening; sawElite|=elite.IsThreatening;
                if(Time.time>=next) {
                    var agent=melee.GetComponent<NavMeshAgent>();
                    var planarTarget=new Vector3(player.transform.position.x,agent.nextPosition.y,player.transform.position.z);
                    bool raw=NavMesh.SamplePosition(player.transform.position,out _,.8f,NavMesh.AllAreas);
                    bool planar=NavMesh.SamplePosition(planarTarget,out var planarHit,.8f,NavMesh.AllAreas);
                    var diagnosticPath=new NavMeshPath();
                    bool planarPath=planar && agent.CalculatePath(planarHit.position,diagnosticPath) && diagnosticPath.status==NavMeshPathStatus.PathComplete;
                    _rows.Add($"t={Time.time:F3} melee={melee.State} position={melee.transform.position:F3} ranged={ranged.State} elite={elite.State} player={player.transform.position:F3} available={player.IsAvailable} agentOn={agent.isOnNavMesh} stopped={agent.isStopped} path={agent.pathStatus} rawTargetSample={raw} planarSample={planar} planarPath={planarPath}"); next+=.2f;
                }
                yield return null;
            }
            _rows.Add($"Observed natural attack phases melee={sawMelee} ranged={sawRanged} elite={sawElite}; melee displacement={Vector3.Distance(start,melee.transform.position):F3}; fixture repositions PLAYER once, no enemy warp/manual tick/damage injection.");
            Assert.That(sawMelee && sawRanged && sawElite,Is.True,"All three courtyard members must naturally commit an attack.");
            Assert.That(Vector3.Distance(start,melee.transform.position),Is.GreaterThan(1));
        }
        [UnityTest] public IEnumerator Gym_SpawnPaths_AreTraversable()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym"); yield return null; yield return null;
            Assert.That(GameObject.Find("[Art] M6 Environment"),Is.Not.Null);
            foreach(var agent in Object.FindObjectsOfType<NavMeshAgent>())Path(agent.transform.position,new Vector3(0,0,0),agent.name);
        }
        [UnityTest] public IEnumerator Forest_RealMeleeApproachesAndAttacks()
        {
            M2LaunchIntent.RequestNewGame();
            yield return LoadValley(); yield return null; yield return null;
            var player=Object.FindObjectOfType<PlayerCombatActor>();
            var controller=player.GetComponent<CharacterController>(); controller.enabled=false;
            player.transform.position=Point(new Vector3(0,0,30))+Vector3.up*(controller.height*.5f-controller.center.y+.05f);
            controller.enabled=true; player.GetComponent<ThirdPersonMotor>().ResetAfterTeleport();
            var enemy=Object.FindObjectsOfType<MeleeEnemyActor>().Single(x=>x.name=="Enemy_Fogwalker_Forest");
            Vector3 start=enemy.transform.position; float until=Time.time+12, next=Time.time;
            bool attacked=false;
            while(Time.time<until && !attacked)
            {
                attacked=enemy.IsThreatening;
                if(Time.time>=next) {
                    _rows.Add($"t={Time.time:F3} enemy={enemy.State} position={enemy.transform.position:F3} player={player.transform.position:F3} distance={Vector3.ProjectOnPlane(enemy.transform.position-player.transform.position,Vector3.up).magnitude:F3} granted={enemy.IsGroupAttackAllowed}"); next+=.2f;
                }
                yield return null;
            }
            _rows.Add($"Natural forest attack={attacked}; displacement={Vector3.Distance(start,enemy.transform.position):F3}");
            Assert.That(attacked,Is.True,"Forest melee must naturally approach and commit an attack within the same 12s budget.");
            Assert.That(Vector3.Distance(start,enemy.transform.position),Is.GreaterThan(1));
        }
    }
}
