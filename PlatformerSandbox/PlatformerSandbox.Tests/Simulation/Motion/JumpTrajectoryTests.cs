using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.Motion;

public sealed class JumpTrajectoryTests
{
    [Theory]
    [InlineData(0.0, 0.0, 20.0, -50.0)]
    [InlineData(0.2, 3.0, 10.0, -50.0)]
    [InlineData(0.4, 4.0, 0.0, -50.0)]
    [InlineData(0.55, 3.0, -13.333333333333333, -88.88888888888889)]
    [InlineData(0.7, 0.0, -26.666666666666668, -88.88888888888889)]
    public void BallisticProfileMatchesReferenceMotion(
        double seconds,
        double expectedHeight,
        double expectedVelocity,
        double expectedAcceleration)
    {
        var trajectory = JumpConstraintSolver.Solve(new JumpConstraints(4.0, 0.4, 0.3));
        var sample = trajectory.Evaluate(seconds);

        AssertClose(expectedHeight, sample.VerticalDisplacement);
        AssertClose(expectedVelocity, sample.VerticalVelocity);
        AssertClose(expectedAcceleration, sample.VerticalAcceleration);
    }

    [Fact]
    public void AnalyticApexHangProfileHasZeroVelocityAtApex()
    {
        var trajectory = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3, 0.1, 0.35));
        var apex = trajectory.Evaluate(0.4);

        AssertClose(4.0, apex.VerticalDisplacement);
        AssertClose(0.0, apex.VerticalVelocity);
        AssertClose(0.0, apex.VerticalAcceleration);
    }

    [Fact]
    public void HasNonzeroVelocityAtNominalReturnToTakeoffHeight()
    {
        var trajectory = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3));
        var end = trajectory.Evaluate(trajectory.TotalTimeSeconds);

        AssertClose(0.0, end.VerticalDisplacement);
        Assert.True(end.VerticalVelocity < 0.0);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.71)]
    [InlineData(double.NaN)]
    public void RejectsTimeOutsideTheDefinedProfile(double seconds)
    {
        var trajectory = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3));
        Assert.Throws<ArgumentOutOfRangeException>(() => trajectory.Evaluate(seconds));
    }

    private static void AssertClose(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-9);
}
