using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.MovingPlatforms;

public sealed class Milestone24RideArbitrationTests
{
    private static Aabb Box(double x0, double y0, double x1, double y1) =>
        new(new Vector2D(x0, y0), new Vector2D(x1, y1));

    [Fact]
    public void RideEndsAtStaticWallRatherThanMovingThroughIt()
    {
        // Travel offset is distance, not speed: 2 units over 2 seconds = 1 unit/s.
        var platform = new MovingPlatformTrajectory(7, Box(-1, 0, 3, 0.25),
            travelOffset: new Vector2D(2, 0), oneWayDurationSeconds: 2.0);
        var wall = new StaticAabbCollider(2, Box(3, 0.25, 3.5, 2));
        var result = MovingPlatformRideEventScheduler.Step(platform, 0, 0.5,
            Box(2, 0.25, 2.8, 1.85), 0.0, 0.0, [wall]);
        Assert.NotNull(result.StaticContact);
        Assert.Equal(2, result.StaticContact!.Value.ColliderId);
        Assert.Equal(new Vector2D(-1, 0), result.StaticContact.Value.Normal);
        Assert.InRange(Math.Abs(result.AdvancedSeconds - 0.2), 0, 1e-7);
        Assert.InRange(Math.Abs(result.Bounds.Right - 3.0), 0, 1e-7);
        Assert.Equal(0.0, result.WorldVelocity.X);
        Assert.True(result.IsSupported);
    }

    [Fact]
    public void SupportDeparturePrecedesUnreachableStaticContact()
    {
        var platform = new MovingPlatformTrajectory(7, Box(-1, 0, 3, 0.25),
            new Vector2D(1, 0), 2.0);
        var distantWall = new StaticAabbCollider(2, Box(7, 0.25, 8, 2));
        var result = MovingPlatformRideEventScheduler.Step(platform, 0, 1.0,
            Box(2, 0.25, 2.8, 1.85), 2.0, 0.0, [distantWall]);
        Assert.Null(result.StaticContact);
        Assert.False(result.IsSupported);
        Assert.InRange(Math.Abs(result.AdvancedSeconds - 0.5), 0, 1e-7);
    }

    [Fact]
    public void TenThousandNeutralRideUpdatesCrossTurnaroundsWithoutFault()
    {
        // Wide support prevents departure so the test isolates the absolute
        // and relative clocks through many exact and near-exact turnarounds.
        var platform = new MovingPlatformTrajectory(7,
            Box(-50, 0, 50, 0.25), new Vector2D(2.4, 0), 2.4);
        var ground = PlatformerSandbox.Simulation.Motion.Solvers.HorizontalMotionSolver.Solve(
            new PlatformerSandbox.Simulation.Motion.Constraints.HorizontalMotionConstraints(
                9, 0.18, 0.13, 0.38));
        var jump = PlatformerSandbox.Simulation.Motion.Solvers.JumpFlightSolver.ForHeldJump(
            PlatformerSandbox.Simulation.Motion.Solvers.JumpConstraintSolver.Solve(
                new PlatformerSandbox.Simulation.Motion.Constraints.JumpConstraints(
                    3.7, 0.38, 0.34)));
        var motor = new PlatformerSandbox.Simulation.CharacterMotor.CharacterMovementController(
            Box(0, 0.25, 0.8, 1.85), Array.Empty<StaticAabbCollider>(),
            ground, ground, jump, -24.0, movingPlatform: platform,
            faultPolicy: PlatformerSandbox.Simulation.Diagnostics.SimulationFaultPolicy.QualityAssurance);
        for (int i = 0; i < 10_000; i++)
        {
            try
            {
                motor.Step(1.0 / 60.0,
                    PlatformerSandbox.Simulation.Motion.Profiles.HorizontalDirection.Neutral,
                    false);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    $"Ride stress failed at frame {i}, t={motor.ElapsedSeconds:R}", error);
            }
            Assert.True(motor.IsGrounded);
            Assert.Equal(7, motor.MovingSupportId);
            Assert.Equal(platform.Evaluate(motor.ElapsedSeconds).Bounds.Top,
                motor.Bounds.Bottom);
        }
    }

    [Fact]
    public void UnobstructedRidingContinuesWithPlatformCarry()
    {
        // Travel offset is distance, not speed: 2 units over 2 seconds = 1 unit/s.
        var platform = new MovingPlatformTrajectory(7, Box(-1, 0, 3, 0.25),
            travelOffset: new Vector2D(2, 0), oneWayDurationSeconds: 2.0);
        var result = MovingPlatformRideEventScheduler.Step(platform, 0, 0.25,
            Box(0, 0.25, 0.8, 1.85), 0.0, 0.0,
            Array.Empty<StaticAabbCollider>());
        Assert.Null(result.StaticContact);
        Assert.True(result.IsSupported);
        Assert.InRange(Math.Abs(result.Bounds.Left - 0.25), 0, 1e-10);
    }
}
