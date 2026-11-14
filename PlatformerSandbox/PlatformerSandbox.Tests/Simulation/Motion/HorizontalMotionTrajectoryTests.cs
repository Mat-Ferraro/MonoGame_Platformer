using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.Motion;

public sealed class HorizontalMotionTrajectoryTests
{
    private static readonly HorizontalMotionProfile Profile = HorizontalMotionSolver.Solve(
        new HorizontalMotionConstraints(10.0, 0.20, 0.10, 0.35));

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 50.0)]
    [InlineData(0.10, 0.25, 5.0, 50.0)]
    [InlineData(0.20, 1.0, 10.0, 0.0)]
    [InlineData(0.50, 4.0, 10.0, 0.0)]
    public void RightInputAcceleratesThenCruises(
        double time, double position, double velocity, double acceleration)
    {
        var trajectory = Profile.CreateTrajectory(0.0, 0.0, HorizontalDirection.Right);
        var result = trajectory.Evaluate(time);
        Assert.Equal(position, result.Position, 10);
        Assert.Equal(velocity, result.Velocity, 10);
        Assert.Equal(acceleration, result.Acceleration, 10);
    }

    [Theory]
    [InlineData(0.10, -0.25, -5.0)]
    [InlineData(0.20, -1.0, -10.0)]
    [InlineData(0.50, -4.0, -10.0)]
    public void LeftInputIsMirrorOfRight(double time, double position, double velocity)
    {
        var result = Profile.CreateTrajectory(0.0, 0.0, HorizontalDirection.Left).Evaluate(time);
        Assert.Equal(position, result.Position, 10);
        Assert.Equal(velocity, result.Velocity, 10);
    }

    [Theory]
    [InlineData(0.05, 0.375, 5.0, -100.0)]
    [InlineData(0.10, 0.5, 0.0, 0.0)]
    [InlineData(0.30, 0.5, 0.0, 0.0)]
    public void NeutralInputBrakesAndStopsWithoutCrossingZero(
        double time, double position, double velocity, double acceleration)
    {
        var sample = Profile.CreateTrajectory(0.0, 10.0, HorizontalDirection.Neutral).Evaluate(time);
        Assert.Equal(position, sample.Position, 10);
        Assert.Equal(velocity, sample.Velocity, 10);
        Assert.Equal(acceleration, sample.Acceleration, 10);
    }

    [Fact]
    public void ReversalTimeMeansFullSpeedToFullOppositeSpeed()
    {
        var trajectory = Profile.CreateTrajectory(0.0, 10.0, HorizontalDirection.Left);
        Assert.Equal(0.35, trajectory.TimeToCruiseSeconds, 10);

        var stopped = trajectory.Evaluate(0.15);
        Assert.Equal(0.75, stopped.Position, 10);
        Assert.Equal(0.0, stopped.Velocity, 10);

        var reversed = trajectory.Evaluate(0.35);
        Assert.Equal(-0.25, reversed.Position, 10);
        Assert.Equal(-10.0, reversed.Velocity, 10);
        Assert.Equal(0.0, reversed.Acceleration, 10);
    }

    [Fact]
    public void ReversalFromPartialSpeedWorks()
    {
        var trajectory = Profile.CreateTrajectory(0.0, -5.0, HorizontalDirection.Right);
        Assert.Equal(0.275, trajectory.TimeToCruiseSeconds, 10);
        var reversed = trajectory.Evaluate(0.275);
        Assert.Equal(0.8125, reversed.Position, 10);
        Assert.Equal(10.0, reversed.Velocity, 10);
    }

    [Fact]
    public void InitialOverspeedIsDeceleratedWithoutInstantVelocitySnap()
    {
        var trajectory = Profile.CreateTrajectory(0.0, 15.0, HorizontalDirection.Right);
        var atStart = trajectory.Evaluate(0.0);
        var afterRecovery = trajectory.Evaluate(0.05);
        var later = trajectory.Evaluate(0.10);

        Assert.Equal(15.0, atStart.Velocity, 10);
        Assert.Equal(10.0, afterRecovery.Velocity, 10);
        Assert.Equal(0.625, afterRecovery.Position, 10);
        Assert.Equal(1.125, later.Position, 10);
    }

    [Fact]
    public void StartingWithNoInputAndNoSpeedRemainsStill()
    {
        var trajectory = Profile.CreateTrajectory(2.0, 0.0, HorizontalDirection.Neutral);
        var later = trajectory.Evaluate(120.0);
        Assert.Equal(2.0, later.Position, 10);
        Assert.Equal(0.0, later.Velocity, 10);
        Assert.Equal(0.0, later.Acceleration, 10);
    }

    [Theory]
    [InlineData(0.05, 0.20)]
    [InlineData(0.15, 0.25)]
    [InlineData(0.30, 0.50)]
    public void SplittingAnUninterruptedIntervalPreservesPositionAndVelocity(
        double firstDuration, double totalDuration)
    {
        var continuous = Profile.CreateTrajectory(2.0, 10.0, HorizontalDirection.Left);
        var mid = continuous.Evaluate(firstDuration);
        var afterResuming = Profile.CreateTrajectory(
            mid.Position, mid.Velocity, HorizontalDirection.Left)
            .Evaluate(totalDuration - firstDuration);
        var direct = continuous.Evaluate(totalDuration);

        Assert.Equal(direct.Position, afterResuming.Position, 9);
        Assert.Equal(direct.Velocity, afterResuming.Velocity, 9);
    }

    [Fact]
    public void InputChangeStartsNewProfileContinuously()
    {
        var forward = Profile.CreateTrajectory(0.0, 0.0, HorizontalDirection.Right);
        var whenReversed = forward.Evaluate(0.10);
        var backward = Profile.CreateTrajectory(
            whenReversed.Position, whenReversed.Velocity, HorizontalDirection.Left);
        var start = backward.Evaluate(0.0);

        Assert.Equal(whenReversed.Position, start.Position, 10);
        Assert.Equal(whenReversed.Velocity, start.Velocity, 10);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.001)]
    public void RejectsInvalidSampleTimes(double time)
    {
        var trajectory = Profile.CreateTrajectory(0.0, 0.0, HorizontalDirection.Right);
        Assert.Throws<ArgumentOutOfRangeException>(() => trajectory.Evaluate(time));
    }

    [Fact]
    public void RejectsInvalidInitialConditionsAndDirection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Profile.CreateTrajectory(
            double.NaN, 0.0, HorizontalDirection.Right));
        Assert.Throws<ArgumentOutOfRangeException>(() => Profile.CreateTrajectory(
            0.0, double.PositiveInfinity, HorizontalDirection.Right));
        Assert.Throws<ArgumentOutOfRangeException>(() => Profile.CreateTrajectory(
            0.0, 0.0, (HorizontalDirection)123));
    }

    [Fact]
    public void RejectsTrajectoriesWhoseEndpointsOverflow()
    {
        Assert.Throws<OverflowException>(() => Profile.CreateTrajectory(
            double.MaxValue, double.MaxValue / 2.0, HorizontalDirection.Neutral));
    }
}
