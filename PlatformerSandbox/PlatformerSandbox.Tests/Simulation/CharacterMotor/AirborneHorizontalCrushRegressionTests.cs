using System.Reflection;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class AirborneHorizontalCrushRegressionTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    private static CharacterMovementController Motor(Aabb body)
    {
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.18, 0.13, 0.38));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.22, 0.16, 0.43));
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        var pad = new StaticAabbCollider(11, Box(8.2, 0, 10.35, 1.0));
        var wall = new StaticAabbCollider(12, Box(10.35, 0, 10.75, 3.6));
        var platform = new MovingPlatformTrajectory(7,
            Box(5.3, 2.05, 7.35, 2.30), new Vector2D(2.4, 0), 2.4);
        return new CharacterMovementController(body, [pad, wall],
            ground, air, jump, gravityAcceleration: -24.0,
            movingPlatform: platform);
    }

    [Theory]
    [InlineData(2.15)]
    [InlineData(6.95)]
    public void AirbornePinchIsClassifiedBeforeAlternatingWallContacts(double worldTime)
    {
        // 0.0001 above the pad means the character is not yet grounded.
        // During the next 0.10 s, the moving platform closes its gap to
        // the wall from 0.85 to 0.75: less than the 0.80-wide character.
        Aabb start = Box(9.55, 1.0001, 10.35, 2.6001);
        var motor = Motor(start);
        Assert.False(motor.IsGrounded);
        // Test-only clock setup: no test motion or geometry is modified.
        // Avoid the prior passes of the platform through this same fixture.
        typeof(CharacterMovementController)
            .GetField("_elapsedSeconds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(motor, worldTime);

        CharacterCrushException failure = Assert.Throws<CharacterCrushException>(
            () => motor.Step(0.10, HorizontalDirection.Neutral, false));
        Assert.Equal(CharacterCrushAxis.Horizontal, failure.Crush.Axis);
        Assert.Equal(7, failure.Crush.PushingColliderId);
        Assert.Equal(12, failure.Crush.OpposingColliderId);
        Assert.Equal(start, motor.Bounds);
        Assert.Equal(worldTime, motor.ElapsedSeconds);
    }

    [Fact]
    public void AirborneSideApproachWithoutOpposingWallIsNotACrush()
    {
        var platform = new MovingPlatformTrajectory(7,
            Box(5.3, 2.05, 7.35, 2.30), new Vector2D(2.4, 0), 2.4);
        Aabb body = Box(9.55, 1.0001, 10.35, 2.6001);
        Assert.Null(OpposingSurfaceCrushDetector.AcrossClosingSideSurfaces(
            body, body.Translated(new Vector2D(0, -0.12)),
            6.95, 0.10, [], [platform]));
    }
}