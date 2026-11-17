using System;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;
using Xunit;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>
/// Regression for the 2026-11-16 Connected Cavern wind-zone freeze.
/// At the selected world time the LDtk platform is returning; its X velocity
/// equals the airborne character's world X velocity.  Adding/cancelling wind
/// before subtracting the platform velocity used to invent an extremely short
/// relative X turn, and the sub-ULP normalizer reentered without advancing.
/// </summary>
public sealed class WindPlatformSubUlpJumpRegressionTests
{
    [Theory]
    [InlineData(0.7, -0.75, HorizontalDirection.Right)]  // Cavern A
    [InlineData(-0.7, 0.75, HorizontalDirection.Left)]  // Cavern B
    public void MatchedWorldAndPlatformSpeedWithWindDoesNotCreateZeroTimeTurn(
        double travelOffsetX, double windFactor, HorizontalDirection direction)
    {
        const double worldStart = 8.516666666666666;
        const double step = 0.016666666666667496;

        var platform = new MovingPlatformTrajectory(8001,
            new Aabb(new Vector2D(40.625, -10.0),
                     new Vector2D(44.625, -9.4375)),
            new Vector2D(travelOffsetX, 0.0), 1.5);
        double platformVx = platform.Evaluate(worldStart).Velocity.X;
        double windVx = 9.0 * windFactor;
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.22, 0.16, 0.43));
        var flight = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(
                3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        var player = new Aabb(
            new Vector2D(44.39944444444444, 19.024020991784802),
            new Vector2D(45.199444444444396, 20.624020991784803));
        var motor = new SteerableJumpMotor(player, platformVx, air,
            flight, Array.Empty<StaticAabbCollider>(), -24.0,
            movingPlatform: platform, startTimeSeconds: worldStart,
            externalHorizontalSpeed: windVx);

        // The pre-fix implementation reentered 'normalized-sub-ulp-event'
        // 129 times and threw SimulationNoProgressException here.
        SteerableJumpStepResult result = motor.Step(step, direction,
            externalHorizontalSpeed: windVx);

        Assert.True(result.Bounds.IsValid);
        Assert.True(double.IsFinite(result.Bounds.Left));
        Assert.True(double.IsFinite(result.Velocity.X));
        Assert.True(motor.FlightElapsedSeconds > 0.0);
        Assert.False(result.IsGrounded);
    }
}
