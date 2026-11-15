using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class Milestone26CrushOutcomeTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    private static CharacterMovementController Motor(
        Aabb body, IReadOnlyList<StaticAabbCollider> statics,
        MovingPlatformTrajectory platform,
        IReadOnlyList<MovingPlatformTrajectory>? others = null)
    {
        var horizontal = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, .2, .1, .35));
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, .4, .3)));
        return new CharacterMovementController(body, statics, horizontal, horizontal,
            jump, -20, movingPlatform: platform, additionalMovingPlatforms: others);
    }

    [Fact]
    public void RisingElevatorAgainstCeilingIsClassifiedWithoutCommittingPartialState()
    {
        var lift = new MovingPlatformTrajectory(7,
            Box(-1, 0, 2, .25), new Vector2D(0, 2), 1);
        var ceiling = new StaticAabbCollider(3, Box(-2, 2.15, 3, 2.45));
        Aabb initial = Box(0, .25, .8, 1.85);
        var motor = Motor(initial, [ceiling], lift);

        CharacterCrushException failure = Assert.Throws<CharacterCrushException>(
            () => motor.Step(.3, HorizontalDirection.Neutral, false));
        Assert.Equal(7, failure.Crush.PushingColliderId);
        Assert.Equal(3, failure.Crush.OpposingColliderId);
        Assert.Equal(CharacterCrushAxis.Vertical, failure.Crush.Axis);
        Assert.Equal(initial, motor.Bounds);
        Assert.Equal(0.0, motor.ElapsedSeconds);
        Assert.Equal(7, motor.MovingSupportId);
        Assert.NotNull(motor.LastFault);
    }

    [Fact]
    public void DownwardPlatformAgainstStaticFloorIsClassifiedInsteadOfPenetrating()
    {
        var overhead = new MovingPlatformTrajectory(7,
            Box(-1, 1.8, 2, 2.1), new Vector2D(0, -1.5), .5);
        Aabb initial = Box(0, 0, .8, 1.6);
        var floor = new StaticAabbCollider(1, Box(-2, -1, 2, 0));
        var motor = Motor(initial, [floor], overhead);

        CharacterCrushException failure = Assert.Throws<CharacterCrushException>(
            () => motor.Step(.2, HorizontalDirection.Neutral, false));
        Assert.Equal(7, failure.Crush.PushingColliderId);
        Assert.Equal(1, failure.Crush.OpposingColliderId);
        Assert.Equal(initial, motor.Bounds);
        Assert.Equal(0.0, motor.ElapsedSeconds);
    }

    [Fact]
    public void TwoClosingMovingPlatformsReportBothStableIds()
    {
        var support = new MovingPlatformTrajectory(7,
            Box(-1, 0, 2, .25), new Vector2D(0, 1), 1);
        var overhead = new MovingPlatformTrajectory(8,
            Box(-1, 2.05, 2, 2.35), new Vector2D(0, -1), 1);
        var motor = Motor(Box(0, .25, .8, 1.85), [], support, [overhead]);
        CharacterCrushException failure = Assert.Throws<CharacterCrushException>(
            () => motor.Step(.2, HorizontalDirection.Neutral, false));
        Assert.Equal(8, failure.Crush.PushingColliderId);
        Assert.Equal(7, failure.Crush.OpposingColliderId);
        Assert.Equal(7, motor.MovingSupportId);
        Assert.Equal(0.0, motor.ElapsedSeconds);
    }

    [Fact]
    public void SideContactWithoutOpposingConstraintIsNotAJumpOrCrush()
    {
        var platform = new MovingPlatformTrajectory(7,
            Box(1.3, .5, 2.3, .85), new Vector2D(0, -1.5), 1);
        var motor = Motor(Box(0, 0, .8, 1.6),
            [new StaticAabbCollider(1, Box(-4, -1, 5, 0))], platform);
        for (int i = 0; i < 12; i++)
        {
            motor.Step(1.0 / 60.0, HorizontalDirection.Right, false);
            Assert.False(AabbCollisionDetector.Overlaps(motor.Bounds,
                platform.Evaluate(motor.ElapsedSeconds).Bounds));
        }
    }

    [Fact]
    public void APlatformOverheadIsNotCrushingWhenGapIsOpening()
    {
        Aabb body = Box(0, 0, .8, 1.6);
        var floor = new StaticAabbCollider(1, Box(-2, -1, 2, 0));
        var receding = new MovingPlatformTrajectory(7,
            Box(-1, 1.8, 2, 2.1), new Vector2D(0, 1), 1);
        Assert.Null(OpposingSurfaceCrushDetector.AboveStaticGround(
            body, body, 0, .2, [floor], [receding]));
    }

    [Fact]
    public void LeavingOverheadPlatformsHorizontalCoverageIsNotPrematurelyCrushed()
    {
        Aabb body = Box(0, 0, .8, 1.6);
        var floor = new StaticAabbCollider(1, Box(-2, -1, 5, 0));
        var closing = new MovingPlatformTrajectory(7,
            Box(0, 1.8, 1.0, 2.1), new Vector2D(0, -1.5), .5);
        Aabb escaped = Box(1.5, 0, 2.3, 1.6);
        Assert.Null(OpposingSurfaceCrushDetector.AboveStaticGround(
            body, escaped, 0, .2, [floor], [closing]));
    }

    [Fact]
    public void MovingPlatformAndStaticWallCanCloseAnImpossibleHorizontalGap()
    {
        Aabb body = Box(0, 0, .8, 1.6);
        var rightWall = new StaticAabbCollider(3, Box(1.0, -.5, 1.5, 2));
        var leftPusher = new MovingPlatformTrajectory(7,
            Box(-1, -.25, -.2, 1.9), new Vector2D(.5, 0), .5);
        CharacterCrushEvent? crush =
            OpposingSurfaceCrushDetector.AcrossClosingSideSurfaces(
                body, body, 0, .5, [rightWall], [leftPusher]);
        Assert.NotNull(crush);
        Assert.Equal(CharacterCrushAxis.Horizontal, crush.Axis);
        Assert.Equal(7, crush.PushingColliderId);
        Assert.Equal(3, crush.OpposingColliderId);
    }

    [Fact]
    public void GroundedPlayerPinnedBetweenMovingSideAndStaticWallDoesNotCommitPenetration()
    {
        Aabb initial = Box(0, 0, .8, 1.6);
        var pusher = new MovingPlatformTrajectory(7,
            Box(-1, -.25, -.2, 1.9), new Vector2D(.5, 0), .5);
        var floor = new StaticAabbCollider(1, Box(-3, -1, 3, 0));
        var wall = new StaticAabbCollider(3, Box(1, -.5, 1.5, 2));
        var motor = Motor(initial, [floor, wall], pusher);
        CharacterCrushException crushed = Assert.Throws<CharacterCrushException>(
            () => motor.Step(.5, HorizontalDirection.Neutral, false));
        Assert.Equal(CharacterCrushAxis.Horizontal, crushed.Crush.Axis);
        Assert.Equal(initial, motor.Bounds);
        Assert.Equal(0.0, motor.ElapsedSeconds);
    }

    [Fact]
    public void TwoMovingSidesCanCloseAndReportAHorizontalCrush()
    {
        Aabb body = Box(0, 0, .8, 1.6);
        var left = new MovingPlatformTrajectory(7,
            Box(-1, -.25, -.2, 1.9), new Vector2D(.4, 0), .5);
        var right = new MovingPlatformTrajectory(8,
            Box(1.2, -.25, 2, 1.9), new Vector2D(-.4, 0), .5);
        var result = OpposingSurfaceCrushDetector.AcrossClosingSideSurfaces(
            body, body, 0, .5, [], [left, right]);
        Assert.NotNull(result);
        Assert.Equal(CharacterCrushAxis.Horizontal, result.Axis);
        Assert.Equal(7, result.PushingColliderId);
        Assert.Equal(8, result.OpposingColliderId);
    }

    [Fact]
    public void SingleMovingSideContactWithoutOpposingWallIsNotCrushing()
    {
        Aabb body = Box(0, 0, .8, 1.6);
        var left = new MovingPlatformTrajectory(7,
            Box(-1, -.25, -.2, 1.9), new Vector2D(.5, 0), .5);
        Assert.Null(OpposingSurfaceCrushDetector.AcrossClosingSideSurfaces(
            body, body, 0, .5, [], [left]));
    }

    [Fact]
    public void InvalidCrushEventCannotBeInventedWithSameSurfaceOnBothSides()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterCrushEvent(
            7, 7, CharacterCrushAxis.Vertical, 0, Box(0, 0, 1, 1)));
    }
}
