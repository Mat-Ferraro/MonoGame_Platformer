using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class LinearKinematicStepperValidationTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));
    private static readonly Aabb Body = Box(0, 0, 1, 1);

    [Fact]
    public void InitialPenetrationRequiresExplicitDepenetration()
    {
        var obstacle = new StaticAabbCollider(3, Box(0.5, 0, 2, 1));
        var error = Assert.Throws<InvalidOperationException>(() =>
            LinearKinematicStepper.Step(Body, Vector2D.Zero, 1, new[] { obstacle }));
        Assert.Contains("3", error.Message);
    }

    [Fact]
    public void DuplicateColliderIdsAreRejectedEvenForZeroDuration()
    {
        var a = new StaticAabbCollider(4, Box(3, 0, 4, 1));
        var b = new StaticAabbCollider(4, Box(5, 0, 6, 1));
        Assert.Throws<ArgumentException>(() =>
            LinearKinematicStepper.Step(Body, Vector2D.Zero, 0, new[] { a, b }));
    }

    [Fact]
    public void NullWorldIsRejected() =>
        Assert.Throws<ArgumentNullException>(() =>
            LinearKinematicStepper.Step(Body, Vector2D.Zero, 1, null!));

    [Fact]
    public void NullColliderIsRejected() =>
        Assert.Throws<ArgumentNullException>(() =>
            LinearKinematicStepper.Step(Body, Vector2D.Zero, 1,
                new StaticAabbCollider[] { null! }));

    [Theory]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidDurationIsRejected(double seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LinearKinematicStepper.Step(Body, Vector2D.Zero, seconds,
                Array.Empty<StaticAabbCollider>()));

    [Fact]
    public void DefaultBoundsAreRejected() =>
        Assert.Throws<ArgumentException>(() =>
            LinearKinematicStepper.Step(default, Vector2D.Zero, 1,
                Array.Empty<StaticAabbCollider>()));

    [Fact]
    public void InvalidResponsePolicyIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LinearKinematicStepper.Step(Body, Vector2D.Zero, 1,
                Array.Empty<StaticAabbCollider>(), (CollisionResponseMode)20));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveEventLimitIsRejected(int maxEvents) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LinearKinematicStepper.Step(Body, Vector2D.Zero, 1,
                Array.Empty<StaticAabbCollider>(), maxContactEvents: maxEvents));

    [Fact]
    public void ExceedingEventLimitThrowsRatherThanSkippingCollisions()
    {
        var floor = new StaticAabbCollider(1, Box(-10, -1, 10, 0));
        var wall = new StaticAabbCollider(2, Box(3, -10, 4, 10));
        Assert.Throws<InvalidOperationException>(() =>
            LinearKinematicStepper.Step(Body, new Vector2D(4, -2), 1,
                new[] { floor, wall }, maxContactEvents: 1));
    }
}
