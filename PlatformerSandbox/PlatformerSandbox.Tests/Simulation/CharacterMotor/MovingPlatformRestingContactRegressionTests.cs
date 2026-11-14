using System.Reflection;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class MovingPlatformRestingContactRegressionTests
{
    private static CharacterMovementController CreateController(MovingPlatformTrajectory platform)
    {
        var body = new Aabb(new Vector2D(0, 0.25), new Vector2D(0.8, 1.85));
        HorizontalMotionProfile horizontal = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        JumpFlightTrajectory jump = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3)));
        return new CharacterMovementController(body, [], horizontal, horizontal,
            jump, -20, movingPlatform: platform);
    }

    private static MovingPlatformTrajectory CreatePlatform() =>
        new(7, new Aabb(new Vector2D(-1, 0), new Vector2D(3, 0.25)),
            new Vector2D(2, 0), 1.0);

    // Test-only reproduction of the transient event-boundary state observed
    // in the debugger: a flush body with support not yet latched. This avoids
    // making a special mutable-state constructor part of the public motor API.
    private static void SetPrivateField(CharacterMovementController motor,
        string fieldName, object value)
    {
        FieldInfo field = typeof(CharacterMovementController).GetField(
            fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing field: {fieldName}");
        field.SetValue(motor, value);
    }

    [Fact]
    public void ZeroTimeRestingTopContactAcquiresSupportInsteadOfRepeating()
    {
        MovingPlatformTrajectory platform = CreatePlatform();
        CharacterMovementController motor = CreateController(platform);
        SetPrivateField(motor, "_onMovingPlatform", false);
        SetPrivateField(motor, "_grounded", false);
        SetPrivateField(motor, "_velocity", new Vector2D(9, 0));

        CharacterControlStepResult result = motor.Step(
            1.0 / 60.0, HorizontalDirection.Right, false);

        Assert.True(result.IsGrounded);
        Assert.Equal(7, motor.MovingSupportId);
        CharacterControlContact contact = Assert.Single(result.Contacts,
            c => c.ColliderId == 7 && c.Normal == new Vector2D(0, 1));
        Assert.Equal(0.0, contact.TimeWithinStepSeconds);
        Assert.Equal(new Vector2D(9, 0), contact.IncomingVelocity);
        Assert.Equal(new Vector2D(9, 0), contact.OutgoingVelocity);
        Assert.Equal(1.0 / 60.0, motor.ElapsedSeconds, 12);
        Assert.False(AabbCollisionDetector.Overlaps(
            motor.Bounds, platform.Evaluate(motor.ElapsedSeconds).Bounds));
    }

    [Fact]
    public void UpwardMotionFromTopDoesNotBecomePlatformSupport()
    {
        MovingPlatformTrajectory platform = CreatePlatform();
        CharacterMovementController motor = CreateController(platform);
        SetPrivateField(motor, "_onMovingPlatform", false);
        SetPrivateField(motor, "_grounded", false);
        SetPrivateField(motor, "_velocity", new Vector2D(2, 3));

        CharacterControlStepResult result = motor.Step(
            1.0 / 60.0, HorizontalDirection.Neutral, false);

        Assert.False(result.IsGrounded);
        Assert.Null(motor.MovingSupportId);
        Assert.True(result.Velocity.Y > 0);
    }
}
