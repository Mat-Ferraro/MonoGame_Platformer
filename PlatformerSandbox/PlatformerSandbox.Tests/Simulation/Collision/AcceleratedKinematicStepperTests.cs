using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class AcceleratedKinematicStepperTests
{
    private static Aabb Box(double x0, double y0, double x1, double y1) =>
        new(new Vector2D(x0, y0), new Vector2D(x1, y1));
    private static StaticAabbCollider Collider(int id, Aabb bounds) => new(id, bounds);
    private static void Close(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-9);

    [Fact]
    public void EmptyWorldIntegratesBothAxesAnalytically()
    {
        var result = AcceleratedKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(2, 3), new Vector2D(4, -2), 2,
            Array.Empty<StaticAabbCollider>());
        Assert.Equal(Box(12, 2, 13, 3), result.Bounds);
        Assert.Equal(new Vector2D(10, -1), result.Velocity);
        Assert.Equal(new Vector2D(4, -2), result.Acceleration);
        Assert.Empty(result.Contacts);
    }

    [Fact]
    public void ArcHitsCeilingEvenWithZeroNetVerticalDisplacement()
    {
        var ceiling = Collider(4, Box(-2, 2.5, 2, 4));
        var result = AcceleratedKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(0, 8), new Vector2D(0, -16), 1,
            new[] { ceiling });
        var contact = Assert.Single(result.Contacts);
        Close(0.25, contact.TimeSeconds);
        Assert.Equal(new Vector2D(0, -1), contact.Normal);
        Close(4, contact.IncomingVelocity.Y);
        Assert.Equal(Vector2D.Zero, contact.OutgoingVelocity);
        Assert.Equal(new Vector2D(0, -16), contact.OutgoingAcceleration);
        Close(-3, result.Bounds.Bottom);
        Close(-16 * 0.75, result.Velocity.Y);
    }

    [Fact]
    public void SlideAlongWallPreservesTangentialAcceleration()
    {
        var wall = Collider(3, Box(3, -10, 4, 10));
        var result = AcceleratedKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(0, 1), new Vector2D(4, 2), 2,
            new[] { wall });
        var c = Assert.Single(result.Contacts);
        Close(1, c.TimeSeconds);
        Assert.Equal(new Vector2D(4, 3), c.IncomingVelocity);
        Assert.Equal(new Vector2D(0, 3), c.OutgoingVelocity);
        Assert.Equal(new Vector2D(0, 2), c.OutgoingAcceleration);
        Close(2, result.Bounds.Left);
        Close(6, result.Bounds.Bottom); // first y=2; next +3*1 + 1 = 6
        Assert.Equal(new Vector2D(0, 5), result.Velocity);
    }

    [Fact]
    public void FloorConstrainsInwardGravityAndPreservesHorizontalSpeed()
    {
        var floor = Collider(1, Box(-10, -1, 10, 0));
        var result = AcceleratedKinematicStepper.Step(Box(0, 3, 1, 4),
            new Vector2D(2, 0), new Vector2D(0, -4), 2,
            new[] { floor });
        var c = Assert.Single(result.Contacts);
        Close(Math.Sqrt(1.5), c.TimeSeconds);
        Assert.Equal(new Vector2D(0, 1), c.Normal);
        Close(0, result.Bounds.Bottom);
        Close(4, result.Bounds.Left);
        Assert.Equal(new Vector2D(2, 0), result.Velocity);
        Assert.Equal(Vector2D.Zero, result.Acceleration);
    }

    [Fact]
    public void StartingOnFloorWithGravityProducesOneTimeZeroContact()
    {
        var floor = Collider(1, Box(-10, -1, 10, 0));
        var result = AcceleratedKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(2, 0), new Vector2D(0, -10), 1,
            new[] { floor });
        Close(0, Assert.Single(result.Contacts).TimeSeconds);
        Assert.Equal(Box(2, 0, 3, 1), result.Bounds);
        Assert.Equal(new Vector2D(2, 0), result.Velocity);
        Assert.Equal(Vector2D.Zero, result.Acceleration);
    }

    [Fact]
    public void StartingAtWallThenAcceleratingAwayIsNotBlocked()
    {
        var wall = Collider(1, Box(1, -10, 2, 10));
        var result = AcceleratedKinematicStepper.Step(Box(0, 0, 1, 1),
            Vector2D.Zero, new Vector2D(-4, 0), 1,
            new[] { wall });
        Assert.Empty(result.Contacts);
        Assert.Equal(Box(-2, 0, -1, 1), result.Bounds);
    }

    [Fact]
    public void StopModeStopsAllMovementAtFirstCurvedImpact()
    {
        var wall = Collider(1, Box(3, -10, 4, 10));
        var result = AcceleratedKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(0, 1), new Vector2D(4, 0), 2,
            new[] { wall }, CollisionResponseMode.Stop);
        Close(1, Assert.Single(result.Contacts).TimeSeconds);
        Assert.Equal(Box(2, 1, 3, 2), result.Bounds);
        Assert.Equal(Vector2D.Zero, result.Velocity);
        Assert.Equal(Vector2D.Zero, result.Acceleration);
    }

    [Fact]
    public void SlideCanHitWallThenFloorDuringSameStep()
    {
        var wall = Collider(2, Box(3, -10, 4, 10));
        var floor = Collider(1, Box(-10, -1, 10, 0));
        var result = AcceleratedKinematicStepper.Step(Box(0, 3, 1, 4),
            new Vector2D(4, 0), new Vector2D(0, -4), 2,
            new[] { floor, wall });
        Assert.Equal(2, result.Contacts.Count);
        Assert.Equal(2, result.Contacts[0].ColliderId);
        Assert.Equal(1, result.Contacts[1].ColliderId);
        Close(0.5, result.Contacts[0].TimeSeconds);
        Close(Math.Sqrt(1.5), result.Contacts[1].TimeSeconds);
        Close(2, result.Bounds.Left);
        Close(0, result.Bounds.Bottom);
        Assert.Equal(Vector2D.Zero, result.Velocity);
        Assert.Equal(Vector2D.Zero, result.Acceleration);
    }

    [Fact]
    public void ExactSimultaneousCollisionsUseStableIds()
    {
        var wall = Collider(2, Box(3, -10, 4, 10));
        var ceiling = Collider(1, Box(-10, 3, 10, 4));
        var a = AcceleratedKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(4, 4), Vector2D.Zero, 1,
            new[] { wall, ceiling });
        var b = AcceleratedKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(4, 4), Vector2D.Zero, 1,
            new[] { ceiling, wall });
        Assert.Equal(2, a.Contacts.Count);
        Assert.Equal(1, a.Contacts[0].ColliderId);
        Assert.Equal(2, a.Contacts[1].ColliderId);
        Assert.Equal(a.Bounds, b.Bounds);
        Assert.Equal(a.Contacts.ToArray(), b.Contacts.ToArray());
    }

    [Fact]
    public void CollisionAtEndOfStepProjectsAcceleration()
    {
        var wall = Collider(5, Box(3, -1, 4, 2));
        var r = AcceleratedKinematicStepper.Step(Box(0, 0, 1, 1),
            Vector2D.Zero, new Vector2D(4, 0), 1, new[] { wall });
        var contact = Assert.Single(r.Contacts);
        Close(1, contact.TimeSeconds);
        Assert.Equal(Box(2, 0, 3, 1), r.Bounds);
        Assert.Equal(Vector2D.Zero, r.Velocity);
        Assert.Equal(Vector2D.Zero, r.Acceleration);
    }

    [Fact]
    public void UninterruptedMotionIsIndependentOfHowTimeIsSplit()
    {
        var empty = Array.Empty<StaticAabbCollider>();
        var one = AcceleratedKinematicStepper.Step(Box(0, 1, 1, 2),
            new Vector2D(3, 5), new Vector2D(2, -6), 1,
            empty);
        var a = AcceleratedKinematicStepper.Step(Box(0, 1, 1, 2),
            new Vector2D(3, 5), new Vector2D(2, -6), .4,
            empty);
        var b = AcceleratedKinematicStepper.Step(a.Bounds, a.Velocity,
            a.Acceleration, .6, empty);
        Close(one.Bounds.Left, b.Bounds.Left);
        Close(one.Bounds.Bottom, b.Bounds.Bottom);
        Close(one.Velocity.X, b.Velocity.X);
        Close(one.Velocity.Y, b.Velocity.Y);
    }

    [Fact]
    public void MotionSplitBeforeImpactYieldsSameFinalState()
    {
        var wall = Collider(1, Box(3, -10, 4, 10));
        var world = new[] { wall };
        var one = AcceleratedKinematicStepper.Step(Box(0, 0, 1, 1),
            Vector2D.Zero, new Vector2D(4, 0), 2, world);
        var first = AcceleratedKinematicStepper.Step(Box(0, 0, 1, 1),
            Vector2D.Zero, new Vector2D(4, 0), 0.5, world);
        var second = AcceleratedKinematicStepper.Step(first.Bounds, first.Velocity,
            first.Acceleration, 1.5, world);
        Close(one.Bounds.Left, second.Bounds.Left);
        Close(one.Velocity.X, second.Velocity.X);
        Close(one.Acceleration.X, second.Acceleration.X);
        Close(1, Assert.Single(second.Contacts).TimeSeconds + 0.5);
    }

    [Fact]
    public void ReturnedContactCollectionIsReadOnly()
    {
        var wall = Collider(1, Box(3, -10, 4, 10));
        var result = AcceleratedKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(4, 0), Vector2D.Zero, 1, new[] { wall });
        var contacts = Assert.IsAssignableFrom<IList<AcceleratedKinematicContact>>(result.Contacts);
        Assert.Throws<NotSupportedException>(() => contacts.Add(default));
    }
}
