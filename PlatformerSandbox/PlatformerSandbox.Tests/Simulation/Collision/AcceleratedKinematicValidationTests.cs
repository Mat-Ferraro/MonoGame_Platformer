using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class AcceleratedKinematicValidationTests
{
    private static Aabb Box(double x0, double y0, double x1, double y1) =>
        new(new Vector2D(x0, y0), new Vector2D(x1, y1));
    private static readonly Aabb Start = Box(0, 0, 1, 1);
    private static readonly StaticAabbCollider Wall =
        new(1, Box(3, -10, 4, 10));

    [Fact]
    public void ZeroDurationDoesNotIntegrateOrEmitContacts()
    {
        var r = AcceleratedKinematicStepper.Step(Start,
            new Vector2D(2, 3), new Vector2D(4, -8), 0, new[] { Wall });
        Assert.Equal(Start, r.Bounds);
        Assert.Equal(new Vector2D(2, 3), r.Velocity);
        Assert.Equal(new Vector2D(4, -8), r.Acceleration);
        Assert.Empty(r.Contacts);
    }

    [Fact]
    public void InitialPenetrationIsRejected()
    {
        var penetrating = new StaticAabbCollider(5, Box(.5, .5, 1.5, 1.5));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            AcceleratedKinematicStepper.Step(Start, Vector2D.Zero,
                Vector2D.Zero, 1, new[] { penetrating }));
        Assert.Contains("5", ex.Message);
    }

    [Fact]
    public void DuplicateStableIdsAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            AcceleratedKinematicStepper.Step(Start, Vector2D.Zero,
                Vector2D.Zero, 1, new[] { Wall, new StaticAabbCollider(1, Box(7, 0, 8, 1)) }));
    }

    [Fact]
    public void NullWorldIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AcceleratedKinematicStepper.Step(Start, Vector2D.Zero,
                Vector2D.Zero, 1, null!));
    }

    [Fact]
    public void NullColliderIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AcceleratedKinematicStepper.Step(Start, Vector2D.Zero,
                Vector2D.Zero, 1, new StaticAabbCollider[] { null! }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDurationIsRejected(double dt)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AcceleratedKinematicStepper.Step(Start, Vector2D.Zero,
                Vector2D.Zero, dt, new[] { Wall }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidAlignmentToleranceIsRejected(double tolerance)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AcceleratedKinematicStepper.Step(Start, Vector2D.Zero,
                Vector2D.Zero, 1, new[] { Wall },
                contactAlignmentTolerance: tolerance));
    }

    [Fact]
    public void InvalidResponseModeRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AcceleratedKinematicStepper.Step(Start, Vector2D.Zero,
                Vector2D.Zero, 1, new[] { Wall }, (CollisionResponseMode)123));
    }

    [Fact]
    public void EventBudgetIsExplicitAndNeverReturnsPartialResult()
    {
        var ceiling = new StaticAabbCollider(2, Box(-10, 3, 10, 4));
        Assert.Throws<InvalidOperationException>(() =>
            AcceleratedKinematicStepper.Step(Start,
                new Vector2D(4, 4), Vector2D.Zero, 1,
                new[] { Wall, ceiling }, maxContactEvents: 1));
    }

    [Fact]
    public void InvalidEventBudgetIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AcceleratedKinematicStepper.Step(Start, Vector2D.Zero,
                Vector2D.Zero, 1, new[] { Wall }, maxContactEvents: 0));
    }

    [Fact]
    public void InvalidDefaultAabbRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            AcceleratedKinematicStepper.Step(default, Vector2D.Zero,
                Vector2D.Zero, 1, Array.Empty<StaticAabbCollider>()));
    }

    [Fact]
    public void DetectorRejectsDuplicateIdsEvenWhenZeroDuration()
    {
        Assert.Throws<ArgumentException>(() =>
            ParabolicAabbCollisionDetector.SweepFirst(Start,
                Vector2D.Zero, Vector2D.Zero, 0,
                new[] { Wall, new StaticAabbCollider(1, Box(7, 0, 8, 1)) }));
    }
}
