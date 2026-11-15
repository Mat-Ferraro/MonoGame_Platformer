using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Input.Buffering;
using PlatformerSandbox.Gameplay.Input.Requests;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Gameplay.Movement.Gyms;

/// <summary>
/// A tiny, deterministic gameplay test level. All authoritative motion is
/// delegated to CharacterMovementController; the MonoGame host only provides
/// discrete actions and reads the resulting state for presentation.
/// The stage and movement values are test fixtures, NOT engine defaults.
/// </summary>
public sealed class MovementGymSession
{
    public const double FixedStepSeconds = 1.0 / 60.0;
    public const double RespawnBelowY = -10.0;
    // Dedicated grounded step-assist test in the default Both gym.
    // Walk LEFT from the normal spawn. F9 toggles the assistance bundle.
    public const int GroundStepBumpColliderId = 16;
    public const double GroundStepBumpHeight = 0.10;

    // Playable gym default. Headless sessions retain their explicit unassisted
    // baseline unless they opt in through the constructor or SetAssistancePolicy.
    public static CollisionAssistancePolicy PlayableAssistancePreset { get; } = new(
        0.18, 2, groundStepHeightUnits: 0.12,
        groundStepInwardUnits: 0.04,
        ledgeCatchHorizontalUnits: 0.16,
        ledgeCatchVerticalUnits: 0.15);

    private static readonly Aabb SpawnBounds = new(
        new Vector2D(0.0, 0.0), new Vector2D(0.8, 1.6));
    // Start directly on the elevator for a reproducible riding test.
    // The lift occupies the existing pit, without intersecting static floors.
    private static readonly Aabb ElevatorSpawnBounds = new(
        new Vector2D(6.55, 0.0), new Vector2D(7.35, 1.6));

    private IReadOnlyList<StaticAabbCollider> _colliders;
    private HorizontalMotionProfile _groundMotion;
    private HorizontalMotionProfile _airMotion;
    private readonly JumpFlightTrajectory _jump;
    private MovingPlatformTrajectory _movingPlatform;
    private MovingPlatformTrajectory[] _additionalMovingPlatforms;
    private readonly JumpReleaseConstraints _jumpRelease = new(1.10);
    private CharacterMovementController _motor;
    private SimulationFaultPolicy _faultPolicy;
    private MovementGymCrushPolicy _crushPolicy;
    private CollisionAssistancePolicy _assistancePolicy = CollisionAssistancePolicy.Disabled;
    private int _consecutiveCheckpointFallbacks;
    private readonly BufferedJumpInput _jumpTiming = new(
        new JumpTimingConstraints(CoyoteTimeSeconds, JumpBufferSeconds));

    public const double CoyoteTimeSeconds = 0.10;
    public const double JumpBufferSeconds = 0.10;

