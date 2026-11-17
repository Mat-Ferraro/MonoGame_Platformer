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
/// A second, independently selectable F4 fixture built from reusable level
/// content. Tests anchored spawn motion, stable player/character/device IDs,
/// reassignment, late join, and player-only respawn without changing the
/// validated original shared-clock or crush gyms.
/// </summary>
public static class AuthoredWorldGym
{
    public const int FirstPlayerId = 1;
    public const int SecondPlayerId = 2;
    public const int ThirdPlayerId = 3;
    public const int LeftPlatformId = 201;
    public const int RightPlatformId = 202;
    public const string LeftSpawn = "LEFT_RIDE";
    public const string GroundSpawn = "GROUND";
    public const string RightSpawn = "RIGHT_RIDE";
    public const string GuestSpawn = "RIGHT_GUEST";

    public static WorldDefinition CreateDefinition() => new(
        "GYM_TWO_PLATFORMS",
        [new StaticAabbCollider(200, Box(-16, -2.0, 16, -1.5))],
        [
            new MovingPlatformTrajectory(LeftPlatformId,
                Box(-5.0, 0.5, -1.0, 0.8), new Vector2D(0.9, 0.0), 1.8),
            new MovingPlatformTrajectory(RightPlatformId,
                Box(1.5, 0.4, 5.5, 0.7), new Vector2D(-0.7, 0.0), 2.5)
        ],
        [
            new WorldSpawnPoint(LeftSpawn, Box(-4.5, 0.8, -3.7, 2.4), LeftPlatformId),
            new WorldSpawnPoint(GroundSpawn, Box(-0.4, -1.5, 0.4, 0.1)),
            new WorldSpawnPoint(RightSpawn, Box(3.3, 0.7, 4.1, 2.3), RightPlatformId),
            new WorldSpawnPoint(GuestSpawn, Box(2.0, 0.7, 2.8, 2.3), RightPlatformId)
        ],
        [new WorldTriggerDefinition(301, "CHECKPOINT", Box(-1.2, -1.45, 1.2, 1.0))]);

    public static WorldSession Create(
        CollisionAssistancePolicy? assistance = null,
        SimulationFaultSensitivity sensitivity = SimulationFaultSensitivity.Strict,
        int firstPlayerInputSource = 1,
        long initialWorldTick = 0,
        long worldInstanceId = 1)
    {
        var ground = HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Ground);
        var air = HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Air);
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        var world = new WorldSession(CreateDefinition(), ground, air, jump,
            gravityAcceleration: -24.0,
            fixedStepSeconds: MovementGymSession.FixedStepSeconds,
            initialWorldTick: initialWorldTick,
            releaseConstraints: new JumpReleaseConstraints(1.10),
            assistancePolicy: assistance ?? MovementGymSession.PlayableAssistancePreset,
            faultPolicy: new SimulationFaultPolicy(sensitivity),
            worldInstanceId: worldInstanceId);
        world.AddPlayer(new WorldPlayerSlotRegistration(FirstPlayerId, 1001,
            LeftSpawn, firstPlayerInputSource));
        world.AddPlayer(new WorldPlayerSlotRegistration(SecondPlayerId, 1002,
            RightSpawn, 22));
        return world;
    }

    public static void JoinGuest(WorldSession world)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.AddPlayer(new WorldPlayerSlotRegistration(ThirdPlayerId, 1003,
            GuestSpawn));
    }

    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));
}
