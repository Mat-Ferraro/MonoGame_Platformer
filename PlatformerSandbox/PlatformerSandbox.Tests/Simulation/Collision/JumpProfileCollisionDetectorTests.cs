using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class JumpProfileCollisionDetectorTests
{
    private static Aabb Body => Box(0, 0, 1, 1);
    private static Aabb Box(double x, double y, double width, double height) =>
        new(new Vector2D(x, y), new Vector2D(x + width, y + height));

    private static HorizontalMotionProfile Horizontal() =>
        HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));

    private static JumpFlightTrajectory Held(bool apexHang = false) =>
        JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            apexHang ? new JumpConstraints(4, 0.4, 0.3, 0.1, 0.35)
                     : new JumpConstraints(4, 0.4, 0.3)));

    private static JumpReferenceCollisionPlan Plan(
        HorizontalDirection direction = HorizontalDirection.Right,
        JumpFlightTrajectory? flight = null,
        double initialVelocity = 0.0) =>
        JumpReferenceCollisionPlan.Create(flight ?? Held(), Horizontal(),
            [new JumpTravelInputChange(0, direction)], initialVelocity);

    private static StaticAabbCollider Obstacle(int id, Aabb bounds) => new(id, bounds);

    [Fact]
    public void NoObstaclesMeansNoCollision()
    {
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, Plan(), []);
        Assert.Null(hit);
    }

    [Fact]
    public void FindsRightWallAtAnalyticallyKnownTime()
    {
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, Plan(),
            [Obstacle(4, Box(2, -1, 0.2, 10))]);
        Assert.True(hit.HasValue);
        Close(0.2, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
        Assert.Equal(4, hit.Value.ColliderId);
        Close(2, hit.Value.MovingBoundsAtImpact.Right);
        Close(10, hit.Value.VelocityAtImpact.X);
    }

    [Fact]
    public void FindsFirstOfTwoWallsRegardlessOfEnumerationOrder()
    {
        var far = Obstacle(1, Box(4, -1, 0.2, 10));
        var near = Obstacle(9, Box(2, -1, 0.2, 10));
        var a = JumpProfileCollisionDetector.SweepFirst(Body, Plan(), [far, near]);
        var b = JumpProfileCollisionDetector.SweepFirst(Body, Plan(), [near, far]);
        Assert.Equal(9, a!.Value.ColliderId);
        Assert.Equal(a.Value, b!.Value);
    }

    [Fact]
    public void ExactSimultaneousContactsUseLowerColliderId()
    {
        var wall1 = Obstacle(50, Box(2, -1, 0.2, 10));
        var wall2 = Obstacle(3, Box(2, -1, 0.2, 10));
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, Plan(), [wall1, wall2]);
        Assert.Equal(3, hit!.Value.ColliderId);
    }

    [Fact]
    public void CeilingHitUsesActualCurvedRiseRatherThanChord()
    {
        var plan = Plan(HorizontalDirection.Neutral);
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, plan,
            [Obstacle(2, Box(-10, 2, 20, 0.1))]);
        Assert.True(hit.HasValue);
        Assert.InRange(hit.Value.TimeSeconds, 0.05, 0.06);
        Assert.Equal(new Vector2D(0, -1), hit.Value.Normal);
        Close(2, hit.Value.MovingBoundsAtImpact.Top);
        Assert.True(hit.Value.VelocityAtImpact.Y > 0);
    }

    [Fact]
    public void PowerCurveApexHangHasContinuousCollisionDetection()
    {
        var plan = Plan(HorizontalDirection.Neutral, Held(apexHang: true));
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, plan,
            [Obstacle(2, Box(-10, 3.6, 20, 0.15))]);
        Assert.True(hit.HasValue);
        Assert.InRange(hit.Value.TimeSeconds, 0.0, 0.4);
        Assert.Equal(new Vector2D(0, -1), hit.Value.Normal);
        Close(3.6, hit.Value.MovingBoundsAtImpact.Top);
    }

    [Fact]
    public void ThinCeilingIsDetectedWithoutSubstepSampling()
    {
        var plan = Plan(HorizontalDirection.Neutral, Held(apexHang: true));
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, plan,
            [Obstacle(1, Box(-10, 3.125, 20, 0.001))]);
        Assert.True(hit.HasValue);
        Close(3.125, hit.Value.MovingBoundsAtImpact.Top);
    }

    [Fact]
    public void ApexTangentOnlyIsNotPenetratingContact()
    {
        var hit = JumpProfileCollisionDetector.SweepFirst(Body,
            Plan(HorizontalDirection.Neutral),
            [Obstacle(7, Box(-10, 5, 20, 0.1))]);
        Assert.Null(hit);
    }

    [Fact]
    public void TouchingFloorAtTakeoffDoesNotStopUpwardJump()
    {
        var hit = JumpProfileCollisionDetector.SweepFirst(Body,
            Plan(HorizontalDirection.Neutral),
            [Obstacle(7, Box(-10, -0.5, 20, 0.5))]);
        Assert.True(hit.HasValue);
        Close(0.7, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(0, 1), hit.Value.Normal);
    }

    [Fact]
    public void LandingAtEndOfFlightCountsAsImpact()
    {
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, Plan(),
            [Obstacle(4, Box(-10, -0.5, 30, 0.5))]);
        Assert.True(hit.HasValue);
        Close(0.7, hit.Value.TimeSeconds);
        Assert.True(hit.Value.VelocityAtImpact.Y < 0);
    }

    [Fact]
    public void StartsFlushWithWallAndAcceleratesIntoIt()
    {
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, Plan(),
            [Obstacle(4, Box(1, -1, 0.1, 10))]);
        Assert.True(hit.HasValue);
        Close(0, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
        Close(0, hit.Value.VelocityAtImpact.X);
    }

    [Fact]
    public void StartsFlushWithWallAndMovesAwayWithoutContact()
    {
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, Plan(),
            [Obstacle(4, Box(-1, -1, 1, 10))]);
        Assert.Null(hit);
    }

    [Fact]
    public void ParallelMotionAlongWallIsNotBlocking()
    {
        var hit = JumpProfileCollisionDetector.SweepFirst(Body,
            Plan(HorizontalDirection.Neutral),
            [Obstacle(4, Box(1, -1, 1, 10))]);
        Assert.Null(hit);
    }

    [Fact]
    public void ReversalCanStrikeWallOnTheOppositeSideAfterTurning()
    {
        var plan = Plan(HorizontalDirection.Left, initialVelocity: 10);
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, plan,
            [Obstacle(6, Box(-1.2, -1, 0.7, 10))]);
        Assert.True(hit.HasValue);
        Assert.InRange(hit.Value.TimeSeconds, 0.35, 0.7);
        Assert.Equal(new Vector2D(1, 0), hit.Value.Normal);
        Assert.True(hit.Value.VelocityAtImpact.X < 0);
    }

    [Fact]
    public void InputChangeToNeutralStillFindsImpactDuringBraking()
    {
        var flight = Held();
        var schedule = new JumpTravelInputChange[]
        {
            new(0, HorizontalDirection.Right),
            new(0.3, HorizontalDirection.Neutral)
        };
        var plan = JumpReferenceCollisionPlan.Create(flight, Horizontal(), schedule);
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, plan,
            [Obstacle(4, Box(3.4, -1, 0.1, 10))]);
        Assert.True(hit.HasValue);
        Assert.InRange(hit.Value.TimeSeconds, 0.3, 0.4);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void EarlyReleaseChangesCollisionTimeAndFlightEnd()
    {
        var held = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3));
        var released = JumpFlightSolver.ForEarlyRelease(
            held, new JumpReleaseConstraints(1), 0);
        var plan = Plan(HorizontalDirection.Neutral, released);
        Close(0.25, plan.TotalDurationSeconds);
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, plan,
            [Obstacle(2, Box(-10, 1.5, 20, 0.1))]);
        Assert.True(hit.HasValue);
        Assert.InRange(hit.Value.TimeSeconds, 0, released.ApexTimeSinceTakeoffSeconds);
        Assert.Equal(new Vector2D(0, -1), hit.Value.Normal);
    }

    [Fact]
    public void EarlyReleaseCannotReachCeilingAboveShortenedApex()
    {
        var held = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3));
        var released = JumpFlightSolver.ForEarlyRelease(
            held, new JumpReleaseConstraints(1), 0);
        var hit = JumpProfileCollisionDetector.SweepFirst(Body,
            Plan(HorizontalDirection.Neutral, released),
            [Obstacle(2, Box(-10, 2.5, 20, 0.1))]);
        Assert.Null(hit);
    }

    [Fact]
    public void MidAscentReleaseCanHitCeilingDuringShortenedAscent()
    {
        var held = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3));
        var released = JumpFlightSolver.ForEarlyRelease(
            held, new JumpReleaseConstraints(1), 0.2);
        var hit = JumpProfileCollisionDetector.SweepFirst(Body,
            Plan(HorizontalDirection.Neutral, released),
            [Obstacle(2, Box(-10, 4.15, 20, 0.1))]);
        Assert.True(hit.HasValue);
        Assert.InRange(hit.Value.TimeSeconds, 0.2, released.ApexTimeSinceTakeoffSeconds);
        Assert.Equal(new Vector2D(0, -1), hit.Value.Normal);
    }

    [Fact]
    public void EarlyReleaseCanLandOnPlatformReachedOnlyDuringDescent()
    {
        var held = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3));
        var released = JumpFlightSolver.ForEarlyRelease(
            held, new JumpReleaseConstraints(1), 0.2);
        var hit = JumpProfileCollisionDetector.SweepFirst(Body,
            Plan(HorizontalDirection.Right, released),
            [Obstacle(2, Box(2.8, 1.8, 4, 0.2))]);
        Assert.True(hit.HasValue);
        Assert.InRange(hit.Value.TimeSeconds,
            released.ApexTimeSinceTakeoffSeconds, released.TotalDurationSeconds);
        Assert.Equal(new Vector2D(0, 1), hit.Value.Normal);
        Close(2.0, hit.Value.MovingBoundsAtImpact.Bottom);
    }

    [Fact]
    public void MidairEarlyReleaseAndApexHangCanBothBeSwept()
    {
        var held = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3, 0.1, 0.35));
        var cut = JumpFlightSolver.ForEarlyRelease(
            held, new JumpReleaseConstraints(1), 0.15);
        var hit = JumpProfileCollisionDetector.SweepFirst(Body,
            Plan(HorizontalDirection.Neutral, cut),
            [Obstacle(8, Box(-5, 2.1, 10, 0.1))]);
        Assert.True(hit.HasValue);
        Assert.True(hit.Value.TimeSeconds < cut.ApexTimeSinceTakeoffSeconds);
    }

    [Fact]
    public void StartingPenetrationIsRejectedWithoutImplicitDepenetration()
    {
        Assert.Throws<InvalidOperationException>(() =>
            JumpProfileCollisionDetector.SweepFirst(Body, Plan(),
                [Obstacle(4, Box(0.5, 0.5, 1, 1))]));
    }

    [Fact]
    public void DuplicateStableIdsAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            JumpProfileCollisionDetector.SweepFirst(Body, Plan(),
                [Obstacle(4, Box(2, -1, 1, 4)), Obstacle(4, Box(8, -1, 1, 4))]));
    }

    [Fact]
    public void NullColliderIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            JumpProfileCollisionDetector.SweepFirst(Body, Plan(),
                [null!]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidAlignmentToleranceIsRejected(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JumpProfileCollisionDetector.SweepFirst(Body, Plan(), [], value));
    }

    [Fact]
    public void DefaultAabbIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            JumpProfileCollisionDetector.SweepFirst(default, Plan(), []));
    }

    [Fact]
    public void ExplicitlyZeroToleranceStillWorksForExactContactAtStart()
    {
        var hit = JumpProfileCollisionDetector.SweepFirst(Body, Plan(),
            [Obstacle(1, Box(1, -1, 1, 10))], 0);
        Assert.True(hit.HasValue);
        Close(0, hit.Value.TimeSeconds);
    }

    private static void Close(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-7);
}
