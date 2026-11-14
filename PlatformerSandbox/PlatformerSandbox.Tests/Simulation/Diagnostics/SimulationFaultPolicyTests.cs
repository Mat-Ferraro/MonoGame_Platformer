using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Diagnostics;

namespace PlatformerSandbox.Tests.Simulation.Diagnostics;

public sealed class SimulationFaultPolicyTests
{
    private static readonly Vector2D Zero = Vector2D.Zero;

    [Fact]
    public void StrictModeFaultsOnAnOtherwiseRecoverableRemainder()
    {
        var session = SimulationFaultPolicy.Development.StartStep();
        var exception = Assert.Throws<SimulationFaultException>(() =>
            SimulationTimeProgress.TryConsumeKnownRemainder(
                20.0, 1e-18, Zero, Zero, session));
        Assert.Equal(SimulationRecoveryKind.SubResolutionTimeRemainder, exception.Kind);
        Assert.Equal(1, session.RecoveryCount);
    }

    [Fact]
    public void DiagnosticModeRecordsAndConsumesTheSameRemainder()
    {
        var session = SimulationFaultPolicy.QualityAssurance.StartStep();
        Assert.True(SimulationTimeProgress.TryConsumeKnownRemainder(
            20.0, 1e-18, Zero, Zero, session));
        Assert.Equal(1, session.RecoveryCount);
        Assert.Equal(SimulationRecoveryKind.SubResolutionTimeRemainder,
            Assert.Single(session.Recoveries).Kind);
    }

    [Fact]
    public void ResilientModeAlsoKeepsARecoveryCounter()
    {
        var session = SimulationFaultPolicy.Release.StartStep();
        Assert.True(SimulationTimeProgress.TryConsumeKnownRemainder(
            20.0, 1e-18, Zero, Zero, session));
        Assert.Equal(1, session.RecoveryCount);
    }

    [Fact]
    public void RepresentableIntervalNeedsNoRecovery()
    {
        var session = SimulationFaultPolicy.Development.StartStep();
        Assert.False(SimulationTimeProgress.TryConsumeKnownRemainder(
            20.0, 1.0 / 60.0, Zero, Zero, session));
        Assert.Empty(session.Recoveries);
    }

    [Fact]
    public void ZeroDurationNeedsNoRecovery()
    {
        var session = SimulationFaultPolicy.Development.StartStep();
        Assert.False(SimulationTimeProgress.TryConsumeKnownRemainder(
            20.0, 0.0, Zero, Zero, session));
        Assert.Equal(0, session.RecoveryCount);
    }

    [Fact]
    public void UnsafeDisplacementAlwaysFaultsEvenInResilientMode()
    {
        var session = SimulationFaultPolicy.Release.StartStep();
        Assert.Throws<SimulationFaultException>(() =>
            SimulationTimeProgress.TryConsumeKnownRemainder(
                20.0, 1e-18, new Vector2D(1e14, 0), Zero, session));
        Assert.Empty(session.Recoveries);
    }

    [Fact]
    public void CustomDisplacementLimitIsHonored()
    {
        var permissive = new SimulationFaultPolicy(
            SimulationFaultSensitivity.Resilient,
            maximumDiscardedDisplacement: 1e-4).StartStep();
        Assert.True(SimulationTimeProgress.TryConsumeKnownRemainder(
            20.0, 1e-18, new Vector2D(1e14, 0), Zero, permissive));
        Assert.Single(permissive.Recoveries);
    }

    [Fact]
    public void TooManyRecoveriesFaultEvenInResilientMode()
    {
        var session = new SimulationFaultPolicy(
            SimulationFaultSensitivity.Resilient,
            maximumRecoveriesPerStep: 1).StartStep();
        session.RecordRecovery(SimulationRecoveryKind.ContactAlignment, 0.0, "first");
        Assert.Throws<SimulationFaultException>(() =>
            session.RecordRecovery(SimulationRecoveryKind.ContactAlignment, 0.0, "second"));
        Assert.Equal(1, session.RecoveryCount);
    }

    [Fact]
    public void ExplicitFaultProfilesAreNotTiedToBuildConfiguration()
    {
        Assert.Equal(SimulationFaultSensitivity.Strict,
            SimulationFaultPolicy.Development.Sensitivity);
        Assert.Equal(SimulationFaultSensitivity.Diagnostic,
            SimulationFaultPolicy.QualityAssurance.Sensitivity);
        Assert.Equal(SimulationFaultSensitivity.Resilient,
            SimulationFaultPolicy.Release.Sensitivity);
    }

    [Fact]
    public void InvalidPolicyParametersAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationFaultPolicy((SimulationFaultSensitivity)999));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationFaultPolicy(SimulationFaultSensitivity.Resilient,
                maximumDiscardedDisplacement: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SimulationFaultPolicy(SimulationFaultSensitivity.Resilient,
                maximumRecoveriesPerStep: 0));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDurationsNeverRecover(double duration)
    {
        var session = SimulationFaultPolicy.Release.StartStep();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SimulationTimeProgress.TryConsumeKnownRemainder(
                20.0, duration, Zero, Zero, session));
    }

    [Fact]
    public void ARecoveryRecordCannotBeRetroactivelyEdited()
    {
        var session = SimulationFaultPolicy.Release.StartStep();
        session.RecordRecovery(SimulationRecoveryKind.ContactAlignment, 1e-12,
            "aligned face");
        Assert.Equal(1e-12, Assert.Single(session.Recoveries).Amount);
        Assert.Equal(1, session.RecoveryCount);
    }
}
