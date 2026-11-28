using System.Diagnostics;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Simulation.Physics;

namespace PlatformerSandbox.Tests.Physics;

/// <summary>PB01 acceptance workloads. No simulation fault is swallowed and no
/// performance threshold depends on the developer's particular hardware.</summary>
public sealed class PB01CloseoutTests
{
    private static string Tier => Environment.GetEnvironmentVariable("PS_PB01_TIER") ?? "quick";
    private static int ReplayTicks => Tier == "full" ? 10000 : Tier == "standard" ? 3000 : 600;
    private static string Root()
    {
        string? path = AppContext.BaseDirectory;
        while (path is not null && !File.Exists(Path.Combine(path,"Content","Physics","Materials.json")))
            path = Directory.GetParent(path)?.FullName;
        return path ?? throw new DirectoryNotFoundException("PB01 content root missing.");
    }
    private static PhysicsMaterialCatalog Materials() => PhysicsMaterialCatalog.FromJson(File.ReadAllText(Path.Combine(Root(),"Content","Physics","Materials.json")));
    private static Aabb Box(double l,double b,double r,double t) => new(new(l,b),new(r,t));
    private static WorldSession Create(string level)
    {
        var content = LdtkContentCatalog.Discover(Path.Combine(Root(),"Content","Levels")).Prepare(new SceneDestination(level));
        var world = LdtkLevelGym.Create(content.Imported,MovementGymSession.PlayableAssistancePreset,SimulationFaultSensitivity.Strict,1,2027);
        var fixtures = WorldDynamicBodyFixtures.FromJson(File.ReadAllText(Path.Combine(Root(),"Content","Physics","DynamicBodies.json")));
        Assert.True(fixtures.TryInstall(world,level,Materials()));
        world.ConfigureCrushLifecycle(WorldCrushPolicy.Death,new());
        return world;
    }
    private static void Separated(Aabb a,Aabb b,string context) => Assert.False(AabbCollisionDetector.Overlaps(a,b),context);
    private static void CheckWorld(WorldSession world, WorldStepResult result, string context)
    {
        var d = world.DynamicBodies!;
        var bodies = d.Bodies;
        var platforms = world.MovingPlatforms;
        foreach (var body in bodies)
        {
            Assert.True(body.Bounds.IsValid && double.IsFinite(body.Velocity.X) && double.IsFinite(body.Velocity.Y),context);
            foreach (var terrain in world.StaticColliders)
                if (body.Settings.Filter.Allows(BodyCollisionFilter.Terrain) && !d.IsExcluded(body.Id.LocalId,terrain.Id))
                    Separated(body.Bounds,terrain.Bounds,$"{context}: prop {body.Id.LocalId}, terrain {terrain.Id}");
            if (DynamicBodyWorld.PlatformSolid(body)) foreach (var platform in platforms)
                Separated(body.Bounds,platform.Bounds,$"{context}: prop {body.Id.LocalId}, platform {platform.PlatformId}");
        }
        for (int i=0;i<bodies.Count;i++) for (int j=i+1;j<bodies.Count;j++)
            if (bodies[i].Settings.Filter.Allows(bodies[j].Settings.Filter) && !d.IsBodyPairExcluded(bodies[i].Id.LocalId,bodies[j].Id.LocalId))
                Separated(bodies[i].Bounds,bodies[j].Bounds,$"{context}: props {bodies[i].Id.LocalId}/{bodies[j].Id.LocalId}");
        foreach (var player in result.Players.Where(p=>!p.IsDead))
        {
            Assert.True(player.Bounds.IsValid && double.IsFinite(player.Velocity.X) && double.IsFinite(player.Velocity.Y),context);
            foreach (var body in bodies.Where(DynamicBodyWorld.CharacterSolid))
                Separated(player.Bounds,body.Bounds,$"{context}: player {player.PlayerId}, prop {body.Id.LocalId}");
            foreach (var terrain in world.StaticColliders)
                Separated(player.Bounds,terrain.Bounds,$"{context}: player {player.PlayerId}, terrain {terrain.Id}");
            foreach (var platform in platforms)
                Separated(player.Bounds,platform.Bounds,$"{context}: player {player.PlayerId}, platform {platform.PlatformId}");
        }
    }
    private static WorldPlayerCommand[] Input(int tick) => [new(1,
        tick%240<100?HorizontalDirection.Right:tick%240<200?HorizontalDirection.Left:HorizontalDirection.Neutral,
        tick%90==20,tick%90==45)];
    private static void Script(WorldSession w,int tick)
    {
        if (tick%300==200) w.RequestPlayerDeath(1);
        if (tick%120==15 && w.DynamicBodies!.Bodies.Any(b=>b.Id.LocalId==1)) w.DynamicBodies.ApplyImpulse(1,new(0,2));
        if (tick%360 is 30 or 60 or 120)
        {
            int id=w.MovingPlatformTrajectories[0].Id;
            w.ControlPlatform(id,tick%360==30?PlatformPlaybackCommand.Pause:tick%360==60?PlatformPlaybackCommand.Resume:PlatformPlaybackCommand.Reverse);
        }
    }
    private static void Write(string name,IEnumerable<string> rows)
    {
        string? path=Environment.GetEnvironmentVariable("PS_PB01_REPORT_DIR");
        if(string.IsNullOrEmpty(path))return;
        Directory.CreateDirectory(path);File.WriteAllLines(Path.Combine(path,name),rows);
    }
    private static double P95(double[] samples)
    {
        Array.Sort(samples);return samples[(int)Math.Ceiling(samples.Length*.95)-1];
    }

