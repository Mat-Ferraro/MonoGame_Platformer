using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>
/// Horizontal pinches are gameplay outcomes whether the motor is in its
/// ordinary-motion or jumping branch. Wall contacts must remain repeatable
/// without becoming same-timestamp collision loops.
/// </summary>
public sealed class HorizontalCrushJumpRegressionTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    private static HorizontalMotionProfile AirMotion() =>
        HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.22, 0.16, 0.43));

    private static JumpFlightTrajectory Jump(double height, double rise, double fall) =>
        JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(height, rise, fall)));

    [Fact]
    public void AirbornePinchBetweenMovingSideAndWallIsClassifiedWithoutPartialCommit()
    {
        Aabb start = Box(0.0, 0.0, 0.8, 1.6);
        var wall = new StaticAabbCollider(12, Box(1.0, -1.0, 1.4, 3.0));
        var pusher = new MovingPlatformTrajectory(7,
            Box(-0.8, -0.25, -0.2, 1.9), new Vector2D(2.0, 0.0), 1.0);
        var motor = new SteerableJumpMotor(start, 0.0, AirMotion(),
            Jump(0.6, 1.0, 1.0), [wall], -24.0, movingPlatform: pusher);

        CharacterCrushException crushed = Assert.Throws<CharacterCrushException>(
            () => motor.Step(0.25, HorizontalDirection.Neutral));

        Assert.Equal(CharacterCrushAxis.Horizontal, crushed.Crush.Axis);
        Assert.Equal(7, crushed.Crush.PushingColliderId);
        Assert.Equal(12, crushed.Crush.OpposingColliderId);
        Assert.Equal(start, motor.Bounds);
        Assert.Equal(0.0, motor.FlightElapsedSeconds);
    }

    [Fact]
    public void AlreadyPinnedJumpReportsCrushInsteadOfOpposingZeroTimeContacts()
    {
        // At a fixed-update boundary the two side faces are exactly flush
        // with the body. The left platform is still advancing to the right;
        // the wall prevents any further horizontal displacement.
        Aabb start = Box(0.0, 0.0, 0.8, 1.6);
        var wall = new StaticAabbCollider(12, Box(0.8, -1.0, 1.2, 3.0));
        var pusher = new MovingPlatformTrajectory(7,
            Box(-0.8, -0.25, 0.0, 1.9), new Vector2D(0.5, 0.0), 1.0);
        var motor = new SteerableJumpMotor(start, 0.0, AirMotion(),
            Jump(0.6, 1.0, 1.0), [wall], -24.0, movingPlatform: pusher);

        CharacterCrushException crushed = Assert.Throws<CharacterCrushException>(
            () => motor.Step(1.0 / 60.0, HorizontalDirection.Neutral));
        Assert.Equal(CharacterCrushAxis.Horizontal, crushed.Crush.Axis);
        Assert.Equal(7, crushed.Crush.PushingColliderId);
        Assert.Equal(12, crushed.Crush.OpposingColliderId);
        Assert.Equal(start, motor.Bounds);
    }

    [Fact]
    public void JumpThatClearsMovingSideVerticallyDoesNotBecomeAFalseCrush()
    {
        Aabb start = Box(0.0, 0.0, 0.8, 1.6);
        var wall = new StaticAabbCollider(12, Box(1.0, -1.0, 1.4, 3.0));
        var lowPusher = new MovingPlatformTrajectory(7,
            Box(-0.8, -0.25, -0.2, 0.15), new Vector2D(2.0, 0.0), 1.0);
        var motor = new SteerableJumpMotor(start, 0.0, AirMotion(),
            Jump(4.0, 0.4, 0.3), [wall], -24.0, movingPlatform: lowPusher);

        SteerableJumpStepResult result = motor.Step(0.25, HorizontalDirection.Neutral);
        Assert.False(result.IsGrounded);
        Assert.True(result.Bounds.Bottom > lowPusher.Evaluate(0.25).Bounds.Top);
    }

    [Fact]
    public void JumpFromHorizontalPlatformNextToWallDoesNotRepeatZeroTimeContacts()
    {
        // Same right-hand platform and crush wall as the combined gym. The
        // jumper is standing on the moving top at the right endpoint, flush
        // against wall 12, and requests rightward acceleration during takeoff.
        var platform = new MovingPlatformTrajectory(7,
            Box(5.3, 2.05, 7.35, 2.30), new Vector2D(2.4, 0.0), 2.4);
        var wall = new StaticAabbCollider(12, Box(10.35, 0.0, 10.75, 3.6));
        Aabb start = Box(9.55, 2.30, 10.35, 3.90);
        var motor = new SteerableJumpMotor(start, 2.4, AirMotion(),
            Jump(3.7, 0.38, 0.34), [wall], -24.0,
            movingPlatform: platform, startTimeSeconds: 2.4);

        bool clearedWall = false;
        bool movedOverWall = false;
        for (int i = 0; i < 8; i++)
        {
            SteerableJumpStepResult result = motor.Step(1.0 / 60.0,
                HorizontalDirection.Right);
            Assert.InRange(result.Contacts.Count, 0, 8);

            // The player's feet clear the wall top during this eight-frame
            // jump. Constrain X only while the body still overlaps the wall
            // vertically; once above it, moving right is legitimate.
            if (result.Bounds.Bottom < wall.Bounds.Top &&
                result.Bounds.Top > wall.Bounds.Bottom)
            {
                Assert.True(result.Bounds.Right <= wall.Bounds.Left,
                    $"Frame {i + 1}: jumped body entered wall's horizontal span " +
                    $"before clearing its top. Bounds={result.Bounds}");
            }
            else if (result.Bounds.Bottom >= wall.Bounds.Top)
            {
                clearedWall = true;
                if (result.Bounds.Right > wall.Bounds.Left)
                    movedOverWall = true;
            }

            Assert.True(result.Bounds.Bottom > start.Bottom);
            Assert.False(AabbCollisionDetector.Overlaps(result.Bounds, wall.Bounds));
        }

        Assert.True(clearedWall, "The jump should clear the 3.60-high wall.");
        Assert.True(movedOverWall,
            "Horizontal input should work again after clearing the wall top.");
    }

    [Fact]
    public void AnalyticalHorizontalWallHitIsCanonicalizedToExactFace()
    {
        var wall = new StaticAabbCollider(12, Box(1.0, -1.0, 1.4, 5.0));
        var motor = new SteerableJumpMotor(
            Box(0.0, 0.0, 0.8, 1.6), 0.0, AirMotion(),
            Jump(3.7, 0.38, 0.34), [wall], -24.0);

        SteerableJumpStepResult hit = motor.Step(0.12, HorizontalDirection.Right);
        Assert.Contains(hit.Contacts, c => c.ColliderId == 12 &&
            c.Normal == new Vector2D(-1.0, 0.0));
        Assert.Equal(wall.Bounds.Left, hit.Bounds.Right);

        for (int i = 0; i < 5; ++i)
        {
            SteerableJumpStepResult next = motor.Step(1.0 / 60.0,
                HorizontalDirection.Right);
            Assert.Equal(wall.Bounds.Left, next.Bounds.Right);
            Assert.False(AabbCollisionDetector.Overlaps(next.Bounds, wall.Bounds));
        }
    }
}
