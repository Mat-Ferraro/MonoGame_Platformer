using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.Motion;

public sealed class JumpReleaseSolverTests
{
    private static readonly JumpConstraints FullJump = new(4.0, 0.4, 0.3);

    [Fact]
    public void ImmediateReleaseReachesExactlyTheSpecifiedMinimumHeight()
    {
        var full = JumpConstraintSolver.Solve(FullJump);
        var cut = JumpReleaseSolver.Solve(full, new JumpReleaseConstraints(1.0), 0.0);

        Close(200.0, cut.CutDeceleration);
        Close(0.1, cut.TimeFromReleaseToApexSeconds);
        Close(1.0, cut.PeakHeight);
        Close(1.0, cut.Evaluate(cut.TimeFromReleaseToApexSeconds).VerticalDisplacement);
        Close(0.0, cut.Evaluate(cut.TimeFromReleaseToApexSeconds).VerticalVelocity);
    }

    [Fact]
    public void ReleasePreservesPositionAndVelocityExactly()
    {
        var full = JumpConstraintSolver.Solve(FullJump);
        var released = JumpReleaseSolver.Solve(full, new JumpReleaseConstraints(1.0), 0.2);
        var before = full.Evaluate(0.2);
        var after = released.Evaluate(0.0);

        Close(before.VerticalDisplacement, after.VerticalDisplacement);
        Close(before.VerticalVelocity, after.VerticalVelocity);
    }

    [Fact]
    public void MidAscentReleaseHasExactlyCalculatedApex()
    {
        var full = JumpConstraintSolver.Solve(FullJump);
        var cut = JumpReleaseSolver.Solve(full, new JumpReleaseConstraints(1.0), 0.2);

        Close(3.0, cut.ReleaseHeight);
        Close(10.0, cut.ReleaseVelocity);
        Close(0.05, cut.TimeFromReleaseToApexSeconds);
        Close(3.25, cut.PeakHeight);
        Close(0.25, cut.ApexTimeSinceTakeoffSeconds);

        var midpoint = cut.Evaluate(0.025);
        Close(3.1875, midpoint.VerticalDisplacement);
        Close(5.0, midpoint.VerticalVelocity);
        Close(-200.0, midpoint.VerticalAcceleration);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(0.1)]
    [InlineData(0.2)]
    [InlineData(0.3)]
    [InlineData(0.39)]
    public void LaterReleasePeaksAboveMinimumButNeverExceedsHeldHeight(double releaseTime)
    {
        var full = JumpConstraintSolver.Solve(FullJump);
        var cut = JumpReleaseSolver.Solve(full, new JumpReleaseConstraints(1.0), releaseTime);
        Assert.InRange(cut.PeakHeight, 1.0 - 1e-9, full.Height + 1e-9);
    }

    [Fact]
    public void ApexHangCurveCanAlsoBeReleasedWithoutVelocityDiscontinuity()
    {
        var full = JumpConstraintSolver.Solve(new JumpConstraints(4.0, 0.4, 0.3, 0.1, 0.35));
        var cut = JumpReleaseSolver.Solve(full, new JumpReleaseConstraints(1.0), 0.15);
        var heldAtRelease = full.Evaluate(0.15);

        Close(heldAtRelease.VerticalDisplacement, cut.Evaluate(0).VerticalDisplacement);
        Close(heldAtRelease.VerticalVelocity, cut.Evaluate(0).VerticalVelocity);
        Assert.InRange(cut.PeakHeight, heldAtRelease.VerticalDisplacement, full.Height + 1e-9);
        Close(0.0, cut.Evaluate(cut.TimeFromReleaseToApexSeconds).VerticalVelocity);
    }

    [Fact]
    public void AnalyticalSegmentSubdividesWithoutChangingEndResult()
    {
        var full = JumpConstraintSolver.Solve(FullJump);
        var cut = JumpReleaseSolver.Solve(full, new JumpReleaseConstraints(1.0), 0.2);
        var half = cut.TimeFromReleaseToApexSeconds / 2.0;
        var a = cut.Evaluate(half);
        var b = cut.Evaluate(cut.TimeFromReleaseToApexSeconds);
        double remaining = cut.TimeFromReleaseToApexSeconds - half;
        double advancedPosition = a.VerticalDisplacement + a.VerticalVelocity * remaining
            + 0.5 * a.VerticalAcceleration * remaining * remaining;
        double advancedVelocity = a.VerticalVelocity + a.VerticalAcceleration * remaining;

        Close(b.VerticalDisplacement, advancedPosition);
        Close(b.VerticalVelocity, advancedVelocity);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(4.0)]
    [InlineData(5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInfeasibleMinimumHeight(double minHeight)
    {
        var full = JumpConstraintSolver.Solve(FullJump);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JumpReleaseSolver.Solve(full, new JumpReleaseConstraints(minHeight), 0));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.4)]
    [InlineData(0.5)]
    [InlineData(double.NaN)]
    public void RejectsReleaseOutsideTheRisingPhase(double releaseTime)
    {
        var full = JumpConstraintSolver.Solve(FullJump);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JumpReleaseSolver.Solve(full, new JumpReleaseConstraints(1.0), releaseTime));
    }

    [Fact]
    public void RejectsNullFullJump()
    {
        Assert.Throws<ArgumentNullException>(() =>
            JumpReleaseSolver.Solve(null!, new JumpReleaseConstraints(1.0), 0));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.050000001)]
    [InlineData(double.NaN)]
    public void RejectsEvaluationOutsideCutAscent(double time)
    {
        var full = JumpConstraintSolver.Solve(FullJump);
        var cut = JumpReleaseSolver.Solve(full, new JumpReleaseConstraints(1.0), 0.2);
        Assert.Throws<ArgumentOutOfRangeException>(() => cut.Evaluate(time));
    }

    private static void Close(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-9);
}
