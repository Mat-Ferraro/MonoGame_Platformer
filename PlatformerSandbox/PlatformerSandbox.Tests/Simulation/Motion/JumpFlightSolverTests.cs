using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.Motion;

public sealed class JumpFlightSolverTests
{
    private static readonly JumpConstraints Reference = new(4.0, 0.4, 0.3);

    [Fact]
    public void HeldFlightPreservesEverySampleOfExistingProfile()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var flight = JumpFlightSolver.ForHeldJump(held);

        Assert.False(flight.IsEarlyReleased);
        Assert.Null(flight.ReleaseTimeSinceTakeoffSeconds);
        Close(4, flight.PeakHeight);
        Close(0.4, flight.ApexTimeSinceTakeoffSeconds);
        Close(0.3, flight.DescentDurationSeconds);
        Close(0.7, flight.TotalDurationSeconds);

        foreach (double time in new[] { 0, 0.1, 0.2, 0.4, 0.45, 0.6, 0.7 })
        {
            var expected = held.Evaluate(time);
            var actual = flight.Evaluate(time);
            Close(expected.VerticalDisplacement, actual.VerticalDisplacement);
            Close(expected.VerticalVelocity, actual.VerticalVelocity);
            Close(expected.VerticalAcceleration, actual.VerticalAcceleration);
        }
    }

    [Fact]
    public void ImmediateReleasePreservesOriginalFallAccelerationWithDefaultPolicy()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var cut = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0);

        Assert.True(cut.IsEarlyReleased);
        Close(1.0, cut.PeakHeight);
        Close(0.1, cut.ApexTimeSinceTakeoffSeconds);
        Close(0.15, cut.DescentDurationSeconds);
        Close(0.25, cut.TotalDurationSeconds);
        Close(0, cut.Evaluate(cut.TotalDurationSeconds).VerticalDisplacement);
        Close(-2 * Reference.Height / (Reference.FallTimeSeconds * Reference.FallTimeSeconds),
              cut.Evaluate(cut.ApexTimeSinceTakeoffSeconds).VerticalAcceleration);
    }

    [Fact]
    public void MidAscentReleaseProducesCalculatedApexAndReturnTime()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var cut = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2);

        Close(0.2, cut.ReleaseTimeSinceTakeoffSeconds!.Value);
        Close(3.25, cut.PeakHeight);
        Close(0.25, cut.ApexTimeSinceTakeoffSeconds);
        Close(0.3 * Math.Sqrt(3.25 / 4), cut.DescentDurationSeconds);
        Close(0.25 + cut.DescentDurationSeconds, cut.TotalDurationSeconds);
        Close(3.25, cut.Evaluate(cut.ApexTimeSinceTakeoffSeconds).VerticalDisplacement);
        Close(0, cut.Evaluate(cut.ApexTimeSinceTakeoffSeconds).VerticalVelocity);
    }

    [Fact]
    public void ExplicitDescentTimeIsHonoredWithoutChangingPeak()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var cut = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2,
            new JumpDescentConstraints(JumpDescentPolicy.SpecifyDescentDuration, 0.5));

        Assert.Equal(JumpDescentPolicy.SpecifyDescentDuration, cut.DescentPolicy);
        Close(3.25, cut.PeakHeight);
        Close(0.5, cut.DescentDurationSeconds);
        Close(0.75, cut.TotalDurationSeconds);
        Close(0.0, cut.Evaluate(0.75).VerticalDisplacement);
    }

    [Fact]
    public void ApexHangReferenceUsesSameFallLawAfterRelease()
    {
        var held = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3, 0.1, 0.35));
        var cut = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.15);

        double expectedDuration = held.FallTimeSeconds * Math.Pow(
            cut.PeakHeight / held.Height, 1.0 / held.Exponent);
        Close(expectedDuration, cut.DescentDurationSeconds);
        Close(cut.PeakHeight, cut.Evaluate(cut.ApexTimeSinceTakeoffSeconds).VerticalDisplacement);
        Close(0, cut.Evaluate(cut.ApexTimeSinceTakeoffSeconds).VerticalVelocity);
        Close(0, cut.Evaluate(cut.ApexTimeSinceTakeoffSeconds).VerticalAcceleration);
    }

    [Fact]
    public void RejectsNullHeldJump()
    {
        Assert.Throws<ArgumentNullException>(() => JumpFlightSolver.ForHeldJump(null!));
        Assert.Throws<ArgumentNullException>(() => JumpFlightSolver.ForEarlyRelease(
            null!, new JumpReleaseConstraints(1), 0));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidExplicitDescentDuration(double duration)
    {
        var held = JumpConstraintSolver.Solve(Reference);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2,
                new JumpDescentConstraints(JumpDescentPolicy.SpecifyDescentDuration, duration)));
    }

    [Fact]
    public void RejectsMissingExplicitDuration()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2,
                new JumpDescentConstraints(JumpDescentPolicy.SpecifyDescentDuration)));
    }

    [Fact]
    public void RejectsUnusedDurationUnderPreserveLaw()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        Assert.Throws<ArgumentException>(() =>
            JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2,
                new JumpDescentConstraints(JumpDescentPolicy.PreserveReferenceFallLaw, 0.3)));
    }

    [Fact]
    public void RejectsUnknownPolicy()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2,
                new JumpDescentConstraints((JumpDescentPolicy)99)));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(0.4)]
    [InlineData(double.NaN)]
    public void ExistingReleaseTimeValidationRemainsEnforced(double releaseTime)
    {
        var held = JumpConstraintSolver.Solve(Reference);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), releaseTime));
    }

    private static void Close(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-9);
}
