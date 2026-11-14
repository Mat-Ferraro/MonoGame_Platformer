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

public sealed class Milestone24Phase2BLiveSchedulerTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    private static CharacterMovementController NewMotor(
        Aabb body, StaticAabbCollider[] obstacles,
        MovingPlatformTrajectory moving, int maxContactEvents = 8)
    {
        HorizontalMotionProfile ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10.0, 0.2, 0.1, 0.35));
        HorizontalMotionProfile air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10.0, 0.2, 0.1, 0.35));
        JumpFlightTrajectory jump = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(4.0, 0.4, 0.3)));
        return new CharacterMovementController(
            body, obstacles, ground, air, jump, -20.0, maxContactEvents,
            movingPlatform: moving, faultPolicy: SimulationFaultPolicy.Development);
    }

    [Fact]
    public void ControllerUsesBothColliderSetsAfterFirstStaticHit()
    {
        // Accelerate into the static wall first; fall onto a horizontally
        // moving floor later in the SAME fixed step. The old Phase 2A path
        // let its static-only stepper consume the interval after the wall.
        var wall = new StaticAabbCollider(1, Box(2.0, 2.0, 2.3, 5.0));
        var moving = new MovingPlatformTrajectory(7,
            Box(0.2, 0.0, 3.5, 0.25), new Vector2D(0.5, 0), 2.0);
        CharacterMovementController motor = NewMotor(
            Box(0.0, 2.0, 0.8, 3.6), [wall], moving);

        CharacterControlStepResult step = motor.Step(0.5,
            HorizontalDirection.Right, false);
        Assert.Contains(step.Contacts, c =>
            c.ColliderId == 1 && c.Normal == new Vector2D(-1, 0));
        Assert.Contains(step.Contacts, c =>
            c.ColliderId == 7 && c.Normal == new Vector2D(0, 1));
        Assert.Equal(7, motor.MovingSupportId);
    }

    [Fact]
    public void FaultPolicyCanBeChangedWithoutResettingMotorState()
    {
        var moving = new MovingPlatformTrajectory(7,
            Box(10, 2, 12, 2.25), new Vector2D(1, 0), 1.0);
        CharacterMovementController motor = NewMotor(
            Box(0.0, 3, 0.8, 4.6), [], moving);
        motor.Step(0.05, HorizontalDirection.Right, false);
        Aabb bounds = motor.Bounds;
        Vector2D velocity = motor.Velocity;
        double clock = motor.ElapsedSeconds;
        motor.SetFaultPolicy(SimulationFaultPolicy.Release);
        Assert.Equal(SimulationFaultSensitivity.Resilient, motor.FaultSensitivity);
        Assert.Equal(bounds, motor.Bounds);
        Assert.Equal(velocity, motor.Velocity);
        Assert.Equal(clock, motor.ElapsedSeconds);
    }

    [Fact]
    public void GymFaultProfilesAreIndependentOfMovementTuning()
    {
        var gym = new MovementGymSession();
        Assert.Equal(SimulationFaultSensitivity.Strict, gym.FaultSensitivity);
        gym.Step(0.05, HorizontalDirection.Right, false);
        Aabb before = gym.PlayerBounds;
        double time = gym.ElapsedSeconds;
        gym.SetFaultSensitivity(SimulationFaultSensitivity.Diagnostic);
        Assert.Equal(SimulationFaultSensitivity.Diagnostic, gym.FaultSensitivity);
        Assert.Equal(time, gym.ElapsedSeconds);
        Assert.Equal(before, gym.PlayerBounds);
        gym.ApplyTuning(MovementGymTuning.Snappy);
        Assert.Equal(SimulationFaultSensitivity.Diagnostic, gym.FaultSensitivity);
        gym.SetFaultSensitivity(SimulationFaultSensitivity.Resilient);
        gym.Reset();
        Assert.Equal(SimulationFaultSensitivity.Resilient, gym.FaultSensitivity);
        Assert.Equal(MovementGymTuning.Snappy, gym.Tuning);
    }

    [Fact]
    public void InvalidFaultPolicyDoesNotChangeController()
    {
        var moving = new MovingPlatformTrajectory(7,
            Box(10, 2, 12, 2.25), new Vector2D(1, 0), 1.0);
        CharacterMovementController motor = NewMotor(
            Box(0, 3, 0.8, 4.6), [], moving);
        Assert.Throws<ArgumentNullException>(() => motor.SetFaultPolicy(null!));
        Assert.Equal(SimulationFaultSensitivity.Strict, motor.FaultSensitivity);
    }

    [Fact]
    public void MovingFloorRestingContactIsAOneTimeHandoff()
    {
        var moving = new MovingPlatformTrajectory(7,
            Box(-1, 0, 3, 0.25), new Vector2D(1, 0), 2.0);
        var result = UnifiedAcceleratedKinematicStepper.Step(
            Box(0, 0.25, 0.8, 1.85), new Vector2D(0, 0),
            new Vector2D(0, -20), 0, 0.15,
            Array.Empty<StaticAabbCollider>(), [moving]);
        // A top contact must not repeat eight times at the same t=0.
        Assert.Equal(7, result.MovingFloorId);
        Assert.Single(result.Contacts);
        Assert.Equal(0.0, result.AdvancedSeconds);
    }
}
