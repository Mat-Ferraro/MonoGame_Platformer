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
/// Isolated F4/F1 collision-safe motion lab. It deliberately uses real static
/// walls; every source of motion must go through the authoritative solver.
/// No per-frame velocity or position writes from the MonoGame host.
/// </summary>
public static class MotionContributionGym
{
    public const int FirstPlayerId = 1;
    public const int SecondPlayerId = 2;
    public const string PlayerOneSpawn = "LAB_LEFT";
    public const string PlayerTwoSpawn = "LAB_RIGHT";

    public static double NormalWalkingSpeed =>
        HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Ground).MaximumSpeed;

    public static double WindLeft75 => -0.75 * NormalWalkingSpeed;
    public static double WindRight75 => 0.75 * NormalWalkingSpeed;

    public static double KnockbackAgainstFacing(WorldMotionSnapshot state) =>
        -state.FacingDirection * NormalWalkingSpeed * 1.75;

    public static WorldDefinition CreateDefinition() => new(
        "GYM_MOTION_LAB",
        [
            new StaticAabbCollider(401, Box(-25, -1.0, 25, -0.5)),
            new StaticAabbCollider(402, Box(-18.5, -0.5, -18, 4)),
            new StaticAabbCollider(403, Box(18, -0.5, 18.5, 4))
        ],
        Array.Empty<MovingPlatformTrajectory>(),
        [
            new WorldSpawnPoint(PlayerOneSpawn, Box(-4.5, -0.5, -3.7, 1.1)),
            new WorldSpawnPoint(PlayerTwoSpawn, Box(3.5, -0.5, 4.3, 1.1))
        ]);

    public static WorldSession Create(
        CollisionAssistancePolicy? assistance = null,
        SimulationFaultSensitivity sensitivity = SimulationFaultSensitivity.Strict,
        int firstPlayerInputSource = 1,
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
            releaseConstraints: new JumpReleaseConstraints(1.10),
            assistancePolicy: assistance ?? MovementGymSession.PlayableAssistancePreset,
            faultPolicy: new SimulationFaultPolicy(sensitivity),
            worldInstanceId: worldInstanceId);
        world.AddPlayer(new WorldPlayerSlotRegistration(FirstPlayerId, 1001,
            PlayerOneSpawn, firstPlayerInputSource));
        world.AddPlayer(new WorldPlayerSlotRegistration(SecondPlayerId, 1002,
            PlayerTwoSpawn, 22));
        return world;
    }

    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));
}
