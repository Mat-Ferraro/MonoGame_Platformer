using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class ElevatorSideGrazingEventLoopTests
{
    private static readonly MovingPlatformTrajectory Lift = new(
        8, new Aabb(new Vector2D(-7.7, -0.35), new Vector2D(-6.2, 0.0)),
        new Vector2D(0, 3), 2.0);

    [Fact]
    public void DescendingLiftAtCornerDoesNotAlternateCeilingAndFloorContacts()
    {
        // Reproduces the reported event time and X side overlap of ~2e-15.
        // Use the widened bank/pit fixture so a width-0.8 player is not
        // numerically wedged between two exact, opposing horizontal faces.
        double t = 3.3666666666666596;
        Aabb body = new(
            new Vector2D(-6.200000000000002, -1),
            new Vector2D(-5.4, 0.6));
        var floor = new StaticAabbCollider(10,
            new Aabb(new Vector2D(-8.4, -1.5), new Vector2D(-5.2, -1)));
        var bank = new StaticAabbCollider(9,
            new Aabb(new Vector2D(-5.2, -1), new Vector2D(6, 0)));

        var result = UnifiedAcceleratedKinematicStepper.Step(
            body, Vector2D.Zero, new Vector2D(0, -24),
            t, 1.0 / 60, [floor, bank], [Lift]);

        Assert.True(result.AdvancedSeconds > 0);
        Assert.Equal(Lift.Evaluate(t).Bounds.Right, result.Bounds.Left);
        Assert.False(AabbCollisionDetector.Overlaps(result.Bounds,
            Lift.Evaluate(t + result.AdvancedSeconds).Bounds));
        Assert.DoesNotContain(result.Contacts,
            c => c.ColliderId == 8 && c.Normal.Y != 0);
    }

    [Fact]
    public void CornerGrazeDoesNotCountAsVerticalCrushButRealCoverageDoes()
    {
        // At t=3.30 -> 3.40 the descending underside closes past the
        // player head (floor at y=-1, player height 1.6).
        double t = 3.30;
        double duration = 0.10;
        var floor = new StaticAabbCollider(10,
            new Aabb(new Vector2D(-8.4, -1.5), new Vector2D(-5.2, -1)));
        Aabb corner = new(
            new Vector2D(-6.200000000000002, -1),
            new Vector2D(-5.4, 0.6));
        Assert.Null(OpposingSurfaceCrushDetector.AboveStaticGround(
            corner, corner, t, duration, [floor], [Lift]));
        Aabb covered = new(
            new Vector2D(-6.3, -1), new Vector2D(-5.5, 0.6));
        Assert.NotNull(OpposingSurfaceCrushDetector.AboveStaticGround(
            covered, covered, t, duration, [floor], [Lift]));
    }
}
