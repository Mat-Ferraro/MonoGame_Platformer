using System;
using System.Reflection;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;
using Xunit;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>
/// Regression for the Connected Cavern B moving-support + wind freeze.
/// A representable WORLD cruise speed can reconstruct to a relative speed
/// one or two ULPs past the personal speed cap. Prior to the fix this created
/// 129 zero-time iterations on "moving-support-subresolution-cruise".
/// </summary>
public sealed class MovingSupportWindCruiseRegressionTests
{
    [Theory]
    [InlineData(7.516666666666666, -0.7, 6.75, HorizontalDirection.Right)]
    [InlineData(7.516666666666666, 0.7, -6.75, HorizontalDirection.Left)]
    public void RepresentableCruiseOnSupportedWindPlatformDoesNotLoop(
        double startTime, double travelX, double windX, HorizontalDirection direction)
    {
        // Deliberately wide platform: this test isolates the numerical cruise
        // event from actual edge departures, wall contacts and crush logic.
        var platform = new MovingPlatformTrajectory(8002,
            new Aabb(new Vector2D(-100.0, 0.0),
                     new Vector2D(100.0, 0.25)),
            new Vector2D(travelX, 0.0), 1.5);
        double platformX = platform.Evaluate(startTime).Velocity.X;
        double targetRelativeX = (int)direction * 9.0;
        double cruiseWorldX = platformX + windX + targetRelativeX;

        // Assert that the test actually exercises the rounding asymmetry.
        Assert.NotEqual(targetRelativeX, cruiseWorldX - platformX - windX);
        Assert.Equal(cruiseWorldX,
            platformX + windX + targetRelativeX);

        HorizontalMotionProfile motion = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.2, 0.1, 0.35));
        JumpFlightTrajectory flight = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(4.0, 0.4, 0.3)));
        var body = new Aabb(new Vector2D(0.0, 0.25),
                            new Vector2D(0.8, 1.85));
        var motor = new CharacterMovementController(
            body, Array.Empty<PlatformerSandbox.Simulation.Collision.Detection.StaticAabbCollider>(),
            motion, motion, flight, -20.0,
            movingPlatform: platform, initialWorldTimeSeconds: startTime);
        Assert.Equal(8002, motor.MovingSupportId);
        Assert.True(motor.IsGrounded);

        // Recreate the pre-step state recorded by the watchdog. Setting the
        // internal velocity/transport fields only is intentional: the public
        // motor constructor does not accept a nonzero running velocity.
        SetField(motor, "_velocity", new Vector2D(cruiseWorldX, 0.0));
        SetField(motor, "_externalHorizontalSpeed", windX);

        for (int i = 0; i < 10; i++)
        {
            var result = motor.Step(1.0 / 60.0, direction, false,
                externalHorizontalSpeed: windX);
            Assert.True(result.Bounds.IsValid);
            Assert.True(double.IsFinite(result.Velocity.X));
            Assert.True(result.IsGrounded);
            Assert.Equal(8002, motor.MovingSupportId);
            Assert.InRange(Math.Abs(result.Velocity.X - cruiseWorldX), 0.0, 1e-8);
        }
    }

    [Fact]
    public void MeaningfulOverspeedStillUsesAnalyticalDeceleration()
    {
        const double startTime = 7.516666666666666;
        const double windX = 6.75;
        var platform = new MovingPlatformTrajectory(8002,
            new Aabb(new Vector2D(-100, 0), new Vector2D(100, 0.25)),
            new Vector2D(-0.7, 0.0), 1.5);
        double floorX = platform.Evaluate(startTime).Velocity.X;
        double cruiseX = floorX + windX + 9.0;
        HorizontalMotionProfile motion = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.2, 0.1, 0.35));
        JumpFlightTrajectory flight = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(4.0, 0.4, 0.3)));
        var motor = new CharacterMovementController(
            new Aabb(new Vector2D(0, 0.25), new Vector2D(0.8, 1.85)),
            Array.Empty<PlatformerSandbox.Simulation.Collision.Detection.StaticAabbCollider>(),
            motion, motion, flight, -20.0,
            movingPlatform: platform, initialWorldTimeSeconds: startTime);
        SetField(motor, "_velocity", new Vector2D(cruiseX + 0.5, 0.0));
        SetField(motor, "_externalHorizontalSpeed", windX);

        var result = motor.Step(0.001, HorizontalDirection.Right, false,
            externalHorizontalSpeed: windX);
        Assert.True(result.IsGrounded);
        Assert.True(result.Velocity.X < cruiseX + 0.5);
        Assert.True(result.Velocity.X > cruiseX);
    }

    // Recreate the transient dynamic-support leg and wind combination from
    // the PB01 integration-lab watchdog at world time 30.45. Mirror the case
    // so either sign of reconstructed neutral speed is covered.
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void PB01_NeutralOnDynamicSupportWithWindAdvancesFullTick(int sign)
    {
        var (motor, platform, windX, worldX, endTime) = CreateDynamicWindRider(sign);
        double startTime = motor.ElapsedSeconds;
        var floor = platform.Evaluate(startTime);
        double residual = worldX - floor.Velocity.X - windX;
        Assert.NotEqual(0.0, residual);
        Assert.Equal(startTime, startTime + Math.Abs(residual) / 90.0);
        double startX = motor.Bounds.Left;

        var result = motor.StepOnWorldTimeline(startTime, endTime,
            HorizontalDirection.Neutral, false, externalHorizontalSpeed: windX);

        Assert.Equal(endTime, result.ElapsedSeconds);
        Assert.True(result.IsGrounded);
        Assert.Equal(platform.Id, motor.MovingSupportId);
        Assert.Empty(result.Contacts);
        Assert.Equal(platform.Evaluate(endTime).Bounds.Top, result.Bounds.Bottom);
        Assert.InRange(Math.Abs(result.Velocity.X - worldX), 0.0, 1e-10);
        Assert.InRange(Math.Abs(result.Bounds.Left -
            (startX + worldX * (endTime - startTime))), 0.0, 1e-9);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void PB01_NeutralOnDynamicSupportStillBrakesRealPersonalVelocity(int sign)
    {
        var (motor, platform, windX, worldX, endTime) = CreateDynamicWindRider(sign);
        double startTime = motor.ElapsedSeconds;
        double dt = endTime - startTime;
        double personal = sign * 2.0;
        SetField(motor, "_velocity", new Vector2D(worldX + personal,
            platform.Evaluate(startTime).Velocity.Y));
        double startX = motor.Bounds.Left;

        var result = motor.StepOnWorldTimeline(startTime, endTime,
            HorizontalDirection.Neutral, false, externalHorizontalSpeed: windX);

        Assert.True(result.IsGrounded);
        Assert.Equal(platform.Id, motor.MovingSupportId);
        double expectedPersonal = personal - sign * 90.0 * dt;
        Assert.InRange(Math.Abs(result.Velocity.X - (worldX + expectedPersonal)), 0.0, 1e-9);
        double expectedX = startX + (worldX + personal) * dt - sign * 45.0 * dt * dt;
        Assert.InRange(Math.Abs(result.Bounds.Left - expectedX), 0.0, 1e-9);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void PB01_DirectionalInputFromDynamicTransportEndpointAccelerates(int sign)
    {
        var (motor, platform, windX, worldX, endTime) = CreateDynamicWindRider(sign);
        double startTime = motor.ElapsedSeconds;
        // Input opposite the direction of combined support and wind motion.
        var direction = sign < 0 ? HorizontalDirection.Right : HorizontalDirection.Left;
        var result = motor.StepOnWorldTimeline(startTime, endTime,
            direction, false, externalHorizontalSpeed: windX);
        Assert.True(result.IsGrounded);
        Assert.Equal(platform.Id, motor.MovingSupportId);
        double expectedPersonal = -sign * 45.0 * (endTime - startTime);
        Assert.InRange(Math.Abs(result.Velocity.X - (worldX + expectedPersonal)), 0.0, 1e-9);
    }

    private static (CharacterMovementController Motor, MovingPlatformTrajectory Platform,
        double WindX, double WorldX, double EndTime) CreateDynamicWindRider(int sign)
    {
        const double startTime = 30.45;
        double endTime = startTime + 1.0 / 60.0;
        double dt = endTime - startTime;
        double windX = sign * 6.75;
        var floorBounds = new Aabb(new Vector2D(40.0, 18.96),
            new Vector2D(52.0, 19.96));
        var platform = MovingPlatformTrajectory.FromTickMotion(int.MaxValue - 13,
            new[] { (startTime, floorBounds), (endTime, floorBounds.Translated(
                new Vector2D(sign * 2.9523359317032574 * dt, -1.6 * dt))) });
        var floor = platform.Evaluate(startTime);
        double worldX = floor.Velocity.X + windX;
        HorizontalMotionProfile motion = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.2, 0.1, 0.35));
        JumpFlightTrajectory flight = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(4.0, 0.4, 0.3)));
        var body = new Aabb(new Vector2D(48.10626181156833, floor.Bounds.Top),
            new Vector2D(48.9062618115683, floor.Bounds.Top + 1.6));
        var motor = new CharacterMovementController(body,
            Array.Empty<PlatformerSandbox.Simulation.Collision.Detection.StaticAabbCollider>(),
            motion, motion, flight, -20.0,
            movingPlatform: platform, initialWorldTimeSeconds: startTime);
        Assert.True(motor.IsGrounded);
        SetField(motor, "_velocity", new Vector2D(worldX, floor.Velocity.Y));
        SetField(motor, "_externalHorizontalSpeed", windX);
        return (motor, platform, windX, worldX, endTime);
    }

    private static void SetField(CharacterMovementController motor,
        string name, object value)
    {
        FieldInfo field = typeof(CharacterMovementController).GetField(
            name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing internal field: {name}");
        field.SetValue(motor, value);
    }
}
