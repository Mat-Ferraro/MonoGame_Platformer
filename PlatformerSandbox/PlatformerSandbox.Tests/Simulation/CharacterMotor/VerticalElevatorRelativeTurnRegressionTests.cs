using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>
/// End-to-end jump motor regression for the relative-turn fault observed when
/// jumping towards a rising lift in the playable gym.
/// </summary>
public sealed class VerticalElevatorRelativeTurnRegressionTests
{
    [Fact]
    public void JumpBesideRisingElevatorCrossesRelativeVerticalTurnWithoutThrowing()
    {
        var lift = new MovingPlatformTrajectory(7,
            new Aabb(new Vector2D(2, 0), new Vector2D(3, .25)),
            new Vector2D(0, 3), 2.0);
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, .2, .1, .35));
        var flight = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, .38, .34,
                ApexRegionFraction: .10, TimeNearApexSeconds: .32)));
        var jump = new SteerableJumpMotor(
            new Aabb(new Vector2D(0, 0), new Vector2D(.8, 1.6)),
            0.0, air, flight, [], gravityAcceleration: -24.0,
            movingPlatform: lift);

        // The body is laterally separated, but the scheduler still has to
        // handle the relative vy zero before deciding there is no impact.
        for (int frame = 0; frame < 48; ++frame)
        {
            var step = jump.Step(1.0 / 60.0, HorizontalDirection.Neutral);
            Assert.True(step.Bounds.IsValid);
            Assert.False(AabbCollisionDetector.Overlaps(
                step.Bounds, lift.Evaluate((frame + 1) / 60.0).Bounds));
        }
    }

    [Fact]
    public void LargerAnalyticalJumpIntervalMayContainRelativeElevatorTurn()
    {
        var lift = new MovingPlatformTrajectory(7,
            new Aabb(new Vector2D(2, 0), new Vector2D(3, .25)),
            new Vector2D(0, 3), 2.0);
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, .2, .1, .35));
        var flight = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, .38, .34,
                ApexRegionFraction: .10, TimeNearApexSeconds: .32)));
        var jump = new SteerableJumpMotor(
            new Aabb(new Vector2D(0, 0), new Vector2D(.8, 1.6)),
            0.0, air, flight, [], gravityAcceleration: -24.0,
            movingPlatform: lift);

        var step = jump.Step(.4, HorizontalDirection.Neutral);
        Assert.True(step.Bounds.IsValid);
        Assert.False(AabbCollisionDetector.Overlaps(
            step.Bounds, lift.Evaluate(.4).Bounds));
    }
}
