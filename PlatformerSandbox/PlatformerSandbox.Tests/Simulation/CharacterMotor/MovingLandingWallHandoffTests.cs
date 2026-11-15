using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>
/// The jump-to-ride handoff must use the SAME collision arbitration as normal
/// riding. Landing midway through a frame must not carry the player through a
/// wall and leave the next frame with initial penetration.
/// </summary>
public sealed class MovingLandingWallHandoffTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    private static MovingPlatformTrajectory Platform() => new(
        7, Box(8.0, 2.05, 9.0, 2.30),
        new Vector2D(10.0, 0.0), oneWayDurationSeconds: 10.0);

    private static CharacterMovementController Motor(
        MovingPlatformTrajectory platform, IReadOnlyList<StaticAabbCollider> walls) =>
        new(Box(8.6, 2.30, 9.4, 3.90), walls,
            HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(
                1.0, 0.1, 100.0, 0.4)),
            HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(
                1.0, 0.1, 100.0, 0.4)),
            JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
                new JumpConstraints(4.0, 0.4, 0.3))),
            gravityAcceleration: -20.0,
            movingPlatform: platform,
            faultPolicy: SimulationFaultPolicy.Development);

    private static CharacterControlStepResult JumpThenLand(CharacterMovementController motor)
    {
        var launch = motor.Step(0.25, HorizontalDirection.Neutral, true);
        Assert.True(launch.JumpStarted);
        Assert.Null(motor.MovingSupportId);
        motor.Step(0.25, HorizontalDirection.Neutral, false);
        return motor.Step(0.25, HorizontalDirection.Neutral, false);
    }

    [Fact]
    public void LandingCarryStopsAtStaticWallAndNextUpdateStartsWithoutPenetration()
    {
        MovingPlatformTrajectory platform = Platform();
        var unobstructed = Motor(platform, []);
        CharacterControlStepResult reference = JumpThenLand(unobstructed);
        Assert.Equal(7, unobstructed.MovingSupportId);
        var landing = Assert.Single(reference.Contacts.Where(c =>
            c.ColliderId == 7 && c.Normal.Y > 0.0));
        Assert.InRange(landing.TimeWithinStepSeconds, 0.0, 0.23);

        // Without wall arbitration, this is INSIDE the end-of-frame carry
        // distance, but beyond the player's position at the landing instant.
        double wallLeft = unobstructed.Bounds.Right - 0.02;
        var wall = new StaticAabbCollider(12,
            Box(wallLeft, 0.0, wallLeft + 0.4, 12.0));
        CharacterMovementController withWall = Motor(platform, [wall]);
        CharacterControlStepResult result = JumpThenLand(withWall);

        Assert.Equal(7, withWall.MovingSupportId);
        Assert.True(result.IsGrounded);
        Assert.False(AabbCollisionDetector.Overlaps(result.Bounds, wall.Bounds));
        Assert.Equal(wall.Bounds.Left, result.Bounds.Right);
        Assert.Contains(result.Contacts, c =>
            c.ColliderId == 12 && c.Normal.X < 0.0);

        for (int frame = 0; frame < 10; frame++)
        {
            withWall.Step(1.0 / 60.0, HorizontalDirection.Neutral, false);
            Assert.False(AabbCollisionDetector.Overlaps(withWall.Bounds, wall.Bounds));
            Assert.True(withWall.Bounds.Right <= wall.Bounds.Left);
        }
    }

    [Fact]
    public void OrdinaryUnobstructedLandingStillCarriesThroughRemainingFrame()
    {
        var platform = Platform();
        var motor = Motor(platform, []);
        var result = JumpThenLand(motor);
        Assert.Equal(7, motor.MovingSupportId);
        Assert.Equal(platform.Evaluate(motor.ElapsedSeconds).Bounds.Top,
            result.Bounds.Bottom);
        Assert.True(result.IsGrounded);
        Assert.False(result.IsJumping);
        Assert.Equal(0.75, motor.ElapsedSeconds);
    }
}
