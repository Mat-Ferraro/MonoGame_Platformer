using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Simulation.Physics;

namespace PlatformerSandbox.Tests.Physics;

public sealed class PB01PlatformPropTests
{
    private static Aabb Box(double l,double b,double r,double t)=>new(new(l,b),new(r,t));
    private static PhysicsMaterialCatalog Catalog()=>new(new[]{new PhysicsMaterial("Default",1,1,0,1,0)});
    private static DynamicBodySettings Settings(double gravity=0,RigidBodyCrushResponse response=RigidBodyCrushResponse.StopPlatform)=>
        new(){GravityScale=gravity,MaxSpeed=1000,Filter=new(BodyCollisionLayer.Dynamic,BodyCollisionLayer.All),CrushResponse=response};
    private static void Add(DynamicBodyWorld world,int id,double x,double y,double gravity=0,RigidBodyCrushResponse response=RigidBodyCrushResponse.StopPlatform)=>
        world.Add(new(new(PhysicalBodyKind.Dynamic,id),new(.5,.5),"Default",1),Settings(gravity,response),new(x,y));
    private static void Advance(DynamicBodyWorld world,MovingPlatformTrajectory path,double dt,double gravity,double time=0)=>
        world.Advance(dt,gravity,Array.Empty<PhysicalBodySnapshot>(),null,new[]{path},time);

