using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.Motion;

public sealed class JumpTravelTrajectoryTests
{
    private static readonly HorizontalMotionConstraints Horizontal = new(10, 0.20, 0.10, 0.35);

    private static JumpFlightTrajectory HeldFlight() =>
        JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3)));

    private static JumpTravelInputChange[] RightOnly() =>
        [new JumpTravelInputChange(0, HorizontalDirection.Right)];

    private static JumpTravelTrajectory Make(
        JumpTravelInputChange[]? schedule = null, double x0 = 0, double vx0 = 0) =>
        JumpTravelSolver.Create(HeldFlight(), HorizontalMotionSolver.Solve(Horizontal),
            schedule ?? RightOnly(), x0, vx0);

    [Fact]
    public void ContinuousRightInputReachesAnalyticalDistance()
    {
        var travel = Make();
        Close(0.7, travel.TotalDurationSeconds);
        Close(6.0, travel.SignedTravelDistance);
        Close(10.0, travel.HorizontalProfile.MaximumSpeed);
        Close(1.0, travel.Evaluate(0.2).HorizontalPosition);
        Close(6.0, travel.Evaluate(0.7).HorizontalPosition);
        Close(10.0, travel.Evaluate(0.7).HorizontalVelocity);
    }

    [Fact]
    public void VerticalFlightRemainsThePreviouslySolvedCurve()
    {
        JumpFlightTrajectory flight = HeldFlight();
        var travel = JumpTravelSolver.Create(flight, HorizontalMotionSolver.Solve(Horizontal), RightOnly());
        foreach (double time in new[] { 0.0, 0.1, 0.2, 0.4, 0.5, 0.7 })
        {
            var expected = flight.Evaluate(time);
            var actual = travel.Evaluate(time);
            Close(expected.VerticalDisplacement, actual.VerticalDisplacement);
            Close(expected.VerticalVelocity, actual.VerticalVelocity);
            Close(expected.VerticalAcceleration, actual.VerticalAcceleration);
        }
    }

    [Fact]
    public void AbsoluteHorizontalPositionAndSignedRelativeDistanceAreDistinct()
    {
        var travel = Make(x0: 100.0);
        Close(100, travel.Evaluate(0).HorizontalPosition);
        Close(106, travel.Evaluate(0.7).HorizontalPosition);
        Close(6, travel.SignedTravelDistance);
    }

    [Fact]
    public void ReleasingDirectionBrakesToRestAtExactEvent()
    {
        var travel = Make([
            new(0.0, HorizontalDirection.Right),
            new(0.3, HorizontalDirection.Neutral)]);

        Close(2.0, travel.Evaluate(0.3).HorizontalPosition);
        Close(10.0, travel.Evaluate(0.3).HorizontalVelocity);
        Close(-100.0, travel.Evaluate(0.3).HorizontalAcceleration);
        Close(2.5, travel.Evaluate(0.4).HorizontalPosition);
        Close(0.0, travel.Evaluate(0.4).HorizontalVelocity);
        Close(2.5, travel.SignedTravelDistance);
    }

    [Fact]
    public void ReversingInputHasContinuousPositionAndVelocity()
    {
        var travel = Make([
            new(0, HorizontalDirection.Right),
            new(0.3, HorizontalDirection.Left)]);
        double t = 0.3;
        var before = travel.Evaluate(t - 1e-8);
        var at = travel.Evaluate(t);
        Assert.InRange(Math.Abs(at.HorizontalPosition - before.HorizontalPosition), 0, 2e-7);
        Assert.InRange(Math.Abs(at.HorizontalVelocity - before.HorizontalVelocity), 0, 2e-6);
        Close(2.0, at.HorizontalPosition);
        Close(10.0, at.HorizontalVelocity);
        Close(-10.0 / 0.15, at.HorizontalAcceleration);
        Close(1.25, travel.SignedTravelDistance);
        Close(-10, travel.Evaluate(0.7).HorizontalVelocity);
    }

    [Fact]
    public void SegmentsUseExactCurrentVelocityWhenInputChangesDuringAcceleration()
    {
        var travel = Make([
            new(0, HorizontalDirection.Right),
            new(0.1, HorizontalDirection.Neutral)]);
        Close(0.25, travel.Evaluate(0.1).HorizontalPosition);
        Close(5.0, travel.Evaluate(0.1).HorizontalVelocity);
        Close(0.375, travel.Evaluate(0.15).HorizontalPosition);
        Close(0.0, travel.Evaluate(0.15).HorizontalVelocity);
        Close(0.375, travel.SignedTravelDistance);
    }

    [Fact]
    public void RedundantSameDirectionEventDoesNotChangeTravel()
    {
        var uninterrupted = Make();
        var redundant = Make([
            new(0, HorizontalDirection.Right),
            new(0.15, HorizontalDirection.Right),
            new(0.4, HorizontalDirection.Right)]);
        foreach (double time in new[] { 0.0, 0.1, 0.15, 0.2, 0.4, 0.6, 0.7 })
        {
            Close(uninterrupted.Evaluate(time).HorizontalPosition, redundant.Evaluate(time).HorizontalPosition);
            Close(uninterrupted.Evaluate(time).HorizontalVelocity, redundant.Evaluate(time).HorizontalVelocity);
        }
    }

    [Fact]
    public void EarlyReleaseShortensReferenceHorizontalTravelWindow()
    {
        var held = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3));
        var released = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0);
        var travel = JumpTravelSolver.Create(released, HorizontalMotionSolver.Solve(Horizontal), RightOnly());

        Close(0.25, travel.TotalDurationSeconds);
        Close(1.5, travel.SignedTravelDistance);
        Close(0.0, travel.Evaluate(0.25).VerticalDisplacement);
        Assert.True(travel.Evaluate(0.25).VerticalVelocity < 0.0);
    }

    [Fact]
    public void ApexHangChangesOnlyVerticalFlightNotInputPolicy()
    {
        var held = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3, 0.1, 0.35));
        var travel = JumpTravelSolver.Create(
            JumpFlightSolver.ForHeldJump(held), HorizontalMotionSolver.Solve(Horizontal), RightOnly());
        Close(6.0, travel.SignedTravelDistance);
        Close(4.0, travel.Evaluate(0.4).VerticalDisplacement);
        Close(0.0, travel.Evaluate(0.4).VerticalVelocity);
    }

    [Fact]
    public void NeutralThroughoutProducesNoHorizontalMovementFromRest()
    {
        var travel = Make([new(0, HorizontalDirection.Neutral)]);
        Close(0, travel.SignedTravelDistance);
        Close(0, travel.Evaluate(0.7).HorizontalVelocity);
    }

    [Fact]
    public void InitialHorizontalVelocityIsPreservedAtTakeoff()
    {
        var travel = Make(vx0: 2.0);
        Close(2.0, travel.Evaluate(0).HorizontalVelocity);
        Close(0.0, travel.Evaluate(0).HorizontalPosition);
    }

    [Fact]
    public void InputScheduleIsSnapshotNotCallerOwnedMutableState()
    {
        var schedule = new[] { new JumpTravelInputChange(0, HorizontalDirection.Right) };
        var travel = Make(schedule);
        schedule[0] = new JumpTravelInputChange(0, HorizontalDirection.Left);
        Close(6.0, travel.SignedTravelDistance);
    }

    [Fact]
    public void SamplingDoesNotAdvanceOrMutateTrajectory()
    {
        var travel = Make([
            new(0, HorizontalDirection.Right),
            new(0.3, HorizontalDirection.Neutral)]);
        var late = travel.Evaluate(0.7);
        _ = travel.Evaluate(0.15);
        _ = travel.Evaluate(0.4);
        Assert.Equal(late, travel.Evaluate(0.7));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.70001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsOutOfFlightEvaluation(double time)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Make().Evaluate(time));
    }

    private static void Close(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-9);
}
