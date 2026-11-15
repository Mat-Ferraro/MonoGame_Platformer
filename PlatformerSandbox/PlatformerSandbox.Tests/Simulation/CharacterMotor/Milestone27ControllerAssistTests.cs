using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Gameplay.Movement.Gyms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class Milestone27ControllerAssistTests
{
    [Fact]
    public void GameplaySwitchResetsButPreservesPresetsAndFaultSensitivity()
    {
        var gym = new MovementGymSession();
        Assert.False(gym.AssistancePolicy.CeilingCornersEnabled);
        gym.Step(0.05, HorizontalDirection.Right, false);
        gym.SetAssistancePolicy(new CollisionAssistancePolicy(0.18));
        Assert.True(gym.AssistancePolicy.CeilingCornersEnabled);
        Assert.Equal(0.0, gym.ElapsedSeconds);
        Assert.Equal(0, gym.TotalAssists);
        gym.Reset();
        Assert.True(gym.AssistancePolicy.CeilingCornersEnabled);
        gym.ApplyTuning(MovementGymTuning.Responsive);
        Assert.True(gym.AssistancePolicy.CeilingCornersEnabled);
        gym.SetAssistancePolicy(CollisionAssistancePolicy.Disabled);
        Assert.False(gym.AssistancePolicy.CeilingCornersEnabled);
    }

    [Fact]
    public void ControllerReportsAssistsAfterSuccessfulJumpStep()
    {
        static Aabb Box(double l, double b, double r, double t) =>
            new(new Vector2D(l, b), new Vector2D(r, t));
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9, 0.22, 0.16, 0.43));
        var flight = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(2.0, 0.4, 0.3)));
        var ceiling = new StaticAabbCollider(20, Box(0.65, 1.8, 3.0, 2.2));
        var floor = new StaticAabbCollider(1, Box(-10, -1, 10, 0));
        var player = Box(0, 0, 0.8, 1.6);
        var controller = new CharacterMovementController(player,
            [floor, ceiling], ground, ground, flight, -24.0,
            assistancePolicy: new CollisionAssistancePolicy(0.18));
        var result = controller.Step(0.08, HorizontalDirection.Left, true);
        Assert.True(result.JumpStarted);
        Assert.Single(result.Assists);
        Assert.Single(controller.LastStepAssists);
        Assert.Equal(result.Assists[0], controller.LastStepAssists[0]);
        Assert.True(controller.Bounds.Top > ceiling.Bounds.Bottom);
        Assert.False(AabbCollisionDetector.Overlaps(controller.Bounds, ceiling.Bounds));
        var zeroTime = controller.Step(0.0, HorizontalDirection.Neutral, false);
        Assert.Equal(result.Bounds, zeroTime.Bounds);
    }
}
