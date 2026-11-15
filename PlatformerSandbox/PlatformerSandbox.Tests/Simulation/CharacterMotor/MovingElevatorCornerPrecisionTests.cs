using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class MovingElevatorCornerPrecisionTests
{
    private static readonly MovingPlatformTrajectory Elevator = new(
        8, new Aabb(new Vector2D(-7.7, -0.35), new Vector2D(-6.2, 0.0)),
        new Vector2D(0.0, 3.0), 2.0);

    // At t=3.366666666666658 the descending lift underside is approximately
    // y=0.6000000000000133; the player sits at the right-hand corner of
    // the lift, with only a few ULPs of X and Y overlap.
    private const double CornerTime = 3.366666666666658;
    private static Aabb CornerSliver() => new(
        new Vector2D(-6.200000000000003, -0.999999999999975),
        new Vector2D(-5.400000000000003, 0.600000000000025));

    [Fact]
    public void TinyLiftCornerOverlapAlignsToExactSideWithoutMovingVertically()
    {
        Aabb body = CornerSliver();
        Assert.True(AabbCollisionDetector.Overlaps(
            body, Elevator.Evaluate(CornerTime).Bounds));

        Aabb aligned = MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
            body, CornerTime, [Elevator], []);

        Assert.Equal(Elevator.Evaluate(CornerTime).Bounds.Right, aligned.Left);
        Assert.Equal(body.Bottom, aligned.Bottom);
        Assert.Equal(body.Top, aligned.Top);
        Assert.False(AabbCollisionDetector.Overlaps(
            aligned, Elevator.Evaluate(CornerTime).Bounds));

        // The strict query remains strict. No detector tolerance was altered.
        _ = UnifiedCollisionEventScheduler.SweepParabolicFirst(
            aligned, Vector2D.Zero, Vector2D.Zero, CornerTime,
            0.0, [], [Elevator]);
    }

    [Fact]
    public void NearVerticalTouchWithPrecisionSideOverhangAlignsBeforeImpact()
    {
        // At the reported crash time the lift is still just above the head.
        // Older cleanup required a Y overlap and missed this corner.
        double time = 3.3666666666666596;
        Aabb lift = Elevator.Evaluate(time).Bounds;
        Aabb body = new(
            new Vector2D(-6.200000000000002, -1.0),
            new Vector2D(-5.4, lift.Bottom - 2e-14));
        Assert.True(body.Left < lift.Right);
        Assert.False(AabbCollisionDetector.Overlaps(body, lift));

        Aabb aligned = MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
            body, time, [Elevator], []);
        Assert.Equal(lift.Right, aligned.Left);
        Assert.Equal(body.Bottom, aligned.Bottom);
        Assert.Equal(body.Top, aligned.Top);
    }

    [Fact]
    public void FarAwayVerticalGapNeverTriggersSideCorrection()
    {
        Aabb body = new(
            new Vector2D(-6.200000000000002, -1),
            new Vector2D(-5.4, -0.6));
        Assert.Equal(body, MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
            body, 0.0, [Elevator], []));
    }

    [Fact]
    public void MeaningfulSidePenetrationIsNotSilentlyRepaired()
    {
        Aabb body = new(
            new Vector2D(-6.21, -0.999999999999975),
            new Vector2D(-5.41, 0.600000000000025));
        Assert.Equal(body,
            MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
                body, CornerTime, [Elevator], []));

        Assert.Throws<InvalidOperationException>(() =>
            UnifiedCollisionEventScheduler.SweepParabolicFirst(
                body, Vector2D.Zero, Vector2D.Zero, CornerTime,
                0.0, [], [Elevator]));
    }

    [Fact]
    public void InitialPenetrationStillFailsAtStrictStepperEntry()
    {
        Aabb body = CornerSliver();
        Assert.Throws<InvalidOperationException>(() =>
            UnifiedAcceleratedKinematicStepper.Step(
                body, Vector2D.Zero, Vector2D.Zero,
                CornerTime, 1.0 / 60.0, [], [Elevator]));
    }

    [Fact]
    public void NonOverlappingSideDoesNotMove()
    {
        Aabb body = new(
            new Vector2D(-6.2, -1.0),
            new Vector2D(-5.4, 0.6));
        Assert.Equal(body,
            MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
                body, CornerTime, [Elevator], []));
    }

    [Fact]
    public void SideAlignmentCannotCreateStaticPenetration()
    {
        Aabb body = CornerSliver();
        // An intentionally conflicting static wall prevents the rightward
        // precision correction. Do not resolve a moving overlap by inventing
        // a new static overlap.
        var wall = new StaticAabbCollider(90, new Aabb(
            new Vector2D(body.Right, -1.2),
            new Vector2D(body.Right + 0.4, 1.0)));
        Assert.False(AabbCollisionDetector.Overlaps(body, wall.Bounds));
        Assert.Throws<InvalidOperationException>(() =>
            MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
                body, CornerTime, [Elevator], [wall]));
    }
}
