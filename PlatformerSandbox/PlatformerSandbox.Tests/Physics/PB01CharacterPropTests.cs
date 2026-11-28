using System.Diagnostics;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Simulation.Physics;

namespace PlatformerSandbox.Tests.Physics;

public sealed class PB01CharacterPropTests
{
    private static Aabb Box(double l,double b,double r,double t)=>new(new(l,b),new(r,t));
    private static WorldSession Create(Aabb? player=null,double x=2,double y=.5,double mass=1,double vx=0,
        BodyCollisionLayer mask=BodyCollisionLayer.Terrain|BodyCollisionLayer.Dynamic|BodyCollisionLayer.Character,bool bounded=false)
    {
        var floor=new StaticAabbCollider(1,Box(-100,-1,100,0));
        var motion=HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10,.2,.1,.35));
        var jump=JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4,.4,.3)));
        var terrain=bounded?new[]{floor,new StaticAabbCollider(2,Box(12,0,13,10))}:new[]{floor};
        var world=new WorldSession(terrain,Array.Empty<MovingPlatformTrajectory>(),motion,motion,jump,-20);
        world.AddPlayer(1,player??Box(0,0,.8,1.6));
        var catalog=new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",0,0,0,1,0)});
        world.InstallDynamicBodies(catalog,new[]{new WorldDynamicBodyFixtures.Fixture {Id=1,Material="Default",X=x,Y=y,Mass=mass,VelocityX=vx,CollisionMask=mask}});
        return world;
    }
    private static void NoOverlap(WorldSession w)=>Assert.False(AabbCollisionDetector.Overlaps(w.GetPlayer(1).Bounds,w.DynamicBodies!.Get(1).Bounds));
    private static WorldPlayerCommand[] Right=>[new(1,HorizontalDirection.Right)];
    private static WorldPlayerCommand[] Neutral=>[new(1,HorizontalDirection.Neutral)];
    [Theory]
    [InlineData(SpatialQueryMode.Reference)] [InlineData(SpatialQueryMode.Indexed)] [InlineData(SpatialQueryMode.Verify)]
    public void CharacterBlocksAndPushesWithoutBecomingADynamicBody(SpatialQueryMode mode)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        var w=Create();w.SetCharacterPushForce(0);
        for(int i=0;i<60;i++){w.Step(Right);NoOverlap(w);}
        Assert.Equal(2d,w.DynamicBodies!.Get(1).Bounds.Center.X,8);
        Assert.Equal(w.DynamicBodies.Get(1).Bounds.Left,w.GetPlayer(1).Bounds.Right,7);
        w.SetCharacterPushForce(120);
        for(int i=0;i<30;i++){w.Step(Right);NoOverlap(w);}
        Assert.True(w.DynamicBodies.Get(1).Bounds.Center.X>2);
        Assert.True(w.DynamicBodies.CharacterPushes>0);
        Assert.Equal(MotionAuthority.CharacterController,WorldPhysicalBodyView.Capture(w).Bodies.Single(b=>b.Id.Kind==PhysicalBodyKind.KinematicCharacter).Authority);
    }
    [Fact]
    public void CharacterCanStandOnSettledCrateAndJump()
    {
        var w=Create(Box(1.6,1,2.4,2.6));
        for(int i=0;i<30;i++){w.Step(Neutral);NoOverlap(w);}
        Assert.True(w.GetPlayer(1).IsGrounded);Assert.Equal(1d,w.GetPlayer(1).Bounds.Bottom,8);
        w.Step([new(1,HorizontalDirection.Neutral,true)]);
        Assert.True(w.GetPlayer(1).JumpStartedThisTick);Assert.True(w.GetPlayer(1).Bounds.Bottom>1);
    }
    [Theory]
    [InlineData(SpatialQueryMode.Reference)] [InlineData(SpatialQueryMode.Indexed)] [InlineData(SpatialQueryMode.Verify)]
    public void CharacterOnCrateRidesVerticalPlatformWithoutFalseCrushOrLanding(SpatialQueryMode mode)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        var motion=HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10,.2,.1,.35));
        var jump=JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4,.4,.3)));
        var path=new MovingPlatformTrajectory(8,Box(0,0,3,1),new(0,2),1);
        var w=new WorldSession(Array.Empty<StaticAabbCollider>(),new[]{path},motion,motion,jump,-20);
        var catalog=new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",1,1,0,1,0)});
        w.InstallDynamicBodies(catalog,new[]{new WorldDynamicBodyFixtures.Fixture{Id=1,Material="Default",X=1,Y=1.5,Mass=1,CollisionMask=BodyCollisionLayer.All}});
        w.AddPlayer(1,Box(.6,2,1.4,3.6));
        // A crate may briefly leave a reversing elevator under inertia; the
        // rider must still follow the actual crate, not the elevator waypoint.
        for(int i=0;i<240;i++)
        {
            var result=w.Step(Neutral);var player=w.GetPlayer(1);var body=w.DynamicBodies!.Get(1);
            Assert.True(player.IsGrounded);Assert.Equal(body.Bounds.Top,player.Bounds.Bottom,8);
            NoOverlap(w);Assert.False(w.IsPropObstructed(8));
            Assert.DoesNotContain(result.Events,e=>e.Kind==WorldGameplayEventKind.Landed||e.Kind==WorldGameplayEventKind.LeftGround);
        }
        Assert.Equal(0L,w.PropPlatformStops);
        var jumpResult=w.Step([new(1,HorizontalDirection.Neutral,true)]);
        Assert.True(w.GetPlayer(1).JumpStartedThisTick);
        Assert.Contains(jumpResult.Events,e=>e.Kind==WorldGameplayEventKind.JumpStarted);
        NoOverlap(w);
    }
    [Theory]
    [InlineData(SpatialQueryMode.Reference,false)] [InlineData(SpatialQueryMode.Indexed,false)] [InlineData(SpatialQueryMode.Verify,false)]
    [InlineData(SpatialQueryMode.Reference,true)] [InlineData(SpatialQueryMode.Indexed,true)] [InlineData(SpatialQueryMode.Verify,true)]
    public void LandingOnDescendingCrateEstablishesContinuousSupport(SpatialQueryMode mode,bool jumpFromCrate)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        var motion=HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10,.2,.1,.35));
        var jump=JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4,.4,.3)));
        var path=new MovingPlatformTrajectory(8,Box(0,0,3,1),new(0,6),3);
        // Start halfway through the first second of the downward leg. The
        // crate is already co-moving with the platform, not waiting at a turn.
        var w=new WorldSession(Array.Empty<StaticAabbCollider>(),new[]{path},motion,motion,jump,-20,
            initialWorldTick:210);
        var sample=w.MovingPlatforms.Single();
        Assert.True(sample.Velocity.Y<0);
        w.InstallDynamicBodies(new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",1,1,0,1,0)}),
            new[]{new WorldDynamicBodyFixtures.Fixture{Id=1,Material="Default",X=1,Y=sample.Bounds.Top+.5,
                VelocityY=sample.Velocity.Y,Mass=1,CollisionMask=BodyCollisionLayer.All}});
        double bottom=w.DynamicBodies!.Get(1).Bounds.Top+(jumpFromCrate?0:.2);
        w.AddPlayer(1,Box(.6,bottom,1.4,bottom+1.6));
        if(jumpFromCrate)
        {
            var launch=w.Step([new(1,HorizontalDirection.Neutral,true)]);
            Assert.Contains(launch.Events,e=>e.Kind==WorldGameplayEventKind.JumpStarted);
            Assert.False(w.GetPlayer(1).IsGrounded);
        }
        else Assert.False(w.GetPlayer(1).IsGrounded);
        bool landed=false;
        int groundedTicks=0;
        int landingCount=0;
        for(int i=0;i<120;i++)
        {
            var result=w.Step(Neutral);
            var player=w.GetPlayer(1);
            var body=w.DynamicBodies!.Get(1);
            NoOverlap(w);
            Assert.True(w.MovingPlatforms.Single().Velocity.Y<0);
            Assert.False(w.IsPropObstructed(8));
            var landings=result.Events.Where(e=>e.Kind==WorldGameplayEventKind.Landed).ToArray();
            landingCount+=landings.Length;
            if(!landed&&player.IsGrounded)
            {
                Assert.Single(landings);
                landed=true;
            }
            if(landed)
            {
                Assert.True(player.IsGrounded);
                Assert.Equal<int?>(int.MaxValue-1,player.MovingSupportId);
                Assert.Equal(body.Bounds.Top,player.Bounds.Bottom,8);
                Assert.Equal(body.Velocity.Y,player.Velocity.Y,8);
                Assert.DoesNotContain(result.Events,e=>e.Kind==WorldGameplayEventKind.LeftGround);
                groundedTicks++;
            }
        }
        Assert.True(landed);
        Assert.Equal(1,landingCount);
        Assert.True(groundedTicks>=30);
        Assert.Equal(0L,w.PropPlatformStops);
    }

    [Theory]
    [InlineData(SpatialQueryMode.Reference)] [InlineData(SpatialQueryMode.Indexed)] [InlineData(SpatialQueryMode.Verify)]
    public void AirborneCharacterCanEnterVacatedCrateSpaceBeforeLanding(SpatialQueryMode mode)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        var motion=HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10,.2,.1,.35));
        var jump=JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4,.4,.3)));
        var path=new MovingPlatformTrajectory(8,Box(0,0,3,1),new(0,6),3);
        var w=new WorldSession(Array.Empty<StaticAabbCollider>(),new[]{path},motion,motion,jump,-20,
            initialWorldTick:210);
        var sample=w.MovingPlatforms.Single();
        w.InstallDynamicBodies(new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",1,1,0,1,0)}),
            new[]{new WorldDynamicBodyFixtures.Fixture{Id=1,Material="Default",X=1,Y=sample.Bounds.Top+.5,
                VelocityY=sample.Velocity.Y,Mass=1,CollisionMask=BodyCollisionLayer.All}});
        var oldCrate=w.DynamicBodies!.Get(1).Bounds;
        // During the first 1/60 s the player falls ~.00278 m, while the
        // crate descends ~.03333 m. The old crate box overlaps the final
        // player box even though the actual bodies remain separated.
        double bottom=oldCrate.Top+.001;
        w.AddPlayer(1,Box(.6,bottom,1.4,bottom+1.6));
        Assert.False(w.GetPlayer(1).IsGrounded);
        var result=w.Step(Neutral);
        var player=w.GetPlayer(1);
        Assert.True(AabbCollisionDetector.Overlaps(player.Bounds,oldCrate));
        NoOverlap(w);
        Assert.True(player.Bounds.Bottom>w.DynamicBodies.Get(1).Bounds.Top);
        Assert.False(player.IsGrounded);
        Assert.Null(player.MovingSupportId);
        Assert.DoesNotContain(result.Events,e=>e.Kind==WorldGameplayEventKind.Landed);
        Assert.False(w.IsPropObstructed(8));
        Assert.Equal(211L,w.WorldTick);
    }

    [Fact]
    public void GenuineInitialCharacterBoundaryPenetrationStillRejectsPropPass()
    {
        var w=Create();
        var dynamics=w.DynamicBodies!;
        var before=dynamics.Get(1);
        var boundary=new PhysicalBodySnapshot(new(PhysicalBodyKind.KinematicCharacter,1),before.Bounds,Vector2D.Zero);
        var failure=Assert.Throws<InvalidOperationException>(()=>dynamics.Advance(1d/60,-20,new[]{boundary},null));
        Assert.Contains("Initial coupled body penetration.",failure.Message);
        Assert.Contains("dynamic 1",failure.Message);
        Assert.Contains("character boundary",failure.Message);
        Assert.Equal(before,dynamics.Get(1));
    }

    [Fact]
    public void DirectPlatformRiderBesideCrateRemainsGrounded()
    {
        var motion=HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10,.2,.1,.35));
        var jump=JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4,.4,.3)));
        var path=new MovingPlatformTrajectory(8,Box(0,0,4,1),new(0,2),1);
        var w=new WorldSession(Array.Empty<StaticAabbCollider>(),new[]{path},motion,motion,jump,-20);
        w.InstallDynamicBodies(new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",1,1,0,1,0)}),
            new[]{new WorldDynamicBodyFixtures.Fixture{Id=1,Material="Default",X=3,Y=1.5,Mass=1,CollisionMask=BodyCollisionLayer.All}});
        w.AddPlayer(1,Box(.5,1,1.3,2.6));
        for(int i=0;i<120;i++)
        {
            var result=w.Step(Neutral);Assert.True(w.GetPlayer(1).IsGrounded);
            Assert.Equal(w.MovingPlatforms.Single().Bounds.Top,w.GetPlayer(1).Bounds.Bottom,8);
            Assert.DoesNotContain(result.Events,e=>e.Kind==WorldGameplayEventKind.Landed||e.Kind==WorldGameplayEventKind.LeftGround);
            NoOverlap(w);
        }
    }
    [Fact]
    public void HorizontalWallAndCrateStillStopAgainstCharacterSide()
    {
        var motion=HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10,.2,.1,.35));
        var jump=JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4,.4,.3)));
        var path=new MovingPlatformTrajectory(8,Box(0,0,1,2),new(2,0),1);
        var w=new WorldSession(new[]{new StaticAabbCollider(1,Box(-5,-1,10,0))},new[]{path},motion,motion,jump,-20);
        w.InstallDynamicBodies(new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",1,1,0,1,0)}),
            new[]{new WorldDynamicBodyFixtures.Fixture{Id=1,Material="Default",X=1.5,Y=.5,Mass=1,CollisionMask=BodyCollisionLayer.All}});
        w.AddPlayer(1,Box(2,0,2.8,1.6));
        w.Step(Neutral);Assert.True(w.IsPropObstructed(8));
        Assert.Equal(1L,w.PropPlatformStops);Assert.Equal(1.5,w.DynamicBodies!.Get(1).Bounds.Center.X,8);
        NoOverlap(w);
    }
    [Theory]
    [InlineData(SpatialQueryMode.Reference, false)] [InlineData(SpatialQueryMode.Indexed, false)] [InlineData(SpatialQueryMode.Verify, false)]
    [InlineData(SpatialQueryMode.Reference, true)] [InlineData(SpatialQueryMode.Indexed, true)] [InlineData(SpatialQueryMode.Verify, true)]
    public void PlayerBetweenWallAndStopPropDoesNotCrashAndWallRetreats(SpatialQueryMode mode, bool mirrored)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        Aabb Reflect(Aabb b)=>mirrored?Box(-b.Right,b.Bottom,-b.Left,b.Top):b;
        int sign=mirrored?-1:1;
        var motion=HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10,.2,.1,.35));
        var jump=JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4,.4,.3)));
        var path=new MovingPlatformTrajectory(8001,Reflect(Box(0,0,1,2)),new(2*sign,0),1);
        var w=new WorldSession(new[]{new StaticAabbCollider(1,Box(-10,-1,10,0))},new[]{path},motion,motion,jump,-20,initialWorldTick:16200);
        w.ConfigureCrushLifecycle(WorldCrushPolicy.Death,new WorldRespawnSettings{Mode=WorldRespawnMode.None});
        w.InstallDynamicBodies(new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",1,1,0,1,0)}),
            new[]{new WorldDynamicBodyFixtures.Fixture{Id=1,Material="Default",X=2.5*sign,Y=.5,Mass=1,CollisionMask=BodyCollisionLayer.All}});
        w.AddPlayer(1,Reflect(Box(1.2,0,2,1.6)));
        bool stopped=false, retreated=false;
        for(int i=0;i<180;i++)
        {
            long previousTick=w.WorldTick;
            var result=w.Step(Neutral);
            Assert.Equal(previousTick+1,w.WorldTick);
            var player=w.GetPlayer(1);var wall=w.MovingPlatforms.Single();
            Assert.False(player.IsDead);
            NoOverlap(w);
            Assert.False(AabbCollisionDetector.Overlaps(player.Bounds,wall.Bounds));
            Assert.False(AabbCollisionDetector.Overlaps(w.DynamicBodies!.Get(1).Bounds,wall.Bounds));
            Assert.DoesNotContain(result.Events,e=>e.Kind==WorldGameplayEventKind.PlayerCrushed||e.Kind==WorldGameplayEventKind.PlayerDied);
            stopped|=w.IsPropObstructed(8001);
            retreated|=stopped&&wall.Velocity.X*sign<0;
        }
        Assert.True(stopped);Assert.True(retreated);
        Assert.True(w.PropPlatformStops>0);Assert.True(w.PropPlatformRecoveryAttempts>0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PersistentOppositeSideIsClassifiedAtActualImpact(bool mirrored)
    {
        Aabb Reflect(Aabb b)=>mirrored?Box(-b.Right,b.Bottom,-b.Left,b.Top):b;
        int sign=mirrored?-1:1;
        var wall=new MovingPlatformTrajectory(8001,Reflect(Box(0,0,1,2)),new(2*sign,0),1);
        var crate=new StaticAabbCollider(91,Reflect(Box(1.9,0,2.9,1)));
        // At wall impact t=.05 the rising player still overlaps the crate;
        // by the end of the proposed interval the player would have escaped.
        // Endpoint-only crush prediction cannot detect this contact.
        var failure=Assert.Throws<CharacterCrushException>(()=>UnifiedAcceleratedKinematicStepper.Step(
            Reflect(Box(1.1,.5,1.9,2.1)),new(0,2),Vector2D.Zero,0,.4,new[]{crate},new[]{wall}));
        Assert.Equal(8001,failure.Crush.PushingColliderId);
        Assert.Equal(91,failure.Crush.OpposingColliderId);
        Assert.Equal(CharacterCrushAxis.Horizontal,failure.Crush.Axis);
        Assert.Equal(.05,failure.Crush.WorldTimeSeconds,8);
    }

    [Fact]
    public void GenuineInitialMovingPlatformPenetrationStillFailsStrictValidation()
    {
        var wall=new MovingPlatformTrajectory(8001,Box(0,0,1,2),new(2,0),1);
        var failure=Assert.Throws<InvalidOperationException>(()=>UnifiedAcceleratedKinematicStepper.Step(
            Box(.9,0,1.7,1.6),Vector2D.Zero,Vector2D.Zero,0,.01,
            Array.Empty<StaticAabbCollider>(),new[]{wall}));
        Assert.Contains("begins penetrating moving platform 8001",failure.Message);
    }

    [Fact]
    public void RemovingSupportDoesNotLeaveCharacterFloating()
    {
        var w=Create(Box(1.6,1,2.4,2.6));w.Step(Neutral);w.DynamicBodies!.Remove(1);
        for(int i=0;i<15;i++)w.Step(Neutral);
        Assert.True(w.GetPlayer(1).Bounds.Bottom<1);
    }
    [Fact]
    public void InboundCrateStopsAtKinematicCharacter()
    {
        var w=Create(x:3,vx:-100);
        for(int i=0;i<10;i++){w.Step(Neutral);NoOverlap(w);}
        Assert.Equal(.8,w.DynamicBodies!.Get(1).Bounds.Left,7);Assert.Equal(0d,w.DynamicBodies.Get(1).Velocity.X,7);
        Assert.True(w.DynamicBodies.CharacterBoundaryImpacts>0);
    }
    [Fact]
    public void CollisionMaskEditRejectsRestorationInsideCharacter()
    {
        var w=Create(x:.4,mask:BodyCollisionLayer.Terrain|BodyCollisionLayer.Dynamic);
        var d=w.DynamicBodies!;var prior=d.Get(1);
        Assert.Throws<ArgumentException>(()=>d.Configure(1,"Default",1,prior.Settings with {Filter=BodyCollisionFilter.DynamicCharacterSolid}));
        Assert.Equal(prior,d.Get(1));
    }
    [Fact]
    public void DisabledCharacterMaskPreservesPassThrough()
    {
        var w=Create(mask:BodyCollisionLayer.Terrain|BodyCollisionLayer.Dynamic);
        for(int i=0;i<60;i++)w.Step(Right);
        Assert.True(w.GetPlayer(1).Bounds.Left>w.DynamicBodies!.Get(1).Bounds.Right);
        Assert.Equal(0L,w.DynamicBodies.CharacterPushes);
    }
    [Fact]
    public void EquivalentWorkloadsAreDeterministicAndEmitBenchmarkEvidence()
    {
        var a=Create(bounded:true);var b=Create(bounded:true);var mode=SpatialQueryPolicy.Current;
        int ticks=Environment.GetEnvironmentVariable("PS_PB01_TIER")=="full"?3600:600;
        long start=Stopwatch.GetTimestamp();
        for(int i=0;i<ticks;i++)
        {
            var cmd=i%120<60?Right:Neutral;
            using(SpatialQueryPolicy.Use(SpatialQueryMode.Reference))a.Step(cmd);
            using(SpatialQueryPolicy.Use(mode))b.Step(cmd);
            NoOverlap(a);NoOverlap(b);Assert.Equal(a.GetPlayer(1),b.GetPlayer(1));
            Assert.Equal(a.DynamicBodies!.Get(1),b.DynamicBodies!.Get(1));
        }
        Assert.True(b.DynamicBodies!.CharacterPushes>0);
        string? dir=Environment.GetEnvironmentVariable("PS_PB01_REPORT_DIR");
        if(string.IsNullOrEmpty(dir))return;
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir,"PB01_character_prop_ticks.csv"),new[]{"mode,ticks,push_requests,boundary_impacts,elapsed_ms,dynamic_cpu_ms",
            FormattableString.Invariant($"{mode},{ticks},{b.DynamicBodies.CharacterPushes},{b.DynamicBodies.CharacterBoundaryImpacts},{Stopwatch.GetElapsedTime(start).TotalMilliseconds:F6},{b.DynamicBodies.TotalStepMilliseconds:F6}")});
    }
}
