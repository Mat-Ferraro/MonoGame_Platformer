using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

/// <summary>
/// Regresses a Milestone 26 crash in the unchanged Milestone 24 input schedule.
/// A short known horizontal acceleration phase, at a nonzero absolute clock,
/// can be smaller than a world-clock ULP. The controller must normalize that
/// phase before submitting its prospective crush queries.
/// </summary>
public sealed class Milestone26SubResolutionCrushQueryTests
{
    [Fact]
    public void ResponsiveReplayReachesFrame338WithoutInvalidCrushQueryDuration()
    {
        var gym = new MovementGymSession(MovementGymTuning.Responsive,
            SimulationFaultSensitivity.Diagnostic);

        for (int frame = 0; frame <= 338; frame++)
        {
            int phase = frame % 240;
            HorizontalDirection direction = phase < 80 ? HorizontalDirection.Right :
                phase < 160 ? HorizontalDirection.Left : HorizontalDirection.Neutral;
            bool pressed = phase == 7 || phase == 105;
            bool released = phase == 12 || phase == 111;
            // Frame 338 was the first failure before the classifier was moved
            // behind the existing non-advancing phase guard.
            gym.Step(MovementGymSession.FixedStepSeconds,
                direction, pressed, released);
        }

        Assert.True(double.IsFinite(gym.ElapsedSeconds));
        Assert.Equal(0, gym.CheckpointFallbackCount);
    }

    [Fact]
    public void DirectCrushQueriesStillRejectNonadvancingAbsoluteTime()
    {
        var body = new Aabb(new Vector2D(0, 0), new Vector2D(0.8, 1.6));
        var floor = new StaticAabbCollider(1,
            new Aabb(new Vector2D(-2, -1), new Vector2D(2, 0)));
        var moving = new MovingPlatformTrajectory(7,
            new Aabb(new Vector2D(-1, 2), new Vector2D(2, 2.25)),
            new Vector2D(0, -1), 1.0);
        const double worldTime = 3.9;
        double subResolution = double.Epsilon;
        Assert.Equal(worldTime, worldTime + subResolution);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpposingSurfaceCrushDetector.AboveStaticGround(
                body, body, worldTime, subResolution, [floor], [moving]));
    }
}
