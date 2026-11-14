using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.MovingPlatforms;

public sealed class MovingPlatformInteractionSolverTests
{
    private static Aabb Box(double x, double y, double width = 1, double height = 1) =>
        new(new Vector2D(x, y), new Vector2D(x + width, y + height));

    private static MovingPlatformTrajectory Path(
        Vector2D? travel = null, Aabb? initial = null,
        double oneWay = 2) => new(9, initial ?? Box(0, -1, 4),
            travel ?? new Vector2D(4, 0), oneWay);

    private static MovingPlatformInteractionResult Step(
        MovingPlatformTrajectory platform, Aabb body,
        Vector2D velocity, Vector2D acceleration,
        double duration, double startTime = 0,
        double groundedRelativeAx = 0) =>
        MovingPlatformInteractionSolver.Step(platform, startTime, duration,
            body, velocity, acceleration, groundedRelativeAx);

    private static void Near(double expected, double actual, double tolerance = 1e-8) =>
        Assert.InRange(Math.Abs(expected - actual), 0, tolerance);

    [Fact]
    public void InitiallySupportedBodyRidesMovingFloor()
    {
        var result = Step(Path(), Box(1, 0),
            new Vector2D(2, 0), Vector2D.Zero, 1);
        Assert.Equal(MovingPlatformInteractionPhase.RidingCompleted, result.Phase);
        Assert.True(result.IsSupported);
        Assert.False(result.LandedThisStep);
        Near(3, result.Bounds.Left);
        Near(2, result.WorldVelocity.X);
        Near(1, result.AdvancedSeconds);
        Assert.Null(result.Contact);
    }

    [Fact]
    public void SupportedBodyCarriesVertically()
    {
        var platform = Path(travel: new Vector2D(0, 4));
        var result = Step(platform, Box(1, 0),
            new Vector2D(0, 2), Vector2D.Zero, 1);
        Near(2, result.Bounds.Bottom);
        Near(2, result.WorldVelocity.Y);
    }

    [Fact]
    public void LandingOnMovingFloorContinuesWithRemainingTime()
    {
        var result = Step(Path(), Box(2, 2),
            new Vector2D(0, -2), Vector2D.Zero, 1.25);
        Assert.Equal(MovingPlatformInteractionPhase.RidingCompleted, result.Phase);
        Assert.True(result.LandedThisStep);
        Near(1.25, result.AdvancedSeconds);
        Near(1, result.Contact!.Value.TimeSeconds);
        Near(0, result.Bounds.Bottom);
        Near(2, result.Bounds.Left); // world X remains unchanged
        Near(0, result.WorldVelocity.Y);
        Near(0, result.WorldVelocity.X);
    }

    [Fact]
    public void LandingOnRisingPlatformCarriesBodyUpAfterContact()
    {
        var rising = Path(travel: new Vector2D(0, 4),
            initial: Box(0, -3, 4));
        var result = Step(rising, Box(1, 0),
            Vector2D.Zero, Vector2D.Zero, 1.25);
        Assert.True(result.IsSupported);
        Assert.True(result.LandedThisStep);
        Near(1, result.Contact!.Value.TimeSeconds);
        Near(0.5, result.Bounds.Bottom);
        Near(2, result.WorldVelocity.Y);
    }

    [Fact]
    public void FallingOntoDescendingPlatformIsRelativeCollision()
    {
        var sinking = Path(travel: new Vector2D(0, -4));
        var result = Step(sinking, Box(1, 2),
            new Vector2D(0, -4), Vector2D.Zero, 1.25);
        Assert.True(result.IsSupported);
        Near(1, result.Contact!.Value.TimeSeconds);
        Near(-2.5, result.Bounds.Bottom);
        Near(-2, result.WorldVelocity.Y);
    }

    [Fact]
    public void LandingExactlyAtEndStillGroundsBody()
    {
        var result = Step(Path(), Box(2, 2),
            new Vector2D(0, -2), Vector2D.Zero, 1);
        Assert.True(result.IsSupported);
        Assert.True(result.LandedThisStep);
        Near(1, result.AdvancedSeconds);
        Near(0, result.Bounds.Bottom);
    }

    [Fact]
    public void NoCollisionCompletesExactAirborneMotion()
    {
        var result = Step(Path(), Box(20, 3),
            new Vector2D(-1, 0), new Vector2D(0, -4), 0.5);
        Assert.Equal(MovingPlatformInteractionPhase.FreeFlightCompleted, result.Phase);
        Assert.False(result.IsSupported);
        Assert.False(result.LandedThisStep);
        Near(19.5, result.Bounds.Left);
        Near(2.5, result.Bounds.Bottom);
        Near(-2, result.WorldVelocity.Y);
        Assert.Null(result.Contact);
    }

