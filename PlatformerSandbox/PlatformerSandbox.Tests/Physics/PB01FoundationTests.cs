using System.Diagnostics;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Physics;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Physics;

public sealed class PB01FoundationTests
{
    private static string Root()
    {
        string? p=AppContext.BaseDirectory;
        while(p is not null && !File.Exists(Path.Combine(p,"Content","Physics","Materials.json")))
            p=Directory.GetParent(p)?.FullName;
        return p ?? throw new DirectoryNotFoundException("PB01 material file missing.");
    }
    private static PhysicsMaterialCatalog Materials() => PhysicsMaterialCatalog.FromJson(File.ReadAllText(Path.Combine(Root(),"Content","Physics","Materials.json")));
    private static PhysicsMaterialCatalog SampleMaterials() => new(new[] {
        new PhysicsMaterial("Default", .5, .4, 0, 1, 0),
        new PhysicsMaterial("Wood", .5, .35, .1, .6, 0),
        new PhysicsMaterial("Ice", .05, .03, .05, .9, 0) });
    private static Aabb Bounds => new(new Vector2D(0,0),new Vector2D(1,1));

    [Fact]
    public void BodyIdentitySeparatesMotionAuthorityAndLegacyIdNamespaces()
    {
        var ids=new[]{new PhysicalBodyId(PhysicalBodyKind.Static,7),new PhysicalBodyId(PhysicalBodyKind.KinematicPlatform,7),new PhysicalBodyId(PhysicalBodyKind.KinematicCharacter,7),new PhysicalBodyId(PhysicalBodyKind.Dynamic,7)};
        Assert.Equal(4,ids.Distinct().Count());
        Assert.Equal(new[]{MotionAuthority.Fixed,MotionAuthority.AuthoredTrajectory,MotionAuthority.CharacterController,MotionAuthority.DynamicIntegrator},ids.Select(i=>i.Authority).ToArray());
        Assert.False(default(PhysicalBodyId).IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new PhysicalBodyId(PhysicalBodyKind.Dynamic,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new PhysicalBodyId((PhysicalBodyKind)99,7));
        Assert.Throws<ArgumentException>(()=>new PhysicalBodySnapshot(ids[0],Bounds,new Vector2D(1,0)));
    }

    [Fact]
    public void BodyFramesAreOrderedDetachedAndRejectDuplicates()
    {
        var a=new PhysicalBodySnapshot(new(PhysicalBodyKind.Static,4),Bounds,Vector2D.Zero);
        var b=new PhysicalBodySnapshot(new(PhysicalBodyKind.KinematicCharacter,1),Bounds,new Vector2D(3,0));
        var list=new List<PhysicalBodySnapshot>{b,a};
        var frame=new PhysicalBodyFrame(1,0,list);
        list.Clear();
        Assert.Equal(new[]{a,b},frame.Bodies.ToArray());
        Assert.Throws<ArgumentException>(()=>new PhysicalBodyFrame(1,0,new[]{a,a}));
        Assert.Throws<ArgumentException>(()=>new PhysicalBodyFrame(1,0,new[]{default(PhysicalBodySnapshot)}));
    }

    [Fact]
    public void OnlyDynamicBodiesResolveMassAndMaterialsDoNotOwnCharacterMotion()
    {
        var catalog=SampleMaterials();
        var crate=new PhysicalBodyDefinition(new(PhysicalBodyKind.Dynamic,1),new(1,2),"Wood");
        Assert.Equal(4.8,crate.ResolveMass(catalog)!.Value,10);
        var heavy=new PhysicalBodyDefinition(new(PhysicalBodyKind.Dynamic,2),new(1,2),"Wood",20);
        Assert.Equal(20d,heavy.ResolveMass(catalog)!.Value);
        var player=new PhysicalBodyDefinition(new(PhysicalBodyKind.KinematicCharacter,1),new(.4,.8),"Ice");
        Assert.Null(player.ResolveMass(catalog)); Assert.Equal(MotionAuthority.CharacterController,player.Authority);
        Assert.Throws<ArgumentOutOfRangeException>(()=>new PhysicalBodyDefinition(player.Id,new(.4,.8),"Ice",20));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new PhysicalBodyDefinition(crate.Id,new(0,1),"Wood"));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new PhysicalBodyDefinition(crate.Id,new(1,1),"Wood",double.NaN));
    }

    [Theory]
    [InlineData(MaterialCombineRule.Minimum,.03)]
    [InlineData(MaterialCombineRule.Maximum,.35)]
    [InlineData(MaterialCombineRule.Average,.19)]
    [InlineData(MaterialCombineRule.Multiply,.0105)]
    [InlineData(MaterialCombineRule.GeometricMean,.10246950765959598)]
    public void MaterialRulesAreSymmetricAndIndependent(MaterialCombineRule rule,double expected)
    {
        var catalog=SampleMaterials();
        var a=catalog.ResolveContact("Wood","Ice",rule,MaterialCombineRule.Maximum);
        var b=catalog.ResolveContact("Ice","Wood",rule,MaterialCombineRule.Maximum);
        Assert.Equal(a,b); Assert.Equal(expected,a.DynamicFriction,10); Assert.Equal(.1,a.Restitution,10);
        Assert.True(a.StaticFriction>=a.DynamicFriction);
    }

    [Fact]
    public void MaterialsAreEditableValidatedAndDoNotSilentlyFallback()
    {
        var catalog=Materials(); Assert.True(catalog.Materials.ContainsKey("Default"));
        Assert.True(Enum.IsDefined(catalog.FrictionCombine));
        Assert.True(Enum.IsDefined(catalog.RestitutionCombine));
        Assert.Equal(MaterialCombineRule.GeometricMean,SampleMaterials().FrictionCombine);
        Assert.Equal(MaterialCombineRule.Maximum,SampleMaterials().RestitutionCombine);
        Assert.Throws<ArgumentException>(()=>catalog.Require("Unknown"));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new PhysicsMaterial("Bad",.1,.2,0,1,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new PhysicsMaterial("Bad",.5,.4,1.2,1,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new PhysicsMaterial("Bad",.5,.4,0,0,0));
        Assert.Throws<ArgumentException>(()=>new PhysicsMaterialCatalog(new[]{SampleMaterials().Require("Wood")}));
        Assert.Throws<ArgumentException>(()=>new PhysicsMaterialCatalog(new[]{catalog.Require("Default"),catalog.Require("Default")}));
        Assert.Throws<System.Text.Json.JsonException>(()=>PhysicsMaterialCatalog.FromJson("{\"Version\":1}"));
        const string invalid="{\"Version\":999,\"FrictionCombine\":\"Minimum\",\"RestitutionCombine\":\"Maximum\",\"Materials\":[]}";
        Assert.Throws<System.Text.Json.JsonException>(()=>PhysicsMaterialCatalog.FromJson(invalid));
    }

    private static LdtkPreparedContent Prepare(string level)
    {
        var catalog=LdtkContentCatalog.Discover(Path.Combine(Root(),"Content","Levels"));
        Assert.True(catalog.Supports(new SceneDestination(level)),string.Join("; ",catalog.RejectedProjects.Select(p=>$"{p.Key}: {p.Value}")));
        return catalog.Prepare(new SceneDestination(level));
    }
    private static WorldSession Create(LdtkPreparedContent content) => LdtkLevelGym.Create(content.Imported,MovementGymSession.PlayableAssistancePreset,SimulationFaultSensitivity.Strict,1,2027);

    [Fact]
    public void BothDedicatedAndIntegrationLabsAreRealDiscoverableLevels()
    {
        var feature=Prepare(GameLevelIds.PhysicsInteractionLab);
        var integrated=Prepare(GameLevelIds.EngineIntegrationLab);
        Assert.Equal(3,feature.Imported.World.MovingPlatformTrajectories.Count);
        Assert.Equal(3072,feature.Imported.WidthPx);
        Assert.NotEmpty(integrated.Imported.World.MovingPlatformTrajectories);
        Assert.NotEmpty(integrated.Imported.World.Triggers);
        Assert.NotEmpty(integrated.Connected.WindRegions);
        Assert.Empty(integrated.Connected.Exits);
        Assert.NotNull(integrated.ParticleZones);
        Assert.NotEmpty(integrated.ParticleZones!.Zones);
        Assert.NotNull(integrated.Lighting);
        Assert.NotEmpty(integrated.Lighting!.Lights);
        Assert.True(integrated.Scenery.SpriteCount>0);
        Assert.True(integrated.Terrain.ArtworkCount>0);
    }

    [Theory]
    [InlineData(GameLevelIds.PhysicsInteractionLab)]
    [InlineData(GameLevelIds.EngineIntegrationLab)]
    public void RuntimePlatformPlaybackIsWorldOwnedAndDeterministic(string level)
    {
        var content=Prepare(level);var a=Create(content);var b=Create(content);var independent=Create(content);
        var fixtures=WorldDynamicBodyFixtures.FromJson(File.ReadAllText(Path.Combine(Root(),"Content","Physics","DynamicBodies.json")));
        Assert.True(fixtures.TryInstall(a,level,Materials()));Assert.True(fixtures.TryInstall(b,level,Materials()));
        a.ConfigureCrushLifecycle(WorldCrushPolicy.Death,new());b.ConfigureCrushLifecycle(WorldCrushPolicy.Death,new());
        int id=a.MovingPlatforms[0].PlatformId;
        WorldPlayerCommand[] neutral=[new(1,HorizontalDirection.Neutral)];
        for(int i=0;i<180;i++)
        {
            if(i==100){a.RequestPlayerDeath(1);b.RequestPlayerDeath(1);}
            if(i%60==0){a.DynamicBodies!.ApplyImpulse(1,new(0,2));b.DynamicBodies!.ApplyImpulse(1,new(0,2));}
            if(i==30||i==90||i==120)
            {
                var command=i==30?PlatformPlaybackCommand.Pause:i==90?PlatformPlaybackCommand.Resume:PlatformPlaybackCommand.Reverse;
                var before=a.MovingPlatforms.Single(p=>p.PlatformId==id).Bounds;
                a.ControlPlatform(id,command);b.ControlPlatform(id,command);
                Assert.Equal(before,a.MovingPlatforms.Single(p=>p.PlatformId==id).Bounds);
                if(i==30) Assert.Equal(Vector2D.Zero,a.MovingPlatforms.Single(p=>p.PlatformId==id).Velocity);
            }
            WorldStepResult x,y;
            using(SpatialQueryPolicy.Use(SpatialQueryMode.Reference)) x=a.Step(neutral);
            y=b.Step(neutral);independent.Step(neutral);
            Assert.Equal(x.Players.ToArray(),y.Players.ToArray());Assert.Equal(x.Events.ToArray(),y.Events.ToArray());
            Assert.Equal(a.MovingPlatforms.ToArray(),b.MovingPlatforms.ToArray());
            Assert.Equal(a.DynamicBodies!.Bodies.Select(d=>d.Snapshot).ToArray(),b.DynamicBodies!.Bodies.Select(d=>d.Snapshot).ToArray());
            if(i==100){Assert.Contains(x.Events,e=>e.Kind==WorldGameplayEventKind.PlayerDied);Assert.Contains(x.Events,e=>e.Kind==WorldGameplayEventKind.PlayerRespawned);}
            var authored=content.Imported.World.MovingPlatformTrajectories.Single(p=>p.Id==id);
            Assert.Equal(authored.Evaluate(independent.WorldTimeSeconds),independent.MovingPlatforms.Single(p=>p.PlatformId==id));
            if(i>=30&&i<90) Assert.Equal(Vector2D.Zero,a.MovingPlatforms.Single(p=>p.PlatformId==id).Velocity);
        }
    }

    [Fact]
    public void WorldBodyAdapterPreservesIdentityAndOldFramesAfterRemoval()
    {
        var world=Create(Prepare(GameLevelIds.PhysicsInteractionLab));
        var old=WorldPhysicalBodyView.Capture(world);
        var character=Assert.Single(old.Bodies.Where(b=>b.Id.Kind==PhysicalBodyKind.KinematicCharacter));
        Assert.Equal(world.GetPlayerSlot(1).CharacterId,character.Id.LocalId);
        Assert.Equal(world.GetPlayer(1).Bounds,character.Bounds);
        Assert.Equal(world.StaticColliders.Count+world.MovingPlatforms.Count+1,old.Bodies.Count);
        world.RespawnPlayer(1);
        Assert.Contains(WorldPhysicalBodyView.Capture(world).Bodies,b=>b.Id==character.Id);
        world.RemovePlayer(1);
        Assert.DoesNotContain(WorldPhysicalBodyView.Capture(world).Bodies,b=>b.Id==character.Id);
        Assert.Contains(old.Bodies,b=>b.Id==character.Id);
    }

    [Theory]
    [InlineData(GameLevelIds.PhysicsInteractionLab)]
    [InlineData(GameLevelIds.EngineIntegrationLab)]
    public void FixedTickReplayPreservesLegacyCharacterStateAndMeasuresObservation(string level)
    {
        var content=Prepare(level); var a=Create(content); var b=Create(content);
        string tier=Environment.GetEnvironmentVariable("PS_PB01_TIER")??"quick";
        int ticks=tier=="full"?3600:tier=="standard"?1800:600;
        var mode=SpatialQueryPolicy.Current;
        // First prove read-only observation does not change existing state/events and mode parity.
        for(int i=0;i<ticks;i++)
        {
            WorldPlayerCommand[] command=[new(1,i%240<120?HorizontalDirection.Right:HorizontalDirection.Left,i%90==20,i%90==45)];
            WorldStepResult x,y;
            using(SpatialQueryPolicy.Use(SpatialQueryMode.Reference)) x=a.Step(command);
            using(SpatialQueryPolicy.Use(mode)) y=b.Step(command);
            var frame=WorldPhysicalBodyView.Capture(b);
            Assert.Equal(x.Players.ToArray(),y.Players.ToArray()); Assert.Equal(x.Events.ToArray(),y.Events.ToArray());
            Assert.Equal(a.MovingPlatforms.ToArray(),b.MovingPlatforms.ToArray());
            Assert.Equal(y.WorldTick,frame.Tick);
            Assert.Equal(y.Players[0].Bounds,Assert.Single(frame.Bodies.Where(body=>body.Id.Kind==PhysicalBodyKind.KinematicCharacter)).Bounds);
        }
        // Timed workload runs separate from correctness assertions. Same tick count, input, world and mode.
        string? target=Environment.GetEnvironmentVariable("PS_PB01_REPORT_DIR");
        if(string.IsNullOrEmpty(target)) return;
        var rows=new List<string>{"level,mode,workload,ticks,elapsed_ms,allocated_bytes,body_count"};
        foreach(bool observe in new[]{false,true})
        {
            var world=Create(content);
            var neutral=new WorldPlayerCommand[]{new(1,HorizontalDirection.Neutral)};
            // JIT warm-up on an independent world, not measured fixture.
            var warm=Create(content);
            for(int i=0;i<60;i++){warm.Step(neutral);if(observe) _=WorldPhysicalBodyView.Capture(warm);}
            long start=Stopwatch.GetTimestamp(), bytes=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<ticks;i++)
            {
                world.Step([new(1,i%240<120?HorizontalDirection.Right:HorizontalDirection.Left,i%90==20,i%90==45)]);
                if(observe) _=WorldPhysicalBodyView.Capture(world);
            }
            double ms=Stopwatch.GetElapsedTime(start).TotalMilliseconds; long allocated=GC.GetAllocatedBytesForCurrentThread()-bytes;
            rows.Add(FormattableString.Invariant($"{level},{mode},{(observe?"step_and_observe":"step_only")},{ticks},{ms:F6},{allocated},{WorldPhysicalBodyView.Capture(world).Bodies.Count}"));
        }
        Directory.CreateDirectory(target);File.WriteAllLines(Path.Combine(target,level+"_fixed_ticks.csv"),rows);
    }

    [Theory]
    [InlineData(GameLevelIds.PhysicsInteractionLab)]
    [InlineData(GameLevelIds.EngineIntegrationLab)]
    public void CoupledAuthoredLabReplayKeepsCharacterAndPropsSeparated(string level)
    {
        var content=Prepare(level);var a=Create(content);var b=Create(content);
        var fixtures=WorldDynamicBodyFixtures.FromJson(File.ReadAllText(Path.Combine(Root(),"Content","Physics","DynamicBodies.json")));
        Assert.True(fixtures.TryInstall(a,level,Materials()));Assert.True(fixtures.TryInstall(b,level,Materials()));
        a.ConfigureCrushLifecycle(WorldCrushPolicy.Death,new());b.ConfigureCrushLifecycle(WorldCrushPolicy.Death,new());
        var mode=SpatialQueryPolicy.Current;
        for(int i=0;i<240;i++)
        {
            WorldPlayerCommand[] command=[new(1,i%120<60?HorizontalDirection.Right:HorizontalDirection.Left,i%120==45,i%120==55)];
            WorldStepResult x,y;
            using(SpatialQueryPolicy.Use(SpatialQueryMode.Reference)) x=a.Step(command);
            using(SpatialQueryPolicy.Use(mode)) y=b.Step(command);
            Assert.Equal(x.Players.ToArray(),y.Players.ToArray());Assert.Equal(x.Events.ToArray(),y.Events.ToArray());
            Assert.Equal(a.DynamicBodies!.Bodies.ToArray(),b.DynamicBodies!.Bodies.ToArray());
            foreach(var player in y.Players.Where(p=>!p.IsDead))
                foreach(var body in b.DynamicBodies.Bodies.Where(DynamicBodyWorld.CharacterSolid))
                    Assert.False(PlatformerSandbox.Simulation.Collision.Detection.AabbCollisionDetector.Overlaps(player.Bounds,body.Bounds));
        }
        Assert.Equal(a.DynamicBodies!.CharacterPushes,b.DynamicBodies!.CharacterPushes);
    }

    [Theory]
    [InlineData(GameLevelIds.PhysicsInteractionLab)]
    [InlineData(GameLevelIds.EngineIntegrationLab)]
    public void DynamicFixturesAdvanceWithWorldAndPreserveKinematicReplay(string level)
    {
        var content=Prepare(level);var legacy=Create(content);var a=Create(content);var b=Create(content);
        var fixtures=WorldDynamicBodyFixtures.FromJson(File.ReadAllText(Path.Combine(Root(),"Content","Physics","DynamicBodies.json")));
        Assert.True(fixtures.TryInstall(a,level,Materials()));Assert.True(fixtures.TryInstall(b,level,Materials()));
        // The legacy-equivalence contract applies when character/prop and platform/prop collisions are disabled.
        foreach(var w in new[]{a,b})foreach(var body in w.DynamicBodies!.Bodies)
            w.DynamicBodies.Configure(body.Id.LocalId,body.Definition.MaterialId,body.Definition.MassOverride,
                body.Settings with {Filter=new(BodyCollisionLayer.Dynamic,body.Settings.Filter.Mask&~(BodyCollisionLayer.Character|BodyCollisionLayer.Platform))});
        var old=WorldPhysicalBodyView.Capture(a);var original=old.Bodies.Where(x=>x.Id.Kind==PhysicalBodyKind.Dynamic).ToArray();Assert.NotEmpty(original);
        var mode=SpatialQueryPolicy.Current;int ticks=Environment.GetEnvironmentVariable("PS_PB01_TIER")=="full"?3600:600;
        long start=Stopwatch.GetTimestamp();
        for(int i=0;i<ticks;i++)
        {
            WorldPlayerCommand[] command=[new(1,i%240<120?HorizontalDirection.Right:HorizontalDirection.Left,i%90==20,i%90==45)];
            if(i%60==0){a.DynamicBodies!.ApplyImpulse(1,new(0,2));b.DynamicBodies!.ApplyImpulse(1,new(0,2));}
            WorldStepResult x,y,z;
            using(SpatialQueryPolicy.Use(SpatialQueryMode.Reference)){x=legacy.Step(command);y=a.Step(command);}
            using(SpatialQueryPolicy.Use(mode))z=b.Step(command);
            Assert.Equal(x.Players.ToArray(),y.Players.ToArray());Assert.Equal(y.Players.ToArray(),z.Players.ToArray());
            Assert.Equal(x.Events.ToArray(),y.Events.ToArray());Assert.Equal(y.Events.ToArray(),z.Events.ToArray());
            Assert.Equal(a.DynamicBodies!.Bodies.Select(d=>d.Snapshot).ToArray(),b.DynamicBodies!.Bodies.Select(d=>d.Snapshot).ToArray());
        }
        Assert.True(b.DynamicBodies!.Metrics.Contacts>0);Assert.Equal((long)ticks,b.DynamicBodies.Metrics.Steps);
        var final=WorldPhysicalBodyView.Capture(b);Assert.Equal(original.Length,final.Bodies.Count(x=>x.Id.Kind==PhysicalBodyKind.Dynamic));
        Assert.Equal(original,old.Bodies.Where(x=>x.Id.Kind==PhysicalBodyKind.Dynamic).ToArray());
        var firstBody=b.DynamicBodies.Get(1);
        Assert.Contains(b.CaptureSpatialSnapshot().Point(firstBody.Bounds.Center),x=>x.Kind==WorldSpatialKind.DynamicBody&&x.Id==1);
        string? target=Environment.GetEnvironmentVariable("PS_PB01_REPORT_DIR");if(string.IsNullOrEmpty(target))return;
        Directory.CreateDirectory(target);
        File.WriteAllLines(Path.Combine(target,level+"_dynamic_ticks.csv"),new[]{"level,mode,ticks,bodies,sweeps,contacts,dynamic_cpu_ms,total_test_wall_ms",
            FormattableString.Invariant($"{level},{mode},{ticks},{b.DynamicBodies.Bodies.Count},{b.DynamicBodies.Metrics.Sweeps},{b.DynamicBodies.Metrics.Contacts},{b.DynamicBodies.TotalStepMilliseconds:F6},{Stopwatch.GetElapsedTime(start).TotalMilliseconds:F6}")});
    }
}
