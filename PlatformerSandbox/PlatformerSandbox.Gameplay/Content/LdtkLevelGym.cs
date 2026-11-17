using System;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Gameplay.Content;

/// <summary>
/// Thin adapter from validated LDtk data to the same authoritative motor and
/// world clock used by every other gym. No LDtk rendering or physics fork.
/// </summary>
public static class LdtkLevelGym
{
    public static WorldSession Create(LdtkImportedLevel level,
        CollisionAssistancePolicy assistance,
        SimulationFaultSensitivity sensitivity,
        int inputSourceId,
        long worldInstanceId)
    {
        ArgumentNullException.ThrowIfNull(level);
        var ground = HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Ground);
        var air = HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Air);
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        var world = new WorldSession(level.World, ground, air, jump,
            gravityAcceleration: -24.0,
            fixedStepSeconds: MovementGymSession.FixedStepSeconds,
            releaseConstraints: new JumpReleaseConstraints(1.10),
            assistancePolicy: assistance,
            faultPolicy: new SimulationFaultPolicy(sensitivity),
            worldInstanceId: worldInstanceId);
        world.AddPlayer(new WorldPlayerSlotRegistration(1, 1001,
            level.StartSpawnId, inputSourceId));
        return world;
    }
}
