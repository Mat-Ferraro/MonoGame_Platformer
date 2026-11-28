using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Simulation.Physics;

namespace PlatformerSandbox.Tests.Physics;

public sealed class PB01LandingWallHandoffTests
{
    private static Aabb Box(double l, double b, double r, double t) => new(new(l,b),new(r,t));
    private static Aabb Reflect(Aabb b, bool mirrored) => mirrored ? Box(-b.Right,b.Bottom,-b.Left,b.Top) : b;
    private static HorizontalMotionProfile Motion() => HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10,.2,.1,.35));
    private static JumpFlightTrajectory Jump() => JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4,.405,.3)));
    private static MovingPlatformTrajectory Wall(double flight, bool mirrored)
    {
        // The leading face reaches the player 5 ms AFTER landing.
        double right = .5 - 2 * (flight + .005);
        return new MovingPlatformTrajectory(8001, Reflect(Box(right-1,0,right,2),mirrored),
            new Vector2D(mirrored ? -4 : 4,0),2);
    }

    [Theory]
    [InlineData(SpatialQueryMode.Reference,false)] [InlineData(SpatialQueryMode.Indexed,false)] [InlineData(SpatialQueryMode.Verify,false)]
    [InlineData(SpatialQueryMode.Reference,true)] [InlineData(SpatialQueryMode.Indexed,true)] [InlineData(SpatialQueryMode.Verify,true)]
    public void StaticLandingCompletesTickAgainstApproachingWall(SpatialQueryMode mode,bool mirrored)
    {
        using var scope = SpatialQueryPolicy.Use(mode);
        var jump = Jump(); double flight = jump.TotalDurationSeconds;
        var wall = Wall(flight,mirrored);
        var motor = new CharacterMovementController(Reflect(Box(.5,0,1.3,1.6),mirrored),
            [new StaticAabbCollider(1,Box(-20,-1,20,0))],Motion(),Motion(),jump,-20,
            movingPlatform:wall);
        motor.Step(flight-.005,HorizontalDirection.Neutral,true);
        Assert.True(motor.IsJumping);
        var result = motor.Step(.02,HorizontalDirection.Neutral,false);
        Assert.True(result.IsGrounded);
        Assert.False(result.IsJumping);
        var finalWall = wall.Evaluate(motor.ElapsedSeconds).Bounds;
        Assert.False(AabbCollisionDetector.Overlaps(result.Bounds,finalWall));
        Assert.Contains(result.Contacts,c=>c.ColliderId==1&&c.Normal.Y>0);
        Assert.Contains(result.Contacts,c=>c.ColliderId==8001&&c.Normal.X==(mirrored?-1:1));
        Assert.Equal(mirrored?finalWall.Left:finalWall.Right,mirrored?result.Bounds.Right:result.Bounds.Left,8);
        motor.Step(.01,HorizontalDirection.Neutral,false);
        Assert.False(AabbCollisionDetector.Overlaps(motor.Bounds,wall.Evaluate(motor.ElapsedSeconds).Bounds));
    }

    [Theory]
    [InlineData(SpatialQueryMode.Reference,false)] [InlineData(SpatialQueryMode.Indexed,false)] [InlineData(SpatialQueryMode.Verify,false)]
    [InlineData(SpatialQueryMode.Reference,true)] [InlineData(SpatialQueryMode.Indexed,true)] [InlineData(SpatialQueryMode.Verify,true)]
    public void StaticLandingBetweenWallAndStopCrateRetriesWholeTick(SpatialQueryMode mode,bool mirrored)
    {
        using var scope = SpatialQueryPolicy.Use(mode);
        var jump = Jump();
        var wall = Wall(jump.TotalDurationSeconds,mirrored);
        var w = new WorldSession([new StaticAabbCollider(1,Box(-20,-1,20,0))], [wall],Motion(),Motion(),jump,-20);
        w.ConfigureCrushLifecycle(WorldCrushPolicy.Death,new WorldRespawnSettings{Mode=WorldRespawnMode.None});
        w.InstallDynamicBodies(new PhysicsMaterialCatalog(new[]{new PhysicsMaterial("Default",1,1,0,1,0)}),
            new[]{new WorldDynamicBodyFixtures.Fixture{Id=1,Material="Default",X=mirrored?-1.805:1.805,Y=.5,Mass=1,CollisionMask=BodyCollisionLayer.All}});
        w.AddPlayer(1,Reflect(Box(.5,0,1.3,1.6),mirrored));
        bool stopped=false,retreated=false;
        for (int i=0;i<100;i++)
        {
            long tick=w.WorldTick;
            var step=w.Step([new WorldPlayerCommand(1,HorizontalDirection.Neutral,i==0)]);
            Assert.Equal(tick+1,w.WorldTick);
            var player=w.GetPlayer(1); var currentWall=w.MovingPlatforms.Single();
            Assert.False(player.IsDead);
            Assert.False(AabbCollisionDetector.Overlaps(player.Bounds,currentWall.Bounds));
            Assert.False(AabbCollisionDetector.Overlaps(player.Bounds,w.DynamicBodies!.Get(1).Bounds));
            Assert.DoesNotContain(step.Events,e=>e.Kind==WorldGameplayEventKind.PlayerCrushed||e.Kind==WorldGameplayEventKind.PlayerDied);
            stopped |= w.IsPropObstructed(8001);
            retreated |= stopped && currentWall.Velocity.X*(mirrored?-1:1)<0;
        }
        Assert.True(stopped);
        Assert.True(retreated);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void EndpointPinchRecognizesEntryIntoOpposingVerticalSpan(bool mirrored)
    {
        var wall=new MovingPlatformTrajectory(8001,Reflect(Box(0,0,1,2),mirrored),new Vector2D(mirrored?-2:2,0),1);
        var crate=new StaticAabbCollider(90,Reflect(Box(2,0,3,1),mirrored));
        var start=Reflect(Box(1.1,1.01,1.9,2.61),mirrored);
        var end=Reflect(Box(1.1,.9,1.9,2.5),mirrored);
        // End wall face is 1.3; the .7-wide corridor cannot contain the .8-wide body.
        var crush=OpposingSurfaceCrushDetector.AtPenetratingSideEndpoint(start,end,0,.15,[crate],[wall]);
        Assert.NotNull(crush);
        Assert.Equal(8001,crush!.PushingColliderId);
        Assert.Equal(90,crush.OpposingColliderId);
        Assert.Equal(CharacterCrushAxis.Horizontal,crush.Axis);
        Assert.Null(OpposingSurfaceCrushDetector.AtPenetratingSideEndpoint(start,end,0,.15,[],[wall]));
        Assert.Null(OpposingSurfaceCrushDetector.AtPenetratingSideEndpoint(start,start,0,.15,[crate],[wall]));
        var far=new StaticAabbCollider(91,Reflect(Box(3,0,4,1),mirrored));
        Assert.Null(OpposingSurfaceCrushDetector.AtPenetratingSideEndpoint(start,end,0,.15,[far],[wall]));
    }
}
