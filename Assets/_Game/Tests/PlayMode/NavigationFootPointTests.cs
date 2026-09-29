using System.Collections;
using System.Collections.Generic;
using System.IO;
using Emberfall.AI.Unity;
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
    public sealed class NavigationFootPointTests
    {
        [UnityTest]
        public IEnumerator CapsuleFeet_PreservePlanarDestinationBoundsAndTargetFloor()
        {
            yield return SceneManager.LoadSceneAsync("90_CombatGym"); yield return null;
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            player.enabled = false; player.GetComponent<ThirdPersonMotor>().enabled = false;
            var controller = player.GetComponent<CharacterController>(); controller.enabled = false;
            controller.height = 2; controller.center = Vector3.zero;
            var sources = new List<NavMeshBuildSource> {
                Box(new Vector3(500,-.1f,0),Quaternion.identity,new Vector3(20,.2f,20)),
                Box(new Vector3(500,3.9f,0),Quaternion.identity,new Vector3(20,.2f,20)),
                Box(new Vector3(530,0,0),Quaternion.Euler(0,0,10),new Vector3(16,.2f,16)) };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),sources,
                new Bounds(new Vector3(515,2,0),new Vector3(60,16,30)),Vector3.zero,Quaternion.identity);
            Assert.That(data,Is.Not.Null);
            var instance = NavMesh.AddNavMeshData(data);
            var go = new GameObject("Foot-point isolated navigation agent");
            var boundsObject = new GameObject("Foot-point isolated arena");
            try
            {
                Vector3 start = Sample(new Vector3(500,0,-3)); go.transform.position = start;
                var agent = go.AddComponent<NavMeshAgent>(); agent.radius=.4f; agent.height=1.8f;
                var leash = go.AddComponent<EncounterLeash>();
                var arena = boundsObject.AddComponent<CombatEncounterCoordinator>(); arena.enabled=false;
                arena.ConfigureTelemetryArena(new Vector3(500,0,0),new Vector2(8,8),0); leash.Configure(arena);
                yield return null;
                Vector3 flat = Sample(new Vector3(500,0,2));
                player.transform.position = flat+Vector3.up;
                Assert.That(Vector3.Distance(player.NavigationFootPosition,flat),Is.LessThan(.001f));
                Assert.That(leash.SetDestination(player.NavigationFootPosition),Is.True);
                var firstCorners = agent.path.corners;
                // Alternative capsule centre/root convention, exact same feet and planar target.
                controller.center = new Vector3(0,.4f,0); player.transform.position -= Vector3.up*.4f;
                Assert.That(Vector3.Distance(player.NavigationFootPosition,flat),Is.LessThan(.001f));
                agent.ResetPath();
                Assert.That(leash.SetDestination(player.NavigationFootPosition),Is.True);
                Assert.That(agent.path.corners.Length,Is.EqualTo(firstCorners.Length));
                for(int i=0;i<firstCorners.Length;i++) Assert.That(Vector3.Distance(agent.path.corners[i],firstCorners[i]),Is.LessThan(.001f));
                // Same XZ, genuine upper platform. No projection to the attacker's lower floor.
                Vector3 upper = Sample(new Vector3(500,4,2));
                player.transform.position = upper+Vector3.up*.6f;
                Assert.That(player.NavigationFootPosition.y,Is.GreaterThan(3.5f));
                Assert.That(Sample(player.NavigationFootPosition).y,Is.GreaterThan(3.5f));
                Assert.That(leash.SetDestination(player.NavigationFootPosition),Is.False,"Disconnected upper target must not snap to lower floor.");
                Assert.That(agent.hasPath,Is.False);
                // Slope uses the target's own sampled foot height, not a fixed arena/agent height.
                Vector3 slope = Sample(new Vector3(533,.6f,0)); player.transform.position=slope+Vector3.up*.6f;
                Assert.That(Vector3.Distance(player.NavigationFootPosition,slope),Is.LessThan(.001f));
                Assert.That(slope.y,Is.GreaterThan(.3f));
                // Existing XZ clamp and path-corner policy stay in force.
                Vector3 outside = new Vector3(509,flat.y,2); player.transform.position=outside+Vector3.up*.6f;
                Assert.That(player.NavigationFootPosition.x,Is.EqualTo(509));
                Assert.That(leash.SetDestination(player.NavigationFootPosition),Is.True);
                foreach(var corner in agent.path.corners) Assert.That(leash.Contains(corner),Is.True);
                Assert.That(agent.pathEndPosition.x,Is.LessThanOrEqualTo(507.4f+.01f));
                Directory.CreateDirectory("Builds/ArtReview/0.9.6-navigation-fix");
                File.WriteAllText("Builds/ArtReview/0.9.6-navigation-fix/foot-regression.txt",
                    $"flat={flat:F4}; upper={upper:F4}; slope={slope:F4}; boundedEnd={agent.pathEndPosition:F4}\n"+
                    "Equivalent feet/capsule conventions; disconnected upper layer rejected; slope height retained; XZ and corners bounded. Synthetic regression, not natural AI evidence.");
            }
            finally { instance.Remove(); Object.Destroy(go); Object.Destroy(boundsObject); Object.Destroy(data); }
        }

        private static Vector3 Sample(Vector3 point)
        {
            Assert.That(NavMesh.SamplePosition(point,out var hit,.8f,NavMesh.AllAreas),Is.True,"Missing synthetic navigation at "+point);
            return hit.position;
        }
        private static NavMeshBuildSource Box(Vector3 position,Quaternion rotation,Vector3 size) =>
            new NavMeshBuildSource { shape=NavMeshBuildSourceShape.Box,transform=Matrix4x4.TRS(position,rotation,Vector3.one),size=size,area=0 };
    }
}