    [Theory]
    [InlineData(GameLevelIds.PhysicsInteractionLab)]
    [InlineData(GameLevelIds.EngineIntegrationLab)]
    public void AuthoredMixedWorldStressReplay(string level)
    {
        var reference=Create(level);var candidate=Create(level);var mode=SpatialQueryPolicy.Current;
        int ticks=ReplayTicks,peakIsland=0,landings=0,jumps=0,deaths=0,respawns=0;
        long allocated=0;double sum=0;var times=new double[ticks];
        for(int i=0;i<ticks;i++)
        {
            string context=$"{level}/{mode}/tick {i+1}";
            var command=Input(i);WorldStepResult a,b;
            try { using(SpatialQueryPolicy.Use(SpatialQueryMode.Reference)){Script(reference,i);a=reference.Step(command);} }
            catch(Exception ex){throw new InvalidOperationException($"PB01 reference replay failed at {context}",ex);}
            Script(candidate,i);
            long bytes=GC.GetAllocatedBytesForCurrentThread(), start=Stopwatch.GetTimestamp();
            try { b=candidate.Step(command); }
            catch(Exception ex){throw new InvalidOperationException($"PB01 closeout failed at {context}",ex);}
            times[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;sum+=times[i];allocated+=GC.GetAllocatedBytesForCurrentThread()-bytes;
            Assert.Equal((long)i+1,b.WorldTick);Assert.Equal(a.WorldTick,b.WorldTick);
            Assert.Equal(a.Players.ToArray(),b.Players.ToArray());Assert.Equal(a.Events.ToArray(),b.Events.ToArray());
            Assert.Equal(reference.MovingPlatforms.ToArray(),candidate.MovingPlatforms.ToArray());
            Assert.Equal(reference.DynamicBodies!.Bodies.ToArray(),candidate.DynamicBodies!.Bodies.ToArray());
            Assert.Equal(reference.PropPlatformStops,candidate.PropPlatformStops);
            Assert.Equal(reference.PropPlatformRecoveryAttempts,candidate.PropPlatformRecoveryAttempts);
            foreach(var platform in candidate.MovingPlatforms)
            {
                Assert.Equal(reference.IsPropObstructed(platform.PlatformId),candidate.IsPropObstructed(platform.PlatformId));
                Assert.Equal(reference.GetPlatformObstructionRemainingSeconds(platform.PlatformId),candidate.GetPlatformObstructionRemainingSeconds(platform.PlatformId));
            }
            CheckWorld(candidate,b,context);
            peakIsland=Math.Max(peakIsland,candidate.DynamicBodies.LargestContactIsland);
            foreach(var e in b.Events)
            {
                if(e.Kind==WorldGameplayEventKind.Landed)landings++;
                if(e.Kind==WorldGameplayEventKind.JumpStarted)jumps++;
                if(e.Kind==WorldGameplayEventKind.PlayerDied)deaths++;
                if(e.Kind==WorldGameplayEventKind.PlayerRespawned)respawns++;
            }
        }
        var d=candidate.DynamicBodies!;
        Assert.Equal((long)ticks,d.Metrics.Steps);Assert.True(d.Metrics.Contacts>0);Assert.True(d.PlatformContacts>0);
        Assert.True(d.CoupledEventIterations>0);Assert.True(deaths>0&&respawns>0);
        // No equality assertion on timing: correctness is deterministic, performance is measured.
        Write(level+"_closeout_replay.csv",new[]{
            "level,mode,ticks,mean_world_step_ms,p95_world_step_ms,allocated_bytes_per_tick,committed_dynamic_cpu_ms_per_tick,final_bodies,sweeps,contacts,pair_contacts,platform_contacts,platform_stops,recovery_attempts,destroyed,committed_coupled_event_iterations,peak_contact_island,jumps,landings,deaths,respawns",
            FormattableString.Invariant($"{level},{mode},{ticks},{sum/ticks:F6},{P95(times):F6},{(double)allocated/ticks:F3},{d.TotalStepMilliseconds/ticks:F6},{d.Bodies.Count},{d.Metrics.Sweeps},{d.Metrics.Contacts},{d.PairContacts},{d.PlatformContacts},{candidate.PropPlatformStops},{candidate.PropPlatformRecoveryAttempts},{d.DestroyedByPlatform},{d.CoupledEventIterations},{peakIsland},{jumps},{landings},{deaths},{respawns}")});
    }

    [Fact]
    public void IntegratedElevatorActuallyObstructsAndRetreats()
    {
        var world=Create(GameLevelIds.EngineIntegrationLab);
        var neutral=new WorldPlayerCommand[]{new(1,HorizontalDirection.Neutral)};
        bool blocked=false,retreated=false;
        double blockedBottom=0;
        for(int i=0;i<480;i++)
        {
            var result=world.Step(neutral);
            var elevator=world.MovingPlatforms.Single(p=>p.PlatformId==8002);
            if(world.IsPropObstructed(8002)) {blocked=true;blockedBottom=elevator.Bounds.Bottom;}
            else if(blocked && elevator.Bounds.Bottom>blockedBottom && elevator.Velocity.Y>0)retreated=true;
            CheckWorld(world,result,$"integrated elevator tick {i+1}");
        }
        Assert.True(blocked,"The authored lower crate must obstruct the descending elevator.");
        Assert.True(retreated,"The elevator must retreat from the obstruction.");
        Assert.True(world.PropPlatformRecoveryAttempts>0);
        Assert.Contains(world.DynamicBodies!.Bodies,b=>b.Id.LocalId==13);
        Assert.Contains(world.DynamicBodies!.Bodies,b=>b.Id.LocalId==14);
    }

    private static DynamicBodyWorld ScaleWorld(int count)
    {
        var world=new DynamicBodyWorld(new[]{new StaticAabbCollider(1,Box(-4,-1,count*2+4,0))},
            new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",.5,.4,0,1,0)}));
        for(int i=0;i<count;i++)
            world.Add(new(new(PhysicalBodyKind.Dynamic,i+1),new(.5,.5),"Default",1),
                new DynamicBodySettings{Filter=BodyCollisionFilter.DynamicSolid},new(2+3*(i/2),i%2==0?.5:1.75));
        return world;
    }
    [Fact]
    public void CoupledBodyCountScalingBaseline()
    {
        int ticks=Tier=="full"?600:Tier=="standard"?300:120;
        var rows=new List<string>{"mode,body_count,ticks,mean_step_ms,p95_step_ms,allocated_bytes_per_tick,sweeps,contacts,pair_contacts,committed_coupled_event_iterations,peak_contact_island"};
        foreach(int count in new[]{8,32,64})
        {
            // Warm up on a separate world so the measured workload starts identically.
            var warm=ScaleWorld(count);for(int i=0;i<60;i++)warm.Advance(1d/60,-20);
            var world=ScaleWorld(count);var times=new double[ticks];double sum=0;long allocated=0;int peak=0;
            for(int i=0;i<ticks;i++)
            {
                if(i%60==0)for(int id=1;id<=count;id+=2)world.ApplyImpulse(id,new(0,1));
                long bytes=GC.GetAllocatedBytesForCurrentThread(),start=Stopwatch.GetTimestamp();
                world.Advance(1d/60,-20);
                times[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;sum+=times[i];allocated+=GC.GetAllocatedBytesForCurrentThread()-bytes;
                peak=Math.Max(peak,world.LargestContactIsland);
                var bodies=world.Bodies;Assert.Equal(count,bodies.Count);
                for(int a=0;a<count;a++)
                {
                    Assert.True(bodies[a].Bounds.IsValid);Assert.True(bodies[a].Bounds.Bottom>=0);
                    for(int b=a+1;b<count;b++)Separated(bodies[a].Bounds,bodies[b].Bounds,$"scale {count} tick {i}: {a}/{b}");
                }
            }
            Assert.Equal((long)ticks,world.Metrics.Steps);Assert.True(world.PairContacts>0);Assert.True(world.CoupledEventIterations>0);
            rows.Add(FormattableString.Invariant($"{SpatialQueryPolicy.Current},{count},{ticks},{sum/ticks:F6},{P95(times):F6},{(double)allocated/ticks:F3},{world.Metrics.Sweeps},{world.Metrics.Contacts},{world.PairContacts},{world.CoupledEventIterations},{peak}"));
        }
        Write("PB01_closeout_scaling.csv",rows);
    }
}
