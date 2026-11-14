using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.Motion;

public sealed class JumpConstraintSolverTests
{
    [Fact]
    public void BasicJumpUsesConstantAccelerationProfiles()
    {
        var trajectory = JumpConstraintSolver.Solve(new JumpConstraints(4.0, 0.4, 0.3));

        AssertClose(2.0, trajectory.Exponent);
        AssertClose(0.7, trajectory.TotalTimeSeconds);
        AssertClose(0.7 * Math.Sqrt(0.1), trajectory.TimeNearApexSeconds);
    }

    [Fact]
    public void SolvesExactTimeSpentInTopTenPercentOfHeight()
    {
        var trajectory = JumpConstraintSolver.Solve(new JumpConstraints(
            Height: 4.0,
            RiseTimeSeconds: 0.4,
            FallTimeSeconds: 0.3,
            ApexRegionFraction: 0.1,
            TimeNearApexSeconds: 0.35));

        AssertClose(Math.Log(0.1) / Math.Log(0.5), trajectory.Exponent);
        AssertClose(0.35, trajectory.TimeNearApexSeconds);

        double windowRatio = Math.Pow(trajectory.ApexRegionFraction, 1.0 / trajectory.Exponent);
        double windowStart = trajectory.RiseTimeSeconds * (1.0 - windowRatio);
        double windowEnd = trajectory.RiseTimeSeconds + trajectory.FallTimeSeconds * windowRatio;

        AssertClose(3.6, trajectory.Evaluate(windowStart).VerticalDisplacement);
        AssertClose(3.6, trajectory.Evaluate(windowEnd).VerticalDisplacement);
        AssertClose(0.35, windowEnd - windowStart);
    }

    [Theory]
    [InlineData(0.0, 0.4, 0.3)]
    [InlineData(-4.0, 0.4, 0.3)]
    [InlineData(4.0, 0.0, 0.3)]
    [InlineData(4.0, 0.4, -0.3)]
    public void RejectsNonPositiveConstraints(double height, double rise, double fall)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JumpConstraintSolver.Solve(new JumpConstraints(height, rise, fall)));
    }

    [Fact]
    public void RejectsNonFiniteConstraints()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JumpConstraintSolver.Solve(new JumpConstraints(double.NaN, 0.4, 0.3)));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(double.NaN)]
    public void RejectsInvalidApexRegion(double fraction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3, fraction)));
    }

    [Fact]
    public void RejectsApexDurationTooShortForThisCurveFamily()
    {
        Assert.Throws<ArgumentException>(() =>
            JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3, 0.1, 0.1)));
    }

    [Fact]
    public void RejectsApexDurationAtOrBeyondTotalJumpTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3, 0.1, 0.7)));
    }

    private static void AssertClose(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-9);
}
