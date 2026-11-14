using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.Motion;

/// <summary>
/// Regression: the nominal end of a held jump is an exact analytical
/// boundary, not an approximate value with a positive floating-point residue.
/// The continuous collision detector uses exact face crossings at this time.
/// </summary>
public sealed class JumpTrajectoryEndpointRegressionTests
{
    [Fact]
    public void BallisticJumpEndsAtExactlyTheTakeoffHeight()
    {
        var jump = JumpConstraintSolver.Solve(new JumpConstraints(4.0, 0.4, 0.3));
        var end = jump.Evaluate(jump.TotalTimeSeconds);

        Assert.Equal(0.0, end.VerticalDisplacement);
        Assert.True(end.VerticalVelocity < 0.0);
    }

    [Fact]
    public void ApexHangJumpEndsAtExactlyTheTakeoffHeight()
    {
        var jump = JumpConstraintSolver.Solve(
            new JumpConstraints(4.0, 0.4, 0.3, 0.1, 0.35));
        var end = jump.Evaluate(jump.TotalTimeSeconds);

        Assert.Equal(0.0, end.VerticalDisplacement);
        Assert.True(end.VerticalVelocity < 0.0);
    }

    [Fact]
    public void CompleteHeldFlightAlsoReportsAnExactEndpoint()
    {
        var jump = JumpConstraintSolver.Solve(new JumpConstraints(4.0, 0.4, 0.3));
        var flight = JumpFlightSolver.ForHeldJump(jump);
        var end = flight.Evaluate(flight.TotalDurationSeconds);

        Assert.Equal(0.0, end.VerticalDisplacement);
        Assert.True(end.VerticalVelocity < 0.0);
    }
}
