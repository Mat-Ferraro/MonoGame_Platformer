using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Simulation.Motion;

public sealed class JumpFlightTrajectoryTests
{
    private static readonly JumpConstraints Reference = new(4, 0.4, 0.3);

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(0.199)]
    public void BeforeReleaseMatchesHeldReference(double t)
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var flight = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2);
        var expected = held.Evaluate(t);
        var actual = flight.Evaluate(t);
        Close(expected.VerticalDisplacement, actual.VerticalDisplacement);
        Close(expected.VerticalVelocity, actual.VerticalVelocity);
        Close(expected.VerticalAcceleration, actual.VerticalAcceleration);
    }

    [Fact]
    public void ReleaseIsPositionAndVelocityContinuous()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var flight = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2);
        JumpSample before = held.Evaluate(0.2);
        JumpSample at = flight.Evaluate(0.2);

        Close(before.VerticalDisplacement, at.VerticalDisplacement);
        Close(before.VerticalVelocity, at.VerticalVelocity);
        Close(-200, at.VerticalAcceleration); // Acceleration may change at release.
    }

    [Fact]
    public void CutAscentMatchesMilestone6AnalyticalSegment()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var cut = JumpReleaseSolver.Solve(held, new JumpReleaseConstraints(1), 0.2);
        var flight = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2);

        foreach (double s in new[] { 0.0, 0.01, 0.025, 0.049 })
        {
            JumpSample expected = cut.Evaluate(s);
            JumpSample actual = flight.Evaluate(0.2 + s);
            Close(expected.VerticalDisplacement, actual.VerticalDisplacement);
            Close(expected.VerticalVelocity, actual.VerticalVelocity);
            Close(expected.VerticalAcceleration, actual.VerticalAcceleration);
        }
    }

    [Fact]
    public void ApexIsPositionAndVelocityContinuous()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var cut = JumpReleaseSolver.Solve(held, new JumpReleaseConstraints(1), 0.2);
        var flight = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2);

        JumpSample before = cut.Evaluate(cut.TimeFromReleaseToApexSeconds);
        JumpSample after = flight.Evaluate(flight.ApexTimeSinceTakeoffSeconds);
        Close(before.VerticalDisplacement, after.VerticalDisplacement);
        Close(before.VerticalVelocity, after.VerticalVelocity);
        Close(0, after.VerticalVelocity);
    }

    [Fact]
    public void ConstantAccelerationCutDescentHasExpectedIntermediateSample()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var flight = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0);
        double midDescentTime = flight.ApexTimeSinceTakeoffSeconds
            + flight.DescentDurationSeconds / 2.0;
        JumpSample middle = flight.Evaluate(midDescentTime);

        Close(0.75, middle.VerticalDisplacement);
        Close(-2.0 * flight.PeakHeight / flight.DescentDurationSeconds * 0.5,
            middle.VerticalVelocity);
        Close(-2.0 * Reference.Height / (Reference.FallTimeSeconds * Reference.FallTimeSeconds),
            middle.VerticalAcceleration);
    }

    [Fact]
    public void FullFlightReturnsToLaunchHeightWithDownwardVelocity()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var heldFlight = JumpFlightSolver.ForHeldJump(held);
        var earlyFlight = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2);

        Close(0, heldFlight.Evaluate(heldFlight.TotalDurationSeconds).VerticalDisplacement);
        Close(0, earlyFlight.Evaluate(earlyFlight.TotalDurationSeconds).VerticalDisplacement);
        Assert.True(heldFlight.Evaluate(heldFlight.TotalDurationSeconds).VerticalVelocity < 0);
        Assert.True(earlyFlight.Evaluate(earlyFlight.TotalDurationSeconds).VerticalVelocity < 0);
    }

    [Fact]
    public void ExplicitTimePreservesPowerCurveForNonBallisticFall()
    {
        var held = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3, 0.1, 0.35));
        var flight = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.15,
            new JumpDescentConstraints(JumpDescentPolicy.SpecifyDescentDuration, 0.6));
        JumpSample middle = flight.Evaluate(flight.ApexTimeSinceTakeoffSeconds + 0.3);

        Close(flight.PeakHeight * (1.0 - Math.Pow(0.5, held.Exponent)),
            middle.VerticalDisplacement);
        Close(-flight.PeakHeight * held.Exponent / 0.6 * Math.Pow(0.5, held.Exponent - 1),
            middle.VerticalVelocity);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidEvaluationTime(double time)
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var flight = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2);
        Assert.Throws<ArgumentOutOfRangeException>(() => flight.Evaluate(time));
    }

    [Fact]
    public void RejectsEvaluationAfterReturnTime()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var flight = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            flight.Evaluate(flight.TotalDurationSeconds + 0.001));
    }

    [Fact]
    public void SpecifiedDescentDurationUsesItsOwnExactlyKnownAcceleration()
    {
        var held = JumpConstraintSolver.Solve(Reference);
        var flight = JumpFlightSolver.ForEarlyRelease(held, new JumpReleaseConstraints(1), 0.2,
            new JumpDescentConstraints(JumpDescentPolicy.SpecifyDescentDuration, 0.5));
        var atApex = flight.Evaluate(flight.ApexTimeSinceTakeoffSeconds);
        Close(-2 * flight.PeakHeight / (0.5 * 0.5), atApex.VerticalAcceleration);
    }

    private static void Close(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-9);
}