    public MovementGymSession(
        MovementGymTuning? tuning = null,
        SimulationFaultSensitivity faultSensitivity = SimulationFaultSensitivity.Strict,
        MovingPlatformGymMode platformMode = MovingPlatformGymMode.Both,
        MovementGymCrushPolicy crushPolicy = MovementGymCrushPolicy.Respawn,
        CollisionAssistancePolicy? assistancePolicy = null)
    {
        // Separated walkable floors form a pit from x=6 to x=8.
        // Platforms and walls expose different collision response cases.
        _colliders = CreateColliders(platformMode);
        PlatformMode = platformMode;
        _movingPlatform = CreatePlatform(platformMode);
        _additionalMovingPlatforms = platformMode == MovingPlatformGymMode.Both
            ? [CreateSecondElevator()] : [];

        if (!Enum.IsDefined(crushPolicy))
            throw new ArgumentOutOfRangeException(nameof(crushPolicy));
        _crushPolicy = crushPolicy;
        _assistancePolicy = assistancePolicy ?? CollisionAssistancePolicy.Disabled;
        _faultPolicy = new SimulationFaultPolicy(faultSensitivity);
        Tuning = tuning ?? MovementGymTuning.Baseline;
        _groundMotion = HorizontalMotionSolver.Solve(Tuning.Ground);
        _airMotion = HorizontalMotionSolver.Solve(Tuning.Air);
        _jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));

        _motor = CreateMotor();
    }

    public MovementGymTuning Tuning { get; private set; }
    public MovingPlatformGymMode PlatformMode { get; private set; }

    /// <summary>
    /// Optional test-only legacy scenario selection. The playable gym starts
    /// with BOTH platforms and has no F8 mode toggle. This API retains the
    /// focused isolated-platform regression fixtures.
    /// </summary>
    public void SetPlatformMode(MovingPlatformGymMode mode)
    {
        IReadOnlyList<StaticAabbCollider> colliders = CreateColliders(mode);
        MovingPlatformTrajectory platform = CreatePlatform(mode);
        MovingPlatformTrajectory[] extras = mode == MovingPlatformGymMode.Both
            ? [CreateSecondElevator()] : [];
        CharacterMovementController newMotor = CreateMotor(
            _groundMotion, _airMotion, platform, mode, colliders, extras);
        _colliders = colliders;
        _movingPlatform = platform;
        _additionalMovingPlatforms = extras;
        PlatformMode = mode;
        _motor = newMotor;
        _jumpTiming.Reset();
        _consecutiveCheckpointFallbacks = 0;
    }
    public IReadOnlyList<StaticAabbCollider> Colliders => _colliders;
    public MovingPlatformSample MovingPlatform =>
        _movingPlatform.Evaluate(_motor.ElapsedSeconds);
    public IReadOnlyList<MovingPlatformSample> MovingPlatforms =>
        Array.AsReadOnly(new[] { _movingPlatform.Evaluate(_motor.ElapsedSeconds) }
            .Concat(_additionalMovingPlatforms.Select(p => p.Evaluate(_motor.ElapsedSeconds)))
            .ToArray());
    public bool IsRidingMovingPlatform => _motor.MovingSupportId is not null;
    public int? MovingSupportId => _motor.MovingSupportId;
    public SimulationFaultSensitivity FaultSensitivity => _motor.FaultSensitivity;
    public int LastStepRecoveryCount => _motor.LastStepRecoveries.Count;
    public int CheckpointFallbackCount { get; private set; }
    public int CrushRespawns { get; private set; }
    public CharacterCrushEvent? LastCrush { get; private set; }
    public MovementGymCrushPolicy CrushPolicy => _crushPolicy;
    public CollisionAssistancePolicy AssistancePolicy => _assistancePolicy;
    public int TotalAssists { get; private set; }
    public int LastStepAssistCount => _motor.LastStepAssists.Count;

    /// <summary>
    /// Switch gameplay assistance deterministically. Reset to a known spawn
    /// because an in-flight jump owns the policy captured at takeoff.
    /// Fault mode, tuning and platform geometry are preserved.
    /// </summary>
    public void SetAssistancePolicy(CollisionAssistancePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _assistancePolicy = policy;
        Reset();
    }

    public void SetCrushPolicy(MovementGymCrushPolicy policy)
    {
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        _crushPolicy = policy;
    }
    public MovementGymFaultReport? LastCheckpointFault { get; private set; }

    /// <summary>Changes diagnostics only; does not reset the active player.</summary>
    public void SetFaultSensitivity(SimulationFaultSensitivity sensitivity)
    {
        SimulationFaultPolicy policy = new(sensitivity);
        _motor.SetFaultPolicy(policy);
        _faultPolicy = policy;
    }

    /// <summary>
    /// Applies both profiles as one operation, then resets the character and
    /// input buffers. A failed validation leaves the live session unchanged.
    /// </summary>
    public void ApplyTuning(MovementGymTuning tuning)
    {
        HorizontalMotionProfile ground = HorizontalMotionSolver.Solve(tuning.Ground);
        HorizontalMotionProfile air = HorizontalMotionSolver.Solve(tuning.Air);
        CharacterMovementController motor = CreateMotor(ground, air);
        _groundMotion = ground;
        _airMotion = air;
        Tuning = tuning;
        _motor = motor;
        _jumpTiming.Reset();
    }

    public MovementGymDiagnostics Diagnostics
    {
        get
        {
            bool ground = _motor.IsGrounded;
            HorizontalMotionProfile profile = ground ? _groundMotion : _airMotion;
            double speed = Math.Abs(_motor.Velocity.X);
            double deceleration = profile.StoppingDecelerationRate;
            return new MovementGymDiagnostics(
                speed, profile.MaximumSpeed, deceleration,
                speed / deceleration,
                speed * speed / (2.0 * deceleration), ground);
        }
    }
    public Aabb PlayerBounds => _motor.Bounds;
    public Vector2D PlayerVelocity => _motor.Velocity;
    public bool IsGrounded => _motor.IsGrounded;
    public bool IsJumping => _motor.IsJumping;
    public bool IsJumpEarlyReleased => _motor.IsJumpEarlyReleased;
    public int JumpsStarted => _motor.JumpsStarted;
    public double ElapsedSeconds => _motor.ElapsedSeconds;
    public int PitRespawns { get; private set; }

    /// <summary>
    /// Typed gameplay request entry point. Pure priority arbitration occurs
    /// before the authoritative step; rejected requests never mutate game state.
    /// The MonoGame host calls this once per fixed update.
    /// </summary>
    public CharacterControlStepResult Step(
        double durationSeconds, IReadOnlyList<MovementRequest> requests) =>
        Step(durationSeconds, requests, out _);

    /// <summary>
    /// Report the *same* resolution used by this step after successful commit.
    /// A failed resolution or motor update does not produce a success report.
    /// </summary>
    public CharacterControlStepResult Step(
        double durationSeconds, IReadOnlyList<MovementRequest> requests,
        out MovementResolution resolution)
    {
        MovementResolution candidate = MovementRequestResolver.ResolveDetailed(requests);
        CharacterControlStepResult result = StepResolved(durationSeconds,
            candidate.Intent.Direction, candidate.Intent.JumpPressed,
            candidate.Intent.JumpReleased);
        resolution = candidate;
        return result;
    }

    /// <summary>
    /// Backward-compatible headless/replay entry point. Both paths use the same
    /// authoritative input buffering, collision, and transactional motor update.
    /// </summary>
    public CharacterControlStepResult Step(
        double durationSeconds, HorizontalDirection direction,
        bool jumpPressedThisStep, bool jumpReleasedThisStep = false) =>
        StepResolved(durationSeconds, direction,
            jumpPressedThisStep, jumpReleasedThisStep);

    private CharacterControlStepResult StepResolved(
        double durationSeconds, HorizontalDirection direction,
        bool jumpPressedThisStep, bool jumpReleasedThisStep)
    {
        // Keep zero-time reads observational (including input buffering).
        if (durationSeconds == 0.0)
            return _motor.Step(0.0, direction, false);
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));

        // Plan input without mutating the buffer. Commit only if simulation
        // successfully advances: a rejected collision step must not eat input.
        bool wasGrounded = _motor.IsGrounded;
        JumpInputPlan plan = _jumpTiming.Plan(
            _motor.ElapsedSeconds, wasGrounded, _motor.IsJumping,
            jumpPressedThisStep, jumpReleasedThisStep);
        bool checkpointRestarted = false;
        // Physical crushing is a gameplay outcome, independent of fault
        // sensitivity. Only the specifically classified event can respawn:
        // unknown errors and numerical faults keep their existing policies.
        CharacterControlStepResult result = MovementGymCrushHandler.Execute<CharacterControlStepResult>(
            () => MovementGymCheckpointFallback.Execute(
                () => _motor.Step(durationSeconds, direction, plan.StartJump,
                    jumpReleasedThisStep || plan.ReleaseOnStart,
                    jumpGraceAuthorized: plan.AuthorizeCoyote),
                fault =>
                {
                    // No partial movement or buffered input is committed.
                    _consecutiveCheckpointFallbacks++;
                    LastCheckpointFault = new MovementGymFaultReport(
                        _motor.ElapsedSeconds, _motor.Bounds, _motor.Velocity,
                        direction, jumpPressedThisStep, fault.GetType().Name,
                        fault.Message, _consecutiveCheckpointFallbacks);
                    CheckpointFallbackCount++;
                    _motor = CreateMotor();
                    _jumpTiming.Reset();
                    checkpointRestarted = true;
                    return _motor.Step(0.0, HorizontalDirection.Neutral, false);
                }, _faultPolicy.Sensitivity, _consecutiveCheckpointFallbacks),
            crush =>
            {
                CrushRespawns++;
                LastCrush = crush;
                Reset();
                checkpointRestarted = true;
                return _motor.Step(0.0, HorizontalDirection.Neutral, false);
            }, _crushPolicy);
        if (checkpointRestarted)
            return result;
        TotalAssists += result.Assists.Count;
        _consecutiveCheckpointFallbacks = 0;
        _jumpTiming.Commit(plan, _motor.ElapsedSeconds, wasGrounded,
            result.IsGrounded, result.JumpStarted);

        if (_motor.Bounds.Top < RespawnBelowY)
        {
            PitRespawns++;
            Reset();
            // Zero-duration reads return a snapshot without advancing time.
            return _motor.Step(0.0, HorizontalDirection.Neutral, false);
        }

        return result;
    }

    public void Reset()
    {
        _motor = CreateMotor();
        _jumpTiming.Reset();
        _consecutiveCheckpointFallbacks = 0;
    }

    private CharacterMovementController CreateMotor() =>
        CreateMotor(_groundMotion, _airMotion);

    private CharacterMovementController CreateMotor(
        HorizontalMotionProfile ground, HorizontalMotionProfile air) =>
        CreateMotor(ground, air, _movingPlatform, PlatformMode);

    private CharacterMovementController CreateMotor(
        HorizontalMotionProfile ground, HorizontalMotionProfile air,
        MovingPlatformTrajectory platform, MovingPlatformGymMode mode,
        IReadOnlyList<StaticAabbCollider>? colliders = null,
        MovingPlatformTrajectory[]? extras = null) => new(
        mode == MovingPlatformGymMode.Elevator ? ElevatorSpawnBounds : SpawnBounds,
        colliders ?? _colliders, ground, air, _jump,
        gravityAcceleration: -24.0,
        releaseConstraints: _jumpRelease,
        movingPlatform: platform,
        faultPolicy: _faultPolicy,
        additionalMovingPlatforms: extras ?? _additionalMovingPlatforms,
        assistancePolicy: _assistancePolicy);

    private static MovingPlatformTrajectory CreatePlatform(MovingPlatformGymMode mode) =>
        mode switch
        {
            // Preserve Milestone 23's working horizontal moving floor.
            MovingPlatformGymMode.Horizontal or MovingPlatformGymMode.Both => new MovingPlatformTrajectory(
                7, Box(5.3, 2.05, 7.35, 2.30),
                new Vector2D(2.4, 0.0), 2.4),
            // Vertical elevator: starting top at y=0, rising 3 units in 2 s.
            // The full path stays inside the pit, clear of fixed geometry.
            MovingPlatformGymMode.Elevator => new MovingPlatformTrajectory(
                7, Box(6.2, -0.35, 7.7, 0.0),
                new Vector2D(0.0, 3.0), 2.0),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

    // The second elevator is on the LEFT of spawn. Separate pits prevent the
    // horizontal platform and the lift from intersecting one another.
    private static MovingPlatformTrajectory CreateSecondElevator() => new(
        8, Box(-7.7, -0.35, -6.2, 0.0), new Vector2D(0.0, 3.0), 2.0);

    private static IReadOnlyList<StaticAabbCollider> CreateColliders(
        MovingPlatformGymMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        var obstacles = new List<StaticAabbCollider>
        {
            new(1, Box(-35.0, -1.0,
                mode == MovingPlatformGymMode.Both ? -8.4 : 6.0, 0.0)),
            new(2, Box(8.0, -1.0, 40.0, 0.0)),
            new(3, Box(11.0, 2.2, 16.0, 2.5)),
            new(4, Box(22.0, 0.0, 22.8, 3.4)),
            new(5, Box(-12.0, 0.0, -10.0, 1.25)),
            new(6, Box(-5.0, 3.0, -2.5, 3.35))
        };
        if (mode == MovingPlatformGymMode.Both)
        {
            // Leave a 1.00-unit corridor between lift 8's right edge
            // (x=-6.20) and this bank (x=-5.20): player width is 0.80.
            // The old 0.80 corridor could not represent two simultaneous
            // exact wall contacts after normal binary64 rounding.
            obstacles.Add(new StaticAabbCollider(9,
                Box(-5.2, -1.0, 6.0, 0.0)));
            // Solid floor below the left lift (ID 8), for descending-crush tests.
            // The lift's lowest underside is y=-0.35; the floor top is y=-1.0.
            // Both surfaces remain distinct, with room for the player to enter
            // the pit from either side when the lift is raised.
            obstacles.Add(new StaticAabbCollider(10,
                Box(-8.4, -1.5, -5.2, -1.0)));

            // Horizontal-crush fixture on the RIGHT bank, beyond the pit.
            // The moving platform (ID 7) has side faces from y=2.05 to 2.30,
            // so ordinary ground-level players (top y=1.60) cannot be pushed.
            // This small raised floor places the player's side in its path.
            // Raise the pad to 1.00 so the standing player's head is at 2.60,
            // not exactly aligned with the platform's top (2.30). This
            // avoids a nonessential simultaneous top/side corner contact.
            obstacles.Add(new StaticAabbCollider(11,
                Box(8.2, 0.0, 10.35, 1.00)));
            // The platform's furthest-right face is x=9.75. A player is 0.8
            // units wide: the 0.60-unit minimum gap to this wall makes a real
            // horizontal pinch without the platform ever touching the wall.
            obstacles.Add(new StaticAabbCollider(12,
                Box(10.35, 0.0, 10.75, 3.60)));

            // Left-facing horizontal crush station. The player can jump over
            // wall 14 from the starting side, land on pad 13, and wait as
            // platform 7 returns LEFT. Its leftmost face is x=5.30; wall 14
            // ends at x=4.70, leaving a 0.60-unit gap for a 0.80-unit body.
            // Pad top y=1.00 creates vertical overlap with the moving side
            // (y=2.05..2.30), but never intersects the moving platform itself.
            // The wall is lower than the right wall for easier gym traversal.
            obstacles.Add(new StaticAabbCollider(13,
                Box(4.70, 0.0, 6.40, 1.00)));
            obstacles.Add(new StaticAabbCollider(14,
                Box(4.30, 0.0, 4.70, 2.80)));

            // Rising-elevator ceiling fixture: elevator 8 reaches top y=3.00,
            // but the ceiling underside is y=4.30. At the upper turnaround,
            // the 1.30-unit opening is narrower than the 1.60-unit player.
            // The elevator itself never touches the ceiling. Keep floor 10
            // for the existing descending-elevator crush scenario.
            obstacles.Add(new StaticAabbCollider(15,
                Box(-8.40, 4.30, -5.40, 4.75)));

            // Ground STEP-ASSIST station, immediately LEFT of player spawn.
            // At 0.10 units this is lower than F9's 0.12-unit step budget.
            // Disabled: moving left stops at x=-1.20. Enabled: the motor
            // climbs to y=0.10 with an explicit GroundStep assistance event.
            // Keep it in Both only, preserving the isolated older gyms.
            obstacles.Add(new StaticAabbCollider(GroundStepBumpColliderId,
                Box(-2.40, 0.0, -1.20, GroundStepBumpHeight)));
        }
        return obstacles.AsReadOnly();
    }

    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));
}
