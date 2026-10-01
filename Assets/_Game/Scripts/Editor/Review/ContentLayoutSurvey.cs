using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.AI.Unity;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Review
{
    /// <summary>Reads the saved production layout; creates evidence only, never geometry or a bake.</summary>
    public static class ContentLayoutSurvey
    {
        const string Source = "Assets/_Game/Scenes/10_EmberValley.unity";
        const string InputEvidence = "Builds/RouteReview/0.9.6-r4-input/20260930-100442-874/result.json";

        public static string Run(bool fitNavigationEdges = false)
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || scene.path != Source || scene.isDirty || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("One saved Valley scene in idle Edit Mode required.");
            var surface = UnityEngine.Object.FindObjectOfType<NavMeshSurface>();
            if (surface == null || surface.navMeshData == null) throw new InvalidOperationException("Missing production bake.");
            string navAsset = AssetDatabase.GetAssetPath(surface.navMeshData);
            if (string.IsNullOrEmpty(navAsset)) throw new InvalidOperationException("Persistent production bake required.");
            string sceneHash = Hash(Source), navHash = Hash(navAsset);
            var nav = NavMesh.CalculateTriangulation();
            var existing = UnityEngine.Object.FindObjectsOfType<CombatEncounterCoordinator>()
                .OrderBy(x => x.TelemetrySegment).Select(x => new Rectangle
                { id = x.TelemetrySegment, center = x.ArenaCenter, half = x.ArenaHalfExtents,
                    margin = x.TelemetryActivationMargin }).ToArray();
            var candidates = fitNavigationEdges ? new[]
            {
                new Rectangle { id="candidate:entry-a", center=new Vector3(0,0,14.2f), half=new Vector2(3.6f,3.7f), margin=.15f },
                new Rectangle { id="candidate:entry-b", center=new Vector3(0,0,21.5f), half=new Vector2(3.6f,3.2f), margin=.15f },
                new Rectangle { id="candidate:return", center=new Vector3(23,0,10.2f), half=new Vector2(6.5f,2.3f), margin=.3f }
            } : new[]
            {
                new Rectangle { id="candidate:entry-a", center=new Vector3(0,0,14.2f), half=new Vector2(4.1f,3.4f), margin=.3f },
                new Rectangle { id="candidate:entry-b", center=new Vector3(0,0,23.5f), half=new Vector2(4.1f,3), margin=.3f },
                new Rectangle { id="candidate:return", center=new Vector3(23,0,10), half=new Vector2(6.5f,2.5f), margin=.3f }
            };
            var route = JsonUtility.FromJson<Route>(File.ReadAllText(InputEvidence));
            if (!route.pilotPassed || route.deaths != 0 || route.dynamicMoveSamples <= 0)
                throw new InvalidOperationException("Input evidence did not complete its own scope.");
            var measurements = new List<Measurement>();
            foreach (var r in candidates)
            {
                var m = new Measurement { rectangle=r, area=4*r.half.x*r.half.y };
                for (int i=0; i<nav.indices.Length; i+=3)
                {
                    var polygon = new List<Vector3> { nav.vertices[nav.indices[i]], nav.vertices[nav.indices[i+1]], nav.vertices[nav.indices[i+2]] };
                    // Reject rooftop layers before clipping: the authored corridor floors are at y≈0.
                    if (polygon.Any(p => Math.Abs(p.y-r.center.y)>.5f)) continue;
                    polygon=Clip(polygon,0,r.center.x-r.half.x,true);
                    polygon=Clip(polygon,0,r.center.x+r.half.x,false);
                    polygon=Clip(polygon,2,r.center.z-r.half.y,true);
                    polygon=Clip(polygon,2,r.center.z+r.half.y,false);
                    for(int k=1;k+1<polygon.Count;k++)
                        m.groundNavArea += Vector3.Cross(polygon[k]-polygon[0],polygon[k+1]-polygon[0]).magnitude*.5f;
                }
                var misses = new List<Vector3>();
                for(int x=0;x<=8;x++) for(int z=0;z<=8;z++)
                {
                    var p=r.center+new Vector3(Mathf.Lerp(-r.half.x,r.half.x,x/8f),0,Mathf.Lerp(-r.half.y,r.half.y,z/8f));
                    NavMeshHit hit;
                    if(NavMesh.SamplePosition(p,out hit,.2f,NavMesh.AllAreas) &&
                        new Vector2(hit.position.x-p.x,hit.position.z-p.z).magnitude<.05f && Math.Abs(hit.position.y-p.y)<.2f)
                        m.exactGridHits++;
                    else misses.Add(p);
                    var hits=Physics.RaycastAll(p+Vector3.up*2,Vector3.down,2.5f,~0,QueryTriggerInteraction.Ignore);
                    if(hits.Any(h => h.normal.y>.9f && Math.Abs(h.point.y-p.y)<.2f)) m.physicalFloorHits++;
                }
                m.gridMisses=misses.ToArray();
                m.inputSamplesInside=route.samples.Count(s => Inside(r,s.position));
                m.inputFirstSeconds=route.samples.Where(s => Inside(r,s.position)).Select(s=>s.seconds).DefaultIfEmpty(-1).Min();
                m.inputLastSeconds=route.samples.Where(s => Inside(r,s.position)).Select(s=>s.seconds).DefaultIfEmpty(-1).Max();
                bool entry=r.id.StartsWith("candidate:entry",StringComparison.Ordinal);
                Vector3 from=r.center+(entry?Vector3.back:Vector3.right)*(entry?r.half.y+.7f:r.half.x+.7f);
                Vector3 to=r.center+(entry?Vector3.forward:Vector3.left)*(entry?r.half.y+.7f:r.half.x+.7f);
                NavMeshHit start,end; var path=new NavMeshPath();
                m.localPathComplete=NavMesh.SamplePosition(from,out start,.3f,NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(to,out end,.3f,NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) && path.status==NavMeshPathStatus.PathComplete;
                m.pathCorners=path.corners;
                measurements.Add(m);
            }
            var all=existing.Concat(candidates).ToArray(); var pairs=new List<Separation>();
            for(int i=0;i<all.Length;i++) for(int j=i+1;j<all.Length;j++)
            {
                float gapX=Math.Abs(all[i].center.x-all[j].center.x)-all[i].half.x-all[j].half.x-all[i].margin-all[j].margin;
                float gapZ=Math.Abs(all[i].center.z-all[j].center.z)-all[i].half.y-all[j].half.y-all[i].margin-all[j].margin;
                pairs.Add(new Separation { first=all[i].id,second=all[j].id,gapX=gapX,gapZ=gapZ,disjoint=gapX>0 || gapZ>0 });
            }
            var result=new Result { source=Source, navAsset=navAsset, sceneHash=sceneHash, navHash=navHash,
                inputEvidence=InputEvidence, navigationEdgeFit=fitNavigationEdges, existing=existing, candidates=measurements.ToArray(), pairs=pairs.ToArray(),
                allEffectiveRectanglesDisjoint=pairs.All(p=>p.disjoint),
                sourceFilesUnchanged=sceneHash==Hash(Source) && navHash==Hash(navAsset) && !scene.isDirty };
            if(!result.sourceFilesUnchanged) throw new InvalidOperationException("Survey changed production assets.");
            string folder="Builds/LayoutReview/0.8.11/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(folder);
            File.WriteAllText(folder+"/survey.json",JsonUtility.ToJson(result,true));
            return folder;
        }

        static bool Inside(Rectangle r,Vector3 p) => Math.Abs(p.x-r.center.x)<=r.half.x && Math.Abs(p.z-r.center.z)<=r.half.y;
        static string Hash(string path) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-",""); }
        static List<Vector3> Clip(List<Vector3> input,int axis,float limit,bool minimum)
        {
            var output=new List<Vector3>(); if(input.Count==0)return output;
            var previous=input[input.Count-1]; bool priorInside=minimum?previous[axis]>=limit:previous[axis]<=limit;
            foreach(var current in input)
            {
                bool inside=minimum?current[axis]>=limit:current[axis]<=limit;
                if(inside!=priorInside)output.Add(Vector3.LerpUnclamped(previous,current,(limit-previous[axis])/(current[axis]-previous[axis])));
                if(inside)output.Add(current);
                previous=current;priorInside=inside;
            }
            return output;
        }
        [Serializable] sealed class Rectangle { public string id;public Vector3 center;public Vector2 half;public float margin; }
        [Serializable] sealed class Measurement
        {
            public Rectangle rectangle;public float area,groundNavArea;
            public int exactGridHits,physicalFloorHits,inputSamplesInside;public Vector3[] gridMisses,pathCorners;
            public double inputFirstSeconds,inputLastSeconds;public bool localPathComplete;
        }
        [Serializable] sealed class Separation { public string first,second;public float gapX,gapZ;public bool disjoint; }
        [Serializable] sealed class Result
        {
            public string scope="Read-only Editor navigation/physics survey plus prior AI-on normal-input trajectory. Candidate rectangles are proposals, not authored encounters or universal mandatory-route proof.";
            public string source,navAsset,sceneHash,navHash,inputEvidence;
            public Rectangle[] existing;public Measurement[] candidates;public Separation[] pairs;
            public bool navigationEdgeFit,allEffectiveRectanglesDisjoint,sourceFilesUnchanged;
        }
        [Serializable] sealed class Route { public bool pilotPassed;public int deaths,dynamicMoveSamples;public RouteSample[] samples; }
        [Serializable] sealed class RouteSample { public double seconds;public Vector3 position; }
    }
}
