using System;
using System.Collections.Generic;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using Xunit;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>
/// Smoke coverage for the analytical jump iteration guard. The guard must be
/// inert during ordinary successful simulation; it is not a collision policy.
/// </summary>
public sealed class AnalyticalJumpProgressGuardTests
{
    [Theory]
    [InlineData(HorizontalDirection.Neutral)]
    [InlineData(HorizontalDirection.Right)]
    public void OrdinaryJumpAcrossFlatLdtkFloorTerminates(HorizontalDirection direction)
    {
        var floor = new StaticAabbCollider(11801, new Aabb(
            new Vector2D(40.0, 17.0), new Vector2D(80.0, 18.0)));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.12, 0.085, 0.27));
        var flight = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(
                3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        var motor = new SteerableJumpMotor(
            new Aabb(new Vector2D(47.0, 18.0),
                     new Vector2D(47.8, 19.6)),
            0.0, air, flight, new[] { floor }, -24.0,
            startTimeSeconds: 20.0);

        for (int frame = 0; frame < 180; frame++)
        {
            var result = motor.Step(1.0 / 60.0, direction);
            Assert.True(result.Bounds.IsValid);
            Assert.True(double.IsFinite(result.Bounds.Left));
            Assert.True(double.IsFinite(result.Bounds.Bottom));
        }
    }
}