    [Fact]
    public void SideContactInterruptsWithoutInventingAResponse()
    {
        var result = Step(Path(), Box(5, -1),
            Vector2D.Zero, Vector2D.Zero, 1);
        Assert.Equal(MovingPlatformInteractionPhase.InterruptedByContact,
            result.Phase);
        Assert.False(result.LandedThisStep);
        Near(0.5, result.AdvancedSeconds);
        Near(5, result.Bounds.Left);
        Assert.Equal(new Vector2D(1, 0), result.Contact!.Value.Normal);
    }

    [Fact]
    public void JumpingUpFromPlatformDoesNotLatchToSupport()
    {
        var result = Step(Path(), Box(1, 0),
            new Vector2D(2, 4), Vector2D.Zero, 0.25);
        Assert.Equal(MovingPlatformInteractionPhase.FreeFlightCompleted, result.Phase);
        Assert.False(result.IsSupported);
        Assert.False(result.LandedThisStep);
        Near(1, result.Bounds.Bottom);
    }

    [Fact]
    public void UpwardAccelerationFromRestDoesNotGlueCharacterToFloor()
    {
        var result = Step(Path(), Box(1, 0),
            new Vector2D(2, 0), new Vector2D(0, 4), 0.25);
        Assert.Equal(MovingPlatformInteractionPhase.FreeFlightCompleted, result.Phase);
        Assert.False(result.IsSupported);
        Near(0.125, result.Bounds.Bottom);
    }

    [Fact]
    public void LandingAtVerticalTurnDoesNotAttachToRecedingFloor()
    {
        var risingThenFalling = Path(travel: new Vector2D(0, 2), oneWay: 1);
        var result = Step(risingThenFalling, Box(1, 2),
            Vector2D.Zero, Vector2D.Zero, 1.25);
        Assert.Equal(MovingPlatformInteractionPhase.InterruptedByContact,
            result.Phase);
        Assert.False(result.IsSupported);
        Assert.False(result.LandedThisStep);
        Near(1, result.AdvancedSeconds);
    }

    [Fact]
    public void WalkingOffTheRightEdgeInterruptsAtExactDeparture()
    {
        var result = Step(Path(), Box(2.5, 0),
            new Vector2D(5, 0), Vector2D.Zero, 1);
        Assert.Equal(MovingPlatformInteractionPhase.DepartedSupport, result.Phase);
        Assert.False(result.IsSupported);
        Near(0.5, result.AdvancedSeconds);
        Near(5, result.Bounds.Left); // platform carried 1, relative walk 1.5
    }

    [Fact]
    public void PlatformReversalCarriesPreviouslySupportedBody()
    {
        var result = Step(Path(), Box(1, 0),
            new Vector2D(2, 0), Vector2D.Zero, 3);
        Assert.True(result.IsSupported);
        Near(3, result.Bounds.Left);
        Near(-2, result.WorldVelocity.X);
        Near(3, result.AdvancedSeconds);
    }

    [Fact]
    public void RelativeHorizontalAccelerationAppliesOnlyDuringRide()
    {
        var result = Step(Path(), Box(1, 0),
            new Vector2D(2, 0), Vector2D.Zero, 1,
            groundedRelativeAx: 1);
        Assert.True(result.IsSupported);
        Near(3.5, result.Bounds.Left);
        Near(3, result.WorldVelocity.X);
    }

    [Fact]
    public void SupportedZeroDurationIsObservational()
    {
        var body = Box(1, 0);
        var result = Step(Path(), body,
            new Vector2D(2, 0), Vector2D.Zero, 0);
        Assert.True(result.IsSupported);
        Assert.Equal(body, result.Bounds);
        Near(0, result.AdvancedSeconds);
    }

    [Fact]
    public void AirborneZeroDurationIsObservational()
    {
        var body = Box(1, 2);
        var result = Step(Path(), body,
            Vector2D.Zero, new Vector2D(0, -9), 0);
        Assert.Equal(MovingPlatformInteractionPhase.FreeFlightCompleted, result.Phase);
        Assert.Equal(body, result.Bounds);
        Near(0, result.AdvancedSeconds);
    }

    [Fact]
    public void PenetratingStartIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Step(Path(),
            Box(1, -0.5), Vector2D.Zero, Vector2D.Zero, 1));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDurationIsRejected(double duration)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Step(Path(),
            Box(1, 2), Vector2D.Zero, Vector2D.Zero, duration));
    }

    [Fact]
    public void InvalidRideAccelerationIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Step(Path(),
            Box(1, 0), Vector2D.Zero, Vector2D.Zero, 1,
            groundedRelativeAx: double.NaN));
    }
}
