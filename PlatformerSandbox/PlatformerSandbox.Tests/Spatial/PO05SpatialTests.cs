using System.Diagnostics;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Spatial;

public sealed class PO05SpatialTests
{
    private static Aabb Box(double l,double b,double r,double t)=>new(new Vector2D(l,b),new Vector2D(r,t));
    private static int[] Ids(List<SpatialEntry<int>> entries)=>entries.Select(e=>e.Id).ToArray();
    private static string Tier=>Environment.GetEnvironmentVariable("PS_PO05_TIER")??"quick";

    [Fact]
    public void MembershipBoundariesAndLargeObjects()
    {
        var index=new SpatialIndex<int>(4,16); var list=new List<SpatialEntry<int>>(); var scratch=new HashSet<int>();
        index.Add(new(8,new(-8,-4,0,4),8)); index.Add(new(2,new(-4,-4,4,4),2));
        index.Add(new(1,new(-10000,-10000,10000,10000),1));
        Assert.Equal(1,index.OversizedCount);
        index.Point(new(-4,0),list,scratch,SpatialQueryMode.Verify); Assert.Equal(new[]{1,2,8},Ids(list));
        index.Update(new(8,new(20,20,21,21),8));
        index.Point(new(-4,0),list,scratch,SpatialQueryMode.Verify); Assert.Equal(new[]{1,2},Ids(list));
        Assert.True(index.Remove(2)); Assert.False(index.Remove(2));
        Assert.Throws<ArgumentException>(()=>index.Add(new(8,new(1,1,2,2),8)));
        Assert.True(index.Query(new(-100000,-100000,100000,100000),list,scratch).LinearFallback);
        Assert.Equal(new[]{1,8},Ids(list)); index.Clear(); Assert.Equal(0,index.Count); Assert.Equal(0,index.OccupiedCells); Assert.Equal(0,index.OversizedCount);
        Assert.Throws<ArgumentException>(()=>new SpatialBounds(double.NaN,0,1,1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new SpatialIndex<int>(0));
    }

    [Fact]
    public void RadiusNearestRayAndCategoryFiltering()
    {
        var index=new SpatialIndex<int>(); var list=new List<SpatialEntry<int>>(); var scratch=new HashSet<int>();
        index.Add(new(3,new(1,0,2,1),3,1,true)); index.Add(new(2,new(-2,0,-1,1),2,1,true));
        index.Add(new(1,new(2,2,3,3),1,2,false));
        index.Radius(new(0,0),2,list,scratch,SpatialQueryMode.Verify); Assert.Equal(new[]{2,3},Ids(list));
        Assert.Equal(2,index.Nearest(new(0,0),2,list,scratch)!.Value.Id);
        index.RayCandidates(new(-4,0),new(4,0),list,scratch,SpatialQueryMode.Verify,new SpatialFilter(1,false,true,2));
        Assert.Equal(new[]{3},Ids(list));
    }

    [Fact]
    public void RandomizedDifferentialWithUpdates()
    {
        var (seeds,queries)=Tier switch {"full"=>(500,1000),"standard"=>(100,500),_=>(10,100)};
        for(int seed=0;seed<seeds;seed++)
        {
            var rng=new Random(seed); var index=new SpatialIndex<int>(1+seed%8); var truth=new Dictionary<int,SpatialEntry<int>>();
            var list=new List<SpatialEntry<int>>(); var scratch=new HashSet<int>();
            SpatialBounds Bounds(){double x=rng.Next(-200,200),y=rng.Next(-200,200);return new(x,y,x+rng.Next(0,20),y+rng.Next(0,20));}
            for(int id=0;id<250;id++) {var e=new SpatialEntry<int>(id,Bounds(),id); index.Add(e);truth.Add(id,e);}
            for(int q=0;q<queries;q++)
            {
                int id=rng.Next(250); var e=new SpatialEntry<int>(id,Bounds(),id);
                if(q%7==0) {index.Remove(id);truth.Remove(id);}
                else if(truth.ContainsKey(id)) {index.Update(e);truth[id]=e;}
                else {index.Add(e);truth[id]=e;}
                var area=Bounds().Expand(15); index.Query(area,list,scratch,SpatialQueryMode.Indexed);
                var expected=truth.Values.Where(x=>x.Bounds.Intersects(area)).Select(x=>x.Id).OrderBy(x=>x);
                Assert.True(expected.SequenceEqual(Ids(list)), $"seed={seed}; operation={q}; cell={index.CellSize}; query={area}");
            }
        }
    }

    [Theory]
    [InlineData(SpatialQueryMode.Indexed)]
    [InlineData(SpatialQueryMode.Verify)]
    public void SweepsIncludeThinWallsAndInteriorTurningPoints(SpatialQueryMode mode)
    {
        var wall=new SpatialColliderSet([new StaticAabbCollider(7,Box(10,-1,10.1,2)),new StaticAabbCollider(8,Box(100,100,101,101))]);
        var ceiling=new SpatialColliderSet([new StaticAabbCollider(2,Box(-1,2.1,2,3)),new StaticAabbCollider(9,Box(100,100,101,101))]);
        var moving=Box(0,0,0.5,0.5);
        var before=SpatialCollisionDiagnostics.Snapshot.Queries;
        using var scope=SpatialQueryPolicy.Use(mode);
        Assert.Equal(7,AabbCollisionDetector.SweepFirst(moving,new(20,0),wall)!.Value.ColliderId);
        Assert.Equal(2,ParabolicAabbCollisionDetector.SweepFirst(moving,new(0,8),new(0,-16),1,ceiling)!.Value.ColliderId);
        Assert.True(SpatialCollisionDiagnostics.Snapshot.Queries>before);
    }

    [Theory]
    [InlineData(SpatialQueryMode.Indexed)]
    [InlineData(SpatialQueryMode.Verify)]
    public void AuthoredWorldReplayAndTriggerLifecycle(SpatialQueryMode mode)
    {
        var a=AuthoredWorldGym.Create(worldInstanceId:55); var b=AuthoredWorldGym.Create(worldInstanceId:55);
        for(int i=0;i<75;i++)
        {
            WorldPlayerCommand[] commands=[new(1,HorizontalDirection.Left,JumpPressed:i==0),new(2,HorizontalDirection.Right,JumpPressed:i==0)];
            Compare(a,b,commands,mode,i);
        }
        a=AuthoredWorldGym.Create(worldInstanceId:55); b=AuthoredWorldGym.Create(worldInstanceId:55);
        foreach(var world in new[]{a,b}) {world.SetPlayerSpawn(1,AuthoredWorldGym.GroundSpawn);world.RespawnPlayer(1);}
        for(int i=0;i<106;i++) Compare(a,b,i<11?[]:[new(1,HorizontalDirection.Right)],mode,i);
    }
    private static void Compare(WorldSession a,WorldSession b,WorldPlayerCommand[] commands,SpatialQueryMode mode,int tick)
    {
        WorldStepResult x,y;
        using(SpatialQueryPolicy.Use(SpatialQueryMode.Reference)) x=a.Step(commands);
        using(SpatialQueryPolicy.Use(mode)) y=b.Step(commands);
        Assert.True(x.Players.SequenceEqual(y.Players),$"Player state divergence at tick {tick}, mode {mode}");
        Assert.True(x.Events.SequenceEqual(y.Events),$"Event divergence at tick {tick}, mode {mode}");
        Assert.Equal(a.MovingPlatforms.ToArray(),b.MovingPlatforms.ToArray());
    }

    [Fact]
    public void CommittedSnapshotsDoNotRetainRemovedPlayersOrChangeOldView()
    {
        var world=AuthoredWorldGym.Create(); var bounds=new SpatialBounds(-1000,-1000,1000,1000);
        AuthoredWorldGym.JoinGuest(world); var old=world.CaptureSpatialSnapshot(); world.RemovePlayer(3);
        var current=world.CaptureSpatialSnapshot(); ulong players=1UL<<(int)WorldSpatialKind.Player;
        Assert.Contains(old.Query(bounds,players,SpatialQueryMode.Verify),e=>e.Id==3);
        Assert.DoesNotContain(current.Query(bounds,players,SpatialQueryMode.Verify),e=>e.Id==3);
    }

    [Fact]
    public void ScalabilityLabImportsAndReplays()
    {
        string? directory=AppContext.BaseDirectory;
        while(directory is not null&&!File.Exists(Path.Combine(directory,"Content","Levels","EngineScalabilityLab.ldtk"))) directory=Directory.GetParent(directory)?.FullName;
        Assert.NotNull(directory);
        var catalog=LdtkContentCatalog.Discover(Path.Combine(directory!,"Content","Levels"));
        Assert.True(catalog.Supports(new SceneDestination(GameLevelIds.EngineScalabilityLab)),string.Join("; ",catalog.RejectedProjects.Select(p=>$"{p.Key}: {p.Value}")));
        var level=catalog.Prepare(new SceneDestination(GameLevelIds.EngineScalabilityLab)).Imported;
        Assert.Equal(6144,level.WidthPx); Assert.Equal(2048,level.HeightPx);
        Assert.True(level.ColliderCount>100); Assert.Equal(2,level.World.MovingPlatformTrajectories.Count);
        var a=LdtkLevelGym.Create(level,MovementGymSession.PlayableAssistancePreset,SimulationFaultSensitivity.Strict,1,2026);
        var b=LdtkLevelGym.Create(level,MovementGymSession.PlayableAssistancePreset,SimulationFaultSensitivity.Strict,1,2026);
        int ticks=Tier=="full"?3600:Tier=="standard"?1800:600;
        for(int i=0;i<ticks;i++)
        {
            int phase=i%240;
            Compare(a,b,[new(1,phase<120?HorizontalDirection.Right:HorizontalDirection.Left,JumpPressed:i%90==20,JumpReleased:i%90==45)],SpatialQueryMode.Indexed,i);
        }
    }

    [Fact]
    public void SparseWorldScalabilityAndExecutionEvidence()
    {
        var rows=new List<string>{"objects,mode,queries,examined,matches,elapsed_ms,allocated_bytes"};
        foreach(int count in (Tier=="full"?new[]{100,1000,10000,50000}:Tier=="standard"?new[]{100,1000,10000}:new[]{100,1000}))
        {
            var index=new SpatialIndex<int>(); var list=new List<SpatialEntry<int>>(); var scratch=new HashSet<int>();
            for(int i=0;i<count;i++){double x=(i%100)*10,y=(i/100)*10;index.Add(new(i,new(x,y,x+1,y+1),i));}
            long expected=-1;
            foreach(var mode in new[]{SpatialQueryMode.Reference,SpatialQueryMode.Indexed})
            {
                long examined=0,matches=0; var timer=Stopwatch.StartNew();long allocated=GC.GetAllocatedBytesForCurrentThread();
                for(int q=0;q<200;q++){int i=q%count;double x=(i%100)*10,y=(i/100)*10;var stats=index.Query(new(x-2,y-2,x+2,y+2),list,scratch,mode);examined+=stats.Examined;matches+=stats.Matches;}
                long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;timer.Stop();
                if(mode==SpatialQueryMode.Reference) expected=matches; else {Assert.Equal(expected,matches);Assert.True(examined<200L*count);}
                rows.Add(FormattableString.Invariant($"{count},{mode},200,{examined},{matches},{timer.Elapsed.TotalMilliseconds:F6},{bytes}"));
            }
        }
        string? path=Environment.GetEnvironmentVariable("PS_PO05_REPORT_DIR");
        if(!string.IsNullOrEmpty(path)){Directory.CreateDirectory(path);File.WriteAllLines(Path.Combine(path,"scalability.csv"),rows);}
    }
}
