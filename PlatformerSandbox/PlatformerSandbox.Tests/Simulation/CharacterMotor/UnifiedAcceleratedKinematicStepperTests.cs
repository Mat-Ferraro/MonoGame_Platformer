using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class UnifiedAcceleratedKinematicStepperTests
{
    private static Aabb Box(double x, double y, double w = 1, double h = 1) =>
        new(new Vector2D(x, y), new Vector2D(x + w, y + h));

    private static void Near(double expected, double actual, double tolerance = 1e-7) =>
        Assert.InRange(Math.Abs(expected - actual), 0, tolerance);

    [Fact]
    public void UnobstructedMovementUsesExactAnalyticalKinematics()
    {
        var result = UnifiedAcceleratedKinematicStepper.Step(
            Box(0, 0), new Vector2D(2, 3), new Vector2D(0, -2),
            0, 1,
            Array.Empty<StaticAabbCollider>(),
            Array.Empty<MovingPlatformTrajectory>());
        Near(2, result.Bounds.Left);
        Near(2, result.Bounds.Bottom);
        Near(1, result.Velocity.Y);
        Near(1, result.AdvancedSeconds);
        Assert.Empty(result.Contacts);
    }

    [Fact]
    public void TwoContactsCanOccurWithinOneUnifiedUpdate()
    {
        // Right-hand wall hits at t=0.2. The body then travels vertically
        // and hits a horizontal platform moving sideways at t=0.4.
        var wall = new StaticAabbCollider(1, Box(3, -1, 1, 12));
        var ceiling = new MovingPlatformTrajectory(7,
            Box(1.5, 5, 9, 1), new Vector2D(2, 0), 4);
        var result = UnifiedAcceleratedKinematicStepper.Step(
            Box(0, 0), new Vector2D(10, 10), Vector2D.Zero,
            0, 0.6, [wall], [ceiling]);
        Assert.Equal(2, result.Contacts.Count);
        Assert.Equal(1, result.Contacts[0].ColliderId);
        Assert.Equal(new Vector2D(-1, 0), result.Contacts[0].Normal);
        Assert.Equal(7, result.Contacts[1].ColliderId);
        Assert.Equal(new Vector2D(0, -1), result.Contacts[1].Normal);
        Near(0.2, result.Contacts[0].TimeWithinStepSeconds);
        Near(0.4, result.Contacts[1].TimeWithinStepSeconds);
        Near(0.6, result.AdvancedSeconds);
    }

    [Fact]
    public void MovingPlatformPushesStationaryCharacterFromSide()
    {
        var platform = new MovingPlatformTrajectory(7, Box(0, 0),
            new Vector2D(2, 0), 1);
        var result = UnifiedAcceleratedKinematicStepper.Step(
            Box(2, 0), Vector2D.Zero, Vector2D.Zero,
            0, 1,
            Array.Empty<StaticAabbCollider>(), [platform]);
        Assert.Single(result.Contacts);
        Assert.Equal(7, result.Contacts[0].ColliderId);
        Assert.Equal(new Vector2D(1, 0), result.Contacts[0].Normal);
        Near(2, result.Velocity.X);
        Near(3, result.Bounds.Left);
    }

    [Fact]
    public void MovingFloorLandingReturnsAnExactSupportHandoff()
    {
        var platform = new MovingPlatformTrajectory(7, Box(-2, -1, 6, 1),
            new Vector2D(2, 0), 2);
        var result = UnifiedAcceleratedKinematicStepper.Step(
            Box(1, 2), new Vector2D(0, -2), Vector2D.Zero,
            0, 2,
            Array.Empty<StaticAabbCollider>(), [platform]);
        Assert.Equal(7, result.MovingFloorId);
        Near(1, result.AdvancedSeconds);
        Near(0, result.Bounds.Bottom);
        Assert.Equal(new Vector2D(0, 1), Assert.Single(result.Contacts).Normal);
    }

    [Fact]
    public void StopMeansHaltInWorldSpaceNotPlatformRelativeSpace()
    {
        var platform = new MovingPlatformTrajectory(7, Box(0, 0),
            new Vector2D(2, 0), 1);
        var result = UnifiedAcceleratedKinematicStepper.Step(
            Box(2, 0), Vector2D.Zero, Vector2D.Zero,
            0, 1, Array.Empty<StaticAabbCollider>(), [platform],
            responseMode: CollisionResponseMode.Stop);
        Assert.Equal(Vector2D.Zero, result.Velocity);
        Near(0.5, result.AdvancedSeconds);
        Assert.Single(result.Contacts);
    }

    [Fact]
    public void CollisionEventBudgetIsNeverSilentlyExceeded()
    {
        var wall = new StaticAabbCollider(1, Box(3, -1, 1, 12));
        var ceiling = new MovingPlatformTrajectory(7,
            Box(1.5, 5, 9, 1), new Vector2D(2, 0), 4);
        Assert.Throws<InvalidOperationException>(() =>
            UnifiedAcceleratedKinematicStepper.Step(
                Box(0, 0), new Vector2D(10, 10), Vector2D.Zero,
                0, 0.6, [wall], [ceiling], maxContactEvents: 1));
    }

    [Fact]
    public void StrictModeFaultsOnSubResolutionRemainder()
    {
        var session = SimulationFaultPolicy.Development.StartStep();
        Assert.Throws<SimulationFaultException>(() =>
            UnifiedAcceleratedKinematicStepper.Step(
                Box(0, 0), Vector2D.Zero, Vector2D.Zero,
                20.0, 1e-18,
                Array.Empty<StaticAabbCollider>(),
                Array.Empty<MovingPlatformTrajectory>(), diagnostics: session));
    }

    [Fact]
    public void ResilientModeConsumesOnlyBoundedSubResolutionRemainder()
    {
        var session = SimulationFaultPolicy.Release.StartStep();
        var result = UnifiedAcceleratedKinematicStepper.Step(
            Box(0, 0), Vector2D.Zero, Vector2D.Zero,
            20.0, 1e-18,
            Array.Empty<StaticAabbCollider>(),
            Array.Empty<MovingPlatformTrajectory>(), diagnostics: session);
        Near(1e-18, result.AdvancedSeconds, 1e-25);
        Assert.Equal(1, session.RecoveryCount);
        Assert.Empty(result.Contacts);
    }

    [Fact]
    public void RecoveryCannotSilentlyDiscardSignificantMovement()
    {
        var session = SimulationFaultPolicy.Release.StartStep();
        Assert.Throws<SimulationFaultException>(() =>
            UnifiedAcceleratedKinematicStepper.Step(
                Box(0, 0), new Vector2D(1e14, 0), Vector2D.Zero,
                20.0, 1e-18,
                Array.Empty<StaticAabbCollider>(),
                Array.Empty<MovingPlatformTrajectory>(), diagnostics: session));
    }

    [Fact]
    public void StartsOverlappingObstacleIsUnrecoverableInEveryMode()
    {
        var session = SimulationFaultPolicy.Release.StartStep();
        Assert.Throws<InvalidOperationException>(() =>
            UnifiedAcceleratedKinematicStepper.Step(
                Box(0, 0), Vector2D.Zero, Vector2D.Zero,
                0, 1, [new StaticAabbCollider(1, Box(0.5, 0))],
                Array.Empty<MovingPlatformTrajectory>(), diagnostics: session));
    }

    [Fact]
    public void ZeroDurationReturnsInitialStateWithoutEvents()
    {
        var result = UnifiedAcceleratedKinematicStepper.Step(
            Box(0, 0), new Vector2D(1, 2), new Vector2D(0, -9),
            0, 0, Array.Empty<StaticAabbCollider>(),
            Array.Empty<MovingPlatformTrajectory>());
        Near(0, result.AdvancedSeconds);
        Near(0, result.Bounds.Left);
        Assert.Equal(new Vector2D(1, 2), result.Velocity);
        Assert.Empty(result.Contacts);
    }
}
