using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;
using Xunit;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>
/// Integration guard: the LDtk floor-precision handoff must retain the
/// Milestone 26 simultaneous-contact/crush contract. Both families of
/// behavior are required in the same stepper implementation.
/// </summary>
public sealed class SimultaneousCrushFloorHandoffIntegrationTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    [Fact]
    public void FloorPrecisionHandoff_DoesNotReplaceImmediateCrushWithEventLimit()
    {
        Aabb body = Box(0, 0, 1, 1);
        var wall = new StaticAabbCollider(3, Box(1, -1, 2, 2));
        var pusher = new MovingPlatformTrajectory(
            7, Box(-1, 0, 0, 1), new Vector2D(2, 0), 1);

        CharacterCrushException failure = Assert.Throws<CharacterCrushException>(() =>
            UnifiedAcceleratedKinematicStepper.Step(
                body, new Vector2D(1, 0), Vector2D.Zero,
                0, 0.25, [wall], [pusher]));

        Assert.Equal(0, failure.Crush.WorldTimeSeconds);
        Assert.Equal(3, failure.Crush.OpposingColliderId);
        Assert.Equal(7, failure.Crush.PushingColliderId);
    }

    [Fact]
    public void FloorPrecisionHandoff_DoesNotReplaceInteriorCrushWithEventLimit()
    {
        Aabb body = Box(0, 0, 1, 1);
        var wall = new StaticAabbCollider(3, Box(1.5, -1, 2.5, 2));
        var pusher = new MovingPlatformTrajectory(
            7, Box(-1.5, 0, -0.5, 1), new Vector2D(2, 0), 1);

        CharacterCrushException failure = Assert.Throws<CharacterCrushException>(() =>
            UnifiedAcceleratedKinematicStepper.Step(
                body, new Vector2D(1, 0), Vector2D.Zero,
                0, 0.75, [wall], [pusher]));

        Assert.Equal(0.5, failure.Crush.WorldTimeSeconds);
        Assert.Equal(3, failure.Crush.OpposingColliderId);
        Assert.Equal(7, failure.Crush.PushingColliderId);
    }
}
