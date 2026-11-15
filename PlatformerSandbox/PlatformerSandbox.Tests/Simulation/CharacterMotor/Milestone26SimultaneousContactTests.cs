using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class Milestone26SimultaneousContactTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    private static UnifiedCollisionHit Hit(int id, Vector2D normal,
        Vector2D surfaceVelocity, bool moving = false) =>
        new(id, moving, 0, normal, Box(0, 0, 1, 1),
            new Vector2D(1, -1), surfaceVelocity);

    [Fact]
    public void OrthogonalContactSetResolvesBothAxesWithoutContactLoop()
    {
        Aabb body = Box(0, 0, 1, 1);
        var wall = new StaticAabbCollider(8, Box(1, -2, 2, 2));
        var floor = new StaticAabbCollider(3, Box(-1, -1, 2, 0));
        UnifiedAcceleratedStepResult result = UnifiedAcceleratedKinematicStepper.Step(
            body, new Vector2D(1, -1), Vector2D.Zero, 0, .25,
            [wall, floor], Array.Empty<MovingPlatformTrajectory>());

        Assert.Equal(Vector2D.Zero, result.Velocity);
        Assert.Equal(body, result.Bounds);
        Assert.Equal(new[] { 3, 8 }, result.Contacts.Select(c => c.ColliderId));
        Assert.All(result.Contacts, c => Assert.Equal(0, c.TimeWithinStepSeconds));
    }

    [Fact]
    public void CoincidentQueryIsIndependentOfColliderEnumerationOrder()
    {
        Aabb body = Box(0, 0, 1, 1);
        var floor = new StaticAabbCollider(8, Box(-1, -1, 2, 0));
        var wall = new StaticAabbCollider(3, Box(1, -1, 2, 2));
        var velocity = new Vector2D(1, -1);
        var first = UnifiedCollisionEventScheduler.SweepParabolicCoincident(
            body, velocity, Vector2D.Zero, 0, .25, [floor, wall],
            Array.Empty<MovingPlatformTrajectory>());
        var reversed = UnifiedCollisionEventScheduler.SweepParabolicCoincident(
            body, velocity, Vector2D.Zero, 0, .25, [wall, floor],
            Array.Empty<MovingPlatformTrajectory>());
        Assert.Equal(new[] { 3, 8 }, first.Select(c => c.ColliderId));
        Assert.Equal(first.ToArray(), reversed.ToArray());
    }

    [Fact]
    public void MovingPusherAgainstStaticWallIsClassifiedAtConstraintTime()
    {
        Aabb body = Box(0, 0, 1, 1);
        var wall = new StaticAabbCollider(3, Box(1, -1, 2, 2));
        var pusher = new MovingPlatformTrajectory(7,
            Box(-1, 0, 0, 1), new Vector2D(2, 0), 1);
        CharacterCrushException exception = Assert.Throws<CharacterCrushException>(
            () => UnifiedAcceleratedKinematicStepper.Step(
                body, new Vector2D(1, 0), Vector2D.Zero,
                0, .25, [wall], [pusher]));
        Assert.Equal(CharacterCrushAxis.Horizontal, exception.Crush.Axis);
        Assert.Equal(7, exception.Crush.PushingColliderId);
        Assert.Equal(3, exception.Crush.OpposingColliderId);
        Assert.Equal(0, exception.Crush.WorldTimeSeconds);
        Assert.Equal(body, exception.Crush.BodyBounds);
    }

    [Fact]
    public void ClosingGapAtInteriorHalfSecondHasExactCrushTimestamp()
    {
        Aabb body = Box(0, 0, 1, 1);
        var wall = new StaticAabbCollider(3, Box(1.5, -1, 2.5, 2));
        var pusher = new MovingPlatformTrajectory(7,
            Box(-1.5, 0, -.5, 1), new Vector2D(2, 0), 1);
        var manifold = UnifiedCollisionEventScheduler.SweepParabolicCoincident(
            body, new Vector2D(1, 0), Vector2D.Zero,
            0, .75, [wall], [pusher]);
        Assert.Equal(new[] { 3, 7 }, manifold.Select(c => c.ColliderId));
        Assert.All(manifold, c => Assert.Equal(.5, c.TimeSeconds));
        CharacterCrushException failure = Assert.Throws<CharacterCrushException>(() =>
            UnifiedAcceleratedKinematicStepper.Step(body, new Vector2D(1, 0),
                Vector2D.Zero, 0, .75, [wall], [pusher]));
        Assert.Equal(.5, failure.Crush.WorldTimeSeconds);
        Assert.Equal(7, failure.Crush.PushingColliderId);
        Assert.Equal(3, failure.Crush.OpposingColliderId);
    }

    [Fact]
    public void MovingPlatformVersusMovingPlatformReportsStablePusherId()
    {
        var left = Hit(8, new Vector2D(1, 0), new Vector2D(2, 0), true);
        var right = Hit(4, new Vector2D(-1, 0), new Vector2D(-2, 0), true);
        CharacterCrushException exception = Assert.Throws<CharacterCrushException>(() =>
            SimultaneousContactResolver.Resolve(new Vector2D(0, 0),
                Vector2D.Zero, [left, right], CollisionResponseMode.Slide,
                1.5, Box(0, 0, 1, 1)));
        Assert.Equal(4, exception.Crush.PushingColliderId);
        Assert.Equal(8, exception.Crush.OpposingColliderId);
        Assert.Equal(1.5, exception.Crush.WorldTimeSeconds);
    }

    [Fact]
    public void OpposingContactsWithEqualVelocitiesAreSatisfiable()
    {
        var left = Hit(8, new Vector2D(1, 0), new Vector2D(2, 0), true);
        var right = Hit(4, new Vector2D(-1, 0), new Vector2D(2, 0), true);
        var resolved = SimultaneousContactResolver.Resolve(
            Vector2D.Zero, new Vector2D(-9, 0),
            [left, right], CollisionResponseMode.Slide, 0, Box(0, 0, 1, 1));
        Assert.Equal(2, resolved.Velocity.X);
        Assert.Equal(0, resolved.Acceleration.X);
    }

    [Fact]
    public void DifferentContactTimesAreNeverMerged()
    {
        Aabb body = Box(0, 0, 1, 1);
        var near = new StaticAabbCollider(8, Box(2, -1, 3, 2));
        var far = new StaticAabbCollider(3, Box(3, -1, 4, 2));
        var hits = UnifiedCollisionEventScheduler.SweepParabolicCoincident(
            body, new Vector2D(2, 0), Vector2D.Zero,
            0, 1, [far, near], Array.Empty<MovingPlatformTrajectory>());
        Assert.Single(hits);
        Assert.Equal(8, hits[0].ColliderId);
        Assert.Equal(.5, hits[0].TimeSeconds);
    }

    [Fact]
    public void StaticContactWithNonzeroSurfaceVelocityIsRejected()
    {
        var left = Hit(1, new Vector2D(1, 0), new Vector2D(1, 0));
        var right = Hit(2, new Vector2D(-1, 0), new Vector2D(-1, 0));
        Assert.Throws<ArgumentException>(() =>
            SimultaneousContactResolver.Resolve(Vector2D.Zero, Vector2D.Zero,
                [left, right], CollisionResponseMode.Slide, 0, Box(0, 0, 1, 1)));
    }

    [Fact]
    public void RejectsUnsortedTimestampMergingAndDuplicateIds()
    {
        var one = Hit(1, new Vector2D(1, 0), Vector2D.Zero);
        var two = Hit(2, new Vector2D(-1, 0), Vector2D.Zero)
            with { TimeSeconds = double.Epsilon };
        Assert.Throws<ArgumentException>(() => SimultaneousContactResolver.Resolve(
            Vector2D.Zero, Vector2D.Zero, [one, two],
            CollisionResponseMode.Slide, 0, Box(0, 0, 1, 1)));
        Assert.Throws<ArgumentException>(() => SimultaneousContactResolver.Resolve(
            Vector2D.Zero, Vector2D.Zero, [one, one],
            CollisionResponseMode.Slide, 0, Box(0, 0, 1, 1)));
    }

    [Fact]
    public void ActualInitialPenetrationIsStillRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            UnifiedCollisionEventScheduler.SweepParabolicCoincident(
                Box(0, 0, 1, 1), Vector2D.Zero, Vector2D.Zero,
                0, .1, [new StaticAabbCollider(5, Box(.5, 0, 1.5, 1))],
                Array.Empty<MovingPlatformTrajectory>()));
    }
}
