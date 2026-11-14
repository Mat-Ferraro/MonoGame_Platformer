using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class AuthorizedGraceJumpTests
{
    private static CharacterMovementController AirborneMotor()
    {
        var body = new Aabb(new Vector2D(0, 1.5), new Vector2D(1, 2.5));
        var floor = new StaticAabbCollider(1,
            new Aabb(new Vector2D(-100, -1), new Vector2D(100, 0)));
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, 0.4, 0.3)));
        return new CharacterMovementController(body, [floor], ground, air, jump, -20);
    }

    [Fact]
    public void AirbornePressWithoutGameplayAuthorizationDoesNothing()
    {
        var motor = AirborneMotor();
        var state = motor.Step(0.01, HorizontalDirection.Neutral, true);
        Assert.False(state.JumpStarted);
        Assert.False(motor.IsJumping);
    }

    [Fact]
    public void ExplicitlyAuthorizedGracePressStartsJumpFromCurrentLocation()
    {
        var motor = AirborneMotor();
        var startY = motor.Bounds.Bottom;
        var state = motor.Step(0.01, HorizontalDirection.Neutral, true,
            jumpGraceAuthorized: true);
        Assert.True(state.JumpStarted);
        Assert.True(motor.IsJumping);
        Assert.True(motor.Bounds.Bottom > startY);
        Assert.Equal(1, motor.JumpsStarted);
    }

    [Fact]
    public void AuthorizationAloneCannotStartAJumpWithoutPress()
    {
        var motor = AirborneMotor();
        var state = motor.Step(0.01, HorizontalDirection.Neutral, false,
            jumpGraceAuthorized: true);
        Assert.False(state.JumpStarted);
    }

    [Fact]
    public void AuthorizationCannotRestartAnActiveJump()
    {
        var motor = AirborneMotor();
        motor.Step(0.01, HorizontalDirection.Neutral, true,
            jumpGraceAuthorized: true);
        var state = motor.Step(0.01, HorizontalDirection.Neutral, true,
            jumpGraceAuthorized: true);
        Assert.False(state.JumpStarted);
        Assert.Equal(1, motor.JumpsStarted);
    }
}
