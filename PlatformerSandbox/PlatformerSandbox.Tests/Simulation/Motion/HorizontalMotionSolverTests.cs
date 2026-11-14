using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.Motion;

public sealed class HorizontalMotionSolverTests
{
    private static readonly HorizontalMotionConstraints Standard = new(
        MaximumSpeed: 10.0,
        TimeToMaximumSpeedSeconds: 0.20,
        TimeToStopSeconds: 0.10,
        TimeToReverseSeconds: 0.35);

    [Fact]
    public void DerivesAccelerationAndBrakingFromDesignerTimes()
    {
        var profile = HorizontalMotionSolver.Solve(Standard);

        Assert.Equal(10.0, profile.MaximumSpeed, 12);
        Assert.Equal(50.0, profile.AccelerationRate, 10);
        Assert.Equal(100.0, profile.StoppingDecelerationRate, 10);
        Assert.Equal(10.0 / 0.15, profile.ReversalDecelerationRate, 9);
    }

    [Theory]
    [InlineData(0, 0.2, 0.1, 0.35)]
    [InlineData(-10, 0.2, 0.1, 0.35)]
    [InlineData(10, 0, 0.1, 0.35)]
    [InlineData(10, 0.2, double.NaN, 0.35)]
    [InlineData(10, 0.2, 0.1, double.PositiveInfinity)]
    public void RejectsNonpositiveOrNonfiniteConstraints(
        double speed, double accelerationTime, double stopTime, double reverseTime)
    {
        Assert.ThrowsAny<ArgumentException>(() => HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(speed, accelerationTime, stopTime, reverseTime)));
    }

    [Theory]
    [InlineData(0.20)]
    [InlineData(0.10)]
    public void RejectsReversalTimesThatCannotContainFullAcceleration(double reversalTime)
    {
        Assert.Throws<ArgumentException>(() => HorizontalMotionSolver.Solve(
            Standard with { TimeToReverseSeconds = reversalTime }));
    }

    [Fact]
    public void DifferentGroundAndAirProfilesRequireNoNewSimulationCode()
    {
        var ground = HorizontalMotionSolver.Solve(Standard);
        var air = HorizontalMotionSolver.Solve(Standard with
        {
            TimeToMaximumSpeedSeconds = 0.40,
            TimeToStopSeconds = 0.30,
            TimeToReverseSeconds = 0.80
        });

        Assert.True(ground.AccelerationRate > air.AccelerationRate);
        Assert.True(ground.StoppingDecelerationRate > air.StoppingDecelerationRate);
    }

    [Fact]
    public void RejectsDerivedAccelerationOverflow()
    {
        var impossible = new HorizontalMotionConstraints(
            MaximumSpeed: double.MaxValue,
            TimeToMaximumSpeedSeconds: double.Epsilon,
            TimeToStopSeconds: 1.0,
            TimeToReverseSeconds: 2.0);

        Assert.Throws<ArgumentOutOfRangeException>(() => HorizontalMotionSolver.Solve(impossible));
    }
}
