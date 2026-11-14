using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.Motion;

public sealed class JumpTravelSolverTests
{
    private static JumpFlightTrajectory HeldFlight() =>
        JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3)));

    private static HorizontalMotionProfile Horizontal() =>
        HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));

    private static readonly JumpTravelDistanceConstraints RightSix = new(
        SignedTargetDistance: 6.0,
        TimeToMaximumSpeedSeconds: 0.2,
        TimeToStopSeconds: 0.1,
        TimeToReverseSeconds: 0.35);

    private static JumpTravelInputChange[] Right() => [new(0, HorizontalDirection.Right)];
    private static JumpTravelInputChange[] Left() => [new(0, HorizontalDirection.Left)];

    [Fact]
    public void SolvesMaximumSpeedForRightwardJumpDistance()
    {
        var solved = JumpTravelSolver.SolveForDistance(HeldFlight(), RightSix, Right());
        Close(10, solved.HorizontalProfile.MaximumSpeed);
        Close(6, solved.SignedTravelDistance);
        Close(6, solved.Evaluate(0.7).HorizontalPosition);
    }

    [Fact]
    public void SolvesNegativeDistanceWhenReferenceInputGoesLeft()
    {
        var solved = JumpTravelSolver.SolveForDistance(
            HeldFlight(), RightSix with { SignedTargetDistance = -6 }, Left());
        Close(10, solved.HorizontalProfile.MaximumSpeed);
        Close(-6, solved.SignedTravelDistance);
        Close(-10, solved.Evaluate(0.7).HorizontalVelocity);
    }

    [Fact]
    public void SolvesTargetDistanceAfterNeutralInputChange()
    {
        var solved = JumpTravelSolver.SolveForDistance(HeldFlight(),
            RightSix with { SignedTargetDistance = 5.0 },
            [new(0, HorizontalDirection.Right), new(0.3, HorizontalDirection.Neutral)]);
        Close(20, solved.HorizontalProfile.MaximumSpeed);
        Close(5, solved.SignedTravelDistance);
    }

    [Fact]
    public void SolvesDistanceWithAnInputReversal()
    {
        var solved = JumpTravelSolver.SolveForDistance(HeldFlight(),
            RightSix with { SignedTargetDistance = 2.5 },
            [new(0, HorizontalDirection.Right), new(0.3, HorizontalDirection.Left)]);
        Close(20, solved.HorizontalProfile.MaximumSpeed);
        Close(2.5, solved.SignedTravelDistance);
        Close(-20, solved.Evaluate(0.7).HorizontalVelocity);
    }

    [Fact]
    public void SolvesDistanceForEarlyReleasedFlightDuration()
    {
        var held = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3));
        var released = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0);
        var solved = JumpTravelSolver.SolveForDistance(released,
            RightSix with { SignedTargetDistance = 3.0 }, Right());
        Close(0.25, solved.TotalDurationSeconds);
        Close(20.0, solved.HorizontalProfile.MaximumSpeed);
        Close(3.0, solved.SignedTravelDistance);
    }

    [Fact]
    public void NormalizedStartingVelocityPreservesExactSpeedScaling()
    {
        var solved = JumpTravelSolver.SolveForDistance(HeldFlight(),
            RightSix with
            {
                SignedTargetDistance = 3.75,
                InitialVelocityFractionOfMaximumSpeed = -1
            },
            Right());
        Close(10, solved.HorizontalProfile.MaximumSpeed);
        Close(-10, solved.Evaluate(0).HorizontalVelocity);
        Close(3.75, solved.SignedTravelDistance);
    }

    [Fact]
    public void StartingXPositionDoesNotChangeSolvedDistanceOrSpeed()
    {
        var solved = JumpTravelSolver.SolveForDistance(HeldFlight(), RightSix, Right(), 100.0);
        Close(10, solved.HorizontalProfile.MaximumSpeed);
        Close(6, solved.SignedTravelDistance);
        Close(106, solved.Evaluate(0.7).HorizontalPosition);
    }

    [Fact]
    public void RejectsTravelDistanceWhoseSignConflictsWithInputSchedule()
    {
        Assert.Throws<ArgumentException>(() => JumpTravelSolver.SolveForDistance(
            HeldFlight(), RightSix with { SignedTargetDistance = -6 }, Right()));
    }

    [Fact]
    public void RejectsNonzeroTargetIfNeutralInputCannotProduceTravel()
    {
        Assert.Throws<ArgumentException>(() => JumpTravelSolver.SolveForDistance(
            HeldFlight(), RightSix, [new(0, HorizontalDirection.Neutral)]));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsZeroOrNonfiniteTargetDistance(double target)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JumpTravelSolver.SolveForDistance(
            HeldFlight(), RightSix with { SignedTargetDistance = target }, Right()));
    }

    [Theory]
    [InlineData(-1.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInitialVelocityFractionsOutsideClosedUnitRange(double fraction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JumpTravelSolver.SolveForDistance(
            HeldFlight(), RightSix with { InitialVelocityFractionOfMaximumSpeed = fraction }, Right()));
    }

    [Fact]
    public void RejectsUnsupportedHorizontalTimingConstraints()
    {
        Assert.ThrowsAny<ArgumentException>(() => JumpTravelSolver.SolveForDistance(
            HeldFlight(), RightSix with { TimeToReverseSeconds = 0.1 }, Right()));
    }

    [Fact]
    public void RejectsMissingOrEmptyReferenceInputSchedule()
    {
        Assert.Throws<ArgumentNullException>(() => JumpTravelSolver.Create(
            HeldFlight(), Horizontal(), null!));
        Assert.Throws<ArgumentException>(() => JumpTravelSolver.Create(
            HeldFlight(), Horizontal(), Array.Empty<JumpTravelInputChange>()));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.7)]
    [InlineData(0.8)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidEventTimes(double eventTime)
    {
        Assert.Throws<ArgumentException>(() => JumpTravelSolver.Create(
            HeldFlight(), Horizontal(),
            [new(0, HorizontalDirection.Right), new(eventTime, HorizontalDirection.Left)]));
    }

    [Fact]
    public void RejectsInputSchedulesNotStartingAtTakeoff()
    {
        Assert.Throws<ArgumentException>(() => JumpTravelSolver.Create(
            HeldFlight(), Horizontal(), [new(0.1, HorizontalDirection.Right)]));
    }

    [Fact]
    public void RejectsDuplicateOrDecreasingInputTimes()
    {
        Assert.Throws<ArgumentException>(() => JumpTravelSolver.Create(
            HeldFlight(), Horizontal(),
            [new(0, HorizontalDirection.Right), new(0.2, HorizontalDirection.Neutral),
             new(0.2, HorizontalDirection.Left)]));
        Assert.Throws<ArgumentException>(() => JumpTravelSolver.Create(
            HeldFlight(), Horizontal(),
            [new(0, HorizontalDirection.Right), new(0.4, HorizontalDirection.Neutral),
             new(0.2, HorizontalDirection.Left)]));
    }

    [Fact]
    public void RejectsUnknownDirection()
    {
        Assert.Throws<ArgumentException>(() => JumpTravelSolver.Create(
            HeldFlight(), Horizontal(), [new(0, (HorizontalDirection)42)]));
    }

    [Fact]
    public void RejectsNullVerticalAndHorizontalProfiles()
    {
        Assert.Throws<ArgumentNullException>(() => JumpTravelSolver.Create(null!, Horizontal(), Right()));
        Assert.Throws<ArgumentNullException>(() => JumpTravelSolver.Create(HeldFlight(), null!, Right()));
        Assert.Throws<ArgumentNullException>(() => JumpTravelSolver.SolveForDistance(null!, RightSix, Right()));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsNonfiniteInitialHorizontalState(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JumpTravelSolver.Create(
            HeldFlight(), Horizontal(), Right(), initialHorizontalPosition: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => JumpTravelSolver.Create(
            HeldFlight(), Horizontal(), Right(), initialHorizontalVelocity: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => JumpTravelSolver.SolveForDistance(
            HeldFlight(), RightSix, Right(), initialHorizontalPosition: value));
    }

    [Fact]
    public void RejectsDistanceWhenWorldOriginErasesSmallTravelThroughPrecisionLoss()
    {
        Assert.Throws<InvalidOperationException>(() => JumpTravelSolver.SolveForDistance(
            HeldFlight(), RightSix, Right(), initialHorizontalPosition: 1e300));
    }

    [Fact]
    public void RejectsNonfiniteSolvedSpeedFromOverflowedDistance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JumpTravelSolver.SolveForDistance(
            HeldFlight(), RightSix with { SignedTargetDistance = double.MaxValue }, Right()));
    }

    private static void Close(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-9);
}
