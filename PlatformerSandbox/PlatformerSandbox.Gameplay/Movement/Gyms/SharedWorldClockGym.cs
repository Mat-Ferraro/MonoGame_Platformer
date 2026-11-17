using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Gameplay.Movement.Gyms;

/// <summary>
/// Deliberately isolated two-player clock/respawn gym. Both characters start
/// riding the SAME moving platform; one can be respawned mid-trajectory.
/// This is a test fixture, not an authoritative game level schema.
/// </summary>
public static class SharedWorldClockGym
{
    public const int PlatformId = 101;
    public const int FloorId = 100;
    public const int FirstPlayerId = 1;
    public const int SecondPlayerId = 2;

    public static WorldSession Create(
        CollisionAssistancePolicy? assistance = null,
        SimulationFaultSensitivity sensitivity = SimulationFaultSensitivity.Strict,
        long worldInstanceId = 1)
    {
        var ground = HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Ground);
        var air = HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Air);
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        var world = new WorldSession(
            [new StaticAabbCollider(FloorId, Box(-24, -2.0, 24, -1.5))],
            [new MovingPlatformTrajectory(PlatformId,
                Box(-3.0, 0.2, 2.0, 0.5),
                new Vector2D(1.5, 0.0), 2.0)],
            ground, air, jump, gravityAcceleration: -24.0,
            fixedStepSeconds: MovementGymSession.FixedStepSeconds,
            releaseConstraints: new JumpReleaseConstraints(1.10),
            assistancePolicy: assistance ?? MovementGymSession.PlayableAssistancePreset,
            faultPolicy: new SimulationFaultPolicy(sensitivity),
            worldInstanceId: worldInstanceId);
        world.AddPlayer(FirstPlayerId, SpawnBounds(world, FirstPlayerId));
        world.AddPlayer(SecondPlayerId, SpawnBounds(world, SecondPlayerId));
        return world;
    }

    public static Aabb SpawnBounds(WorldSession world, int playerId)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (playerId is not (FirstPlayerId or SecondPlayerId))
            throw new ArgumentOutOfRangeException(nameof(playerId));
        MovingPlatformSample platform = world.MovingPlatforms.Single(
            sample => sample.PlatformId == PlatformId);
        double xOffset = playerId == FirstPlayerId ? 0.5 : 2.5;
        return Box(platform.Bounds.Left + xOffset, platform.Bounds.Top,
            platform.Bounds.Left + xOffset + 0.8, platform.Bounds.Top + 1.6);
    }

    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));
}
