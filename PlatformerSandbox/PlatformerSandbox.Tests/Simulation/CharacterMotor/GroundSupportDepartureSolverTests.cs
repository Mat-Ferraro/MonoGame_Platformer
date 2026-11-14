using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class GroundSupportDepartureSolverTests
{
    private static readonly Aabb Body = new(new Vector2D(0, 0), new Vector2D(1, 1));
    private static StaticAabbCollider Floor(double left, double right, int id = 1,
        double height = 0.0) => new(id,
            new Aabb(new Vector2D(left, height - 1.0), new Vector2D(right, height)));

    private static CharacterMovementController Motor(params StaticAabbCollider[] colliders)
    {
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var flight = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, 0.4, 0.3)));
        return new CharacterMovementController(Body, colliders, ground, air, flight, -20.0);
    }

    private static void Near(double expected, double actual, double tolerance = 1e-7) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, tolerance);

    [Fact]
    public void ConstantVelocityFindsRightHandDepartureTime()
    {
        var result = GroundSupportDepartureSolver.FindFirst(
            Body, 3.0, 0.0, 0.8, [Floor(-10, 1.5)]);
        Assert.True(result.HasValue);
        Near(0.5, result.Value.TimeSeconds);
        Near(1.5, result.Value.BodyLeftAtDeparture);
    }

    [Fact]
    public void AccelerationFromRestFindsAnalyticalRoot()
    {
        var result = GroundSupportDepartureSolver.FindFirst(
            Body, 0.0, 50.0, 0.3, [Floor(-10, 1.5)]);
        Assert.True(result.HasValue);
        Near(Math.Sqrt(0.06), result.Value.TimeSeconds);
    }

    [Fact]
    public void DepartureAtTheExactEndOfIntervalCounts()
    {
        var result = GroundSupportDepartureSolver.FindFirst(
            Body, 5.0, 0.0, 0.3, [Floor(-10, 1.5)]);
        Assert.True(result.HasValue);
        Near(0.3, result.Value.TimeSeconds);
    }

    [Fact]
    public void BrakingToRestBeforeEdgeDoesNotLoseSupport()
    {
        var result = GroundSupportDepartureSolver.FindFirst(
            Body, 2.0, -10.0, 0.2, [Floor(-10, 1.5)]);
        Assert.Null(result);
    }

    [Fact]
    public void TurningAroundCanLeaveByTheOppositeEdge()
    {
        var result = GroundSupportDepartureSolver.FindFirst(
            Body, 2.0, -10.0, 1.5, [Floor(-1, 1.5)]);
        Assert.True(result.HasValue);
        Near(0.2 + Math.Sqrt(0.44), result.Value.TimeSeconds);
        Near(-2.0, result.Value.BodyLeftAtDeparture);
    }

    [Fact]
    public void LeftwardMovementUsesRightSideOfBodyForSupportBoundary()
    {
        var result = GroundSupportDepartureSolver.FindFirst(
            Body, -4.0, 0.0, 1.0, [Floor(-1, 1.5)]);
        Assert.True(result.HasValue);
        Near(0.5, result.Value.TimeSeconds);
        Near(-2.0, result.Value.BodyLeftAtDeparture);
    }

    [Fact]
    public void AdjacentSameHeightFloorsAreOneContinuousSupportRegion()
    {
        Assert.Null(GroundSupportDepartureSolver.FindFirst(
            Body, 10, 0, 0.5, [Floor(-10, 1.5, 1), Floor(1.5, 10, 2)]));
    }

    [Fact]
    public void SmallGapCanBeSpannedByBodyWidth()
    {
        Assert.Null(GroundSupportDepartureSolver.FindFirst(
            Body, 10, 0, 0.5, [Floor(-10, 1.5, 1), Floor(2.0, 10, 2)]));
    }

    [Fact]
    public void GapWiderThanBodyWidthCausesDeparture()
    {
        var result = GroundSupportDepartureSolver.FindFirst(
            Body, 10, 0, 0.5, [Floor(-10, 1.5, 1), Floor(3.0, 10, 2)]);
        Assert.True(result.HasValue);
        Near(0.15, result.Value.TimeSeconds);
    }

    [Fact]
    public void RaisedPlatformCannotCountAsSupportAtCurrentHeight()
    {
        var result = GroundSupportDepartureSolver.FindFirst(
            Body, 10, 0, 0.5, [Floor(-10, 1.5, 1), Floor(1.5, 10, 2, height: 1)]);
        Assert.True(result.HasValue);
        Near(0.15, result.Value.TimeSeconds);
    }

    [Fact]
    public void DetectionIsIndependentOfColliderOrdering()
    {
        var left = Floor(-10, 1.5, 1);
        var right = Floor(1.5, 4, 2);
        var a = GroundSupportDepartureSolver.FindFirst(Body, 10, 0, 0.5, [left, right]);
        var b = GroundSupportDepartureSolver.FindFirst(Body, 10, 0, 0.5, [right, left]);
        Assert.Equal(a, b);
        Assert.True(a.HasValue);
        Near(0.4, a.Value.TimeSeconds);
    }

    [Fact]
    public void ABodyWithoutFloorSupportCannotDepartAgain()
    {
        Assert.Null(GroundSupportDepartureSolver.FindFirst(
            Body.Translated(new Vector2D(0, 1)), 5, 0, 0.5, [Floor(-10, 1.5)]));
    }

    [Fact]
    public void StationaryBodyRemainsSupported()
    {
        Assert.Null(GroundSupportDepartureSolver.FindFirst(
            Body, 0, 0, 1.0, [Floor(-10, 1.5)]));
    }

    [Theory]
    [InlineData(double.NaN, 0, 0.5)]
    [InlineData(double.PositiveInfinity, 0, 0.5)]
    [InlineData(0, double.NegativeInfinity, 0.5)]
    [InlineData(0, 0, -0.1)]
    public void RejectsInvalidMotion(double velocity, double acceleration, double duration)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GroundSupportDepartureSolver.FindFirst(Body, velocity, acceleration,
                duration, [Floor(-10, 1.5)]));
    }

    [Fact]
    public void InvalidRequestDoesNotMutateController()
    {
        var motor = Motor(Floor(-1, 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            motor.Step(-0.1, HorizontalDirection.Right, false));
        Assert.Equal(Body, motor.Bounds);
        Assert.True(motor.IsGrounded);
    }

    [Fact]
    public void FallingBeginsDuringTheSameUpdateAsWalkOff()
    {
        var motor = Motor(Floor(-10, 1.5));
        var result = motor.Step(0.3, HorizontalDirection.Right, false);
        Near(2.0, result.Bounds.Left);
        Near(-0.025, result.Bounds.Bottom);
        Near(-1.0, result.Velocity.Y);
        Assert.False(result.IsGrounded);
    }

    [Fact]
    public void OneLargeUpdateMatchesSmallerUpdatesAcrossEdge()
    {
        var first = Motor(Floor(-10, 1.5));
        var second = Motor(Floor(-10, 1.5));
        var whole = first.Step(0.3, HorizontalDirection.Right, false);
        second.Step(0.2, HorizontalDirection.Right, false);
        var divided = second.Step(0.1, HorizontalDirection.Right, false);
        Near(whole.Bounds.Left, divided.Bounds.Left);
        Near(whole.Bounds.Bottom, divided.Bounds.Bottom);
        Near(whole.Velocity.X, divided.Velocity.X);
        Near(whole.Velocity.Y, divided.Velocity.Y);
    }

    [Fact]
    public void AdjacentPlatformsPreventAnUnintendedFall()
    {
        var motor = Motor(Floor(-10, 1.5, 1), Floor(1.5, 10, 2));
        var result = motor.Step(0.3, HorizontalDirection.Right, false);
        Assert.True(result.IsGrounded);
        Near(0.0, result.Bounds.Bottom);
        Near(0.0, result.Velocity.Y);
    }

    [Fact]
    public void StationaryCharacterNeverDropsFromSupportedLedge()
    {
        var motor = Motor(Floor(-10, 1.5));
        var result = motor.Step(0.4, HorizontalDirection.Neutral, false);
        Assert.True(result.IsGrounded);
        Near(0.0, result.Bounds.Bottom);
    }

    [Fact]
    public void WallBeforeEdgePreventsFalseSupportDeparture()
    {
        var wall = new StaticAabbCollider(2,
            new Aabb(new Vector2D(2, 0), new Vector2D(2.2, 10)));
        var motor = Motor(Floor(-10, 5), wall);
        var result = motor.Step(0.5, HorizontalDirection.Right, false);
        Assert.True(result.IsGrounded);
        Near(1.0, result.Bounds.Left);
        Near(0.0, result.Bounds.Bottom);
    }

    [Fact]
    public void CrossingLedgeMidStepSwitchesToAirAccelerationImmediately()
    {
        var body = Body.Translated(new Vector2D(1.4, 0.0));
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.5, 0.1, 0.7));
        var flight = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, 0.4, 0.3)));
        var motor = new CharacterMovementController(body,
            [Floor(-10, 1.5)], ground, air, flight, -20.0);

        var result = motor.Step(0.2, HorizontalDirection.Right, false);
        double departureTime = Math.Sqrt(2.0 * 0.1 / ground.AccelerationRate);
        double expectedVx = ground.AccelerationRate * departureTime +
            air.AccelerationRate * (0.2 - departureTime);
        Near(expectedVx, result.Velocity.X);
        Near(-10.0 * Math.Pow(0.2 - departureTime, 2), result.Bounds.Bottom);
        Assert.False(result.IsGrounded);
    }

    [Fact]
    public void LowerPlatformCanCatchCharacterWithinWalkOffUpdate()
    {
        var motor = Motor(Floor(-10, 1.5, 1), Floor(2, 10, 2, height: -1));
        var result = motor.Step(0.65, HorizontalDirection.Right, false);
        Assert.True(result.IsGrounded);
        Near(-1.0, result.Bounds.Bottom);
        Near(0.0, result.Velocity.Y);
        Assert.Contains(result.Contacts, c => c.ColliderId == 2 && c.Normal.Y == 1.0);
    }
}
