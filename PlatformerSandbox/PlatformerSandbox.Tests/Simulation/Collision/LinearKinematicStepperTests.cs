using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class LinearKinematicStepperTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    private static StaticAabbCollider Wall(int id = 7) =>
        new(id, Box(3, -10, 4, 10));

    private static void Close(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-10);

    [Fact]
    public void EmptyWorldMovesByVelocityTimesDuration()
    {
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(2, -3), 1.5, Array.Empty<StaticAabbCollider>());
        Assert.Equal(Box(3, -4.5, 4, -3.5), result.Bounds);
        Assert.Equal(new Vector2D(2, -3), result.Velocity);
        Assert.Empty(result.Contacts);
        Close(1.5, result.DurationSeconds);
    }

    [Fact]
    public void ZeroTimeDoesNotMoveOrCollide()
    {
        var start = Box(0, 0, 1, 1);
        var result = LinearKinematicStepper.Step(start, new Vector2D(4, 2), 0,
            new[] { Wall() });
        Assert.Equal(start, result.Bounds);
        Assert.Equal(new Vector2D(4, 2), result.Velocity);
        Assert.Empty(result.Contacts);
    }

    [Fact]
    public void WallStopsXButAllowsRemainingYMotion()
    {
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(4, 2), 1, new[] { Wall() });
        Assert.Equal(Box(2, 2, 3, 3), result.Bounds);
        Assert.Equal(new Vector2D(0, 2), result.Velocity);
        Assert.Single(result.Contacts);
        var contact = result.Contacts[0];
        Assert.Equal(7, contact.ColliderId);
        Close(0.5, contact.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), contact.Normal);
        Assert.Equal(Box(2, 1, 3, 2), contact.BoundsAtImpact);
        Assert.Equal(new Vector2D(4, 2), contact.IncomingVelocity);
        Assert.Equal(new Vector2D(0, 2), contact.OutgoingVelocity);
    }

    [Fact]
    public void FloorStopsDownwardVelocityButAllowsHorizontalSliding()
    {
        var floor = new StaticAabbCollider(10, Box(-10, 0, 10, 1));
        var result = LinearKinematicStepper.Step(Box(0, 3, 1, 4),
            new Vector2D(2, -4), 1, new[] { floor });
        Assert.Equal(Box(2, 1, 3, 2), result.Bounds);
        Assert.Equal(new Vector2D(2, 0), result.Velocity);
        Close(0.5, Assert.Single(result.Contacts).TimeSeconds);
        Assert.Equal(new Vector2D(0, 1), result.Contacts[0].Normal);
    }

    [Fact]
    public void CeilingStopsUpwardVelocityButAllowsHorizontalSliding()
    {
        var ceiling = new StaticAabbCollider(10, Box(-10, 3, 10, 4));
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(2, 4), 1, new[] { ceiling });
        Assert.Equal(Box(2, 2, 3, 3), result.Bounds);
        Assert.Equal(new Vector2D(2, 0), result.Velocity);
        Assert.Equal(new Vector2D(0, -1), Assert.Single(result.Contacts).Normal);
    }

    [Fact]
    public void StopPolicyStopsBothAxesAtFirstImpact()
    {
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(4, 2), 1, new[] { Wall() }, CollisionResponseMode.Stop);
        Assert.Equal(Box(2, 1, 3, 2), result.Bounds);
        Assert.Equal(Vector2D.Zero, result.Velocity);
        Assert.Equal(Vector2D.Zero, Assert.Single(result.Contacts).OutgoingVelocity);
    }

    [Fact]
    public void ThinWallCannotBeTunnelledThrough()
    {
        var thinWall = new StaticAabbCollider(1, Box(3, -10, 3.001, 10));
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(1000, 0), 1, new[] { thinWall });
        Assert.Equal(Box(2, 0, 3, 1), result.Bounds);
        Assert.Equal(Vector2D.Zero, result.Velocity);
        Close(0.002, Assert.Single(result.Contacts).TimeSeconds);
    }

    [Fact]
    public void InitiallyTouchingAndMovingInwardStopsAtTimeZero()
    {
        var wall = new StaticAabbCollider(1, Box(1, -10, 2, 10));
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(2, 3), 1, new[] { wall });
        Assert.Equal(Box(0, 3, 1, 4), result.Bounds);
        Assert.Equal(new Vector2D(0, 3), result.Velocity);
        Close(0, Assert.Single(result.Contacts).TimeSeconds);
    }

    [Fact]
    public void TouchingAndMovingAwayIsNotAHit()
    {
        var wall = new StaticAabbCollider(1, Box(1, -10, 2, 10));
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(-2, 0), 1, new[] { wall });
        Assert.Equal(Box(-2, 0, -1, 1), result.Bounds);
        Assert.Empty(result.Contacts);
    }

    [Fact]
    public void TouchingAndMovingTangentiallyIsNotAHit()
    {
        var wall = new StaticAabbCollider(1, Box(1, -10, 2, 10));
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(0, 2), 1, new[] { wall });
        Assert.Equal(Box(0, 2, 1, 3), result.Bounds);
        Assert.Empty(result.Contacts);
    }

    [Fact]
    public void EndOfStepContactProjectsVelocityForNextStep()
    {
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(2, 1), 1, new[] { Wall() });
        Assert.Equal(Box(2, 1, 3, 2), result.Bounds);
        Assert.Equal(new Vector2D(0, 1), result.Velocity);
        Close(1, Assert.Single(result.Contacts).TimeSeconds);
    }

    [Fact]
    public void TwoSurfacesCanBlockBothAxesDuringOneStep()
    {
        var floor = new StaticAabbCollider(1, Box(-10, -1, 10, 0));
        var wall = Wall(2);
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(4, -2), 1, new[] { wall, floor });
        Assert.Equal(Box(2, 0, 3, 1), result.Bounds);
        Assert.Equal(Vector2D.Zero, result.Velocity);
        Assert.Equal(2, result.Contacts.Count);
        Assert.Equal(1, result.Contacts[0].ColliderId);
        Assert.Equal(2, result.Contacts[1].ColliderId);
        Close(0, result.Contacts[0].TimeSeconds);
        Close(0.5, result.Contacts[1].TimeSeconds);
    }

    [Fact]
    public void ExactSimultaneousImpactsBreakTieWithStableColliderId()
    {
        var wall = new StaticAabbCollider(1, Box(3, -10, 4, 10));
        var ceiling = new StaticAabbCollider(2, Box(-10, 3, 10, 4));
        var first = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(4, 4), 1, new[] { ceiling, wall });
        var second = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(4, 4), 1, new[] { wall, ceiling });
        Assert.Equal(first.Bounds, second.Bounds);
        Assert.Equal(Vector2D.Zero, first.Velocity);
        Assert.Equal(Box(2, 2, 3, 3), first.Bounds);
        Assert.Equal(2, first.Contacts.Count);
        Assert.Equal(1, first.Contacts[0].ColliderId);
        Assert.Equal(2, first.Contacts[1].ColliderId);
        Close(0.5, first.Contacts[0].TimeSeconds);
        Close(0.5, first.Contacts[1].TimeSeconds);
        Assert.Equal(first.Contacts.ToArray(), second.Contacts.ToArray());
    }

    [Fact]
    public void StepSplitBeforeCollisionHasSameFinalState()
    {
        var wall = Wall();
        var single = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(4, 2), 1, new[] { wall });
        var firstHalf = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(4, 2), 0.25, new[] { wall });
        var secondHalf = LinearKinematicStepper.Step(firstHalf.Bounds,
            firstHalf.Velocity, 0.75, new[] { wall });
        Assert.Equal(single.Bounds, secondHalf.Bounds);
        Assert.Equal(single.Velocity, secondHalf.Velocity);
        Assert.Empty(firstHalf.Contacts);
        Close(0.25, Assert.Single(secondHalf.Contacts).TimeSeconds);
    }

    [Fact]
    public void SlidingWorksAfterImmediateContactAndIgnoresRepeatedTangent()
    {
        var wall = new StaticAabbCollider(1, Box(1, -10, 2, 10));
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(2, 2), 5, new[] { wall });
        Assert.Equal(Box(0, 10, 1, 11), result.Bounds);
        Assert.Equal(new Vector2D(0, 2), result.Velocity);
        Assert.Single(result.Contacts);
    }

    [Fact]
    public void NoImplicitGroundedOrJumpingStateIsInferred()
    {
        var floor = new StaticAabbCollider(1, Box(-10, -1, 10, 0));
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            Vector2D.Zero, 1, new[] { floor });
        Assert.Equal(Box(0, 0, 1, 1), result.Bounds);
        Assert.Empty(result.Contacts);
    }

    [Fact]
    public void ContactCollectionIsReadOnly()
    {
        var result = LinearKinematicStepper.Step(Box(0, 0, 1, 1),
            new Vector2D(4, 0), 1, new[] { Wall() });
        var list = Assert.IsAssignableFrom<IList<LinearKinematicContact>>(result.Contacts);
        Assert.Throws<NotSupportedException>(() =>
            list.Add(default));
    }
}
