using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class Milestone24Phase2CSubResolutionJumpTests
{
    private static SteerableJumpMotor MakeJump(double initialVelocity,
        double worldTime = 20.0)
    {
        HorizontalMotionProfile air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.12, 0.085, 0.27));
        JumpFlightTrajectory flight = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(
                3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        Aabb body = new(new Vector2D(0.0, 0.0), new Vector2D(0.8, 1.6));
        return new SteerableJumpMotor(body, initialVelocity, air, flight,
            [], -24.0, startTimeSeconds: worldTime);
    }

    [Fact]
    public void AccelerationRemainderBelowWorldClockResolutionSnapsToCruise()
    {
        // One representable speed below the 9-unit/s cap; the time to cap
        // is much smaller than an ULP at world time 20 seconds.
        SteerableJumpMotor jump = MakeJump(Math.BitDecrement(9.0));
        SteerableJumpStepResult result = jump.Step(
            1.0 / 60.0, HorizontalDirection.Right);
        Assert.Equal(9.0, result.Velocity.X);
        Assert.True(result.Bounds.Left > 0.0);
        Assert.True(result.Bounds.Bottom > 0.0);
    }

    [Fact]
    public void TinyReversalBrakingPhaseNormalizesToZeroThenAccelerates()
    {
        SteerableJumpMotor jump = MakeJump(-1e-14);
        SteerableJumpStepResult result = jump.Step(
            1.0 / 60.0, HorizontalDirection.Right);
        Assert.True(result.Velocity.X > 0.0);
        Assert.True(result.Bounds.Bottom > 0.0);
    }

    [Fact]
    public void NearApexFlightPhaseDoesNotSubmitNonAdvancingDuration()
    {
        SteerableJumpMotor jump = MakeJump(0.0);
        // End the first step one representable double below the apex time.
        double justBeforeApex = Math.BitDecrement(0.38);
        var before = jump.Step(justBeforeApex, HorizontalDirection.Neutral);
        var after = jump.Step(1.0 / 60.0, HorizontalDirection.Neutral);
        Assert.True(before.Bounds.Bottom > 0.0);
        Assert.True(after.Bounds.Bottom > 0.0);
        Assert.True(double.IsFinite(after.Velocity.Y));
    }

    [Fact]
    public void SnappyGymScheduleRemainsExecutableAndDeterministic()
    {
        MovementGymSession a = new(MovementGymTuning.Snappy);
        MovementGymSession b = new(MovementGymTuning.Snappy);
        for (int i = 0; i < 90; ++i)
        {
            HorizontalDirection direction = i < 25 ? HorizontalDirection.Right :
                i < 55 ? HorizontalDirection.Left : HorizontalDirection.Neutral;
            bool jump = i == 5;
            a.Step(MovementGymSession.FixedStepSeconds, direction, jump);
            b.Step(MovementGymSession.FixedStepSeconds, direction, jump);
        }
        Assert.Equal(a.PlayerBounds, b.PlayerBounds);
        Assert.Equal(a.PlayerVelocity, b.PlayerVelocity);
        Assert.Equal(a.JumpsStarted, b.JumpsStarted);
    }
}
