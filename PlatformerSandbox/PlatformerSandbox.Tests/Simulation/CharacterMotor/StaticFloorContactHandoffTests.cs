using System;
using System.Collections.Generic;
using System.Reflection;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using Xunit;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>
/// Regression for the connected LDtk cavern floor at Y=18 (collider 11801).
/// The previous helper covered ordinary controller motion, but analytical
/// jump and re-entrant accelerated collision phases needed the same bounded
/// face canonicalization AFTER a known collision boundary.
/// </summary>
public sealed class StaticFloorContactHandoffTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    private static StaticAabbCollider CavernFloor() =>
        new(11801, Box(40.0, 17.0, 80.0, 18.0));

    private static SteerableJumpMotor CreateJump(StaticAabbCollider floor)
    {
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.12, 0.085, 0.27));
        var flight = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(
                3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        return new SteerableJumpMotor(
            Box(47.98687090589234, 18.0, 48.78687090589234, 19.6),
            0.0, air, flight, new[] { floor }, -24.0,
            startTimeSeconds: 20.0);
    }

    private static void InjectPreviousContactRounding(SteerableJumpMotor jump,
        double bottom)
    {
        // Reproduce the previous solved segment's boundary rounding, which
        // cannot be injected through the constructor (correctly validates
        // all externally supplied initial bounds). Keep the state otherwise
        // identical to a legal takeoff.
        FieldInfo? boundsField = typeof(SteerableJumpMotor).GetField(
            "_bounds", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(boundsField);
        boundsField!.SetValue(jump, Box(47.98687090589234, bottom,
            48.78687090589234, bottom + 1.6));
    }

    [Fact]
    public void AnalyticalJump_OneUlpFloorOverlap_IsCanonicalizedBeforeStrictSweep()
    {
        StaticAabbCollider floor = CavernFloor();
        SteerableJumpMotor jump = CreateJump(floor);
        InjectPreviousContactRounding(jump, Math.BitDecrement(18.0));

        SteerableJumpStepResult result = jump.Step(
            1.0 / 60.0, HorizontalDirection.Right);
        Assert.True(result.Bounds.IsValid);
        Assert.False(AabbCollisionDetector.Overlaps(result.Bounds, floor.Bounds));
        Assert.True(result.Bounds.Bottom >= 18.0);
    }

    [Fact]
    public void AnalyticalJump_MeaningfulFloorOverlap_StillFailsStrictSweep()
    {
        StaticAabbCollider floor = CavernFloor();
        SteerableJumpMotor jump = CreateJump(floor);
        InjectPreviousContactRounding(jump, 18.0 - 1e-5);
        Assert.Throws<InvalidOperationException>(() =>
            jump.Step(1.0 / 60.0, HorizontalDirection.Right));
    }

    [Fact]
    public void AcceleratedStepper_AlreadyPenetratingInput_RemainsRejected()
    {
        StaticAabbCollider floor = CavernFloor();
        Assert.Throws<InvalidOperationException>(() =>
            UnifiedAcceleratedKinematicStepper.Step(
                Box(47.98687090589234, 18.0 - 1e-5,
                    48.78687090589234, 19.6 - 1e-5),
                Vector2D.Zero, new Vector2D(0, -24), 20.0,
                1.0 / 60.0, new[] { floor },
                Array.Empty<PlatformerSandbox.Simulation.MovingPlatforms.MovingPlatformTrajectory>()));
    }

    [Fact]
    public void AcceleratedStepper_LongStaticTopTraversal_NeverCommitsPenetration()
    {
        StaticAabbCollider floor = CavernFloor();
        IReadOnlyList<StaticAabbCollider> staticColliders = new[] { floor };
        Aabb bounds = Box(47.98687090589234, 18.0,
            48.78687090589234, 19.6);
        Vector2D velocity = new(2.0, 0.0);
        const double dt = 1.0 / 60.0;

        for (int frame = 0; frame < 360; frame++)
        {
            var result = UnifiedAcceleratedKinematicStepper.Step(
                bounds, velocity, new Vector2D(0, -24),
                20.0 + frame * dt, dt, staticColliders,
                Array.Empty<PlatformerSandbox.Simulation.MovingPlatforms.MovingPlatformTrajectory>());
            bounds = result.Bounds;
            velocity = result.Velocity;
            Assert.False(AabbCollisionDetector.Overlaps(bounds, floor.Bounds));
            Assert.True(bounds.IsValid);
        }
    }
}
