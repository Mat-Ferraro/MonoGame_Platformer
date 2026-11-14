using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class Milestone24Phase2CIntegrationTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    private static CharacterMovementController MakeMotor(
        IReadOnlyList<StaticAabbCollider>? obstacles = null,
        MovingPlatformTrajectory? movingPlatform = null)
    {
        HorizontalMotionProfile ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9, 0.18, 0.13, 0.38));
        HorizontalMotionProfile air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9, 0.22, 0.16, 0.43));
        JumpFlightTrajectory flight = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        return new CharacterMovementController(Box(0, 0, 0.8, 1.6),
            obstacles ?? [new StaticAabbCollider(1, Box(-10, -1, 10, 0))],
            ground, air, flight, -24, movingPlatform: movingPlatform);
    }

    [Theory]
    [InlineData(SimulationFaultSensitivity.Strict, "FAULT: STRICT")]
    [InlineData(SimulationFaultSensitivity.Diagnostic, "FAULT: DIAGNOSTIC")]
    [InlineData(SimulationFaultSensitivity.Resilient, "FAULT: RESILIENT")]
    public void FaultModeHudLabelIsExplicitAndStable(
        SimulationFaultSensitivity sensitivity, string expected)
    {
        Assert.Equal(expected, MovementGymFaultModeLabel.For(sensitivity));
    }

    [Fact]
    public void InvalidFaultModeDoesNotHaveMisleadingHudLabel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovementGymFaultModeLabel.For((SimulationFaultSensitivity)99));
    }

    [Fact]
    public void GymModeSelectionSurvivesPresetChangesAndReset()
    {
        MovementGymSession gym = new();
        Assert.Equal("FAULT: STRICT", MovementGymFaultModeLabel.For(gym.FaultSensitivity));
        gym.SetFaultSensitivity(SimulationFaultSensitivity.Diagnostic);
        Assert.Equal("FAULT: DIAGNOSTIC", MovementGymFaultModeLabel.For(gym.FaultSensitivity));
        gym.ApplyTuning(MovementGymTuning.Snappy);
        Assert.Equal("FAULT: DIAGNOSTIC", MovementGymFaultModeLabel.For(gym.FaultSensitivity));
        gym.SetFaultSensitivity(SimulationFaultSensitivity.Resilient);
        gym.Reset();
        Assert.Equal("FAULT: RESILIENT", MovementGymFaultModeLabel.For(gym.FaultSensitivity));
    }

    [Fact]
    public void UnobstructedHeldJumpIsStableAcrossFixedUpdatePartitions()
    {
        CharacterMovementController a = MakeMotor();
        CharacterMovementController b = MakeMotor();
        a.Step(0.3, HorizontalDirection.Right, true);
        for (int i = 0; i < 30; i++)
            b.Step(0.01, HorizontalDirection.Right, i == 0);
        Assert.InRange(Math.Abs(a.Bounds.Left - b.Bounds.Left), 0, 1e-7);
        Assert.InRange(Math.Abs(a.Bounds.Bottom - b.Bounds.Bottom), 0, 1e-7);
        Assert.InRange(Math.Abs(a.Velocity.X - b.Velocity.X), 0, 1e-7);
        Assert.InRange(Math.Abs(a.Velocity.Y - b.Velocity.Y), 0, 1e-7);
    }

    [Fact]
    public void WallContactDoesNotChangeDesignerVerticalJumpPath()
    {
        var wall = new StaticAabbCollider(2, Box(0.8, 0, 1.4, 10));
        var floor = new StaticAabbCollider(1, Box(-10, -1, 10, 0));
        CharacterMovementController wallJump = MakeMotor([floor, wall]);
        CharacterMovementController freeJump = MakeMotor([floor]);
        for (int i = 0; i < 15; i++)
        {
            var contactStep = wallJump.Step(1.0 / 60.0, HorizontalDirection.Right, i == 0);
            freeJump.Step(1.0 / 60.0, HorizontalDirection.Right, i == 0);
            Assert.InRange(Math.Abs(contactStep.Bounds.Bottom - freeJump.Bounds.Bottom), 0, 1e-8);
            Assert.InRange(Math.Abs(contactStep.Velocity.Y - freeJump.Velocity.Y), 0, 1e-8);
        }
    }

    [Fact]
    public void LaunchFromMovingFloorUsesUnifiedAnalyticalQueryWithoutLosingJump()
    {
        var movingFloor = new MovingPlatformTrajectory(7,
            Box(-1, 0, 3, 0.25), new Vector2D(2, 0), 1.0);
        // The body starts supported, with the platform's world velocity.
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9, 0.18, 0.13, 0.38));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9, 0.22, 0.16, 0.43));
        var jump = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(3.7, 0.38, 0.34)));
        var motor = new CharacterMovementController(
            Box(0, 0.25, 0.8, 1.85), [], ground, air, jump, -24,
            movingPlatform: movingFloor);
        Assert.Equal(7, motor.MovingSupportId);
        var first = motor.Step(1.0 / 60.0, HorizontalDirection.Neutral, true);
        Assert.True(first.JumpStarted);
        Assert.True(first.IsJumping);
        Assert.Null(motor.MovingSupportId);
        Assert.True(motor.Velocity.Y > 0);
        for (int frame = 0; frame < 10; frame++)
            motor.Step(1.0 / 60.0, HorizontalDirection.Neutral, false);
        Assert.True(motor.IsJumping);
        Assert.True(motor.Bounds.Bottom > 0.25);
        Assert.False(AabbCollisionDetector.Overlaps(
            motor.Bounds, movingFloor.Evaluate(motor.ElapsedSeconds).Bounds));
    }

    [Fact]
    public void InvalidStepDoesNotAdvanceRunningJumpOrSimulationClock()
    {
        CharacterMovementController motor = MakeMotor();
        motor.Step(0.05, HorizontalDirection.Right, true);
        Aabb bounds = motor.Bounds;
        Vector2D velocity = motor.Velocity;
        double clock = motor.ElapsedSeconds;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            motor.Step(0.1, (HorizontalDirection)12345, false));
        Assert.Equal(clock, motor.ElapsedSeconds);
        Assert.Equal(bounds, motor.Bounds);
        Assert.Equal(velocity, motor.Velocity);
        Assert.True(motor.IsJumping);
    }
}