    [Theory]
    [InlineData(SpatialQueryMode.Reference)] [InlineData(SpatialQueryMode.Indexed)] [InlineData(SpatialQueryMode.Verify)]
    public void RelativePlatformSweepPushesWithoutPenetration(SpatialQueryMode mode)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),Catalog());
        var path=new MovingPlatformTrajectory(8,Box(0,0,1,1),new(2,0),1);
        Add(w,1,2,.5);Advance(w,path,.5,0);
        Assert.Equal(2.5,w.Get(1).Bounds.Center.X,10);Assert.Equal(2d,w.Get(1).Velocity.X,10);
        Assert.False(AabbCollisionDetector.Overlaps(w.Get(1).Bounds,path.Evaluate(.5).Bounds));Assert.True(w.PlatformContacts>0);
    }
    [Fact]
    public void HorizontalSupportTransfersFrictionEvenWhenOtherFloorContactWinsTie()
    {
        var floor=new StaticAabbCollider(1,Box(9,-1,11,0));
        var w=new DynamicBodyWorld(new[]{floor},Catalog());
        var path=new MovingPlatformTrajectory(8,Box(0,2,2,3),new(1,0),1);
        Add(w,1,10,.5,1);Add(w,2,1,3.5,1);Advance(w,path,.25,-4);
        Assert.Equal(1.25,w.Get(2).Bounds.Center.X,10);Assert.Equal(3d,w.Get(2).Bounds.Bottom,10);
        Assert.True(w.PlatformContacts>0);
    }
    [Fact]
    public void RisingPlatformLiftsBodyAndStack()
    {
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),Catalog());
        var path=new MovingPlatformTrajectory(8,Box(0,0,2,1),new(0,2),1);
        Add(w,1,1,1.5,1);Add(w,2,1,2.5,1);Advance(w,path,.25,-4);
        Assert.Equal(1.5,w.Get(1).Bounds.Bottom,10);Assert.Equal(2.5,w.Get(2).Bounds.Bottom,10);
        Assert.Equal(2d,w.Get(2).Velocity.Y,10);
        Assert.False(AabbCollisionDetector.Overlaps(w.Get(1).Bounds,w.Get(2).Bounds));
    }
    [Fact]
    public void TurnWithinTickDoesNotContinuePlatformAlongOldVelocity()
    {
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),Catalog());
        var path=new MovingPlatformTrajectory(8,Box(0,0,1,1),new(1,0),.25);
        Add(w,1,2,.5);Advance(w,path,.25,0,.125);
        Assert.Equal(3d,w.Get(1).Bounds.Center.X,10);Assert.Equal(4d,w.Get(1).Velocity.X,10);
        Assert.False(AabbCollisionDetector.Overlaps(w.Get(1).Bounds,path.Evaluate(.375).Bounds));
    }
    [Fact]
    public void StopClassificationDoesNotCommitPartialBodyTick()
    {
        var wall=new StaticAabbCollider(1,Box(3,0,4,2));
        var w=new DynamicBodyWorld(new[]{wall},Catalog());
        var path=new MovingPlatformTrajectory(8,Box(0,0,1,1),new(2,0),1);
        Add(w,1,2.5,.5);var before=w.Get(1);
        var ex=Assert.Throws<PlatformPropCompressionException>(()=>Advance(w,path,.5,0));
        Assert.Equal(new[]{8},ex.PlatformIds.ToArray());Assert.Equal(new[]{1},ex.BodyIds.ToArray());
        Assert.Equal(before,w.Get(1));Assert.Equal(0L,w.Metrics.Steps);Assert.Equal(0L,w.DestroyedByPlatform);
    }
    private static WorldSession CrushWorld(RigidBodyCrushResponse response,bool chain=false)
    {
        var path=new MovingPlatformTrajectory(8,Box(0,0,1,1),new(2,0),1);
        var wall=new StaticAabbCollider(1,Box(chain?4:3,0,chain?5:4,2));
        var motion=HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10,.2,.1,.35));
        var jump=JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4,.4,.3)));
        var world=new WorldSession(new[]{wall},new[]{path},motion,motion,jump,-20,fixedStepSeconds:.25);
        var entries=new List<WorldDynamicBodyFixtures.Fixture>{new(){Id=1,Material="Default",X=2.5,Y=.5,Mass=1,GravityScale=0,CollisionMask=BodyCollisionLayer.All,CrushResponse=response}};
        if(chain)entries.Add(new(){Id=2,Material="Default",X=3.5,Y=.5,Mass=1,GravityScale=0,CollisionMask=BodyCollisionLayer.All,CrushResponse=RigidBodyCrushResponse.StopPlatform});
        world.InstallDynamicBodies(Catalog(),entries);
        return world;
    }
    private static WorldPlayerCommand[] None=>Array.Empty<WorldPlayerCommand>();
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ObstructionPausesBeforePenetrationAndCanReverse(bool mixedChain)
    {
        var world=CrushWorld(mixedChain?RigidBodyCrushResponse.Destroy:RigidBodyCrushResponse.StopPlatform,mixedChain);
        world.Step(None);var before=world.MovingPlatforms.Single().Bounds;world.Step(None);
        Assert.Equal(2L,world.WorldTick);Assert.Equal(before,world.MovingPlatforms.Single().Bounds);
        Assert.True(world.IsPropObstructed(8));Assert.Equal(1L,world.PropPlatformStops);
        Assert.Equal(mixedChain?2:1,world.DynamicBodies!.Bodies.Count);
        Assert.Equal(0L,world.DynamicBodies.DestroyedByPlatform);
        world.ControlPlatform(8,PlatformPlaybackCommand.Reverse);world.Step(None);
        Assert.False(world.IsPropObstructed(8));Assert.True(world.MovingPlatforms.Single().Bounds.Left<before.Left);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BlockedHorizontalAndVerticalPathsReturnAfterTimeout(bool vertical)
    {
        WorldSession world;
        if(!vertical)world=CrushWorld(RigidBodyCrushResponse.StopPlatform);
        else
        {
            var path=new MovingPlatformTrajectory(8,Box(0,2,1,3),new(0,-2),1);
            var floor=new StaticAabbCollider(1,Box(-2,-1,2,0));
            var motion=HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10,.2,.1,.35));
            var jump=JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4,.4,.3)));
            world=new WorldSession(new[]{floor},new[]{path},motion,motion,jump,-20,fixedStepSeconds:.25);
            world.InstallDynamicBodies(Catalog(),new[]{new WorldDynamicBodyFixtures.Fixture{Id=1,Material="Default",X=.5,Y=.5,Mass=1,GravityScale=0,CollisionMask=BodyCollisionLayer.All,CrushResponse=RigidBodyCrushResponse.StopPlatform}});
        }
        world.ConfigurePlatformObstructionRecovery(8,.5);
        world.Step(None);var before=world.MovingPlatforms.Single().Bounds;
        world.Step(None);Assert.True(world.IsPropObstructed(8));
        Assert.Equal(before,world.MovingPlatforms.Single().Bounds);
        world.Step(None);Assert.True(world.IsPropObstructed(8));
        Assert.Equal(before,world.MovingPlatforms.Single().Bounds);
        world.Step(None);Assert.False(world.IsPropObstructed(8));
        var returned=world.MovingPlatforms.Single().Bounds;
        Assert.True(vertical?returned.Bottom>before.Bottom:returned.Left<before.Left);
        Assert.Equal(1L,world.PropPlatformRecoveryAttempts);
        Assert.Single(world.DynamicBodies!.Bodies);
        Assert.False(AabbCollisionDetector.Overlaps(returned,world.DynamicBodies.Get(1).Bounds));
        for(int tick=0;tick<24;tick++)world.Step(None);
        Assert.True(world.PropPlatformRecoveryAttempts>1);
        Assert.Single(world.DynamicBodies.Bodies);
    }
    [Fact]
    public void HoldAndManualPauseAreNotOverriddenByRecovery()
    {
        var world=CrushWorld(RigidBodyCrushResponse.StopPlatform);
        world.ConfigurePlatformObstructionRecovery(8,null);
        world.Step(None);world.Step(None);var blocked=world.MovingPlatforms.Single().Bounds;
        for(int tick=0;tick<8;tick++)world.Step(None);
        Assert.True(world.IsPropObstructed(8));Assert.Equal(blocked,world.MovingPlatforms.Single().Bounds);
        Assert.Equal(0L,world.PropPlatformRecoveryAttempts);
        world.ControlPlatform(8,PlatformPlaybackCommand.Reverse);world.Step(None);
        Assert.False(world.IsPropObstructed(8));
        world.ConfigurePlatformObstructionRecovery(8,.25);
        world.ControlPlatform(8,PlatformPlaybackCommand.Pause);var paused=world.MovingPlatforms.Single().Bounds;
        for(int tick=0;tick<8;tick++)world.Step(None);
        Assert.Equal(paused,world.MovingPlatforms.Single().Bounds);Assert.Equal(0L,world.PropPlatformRecoveryAttempts);
    }
    [Fact]
    public void GroundFrictionDoesNotPreventMovingWallFromPushingFreeCrates()
    {
        var floor=new StaticAabbCollider(1,Box(-10,-1,20,0));
        var w=new DynamicBodyWorld(new[]{floor},Catalog());
        var path=new MovingPlatformTrajectory(8,Box(0,0,1,1),new(2,0),1);
        Add(w,1,-5,.5,1);Add(w,2,1.5,.5,1);Add(w,3,2.5,.5,1);
        Advance(w,path,.25,-4);
        Assert.Equal(2d,w.Get(2).Bounds.Center.X,10);
        Assert.Equal(3d,w.Get(3).Bounds.Center.X,10);
        Assert.False(AabbCollisionDetector.Overlaps(w.Get(2).Bounds,w.Get(3).Bounds));
        Assert.Equal(0d,w.Get(2).Bounds.Bottom,10);Assert.Equal(0d,w.Get(3).Bounds.Bottom,10);
    }
    [Fact]
    public void DestroyRemovesCrateAndPlatformContinues()
    {
        var world=CrushWorld(RigidBodyCrushResponse.Destroy);
        world.Step(None);world.Step(None);
        Assert.Empty(world.DynamicBodies!.Bodies);Assert.Equal(1L,world.DynamicBodies.DestroyedByPlatform);
        Assert.Equal(new[]{1},world.DynamicBodies.LastPlatformDestroyedBodyIds.ToArray());
        Assert.False(world.IsPropObstructed(8));Assert.Equal(1d,world.MovingPlatforms.Single().Bounds.Left,10);
        world.Step(None);Assert.Empty(world.DynamicBodies.LastPlatformDestroyedBodyIds);
        Assert.DoesNotContain(WorldPhysicalBodyView.Capture(world).Bodies,b=>b.Id.Kind==PhysicalBodyKind.Dynamic);
    }
    [Theory]
    [InlineData(RigidBodyCrushResponse.StopPlatform)] [InlineData(RigidBodyCrushResponse.Destroy)]
    public void DescendingPlatformAgainstFloorClassifiesVerticalCrush(RigidBodyCrushResponse response)
    {
        var floor=new StaticAabbCollider(1,Box(-2,-1,2,0));
        var w=new DynamicBodyWorld(new[]{floor},Catalog());
        var path=new MovingPlatformTrajectory(8,Box(0,2,1,3),new(0,-2),1);
        Add(w,1,.5,.5,response:response);
        if(response==RigidBodyCrushResponse.StopPlatform)
        {
            Assert.Throws<PlatformPropCompressionException>(()=>Advance(w,path,.5,0));
            Assert.Single(w.Bodies);Assert.Equal(0L,w.Metrics.Steps);
        }
        else { Advance(w,path,.5,0);Assert.Empty(w.Bodies);Assert.Equal(1L,w.DestroyedByPlatform); }
    }
    [Theory]
    [InlineData(SpatialQueryMode.Reference,0)]
    [InlineData(SpatialQueryMode.Indexed,0)]
    [InlineData(SpatialQueryMode.Verify,0)]
    [InlineData(SpatialQueryMode.Reference,36000)]
    [InlineData(SpatialQueryMode.Indexed,36000)]
    [InlineData(SpatialQueryMode.Verify,36000)]
    public void RepeatedSixtyHzCarryingMaintainsNonpenetration(SpatialQueryMode mode,int startTick)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),Catalog());
        var path=new MovingPlatformTrajectory(8,Box(0,0,3,1),new(.7,1.3),1.5);
        Add(w,1,1.5,1.5,1);
        for(int i=startTick;i<startTick+360;i++)
        {
            double start=i/60d,end=(i+1)/60d;
            Advance(w,path,end-start,-60,start);
            Assert.False(AabbCollisionDetector.Overlaps(w.Get(1).Bounds,path.Evaluate(end).Bounds),
                $"Tick {i+1}, time {end:R}, body bottom {w.Get(1).Bounds.Bottom:R}, platform top {path.Evaluate(end).Bounds.Top:R}");
        }
        Assert.True(w.PlatformContacts>0);
    }
    [Theory]
    [InlineData(SpatialQueryMode.Reference)]
    [InlineData(SpatialQueryMode.Indexed)]
    [InlineData(SpatialQueryMode.Verify)]
    public void TurnAtTickEndDoesNotLeaveUnrepresentableRemainder(SpatialQueryMode mode)
    {
        using var scope=SpatialQueryPolicy.Use(mode);
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),Catalog());
        var path=new MovingPlatformTrajectory(8,Box(0,0,3,1),new(.7,1.3),2.4);
        Add(w,1,100,100);
        double start=719/60d,end=720/60d;
        double duration=end-start;
        double toTurn=path.TimeUntilNextTurn(start);
        // This is the problematic binary64 interval: the analytical event
        // rounds to the exact tick end, although duration-toTurn is positive.
        Assert.True(toTurn<duration);
        Assert.Equal(end,start+toTurn);
        Assert.Equal(end,end+(duration-toTurn));
        var before=w.Get(1);
        Advance(w,path,duration,0,start);
        Assert.Equal(before,w.Get(1));
        Assert.Equal(1L,w.Metrics.Steps);
        Assert.False(AabbCollisionDetector.Overlaps(w.Get(1).Bounds,path.Evaluate(end).Bounds));
        Advance(w,path,721/60d-end,0,end);
        Assert.Equal(2L,w.Metrics.Steps);
    }
    [Fact]
    public void PlatformMaskIsIndependentAndUnsafeRestorationIsRejected()
    {
        var path=new MovingPlatformTrajectory(8,Box(0,0,1,1),new(2,0),1);
        var w=new DynamicBodyWorld(Array.Empty<StaticAabbCollider>(),Catalog(),platforms:()=>new[]{path.Evaluate(0)});
        var settings=Settings() with {Filter=new(BodyCollisionLayer.Dynamic,BodyCollisionLayer.None)};
        w.Add(new(new(PhysicalBodyKind.Dynamic,1),new(.5,.5),"Default",1),settings,new(.5,.5));
        Assert.Throws<ArgumentException>(()=>w.Configure(1,"Default",1,Settings()));
        Assert.Equal(settings,w.Get(1).Settings);Advance(w,path,.25,0);Assert.Equal(.5,w.Get(1).Bounds.Center.X);
    }
}
