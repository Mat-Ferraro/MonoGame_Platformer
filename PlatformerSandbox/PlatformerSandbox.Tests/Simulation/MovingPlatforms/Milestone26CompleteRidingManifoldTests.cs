using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.MovingPlatforms;

public sealed class Milestone26CompleteRidingManifoldTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    [Fact]
    public void TwoCoincidentWallsShareOneSupportedRideEvent()
    {
        var support = new MovingPlatformTrajectory(7,
            Box(-10, -0.25, 10, 0), new Vector2D(2, 0), 2);
        var lowerWall = new StaticAabbCollider(5, Box(1, 0, 2, 0.5));
        var upperWall = new StaticAabbCollider(3, Box(1, 0.5, 2, 1.5));
        var result = MovingPlatformRideEventScheduler.Step(support, 0, 0.25,
            Box(0, 0, 1, 1), 0, 0, [lowerWall, upperWall]);
        Assert.Equal(new[] { 3, 5 }, result.Contacts.Select(c => c.ColliderId));
        Assert.All(result.Contacts, c => Assert.Equal(0.0, c.TimeWithinStepSeconds));
        Assert.Equal(3, result.StaticContact!.Value.ColliderId);
        Assert.Equal(0.0, result.WorldVelocity.X);
        Assert.True(result.IsSupported);
        Assert.Equal(7, result.SupportPlatformId);
    }

    [Fact]
    public void SameTimeWallContactsAreIndependentOfEnumerationOrder()
    {
        var support = new MovingPlatformTrajectory(7,
            Box(-10, -0.25, 10, 0), new Vector2D(2, 0), 2);
        var lower = new StaticAabbCollider(5, Box(1, 0, 2, 0.5));
        var upper = new StaticAabbCollider(3, Box(1, 0.5, 2, 1.5));
        var first = MovingPlatformRideEventScheduler.Step(support, 0, 0.25,
            Box(0, 0, 1, 1), 0, 0, [lower, upper]);
        var second = MovingPlatformRideEventScheduler.Step(support, 0, 0.25,
            Box(0, 0, 1, 1), 0, 0, [upper, lower]);
        Assert.Equal(first.Contacts.ToArray(), second.Contacts.ToArray());
        Assert.Equal(first.Bounds, second.Bounds);
        Assert.Equal(first.WorldVelocity, second.WorldVelocity);
    }

    [Fact]
    public void AFullManifoldExceedingBudgetFailsAtomically()
    {
        var support = new MovingPlatformTrajectory(7,
            Box(-10, -0.25, 10, 0), new Vector2D(2, 0), 2);
        var lower = new StaticAabbCollider(5, Box(1, 0, 2, 0.5));
        var upper = new StaticAabbCollider(3, Box(1, 0.5, 2, 1.5));
        var error = Assert.Throws<SimulationEventLimitException>(() =>
            MovingPlatformRideEventScheduler.Step(support, 0, 0.25,
                Box(0, 0, 1, 1), 0, 0, [lower, upper],
                maxContactEvents: 1));
        Assert.Equal(new[] { 3, 5 }, error.Contacts.Select(c => c.ColliderId));
    }

    [Fact]
    public void RisingSupportedBodyAgainstCeilingIsClassifiedCrush()
    {
        var lift = new MovingPlatformTrajectory(7,
            Box(-2, -0.25, 2, 0), new Vector2D(0, 2), 2);
        var ceiling = new StaticAabbCollider(3, Box(-2, 1.6, 2, 2));
        var failure = Assert.Throws<CharacterCrushException>(() =>
            MovingPlatformRideEventScheduler.Step(lift, 0, 0.25,
                Box(0, 0, 0.8, 1.6), 0, 0, [ceiling]));
        Assert.Equal(CharacterCrushAxis.Vertical, failure.Crush.Axis);
        Assert.Equal(7, failure.Crush.PushingColliderId);
        Assert.Equal(3, failure.Crush.OpposingColliderId);
    }

    [Fact]
    public void MovingSideContactWhileRidingIsIncludedInCollisionArbitration()
    {
        var support = new MovingPlatformTrajectory(7,
            Box(-10, -0.25, 10, 0), new Vector2D(2, 0), 2);
        var other = new MovingPlatformTrajectory(8,
            Box(2, 0.2, 3, 0.8), new Vector2D(-2, 0), 2);
        var result = MovingPlatformRideEventScheduler.Step(support, 0, 0.75,
            Box(0, 0, 1, 1), 0, 0, Array.Empty<StaticAabbCollider>(), [other]);
        Assert.Single(result.Contacts);
        Assert.Equal(8, result.Contacts[0].ColliderId);
        Assert.InRange(Math.Abs(result.AdvancedSeconds - 0.5), 0, 1e-8);
        Assert.Equal(-1.0, result.WorldVelocity.X);
        Assert.Null(result.StaticContact);
        Assert.True(result.IsSupported);
        Assert.Equal(7, result.SupportPlatformId);
    }

    [Fact]
    public void MovingPusherAndWallAtExactRideStartProduceHorizontalCrush()
    {
        var support = new MovingPlatformTrajectory(7,
            Box(-10, -0.25, 10, 0), new Vector2D(2, 0), 2);
        var wall = new StaticAabbCollider(3, Box(1, 0, 2, 2));
        var pusher = new MovingPlatformTrajectory(8,
            Box(-1, 0.2, 0, 0.8), new Vector2D(4, 0), 2);
        var failure = Assert.Throws<CharacterCrushException>(() =>
            MovingPlatformRideEventScheduler.Step(support, 0, 0.25,
                Box(0, 0, 1, 1), 0, 0, [wall], [pusher]));
        Assert.Equal(CharacterCrushAxis.Horizontal, failure.Crush.Axis);
        Assert.Equal(8, failure.Crush.PushingColliderId);
        Assert.Equal(3, failure.Crush.OpposingColliderId);
    }

    [Fact]
    public void ExistingSingleWallRideRetainsItsPreviousContactAndSupport()
    {
        var support = new MovingPlatformTrajectory(7,
            Box(-1, 0, 3, 0.25), new Vector2D(2, 0), 2);
        var wall = new StaticAabbCollider(2, Box(3, 0.25, 3.5, 2));
        var result = MovingPlatformRideEventScheduler.Step(support, 0, 0.5,
            Box(2, 0.25, 2.8, 1.85), 0, 0, [wall]);
        Assert.Single(result.Contacts);
        Assert.Equal(result.StaticContact!.Value, result.Contacts[0]);
        Assert.Equal(7, result.SupportPlatformId);
        Assert.True(result.IsSupported);
        Assert.Equal(0.0, result.WorldVelocity.X);
    }

    [Fact]
    public void DescendingSupportCanHandoffToStaticFloorWithoutLosingGrounding()
    {
        var lift = new MovingPlatformTrajectory(7,
            Box(-2, -0.25, 2, 0), new Vector2D(0, -2), 2);
        var floor = new StaticAabbCollider(3, Box(-2, -1, 2, 0));
        var result = MovingPlatformRideEventScheduler.Step(lift, 0, 0.25,
            Box(0, 0, 0.8, 1.6), 0, 0, [floor]);
        Assert.Null(result.SupportPlatformId);
        Assert.False(result.IsSupported);
        Assert.Equal(0.0, result.WorldVelocity.Y);
        Assert.Equal(3, result.StaticContact!.Value.ColliderId);
        Assert.Equal(0.0, result.Bounds.Bottom);
    }

}
